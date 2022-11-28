﻿namespace PeerTalk;

using Ipfs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PeerTalk.Protocols;
using PeerTalk.Transports;
using PeerTalkTests;
using SharedCode.Notifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

[TestClass]
public class SwarmTest
{
	private readonly MultiAddress mars = "/ip4/10.1.10.10/tcp/29087/ipfs/QmSoLMeWqB7YGVLJN3pNLQpmmEk35v6wYtsMGLzSr5QBU3";
	private readonly MultiAddress venus = "/ip4/104.236.76.40/tcp/4001/ipfs/QmSoLV4Bbm51jM9C4gDYZQ9Cy3U6aXMJDAbzgu2fzaDs64";
	private readonly MultiAddress earth = "/ip4/178.62.158.247/tcp/4001/ipfs/QmSoLer265NRgSp2LA3dPaeykiS1J6DifTC88f5uVQKNAd";
	private readonly Peer self = new()
	{
		AgentVersion = "self",
		Id = "QmXK9VBxaXFuuT29AaPUTgW3jBWZ9JgLVZYdMYTHC6LLAH",
		PublicKey = "CAASXjBcMA0GCSqGSIb3DQEBAQUAA0sAMEgCQQCC5r4nQBtnd9qgjnG8fBN5+gnqIeWEIcUFUdCG4su/vrbQ1py8XGKNUBuDjkyTv25Gd3hlrtNJV3eOKZVSL8ePAgMBAAE="
	};
	private readonly Peer other = new()
	{
		AgentVersion = "other",
		Id = "QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h",
		PublicKey = "CAASXjBcMA0GCSqGSIb3DQEBAQUAA0sAMEgCQQDlTSgVLprWaXfmxDr92DJE1FP0wOexhulPqXSTsNh5ot6j+UiuMgwb0shSPKzLx9AuTolCGhnwpTBYHVhFoBErAgMBAAE="
	};

