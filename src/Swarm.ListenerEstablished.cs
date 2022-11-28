namespace PeerTalk
{
	using Ipfs;
	using SharedCode.Notifications;

	public partial class Swarm
	{
		/// <summary>
		///   Raised when a listener is establihed.
		/// </summary>
		/// <remarks>
		///   Raised when <see cref="StartListeningAsync(MultiAddress)"/>
		///   succeeds.
		/// </remarks>
		public class ListenerEstablished : Notification
		{
			/// <summary>
			/// Initializes a new instance of the <see cref="ListenerEstablished"/> class.
			/// </summary>
			/// <param name="peer">The peer.</param>
			public ListenerEstablished(Peer peer) => this.Peer = peer;

			/// <summary>
			/// Gets the peer.
			/// </summary>
			/// <value>The peer.</value>
			public Peer Peer { get; }
		}
	}
}
