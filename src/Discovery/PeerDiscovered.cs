namespace PeerTalk.Discovery
{
	using Ipfs;
	using SharedCode.Notifications;

	/// <summary>
	/// The peer discovered notification sent when a peer is discovered. Implements the <see
	/// cref="Notification" />.
	/// </summary>
	/// <remarks>
	/// The peer must contain at least one <see cref="MultiAddress" />. The address must end
	/// with the ipfs protocol and the public ID of the peer. For example "/ip4/104.131.131.82/tcp/4001/ipfs/QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ"
	/// </remarks>
	/// <seealso cref="Notification" />
	public class PeerDiscovered : Notification
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="PeerDiscovered" /> class.
		/// </summary>
		/// <param name="peer">The peer.</param>
		public PeerDiscovered(Peer peer = null)
		{
			this.Peer = peer;
		}

		/// <summary>
		/// Gets the peer.
		/// </summary>
		/// <value>The peer.</value>
		public Peer Peer { get; }
	}
}
