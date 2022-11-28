namespace PeerTalk
{
	using Ipfs;
	using Microsoft.Extensions.Logging;
	using SharedCode.Notifications;
	using System;
	using System.Collections.Concurrent;
	using System.Linq;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	/// Manages the peers.
	/// </summary>
	/// <remarks>Listens to the <see cref="Swarm" /> events to determine the state of a peer.</remarks>
	public class PeerManager : IService
	{
		/// <summary>
		/// The peers that are reachable.
		/// </summary>
		public ConcurrentDictionary<Peer, DeadPeer> DeadPeers = new ConcurrentDictionary<Peer, DeadPeer>();

		/// <summary>
		/// Initial time to wait before attempting a reconnection to a dead peer.
		/// </summary>
		/// <value>Defaults to 1 minute.</value>
		public TimeSpan InitialBackoff = TimeSpan.FromMinutes(1);

		/// <summary>
		/// When reached, the peer is considered permanently dead.
		/// </summary>
		/// <value>Defaults to 64 minutes.</value>
		public TimeSpan MaxBackoff = TimeSpan.FromMinutes(64);

		private readonly ILogger<PeerManager> _logger;

		private readonly INotificationService _notificationService;

		private IDisposable _swarmConnectionEstablishedSubscription;

		private IDisposable _swarmPeerNotReachableSubscription;

		private CancellationTokenSource cancel;

		/// <summary>
		/// Initializes a new instance of the <see cref="PeerManager" /> class.
		/// </summary>
		/// <param name="logger">The logger.</param>
		/// <param name="notificationService">The notification service.</param>
		/// <exception cref="ArgumentNullException">logger</exception>
		/// <exception cref="ArgumentNullException">notificationService</exception>
		public PeerManager(ILogger<PeerManager> logger, INotificationService notificationService)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
		}

		/// <summary>
		/// Provides access to other peers.
		/// </summary>
		public Swarm Swarm { get; set; }

		/// <summary>
		/// Indicates that the peer can not be connected to.
		/// </summary>
		/// <param name="peer"></param>
		public void SetNotReachable(Peer peer)
		{
			var dead = DeadPeers.AddOrUpdate(peer,
				new DeadPeer
				{
					Peer = peer,
					Backoff = InitialBackoff,
					NextAttempt = DateTime.Now + InitialBackoff
				},
				(key, existing) =>
				{
					existing.Backoff += existing.Backoff;
					existing.NextAttempt = existing.Backoff <= MaxBackoff
						? DateTime.Now + existing.Backoff
						: DateTime.MaxValue;
					return existing;
				});

			Swarm.BlackList.Add($"/p2p/{peer.Id}");
			if (dead.NextAttempt != DateTime.MaxValue)
			{
				_logger.LogDebug("Dead '{DeadPeer}' for {DeadBackoffTotalMinutes} minutes.", dead.Peer, dead.Backoff.TotalMinutes);
			}
			else
			{
				Swarm.DeregisterPeer(dead.Peer);
				_logger.LogDebug("Permanently dead '{DeadPeer}'.", dead.Peer);
			}
		}

		/// <summary>
		/// Indicates that the peer can be connected to.
		/// </summary>
		/// <param name="peer"></param>
		public void SetReachable(Peer peer)
		{
			_logger.LogDebug("Alive '{Peer}'.", peer);

			_ = DeadPeers.TryRemove(peer, out _);
			_ = Swarm.BlackList.Remove($"/p2p/{peer.Id}");
		}

		/// <inheritdoc />
		public Task StartAsync()
		{
			_swarmConnectionEstablishedSubscription = _notificationService.Subscribe<Swarm.ConnectionEstablished>(m => Swarm_ConnectionEstablished(m.PeerConnection));
			_swarmPeerNotReachableSubscription = _notificationService.Subscribe<Swarm.PeerNotReachable>(m => Swarm_PeerNotReachable(m.Peer));
			cancel = new CancellationTokenSource();
			var _ = PhoenixAsync(cancel.Token);

			_logger.LogDebug("started");
			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public Task StopAsync()
		{
			_swarmConnectionEstablishedSubscription?.Dispose();
			_swarmConnectionEstablishedSubscription = null;
			_swarmPeerNotReachableSubscription?.Dispose();
			_swarmPeerNotReachableSubscription = null;

			DeadPeers.Clear();

			cancel.Cancel();
			cancel.Dispose();

			_logger.LogDebug("stopped");
			return Task.CompletedTask;
		}

		/// <summary>
		/// Background process to try reconnecting to a dead peer.
		/// </summary>
		private async Task PhoenixAsync(CancellationToken cancellation)
		{
			while (!cancellation.IsCancellationRequested)
			{
				try
				{
					await Task.Delay(InitialBackoff);
					var now = DateTime.Now;
					await DeadPeers.Values
						.Where(p => p.NextAttempt < now)
						.ParallelForEachAsync(
						   async dead =>
						   {
							   _logger.LogDebug("Attempt reconnect to {DeadPeer}", dead.Peer);
							   _ = Swarm.BlackList.Remove($"/p2p/{dead.Peer.Id}");
							   try
							   {
								   _ = await Swarm.ConnectAsync(dead.Peer, cancel.Token).ConfigureAwait(false);
							   }
							   catch
							   {
								   // eat it
							   }
						   },
						maxDoP: 10);
				}
				catch
				{
					// eat it.
				}
			}
		}

		/// <summary>
		/// Is invoked by the <see cref="Swarm" /> when a peer is connected to.
		/// </summary>
		private void Swarm_ConnectionEstablished(PeerConnection connection) => SetReachable(connection.RemotePeer);

		/// <summary>
		/// Is invoked by the <see cref="Swarm" /> when a peer can not be connected to.
		/// </summary>
		private void Swarm_PeerNotReachable(Peer peer) => SetNotReachable(peer);
	}
}
