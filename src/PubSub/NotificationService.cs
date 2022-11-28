namespace PeerTalk.PubSub
{
	using Ipfs;
	using Ipfs.CoreApi;
	using Microsoft.Extensions.Logging;
	using SharedCode.Notifications;
	using System;
	using System.Collections.Concurrent;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Text;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	///   A simple pub/sub messaging service that supports
	///   multiple message routers.
	/// </summary>
	/// <remarks>
	///   Relies upon the router(s) to deliver and receive messages from other peers.
	/// </remarks>
	public partial class NotificationService : IService, IPubSubApi
	{

		private long nextSequenceNumber;
		private ConcurrentDictionary<TopicHandler, TopicHandler> topicHandlers;
		private readonly MessageTracker tracker = new MessageTracker();
		private CancellationTokenSource cancellationTokenSource;

		/// <summary>
		/// Initializes a new instance of the <see cref="NotificationService"/> class.
		/// </summary>
		/// <param name="logger">The logger.</param>
		/// <param name="notificationService">The notification service.</param>
		/// <exception cref="ArgumentNullException">logger</exception>
		/// <exception cref="ArgumentNullException">notificationService</exception>
		public NotificationService(ILogger<NotificationService> logger, INotificationService notificationService)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
			Routers = new List<IMessageRouter>
			{
				new LoopbackRouter(_notificationService)
			};
		}

		/// <summary>
		///   The local peer.
		/// </summary>
		public Peer LocalPeer { get; set; }

		/// <summary>
		///   Sends and receives messages to other peers.
		/// </summary>
		public List<IMessageRouter> Routers { get; set; }

		/// <summary>
		///   The number of messages that have published.
		/// </summary>
		public ulong MesssagesPublished;

		/// <summary>
		///   The number of messages that have been received.
		/// </summary>
		public ulong MesssagesReceived;

		/// <summary>
		///   The number of duplicate messages that have been received.
		/// </summary>
		public ulong DuplicateMesssagesReceived;

		private readonly List<IDisposable> _subscriptions = new List<IDisposable>();
		private readonly ILogger<NotificationService> _logger;
		private readonly INotificationService _notificationService;

		/// <inheritdoc />
		public async Task StartAsync()
		{
			cancellationTokenSource?.Cancel();
			cancellationTokenSource = new CancellationTokenSource();

			topicHandlers = new ConcurrentDictionary<TopicHandler, TopicHandler>();

			// Resolution of 100 nanoseconds.
			nextSequenceNumber = DateTime.UtcNow.Ticks;

			// Init the stats.
			MesssagesPublished = 0;
			MesssagesReceived = 0;
			DuplicateMesssagesReceived = 0;

			// Listen to the routers.
			foreach (var router in Routers)
			{
				_subscriptions.Add(_notificationService.Subscribe<MessageReceived>(m => RouterMessageReceivedAsync(m.MessageRouter, m.PublishedMessage)));
				await router.StartAsync().ConfigureAwait(false);
			}
		}

		/// <inheritdoc />
		public async Task StopAsync()
		{
			cancellationTokenSource?.Cancel();
			cancellationTokenSource = null;

			topicHandlers.Clear();

			foreach (var subscription in _subscriptions)
			{
				subscription?.Dispose();
			}

			_subscriptions.Clear();

			foreach (var router in Routers)
			{
				await router.StopAsync();
			}
		}

		/// <summary>
		///   Creates a message for the topic and data.
		/// </summary>
		/// <param name="topic">
		///   The topic name/id.
		/// </param>
		/// <param name="data">
		///   The payload of message.
		/// </param>
		/// <returns>
		///   A unique published message.
		/// </returns>
		/// <remarks>
		///   The <see cref="PublishedMessage.SequenceNumber"/> is a monitonically
		///   increasing unsigned long.
		/// </remarks>
		public PublishedMessage CreateMessage(string topic, byte[] data)
		{
			var next = Interlocked.Increment(ref nextSequenceNumber);
			var seqno = BitConverter.GetBytes(next);
			if (BitConverter.IsLittleEndian)
			{
				seqno = seqno.Reverse().ToArray();
			}

			return new PublishedMessage
			{
				Topics = new string[] { topic },
				Sender = LocalPeer,
				SequenceNumber = seqno,
				DataBytes = data
			};
		}

		/// <inheritdoc />
		public Task<IEnumerable<string>> SubscribedTopicsAsync(CancellationToken cancel = default)
		{
			cancellationTokenSource?.Token.ThrowIfCancellationRequested();

			var topics = topicHandlers.Values
				.Select(t => t.Topic)
				.Distinct();
			return Task.FromResult(topics);
		}

		/// <inheritdoc />
		public Task<IEnumerable<Peer>> PeersAsync(string topic = null, CancellationToken cancel = default)
		{
			cancellationTokenSource?.Token.ThrowIfCancellationRequested();

			var peers = Routers
				.SelectMany(r => r.InterestedPeers(topic))
				.Distinct();
			return Task.FromResult(peers);
		}

		/// <inheritdoc />
		public Task PublishAsync(string topic, string message, CancellationToken cancel = default) =>
			PublishAsync(topic, Encoding.UTF8.GetBytes(message), cancel);

		/// <inheritdoc />
		public async Task PublishAsync(string topic, Stream message, CancellationToken cancel = default)
		{
			cancellationTokenSource?.Token.ThrowIfCancellationRequested();

			using (var ms = new MemoryStream())
			{
				await message.CopyToAsync(ms);
				await PublishAsync(topic, ms.ToArray(), cancel);
			}
		}

		/// <inheritdoc />
		public async Task PublishAsync(string topic, byte[] message, CancellationToken cancel = default)
		{
			cancellationTokenSource?.Token.ThrowIfCancellationRequested();

			var msg = CreateMessage(topic, message);
			++MesssagesPublished;
			await Task.WhenAll(Routers.Select(r => r.PublishAsync(msg, cancel)));
		}

		/// <inheritdoc />
		public async Task SubscribeAsync(string topic, Action<IPublishedMessage> handler, CancellationToken cancellationToken)
		{
			cancellationTokenSource?.Token.ThrowIfCancellationRequested();

			var topicHandler = new TopicHandler { Topic = topic, Handler = handler };
			_ = topicHandlers.TryAdd(topicHandler, topicHandler);

			// TODO: need a better way.
#pragma warning disable VSTHRD101
			_ = cancellationToken.Register(async () =>
			{
				_ = topicHandlers.TryRemove(topicHandler, out _);
				if (topicHandlers.Values.Count(t => t.Topic == topic) == 0)
				{
					await Task.WhenAll(Routers.Select(r => r.LeaveTopicAsync(topic, CancellationToken.None))).ConfigureAwait(false);
				}
			});
#pragma warning restore VSTHRD101

			// Tell routers if first time.
			if (topicHandlers.Values.Count(t => t.Topic == topic) == 1)
			{
				await Task.WhenAll(Routers.Select(r => r.JoinTopicAsync(topic, CancellationToken.None))).ConfigureAwait(false);
			}
		}

		/// <summary>
		///   Invoked when a router gets a message.
		/// </summary>
		/// <param name="messageRouter">The message router.</param>
		/// <param name="msg">
		///   The message.
		/// </param>
		/// <remarks>
		///   Invokes any topic handlers and publishes the messages on the other routers.
		/// </remarks>
		private async Task RouterMessageReceivedAsync(IMessageRouter messageRouter, PublishedMessage msg)
		{
			cancellationTokenSource?.Token.ThrowIfCancellationRequested();

			++MesssagesReceived;

			// Check for duplicate message.
			if (tracker.RecentlySeen(msg.MessageId))
			{
				++DuplicateMesssagesReceived;
				return;
			}

			// Call local topic handlers.
			var handlers = topicHandlers.Values
				.Where(th => msg.Topics.Contains(th.Topic));
			foreach (var handler in handlers)
			{
				try
				{
					handler.Handler(msg);
				}
				catch (Exception e)
				{
					_logger.LogError(e, "Topic handler for '{HandlerTopic}' failed.", handler.Topic);
				}
			}

			// Tell other message routers.
			await Task.WhenAll(
				Routers
					.Where(r => r != messageRouter)
					.Select(r => r.PublishAsync(msg, CancellationToken.None)));
		}
	}
}
