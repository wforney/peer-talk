namespace PeerTalk.Protocols
{
	using Microsoft.Extensions.Logging;
	using Semver;
	using System;
	using System.IO;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	///   A protocol to select other protocols.
	/// </summary>
	/// <seealso href="https://github.com/multiformats/multistream-select"/>
	public class Multistream1 : IPeerProtocol
	{
		private readonly ILogger<Multistream1> _logger;
		private readonly Message _message;

		/// <summary>
		/// Initializes a new instance of the <see cref="Multistream1"/> class.
		/// </summary>
		/// <param name="logger">The logger.</param>
		/// <param name="message">The message.</param>
		/// <exception cref="ArgumentNullException">logger</exception>
		/// <exception cref="ArgumentNullException">message</exception>
		public Multistream1(ILogger<Multistream1> logger, Message message)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_message = message ?? throw new ArgumentNullException(nameof(message));
		}

		/// <inheritdoc />
		public string Name { get; } = "multistream";

		/// <inheritdoc />
		public SemVersion Version { get; } = new SemVersion(1, 0);

		/// <inheritdoc />
		public override string ToString() => $"/{Name}/{Version}";

		/// <inheritdoc />
		public async Task ProcessMessageAsync(PeerConnection connection, Stream stream, CancellationToken cancel = default)
		{
			var msg = await _message.ReadStringAsync(stream, cancel).ConfigureAwait(false);

			// TODO: msg == "ls"
			if (msg == "ls")
			{
				throw new NotImplementedException("multistream ls");
			}

			// Switch to the specified protocol
			if (!connection.Protocols.TryGetValue(msg, out Func<PeerConnection, Stream, CancellationToken, Task> protocol))
			{
				await _message.WriteAsync("na", stream, cancel).ConfigureAwait(false);
				return;
			}

			// Ack protocol switch
			_logger.LogDebug("switching to {Message}", msg);
			await _message.WriteAsync(msg, stream, cancel).ConfigureAwait(false);

			// Process protocol message.
			await protocol(connection, stream, cancel).ConfigureAwait(false);
		}
	}
}
