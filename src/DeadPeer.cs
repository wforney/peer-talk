namespace PeerTalk
{
	using Ipfs;
	using System;

	/// <summary>
	///   Information on a peer that is not reachable.
	/// </summary>
	public class DeadPeer
	{
		/// <summary>
		///   The peer that does not respond.
		/// </summary>
		public Peer Peer { get; set; }

		/// <summary>
		///   How long to wait before attempting another connect.
		/// </summary>
		public TimeSpan Backoff { get; set; }

		/// <summary>
		///   When another connect should be tried.
		/// </summary>
		public DateTime NextAttempt { get; set; }
	}
}
