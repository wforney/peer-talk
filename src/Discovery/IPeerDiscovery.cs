namespace PeerTalk.Discovery
{
	using static PeerTalk.Discovery.Bootstrap;

	/// <summary>
	/// Describes a service that finds a peer.
	/// </summary>
	/// <remarks>
	/// All discovery services must send the <see cref="PeerDiscovered"/> notification.
	/// </remarks>
	public interface IPeerDiscovery : IService
	{
	}
}
