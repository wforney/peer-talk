namespace PeerTalk.Routing
{
	using Ipfs;
	using Makaretu.Collections;

	internal class RoutingPeer : IContact
	{
		public Peer Peer;

		public RoutingPeer(Peer peer)
		{
			Peer = peer;
		}

		public byte[] Id => RoutingTable.Key(Peer.Id);
	}
}
