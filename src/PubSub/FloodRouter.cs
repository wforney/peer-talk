namespace PeerTalk.PubSub
{
	using Ipfs;
	using Microsoft.Extensions.Logging;
	using PeerTalk.Protocols;
	using ProtoBuf;
	using Semver;
	using SharedCode.Notifications;
	using System;
	using System.Collections.Concurrent;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	/// The original flood sub router.
	/// </summary>
	public class FloodRouter : IPeerProtocol, IMessageRouter
	{
		private readonly ILogger<FloodRouter> _logger;
		private readonly INotificationService _notificationService;
		private readonly ConcurrentDictionary<string, string> localTopics = new ConcurrentDictionary<string, string>();
		private readonly MessageTracker tracker = new MessageTracker();

		private bool _started;
		private IDisposable _swarmConnectionEstablished;
		private IDisposable _swarmPeerDisconnected;

		/// <summary>
		/// Initializes a new instance of the <see cref="FloodRouter"/> class.
		/// </summary>
		/// <param name="logger">The logger.</param>
		/// <param name="notificationService">The notification service.</param>
		/// <exception cref="ArgumentNullException">logger</exception>
		/// <exception cref="ArgumentNullException">notificationService</exception>
		public FloodRouter(ILogger<FloodRouter> logger, INotificationService notificationService)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
		}

		/// <inheritdoc />
		public string Name { get; } = "floodsub";

		/// <summary>
		/// The topics of interest of other peers.
		/// </summary>
		public TopicManager RemoteTopics { get; set; } = new TopicManager();

		/// <summary>
		/// Provides access to other peers.
		/// </summary>
		public Swarm Swarm { get; set; }

		/// <inheritdoc />
		public SemVersion Version { get; } = new SemVersion(1, 0);

		/// <inheritdoc />
		public IEnumerable<Peer> InterestedPeers(string topic) => RemoteTopics.GetPeers(topic);

		/// <inheritdoc />
		public async Task JoinTopicAsync(string topic, CancellationToken cancel)
		{
			_ = localTopics.TryAdd(topic, topic);
			var msg = new PubSubMessage
			{
				Subscriptions = new Subscription[]
				{
					new Subscription
					{
						Topic = topic,
						Subscribe = true
					}
				}
			};
			try
			{
				var peers = Swarm.KnownPeers.Where(p => p.ConnectedAddress != null);
				await SendAsync(msg, peers, cancel).ConfigureAwait(false);
			}
			catch (Exception e)
			{
				_logger.LogWarning(e, "Join topic failed.");
			}
		}

		/// <inheritdoc />
		public async Task LeaveTopicAsync(string topic, CancellationToken cancel)
		{
			_ = localTopics.TryRemove(topic, out _);
			var msg = new PubSubMessage
			{
				Subscriptions = new Subscription[]
				{
					new Subscription
					{
						Topic = topic,
						Subscribe = false
					}
				}
			};
			try
			{
				var peers = Swarm.KnownPeers.Where(p => p.ConnectedAddress != null);
				await SendAsync(msg, peers, cancel).ConfigureAwait(false);
			}
			catch (Exception e)
			{
				_logger.LogWarning(e, "Leave topic failed.");
			}
		}

		/// <inheritdoc />
		public async Task ProcessMessageAsync(PeerConnection connection, Stream stream, CancellationToken cancel = default)
		{
			while (true)
			{
				var request = await ProtoBufHelper.ReadMessageAsync<PubSubMessage>(stream, cancel).ConfigureAwait(false); ;
				_logger.LogDebug("got message from {ConnectionRemotePeer}", connection.RemotePeer);

				if (request.Subscriptions != null)
				{
					foreach (var sub in request.Subscriptions)
					{
						ProcessSubscription(sub, connection.RemotePeer);
					}
				}

				if (request.PublishedMessages != null)
				{
					foreach (var msg in request.PublishedMessages)
					{
						_logger.LogDebug("Message for '{Topics}' fowarded by {ConnectionRemotePeer}", string.Join(", ", msg.Topics), connection.RemotePeer);
						msg.Forwarder = connection.RemotePeer;
						_notificationService.Publish(new MessageReceived(this, msg));
						await PublishAsync(msg, cancel).ConfigureAwait(false);
					}
				}
			}
		}

		/// <summary>
		/// Process a subscription request from another peer.
		/// </summary>
		/// <param name="sub">The subscription request.</param>
		/// <param name="remote">The remote <see cref="Peer" />.</param>
		/// <seealso cref="RemoteTopics" />
		/// <remarks>Maintains the <see cref="RemoteTopics" />.</remarks>
		public void ProcessSubscription(Subscription sub, Peer remote)
		{
			if (sub.Subscribe)
			{
				_logger.LogDebug("Subscribe '{SubTopic}' by {Remote}", sub.Topic, remote);
				RemoteTopics.AddInterest(sub.Topic, remote);
			}
			else
			{
				_logger.LogDebug("Unsubscribe '{SubTopic}' by {Remote}", sub.Topic, remote);
				RemoteTopics.RemoveInterest(sub.Topic, remote);
			}
		}

		/// <inheritdoc />
		public Task PublishAsync(PublishedMessage message, CancellationToken cancel)
		{
			if (tracker.RecentlySeen(message.MessageId))
			{
				return Task.CompletedTask;
			}

			// Find a set of peers that are interested in the topic(s). Exclude author and sender
			var peers = message.Topics
				.SelectMany(topic => RemoteTopics.GetPeers(topic))
				.Where(peer => peer != message.Sender)
				.Where(peer => peer != message.Forwarder);

			// Forward the message.
			var forward = new PubSubMessage
			{
				PublishedMessages = new PublishedMessage[] { message }
			};

			return SendAsync(forward, peers, cancel);
		}

		/// <inheritdoc />
		public Task StartAsync()
		{
			if (_started)
			{
				return Task.CompletedTask;
			}

			_logger.LogDebug("Starting");

			_started = true;
			Swarm.AddProtocol(this);
			_swarmConnectionEstablished = _notificationService.Subscribe<Swarm.ConnectionEstablished>(m => SwarmConnectionEstablishedAsync(m.PeerConnection));
			_swarmPeerDisconnected = _notificationService.Subscribe<Swarm.PeerDisconnected>(m => SwarmPeerDisconnected(m.Peer));

			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public Task StopAsync()
		{
			_logger.LogDebug("Stopping");

			_swarmConnectionEstablished?.Dispose();
			_swarmConnectionEstablished = null;
			_swarmPeerDisconnected?.Dispose();
			_swarmPeerDisconnected = null;

			Swarm.RemoveProtocol(this);
			RemoteTopics.Clear();
			localTopics.Clear();

			_started = false;
			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public override string ToString() => $"/{Name}/{Version}";

		private Task SendAsync(PubSubMessage msg, IEnumerable<Peer> peers, CancellationToken cancel)
		{
			// Get binary representation
			byte[] bin;
			using (var ms = new MemoryStream())
			{
				Serializer.SerializeWithLengthPrefix(ms, msg, PrefixStyle.Base128);
				bin = ms.ToArray();
			}

			return Task.WhenAll(peers.Select(p => SendAsync(bin, p, cancel)));
		}

		private async Task SendAsync(byte[] message, Peer peer, CancellationToken cancel)
		{
			try
			{
				using (var stream = await Swarm.DialAsync(peer, this.ToString(), cancel).ConfigureAwait(false))
				{
					await stream.WriteAsync(message, 0, message.Length, cancel).ConfigureAwait(false);
					await stream.FlushAsync(cancel).ConfigureAwait(false);
				}

				_logger.LogDebug("sending message to {Peer}", peer);
			}
			catch (Exception e)
			{
				_logger.LogDebug(e, "{Peer} refused pubsub message.", peer);
			}
		}

		/// <summary>
		/// Raised when a connection is established to a remote peer.
		/// </summary>
		/// <param name="connection">The peer connection.</param>
		/// <remarks>
		/// Sends the hello message to the remote peer. The message contains all topics that are of
		/// interest to the local peer.
		/// </remarks>
		private async Task SwarmConnectionEstablishedAsync(PeerConnection connection)
		{
			if (localTopics.Count == 0)
			{
				return;
			}

			try
			{
				var hello = new PubSubMessage
				{
					Subscriptions = localTopics.Values
						.Select(topic => new Subscription
						{
							Subscribe = true,
							Topic = topic
						})
						.ToArray()
				};
				await SendAsync(hello, new Peer[] { connection.RemotePeer }, CancellationToken.None).ConfigureAwait(false);
			}
			catch (Exception e)
			{
				_logger.LogWarning(e, "Sending hello message failed");
			}
		}

		/// <summary>
		/// Raised when the peer has no more connections.
		/// </summary>
		/// <param name="peer">The peer.</param>
		/// <remarks>Removes the <paramref name="peer" /> from the <see cref="RemoteTopics" />.</remarks>
		private void SwarmPeerDisconnected(Peer peer) => RemoteTopics.Clear(peer);
	}
}
