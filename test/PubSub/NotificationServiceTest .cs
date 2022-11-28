namespace PeerTalk.PubSub;

using Ipfs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PeerTalk.Protocols;
using PeerTalk.Transports;
using PeerTalkTests;
using SharedCode.Notifications;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

[TestClass]
public class NotificationServiceTest
{
	private readonly Peer self = new()
	{
		AgentVersion = "self",
		Id = "QmXK9VBxaXFuuT29AaPUTgW3jBWZ9JgLVZYdMYTHC6LLAH",
		PublicKey = "CAASXjBcMA0GCSqGSIb3DQEBAQUAA0sAMEgCQQCC5r4nQBtnd9qgjnG8fBN5+gnqIeWEIcUFUdCG4su/vrbQ1py8XGKNUBuDjkyTv25Gd3hlrtNJV3eOKZVSL8ePAgMBAAE="
	};
	private readonly Peer other1 = new() { Id = "QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ" };
	private readonly Peer other2 = new() { Id = "QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvUJ" };

	[TestMethod]
	public async Task MessageID_Increments()
	{
		var notificationService = new SharedCode.Notifications.NotificationService();
		var ns = new NotificationService(Mock.Of<ILogger<NotificationService>>(), notificationService) { LocalPeer = self };
		await ns.StartAsync();
		try
		{
			var a = ns.CreateMessage("topic", Array.Empty<byte>());
			var b = ns.CreateMessage("topic", Array.Empty<byte>());
			Assert.IsTrue(b.MessageId.CompareTo(a.MessageId) > 0);
		}
		finally
		{
			await ns.StopAsync();
		}
	}

	[TestMethod]
	public async Task Publish()
	{
		var notificationService = new SharedCode.Notifications.NotificationService();
		var ns = new NotificationService(Mock.Of<ILogger<NotificationService>>(), notificationService) { LocalPeer = self };
		await ns.StartAsync();
		try
		{
			await ns.PublishAsync("topic", "foo");
			await ns.PublishAsync("topic", new byte[] { 1, 2, 3 });
			await ns.PublishAsync("topic", new MemoryStream(new byte[] { 1, 2, 3 }));
			Assert.AreEqual(3ul, ns.MesssagesPublished);
			Assert.AreEqual(3ul, ns.MesssagesReceived);
		}
		finally
		{
			await ns.StopAsync();
		}
	}

	[TestMethod]
	public async Task Topics()
	{
		var notificationService = new SharedCode.Notifications.NotificationService();
		var ns = new NotificationService(Mock.Of<ILogger<NotificationService>>(), notificationService) { LocalPeer = self };
		await ns.StartAsync();
		try
		{
			var topicA = Guid.NewGuid().ToString();
			var topicB = Guid.NewGuid().ToString();
			var csA = new CancellationTokenSource();
			var csB = new CancellationTokenSource();

			await ns.SubscribeAsync(topicA, msg => { }, csA.Token);
			await ns.SubscribeAsync(topicA, msg => { }, csA.Token);
			await ns.SubscribeAsync(topicB, msg => { }, csB.Token);

			var topics = (await ns.SubscribedTopicsAsync()).ToArray();
			Assert.AreEqual(2, topics.Length);
			CollectionAssert.Contains(topics, topicA);
			CollectionAssert.Contains(topics, topicB);

			csA.Cancel();
			topics = (await ns.SubscribedTopicsAsync()).ToArray();
			Assert.AreEqual(1, topics.Length);
			CollectionAssert.Contains(topics, topicB);

			csB.Cancel();
			topics = (await ns.SubscribedTopicsAsync()).ToArray();
			Assert.AreEqual(0, topics.Length);
		}
		finally
		{
			await ns.StopAsync();
		}
	}

	[TestMethod]
	public async Task Subscribe()
	{
		var notificationService = new SharedCode.Notifications.NotificationService();
		var ns = new NotificationService(Mock.Of<ILogger<NotificationService>>(), notificationService) { LocalPeer = self };
		await ns.StartAsync();
		try
		{
			var topic = Guid.NewGuid().ToString();
			var cs = new CancellationTokenSource();
			int messageCount = 0;
			await ns.SubscribeAsync(topic, msg => ++messageCount, cs.Token);
			await ns.SubscribeAsync(topic, msg => ++messageCount, cs.Token);

			await ns.PublishAsync(topic, "");
			Assert.AreEqual(2, messageCount);
		}
		finally
		{
			await ns.StopAsync();
		}
	}

