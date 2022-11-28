namespace PeerTalk
{
	using Ipfs;
	using SharedCode.Notifications;

	public partial class Swarm
	{
		/// <summary>
		///   Raised when a new peer is discovered for the first time.
		/// </summary>
		public class PeerDiscovered : Notification
		{
			/// <summary>
			/// Initializes a new instance of the <see cref="PeerDiscovered"/> class.
			/// </summary>
			/// <param name="peer">The peer.</param>
			public PeerDiscovered(Peer peer) => this.Peer = peer;

			/// <summary>
			/// Gets the peer.
			/// </summary>
			/// <value>The peer.</value>
			public Peer Peer { get; }
		}
	}
}