	[TestMethod]
	public async Task Start_Stop()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(
			loggerFactory,
			message,
			notificationService,
			protocolRegistry,
			transportRegistry)
		{
			LocalPeer = self
		};
		await swarm.StartAsync();
		await swarm.StopAsync();
	}

	[TestMethod]
	public void Start_NoLocalPeer()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry);
		ExceptionAssert.Throws<NotSupportedException>(() => swarm.StartAsync().Wait());
	}

	[TestMethod]
	public void NewPeerAddress()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		swarm.RegisterPeerAddress(mars);
		Assert.IsTrue(swarm.KnownPeerAddresses.Contains(mars));
	}

	[TestMethod]
	public void NewPeerAddress_Self()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		var selfAddress = "/ip4/178.62.158.247/tcp/4001/ipfs/" + self.Id;
		ExceptionAssert.Throws<Exception>(() =>
		{
			var _ = swarm.RegisterPeerAddress(selfAddress);
		});

		selfAddress = "/ip4/178.62.158.247/tcp/4001/p2p/" + self.Id;
		ExceptionAssert.Throws<Exception>(() =>
		{
			var _ = swarm.RegisterPeerAddress(selfAddress);
		});
	}

	[TestMethod]
	public void NewPeerAddress_BlackList()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		swarm.BlackList.Add(mars);

		ExceptionAssert.Throws<Exception>(() =>
		{
			var _ = swarm.RegisterPeerAddress(mars);
		});
		Assert.IsFalse(swarm.KnownPeerAddresses.Contains(mars));

		Assert.IsNotNull(swarm.RegisterPeerAddress(venus));
		Assert.IsTrue(swarm.KnownPeerAddresses.Contains(venus));
	}

	[TestMethod]
	public void NewPeerAddress_WhiteList()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		swarm.WhiteList.Add(venus);

		ExceptionAssert.Throws<Exception>(() =>
		{
			var _ = swarm.RegisterPeerAddress(mars);
		});
		Assert.IsFalse(swarm.KnownPeerAddresses.Contains(mars));

		Assert.IsNotNull(swarm.RegisterPeerAddress(venus));
		Assert.IsTrue(swarm.KnownPeerAddresses.Contains(venus));
	}

	[TestMethod]
	public void NewPeerAddress_InvalidAddress_MissingPeerId()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		ExceptionAssert.Throws<Exception>(() =>
		{
			var _ = swarm.RegisterPeerAddress("/ip4/10.1.10.10/tcp/29087");
		});
		Assert.AreEqual(0, swarm.KnownPeerAddresses.Count());
	}

	[TestMethod]
	public void NewPeerAddress_Duplicate()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		swarm.RegisterPeerAddress(mars);
		Assert.AreEqual(1, swarm.KnownPeerAddresses.Count());

		swarm.RegisterPeerAddress(mars);
		Assert.AreEqual(1, swarm.KnownPeerAddresses.Count());
	}

	[TestMethod]
	public void KnownPeers()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		Assert.AreEqual(0, swarm.KnownPeers.Count());
		Assert.AreEqual(0, swarm.KnownPeerAddresses.Count());

		swarm.RegisterPeerAddress("/ip4/10.1.10.10/tcp/29087/ipfs/QmSoLMeWqB7YGVLJN3pNLQpmmEk35v6wYtsMGLzSr5QBU3");
		Assert.AreEqual(1, swarm.KnownPeers.Count());
		Assert.AreEqual(1, swarm.KnownPeerAddresses.Count());

		swarm.RegisterPeerAddress("/ip4/10.1.10.11/tcp/29087/p2p/QmSoLMeWqB7YGVLJN3pNLQpmmEk35v6wYtsMGLzSr5QBU3");
		Assert.AreEqual(1, swarm.KnownPeers.Count());
		Assert.AreEqual(2, swarm.KnownPeerAddresses.Count());

		swarm.RegisterPeerAddress(venus);
		Assert.AreEqual(2, swarm.KnownPeers.Count());
		Assert.AreEqual(3, swarm.KnownPeerAddresses.Count());
	}

	[TestMethod]
	public async Task Connect_Disconnect()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var spB = TestSetup.GetScopedServiceProvider();
		var peerB = new Peer
		{
			AgentVersion = "peerB",
			Id = "QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h",
			PublicKey = "CAASXjBcMA0GCSqGSIb3DQEBAQUAA0sAMEgCQQDlTSgVLprWaXfmxDr92DJE1FP0wOexhulPqXSTsNh5ot6j+UiuMgwb0shSPKzLx9AuTolCGhnwpTBYHVhFoBErAgMBAAE="
		};
		var swarmB = spB.GetRequiredService<Swarm>();
		swarmB.LocalPeer = peerB;
		await swarmB.StartAsync();
		var peerBAddress = await swarmB.StartListeningAsync("/ip4/127.0.0.1/tcp/0");
		Assert.IsTrue(peerB.Addresses.Any());

		var swarm = sp.GetRequiredService<Swarm>();
		swarm.LocalPeer = self;
		await swarm.StartAsync();
		try
		{
			var remotePeer = (await swarm.ConnectAsync(peerBAddress)).RemotePeer;
			Assert.IsNotNull(remotePeer.ConnectedAddress);
			Assert.AreEqual(peerB.PublicKey, remotePeer.PublicKey);
			Assert.IsTrue(remotePeer.IsValid());
			Assert.IsTrue(swarm.KnownPeers.Contains(peerB));

			// wait for swarmB to settle
			var endTime = DateTime.Now.AddSeconds(3);
			while (true)
			{
				if (DateTime.Now > endTime)
					Assert.Fail("swarmB does not know about self");
				if (swarmB.KnownPeers.Contains(self))
					break;
				await Task.Delay(100);
			}

			var me = swarmB.KnownPeers.First(p => p == self);
			Assert.AreEqual(self.Id, me.Id);
			Assert.AreEqual(self.PublicKey, me.PublicKey);
			Assert.IsNotNull(me.ConnectedAddress);

			// Check disconnect
			await swarm.DisconnectAsync(peerBAddress);
			Assert.IsNull(remotePeer.ConnectedAddress);
			Assert.IsTrue(swarm.KnownPeers.Contains(peerB));
			Assert.IsTrue(swarmB.KnownPeers.Contains(self));

			// wait for swarmB to settle
			endTime = DateTime.Now.AddSeconds(3);
			while (true)
			{
				if (DateTime.Now > endTime)
					Assert.Fail("swarmB did not close connection.");
				if (me.ConnectedAddress == null)
					break;
				await Task.Delay(100);
			}
		}
		finally
		{
			await swarm.StopAsync();
			await swarmB.StopAsync();
		}
	}

	[TestMethod]
	public async Task Connect_Disconnect_Reconnect()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var spB = TestSetup.GetScopedServiceProvider();
		var peerB = new Peer
		{
			AgentVersion = "peerB",
			Id = "QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h",
			PublicKey = "CAASXjBcMA0GCSqGSIb3DQEBAQUAA0sAMEgCQQDlTSgVLprWaXfmxDr92DJE1FP0wOexhulPqXSTsNh5ot6j+UiuMgwb0shSPKzLx9AuTolCGhnwpTBYHVhFoBErAgMBAAE="
		};
		var swarmB = spB.GetRequiredService<Swarm>();
		swarmB.LocalPeer = peerB;
		await swarmB.StartAsync();
		var peerBAddress = await swarmB.StartListeningAsync("/ip4/127.0.0.1/tcp/0");
		Assert.IsTrue(peerB.Addresses.Any());

		var swarm = sp.GetRequiredService<Swarm>();
		swarm.LocalPeer = self;
		await swarm.StartAsync();
		_ = await swarm.StartListeningAsync("/ip4/127.0.0.1/tcp/0");
		try
		{
			var remotePeer = (await swarm.ConnectAsync(peerBAddress)).RemotePeer;
			Assert.IsNotNull(remotePeer.ConnectedAddress);
			Assert.AreEqual(peerB.PublicKey, remotePeer.PublicKey);
			Assert.IsTrue(remotePeer.IsValid());
			Assert.IsTrue(swarm.KnownPeers.Contains(peerB));

			// wait for swarmB to settle
			var endTime = DateTime.Now.AddSeconds(3);
			while (true)
			{
				if (DateTime.Now > endTime)
					Assert.Fail("swarmB does not know about self");
				if (swarmB.KnownPeers.Contains(self))
					break;
				await Task.Delay(100);
			}

			var me = swarmB.KnownPeers.First(p => p == self);
			Assert.AreEqual(self.Id, me.Id);
			Assert.AreEqual(self.PublicKey, me.PublicKey);
			Assert.IsNotNull(me.ConnectedAddress);

			// Check disconnect
			await swarm.DisconnectAsync(peerBAddress);
			Assert.IsNull(remotePeer.ConnectedAddress);
			Assert.IsTrue(swarm.KnownPeers.Contains(peerB));
			Assert.IsTrue(swarmB.KnownPeers.Contains(self));

			// wait for swarmB to settle
			endTime = DateTime.Now.AddSeconds(3);
			while (true)
			{
				if (DateTime.Now > endTime)
					Assert.Fail("swarmB did not close connection.");
				if (me.ConnectedAddress == null)
					break;
				await Task.Delay(100);
			}

			// Reconnect
			remotePeer = (await swarm.ConnectAsync(peerBAddress)).RemotePeer;
			Assert.IsNotNull(remotePeer.ConnectedAddress);
			Assert.AreEqual(peerB.PublicKey, remotePeer.PublicKey);
			Assert.IsTrue(remotePeer.IsValid());
			Assert.IsTrue(swarm.KnownPeers.Contains(peerB));
		}
		finally
		{
			await swarm.StopAsync();
			await swarmB.StopAsync();
		}
	}

	[TestMethod]
	public async Task RemotePeer_Contains_ConnectedAddress1()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var spB = TestSetup.GetScopedServiceProvider();
		var peerB = new Peer
		{
			AgentVersion = "peerB",
			Id = "QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h",
			PublicKey = "CAASXjBcMA0GCSqGSIb3DQEBAQUAA0sAMEgCQQDlTSgVLprWaXfmxDr92DJE1FP0wOexhulPqXSTsNh5ot6j+UiuMgwb0shSPKzLx9AuTolCGhnwpTBYHVhFoBErAgMBAAE="
		};
		var swarmB = spB.GetRequiredService<Swarm>();
		swarmB.LocalPeer = peerB;
		await swarmB.StartAsync();
		var peerBAddress = await swarmB.StartListeningAsync("/ip4/0.0.0.0/tcp/0");

		var swarm = sp.GetRequiredService<Swarm>();
		swarm.LocalPeer = self;
		await swarm.StartAsync();
		try
		{
			var connection = await swarm.ConnectAsync(peerBAddress);
			var remote = connection.RemotePeer;
			Assert.AreEqual(remote.ConnectedAddress, peerBAddress);
			CollectionAssert.Contains(remote.Addresses.ToArray(), peerBAddress);
		}
		finally
		{
			await swarm.StopAsync();
			await swarmB.StopAsync();
		}
	}

	[TestMethod]
	public async Task RemotePeer_Contains_ConnectedAddress2()
	{
		// Only works on Windows because connecting to 127.0.0.100 is allowed
		// when listening on 0.0.0.0
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return;
		}

		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var peerB = new Peer
		{
			AgentVersion = "peerB",
			Id = "QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h",
			PublicKey = "CAASXjBcMA0GCSqGSIb3DQEBAQUAA0sAMEgCQQDlTSgVLprWaXfmxDr92DJE1FP0wOexhulPqXSTsNh5ot6j+UiuMgwb0shSPKzLx9AuTolCGhnwpTBYHVhFoBErAgMBAAE="
		};
		var swarmB = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = peerB };
		await swarmB.StartAsync();
		var peerBAddress = await swarmB.StartListeningAsync("/ip4/0.0.0.0/tcp/0");
		var peerBPort = peerBAddress.Protocols[1].Value;
		Assert.IsTrue(peerB.Addresses.Any());

		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		await swarm.StartAsync();
		try
		{
			MultiAddress ma = $"/ip4/127.0.0.100/tcp/{peerBPort}/ipfs/{peerB.Id}";
			var connection = await swarm.ConnectAsync(ma);
			var remote = connection.RemotePeer;
			Assert.AreEqual(remote.ConnectedAddress, ma);
			CollectionAssert.Contains(remote.Addresses.ToArray(), ma);
		}
		finally
		{
			await swarm.StopAsync();
			await swarmB.StopAsync();
		}
	}

	[TestMethod]
	public async Task Connect_CancelsOnStop()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		var venus = new Peer
		{
			Id = "QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ",
			Addresses = new MultiAddress[]
			{
					"/ip4/104.131.131.82/tcp/4001/ipfs/QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ",            // mars.i.ipfs.io
			}
		};

		await swarm.StartAsync();
		var a = swarm.ConnectAsync(venus);
		Assert.IsFalse(a.IsCanceled || a.IsFaulted);

		await swarm.StopAsync();
		var endTime = DateTime.Now.AddSeconds(3);
		while (!a.IsCanceled && !a.IsFaulted)
		{
			if (DateTime.Now > endTime)
				Assert.Fail("swarm did not cancel pending connection.");
			await Task.Delay(100);
		}

		Assert.IsTrue(a.IsCanceled || a.IsFaulted);
	}

	[TestMethod]
	public async Task Connect_IsPending()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		var venus = new Peer
		{
			Id = "QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ",
			Addresses = new MultiAddress[]
			{
					"/ip4/104.131.131.82/tcp/4001/ipfs/QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ",            // mars.i.ipfs.io
			}
		};

		await swarm.StartAsync();
		try
		{
			Assert.IsFalse(swarm.HasPendingConnection(venus));

			var a = swarm.ConnectAsync(venus);
			Assert.IsTrue(swarm.HasPendingConnection(venus));
		}
		finally
		{
			await swarm.StopAsync();
		}
	}

	[TestMethod]
	public async Task Connect_WithSomeUnreachableAddresses()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var bid = "QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h";
		var peerB = new Peer
		{
			AgentVersion = "peerB",
			Id = bid,
			PublicKey = "CAASXjBcMA0GCSqGSIb3DQEBAQUAA0sAMEgCQQDlTSgVLprWaXfmxDr92DJE1FP0wOexhulPqXSTsNh5ot6j+UiuMgwb0shSPKzLx9AuTolCGhnwpTBYHVhFoBErAgMBAAE=",
			Addresses = new MultiAddress[]
			{
					$"/ip4/127.0.0.2/tcp/2/ipfs/{bid}",
					$"/ip4/127.0.0.3/tcp/3/ipfs/{bid}"
			}
		};
		var swarmB = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = peerB };
		await swarmB.StartAsync();
		var peerBAddress = await swarmB.StartListeningAsync("/ip4/127.0.0.1/tcp/0");
		Assert.IsTrue(peerB.Addresses.Any());

		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		await swarm.StartAsync();
		try
		{
			var remotePeer = (await swarm.ConnectAsync(peerB)).RemotePeer;
			Assert.IsNotNull(remotePeer.ConnectedAddress);
			Assert.AreEqual(peerB.PublicKey, remotePeer.PublicKey);
			Assert.IsTrue(remotePeer.IsValid());
			Assert.IsTrue(swarm.KnownPeers.Contains(peerB));
		}
		finally
		{
			await swarm.StopAsync();
			await swarmB.StopAsync();
		}
	}

	[TestMethod]
	public async Task ConnectionEstablished()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var peerB = new Peer
		{
			AgentVersion = "peerB",
			Id = "QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h",
			PublicKey = "CAASXjBcMA0GCSqGSIb3DQEBAQUAA0sAMEgCQQDlTSgVLprWaXfmxDr92DJE1FP0wOexhulPqXSTsNh5ot6j+UiuMgwb0shSPKzLx9AuTolCGhnwpTBYHVhFoBErAgMBAAE="
		};
		var swarmB = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = peerB };
		var swarmBConnections = 0;
		notificationService.Subscribe<Swarm.ConnectionEstablished>(m => ++swarmBConnections);
		await swarmB.StartAsync();
		var peerBAddress = await swarmB.StartListeningAsync("/ip4/127.0.0.1/tcp/0");

		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		var swarmConnections = 0;
		notificationService.Subscribe<Swarm.ConnectionEstablished>(m => ++swarmConnections);
		await swarm.StartAsync();
		try
		{
			var remotePeer = await swarm.ConnectAsync(peerBAddress);
			Assert.AreEqual(1, swarmConnections);

			// wait for swarmB to settle
			var endTime = DateTime.Now.AddSeconds(3);
			while (true)
			{
				if (DateTime.Now > endTime)
					Assert.Fail("swarmB did not raise event.");
				if (swarmBConnections == 1)
					break;
				await Task.Delay(100);
			}
		}
		finally
		{
			await swarm.StopAsync();
			await swarmB.StopAsync();
		}
	}

	[TestMethod]
	public void Connect_No_Transport()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var remoteId = "QmXFX2P5ammdmXQgfqGkfswtEVFsZUJ5KeHRXQYCTdiTAb";
		MultiAddress remoteAddress = $"/ip4/127.0.0.1/ipfs/{remoteId}";
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		swarm.StartAsync().Wait();
		try
		{
			ExceptionAssert.Throws<Exception>(() =>
			{
				var _ = swarm.ConnectAsync(remoteAddress).Result;
			});
		}
		finally
		{
			swarm.StopAsync().Wait();
		}
	}

	[TestMethod]
	public void Connect_Refused()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var remoteId = "QmXFX2P5ammdmXQgfqGkfswtEVFsZUJ5KeHRXQYCTdiTAb";
		MultiAddress remoteAddress = $"/ip4/127.0.0.1/tcp/4040/ipfs/{remoteId}";
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		swarm.StartAsync().Wait();
		try
		{
			ExceptionAssert.Throws<Exception>(() =>
			{
				var _ = swarm.ConnectAsync(remoteAddress).Result;
			});
		}
		finally
		{
			swarm.StopAsync().Wait();
		}
	}

	[TestMethod]
	public void Connect_Failure_Event()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var remoteId = "QmXFX2P5ammdmXQgfqGkfswtEVFsZUJ5KeHRXQYCTdiTAb";
		MultiAddress remoteAddress = $"/ip4/127.0.0.1/tcp/4040/ipfs/{remoteId}";
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		Peer unreachable = null;
		var sub = notificationService.Subscribe<Swarm.PeerNotReachable>(m => unreachable = m.Peer);
		swarm.StartAsync().Wait();
		try
		{
			ExceptionAssert.Throws<Exception>(() =>
			{
				var _ = swarm.ConnectAsync(remoteAddress).Result;
			});
		}
		finally
		{
			swarm.StopAsync().Wait();
		}

		Assert.IsNotNull(unreachable);
		Assert.AreEqual(remoteId, unreachable.Id.ToBase58());
		sub.Dispose();
	}

	[TestMethod]
	public void Connect_Not_Peer()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var remoteId = "QmXFX2P5ammdmXQgfqGkfswtEVFsZUJ5KeHRXQYCTdiTAb";
		MultiAddress remoteAddress = $"/dns/npmjs.com/tcp/80/ipfs/{remoteId}";
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		swarm.StartAsync().Wait();
		try
		{
			ExceptionAssert.Throws<Exception>(() =>
			{
				var _ = swarm.ConnectAsync(remoteAddress).Result;
			});
		}
		finally
		{
			swarm.StopAsync().Wait();
		}
	}

	[TestMethod]
	public void Connect_Cancelled()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var cs = new CancellationTokenSource();
		cs.Cancel();
		var remoteId = "QmXFX2P5ammdmXQgfqGkfswtEVFsZUJ5KeHRXQYCTdiTAb";
		MultiAddress remoteAddress = $"/ip4/127.0.0.1/tcp/4002/ipfs/{remoteId}";
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		swarm.StartAsync().Wait();
		try
		{
			ExceptionAssert.Throws<Exception>(() =>
			{
				var _ = swarm.ConnectAsync(remoteAddress, cs.Token).Result;
			});
		}
		finally
		{
			swarm.StopAsync().Wait();
		}
	}

	[TestMethod]
	public void Connecting_To_Blacklisted_Address()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		swarm.BlackList.Add(mars);
		swarm.StartAsync().Wait();
		try
		{
			ExceptionAssert.Throws<Exception>(() =>
			{
				var _ = swarm.ConnectAsync(mars).Result;
			});
		}
		finally
		{
			swarm.StopAsync().Wait();
		}
	}

	[TestMethod]
	public void Connecting_To_Self()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		swarm.StartAsync().Wait();
		try
		{
			ExceptionAssert.Throws<Exception>(() =>
			{
				var _ = swarm.ConnectAsync(earth).Result;
			});
		}
		finally
		{
			swarm.StopAsync().Wait();
		}
	}

	[TestMethod]
	public async Task Connecting_To_Self_Indirect()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		await swarm.StartAsync();
		try
		{
			var listen = await swarm.StartListeningAsync("/ip4/127.0.0.1/tcp/0");
			var bad = listen.Clone();
			bad.Protocols[2].Value = "QmXFX2P5ammdmXQgfqGkfswtEVFsZUJ5KeHRXQYCTdiTAb";
			ExceptionAssert.Throws<Exception>(() => swarm.ConnectAsync(bad).Wait());
		}
		finally
		{
			await swarm.StopAsync();
		}
	}

	[TestMethod]
	public async Task PeerDisconnected()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var spB = TestSetup.GetScopedServiceProvider();

		var peerB = new Peer
		{
			AgentVersion = "peerB",
			Id = "QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h",
			PublicKey = "CAASXjBcMA0GCSqGSIb3DQEBAQUAA0sAMEgCQQDlTSgVLprWaXfmxDr92DJE1FP0wOexhulPqXSTsNh5ot6j+UiuMgwb0shSPKzLx9AuTolCGhnwpTBYHVhFoBErAgMBAAE="
		};
		var swarmB = spB.GetRequiredService<Swarm>();
		swarmB.LocalPeer = peerB;
		await swarmB.StartAsync();
		var peerBAddress = await swarmB.StartListeningAsync("/ip4/127.0.0.1/tcp/0");

		var swarm = sp.GetRequiredService<Swarm>();
		swarm.LocalPeer = self;
		var swarmConnections = 0;
		var subce = sp.GetRequiredService<INotificationService>().Subscribe<Swarm.ConnectionEstablished>(m => ++swarmConnections);
		var subpd = sp.GetRequiredService<INotificationService>().Subscribe<Swarm.PeerDisconnected>(m => --swarmConnections);
		await swarm.StartAsync();
		try
		{
			var remotePeer = await swarm.ConnectAsync(peerBAddress);
			Assert.AreEqual(1, swarmConnections);

			await swarm.StopAsync();
			Assert.AreEqual(0, swarmConnections);
		}
		finally
		{
			await swarm.StopAsync();
			await swarmB.StopAsync();
			subpd.Dispose();
			subce.Dispose();
		}
	}

	[TestMethod]
	public async Task Listening()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var peerA = new Peer
		{
			Id = self.Id,
			PublicKey = self.PublicKey,
			AgentVersion = self.AgentVersion
		};
		MultiAddress addr = "/ip4/127.0.0.1/tcp/0";
		var swarmA = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = peerA };
		var peerB = new Peer
		{
			Id = other.Id,
			PublicKey = other.PublicKey,
			AgentVersion = other.AgentVersion
		};
		var swarmB = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = peerB };
		await swarmA.StartAsync();
		await swarmB.StartAsync();
		try
		{
			var another = await swarmA.StartListeningAsync(addr);
			Assert.IsTrue(peerA.Addresses.Contains(another));

			await swarmB.ConnectAsync(another);
			Assert.IsTrue(swarmB.KnownPeers.Contains(peerA));

			await swarmA.StopListeningAsync(addr);
			Assert.AreEqual(0, peerA.Addresses.Count());
		}
		finally
		{
			await swarmA.StopAsync();
			await swarmB.StopAsync();
		}
	}

	[TestMethod]
	public async Task Listening_Start_Stop()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var peer = new Peer
		{
			Id = self.Id,
			PublicKey = self.PublicKey,
			AgentVersion = self.AgentVersion
		};
		MultiAddress addr = "/ip4/0.0.0.0/tcp/0";
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = peer };
		await swarm.StartAsync();

		try
		{
			_ = await swarm.StartListeningAsync(addr);
			Assert.IsTrue(peer.Addresses.Any());

			await swarm.StopListeningAsync(addr);
			Assert.AreEqual(0, peer.Addresses.Count());

			_ = await swarm.StartListeningAsync(addr);
			Assert.IsTrue(peer.Addresses.Any());

			await swarm.StopListeningAsync(addr);
			Assert.AreEqual(0, peer.Addresses.Count());
		}
		finally
		{
			await swarm.StopAsync();
		}
	}

	[TestMethod]
	public async Task Stop_Closes_Listeners()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var peer = new Peer
		{
			Id = self.Id,
			PublicKey = self.PublicKey,
			AgentVersion = self.AgentVersion
		};
		MultiAddress addr = "/ip4/0.0.0.0/tcp/0";
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = peer };

		try
		{
			await swarm.StartAsync();
			await swarm.StartListeningAsync(addr);
			Assert.IsTrue(peer.Addresses.Any());
			await swarm.StopAsync();
			Assert.AreEqual(0, peer.Addresses.Count());

			await swarm.StartAsync();
			await swarm.StartListeningAsync(addr);
			Assert.IsTrue(peer.Addresses.Any());
			await swarm.StopAsync();
			Assert.AreEqual(0, peer.Addresses.Count());
		}
		catch (Exception)
		{
			await swarm.StopAsync();
			throw;
		}
	}

	[TestMethod]
	public async Task Listening_Event()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var peer = new Peer
		{
			Id = self.Id,
			PublicKey = self.PublicKey,
			AgentVersion = self.AgentVersion
		};
		MultiAddress addr = "/ip4/127.0.0.1/tcp/0";
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = peer };
		Peer listeningPeer = null;
		var sub = notificationService.Subscribe<Swarm.ListenerEstablished>(m => listeningPeer = m.Peer);
		try
		{
			_ = await swarm.StartListeningAsync(addr);
			Assert.AreEqual(peer, listeningPeer);
			Assert.AreNotEqual(0, peer.Addresses.Count());
		}
		finally
		{
			await swarm.StopAsync();
			sub.Dispose();
		}
	}

	[TestMethod]
	public async Task Listening_AnyPort()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var peerA = new Peer
		{
			Id = self.Id,
			PublicKey = self.PublicKey,
			AgentVersion = self.AgentVersion
		};
		MultiAddress addr = "/ip4/127.0.0.1/tcp/0";
		var swarmA = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = peerA };
		var peerB = new Peer
		{
			Id = other.Id,
			PublicKey = other.PublicKey,
			AgentVersion = other.AgentVersion
		};
		var swarmB = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = peerB };
		await swarmA.StartAsync();
		await swarmB.StartAsync();
		try
		{
			var another = await swarmA.StartListeningAsync(addr);
			Assert.IsTrue(peerA.Addresses.Contains(another));

			_ = await swarmB.ConnectAsync(another);
			Assert.IsTrue(swarmB.KnownPeers.Contains(peerA));
			// TODO: Assert.IsTrue(swarmA.KnownPeers.Contains(peerB));

			await swarmA.StopListeningAsync(addr);
			Assert.IsFalse(peerA.Addresses.Contains(another));
		}
		finally
		{
			await swarmA.StopAsync();
			await swarmB.StopAsync();
		}
	}

	[TestMethod]
	public async Task Listening_IPv4Any()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var peerA = new Peer
		{
			Id = self.Id,
			PublicKey = self.PublicKey,
			AgentVersion = self.AgentVersion
		};
		MultiAddress addr = "/ip4/0.0.0.0/tcp/0";
		var swarmA = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = peerA };
		var peerB = new Peer
		{
			Id = other.Id,
			PublicKey = other.PublicKey,
			AgentVersion = other.AgentVersion
		};
		var swarmB = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = peerB };
		await swarmA.StartAsync();
		await swarmB.StartAsync();
		try
		{
			var another = await swarmA.StartListeningAsync(addr);
			Assert.IsFalse(peerA.Addresses.Contains(addr));
			Assert.IsTrue(peerA.Addresses.Contains(another));

			await swarmB.ConnectAsync(another);
			Assert.IsTrue(swarmB.KnownPeers.Contains(peerA));
			// TODO: Assert.IsTrue(swarmA.KnownPeers.Contains(peerB));

			await swarmA.StopListeningAsync(addr);
			Assert.AreEqual(0, peerA.Addresses.Count());
		}
		finally
		{
			await swarmA.StopAsync();
			await swarmB.StopAsync();
		}
	}

	[TestMethod]
	[TestCategory("IPv6")]
	public async Task Listening_IPv6Any()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var peerA = new Peer
		{
			Id = self.Id,
			PublicKey = self.PublicKey,
			AgentVersion = self.AgentVersion
		};
		MultiAddress addr = "/ip6/::/tcp/0";
		var swarmA = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = peerA };
		var peerB = new Peer
		{
			Id = other.Id,
			PublicKey = other.PublicKey,
			AgentVersion = other.AgentVersion
		};
		var swarmB = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = peerB };
		await swarmA.StartAsync();
		await swarmB.StartAsync();
		try
		{
			var another = await swarmA.StartListeningAsync(addr);
			Assert.IsFalse(peerA.Addresses.Contains(addr));
			Assert.IsTrue(peerA.Addresses.Contains(another));

			await swarmB.ConnectAsync(another);
			Assert.IsTrue(swarmB.KnownPeers.Contains(peerA));
			// TODO: Assert.IsTrue(swarmA.KnownPeers.Contains(peerB));

			await swarmA.StopListeningAsync(addr);
			Assert.AreEqual(0, peerA.Addresses.Count());
		}
		finally
		{
			await swarmA.StopAsync();
			await swarmB.StopAsync();
		}
	}

	[TestMethod]
	public void Listening_MissingTransport()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var peer = new Peer
		{
			Id = self.Id,
			PublicKey = self.PublicKey,
			AgentVersion = self.AgentVersion
		};
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = peer };
		ExceptionAssert.Throws<ArgumentException>(() =>
		{
			var _ = swarm.StartListeningAsync("/ip4/127.0.0.1").Result;
		});
		Assert.AreEqual(0, peer.Addresses.Count());
	}

	[TestMethod]
	public void LocalPeer()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		Assert.AreEqual(self, swarm.LocalPeer)
