namespace PeerTalk;

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
using System.Threading.Tasks;

[TestClass]
public class AutoDialerTest
{
	private readonly Peer peerA = new()
	{
		AgentVersion = "A",
		Id = "QmXK9VBxaXFuuT29AaPUTgW3jBWZ9JgLVZYdMYTHC6LLAH",
		PublicKey = "CAASXjBcMA0GCSqGSIb3DQEBAQUAA0sAMEgCQQCC5r4nQBtnd9qgjnG8fBN5+gnqIeWEIcUFUdCG4su/vrbQ1py8XGKNUBuDjkyTv25Gd3hlrtNJV3eOKZVSL8ePAgMBAAE="
	};
	private readonly Peer peerB = new()
	{
		AgentVersion = "B",
		Id = "QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h",
		PublicKey = "CAASXjBcMA0GCSqGSIb3DQEBAQUAA0sAMEgCQQDlTSgVLprWaXfmxDr92DJE1FP0wOexhulPqXSTsNh5ot6j+UiuMgwb0shSPKzLx9AuTolCGhnwpTBYHVhFoBErAgMBAAE="
	};
	private readonly Peer peerC = new()
	{
		AgentVersion = "C",
		Id = "QmTcEBjSTSLjeu2oTiSoBSQQgqH5MADUsemXewn6rThoDT",
		PublicKey = "CAASXjBcMA0GCSqGSIb3DQEBAQUAA0sAMEgCQQCAL8J1Lp6Ad5eYanOwNenXZ6Efvhk9wwFRXqqPn9UT+/JTxBvZPzQwK/FbPRczjZ/A1x8BSec1gvFCzcX4fkULAgMBAAE="
	};

	[TestMethod]
	public void Defaults()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var dialer = sp.GetRequiredService<AutoDialer>();
		Assert.AreEqual(AutoDialer.DefaultMinConnections, dialer.MinConnections);
	}

	[TestMethod]
	public async Task Connects_OnPeerDiscovered_When_Below_MinConnections()
	{
		var spA = TestSetup.GetScopedServiceProvider();
		var swarmA = spA.GetRequiredService<Swarm>();
		swarmA.LocalPeer = peerA;
		await swarmA.StartAsync();
		var peerAAddress = await swarmA.StartListeningAsync("/ip4/127.0.0.1/tcp/0");

		var spB = TestSetup.GetScopedServiceProvider();
		var swarmB = spB.GetRequiredService<Swarm>();
		swarmB.LocalPeer = peerB;
		await swarmB.StartAsync();
		var peerBAddress = await swarmB.StartListeningAsync("/ip4/127.0.0.1/tcp/0");

		AutoDialer autoDialer = null;
		try
		{
			autoDialer = spA.GetRequiredService<AutoDialer>();
			var other = swarmA.RegisterPeerAddress(peerBAddress);

			// wait for the connection.
			var endTime = DateTime.Now.AddSeconds(3);
			while (other.ConnectedAddress is null)
			{
				if (DateTime.Now > endTime)
				{
					Assert.Fail("Did not do autodial");
				}

				await Task.Delay(100);
			}
		}
		finally
		{
			await swarmA?.StopAsync();
			await swarmB?.StopAsync();
			autoDialer?.Dispose();
		}
	}

	[TestMethod]
	public async Task Noop_OnPeerDiscovered_When_NotBelow_MinConnections()
	{
		var sp = TestSetup.GetScopedServiceProvider();

		var notificationService = sp.GetRequiredService<INotificationService>();
		var swarmA = new Swarm(
			sp.GetRequiredService<ILoggerFactory>(),
			sp.GetRequiredService<Message>(),
			notificationService,
			sp.GetRequiredService<ProtocolRegistry>(),
			sp.GetRequiredService<TransportRegistry>())
		{
			LocalPeer = peerA
		};
		await swarmA.StartAsync();
		var peerAAddress = await swarmA.StartListeningAsync("/ip4/127.0.0.1/tcp/0");

		var swarmB = new Swarm(
			sp.GetRequiredService<ILoggerFactory>(),
			sp.GetRequiredService<Message>(),
			notificationService,
			sp.GetRequiredService<ProtocolRegistry>(),
			sp.GetRequiredService<TransportRegistry>())
		{
			LocalPeer = peerB
		};
		await swarmB.StartAsync();
		var peerBAddress = await swarmB.StartListeningAsync("/ip4/127.0.0.1/tcp/0");

		try
		{
			using var dialer = new AutoDialer(
				sp.GetRequiredService<ILogger<AutoDialer>>(),
				notificationService,
				swarmA)
			{
				MinConnections = 0
			};
			var other = swarmA.RegisterPeerAddress(peerBAddress);

			// wait for the connection.
			var endTime = DateTime.Now.AddSeconds(3);
			while (other.ConnectedAddress is null)
			{
				if (DateTime.Now > endTime)
				{
					return;
				}

				await Task.Delay(100);
			}

			Assert.Fail("Autodial should not happen");
		}
		finally
		{
			await swarmA?.StopAsync();
			await swarmB?.StopAsync();
		}
	}

	[TestMethod]
	public async Task Connects_OnPeerDisconnected_When_Below_MinConnections()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();

		var swarmA = new Swarm(
			loggerFactory,
			message,
			notificationService,
			protocolRegistry,
			transportRegistry)
		{
			LocalPeer = peerA
		};
		await swarmA.StartAsync();
		var peerAAddress = await swarmA.StartListeningAsync("/ip4/127.0.0.1/tcp/0");

		var swarmB = new Swarm(
			loggerFactory,
			message,
			notificationService,
			protocolRegistry,
			transportRegistry)
		{
			LocalPeer = peerB
		};
		await swarmB.StartAsync();
		var peerBAddress = await swarmB.StartListeningAsync("/ip4/127.0.0.1/tcp/0");

		var swarmC = new Swarm(
			loggerFactory,
			message,
			notificationService,
			protocolRegistry,
			transportRegistry)
		{
			LocalPeer = peerC
		};
		await swarmC.StartAsync();
		var peerCAddress = await swarmC.StartListeningAsync("/ip4/127.0.0.1/tcp/0");

		bool isBConnected = false;
		var sub = notificationService.Subscribe<Swarm.ConnectionEstablished>(m =>
		{
			if (m.PeerConnection.RemotePeer == peerB)
			{
				isBConnected = true;
			}
		});

		try
		{
			using var dialer = new AutoDialer(Mock.Of<ILogger<AutoDialer>>(), notificationService, swarmA) { MinConnections = 1 };
			var b = swarmA.RegisterPeerAddress(peerBAddress);
			var c = swarmA.RegisterPeerAddress(peerCAddress);

			// wait for the peer B connection.
			var endTime = DateTime.Now.AddSeconds(3);
			while (!isBConnected)
			{
				if (DateTime.Now > endTime)
					Assert.Fail("Did not do autodial on peer discovered");
				await Task.Delay(100);
			}

			Assert.IsNull(c.ConnectedAddress);
			await swarmA.DisconnectAsync(peerBAddress);

			// wait for the peer C connection.
			endTime = DateTime.Now.AddSeconds(3);
			while (c.ConnectedAddress == null)
			{
				if (DateTime.Now > endTime)
					Assert.Fail("Did not do autodial on peer disconnected");
				await Task.Delay(100);
			}
		}
		finally
		{
			await swarmA?.StopAsync();
			await swarmB?.StopAsync();
			await swarmC?.StopAsync();
			sub?.Dispose();
		}
	}
}
