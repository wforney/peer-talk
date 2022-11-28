namespace PeerTalk
{
	using Ipfs;
	using Microsoft.Extensions.Logging;
	using PeerTalk.Cryptography;
	using PeerTalk.Multiplex;
	using PeerTalk.Protocols;
	using SharedCode.Notifications;
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	/// A connection between two peers.
	/// </summary>
	/// <remarks>A connection is used to exchange messages between peers.</remarks>
	public class PeerConnection : IDisposable
	{
		private readonly ILogger<PeerConnection> _logger;
		private readonly Message _message;
		private readonly ILoggerFactory _loggerFactory;
		private readonly INotificationService _notificationService;
		private readonly ProtocolRegistry _protocolRegistry;

		private bool _disposed = false;
		private StatsStream statsStream;
		private Stream stream;

		/// <summary>
		/// Initializes a new instance of the <see cref="PeerConnection"/> class.
		/// </summary>
		/// <param name="loggerFactory">The logger factory.</param>
		/// <param name="message">The message.</param>
		/// <param name="notificationService">The notification service.</param>
		/// <param name="protocolRegistry">The protocol registry.</param>
		/// <exception cref="ArgumentNullException">loggerFactory</exception>
		/// <exception cref="ArgumentNullException">message</exception>
		/// <exception cref="ArgumentNullException">notificationService</exception>
		/// <exception cref="ArgumentNullException">protocolRegistry</exception>
		public PeerConnection(ILoggerFactory loggerFactory, Message message, INotificationService notificationService, ProtocolRegistry protocolRegistry)
		{
			_loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
			_logger = loggerFactory.CreateLogger<PeerConnection>() ?? throw new ArgumentNullException(nameof(loggerFactory));
			_message = message ?? throw new ArgumentNullException(nameof(message));
			_notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
			_protocolRegistry = protocolRegistry ?? throw new ArgumentNullException(nameof(protocolRegistry));
		}

		/// <summary>
		/// Finalizes an instance of the <see cref="PeerConnection" /> class.
		/// </summary>
		~PeerConnection()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(false);
		}

		/// <summary>
		/// Number of bytes read over the connection.
		/// </summary>
		public long BytesRead => statsStream.BytesRead;

		/// <summary>
		/// Number of bytes written over the connection.
		/// </summary>
		public long BytesWritten => statsStream.BytesWritten;

		/// <summary>
		/// Signals that the identity of the remote endpoint is established.
		/// </summary>
		/// <remarks>This can be awaited.</remarks>
		/// <remarks>
		/// The data in <see cref="RemotePeer" /> is not complete until the identity is establish.
		/// </remarks>
		public TaskCompletionSource<Peer> IdentityEstablished { get; } = new TaskCompletionSource<Peer>();

		/// <summary>
		/// Determines if the connection to the remote can be used.
		/// </summary>
		/// <value><b>true</b> if the connection is active.</value>
		public bool IsActive => !(Stream is null) && Stream.CanRead && Stream.CanWrite;

		/// <summary>
		/// Determine which peer (local or remote) initiated the connection.
		/// </summary>
		/// <value>
		/// <b>true</b> if the <see cref="RemotePeer" /> initiated the connection; otherwise, <b>false</b>.
		/// </value>
		public bool IsIncoming { get; set; }

		/// <summary>
		/// When the connection was last used.
		/// </summary>
		public DateTime LastUsed => statsStream.LastUsed;

		/// <summary>
		/// The local peer's end point.
		/// </summary>
		public MultiAddress LocalAddress { get; set; }

		/// <summary>
		/// The local peer.
		/// </summary>
		public Peer LocalPeer { get; set; }

		/// <summary>
		/// The private key of the local peer.
		/// </summary>
		/// <value>Used to prove the identity of the <see cref="LocalPeer" />.</value>
		public Key LocalPeerKey { get; set; }

		/// <summary>
		/// Signals that the muxer for the connection is established.
		/// </summary>
		/// <remarks>This can be awaited.</remarks>
		public TaskCompletionSource<Muxer> MuxerEstablished { get; } = new TaskCompletionSource<Muxer>();

		/// <summary>
		/// The protocols that the connection will handle.
		/// </summary>
		/// <value>
		/// The key is a protocol name, such as "/mplex/6.7.0". The value is a function that will
		/// process the protocol message.
		/// </value>
		/// <seealso cref="AddProtocol" />
		/// <seealso cref="AddProtocols" />
		public Dictionary<string, Func<PeerConnection, Stream, CancellationToken, Task>> Protocols { get; } =
			new Dictionary<string, Func<PeerConnection, Stream, CancellationToken, Task>>();

		/// <summary>
		/// The remote peer's end point.
		/// </summary>
		public MultiAddress RemoteAddress { get; set; }

		/// <summary>
		/// The remote peer.
		/// </summary>
		public Peer RemotePeer { get; set; }

		/// <summary>
		/// Signals that the security for the connection is established.
		/// </summary>
		/// <remarks>This can be awaited.</remarks>
		public TaskCompletionSource<bool> SecurityEstablished { get; } = new TaskCompletionSource<bool>();

		/// <summary>
		/// The duplex stream between the two peers.
		/// </summary>
		public Stream Stream
		{
			get => stream;
			set
			{
				if (!(value is null) && statsStream is null)
				{
					statsStream = new StatsStream(value);
					value = statsStream;
				}

				stream = value;
			}
		}

		/// <summary>
		/// Add a protocol that the connection will handle.
		/// </summary>
		/// <param name="protocol">A peer protocol to add.</param>
		public void AddProtocol(IPeerProtocol protocol) => Protocols.Add(protocol.ToString(), protocol.ProcessMessageAsync);

		/// <summary>
		/// Add a seequence of protocols that the connection will handle.
		/// </summary>
		/// <param name="protocols">The peer protocols to add.</param>
		public void AddProtocols(IEnumerable<IPeerProtocol> protocols)
		{
			foreach (var protocol in protocols)
			{
				if (!(protocol is null))
				{
					Protocols.Add(protocol.ToString(), protocol.ProcessMessageAsync);
				}
			}
		}

		/// <summary>
		/// Performs application-defined tasks associated with freeing, releasing, or resetting
		/// unmanaged resources.
		/// </summary>
		public void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// TODO:
		/// </summary>
		/// <param name="name"></param>
		/// <param name="cancel"></param>
		/// <returns></returns>
		public Task EstablishProtocolAsync(string name, CancellationToken cancel) => EstablishProtocolAsync(name, Stream, cancel);

		/// <summary>
		/// TODO:
		/// </summary>
		/// <param name="name"></param>
		/// <param name="stream"></param>
		/// <param name="cancel"></param>
		/// <returns></returns>
		public async Task EstablishProtocolAsync(string name, Stream stream, CancellationToken cancel = default)
		{
			var protocols = _protocolRegistry.Protocols.Keys
				.Where(k => k == name || k.StartsWith(name))
				.Select(k => VersionedName.Parse(k))
				.OrderByDescending(vn => vn)
				.Select(vn => vn.ToString());
			foreach (var protocol in protocols)
			{
				await _message.WriteAsync(protocol, stream, cancel).ConfigureAwait(false);
				var result = await _message.ReadStringAsync(stream, cancel).ConfigureAwait(false);
				if (result == protocol)
				{
					return;
				}
			}

			if (protocols.Count() == 0)
			{
				throw new Exception($"Protocol '{name}' is not registered.");
			}

			throw new Exception($"{RemotePeer.Id} does not support protocol '{name}'.");
		}

		/// <summary>
		/// Establish the connection with the remote node.
		/// </summary>
		/// <param name="securityProtocols"></param>
		/// <param name="cancel"></param>
		/// <remarks>
		/// This should be called when the local peer wants a connection with the remote peer.
		/// </remarks>
		public async Task InitiateAsync(
			IEnumerable<IEncryptionProtocol> securityProtocols,
			CancellationToken cancel = default)
		{
			await EstablishProtocolAsync("/multistream/", cancel).ConfigureAwait(false);

			// Find the first security protocol that is also supported by the remote.
			var exceptions = new List<Exception>();
			foreach (var protocol in securityProtocols)
			{
				try
				{
					await EstablishProtocolAsync(protocol.ToString(), cancel).ConfigureAwait(false);
				}
				catch (Exception e)
				{
					exceptions.Add(e);
					continue;
				}

				_ = await protocol.EncryptAsync(this, cancel).ConfigureAwait(false);
				break;
			}

			if (!SecurityEstablished.Task.IsCompleted)
			{
				throw new AggregateException("Could not establish a secure connection.", exceptions);
			}

			await EstablishProtocolAsync("/multistream/", cancel).ConfigureAwait(false);
			await EstablishProtocolAsync("/mplex/", cancel).ConfigureAwait(false);

			var muxer = new Muxer(_loggerFactory.CreateLogger<Muxer>(), _notificationService)
			{
				Channel = Stream,
				Initiator = true,
				Connection = this
			};
			_ = _notificationService.Subscribe<Muxer.SubstreamCreated>(m => ReadMessagesAsync(m.Substream, CancellationToken.None)); // TODO: Unsubscribe in dispose?
			this.MuxerEstablished.SetResult(muxer);

			_ = muxer.ProcessRequestsAsync();
		}

		/// <summary>
		/// Starts reading messages from the remote peer.
		/// </summary>
		public async Task ReadMessagesAsync(CancellationToken cancel)
		{
			_logger.LogDebug("start reading messsages from {RemoteAddress}", RemoteAddress);

			// TODO: Only a subset of protocols are allowed until the remote is authenticated.
			IPeerProtocol protocol = new Multistream1(_loggerFactory.CreateLogger<Multistream1>(), _message);
			try
			{
				while (!cancel.IsCancellationRequested && Stream != null)
				{
					await protocol.ProcessMessageAsync(this, Stream, cancel).ConfigureAwait(false);
				}
			}
			catch (IOException e)
			{
				// eat it.
				_logger.LogError(e, "reading message failed");
			}
			catch (Exception e)
			{
				if (!cancel.IsCancellationRequested && Stream != null)
				{
					_logger.LogError(e, "reading message failed");
				}
			}

			// Ignore any disposal exceptions.
			try
			{
				Stream?.Dispose();
			}
			catch (Exception)
			{
				// eat it.
			}

			_logger.LogDebug("stop reading messsages from {RemoteAddress}", RemoteAddress);
		}

		/// <summary>
		/// Starts reading messages from the remote peer on the specified stream.
		/// </summary>
		public async Task ReadMessagesAsync(Stream stream, CancellationToken cancel)
		{
			IPeerProtocol protocol = new Multistream1(_loggerFactory.CreateLogger<Multistream1>(), _message);
			try
			{
				while (!cancel.IsCancellationRequested && stream != null && stream.CanRead)
				{
					await protocol.ProcessMessageAsync(this, stream, cancel).ConfigureAwait(false);
				}
			}
			catch (EndOfStreamException)
			{
				// eat it.
			}
			catch (Exception e)
			{
				if (!cancel.IsCancellationRequested && stream != null)
				{
					_logger.LogError(e, "reading message failed {RemoteAddress} {RemotePeer}", RemoteAddress, RemotePeer);
				}
			}
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

			_disposed = true;

			if (!disposing)
			{
				return;
			}

			_logger.LogDebug("Closing connection to {RemoteAddress}", RemoteAddress);
			if (!(Stream is null))
			{
				try
				{
					Stream.Dispose();
				}
				catch (ObjectDisposedException)
				{
					// ignore stream already closed.
				}
				catch (Exception e)
				{
					// eat it.
					_logger.LogWarning(e, "Failed to close connection to {RemoteAddress}", RemoteAddress);
				}
				finally
				{
					Stream = null;
					statsStream = null;
				}
			}

			_ = SecurityEstablished.TrySetCanceled();
			_ = IdentityEstablished.TrySetCanceled();
			_ = IdentityEstablished.TrySetCanceled();
			_notificationService.Publish(new Closed(this));

			// free unmanaged resources (unmanaged objects) and override a finalizer below. set
			// large fields to null.
		}

		/// <summary>
		/// The closed notification. Implements the <see cref="Notification" /> Signals that the
		/// connection is closed (disposed).
		/// </summary>
		/// <seealso cref="Notification" />
		public class Closed : Notification
		{
			/// <summary>
			/// Initializes a new instance of the <see cref="Closed" /> class.
			/// </summary>
			/// <param name="peerConnection">The peer connection.</param>
			public Closed(PeerConnection peerConnection) => this.PeerConnection = peerConnection;

			/// <summary>
			/// Gets the peer connection.
			/// </summary>
			/// <value>The peer connection.</value>
			public PeerConnection PeerConnection { get; }
		}
	}
}