;
		ExceptionAssert.Throws<ArgumentNullException>(() => swarm.LocalPeer = null);
		ExceptionAssert.Throws<ArgumentNullException>(() => swarm.LocalPeer = new Peer { Id = self.Id });
		ExceptionAssert.Throws<ArgumentNullException>(() => swarm.LocalPeer = new Peer { PublicKey = self.PublicKey });
		ExceptionAssert.Throws<ArgumentException>(() => swarm.LocalPeer = new Peer { Id = self.Id, PublicKey = other.PublicKey });

		swarm.LocalPeer = new Peer { Id = other.Id, PublicKey = other.PublicKey };
		Assert.AreEqual(other, swarm.LocalPeer);
	}

	[TestMethod]
	public async Task Dial_Peer_No_Address()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var peer = new Peer
		{
			Id = mars.PeerId
		};

		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		await swarm.StartAsync();
		try
		{
			ExceptionAssert.Throws<Exception>(() => swarm.DialAsync(peer, "/foo/0.42.0").Wait());
		}
		finally
		{
			await swarm.StopAsync();
		}
	}

	[TestMethod]
	public async Task Dial_Peer_Not_Listening()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var peer = new Peer
		{
			Id = mars.PeerId,
			Addresses = new List<MultiAddress>
				{
					new MultiAddress($"/ip4/127.0.0.1/tcp/4242/ipfs/{mars.PeerId}"),
					new MultiAddress($"/ip4/127.0.0.2/tcp/4242/ipfs/{mars.PeerId}")
				}
		};

		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		await swarm.StartAsync();
		try
		{
			ExceptionAssert.Throws<Exception>(() => swarm.DialAsync(peer, "/foo/0.42.0").Wait());
		}
		finally
		{
			await swarm.StopAsync();
		}
	}

	[TestMethod]
	public async Task Dial_Peer_UnknownProtocol()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var peerB = new Peer
		{
			AgentVersion = "peerB",
			Id = "QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h",
			PublicKey = "CAASXjBcMA0GCSqGSIb3DQEBAQUAA0sAMEgCQQDlTSgVLprWaXfmxDr92DJE1FP0wOexhulPqXSTsNh5ot6j+UiuMgwb0shSPKzLx9AuTolCGhnwpTBYHVhFoBErAgMBAAE="
		};
		var swarmB = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = peerB };
		var peerBAddress = await swarmB.StartListeningAsync("/ip4/127.0.0.1/tcp/0");

		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		await swarm.StartAsync();
		try
		{
			ExceptionAssert.Throws<Exception>(() => swarm.DialAsync(peerB, "/foo/0.42.0").Wait());
		}
		finally
		{
			await swarm.StopAsync();
			await swarmB.StopAsync();
		}
	}

	[TestMethod]
	public async Task Dial_Peer()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var peerB = new Peer
		{
			AgentVersion = "peerB",
			Id = "QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h",
			PublicKey = "CAASXjBcMA0GCSqGSIb3DQEBAQUAA0sAMEgCQQDlTSgVLprWaXfmxDr92DJE1FP0wOexhulPqXSTsNh5ot6j+UiuMgwb0shSPKzLx9AuTolCGhnwpTBYHVhFoBErAgMBAAE="
		};
		var swarmB = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = peerB };
		await swarmB.StartAsync();
		var peerBAddress = await swarmB.StartListeningAsync("/ip4/127.0.0.1/tcp/0");

		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		await swarm.StartAsync();
		try
		{
			using var stream = await swarm.DialAsync(peerB, "/ipfs/id/1.0.0");
			Assert.IsNotNull(stream);
			Assert.IsTrue(stream.CanRead);
			Assert.IsTrue(stream.CanWrite);
		}
		finally
		{
			await swarm.StopAsync();
			await swarmB.StopAsync();
		}
	}

	[TestMethod]
	public void PeerDiscovered()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		var peerCount = 0;
		var sub = notificationService.Subscribe<Swarm.PeerDiscovered>(m => ++peerCount);
		swarm.RegisterPeerAddress("/ip4/127.0.0.1/tcp/4001/ipfs/QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h");
		swarm.RegisterPeerAddress("/ip4/127.0.0.2/tcp/4001/ipfs/QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h");
		swarm.RegisterPeerAddress("/ip4/127.0.0.3/tcp/4001/ipfs/QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h");
		swarm.RegisterPeerAddress("/ip4/127.0.0.1/tcp/4001/ipfs/QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1i");
		swarm.RegisterPeerAddress("/ip4/127.0.0.2/tcp/4001/ipfs/QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1i");
		swarm.RegisterPeerAddress("/ip4/127.0.0.3/tcp/4001/ipfs/QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1i");
		swarm.RegisterPeer(new Peer { Id = "QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1j" });
		swarm.RegisterPeer(new Peer { Id = "QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1j" });

		Assert.AreEqual(3, peerCount);
		sub.Dispose();
	}

	[TestMethod]
	public async Task IsRunning()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		Assert.IsFalse(swarm.IsRunning);

		await swarm.StartAsync();
		Assert.IsTrue(swarm.IsRunning);

		await swarm.StopAsync();
		Assert.IsFalse(swarm.IsRunning);
	}

	[TestMethod]
	public async Task Connect_PrivateNetwork()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var peerB = new Peer
		{
			AgentVersion = "peerB",
			Id = "QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h",
			PublicKey = "CAASXjBcMA0GCSqGSIb3DQEBAQUAA0sAMEgCQQDlTSgVLprWaXfmxDr92DJE1FP0wOexhulPqXSTsNh5ot6j+UiuMgwb0shSPKzLx9AuTolCGhnwpTBYHVhFoBErAgMBAAE="
		};
		var swarmB = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = peerB, NetworkProtector = new OpenNetwork() };
		await swarmB.StartAsync();
		var peerBAddress = await swarmB.StartListeningAsync("/ip4/127.0.0.1/tcp/0");
		Assert.IsTrue(peerB.Addresses.Any());

		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self, NetworkProtector = new OpenNetwork() };
		await swarm.StartAsync();
		try
		{
			var remotePeer = await swarm.ConnectAsync(peerBAddress);
			Assert.AreEqual(2, OpenNetwork.Count);

		}
		finally
		{
			await swarm.StopAsync();
			await swarmB.StopAsync();
		}
	}

	[TestMethod]
	public void DeregisterPeer()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		swarm.RegisterPeer(other);
		Assert.IsTrue(swarm.KnownPeers.Contains(other));

		Peer removedPeer = null;
		var sub = notificationService.Subscribe<Swarm.PeerRemoved>(m => removedPeer = m.Peer);
		swarm.DeregisterPeer(other);
		Assert.IsFalse(swarm.KnownPeers.Contains(other));
		Assert.AreEqual(other, removedPeer);
		sub.Dispose();
	}

	[TestMethod]
	public void IsAllowed_Peer()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry);
		var peer = new Peer
		{
			Id = "QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h",
			Addresses = new MultiAddress[]
			{
					"/ip4/127.0.0.1/ipfs/QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h"
			}
		};

		Assert.IsTrue(swarm.IsAllowed(peer));

		swarm.BlackList.Add(peer.Addresses.First());
		Assert.IsFalse(swarm.IsAllowed(peer));

		swarm.BlackList.Clear();
		swarm.BlackList.Add("/p2p/QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h");
		Assert.IsFalse(swarm.IsAllowed(peer));
	}

	[TestMethod]
	public void RegisterPeer_BlackListed()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var swarm = new Swarm(loggerFactory, message, notificationService, protocolRegistry, transportRegistry) { LocalPeer = self };
		var peer = new Peer
		{
			Id = "QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h",
			Addresses = new MultiAddress[]
			{
					"/ip4/127.0.0.1/ipfs/QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h"
			}
		};

		swarm.BlackList.Add(peer.Addresses.First());
		ExceptionAssert.Throws<Exception>(() => swarm.RegisterPeer(peer));
	}
}