	[TestMethod]
	public async Task Subscribe_HandlerExceptionIsIgnored()
	{
		var notificationService = new SharedCode.Notifications.NotificationService();
		var ns = new NotificationService(Mock.Of<ILogger<NotificationService>>(), notificationService) { LocalPeer = self };
		await ns.StartAsync();
		try
		{
			var topic = Guid.NewGuid().ToString();
			var cs = new CancellationTokenSource();
			int messageCount = 0;
			await ns.SubscribeAsync(topic, msg => { ++messageCount; throw new Exception(); }, cs.Token);

			await ns.PublishAsync(topic, "");
			Assert.AreEqual(1, messageCount);
		}
		finally
		{
			await ns.StopAsync();
		}
	}

	[TestMethod]
	public async Task DuplicateMessagesAreIgnored()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var ns = sp.GetRequiredService<NotificationService>();
		ns.LocalPeer = self;
		ns.Routers.Add(sp.GetRequiredService<LoopbackRouter>());
		await ns.StartAsync();
		try
		{
			var topic = Guid.NewGuid().ToString();
			var cs = new CancellationTokenSource();
			int messageCount = 0;
			await ns.SubscribeAsync(topic, msg => ++messageCount, cs.Token);

			await ns.PublishAsync(topic, "");
			Assert.AreEqual(1, messageCount);
			Assert.AreEqual(2ul, ns.MesssagesReceived);
			Assert.AreEqual(1ul, ns.DuplicateMesssagesReceived);
		}
		finally
		{
			await ns.StopAsync();
		}
	}

	[TestMethod]
	public async Task SubscribedPeers_ForTopic()
	{
		var topic1 = Guid.NewGuid().ToString();
		var topic2 = Guid.NewGuid().ToString();
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var ns = new NotificationService(sp.GetRequiredService<ILogger<NotificationService>>(), notificationService) { LocalPeer = self };
		var router = new FloodRouter(
			sp.GetRequiredService<ILogger<FloodRouter>>(),
			notificationService)
		{
			Swarm = new Swarm(
				loggerFactory,
				message,
				notificationService,
				protocolRegistry,
				transportRegistry)
		};
		router.RemoteTopics.AddInterest(topic1, other1);
		router.RemoteTopics.AddInterest(topic2, other2);
		ns.Routers.Add(router);
		await ns.StartAsync();
		try
		{
			var peers = (await ns.PeersAsync(topic1)).ToArray();
			Assert.AreEqual(1, peers.Length);
			Assert.AreEqual(other1, peers[0]);

			peers = (await ns.PeersAsync(topic2)).ToArray();
			Assert.AreEqual(1, peers.Length);
			Assert.AreEqual(other2, peers[0]);
		}
		finally
		{
			await ns.StopAsync();
		}
	}

	[TestMethod]
	public async Task SubscribedPeers_AllTopics()
	{
		var topic1 = Guid.NewGuid().ToString();
		var topic2 = Guid.NewGuid().ToString();
		var sp = TestSetup.GetScopedServiceProvider();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var ns = new NotificationService(sp.GetRequiredService<ILogger<NotificationService>>(), notificationService) { LocalPeer = self };
		var router = new FloodRouter(
			sp.GetRequiredService<ILogger<FloodRouter>>(),
			notificationService)
		{
			Swarm = sp.GetRequiredService<Swarm>()
		};
		router.RemoteTopics.AddInterest(topic1, other1);
		router.RemoteTopics.AddInterest(topic2, other2);
		ns.Routers.Add(router);
		await ns.StartAsync();
		try
		{
			var peers = (await ns.PeersAsync(null)).ToArray();
			Assert.AreEqual(2, peers.Length);
		}
		finally
		{
			await ns.StopAsync();
		}
	}
}
