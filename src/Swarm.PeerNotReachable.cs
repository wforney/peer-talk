namespace PeerTalk
{
	using Ipfs;
	using SharedCode.Notifications;

	public partial class Swarm
	{
		/// <summary>
		///   Raised when a peer cannot be connected to.
		/// </summary>
		public class PeerNotReachable : Notification
		{
			/// <summary>
			/// Initializes a new instance of the <see cref="PeerNotReachable"/> class.
			/// </summary>
			/// <param name="peer">The peer.</param>
			public PeerNotReachable(Peer peer) => this.Peer = peer;

			/// <summary>
			/// Gets the peer.
			/// </summary>
			/// <value>The peer.</value>
			public Peer Peer { get; }
		}
	}
}
