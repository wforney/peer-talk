namespace PeerTalk
{
	using SharedCode.Notifications;

	public partial class Swarm
	{
		/// <summary>
		///   Raised when a connection to another peer is established.
		/// </summary>
		public class ConnectionEstablished : Notification
		{
			/// <summary>
			/// Initializes a new instance of the <see cref="ConnectionEstablished"/> class.
			/// </summary>
			/// <param name="peerConnection">The peer connection.</param>
			public ConnectionEstablished(PeerConnection peerConnection) => this.PeerConnection = peerConnection;

			/// <summary>
			/// Gets the peer connection.
			/// </summary>
			/// <value>The peer connection.</value>
			public PeerConnection PeerConnection { get; }
		}
	}
}
