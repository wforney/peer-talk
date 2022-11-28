namespace PeerTalk
{
	using Ipfs;
	using Microsoft.Extensions.Logging;
	using PeerTalk.Discovery;
	using SharedCode.Notifications;
	using System;
	using System.Linq;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	/// Maintains a minimum number of peer connections.
	/// </summary>
	/// <remarks>
	/// Listens to the <see cref="Swarm" /> and automically dials a new <see cref="Peer" /> when required.
	/// </remarks>
	public class AutoDialer : IDisposable
	{
		/// <summary>
		/// The default minimum number of connections to maintain (16).
		/// </summary>
		public const int DefaultMinConnections = 16;

		private readonly ILogger<AutoDialer> _logger;
		private readonly INotificationService _notificationService;
		private readonly Swarm _swarm;
		private readonly IDisposable _swarmPeerDisconnected;
		private readonly IDisposable _swarmPeerDiscovered;

		private bool _disposed;
		private int pendingConnects;

		/// <summary>
		/// Initializes a new instance of the <see cref="AutoDialer" /> class.
		/// </summary>
		/// <param name="logger">The logger.</param>
		/// <param name="notificationService">The notification service.</param>
		/// <param name="swarm">The swarm.</param>
		/// <exception cref="ArgumentNullException">logger</exception>
		/// <exception cref="ArgumentNullException">notificationService</exception>
		/// <exception cref="ArgumentNullException">swarm</exception>
		public AutoDialer(ILogger<AutoDialer> logger, INotificationService notificationService, Swarm swarm)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
			_swarm = swarm ?? throw new ArgumentNullException(nameof(swarm));

			_swarmPeerDisconnected = _notificationService.Subscribe<Swarm.PeerDisconnected>(m => this.OnPeerDisconnectedAsync(m.Peer));
			_swarmPeerDiscovered = _notificationService.Subscribe<PeerDiscovered>(m => this.OnPeerDiscoveredAsync(m.Peer));
		}

		/// <summary>
		/// Finalizes an instance of the <see cref="AutoDialer" /> class.
		/// </summary>
		~AutoDialer()
		{
			// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
			Dispose(disposing: false);
		}

		/// <summary>
		/// The low water mark for peer connections.
		/// </summary>
		/// <value>Defaults to <see cref="DefaultMinConnections" />.</value>
		/// <remarks>Setting this to zero will basically disable the auto dial features.</remarks>
		public int MinConnections { get; set; } = DefaultMinConnections;

		/// <summary>
		/// Performs application-defined tasks associated with freeing, releasing, or resetting
		/// unmanaged resources.
		/// </summary>
		public void Dispose()
		{
			// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// Releases unmanaged and - optionally - managed resources.
		/// </summary>
		/// <param name="disposing">
		/// <c>true</c> to release both managed and unmanaged resources; <c>false</c> to release
		/// only unmanaged resources.
		/// </param>
		protected virtual void Dispose(bool disposing)
		{
			if (_disposed)
			{
				return;
			}

			if (disposing)
			{
				// dispose managed state (managed objects)
				_swarmPeerDiscovered?.Dispose();
				_swarmPeerDisconnected?.Dispose();
			}

			// free unmanaged resources (unmanaged objects) and override finalizer, set large fields
			// to null
			_disposed = true;
		}

		/// <summary>
		/// Called when the swarm has lost a connection to a peer.
		/// </summary>
		/// <param name="disconnectedPeer">The peer that was disconnected.</param>
		/// <remarks>
		/// If the <see cref="MinConnections" /> is not reached, then another peer is dialed.
		/// </remarks>
		private async Task OnPeerDisconnectedAsync(Peer disconnectedPeer)
		{
			var n = _swarm.Manager.Connections.Count() + pendingConnects;
			if (!_swarm.IsRunning || n >= MinConnections)
			{
				return;
			}

			// Find a random peer to connect with.
			var peers = _swarm.KnownPeers
				.Where(p => p.ConnectedAddress is null)
				.Where(p => p != disconnectedPeer)
				.Where(p => _swarm.IsAllowed(p))
				.Where(p => !_swarm.HasPendingConnection(p))
				.ToArray();
			if (peers.Length == 0)
			{
				return;
			}

			var rng = new Random();
			var peer = peers[rng.Next(peers.Count())];

			_ = Interlocked.Increment(ref pendingConnects);
			_logger.LogDebug("Dialing {Peer}", peer);
			try
			{
				_ = await _swarm.ConnectAsync(peer).ConfigureAwait(false);
			}
			catch (Exception)
			{
				_logger.LogWarning("Failed to dial {Peer}", peer);
			}
			finally
			{
				_ = Interlocked.Decrement(ref pendingConnects);
			}
		}

		/// <summary>
		/// Called when the swarm has a new peer.
		/// </summary>
		/// <param name="peer">The peer that was discovered.</param>
		/// <remarks>
		/// If the <see cref="MinConnections" /> is not reached, then the <paramref name="peer" />
		/// is dialed.
		/// </remarks>
		private async Task OnPeerDiscoveredAsync(Peer peer)
		{
			var n = _swarm.Manager.Connections.Count() + pendingConnects;
			if (_swarm.IsRunning && n < MinConnections)
			{
				_ = Interlocked.Increment(ref pendingConnects);
				_logger.LogDebug("Dialing new {Peer}", peer);
				try
				{
					_ = await _swarm.ConnectAsync(peer).ConfigureAwait(false);
				}
				catch (Exception)
				{
					_logger.LogWarning("Failed to dial {Peer}", peer);
				}
				finally
				{
					_ = Interlocked.Decrement(ref pendingConnects);
				}
			}
		}
	}
}
