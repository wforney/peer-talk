namespace PeerTalk.Routing
{
	using Ipfs;
	using Microsoft.Extensions.Logging;
	using ProtoBuf;
	using SharedCode.Notifications;
	using System;
	using System.Collections.Concurrent;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	/// A query that is sent to multiple peers.
	/// </summary>
	/// <typeparam name="T">The type of answer returned by a peer.</typeparam>
	public class DistributedQuery<T> where T : class
	{
		/// <summary>
		/// The maximum number of peers that can be queried at one time for all distributed queries.
		/// </summary>
		private static readonly SemaphoreSlim askCount = new SemaphoreSlim(128);

		/// <summary>
		/// The maximum time spent on waiting for an answer from a peer.
		/// </summary>
		private static readonly TimeSpan askTime = TimeSpan.FromSeconds(10);

		private static int nextQueryId = 1;
		private readonly ILogger<DistributedQuery<T>> _logger;
		private readonly INotificationService _notificationService;
		private readonly ConcurrentDictionary<T, T> answers = new ConcurrentDictionary<T, T>();
		private readonly ConcurrentDictionary<Peer, Peer> visited = new ConcurrentDictionary<Peer, Peer>();
		private int failedConnects = 0;
		private DhtMessage queryMessage;

		/// <summary>
		/// Controls the running of the distributed query.
		/// </summary>
		/// <remarks>
		/// Becomes cancelled when the correct number of answers are found or the caller of <see
		/// cref="RunAsync" /> wants to cancel or the DHT is stopped.
		/// </remarks>
		private CancellationTokenSource runningQuery;

		/// <summary>
		/// Initializes a new instance of the <see cref="DistributedQuery{T}" /> class.
		/// </summary>
		/// <param name="logger">The logger.</param>
		/// <param name="notificationService">The notification service.</param>
		/// <exception cref="ArgumentNullException">logger</exception>
		/// <exception cref="ArgumentNullException">notificationService</exception>
		public DistributedQuery(ILogger<DistributedQuery<T>> logger, INotificationService notificationService)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
		}

		/// <summary>
		/// The received answers for the query.
		/// </summary>
		public IEnumerable<T> Answers => answers.Values;

		/// <summary>
		/// The number of answers needed.
		/// </summary>
		/// <remarks>
		/// When the numbers <see cref="Answers" /> reaches this limit the <see
		/// cref="RunAsync">running query</see> will stop.
		/// </remarks>
		public int AnswersNeeded { get; set; } = 1;

		/// <summary>
		/// The maximum number of concurrent peer queries to perform for one distributed query.
		/// </summary>
		/// <value>The default is 16.</value>
		/// <remarks>The number of peers that are asked for the answer.</remarks>
		public int ConcurrencyLevel { get; set; } = 16;

		/// <summary>
		/// The distributed hash table.
		/// </summary>
		public Dht1 Dht { get; set; }

		/// <summary>
		/// The unique identifier of the query.
		/// </summary>
		public int Id { get; } = nextQueryId++;

		/// <summary>
		/// The key to find.
		/// </summary>
		public MultiHash QueryKey { get; set; }

		/// <summary>
		/// The type of query to perform.
		/// </summary>
		public MessageType QueryType { get; set; }

		/// <summary>
		/// Add a answer to the query.
		/// </summary>
		/// <param name="answer">An answer.</param>
		public void AddAnswer(T answer)
		{
			if (answer is null)
			{
				return;
			}

			if (!(runningQuery is null) && runningQuery.IsCancellationRequested)
			{
				return;
			}

			if (answers.TryAdd(answer, answer))
			{
				if (answers.Count >= AnswersNeeded && !(runningQuery is null) && !runningQuery.IsCancellationRequested)
				{
					runningQuery.Cancel(false);
				}
			}

			_notificationService.Publish(new AnswerObtained(answer));
		}

		/// <summary>
		/// Starts the distributed query.
		/// </summary>
		/// <param name="cancel">
		/// Is used to stop the task. When cancelled, the <see cref="TaskCanceledException" /> is raised.
		/// </param>
		/// <returns>A task that represents the asynchronous operation.</returns>
		public async Task RunAsync(CancellationToken cancel)
		{
			_logger.LogDebug("Q{Id} run {QueryType} {QueryKey}", Id, QueryType, QueryKey);

			runningQuery = CancellationTokenSource.CreateLinkedTokenSource(cancel);
			var dhtStoppedSubscription = _notificationService.Subscribe<Dht1.Stopped>(m => OnDhtStopped());
			queryMessage = new DhtMessage
			{
				Type = QueryType,
				Key = QueryKey?.ToArray(),
			};

			var tasks = Enumerable
				.Range(1, ConcurrencyLevel)
				.Select(i => { var id = i; return AskAsync(id); });
			try
			{
				await Task.WhenAll(tasks).ConfigureAwait(false);
			}
			catch (Exception)
			{
				// eat it
			}
			finally
			{
				dhtStoppedSubscription?.Dispose();
				dhtStoppedSubscription = null;
			}

			_logger.LogDebug("Q{Id} found {AnswersCount} answers, visited {VisitedCount} peers, failed {FailedConnects}", Id, answers.Count, visited.Count, failedConnects);
		}

		/// <summary>
		/// Ask the next peer the question.
		/// </summary>
		private async Task AskAsync(int taskId)
		{
			int pass = 0;
			int waits = 20;
			while (!runningQuery.IsCancellationRequested && waits > 0)
			{
				// Get the nearest peer that has not been visited.
				var peer = Dht.RoutingTable
					.NearestPeers(QueryKey)
					.Where(p => !visited.ContainsKey(p))
					.FirstOrDefault();
				if (peer is null)
				{
					--waits;
					await Task.Delay(100);
					continue;
				}

				if (!visited.TryAdd(peer, peer))
				{
					continue;
				}

				++pass;

				// Ask the nearest peer.
				await askCount.WaitAsync(runningQuery.Token).ConfigureAwait(false);
				var start = DateTime.Now;
				_logger.LogDebug("Q{Id}.{TaskId}.{Pass} ask {Peer}", Id, taskId, pass, peer);
				try
				{
					using (var timeout = new CancellationTokenSource(askTime))
					using (var cts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, runningQuery.Token))
					using (var stream = await Dht.Swarm.DialAsync(peer, Dht.ToString(), cts.Token).ConfigureAwait(false))
					{
						// Send the KAD query and get a response.
						ProtoBuf.Serializer.SerializeWithLengthPrefix(stream, queryMessage, PrefixStyle.Base128);
						await stream.FlushAsync(cts.Token).ConfigureAwait(false);
						var response = await ProtoBufHelper.ReadMessageAsync<DhtMessage>(stream, cts.Token).ConfigureAwait(false);

						// Process answer
						ProcessProviders(response.ProviderPeers);
						ProcessCloserPeers(response.CloserPeers);
					}

					var time = DateTime.Now - start;
					_logger.LogDebug("Q{Id}.{TaskId}.{Pass} ok {Peer} ({TimeTotalMilliseconds} ms)", Id, taskId, pass, peer, time.TotalMilliseconds);
				}
				catch (Exception e)
				{
					_ = Interlocked.Increment(ref failedConnects);
					var time = DateTime.Now - start;
					_logger.LogWarning(e, "Q{Id}.{TaskId}.{Pass} failed ({TimeTotalMilliseconds} ms)", Id, taskId, pass, time.TotalMilliseconds);
					// eat it
				}
				finally
				{
					_ = askCount.Release();
				}
			}
		}

		private void OnDhtStopped()
		{
			_logger.LogDebug("Q{Id} cancelled because DHT stopped.", Id);
			runningQuery.Cancel();
		}

		private void ProcessCloserPeers(DhtPeerMessage[] closerPeers)
		{
			if (closerPeers is null)
			{
				return;
			}

			foreach (var closer in closerPeers)
			{
				if (closer.TryToPeer(out Peer p))
				{
					if (p == Dht.Swarm.LocalPeer || !Dht.Swarm.IsAllowed(p))
					{
						continue;
					}

					p = Dht.Swarm.RegisterPeer(p);
					if (QueryType == MessageType.FindNode && QueryKey == p.Id)
					{
						AddAnswer(p as T);
					}
				}
			}
		}

		private void ProcessProviders(DhtPeerMessage[] providers)
		{
			if (providers is null)
			{
				return;
			}

			foreach (var provider in providers)
			{
				if (provider.TryToPeer(out Peer p))
				{
					if (p == Dht.Swarm.LocalPeer || !Dht.Swarm.IsAllowed(p))
					{
						continue;
					}

					p = Dht.Swarm.RegisterPeer(p);
					if (QueryType == MessageType.GetProviders)
					{
						// Only unique answers
						var answer = p as T;
						if (!answers.ContainsKey(answer))
						{
							AddAnswer(answer);
						}
					}
				}
			}
		}

		/// <summary>
		/// Raised when an answer is obtained.
		/// </summary>
		public class AnswerObtained : Notification
		{
			/// <summary>
			/// Initializes a new instance of the <see cref="AnswerObtained" /> class.
			/// </summary>
			/// <param name="value">The value.</param>
			public AnswerObtained(T value) => this.Value = value;

			/// <summary>
			/// Gets the value.
			/// </summary>
			/// <value>The value.</value>
			public T Value { get; }
		}
	}
}
