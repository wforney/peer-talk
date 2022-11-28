namespace PeerTalk.Protocols
{
	using Microsoft.Extensions.DependencyInjection;
	using System;
	using System.Collections.Generic;

	/// <summary>
	/// Metadata on <see cref="IPeerProtocol" />.
	/// </summary>
	public class ProtocolRegistry
	{
		private readonly IServiceProvider _serviceProvider;

		/// <summary>
		/// Initializes a new instance of the <see cref="ProtocolRegistry" /> class.
		/// </summary>
		/// <param name="serviceProvider">The service provider.</param>
		/// <exception cref="ArgumentNullException">serviceProvider</exception>
		public ProtocolRegistry(IServiceProvider serviceProvider)
		{
			_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
			Register<Multistream1>();
			Register<SecureCommunication.Secio1>();
			Register<Plaintext1>();
			Register<Identify1>();
			Register<Mplex67>();
		}

		/// <summary>
		/// All the peer protocols.
		/// </summary>
		/// <remarks>
		/// The key is the name and version of the peer protocol, like "/multiselect/1.0.0". The
		/// value is a Func that returns an new instance of the peer protocol.
		/// </remarks>
		public Dictionary<string, Func<IPeerProtocol>> Protocols { get; } = new Dictionary<string, Func<IPeerProtocol>>();

		/// <summary>
		/// Remove the specified protocol.
		/// </summary>
		/// <param name="protocolName">The protocol name to remove.</param>
		public void Deregister(string protocolName) => Protocols.Remove(protocolName);

		/// <summary>
		/// Register a new protocol.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		public void Register<T>() where T : IPeerProtocol
		{
			var p = _serviceProvider.GetRequiredService<T>();
			Protocols.Add(p.ToString(), () => _serviceProvider.GetRequiredService<T>());
		}
	}
}
