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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

[TestClass]
public class FloodRouterTest
{
	private readonly Peer self = new()
	{
		AgentVersion = "self",
		Id = "QmXK9VBxaXFuuT29AaPUTgW3jBWZ9JgLVZYdMYTHC6LLAH",
		PublicKey = "CAASXjBcMA0GCSqGSIb3DQEBAQUAA0sAMEgCQQCC5r4nQBtnd9qgjnG8fBN5+gnqIeWEIcUFUdCG4su/vrbQ1py8XGKNUBuDjkyTv25Gd3hlrtNJV3eOKZVSL8ePAgMBAAE="
	};
	private readonly Peer other = new()
	{
		AgentVersion = "other",
		Id = "QmXFX2P5ammdmXQgfqGkfswtEVFsZUJ5KeHRXQYCTdiTAb",
		PublicKey = "CAASpgIwggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQCfBYU9c0n28u02N/XCJY8yIsRqRVO5Zw+6kDHCremt2flHT4AaWnwGLAG9YyQJbRTvWN9nW2LK7Pv3uoIlvUSTnZEP0SXB5oZeqtxUdi6tuvcyqTIfsUSanLQucYITq8Qw3IMBzk+KpWNm98g9A/Xy30MkUS8mrBIO9pHmIZa55fvclDkTvLxjnGWA2avaBfJvHgMSTu0D2CQcmJrvwyKMhLCSIbQewZd2V7vc6gtxbRovKlrIwDTmDBXbfjbLljOuzg2yBLyYxXlozO9blpttbnOpU4kTspUVJXglmjsv7YSIJS3UKt3544l/srHbqlwC5CgOgjlwNfYPadO8kmBfAgMBAAE="
	};
	private readonly Peer other1 = new()
	{
		AgentVersion = "other1",
		Id = "QmYSj5nkpHaJG6hDof33fv3YHnQfpFTNAd8jZ5GssgPygn",
		PublicKey = "CAASpgIwggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQC8s23axzV5S/fUoJ1+MT9fH1SzlDwqwdKoIirYAmvHnv6dyoaC7gMeHDJIc2gNnrvpdAoXyPxBS2Oysv/iHzseVi2kYvyU9pD5ZtiorzpV5oOXMfIfgGygXbiIk/DVQWD6Sq8flHY8ht+z69h9JL+Dj/aMfEzY5RoznJkikumoCn7QI6zvPZd9OPd7OyqcCZ31RThtIxrFd0YkHN+VV9pCq4iBfhMt8Ocy0RS/yrqaGE4PX2VsjExBmShEFnTFlhy0Mh4QhBLLquQH0aQEk2s5mZtwh7bKeW84zC0zIGWzcHrwVsHb+Z2/IXDTWNIlNGc/cCV7vAM1EgK1oQVf04NLAgMBAAE=",
	};

	[TestMethod]
	public void Defaults()
	{
		var notificationService = new SharedCode.Notifications.NotificationService();
		var router = new FloodRouter(Mock.Of<ILogger<FloodRouter>>(), notificationService);
		Assert.AreEqual("/floodsub/1.0.0", router.ToString());
	}

	[TestMethod]
	public void RemoteSubscriptions()
	{
		var notificationService = new SharedCode.Notifications.NotificationService();
		var router = new FloodRouter(Mock.Of<ILogger<FloodRouter>>(), notificationService);

		var sub = new Subscription { Topic = "topic", Subscribe = true };
		router.ProcessSubscription(sub, other);
		Assert.AreEqual(1, router.RemoteTopics.GetPeers("topic").Count());

		var can = new Subscription { Topic = "topic", Subscribe = false };
		router.ProcessSubscription(can, other);
		Assert.AreEqual(0, router.RemoteTopics.GetPeers("topic").Count());
	}

