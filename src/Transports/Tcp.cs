namespace PeerTalk.Transports
{
	using Ipfs;
	using JuiceStream;
	using Microsoft.Extensions.Logging;
	using System;
	using System.IO;
	using System.Linq;
	using System.Net;
	using System.Net.Sockets;
	using System.Runtime.InteropServices;
	using System.Text;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	///   Establishes a duplex stream between two peers
	///   over TCP.
	/// </summary>
	/// <remarks>
	///   <see cref="ConnectAsync"/> determines the network latency and sets the timeout
	///   to 3 times the latency or <see cref="MinReadTimeout"/>.
	/// </remarks>
	public class Tcp : IPeerTransport
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="Tcp"/> class.
		/// </summary>
		/// <param name="logger">The logger.</param>
		/// <exception cref="ArgumentNullException">logger</exception>
		public Tcp(ILogger<Tcp> logger)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		/// <summary>
		///  The minimum read timeout.
		/// </summary>
		/// <value>
		///   Defaults to 3 seconds.
		/// </value>
		public static TimeSpan MinReadTimeout = TimeSpan.FromSeconds(3);
		private readonly ILogger<Tcp> _logger;

		/// <inheritdoc />
		public async Task<Stream> ConnectAsync(MultiAddress address, CancellationToken cancel = default)
		{
			var port = address.Protocols
				.Where(p => p.Name == "tcp")
				.Select(p => int.Parse(p.Value))
				.First();
			var ip = address.Protocols
				.Where(p => p.Name == "ip4" || p.Name == "ip6")
				.FirstOrDefault();
			if (ip is null)
			{
				throw new ArgumentException($"Missing IP address in '{address}'.", "address");
			}

			var socket = new Socket(
				ip.Name == "ip4" ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6,
				SocketType.Stream,
				ProtocolType.Tcp);

			TimeSpan latency = MinReadTimeout; // keep compiler happy
			var start = DateTime.Now;
			try
			{
				_logger.LogTrace("connecting to {Address}", address);

				// Handle cancellation of the connect attempt by disposing
				// of the socket.  This will force ConnectAsync to return.
				using (var _ = cancel.Register(() => { socket?.Dispose(); socket = null; }))
				{
					var ipaddr = IPAddress.Parse(ip.Value);
					await socket.ConnectAsync(ipaddr, port).ConfigureAwait(false);
				};

				latency = DateTime.Now - start;
				_logger.LogTrace("connected to {Address} in {LatencyTotalMilliseconds} ms", address, latency.TotalMilliseconds);
			}
			catch (Exception) when (cancel.IsCancellationRequested)
			{
				// eat it, the caller has cancelled and doesn't care.
			}
			catch (Exception)
			{
				latency = DateTime.Now - start;
				_logger.LogTrace("failed to {Address} in {LatencyTotalMilliseconds} ms", address, latency.TotalMilliseconds);
				socket?.Dispose();
				throw;
			}

			if (cancel.IsCancellationRequested)
			{
				_logger.LogTrace("cancel {Address}", address);
				socket?.Dispose();
				cancel.ThrowIfCancellationRequested();
			}

			var timeout = (int)Math.Max(MinReadTimeout.TotalMilliseconds, latency.TotalMilliseconds * 3);
			socket.LingerState = new LingerOption(false, 0);
			socket.ReceiveTimeout = timeout;
			socket.SendTimeout = timeout;
			Stream stream = new NetworkStream(socket, ownsSocket: true)
			{
				ReadTimeout = timeout,
				WriteTimeout = timeout
			};

			stream = new DuplexBufferedStream(stream);

			if (cancel.IsCancellationRequested)
			{
				_logger.LogTrace("cancel {Address}", address);
				stream.Dispose();
				cancel.ThrowIfCancellationRequested();
			}

			return stream;
		}

		/// <inheritdoc />
		public MultiAddress Listen(MultiAddress address, Action<Stream, MultiAddress, MultiAddress> handler, CancellationToken cancel)
		{
			var port = address.Protocols
				.Where(p => p.Name == "tcp")
				.Select(p => int.Parse(p.Value))
				.FirstOrDefault();
			var ip = address.Protocols
				.Where(p => p.Name == "ip4" || p.Name == "ip6")
				.FirstOrDefault();
			if (ip is null)
			{
				throw new ArgumentException($"Missing IP address in '{address}'.", "address");
			}

			var ipAddress = IPAddress.Parse(ip.Value);
			var endPoint = new IPEndPoint(ipAddress, port);
			var socket = new Socket(
				endPoint.AddressFamily,
				SocketType.Stream,
				ProtocolType.Tcp);
			try
			{
				socket.Bind(endPoint);
				socket.Listen(100);
			}
			catch (Exception e)
			{
				socket.Dispose();
				throw new Exception($"Bind/listen failed on {address}", e);
			}

			// If no port specified, then add it.
			var actualPort = ((IPEndPoint)socket.LocalEndPoint).Port;
			if (port != actualPort)
			{
				address = address.Clone();
				var protocol = address.Protocols.FirstOrDefault(p => p.Name == "tcp");
				if (protocol is null)
				{
					address.Protocols.AddRange(new MultiAddress("/tcp/" + actualPort).Protocols);
				}
				else
				{
					protocol.Value = actualPort.ToString();
				}
			}

			_ = Task.Run(() => ProcessConnection(socket, address, handler, cancel));

			return address;
		}

		private void ProcessConnection(Socket socket, MultiAddress address, Action<Stream, MultiAddress, MultiAddress> handler, CancellationToken cancel)
		{
			_logger.LogDebug("listening on {Address}", address);

			// Handle cancellation of the listener
			_ = cancel.Register(
				 () =>
				 {
					 _logger.LogDebug("Got cancel on {Address}", address);

					 try
					 {
						 // .Net Standard on Unix neeeds this to cancel the Accept
						 if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
						 {
							 socket.Shutdown(SocketShutdown.Both);
							 socket.Dispose();
						 }
						 else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
						 {
							 socket.Shutdown(SocketShutdown.Receive);
						 }
						 else // must be windows
						 {
							 socket.Dispose();
						 }
					 }
					 catch (Exception e)
					 {
						 _logger.LogWarning(e, "Cancelling listener.");
					 }
					 finally
					 {
						 socket = null;
					 }
				 });

			try
			{
				while (!cancel.IsCancellationRequested)
				{
					Socket conn = socket.Accept();
					if (conn is null)
					{
						_logger.LogWarning("Null socket from Accept");
						continue;
					}

					MultiAddress remote = null;
					if (conn.RemoteEndPoint is IPEndPoint endPoint)
					{
						var s = new StringBuilder()
							.Append(endPoint.AddressFamily == AddressFamily.InterNetwork ? "/ip4/" : "/ip6/")
							.Append(endPoint.Address.ToString())
							.Append("/tcp/")
							.Append(endPoint.Port);
						remote = new MultiAddress(s.ToString());
						_logger.LogDebug("connection from {Remote}", remote);
					}

					conn.NoDelay = true;
					Stream peer = new NetworkStream(conn, ownsSocket: true);

					// BufferedStream not available in .Net Standard 1.4
					peer = new DuplexBufferedStream(peer);
					try
					{
						handler(peer, address, remote);
					}
					catch (Exception e)
					{
						_logger.LogError(e, "listener handler failed {Address}", address);
						peer.Dispose();
					}
				}
			}
			catch (Exception) when (cancel.IsCancellationRequested)
			{
				// eat it
			}
			catch (Exception e)
			{
				// eat it and give up
				Console.WriteLine(e.Message);
				_logger.LogError(e, "listener failed {Address}", address);
			}
			finally
			{
				socket?.Dispose();
			}

			_logger.LogDebug("stop listening on {Address}", address);
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD002:Avoid problematic synchronous waits", Justification = "<Pending>")]
		private void ProcessConnection(Socket socket, MultiAddress address, Func<Stream, MultiAddress, MultiAddress, Task> handler, CancellationToken cancel)
		{
			_logger.LogDebug("listening on {Address}", address);

			// Handle cancellation of the listener
			_ = cancel.Register(
				 () =>
				 {
					 _logger.LogDebug("Got cancel on {Address}", address);

					 try
					 {
						 // .Net Standard on Unix neeeds this to cancel the Accept
						 if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
						 {
							 socket.Shutdown(SocketShutdown.Both);
							 socket.Dispose();
						 }
						 else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
						 {
							 socket.Shutdown(SocketShutdown.Receive);
						 }
						 else // must be windows
						 {
							 socket.Dispose();
						 }
					 }
					 catch (Exception e)
					 {
						 _logger.LogWarning(e, "Cancelling listener.");
					 }
					 finally
					 {
						 socket = null;
					 }
				 });

			try
			{
				while (!cancel.IsCancellationRequested)
				{
					Socket conn = socket.Accept();
					if (conn is null)
					{
						_logger.LogWarning("Null socket from Accept");
						continue;
					}

					MultiAddress remote = null;
					if (conn.RemoteEndPoint is IPEndPoint endPoint)
					{
						var s = new StringBuilder()
							.Append(endPoint.AddressFamily == AddressFamily.InterNetwork ? "/ip4/" : "/ip6/")
							.Append(endPoint.Address.ToString())
							.Append("/tcp/")
							.Append(endPoint.Port);
						remote = new MultiAddress(s.ToString());
						_logger.LogDebug("connection from {Remote}", remote);
					}

					conn.NoDelay = true;
					Stream peer = new NetworkStream(conn, ownsSocket: true);

					// BufferedStream not available in .Net Standard 1.4
					peer = new DuplexBufferedStream(peer);
					try
					{
						handler(peer, address, remote).GetAwaiter().GetResult();
					}
					catch (Exception e)
					{
						_logger.LogError(e, "listener handler failed {Address}", address);
						peer.Dispose();
					}
				}
			}
			catch (Exception) when (cancel.IsCancellationRequested)
			{
				// eat it
			}
			catch (Exception e)
			{
				// eat it and give up
				Console.WriteLine(e.Message);
				_logger.LogError(e, "listener failed {Address}", address);
			}
			finally
			{
				socket?.Dispose();
			}

			_logger.LogDebug("stop listening on {Address}", address);
		}

		/// <summary>
		/// Listen to any peer connections on the specified address.
		/// </summary>
		/// <param name="address">The address to listen on.</param>
		/// <param name="handler">The action to perform when a peer connection is received.</param>
		/// <param name="cancel">Is used to stop the connection listener. When cancelled, the <see cref="OperationCanceledException" /> is <b>NOT</b> raised.</param>
		/// <returns>The actual address of the listener.</returns>
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
				.Where(p => p.Name == "tcp")
				.Select(p => int.Parse(p.Value))
				.FirstOrDefault();
			var ip = address.Protocols
				.Where(p => p.Name == "ip4" || p.Name == "ip6")
				.FirstOrDefault();
			if (ip is null)
			{
				throw new ArgumentException($"Missing IP address in '{address}'.", "address");
			}

			var ipAddress = IPAddress.Parse(ip.Value);
			var endPoint = new IPEndPoint(ipAddress, port);
			var socket = new Socket(
				endPoint.AddressFamily,
				SocketType.Stream,
				ProtocolType.Tcp);
			try
			{
				socket.Bind(endPoint);
				socket.Listen(100);
			}
			catch (Exception e)
			{
				socket.Dispose();
				throw new Exception($"Bind/listen failed on {address}", e);
			}

			// If no port specified, then add it.
			var actualPort = ((IPEndPoint)socket.LocalEndPoint).Port;
			if (port != actualPort)
			{
				address = address.Clone();
				var protocol = address.Protocols.FirstOrDefault(p => p.Name == "tcp");
				if (protocol is null)
				{
					address.Protocols.AddRange(new MultiAddress("/tcp/" + actualPort).Protocols);
				}
				else
				{
					protocol.Value = actualPort.ToString();
				}
			}

			_ = Task.Run(() => ProcessConnection(socket, address, handler, cancel));

			return address;
		}
	}
}
