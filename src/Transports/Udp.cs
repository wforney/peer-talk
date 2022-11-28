namespace PeerTalk.Transports
{
	using Ipfs;
	using Microsoft.Extensions.Logging;
	using System;
	using System.IO;
	using System.Linq;
	using System.Net;
	using System.Net.Sockets;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	///   Establishes a duplex stream between two peers
	///   over UDP.
	/// </summary>
	public class Udp : IPeerTransport
	{
		private readonly ILogger<Udp> _logger;

		/// <summary>
		/// Initializes a new instance of the <see cref="Udp"/> class.
		/// </summary>
		/// <param name="logger">The logger.</param>
		/// <exception cref="ArgumentNullException">logger</exception>
		public Udp(ILogger<Udp> logger)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		/// <inheritdoc />
		public async Task<Stream> ConnectAsync(MultiAddress address, CancellationToken cancel = default)
		{
			var port = address.Protocols
				.Where(p => p.Name == "udp")
				.Select(p => int.Parse(p.Value))
				.First();
			var ip = address.Protocols
				.Where(p => p.Name == "ip4" || p.Name == "ip6")
				.First();
			var socket = new Socket(
				ip.Name == "ip4" ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6,
				SocketType.Dgram,
				ProtocolType.Udp);

			// Handle cancellation of the connect attempt
			_ = cancel.Register(() =>
			{
				socket.Dispose();
				socket = null;
			});

			try
			{
				_logger.LogDebug("connecting to {Address}", address);
				await socket.ConnectAsync(ip.Value, port).ConfigureAwait(false);
				_logger.LogDebug("connected {Address}", address);
			}
			catch (Exception) when (cancel.IsCancellationRequested)
			{
				// eat it, the caller has cancelled and doesn't care.
			}
			catch (Exception e)
			{
				_logger.LogWarning(e, "failed {Address}", address);
				throw;
			}

			if (cancel.IsCancellationRequested)
			{
				_logger.LogDebug("cancel {Address}", address);
				socket?.Dispose();
				cancel.ThrowIfCancellationRequested();
			}

			return new DatagramStream(socket, ownsSocket: true);
		}

		/// <inheritdoc />
		public MultiAddress Listen(MultiAddress address, Action<Stream, MultiAddress, MultiAddress> handler, CancellationToken cancel)
		{
			var port = address.Protocols
				.Where(p => p.Name == "udp")
				.Select(p => int.Parse(p.Value))
				.FirstOrDefault();
			var ip = address.Protocols
				.Where(p => p.Name == "ip4" || p.Name == "ip6")
				.First();
			var ipAddress = IPAddress.Parse(ip.Value);
			var endPoint = new IPEndPoint(ipAddress, port);
			var socket = new Socket(
				endPoint.AddressFamily,
				SocketType.Dgram,
				ProtocolType.Udp);
			socket.Bind(endPoint);

			// If no port specified, then add it.
			var actualPort = ((IPEndPoint)socket.LocalEndPoint).Port;
			if (port != actualPort)
			{
				address = address.Clone();
				var protocol = address.Protocols.FirstOrDefault(p => p.Name == "udp");
				if (protocol is null)
				{
					address.Protocols.AddRange(new MultiAddress($"/udp/{actualPort}").Protocols);
				}
				else
				{
					protocol.Value = actualPort.ToString();
				}
			}

			// TODO: UDP listener
			throw new NotImplementedException();
#if false
            var stream = new DatagramStream(socket, ownsSocket: true);
            handler(stream, address, null);

            return address;
#endif
		}

		/// <summary>
		/// Listen to any peer connections on the specified address.
		/// </summary>
		/// <param name="address">The address to listen on.</param>
		/// <param name="handler">The action to perform when a peer connection is received.</param>
		/// <param name="cancel">Is used to stop the connection listener. When cancelled, the <see cref="OperationCanceledException" /> is <b>NOT</b> raised.</param>
		/// <returns>The actual address of the listener.</returns>
		/// <exception cref="System.NotImplementedException"></exception>
		/// <remarks>The <paramref name="handler" /> is invoked on the peer listener thread. If it throws,
		/// then the connection is closed but the listener remains active. It is passed a duplex
		/// stream, the local address and the remote address.
		/// <para>
		/// To stop listening, the <paramref name="cancel" /> parameter must be supplied and then
		/// use the <see cref="CancellationTokenSource.Cancel()" /> method.
		/// </para><para>
		/// For socket based transports (tcp or upd), if the port is not defined or is zero an
		/// ephermal port is assigned.
		/// </para></remarks>
		public MultiAddress Listen(MultiAddress address, Func<Stream, MultiAddress, MultiAddress, Task> handler, CancellationToken cancel)
		{
			var port = address.Protocols
				.Where(p => p.Name == "udp")
				.Select(p => int.Parse(p.Value))
				.FirstOrDefault();
			var ip = address.Protocols
				.Where(p => p.Name == "ip4" || p.Name == "ip6")
				.First();
			var ipAddress = IPAddress.Parse(ip.Value);
			var endPoint = new IPEndPoint(ipAddress, port);
			var socket = new Socket(
				endPoint.AddressFamily,
				SocketType.Dgram,
				ProtocolType.Udp);
			socket.Bind(endPoint);

			// If no port specified, then add it.
			var actualPort = ((IPEndPoint)socket.LocalEndPoint).Port;
			if (port != actualPort)
			{
				address = address.Clone();
				var protocol = address.Protocols.FirstOrDefault(p => p.Name == "udp");
				if (protocol is null)
				{
					address.Protocols.AddRange(new MultiAddress($"/udp/{actualPort}").Protocols);
				}
				else
				{
					protocol.Value = actualPort.ToString();
				}
			}

			// TODO: UDP listener
			throw new NotImplementedException();
#if false
            var stream = new DatagramStream(socket, ownsSocket: true);
            handler(stream, address, null);

            return address;
#endif
		}
	}
}