	[TestMethod]
	public async Task Sends_Hello_OnConnect()
	{
		var topic = Guid.NewGuid().ToString();

		var sp = TestSetup.GetScopedServiceProvider();
		var swarm1 = sp.GetRequiredService<Swarm>();
		swarm1.LocalPeer = self;
		var router1 = sp.GetRequiredService<FloodRouter>();
		router1.Swarm = swarm1;
		var ns1 = sp.GetRequiredService<NotificationService>();
		ns1.LocalPeer = self;
		ns1.Routers.Add(router1);
		await swarm1.StartAsync();
		await ns1.StartAsync();

		var sp2 = TestSetup.GetScopedServiceProvider();
		var swarm2 = sp2.GetRequiredService<Swarm>();
		swarm2.LocalPeer = other;
		var router2 = sp2.GetRequiredService<FloodRouter>();
		router2.Swarm = swarm2;
		var ns2 = sp2.GetRequiredService<NotificationService>();
		ns2.LocalPeer = other;
		ns2.Routers.Add(router2);
		await swarm2.StartAsync();
		await ns2.StartAsync();

		try
		{
			_ = await swarm1.StartListeningAsync("/ip4/127.0.0.1/tcp/0");
			_ = await swarm2.StartListeningAsync("/ip4/127.0.0.1/tcp/0");

			var cs = new CancellationTokenSource();
			await ns1.SubscribeAsync(topic, msg => { }, cs.Token);
			_ = await swarm1.ConnectAsync(other);

			Peer[] peers = Array.Empty<Peer>();
			var endTime = DateTime.Now.AddSeconds(3);
			while (peers.Length == 0)
			{
				if (DateTime.Now > endTime)
					Assert.Fail("timeout");
				await Task.Delay(100);
				peers = (await ns2.PeersAsync(topic)).ToArray();
			}

			CollectionAssert.Contains(peers, self);
		}
		finally
		{
			await swarm1.StopAsync();
			await ns1.StopAsync();

			await swarm2.StopAsync();
			await ns2.StopAsync();
		}
	}

	[TestMethod]
	public async Task Sends_NewSubscription()
	{
		var topic = Guid.NewGuid().ToString();

		var sp = TestSetup.GetScopedServiceProvider();
		var swarm1 = sp.GetRequiredService<Swarm>();
		swarm1.LocalPeer = self;
		var router1 = sp.GetRequiredService<FloodRouter>();
		router1.Swarm = swarm1;
		var ns1 = sp.GetRequiredService<NotificationService>();
		ns1.LocalPeer = self;
		ns1.Routers.Add(router1);
		await swarm1.StartAsync();
		await ns1.StartAsync();

		var sp2 = TestSetup.GetScopedServiceProvider();
		var swarm2 = sp2.GetRequiredService<Swarm>();
		swarm2.LocalPeer = other;
		var router2 = sp2.GetRequiredService<FloodRouter>();
		router2.Swarm = swarm2;
		var ns2 = sp2.GetRequiredService<NotificationService>();
		ns2.LocalPeer = other;
		ns2.Routers.Add(router2);
		await swarm2.StartAsync();
		await ns2.StartAsync();

		try
		{
			_ = await swarm1.StartListeningAsync("/ip4/127.0.0.1/tcp/0");
			_ = await swarm2.StartListeningAsync("/ip4/127.0.0.1/tcp/0");

			var cs = new CancellationTokenSource();
			_ = await swarm1.ConnectAsync(other);
			await ns1.SubscribeAsync(topic, msg => { }, cs.Token);

			Peer[] peers = Array.Empty<Peer>();
			var endTime = DateTime.Now.AddSeconds(3);
			while (peers.Length == 0)
			{
				if (DateTime.Now > endTime)
					Assert.Fail("timeout");
				await Task.Delay(100);
				peers = (await ns2.PeersAsync(topic)).ToArray();
			}

			CollectionAssert.Contains(peers, self);
		}
		finally
		{
			await swarm1.StopAsync();
			await ns1.StopAsync();

			await swarm2.StopAsync();
			await ns2.StopAsync();
		}
	}

	[TestMethod]
	public async Task Sends_CancelledSubscription()
	{
		var topic = Guid.NewGuid().ToString();

		var sp = TestSetup.GetScopedServiceProvider();
		var sp2 = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var loggerFactory2 = sp2.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var message2 = sp2.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var notificationService2 = sp2.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var protocolRegistry2 = sp2.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var transportRegistry2 = sp2.GetRequiredService<TransportRegistry>();

		var swarm1 = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		var router1 = new FloodRouter(loggerFactory.CreateLogger<FloodRouter>(), notificationService) { Swarm = swarm1 };
		var ns1 = new NotificationService(loggerFactory.CreateLogger<NotificationService>(), notificationService) { LocalPeer = self };
		ns1.Routers.Add(router1);
		await swarm1.StartAsync();
		await ns1.StartAsync();

		var swarm2 = new Swarm(loggerFactory2, message2, notificationService2, protocolRegistry2, transportRegistry2) { LocalPeer = other };
		var router2 = new FloodRouter(loggerFactory2.CreateLogger<FloodRouter>(), notificationService2) { Swarm = swarm2 };
		var ns2 = new NotificationService(loggerFactory2.CreateLogger<NotificationService>(), notificationService2) { LocalPeer = other };
		ns2.Routers.Add(router2);
		await swarm2.StartAsync();
		await ns2.StartAsync();

		try
		{
			_ = await swarm1.StartListeningAsync("/ip4/127.0.0.1/tcp/0");
			_ = await swarm2.StartListeningAsync("/ip4/127.0.0.1/tcp/0");

			var cs = new CancellationTokenSource();
			_ = await swarm1.ConnectAsync(other);
			await ns1.SubscribeAsync(topic, msg => { }, cs.Token);

			Peer[] peers = Array.Empty<Peer>();
			var endTime = DateTime.Now.AddSeconds(3);
			while (peers.Length == 0)
			{
				if (DateTime.Now > endTime)
					Assert.Fail("timeout");
				await Task.Delay(100);
				peers = (await ns2.PeersAsync(topic)).ToArray();
			}

			CollectionAssert.Contains(peers, self);

			cs.Cancel();
			peers = Array.Empty<Peer>();
			endTime = DateTime.Now.AddSeconds(3);
			while (peers.Length != 0)
			{
				if (DateTime.Now > endTime)
				{
					Assert.Fail("timeout");
				}

				await Task.Delay(100);
				peers = (await ns2.PeersAsync(topic)).ToArray();
			}
		}
		finally
		{
			await swarm1.StopAsync();
			await ns1.StopAsync();

			await swarm2.StopAsync();
			await ns2.StopAsync();
		}
	}

