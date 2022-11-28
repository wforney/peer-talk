namespace PeerTalk.PubSub
{
	using Ipfs;
	using System;

	public partial class NotificationService
	{
		private class TopicHandler
		{
			public string Topic;
			public Action<IPublishedMessage> Handler;
		}
	}
}
