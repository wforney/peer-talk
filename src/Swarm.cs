namespace PeerTalk
{
	using Ipfs;
	using Ipfs.CoreApi;
	using Microsoft.Extensions.Logging;
	using Nito.AsyncEx;
	using PeerTalk.Cryptography;
	using PeerTalk.Protocols;
	using PeerTalk.Transports;
	using SharedCode.Notifications;
	using System;
	using System.Collections.Concurrent;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Net.NetworkInformation;
	using System.Net.Sockets;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	///   Manages communication with other peers.
	/// </summary>
	public partial class Swarm : IService, IPolicy<MultiAddress>, IPolicy<Peer>
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="Swarm"/> class.
		/// </summary>
		/// <param name="loggerFactory">The logger factory.</param>
		/// <param name="message">The message.</param>
		/// <param name="notificationService">The notification service.</param>
		/// <param name="protocolRegistry">The protocol registry.</param>
		/// <param name="transportRegistry">The transport registry.</param>
		/// <exception cref="ArgumentNullException">loggerFactory</exception>
		/// <exception cref="ArgumentNullException">message</exception>
		/// <exception cref="ArgumentNullException">notificationService</exception>
		/// <exception cref="ArgumentNullException">protocolRegistry</exception>
		/// <exception cref="ArgumentNullException">transportRegistry</exception>
		public Swarm(
			ILoggerFactory loggerFactory,
			Message message,
			INotificationService notificationService,
			ProtocolRegistry protocolRegistry,
			TransportRegistry transportRegistry)
		{
			_loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
			_logger = loggerFactory.CreateLogger<Swarm>() ?? throw new ArgumentNullException(nameof(loggerFactory));
			_message = message ?? throw new ArgumentNullException(nameof(message));
			_notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
			_protocolRegistry = protocolRegistry ?? throw new ArgumentNullException(nameof(protocolRegistry));
			_transportRegistry = transportRegistry ?? throw new ArgumentNullException(nameof(transportRegistry));

			protocols = new List<IPeerProtocol>
			{
				new Multistream1(loggerFactory.CreateLogger<Multistream1>(), message),
				new SecureCommunication.Secio1(),
				new Identify1(loggerFactory.CreateLogger<Identify1>()),
				new Mplex67(loggerFactory, notificationService)
			};

			Manager = new ConnectionManager(_notificationService);
		}

		/// <summary>
		///   The time to wait for a low level connection to be established.
		/// </summary>
		/// <value>
		///   Defaults to 30 seconds.
		/// </value>
		public TimeSpan TransportConnectionTimeout = TimeSpan.FromSeconds(30);

		/// <summary>
		///  The supported protocols.
		/// </summary>
		/// <remarks>
		///   Use sychronized access, e.g. <code>lock (protocols) { ... }</code>.
		/// </remarks>
		private readonly List<IPeerProtocol> protocols;

		/// <summary>
		///   Added to connection protocols when needed.
		/// </summary>
		private readonly Plaintext1 plaintext1 = new Plaintext1();
		private readonly ILoggerFactory _loggerFactory;
		private readonly ILogger<Swarm> _logger;
		private readonly Message _message;
		private readonly INotificationService _notificationService;
		private readonly ProtocolRegistry _protocolRegistry;
		private readonly TransportRegistry _transportRegistry;

		private Peer localPeer;

		/// <summary>
		///  The local peer.
		/// </summary>
		/// <value>
		///   The local peer must have an <see cref="Peer.Id"/> and
		///   <see cref="Peer.PublicKey"/>.
		/// </value>
		public Peer LocalPeer
		{
			get => localPeer;
			set
			{
				if (value is null)
				{
					throw new ArgumentNullException();
				}

				if (value.Id is null)
				{
					throw new ArgumentNullException("peer.Id");
				}

				if (value.PublicKey is null)
				{
					throw new ArgumentNullException("peer.PublicKey");
				}

				if (!value.IsValid())
				{
					throw new ArgumentException("Invalid peer.");
				}

				localPeer = value;
			}
		}

		/// <summary>
		///   The private key of the local peer.
		/// </summary>
		/// <value>
		///   Used to prove the identity of the <see cref="LocalPeer"/>.
		/// </value>
		public Key LocalPeerKey { get; set; }

		/// <summary>
		///   Other nodes. Key is the bae58 hash of the peer ID.
		/// </summary>
		private readonly ConcurrentDictionary<string, Peer> otherPeers = new ConcurrentDictionary<string, Peer>();

		/// <summary>
		///   Used to cancel any task when the swarm is stopped.
		/// </summary>
		private CancellationTokenSource swarmCancellation;

		/// <summary>
		///  Outstanding connection tasks initiated by the local peer.
		/// </summary>
		private readonly ConcurrentDictionary<Peer, AsyncLazy<PeerConnection>> pendingConnections = new ConcurrentDictionary<Peer, AsyncLazy<PeerConnection>>();

		/// <summary>
		///  Outstanding connection tasks initiated by a remote peer.
		/// </summary>
		private readonly ConcurrentDictionary<MultiAddress, object> pendingRemoteConnections = new ConcurrentDictionary<MultiAddress, object>();

		/// <summary>
		///   Manages the swarm's peer connections.
		/// </summary>
		public ConnectionManager Manager;

		/// <summary>
		///   Use to find addresses of a peer.
		/// </summary>
		public IPeerRouting Router { get; set; }

		/// <summary>
		///   Provides access to a private network of peers.
		/// </summary>
		public INetworkProtector NetworkProtector { get; set; }

		/// <summary>
		///   Determines if the swarm has been started.
		/// </summary>
		/// <value>
		///   <b>true</b> if the swarm has started; otherwise, <b>false</b>.
		/// </value>
		/// <seealso cref="StartAsync"/>
		/// <seealso cref="StopAsync"/>
		public bool IsRunning { get; private set; }

		/// <summary>
		///   Cancellation tokens for the listeners.
		/// </summary>
		private readonly ConcurrentDictionary<MultiAddress, CancellationTokenSource> listeners = new ConcurrentDictionary<MultiAddress, CancellationTokenSource>();

		/// <summary>
		///   Get the sequence of all known peer addresses.
		/// </summary>
		/// <value>
		///   Contains any peer address that has been
		///   <see cref="RegisterPeerAddress">discovered</see>.
		/// </value>
		/// <seealso cref="RegisterPeerAddress"/>
		public IEnumerable<MultiAddress> KnownPeerAddresses => otherPeers.Values.SelectMany(p => p.Addresses);

		/// <summary>
		///   Get the sequence of all known peers.
		/// </summary>
		/// <value>
		///   Contains any peer that has been
		///   <see cref="RegisterPeerAddress">discovered</see>.
		/// </value>
		/// <seealso cref="RegisterPeerAddress"/>
		public IEnumerable<Peer> KnownPeers => otherPeers.Values;

		/// <summary>
		///   Register that a peer's address has been discovered.
		/// </summary>
		/// <param name="address">
		///   An address to the peer. It must end with the peer ID.
		/// </param>
		/// <returns>
		///   The <see cref="Peer"/> that is registered.
		/// </returns>
		/// <exception cref="Exception">
		///   The <see cref="BlackList"/> or <see cref="WhiteList"/> policies forbid it.
		///   Or the "p2p/ipfs" protocol name is missing.
		/// </exception>
		/// <remarks>
		///   If the <paramref name="address"/> is not already known, then it is
		///   added to the <see cref="KnownPeerAddresses"/>.
		/// </remarks>
		/// <seealso cref="RegisterPeer(Peer)"/>
		public Peer RegisterPeerAddress(MultiAddress address)
		{
			var peer = new Peer
			{
				Id = address.PeerId,
				Addresses = new List<MultiAddress> { address }
			};

			return RegisterPeer(peer);
		}

		/// <summary>
		///   Register that a peer has been discovered.
		/// </summary>
		/// <param name="peer">
		///   The newly discovered peer.
		/// </param>
		/// <returns>
		///   The registered peer.
		/// </returns>
		/// <remarks>
		///   If the peer already exists, then the existing peer is updated with supplied
		///   information and is then returned.  Otherwise, the <paramref name="peer"/>
		///   is added to known peers and is returned.
		///   <para>
		///   If the peer already exists, then a union of the existing and new addresses
		///   is used.  For all other information the <paramref name="peer"/>'s information
		///   is used if not <b>null</b>.
		///   </para>
		///   <para>
		///   If peer does not already exist, then the <see cref="PeerDiscovered"/> event
		///   is raised.
		///   </para>
		/// </remarks>
		/// <exception cref="Exception">
		///   The <see cref="BlackList"/> or <see cref="WhiteList"/> policies forbid it.
		/// </exception>
		public Peer RegisterPeer(Peer peer)
		{
			if (peer.Id is null)
			{
				throw new ArgumentNullException("peer.ID");
			}

			if (peer.Id == LocalPeer.Id)
			{
				throw new ArgumentException("Cannot register self.");
			}

			if (!IsAllowed(peer))
			{
				throw new Exception($"Communication with '{peer}' is not allowed.");
			}

			var isNew = false;
			var p = otherPeers.AddOrUpdate(
				peer.Id.ToBase58(),
				(id) =>
				{
					isNew = true;
					return peer;
				},
				(id, existing) =>
				{
					if (!ReferenceEquals(existing, peer))
					{
						existing.AgentVersion = peer.AgentVersion ?? existing.AgentVersion;
						existing.ProtocolVersion = peer.ProtocolVersion ?? existing.ProtocolVersion;
						existing.PublicKey = peer.PublicKey ?? existing.PublicKey;
						existing.Latency = peer.Latency ?? existing.Latency;
						existing.Addresses = existing
							.Addresses
							.Union(peer.Addresses)
							.ToList();
					}

					return existing;
				});

			if (isNew)
			{
				if (_logger.IsEnabled(LogLevel.Debug))
				{
					_logger.LogDebug("New peer registerd {Peer}", p);
				}

				_notificationService.Publish(new PeerDiscovered(p));
			}

			return p;
		}

		/// <summary>
		///   Deregister a peer.
		/// </summary>
		/// <param name="peer">
		///   The peer to remove..
		/// </param>
		/// <remarks>
		///   Remove all knowledge of the peer. The <see cref="PeerRemoved"/> event
		///   is raised.
		/// </remarks>
		public void DeregisterPeer(Peer peer)
		{
			if (peer.Id is null)
			{
				throw new ArgumentNullException("peer.ID");
			}

			if (otherPeers.TryRemove(peer.Id.ToBase58(), out Peer found))
			{
				peer = found;
			}

			_notificationService.Publish(new PeerRemoved(peer));
		}

		/// <summary>
		///   Determines if a connection is being made to the peer.
		/// </summary>
		/// <param name="peer">
		///   A <see cref="Peer"/>.
		/// </param>
		/// <returns>
		///   <b>true</b> is the <paramref name="peer"/> has a pending connection.
		/// </returns>
		public bool HasPendingConnection(Peer peer)
		{
			return pendingConnections.TryGetValue(peer, out AsyncLazy<PeerConnection> _);
		}

		/// <summary>
		///   The addresses that cannot be used.
		/// </summary>
		public MultiAddressDenyList BlackList { get; set; } = new MultiAddressDenyList();

		/// <summary>
		///   The addresses that can be used.
		/// </summary>
		public MultiAddressAllowList WhiteList { get; set; } = new MultiAddressAllowList();

		private IDisposable _connectionManagerPeerDisconnectedSubscription;
		/// <inheritdoc />
		public Task StartAsync()
		{
			if (LocalPeer is null)
			{
				throw new NotSupportedException("The LocalPeer is not defined.");
			}

			// Many of the unit tests do not setup the LocalPeerKey.  If
			// its missing, then just use plaintext connection.
			// TODO: make the tests setup the security protocols.
			if (LocalPeerKey is null)
			{
				lock (protocols)
				{
					var security = protocols.OfType<IEncryptionProtocol>().ToArray();
					foreach (var p in security)
					{
						_ = protocols.Remove(p);
					}

					protocols.Add(plaintext1);
				}

				_logger.LogWarning("Peer key is missing, using unencrypted connections.");
			}

			_connectionManagerPeerDisconnectedSubscription = _notificationService.Subscribe<ConnectionManager.PeerDisconnected>(m => OnPeerDisconnected(m.MultiHash));
			IsRunning = true;
			swarmCancellation = new CancellationTokenSource();
			_logger.LogDebug("Started");

			return Task.CompletedTask;
		}

		private void OnPeerDisconnected(MultiHash peerId)
		{
			if (!otherPeers.TryGetValue(peerId.ToBase58(), out Peer peer))
			{
				peer = new Peer { Id = peerId };
			}

			_notificationService.Publish(new PeerDisconnected(peer));
		}

		/// <inheritdoc />
		public async Task StopAsync()
		{
			IsRunning = false;
			swarmCancellation?.Cancel(true);

			_logger.LogDebug("Stopping {LocalPeer}", LocalPeer);

			// Stop the listeners.
			while (listeners.Count > 0)
			{
				await StopListeningAsync(listeners.Keys.First()).ConfigureAwait(false);
			}

			// Disconnect from remote peers.
			Manager.Clear();
			_connectionManagerPeerDisconnectedSubscription?.Dispose();

			otherPeers.Clear();
			listeners.Clear();
			pendingConnections.Clear();
			pendingRemoteConnections.Clear();
			BlackList = new MultiAddressDenyList();
			WhiteList = new MultiAddressAllowList();

			_logger.LogDebug("Stopped {LocalPeer}", LocalPeer);
		}

		/// <summary>
		///   Connect to a peer using the specified <see cref="MultiAddress"/>.
		/// </summary>
		/// <param name="address">
		///   An ipfs <see cref="MultiAddress"/>, such as
		///  <c>/ip4/104.131.131.82/tcp/4001/ipfs/QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ</c>.
		/// </param>
		/// <param name="cancel">
		///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
		/// </param>
		/// <returns>
		///   A task that represents the asynchronous operation. The task's result
		///   is the <see cref="PeerConnection"/>.
		/// </returns>
		/// <remarks>
		///   If already connected to the peer and is active on any address, then
		///   the existing connection is returned.
		/// </remarks>
		public async Task<PeerConnection> ConnectAsync(MultiAddress address, CancellationToken cancel = default)
		{
			var peer = RegisterPeerAddress(address);
			return await ConnectAsync(peer, cancel).ConfigureAwait(false);
		}

		/// <summary>
		///   Connect to a peer.
		/// </summary>
		/// <param name="peer">
		///  A peer to connect to.
		/// </param>
		/// <param name="cancel">
		///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
		/// </param>
		/// <returns>
		///   A task that represents the asynchronous operation. The task's result
		///   is the <see cref="PeerConnection"/>.
		/// </returns>
		/// <remarks>
		///   If already connected to the peer and is active on any address, then
		///   the existing connection is returned.
		/// </remarks>
		public async Task<PeerConnection> ConnectAsync(Peer peer, CancellationToken cancel = default)
		{
			if (!IsRunning)
			{
				throw new Exception("The swarm is not running.");
			}

			peer = RegisterPeer(peer);

			// If connected and still open, then use the existing connection.
			if (Manager.TryGet(peer, out PeerConnection conn))
			{
				return conn;
			}

			// Use a current connection attempt to the peer or create a new one.
			try
			{
				using (var cts = CancellationTokenSource.CreateLinkedTokenSource(swarmCancellation.Token, cancel))
				{
					return await pendingConnections
						.GetOrAdd(peer, (key) => new AsyncLazy<PeerConnection>(() => DialAsync(peer, peer.Addresses, cts.Token)))
						.ConfigureAwait(false);
				}
			}
			catch (Exception)
			{
				_notificationService.Publish(new PeerNotReachable(peer));
				throw;
			}
			finally
			{
				_ = pendingConnections.TryRemove(peer, out _);
			}
		}

		/// <summary>
		///   Create a stream to the peer that talks the specified protocol.
		/// </summary>
		/// <param name="peer">
		///   The remote peer.
		/// </param>
		/// <param name="protocol">
		///   The protocol name, such as "/foo/0.42.0".
		/// </param>
		/// <param name="cancel">
		///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
		/// </param>
		/// <returns>
		///   A task that represents the asynchronous operation. The task's result
		///   is the new <see cref="Stream"/> to the <paramref name="peer"/>.
		/// </returns>
		/// <remarks>
		///   <para>
		///   When finished, the caller must <see cref="Stream.Dispose()"/> the
		///   new stream.
		///   </para>
		/// </remarks>
		public async Task<Stream> DialAsync(Peer peer, string protocol, CancellationToken cancel = default)
		{
			peer = RegisterPeer(peer);

			// Get a connection and then a muxer to the peer.
			var connection = await ConnectAsync(peer, cancel).ConfigureAwait(false);
			var muxer = await connection.MuxerEstablished.Task.ConfigureAwait(false);

			// Create a new stream for the peer protocol.
			var stream = await muxer.CreateStreamAsync(protocol).ConfigureAwait(false);
			try
			{
				await connection.EstablishProtocolAsync("/multistream/", stream).ConfigureAwait(false);

				await _message.WriteAsync(protocol, stream, cancel).ConfigureAwait(false);
				var result = await _message.ReadStringAsync(stream, cancel).ConfigureAwait(false);
				return result == protocol ? (Stream)stream : throw new Exception($"Protocol '{protocol}' not supported by '{peer}'.");
			}
			catch (Exception)
			{
				stream.Dispose();
				throw;
			}
		}

		/// <summary>
		///   Establish a duplex stream between the local and remote peer.
		/// </summary>
		/// <param name="remote"></param>
		/// <param name="addrs"></param>
		/// <param name="cancel"></param>
		/// <returns></returns>
		private async Task<PeerConnection> DialAsync(Peer remote, IEnumerable<MultiAddress> addrs, CancellationToken cancel)
		{
			_logger.LogDebug("Dialing {RemotePeer}", remote);

			if (remote == LocalPeer)
			{
				throw new Exception("Cannot dial self.");
			}

			// If no addresses, then ask peer routing.
			if (!(Router is null) && addrs.Count() == 0)
			{
				var found = await Router.FindPeerAsync(remote.Id, cancel).ConfigureAwait(false);
				addrs = found.Addresses;
				remote.Addresses = addrs;
			}

			// Get the addresses we can use to dial the remote.  Filter
			// out any addresses (ip and port) we are listening on.
			var blackList = listeners.Keys
				.Select(a => a.WithoutPeerId())
				.ToArray();
			var possibleAddresses = (await Task.WhenAll(addrs.Select(a => a.ResolveAsync(cancel))).ConfigureAwait(false))
				.SelectMany(a => a)
				.Where(a => !blackList.Contains(a.WithoutPeerId()))
				.Select(a => a.WithPeerId(remote.Id))
				.Distinct()
				.ToArray();
			if (possibleAddresses.Length == 0)
			{
				throw new Exception($"{remote} has no known or reachable address.");
			}

			// Try the various addresses in parallel.  The first one to complete wins.
			PeerConnection connection = null;
			try
			{
				using (var timeout = new CancellationTokenSource(TransportConnectionTimeout))
				using (var cts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancel))
				{
					var attempts = possibleAddresses
						.Select(a => DialAsync(remote, a, cts.Token));
					connection = await TaskHelper.WhenAnyResultAsync(attempts, cts.Token).ConfigureAwait(false);
					cts.Cancel(); // stop other dialing tasks.
				}
			}
			catch (Exception e)
			{
				var attemped = string.Join(", ", possibleAddresses.Select(a => a.ToString()));
				_logger.LogTrace("Cannot dial {Attemped}", attemped);
				throw new Exception($"Cannot dial {remote}.", e);
			}

			// Do the connection handshake.
			try
			{
				MountProtocols(connection);
				IEncryptionProtocol[] security = null;
				lock (protocols)
				{
					security = protocols.OfType<IEncryptionProtocol>().ToArray();
				}

				await connection.InitiateAsync(security, cancel).ConfigureAwait(false);
				_ = await connection.MuxerEstablished.Task.ConfigureAwait(false);
				Identify1 identify = null;
				lock (protocols)
				{
					identify = protocols.OfType<Identify1>().First();
				}

				_ = await identify.GetRemotePeerAsync(connection, cancel).ConfigureAwait(false);
			}
			catch (Exception)
			{
				connection.Dispose();
				throw;
			}

			var actual = Manager.Add(connection);
			if (actual == connection)
			{
				_notificationService.Publish(new ConnectionEstablished(connection));
			}

			return actual;

		}

		private async Task<PeerConnection> DialAsync(Peer remote, MultiAddress addr, CancellationToken cancel)
		{
			// TODO: HACK: Currenty only the ipfs/p2p is supported.
			// short circuit to make life faster.
			if (addr.Protocols.Count != 3 ||
				!(addr.Protocols[2].Name == "ipfs" ||
				addr.Protocols[2].Name == "p2p"))
			{
				throw new Exception($"Cannnot dial; unknown protocol in '{addr}'.");
			}

			// Establish the transport stream.
			Stream stream = null;
			foreach (var protocol in addr.Protocols)
			{
				cancel.ThrowIfCancellationRequested();
				if (_transportRegistry.Transports.TryGetValue(protocol.Name, out Func<IPeerTransport> transport))
				{
					stream = await transport().ConnectAsync(addr, cancel).ConfigureAwait(false);
					if (cancel.IsCancellationRequested)
					{
						stream?.Dispose();
						continue;
					}

					break;
				}
			}

			if (stream is null)
			{
				throw new Exception("Missing a known transport protocol name.");
			}

			// Build the connection.
			var connection = new PeerConnection(_loggerFactory, _message, _notificationService, _protocolRegistry)
			{
				IsIncoming = false,
				LocalPeer = LocalPeer,
				// TODO: LocalAddress
				LocalPeerKey = LocalPeerKey,
				RemotePeer = remote,
				RemoteAddress = addr,
				Stream = stream
			};

			// Are we communicating to a private network?
			if (!(NetworkProtector is null))
			{
				connection.Stream = await NetworkProtector.ProtectAsync(connection).ConfigureAwait(false);
			}

			return connection;
		}

		/// <summary>
		///   Disconnect from a peer.
		/// </summary>
		/// <param name="address">
		///   An ipfs <see cref="MultiAddress"/>, such as
		///  <c>/ip4/104.131.131.82/tcp/4001/ipfs/QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ</c>.
		/// </param>
		/// <param name="cancel">
		///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
		/// </param>
		/// <returns>
		///   A task that represents the asynchronous operation.
		/// </returns>
		/// <remarks>
		///   If the peer is not conected, then nothing happens.
		/// </remarks>
		[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "<Pending>")]
		public Task DisconnectAsync(MultiAddress address, CancellationToken cancel = default)
		{
			_ = Manager.Remove(address.PeerId);
			return Task.CompletedTask;
		}

		/// <summary>
		///   Start listening on the specified <see cref="MultiAddress"/>.
		/// </summary>
		/// <param name="address">
		///   Typically "/ip4/0.0.0.0/tcp/4001" or "/ip6/::/tcp/4001".
		/// </param>
		/// <returns>
		///   A task that represents the asynchronous operation.  The task's result
		///   is a <see cref="MultiAddress"/> than can be used by another peer
		///   to connect to tis peer.
		/// </returns>
		/// <exception cref="Exception">
		///   Already listening on <paramref name="address"/>.
		/// </exception>
		/// <exception cref="ArgumentException">
		///   <paramref name="address"/> is missing a transport protocol (such as tcp or udp).
		/// </exception>
		/// <remarks>
		///   Allows other peers to <see cref="ConnectAsync(MultiAddress, CancellationToken)">connect</see>
		///   to the <paramref name="address"/>.
		///   <para>
		///   The <see cref="Peer.Addresses"/> of the <see cref="LocalPeer"/> are updated.  If the <paramref name="address"/> refers to
		///   any IP address ("/ip4/0.0.0.0" or "/ip6/::") then all network interfaces addresses
		///   are added.  If the port is zero (as in "/ip6/::/tcp/0"), then the peer addresses contains the actual port number
		///   that was assigned.
		///   </para>
		/// </remarks>
		public Task<MultiAddress> StartListeningAsync(MultiAddress address)
		{
			var cancel = new CancellationTokenSource();

			if (!listeners.TryAdd(address, cancel))
			{
				throw new Exception($"Already listening on '{address}'.");
			}

			// Start a listener for the transport
			var didSomething = false;
			foreach (var protocol in address.Protocols)
			{
				if (_transportRegistry.Transports.TryGetValue(protocol.Name, out Func<IPeerTransport> transport))
				{
					address = transport().Listen(address, OnRemoteConnectAsync, cancel.Token);
					_ = listeners.TryAdd(address, cancel);
					didSomething = true;
					break;
				}
			}

			if (!didSomething)
			{
				throw new ArgumentException($"Missing a transport protocol name '{address}'.", "address");
			}

			var result = new MultiAddress($"{address}/ipfs/{LocalPeer.Id}");

			// Get the actual IP address(es).
			IEnumerable<MultiAddress> addresses = new List<MultiAddress>();

			var ips = NetworkInterface.GetAllNetworkInterfaces()
				// It appears that the loopback adapter is not UP on Ubuntu 14.04.5 LTS
				.Where(nic => nic.OperationalStatus == OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
				.SelectMany(nic => nic.GetIPProperties().UnicastAddresses);

			addresses = result.ToString().StartsWith("/ip4/0.0.0.0/")
				? ips
					.Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork)
					.Select(ip => new MultiAddress(result.ToString().Replace("0.0.0.0", ip.Address.ToString())))
					.ToArray()
				: result.ToString().StartsWith("/ip6/::/")
					? ips
						.Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetworkV6)
						.Select(ip => new MultiAddress(result.ToString().Replace("::", ip.Address.ToString())))
						.ToArray()
					: (IEnumerable<MultiAddress>)(new MultiAddress[] { result });

			if (addresses.Count() == 0)
			{
				var msg = $"Cannot determine address(es) for {result}";
				foreach (var ip in ips)
				{
					msg += $" nic-ip: {ip.Address}";
				}

				cancel.Cancel();
				throw new Exception(msg);
			}

			// Add actual addresses to listeners and local peer addresses.
			foreach (var a in addresses)
			{
				_logger.LogDebug("Listening on {Address}", a);
				_ = listeners.TryAdd(a, cancel);
			}

			LocalPeer.Addresses = LocalPeer
				.Addresses
				.Union(addresses)
				.ToArray();

			_notificationService.Publish(new ListenerEstablished(LocalPeer));
			return Task.FromResult(addresses.First());
		}

		/// <summary>
		///   Called when a remote peer is connecting to the local peer.
		/// </summary>
		/// <param name="stream">
		///   The stream to the remote peer.
		/// </param>
		/// <param name="local">
		///   The local peer's address.
		/// </param>
		/// <param name="remote">
		///   The remote peer's address.
		/// </param>
		/// <remarks>
		///   Establishes the protocols of the connection.  Any exception is simply
		///   logged as warning.
		/// </remarks>
		private async Task OnRemoteConnectAsync(Stream stream, MultiAddress local, MultiAddress remote)
		{
			if (!IsRunning)
			{
				try
				{
					stream.Dispose();
				}
				catch (Exception)
				{
					// eat it.
				}

				return;
			}

			// If the remote is already trying to establish a connection, then we
			// can just refuse this one.
			if (!pendingRemoteConnections.TryAdd(remote, null))
			{
				_logger.LogDebug("Duplicate remote connection from {Remote}", remote);
				try
				{
					stream.Dispose();
				}
				catch (Exception)
				{
					// eat it.
				}

				return;
			}

			try
			{
				if (_logger.IsEnabled(LogLevel.Debug))
				{
					_logger.LogDebug("remote connect from {Remote}", remote);
				}

				// TODO: Check the policies

				var connection = new PeerConnection(_loggerFactory, _message, _notificationService, _protocolRegistry)
				{
					IsIncoming = true,
					LocalPeer = LocalPeer,
					LocalAddress = local,
					LocalPeerKey = LocalPeerKey,
					RemoteAddress = remote,
					Stream = stream
				};

				// Are we communicating to a private network?
				if (!(NetworkProtector is null))
				{
					connection.Stream = await NetworkProtector.ProtectAsync(connection).ConfigureAwait(false);
				}

				// Mount the protocols.
				MountProtocols(connection);

				// Start the handshake
				// TODO: Isn't connection cancel token required.
				_ = connection.ReadMessagesAsync(default);

				// Wait for security to be established.
				_ = await connection.SecurityEstablished.Task.ConfigureAwait(false);
				// TODO: Maybe connection.LocalPeerKey = null;

				// Wait for the handshake to complete.
				var muxer = await connection.MuxerEstablished.Task;

				// Need details on the remote peer.
				Identify1 identify = null;
				lock (protocols)
				{
					identify = protocols.OfType<Identify1>().First();
				}

				connection.RemotePeer = await identify.GetRemotePeerAsync(connection, default).ConfigureAwait(false);

				connection.RemotePeer = RegisterPeer(connection.RemotePeer);
				connection.RemoteAddress = new MultiAddress($"{remote}/ipfs/{connection.RemotePeer.Id}");
				var actual = Manager.Add(connection);
				if (actual == connection)
				{
					_notificationService.Publish(new ConnectionEstablished(connection));
				}
			}
			catch (Exception e)
			{
				if (_logger.IsEnabled(LogLevel.Debug))
				{
					_logger.LogDebug(e, "remote connect from {Remote} failed.", remote);
				}

				try
				{
					stream.Dispose();
				}
				catch (Exception)
				{
					// eat it.
				}
			}
			finally
			{
				_ = pendingRemoteConnections.TryRemove(remote, out _);
			}
		}

		/// <summary>
		///   Add a protocol that is supported by the swarm.
		/// </summary>
		/// <param name="protocol">
		///   The protocol to add.
		/// </param>
		public void AddProtocol(IPeerProtocol protocol)
		{
			lock (protocols)
			{
				protocols.Add(protocol);
			}
		}

		/// <summary>
		///   Remove a protocol from the swarm.
		/// </summary>
		/// <param name="protocol">
		///   The protocol to remove.
		/// </param>
		public void RemoveProtocol(IPeerProtocol protocol)
		{
			lock (protocols)
			{
				_ = protocols.Remove(protocol);
			}
		}

		private void MountProtocols(PeerConnection connection)
		{
			lock (protocols)
			{
				connection.AddProtocols(protocols);
			}
		}

		/// <summary>
		///   Stop listening on the specified <see cref="MultiAddress"/>.
		/// </summary>
		/// <param name="address"></param>
		/// <returns>
		///   A task that represents the asynchronous operation.
		/// </returns>
		/// <remarks>
		///   Allows other peers to <see cref="ConnectAsync(MultiAddress, CancellationToken)">connect</see>
		///   to the <paramref name="address"/>.
		///   <para>
		///   The addresses of the <see cref="LocalPeer"/> are updated.
		///   </para>
		/// </remarks>
		public async Task StopListeningAsync(MultiAddress address)
		{
			if (!listeners.TryRemove(address, out CancellationTokenSource listener))
			{
				return;
			}

			try
			{
				if (!listener.IsCancellationRequested)
				{
					listener.Cancel(false);
				}

				// Remove any local peer address that depend on the cancellation token.
				var others = listeners
					.Where(l => l.Value == listener)
					.Select(l => l.Key)
					.ToArray();

				LocalPeer.Addresses = LocalPeer.Addresses
					.Where(a => a != address)
					.Where(a => !others.Contains(a))
					.ToArray();

				foreach (var other in others)
				{
					_ = listeners.TryRemove(other, out _);
				}

				// Give some time away, so that cancel can run
				// TODO: Would be nice to make this deterministic.
				await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
			}
			catch (Exception e)
			{
				_logger.LogError(e, "stop listening failed");
			}
		}

		/// <inheritdoc />
		public bool IsAllowed(MultiAddress target) => BlackList.IsAllowed(target) && WhiteList.IsAllowed(target);

		/// <inheritdoc />
		public bool IsAllowed(Peer peer) => peer.Addresses.All(a => IsAllowed(a));
	}
}
