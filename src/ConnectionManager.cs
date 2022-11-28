namespace PeerTalk
{
	using Ipfs;
	using SharedCode.Notifications;
	using System;
	using System.Collections.Concurrent;
	using System.Collections.Generic;
	using System.Linq;

	/// <summary>
	/// Manages the peer connections in a <see cref="Swarm" />.
	/// </summary>
	/// <remarks>
	/// Enforces that only one connection exists to the <see cref="Peer" />. This prevents the race
	/// condition when two simultaneously connect to each other.
	/// <para>TODO: Enforces a maximum number of open connections.</para>
	/// </remarks>
	public class ConnectionManager : IDisposable
	{
		private readonly INotificationService _notificationService;
		private readonly IDisposable _peerConnectionClosedSubscription;

		/// <summary>
		/// The connections to other peers. Key is the base58 hash of the peer ID.
		/// </summary>
		private readonly ConcurrentDictionary<string, List<PeerConnection>> connections = new ConcurrentDictionary<string, List<PeerConnection>>();

		private bool _disposed;

		/// <summary>
		/// Initializes a new instance of the <see cref="ConnectionManager" /> class.
		/// </summary>
		/// <param name="notificationService">The notification service.</param>
		/// <exception cref="ArgumentNullException">notificationService</exception>
		public ConnectionManager(INotificationService notificationService)
		{
			_notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

			_peerConnectionClosedSubscription = _notificationService.Subscribe<PeerConnection.Closed>(m => Remove(m.PeerConnection));
		}

		/// <summary>
		/// Finalizes an instance of the <see cref="ConnectionManager" /> class.
		/// </summary>
		~ConnectionManager()
		{
			// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
			Dispose(disposing: false);
		}

		/// <summary>
		/// Gets the current active connections.
		/// </summary>
		public IEnumerable<PeerConnection> Connections => connections.Values
			.SelectMany(c => c)
			.Where(c => c.IsActive);

		/// <summary>
		/// Adds a new connection.
		/// </summary>
		/// <param name="connection">The <see cref="PeerConnection" /> to add.</param>
		/// <returns>The connection that should be used.</returns>
		/// <remarks>
		/// If a connection already exists to the peer, the specified <paramref name="connection" />
		/// is closed and existing connection is returned.
		/// </remarks>
		public PeerConnection Add(PeerConnection connection)
		{
			if (connection is null)
			{
				throw new ArgumentNullException("connection");
			}

			if (connection.RemotePeer is null)
			{
				throw new ArgumentNullException("connection.RemotePeer");
			}

			if (connection.RemotePeer.Id is null)
			{
				throw new ArgumentNullException("connection.RemotePeer.Id");
			}

			_ = connections.AddOrUpdate(
				Key(connection.RemotePeer),
				(key) => new List<PeerConnection> { connection },
				(key, conns) =>
				{
					if (!conns.Contains(connection))
					{
						conns.Add(connection);
					}

					return conns;
				}
			);

			if (connection.RemotePeer.ConnectedAddress is null)
			{
				connection.RemotePeer.ConnectedAddress = connection.RemoteAddress;
			}

			return connection;
		}

		/// <summary>
		/// Removes and closes all connections.
		/// </summary>
		public void Clear()
		{
			var conns = connections.Values.SelectMany(c => c).ToArray();
			foreach (var conn in conns)
			{
				_ = Remove(conn);
			}
		}

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
		/// Determines if a connection exists to the specified peer.
		/// </summary>
		/// <param name="peer">Another peer.</param>
		/// <returns>
		/// <b>true</b> if there is a connection to the <paramref name="peer" /> and the connection
		/// is active; otherwise <b>false</b>.
		/// </returns>
		public bool IsConnected(Peer peer) => TryGet(peer, out PeerConnection _);

		/// <summary>
		/// Remove a connection.
		/// </summary>
		/// <param name="connection">The <see cref="PeerConnection" /> to remove.</param>
		/// <returns><b>true</b> if the connection was removed; otherwise, <b>false</b>.</returns>
		/// <remarks>
		/// The <paramref name="connection" /> is removed from the list of connections and is closed.
		/// </remarks>
		public bool Remove(PeerConnection connection)
		{
			if (connection is null)
			{
				return false;
			}

			if (!connections.TryGetValue(Key(connection.RemotePeer), out List<PeerConnection> originalConns))
			{
				connection.Dispose();
				return false;
			}

			if (!originalConns.Contains(connection))
			{
				connection.Dispose();
				return false;
			}

			var newConns = new List<PeerConnection>();
			newConns.AddRange(originalConns.Where(c => c != connection));
			_ = connections.TryUpdate(Key(connection.RemotePeer), newConns, originalConns);

			connection.Dispose();
			if (newConns.Count > 0)
			{
				var last = newConns.Last();
				last.RemotePeer.ConnectedAddress = last.RemoteAddress;
			}
			else
			{
				connection.RemotePeer.ConnectedAddress = null;
				_notificationService.Publish(new PeerDisconnected(connection.RemotePeer.Id));
			}

			return true;
		}

		/// <summary>
		/// Remove and close all connection to the peer ID.
		/// </summary>
		/// <param name="id">The ID of a <see cref="Peer" /> to remove.</param>
		/// <returns><b>true</b> if a connection was removed; otherwise, <b>false</b>.</returns>
		public bool Remove(MultiHash id)
		{
			if (!connections.TryRemove(Key(id), out List<PeerConnection> conns))
			{
				return false;
			}

			foreach (var conn in conns)
			{
				conn.RemotePeer.ConnectedAddress = null;
				conn.Dispose();
			}

			_notificationService.Publish(new PeerDisconnected(id));
			return true;
		}

		/// <summary>
		/// Gets the connection to the peer.
		/// </summary>
		/// <param name="peer">A peer.</param>
		/// <param name="connection">The connection to the peer.</param>
		/// <returns><b>true</b> if a connection exists; otherwise <b>false</b>.</returns>
		/// <remarks>
		/// If the connection's underlaying <see cref="PeerConnection.Stream" /> is closed, then the
		/// connection is removed.
		/// </remarks>
		public bool TryGet(Peer peer, out PeerConnection connection)
		{
			connection = null;
			if (!connections.TryGetValue(Key(peer), out List<PeerConnection> conns))
			{
				return false;
			}

			connection = conns
				.Where(c => c.IsActive)
				.FirstOrDefault();

			return !(connection is null);
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
				_peerConnectionClosedSubscription?.Dispose();
			}

			// free unmanaged resources (unmanaged objects) and override finalizer, set large
			// fields to null
			_disposed = true;
		}

		private string Key(Peer peer) => peer.Id.ToBase58();

		private string Key(MultiHash id) => id.ToBase58();

		/// <summary>
		/// The peer disconnected notification. Implements the <see cref="Notification" /> Sent when
		/// a peer's connection is closed.
		/// </summary>
		/// <seealso cref="Notification" />
		public class PeerDisconnected : Notification
		{
			/// <summary>
			/// Initializes a new instance of the <see cref="PeerDisconnected" /> class.
			/// </summary>
			/// <param name="multiHash">The multi hash.</param>
			public PeerDisconnected(MultiHash multiHash)
			{
				this.MultiHash = multiHash;
			}

			/// <summary>
			/// Gets the multi hash.
			/// </summary>
			/// <value>The multi hash.</value>
			public MultiHash MultiHash { get; }
		}
	}
}
