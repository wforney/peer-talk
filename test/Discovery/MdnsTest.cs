namespace PeerTalk.Discovery;

using Ipfs;
using Makaretu.Dns;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SharedCode.Notifications;
using System;
using System.Threading;
using System.Threading.Tasks;

[TestClass]
public class MdnsTest
{
	[TestMethod]
	public async Task DiscoveryNext()
	{
		var serviceName = $"_{Guid.NewGuid()}._udp";
		var peer1 = new Peer
		{
			Id = "QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ",
			Addresses = new MultiAddress[] { "/ip4/104.131.131.82/tcp/4001/ipfs/QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ" }
		};
		var peer2 = new Peer
		{
			Id = "QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuK",
			Addresses = new MultiAddress[] { "/ip4/104.131.131.82/tcp/4001/ipfs/QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuK" }
		};
		var done = new ManualResetEvent(false);
		var logger = Mock.Of<ILogger<MdnsNext>>();
		var notificationService = new NotificationService();
		var mdns1 = new MdnsNext(logger, notificationService)
		{
			MulticastService = new MulticastService(),
			ServiceName = serviceName,
			LocalPeer = peer1
		};
		var mdns2 = new MdnsNext(logger, notificationService)
		{
			MulticastService = new MulticastService(),
			ServiceName = serviceName,
			LocalPeer = peer2
		};
		var sub = notificationService.Subscribe<PeerDiscovered>(m =>
		{
			if (m.Peer.Id == peer2.Id)
			{
				_ = done.Set();
			}
		});
		await mdns1.StartAsync();
		mdns1.MulticastService.Start();
		await mdns2.StartAsync();
		mdns2.MulticastService.Start();
		try
		{
			Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(2)), "timeout");
		}
		finally
		{
			await mdns1.StopAsync();
			await mdns2.StopAsync();
			mdns1.MulticastService.Stop();
			mdns2.MulticastService.Stop();
			sub.Dispose();
		}
	}

	[TestMethod]
	public async Task DiscoveryJs()
	{
		var serviceName = $"_{Guid.NewGuid()}._udp";
		serviceName = "_foo._udp";
		var peer1 = new Peer
		{
			Id = "QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ",
			Addresses = new MultiAddress[] { "/ip4/104.131.131.82/tcp/4001/ipfs/QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ" }
		};
		var peer2 = new Peer
		{
			Id = "QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuK",
			Addresses = new MultiAddress[] { "/ip4/104.131.131.82/tcp/4001/ipfs/QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuK" }
		};
		var done = new ManualResetEvent(false);
		var logger = Mock.Of<ILogger<MdnsJs>>();
		var notificationService = new NotificationService();
		var mdns1 = new MdnsJs(logger, notificationService)
		{
			MulticastService = new MulticastService(),
			ServiceName = serviceName,
			LocalPeer = peer1
		};
		var mdns2 = new MdnsJs(logger, notificationService)
		{
			MulticastService = new MulticastService(),
			ServiceName = serviceName,
			LocalPeer = peer2
		};
		var sub = notificationService.Subscribe<PeerDiscovered>(m =>
		{
			if (m.Peer.Id == peer2.Id)
			{
				_ = done.Set();
			}
		});
		await mdns1.StartAsync();
		mdns1.MulticastService.Start();
		await mdns2.StartAsync();
		mdns2.MulticastService.Start();
		try
		{
			Assert.IsTrue(done.WaitOne(TimeSpan.FromSeconds(2)), "timeout");
		}
		finally
		{
			await mdns1.StopAsync();
			await mdns2.StopAsync();
			mdns1.MulticastService.Stop();
			mdns2.MulticastService.Stop();
			sub.Dispose();
		}
	}

	[TestMethod]
	public void SafeDnsLabel()
	{
		Assert.AreEqual("a", MdnsNext.SafeLabel("a", 2));
		Assert.AreEqual("ab", MdnsNext.SafeLabel("ab", 2));
		Assert.AreEqual("ab.c", MdnsNext.SafeLabel("abc", 2));
		Assert.AreEqual("ab.cd", MdnsNext.SafeLabel("abcd", 2));
		Assert.AreEqual("ab.cd.e", MdnsNext.SafeLabel("abcde", 2));
		Assert.AreEqual("ab.cd.ef", MdnsNext.SafeLabel("abcdef", 2));
		Assert.AreEqual("ab.cd.ef.g", MdnsNext.SafeLabel("abcdefg", 2));
	}
}
