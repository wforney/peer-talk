namespace PeerTalk.Transports
{
	using Microsoft.Extensions.Logging;
	using System;
	using System.Collections.Concurrent;

	/// <summary>
	/// Class TransportRegistry.
	/// </summary>
	public class TransportRegistry
	{
		/// <summary>
		/// The transports
		/// </summary>
		public ConcurrentDictionary<string, Func<IPeerTransport>> Transports;

		/// <summary>
		/// Initializes a new instance of the <see cref="TransportRegistry"/> class.
		/// </summary>
		/// <param name="loggerFactory">The logger factory.</param>
		public TransportRegistry(ILoggerFactory loggerFactory)
		{
			Transports = new ConcurrentDictionary<string, Func<IPeerTransport>>();
			Register("tcp", () => new Tcp(loggerFactory.CreateLogger<Tcp>()));
			Register("udp", () => new Udp(loggerFactory.CreateLogger<Udp>()));
		}

		/// <summary>
		/// Deregisters the specified protocol name.
		/// </summary>
		/// <param name="protocolName">Name of the protocol.</param>
		public void Deregister(string protocolName) => Transports.TryRemove(protocolName, out _);

		/// <summary>
		/// Registers the specified protocol name.
		/// </summary>
		/// <param name="protocolName">Name of the protocol.</param>
		/// <param name="transport">The transport.</param>
		public void Register(string protocolName, Func<IPeerTransport> transport) => Transports.TryAdd(protocolName, transport);
	}
}
