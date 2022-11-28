namespace PeerTalk.PubSub
{
	using SharedCode.Notifications;

	/// <inheritdoc />
	public class MessageReceived : Notification
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="MessageReceived" /> class.
		/// </summary>
		/// <param name="messageRouter">The message router.</param>
		/// <param name="publishedMessage">The published message.</param>
		public MessageReceived(IMessageRouter messageRouter, PublishedMessage publishedMessage)
		{
			this.MessageRouter = messageRouter;
			this.PublishedMessage = publishedMessage;
		}

		/// <summary>
		/// Gets the message router.
		/// </summary>
		/// <value>The message router.</value>
		public IMessageRouter MessageRouter { get; }

		/// <summary>
		/// Gets the published message.
		/// </summary>
		/// <value>The published message.</value>
		public PublishedMessage PublishedMessage { get; }
	}
}