	[TestMethod]
	public async Task Relays_PublishedMessage()
	{
		var topic = Guid.NewGuid().ToString();

		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();

		var swarm1 = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		var router1 = new FloodRouter(sp.GetRequiredService<ILogger<FloodRouter>>(), notificationService) { Swarm = swarm1 };
		var ns1 = new NotificationService(sp.GetRequiredService<ILogger<NotificationService>>(), notificationService) { LocalPeer = self };
		ns1.Routers.Add(router1);
		await swarm1.StartAsync();
		await ns1.StartAsync();

		var swarm2 = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = other };
		var router2 = new FloodRouter(sp.GetRequiredService<ILogger<FloodRouter>>(), notificationService) { Swarm = swarm2 };
		var ns2 = new NotificationService(sp.GetRequiredService<ILogger<NotificationService>>(), notificationService) { LocalPeer = other };
		ns2.Routers.Add(router2);
		await swarm2.StartAsync();
		await ns2.StartAsync();

		var swarm3 = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = other1 };
		var router3 = new FloodRouter(sp.GetRequiredService<ILogger<FloodRouter>>(), notificationService) { Swarm = swarm3 };
		var ns3 = new NotificationService(sp.GetRequiredService<ILogger<NotificationService>>(), notificationService) { LocalPeer = other1 };
		ns3.Routers.Add(router3);
		await swarm3.StartAsync();
		await ns3.StartAsync();

		try
		{
			IPublishedMessage lastMessage2 = null;
			IPublishedMessage lastMessage3 = null;
			_ = await swarm1.StartListeningAsync("/ip4/127.0.0.1/tcp/0");
			_ = await swarm2.StartListeningAsync("/ip4/127.0.0.1/tcp/0");
			_ = await swarm3.StartListeningAsync("/ip4/127.0.0.1/tcp/0");

			var cs = new CancellationTokenSource();
			await ns2.SubscribeAsync(topic, msg => lastMessage2 = msg, cs.Token);
			await ns3.SubscribeAsync(topic, msg => lastMessage3 = msg, cs.Token);
			_ = await swarm1.ConnectAsync(other);
			_ = await swarm3.ConnectAsync(other);

			Peer[] peers = Array.Empty<Peer>();
			var endTime = DateTime.Now.AddSeconds(3);
			while (peers.Length == 0)
			{
				if (DateTime.Now > endTime)
					Assert.Fail("timeout");
				await Task.Delay(100);
				peers = (await ns2.PeersAsync(topic)).ToArray();
			}

			CollectionAssert.Contains(peers, other1);

			await ns1.PublishAsync(topic, new byte[] { 1 });
			endTime = DateTime.Now.AddSeconds(3);
			while (lastMessage2 == null || lastMessage3 == null)
			{
				if (DateTime.Now > endTime)
					Assert.Fail("timeout");
				await Task.Delay(100);
			}

			Assert.IsNotNull(lastMessage2);
			Assert.AreEqual(self, lastMessage2.Sender);
			CollectionAssert.AreEqual(new byte[] { 1 }, lastMessage2.DataBytes);
			CollectionAssert.Contains(lastMessage2.Topics.ToArray(), topic);

			Assert.IsNotNull(lastMessage3);
			Assert.AreEqual(self, lastMessage3.Sender);
			CollectionAssert.AreEqual(new byte[] { 1 }, lastMessage3.DataBytes);
			CollectionAssert.Contains(lastMessage3.Topics.ToArray(), topic);
		}
		finally
		{
			await swarm1.StopAsync();
			await ns1.StopAsync();

			await swarm2.StopAsync();
			await ns2.StopAsync();

			await swarm3.StopAsync();
			await ns3.StopAsync();
		}
	}
}
