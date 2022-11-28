namespace PeerTalk.Routing
{
	using Ipfs;
	using Ipfs.CoreApi;
	using Microsoft.Extensions.Logging;
	using PeerTalk.Protocols;
	using ProtoBuf;
	using Semver;
	using SharedCode.Notifications;
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	///   DHT Protocol version 1.0
	/// </summary>
	public class Dht1 : IPeerProtocol, IService, IPeerRouting, IContentRouting
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="Dht1"/> class.
		/// </summary>
		/// <param name="loggerFactory">The logger factory.</param>
		/// <param name="notificationService">The notification service.</param>
		/// <exception cref="ArgumentNullException">loggerFactory</exception>
		/// <exception cref="ArgumentNullException">notificationService</exception>
		public Dht1(ILoggerFactory loggerFactory, INotificationService notificationService)
		{
			_loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
			_logger = loggerFactory.CreateLogger<Dht1>() ?? throw new ArgumentNullException(nameof(loggerFactory));
			_notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
		}
		/// <inheritdoc />
		public string Name { get; } = "ipfs/kad";

		/// <inheritdoc />
		public SemVersion Version { get; } = new SemVersion(1, 0);

		/// <summary>
		///   Provides access to other peers.
		/// </summary>
		public Swarm Swarm { get; set; }

		/// <summary>
		///  Routing information on peers.
		/// </summary>
		public RoutingTable RoutingTable;

		/// <summary>
		///   Peers that can provide some content.
		/// </summary>
		public ContentRouter ContentRouter;
		private readonly ILoggerFactory _loggerFactory;
		private readonly ILogger<Dht1> _logger;
		private readonly INotificationService _notificationService;

		/// <summary>
		///   The number of closer peers to return.
		/// </summary>
		/// <value>
		///   Defaults to 20.
		/// </value>
		public int CloserPeerCount { get; set; } = 20;

		/// <summary>
		///   Raised when the DHT is stopped.
		/// </summary>
		/// <seealso cref="StopAsync"/>
		public class Stopped : Notification { }

		/// <inheritdoc />
		public override string ToString() => $"/{Name}/{Version}";

		/// <inheritdoc />
		public async Task ProcessMessageAsync(PeerConnection connection, Stream stream, CancellationToken cancel = default)
		{
			while (true)
			{
				var request = await ProtoBufHelper.ReadMessageAsync<DhtMessage>(stream, cancel).ConfigureAwait(false);

				_logger.LogDebug("got {RequestType} from {ConnectionRemotePeer}", request.Type, connection.RemotePeer);
				var response = new DhtMessage
				{
					Type = request.Type,
					ClusterLevelRaw = request.ClusterLevelRaw
				};
				switch (request.Type)
				{
					case MessageType.Ping:
						response = ProcessPing(request, response);
						break;
					case MessageType.FindNode:
						response = ProcessFindNode(request, response);
						break;
					case MessageType.GetProviders:
						response = ProcessGetProviders(request, response);
						break;
					case MessageType.AddProvider:
						response = ProcessAddProvider(connection.RemotePeer, request, response);
						break;
					default:
						_logger.LogDebug("unknown {RequestType} from {ConnectionRemotePeer}", request.Type, connection.RemotePeer);
						// TODO: Should we close the stream?
						continue;
				}

				if (!(response is null))
				{
					ProtoBuf.Serializer.SerializeWithLengthPrefix(stream, response, PrefixStyle.Base128);
					await stream.FlushAsync(cancel).ConfigureAwait(false);
				}
			}
		}

		private IDisposable _swarmPeerDiscovered;
		private IDisposable _swarmPeerRemoved;

		/// <inheritdoc />
		public Task StartAsync()
		{
			_logger.LogDebug("Starting");

			RoutingTable = new RoutingTable(Swarm.LocalPeer);
			ContentRouter = new ContentRouter();
			Swarm.AddProtocol(this);
			_swarmPeerDiscovered = _notificationService.Subscribe<Swarm.PeerDiscovered>(m => Swarm_PeerDiscovered(m.Peer));
			_swarmPeerRemoved = _notificationService.Subscribe<Swarm.PeerRemoved>(m => Swarm_PeerRemoved(m.Peer));
			foreach (var peer in Swarm.KnownPeers)
			{
				RoutingTable.Add(peer);
			}

			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public Task StopAsync()
		{
			_logger.LogDebug("Stopping");

			Swarm.RemoveProtocol(this);
			_swarmPeerDiscovered?.Dispose();
			_swarmPeerRemoved?.Dispose();

			_notificationService.Publish(new Stopped());
			ContentRouter?.Dispose();
			return Task.CompletedTask;
		}

		/// <summary>
		///   The swarm has discovered a new peer, update the routing table.
		/// </summary>
		private void Swarm_PeerDiscovered(Peer e) => RoutingTable.Add(e);

		/// <summary>
		///   The swarm has removed a peer, update the routing table.
		/// </summary>
		private void Swarm_PeerRemoved(Peer e) => RoutingTable.Remove(e);

		/// <inheritdoc />
		public async Task<Peer> FindPeerAsync(MultiHash id, CancellationToken cancel = default)
		{
			// Can always find self.
			if (Swarm.LocalPeer.Id == id)
			{
				return Swarm.LocalPeer;
			}

			// Maybe the swarm knows about it.
			var found = Swarm.KnownPeers.FirstOrDefault(p => p.Id == id);
			if (found != null && found.Addresses.Count() > 0)
			{
				return found;
			}

			// Ask our peers for information on the requested peer.
			var dquery = new DistributedQuery<Peer>(_loggerFactory.CreateLogger<DistributedQuery<Peer>>(), _notificationService)
			{
				QueryType = MessageType.FindNode,
				QueryKey = id,
				Dht = this,
				AnswersNeeded = 1
			};
			await dquery.RunAsync(cancel).ConfigureAwait(false);

			// If not found, return the closest peer.
			return dquery.Answers.Count() == 0
				? RoutingTable.NearestPeers(id).FirstOrDefault()
				: dquery.Answers.First();
		}

		/// <inheritdoc />
		public Task ProvideAsync(Cid cid, bool advertise = true, CancellationToken cancel = default)
		{
			ContentRouter.Add(cid, this.Swarm.LocalPeer.Id);
			if (advertise)
			{
				Advertise(cid);
			}

			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public async Task<IEnumerable<Peer>> FindProvidersAsync(
			Cid id,
			int limit = 20,
			Action<Peer> action = null,
			CancellationToken cancel = default)
		{
			var dquery = new DistributedQuery<Peer>(_loggerFactory.CreateLogger<DistributedQuery<Peer>>(), _notificationService)
			{
				QueryType = MessageType.GetProviders,
				QueryKey = id.Hash,
				Dht = this,
				AnswersNeeded = limit,
			};
			if (!(action is null))
			{
				subscription = _notificationService.Subscribe<DistributedQuery<Peer>.AnswerObtained>(m => HandleAnswerObtained(action, m.Value));
			}

			// Add any providers that we already know about.
			var providers = ContentRouter
				.Get(id)
				.Select(
					pid =>
						(pid == Swarm.LocalPeer.Id)
							? Swarm.LocalPeer
							: Swarm.RegisterPeer(new Peer { Id = pid }));

			foreach (var provider in providers)
			{
				dquery.AddAnswer(provider);
			}

			// Ask our peers for more providers.
			if (limit > dquery.Answers.Count())
			{
				await dquery.RunAsync(cancel).ConfigureAwait(false);
			}

			return dquery.Answers.Take(limit);
		}

		private IDisposable subscription = null;
		private void HandleAnswerObtained(Action<Peer> action, Peer peer)
		{
			action(peer);
			subscription?.Dispose();
		}

		/// <summary>
		///   Advertise that we can provide the CID to the X closest peers
		///   of the CID.
		/// </summary>
		/// <param name="cid">
		///   The CID to advertise.
		/// </param>
		/// <remarks>
		///   This starts a background process to send the AddProvider message
		///   to the 4 closest peers to the <paramref name="cid"/>.
		/// </remarks>
		public void Advertise(Cid cid) =>
			Task.Run(
				async () =>
				{
					int advertsNeeded = 4;
					var message = new DhtMessage
					{
						Type = MessageType.AddProvider,
						Key = cid.Hash.ToArray(),
						ProviderPeers = new DhtPeerMessage[]
						{
							new DhtPeerMessage
							{
								Id = Swarm.LocalPeer.Id.ToArray(),
								Addresses = Swarm.LocalPeer.Addresses
									.Select(a => a.WithoutPeerId().ToArray())
									.ToArray()
							}
						}
					};
					var peers = RoutingTable
						.NearestPeers(cid.Hash)
						.Where(p => p != Swarm.LocalPeer);
					foreach (var peer in peers)
					{
						try
						{
							using (var stream = await Swarm.DialAsync(peer, this.ToString()))
							{
								ProtoBuf.Serializer.SerializeWithLengthPrefix(stream, message, PrefixStyle.Base128);
								await stream.FlushAsync();
							}

							if (--advertsNeeded == 0)
							{
								break;
							}
						}
						catch (Exception)
						{
							// eat it.  This is fire and forget.
						}
					}
				});

		/// <summary>
		///   Process a ping request.
		/// </summary>
		/// <remarks>
		///   Simply return the <paramref name="request"/>.
		/// </remarks>
		private DhtMessage ProcessPing(DhtMessage request, DhtMessage response) => request;

		/// <summary>
		///   Process a find node request.
		/// </summary>
		public DhtMessage ProcessFindNode(DhtMessage request, DhtMessage response)
		{
			// Some random walkers generate a random Key that is not hashed.
			MultiHash peerId;
			try
			{
				peerId = new MultiHash(request.Key);
			}
			catch (Exception e)
			{
				_logger.LogError(e, "Bad FindNode request key {RequestKey}", request.Key.ToHexString());
				peerId = MultiHash.ComputeHash(request.Key);
			}

			// Do we know the peer?.
			var found = Swarm.LocalPeer.Id == peerId
				? Swarm.LocalPeer
				: Swarm.KnownPeers.FirstOrDefault(p => p.Id == peerId);

			// Find the closer peers.
			var closerPeers = new List<Peer>();
			if (!(found is null))
			{
				closerPeers.Add(found);
			}
			else
			{
				closerPeers.AddRange(RoutingTable.NearestPeers(peerId).Take(CloserPeerCount));
			}

			// Build the response.
			response.CloserPeers = closerPeers
				.Select(
					peer =>
						new DhtPeerMessage
						{
							Id = peer.Id.ToArray(),
							Addresses = peer.Addresses.Select(a => a.WithoutPeerId().ToArray()).ToArray()
						})
				.ToArray();

			if (_logger.IsEnabled(LogLevel.Debug))
			{
				_logger.LogDebug("returning {ResponseCloserPeersLength} closer peers", response.CloserPeers.Length);
			}

			return response;
		}

		/// <summary>
		///   Process a get provider request.
		/// </summary>
		public DhtMessage ProcessGetProviders(DhtMessage request, DhtMessage response)
		{
			// Find providers for the content.
			var cid = new Cid { Hash = new MultiHash(request.Key) };
			response.ProviderPeers = ContentRouter
				.Get(cid)
				.Select(
					pid =>
					{
						var peer = (pid == Swarm.LocalPeer.Id)
							 ? Swarm.LocalPeer
							 : Swarm.RegisterPeer(new Peer { Id = pid });
						return new DhtPeerMessage
						{
							Id = peer.Id.ToArray(),
							Addresses = peer.Addresses.Select(a => a.WithoutPeerId().ToArray()).ToArray()
						};
					})
				.Take(20)
				.ToArray();

			// Also return the closest peers
			return ProcessFindNode(request, response);
		}

		/// <summary>
		///   Process an add provider request.
		/// </summary>
		public DhtMessage ProcessAddProvider(Peer remotePeer, DhtMessage request, DhtMessage response)
		{
			if (request.ProviderPeers is null)
			{
				return null;
			}

			Cid cid;
			try
			{
				cid = new Cid { Hash = new MultiHash(request.Key) };
			}
			catch (Exception e)
			{
				_logger.LogError(e, "Bad AddProvider request key {RequestKey}", request.Key.ToHexString());
				return null;
			}

			var providers = request.ProviderPeers
				.Select(p => p.TryToPeer(out Peer peer) ? peer : (Peer)null)
				.Where(p => !(p is null))
				.Where(p => p == remotePeer)
				.Where(p => p.Addresses.Count() > 0)
				.Where(p => Swarm.IsAllowed(p));
			foreach (var provider in providers)
			{
				_ = Swarm.RegisterPeer(provider);
				ContentRouter.Add(cid, provider.Id);
			};

			// There is no response for this request.
			return null;
		}
	}
}
