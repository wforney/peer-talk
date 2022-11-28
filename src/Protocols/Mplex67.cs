namespace PeerTalk.Protocols
{
	using Microsoft.Extensions.Logging;
	using PeerTalk.Multiplex;
	using Semver;
	using SharedCode.Notifications;
	using System;
	using System.IO;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	/// A Stream Multiplexer protocol.
	/// </summary>
	/// <seealso href="https://github.com/libp2p/mplex" />
	public class Mplex67 : IPeerProtocol
	{
		private readonly ILogger<Mplex67> _logger;
		private readonly ILoggerFactory _loggerFactory;
		private readonly INotificationService _notificationService;

		private IDisposable _muxerSubstreamCreated;

		/// <summary>
		/// Initializes a new instance of the <see cref="Mplex67" /> class.
		/// </summary>
		/// <param name="loggerFactory">The logger factory.</param>
		/// <param name="notificationService">The notification service.</param>
		/// <exception cref="ArgumentNullException">loggerFactory</exception>
		/// <exception cref="ArgumentNullException">notificationService</exception>
		public Mplex67(ILoggerFactory loggerFactory, INotificationService notificationService)
		{
			_loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
			_logger = loggerFactory.CreateLogger<Mplex67>() ?? throw new ArgumentNullException(nameof(loggerFactory));
			_notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
		}

		/// <inheritdoc />
		public string Name { get; } = "mplex";

		/// <inheritdoc />
		public SemVersion Version { get; } = new SemVersion(6, 7);

		/// <inheritdoc />
		public async Task ProcessMessageAsync(PeerConnection connection, Stream stream, CancellationToken cancel = default)
		{
			_logger.LogDebug("start processing requests from {ConnectionRemoteAddress}", connection.RemoteAddress);
			var muxer = new Muxer(_loggerFactory.CreateLogger<Muxer>(), _notificationService)
			{
				Channel = stream,
				Connection = connection,
				Receiver = true
			};

			_muxerSubstreamCreated = _notificationService.Subscribe<Muxer.SubstreamCreated>(m => HandleMuxerSubstreamCreatedAsync(connection, m));

			// Attach muxer to the connection. It now becomes the message reader.
			connection.MuxerEstablished.SetResult(muxer);
			await muxer.ProcessRequestsAsync().ConfigureAwait(false);

			_logger.LogDebug("stop processing from {ConnectionRemoteAddress}", connection.RemoteAddress);
		}

		/// <inheritdoc />
		public Task ProcessResponseAsync(PeerConnection connection, CancellationToken cancel = default) => Task.CompletedTask;

		/// <inheritdoc />
		public override string ToString() => $"/{Name}/{Version}";

		private async Task HandleMuxerSubstreamCreatedAsync(PeerConnection connection, Muxer.SubstreamCreated m)
		{
			await connection.ReadMessagesAsync(m.Substream, CancellationToken.None);
			_muxerSubstreamCreated?.Dispose();
			_muxerSubstreamCreated = null;
		}
	}
}
