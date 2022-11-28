namespace PeerTalk
{
	using Ipfs;
	using SharedCode.Notifications;

	public partial class Swarm
	{
		/// <summary>
		///   Raised when a peer's connection is closed.
		/// </summary>
		public class PeerDisconnected : Notification
		{
			/// <summary>
			/// Initializes a new instance of the <see cref="PeerDisconnected"/> class.
			/// </summary>
			/// <param name="peer">The peer.</param>
			public PeerDisconnected(Peer peer) => this.Peer = peer;

			/// <summary>
			/// Gets the peer.
			/// </summary>
			/// <value>The peer.</value>
			public Peer Peer { get; }
		}
	}
}
