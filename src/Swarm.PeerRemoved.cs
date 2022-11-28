namespace PeerTalk
{
	using Ipfs;
	using SharedCode.Notifications;

	public partial class Swarm
	{
		/// <summary>
		///   Raised when a peer should no longer be used.
		/// </summary>
		/// <remarks>
		///   This event indicates that the peer has been removed
		///   from the <see cref="KnownPeers"/> and should no longer
		///   be used.
		/// </remarks>
		public class PeerRemoved : Notification
		{
			/// <summary>
			/// Initializes a new instance of the <see cref="PeerRemoved"/> class.
			/// </summary>
			/// <param name="peer">The peer.</param>
			public PeerRemoved(Peer peer) => this.Peer = peer;

			/// <summary>
			/// Gets the peer.
			/// </summary>
			/// <value>The peer.</value>
			public Peer Peer { get; }
		}
	}
}
