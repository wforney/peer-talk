namespace PeerTalk.PubSub
{
	using Ipfs;
	using SharedCode.Notifications;
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	///   A message router that always raises <see cref="MessageReceived"/>
	///   when a message is published.
	/// </summary>
	/// <remarks>
	///   The allows the <see cref="NotificationService"/> to invoke the
	///   local subscribtion handlers.
	/// </remarks>
	public class LoopbackRouter : IMessageRouter
	{
		private readonly MessageTracker tracker = new MessageTracker();
		private readonly INotificationService _notificationService;

		/// <summary>
		/// Initializes a new instance of the <see cref="LoopbackRouter"/> class.
		/// </summary>
		/// <param name="notificationService">The notification service.</param>
		/// <exception cref="ArgumentNullException">notificationService</exception>
		public LoopbackRouter(INotificationService notificationService) => _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

		/// <inheritdoc />
		public IEnumerable<Peer> InterestedPeers(string topic) => Enumerable.Empty<Peer>();

		/// <inheritdoc />
		public Task JoinTopicAsync(string topic, CancellationToken cancel) => Task.CompletedTask;

		/// <inheritdoc />
		public Task LeaveTopicAsync(string topic, CancellationToken cancel) => Task.CompletedTask;

		/// <inheritdoc />
		public Task PublishAsync(PublishedMessage message, CancellationToken cancel)
		{
			cancel.ThrowIfCancellationRequested();

			if (!tracker.RecentlySeen(message.MessageId))
			{
				_notificationService.Publish(new MessageReceived(this, message));
			}

			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public Task StartAsync() => Task.CompletedTask;

		/// <inheritdoc />
		public Task StopAsync() => Task.CompletedTask;
	}
}
