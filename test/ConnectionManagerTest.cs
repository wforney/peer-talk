namespace PeerTalk;

using Ipfs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PeerTalk.Protocols;
using PeerTalk.Transports;
using PeerTalkTests;
using SharedCode.Notifications;
using System.IO;
using System.Linq;
using ILoggerFactory = Microsoft.Extensions.Logging.ILoggerFactory;

[TestClass]
public class ConnectionManagerTest
{
	private readonly MultiHash aId = "QmXFX2P5ammdmXQgfqGkfswtEVFsZUJ5KeHRXQYCTdiTAb";
	private readonly MultiHash bId = "QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h";

	[TestMethod]
	public void IsConnected()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var manager = new ConnectionManager(notificationService);
		var peer = new Peer { Id = aId };
		var connection = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peer, Stream = Stream.Null };

		Assert.IsFalse(manager.IsConnected(peer));
		_ = manager.Add(connection);
		Assert.IsTrue(manager.IsConnected(peer));
	}

	[TestMethod]
	public void IsConnected_NotActive()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var manager = new ConnectionManager(notificationService);
		var peer = new Peer { Id = aId };
		var connection = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peer, Stream = Stream.Null };

		Assert.IsFalse(manager.IsConnected(peer));

		_ = manager.Add(connection);
		Assert.IsTrue(manager.IsConnected(peer));
		Assert.AreEqual(1, manager.Connections.Count());

		connection.Stream = null;
		Assert.IsFalse(manager.IsConnected(peer));
		Assert.AreEqual(0, manager.Connections.Count());
	}

	[TestMethod]
	public void Add_Duplicate()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var manager = new ConnectionManager(notificationService);
		var peer = new Peer { Id = aId };
		var a = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peer, Stream = Stream.Null };
		var b = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peer, Stream = Stream.Null };

		Assert.AreSame(a, manager.Add(a));
		Assert.IsTrue(manager.IsConnected(peer));
		Assert.AreEqual(1, manager.Connections.Count());
		Assert.IsNotNull(a.Stream);

		Assert.AreSame(b, manager.Add(b));
		Assert.IsTrue(manager.IsConnected(peer));
		Assert.AreEqual(2, manager.Connections.Count());
		Assert.IsNotNull(a.Stream);
		Assert.IsNotNull(b.Stream);

		manager.Clear();
		Assert.AreEqual(0, manager.Connections.Count());
		Assert.IsNull(a.Stream);
		Assert.IsNull(b.Stream);
	}

	[TestMethod]
	public void Add_Duplicate_SameConnection()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var manager = new ConnectionManager(notificationService);
		var peer = new Peer { Id = aId };
		var a = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peer, Stream = Stream.Null };

		Assert.AreSame(a, manager.Add(a));
		Assert.IsTrue(manager.IsConnected(peer));
		Assert.AreEqual(1, manager.Connections.Count());
		Assert.IsNotNull(a.Stream);

		Assert.AreSame(a, manager.Add(a));
		Assert.IsTrue(manager.IsConnected(peer));
		Assert.AreEqual(1, manager.Connections.Count());
		Assert.IsNotNull(a.Stream);
	}

	[TestMethod]
	public void Add_Duplicate_PeerConnectedAddress()
	{
		var address = "/ip6/::1/tcp/4007";

		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var manager = new ConnectionManager(notificationService);
		var peer = new Peer { Id = aId, ConnectedAddress = address };
		var a = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peer, RemoteAddress = address, Stream = Stream.Null };
		var b = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peer, RemoteAddress = address, Stream = Stream.Null };

		Assert.AreSame(a, manager.Add(a));
		Assert.IsTrue(manager.IsConnected(peer));
		Assert.AreEqual(1, manager.Connections.Count());
		Assert.IsNotNull(a.Stream);
		Assert.AreEqual(address, peer.ConnectedAddress);

		Assert.AreSame(b, manager.Add(b));
		Assert.IsTrue(manager.IsConnected(peer));
		Assert.AreEqual(2, manager.Connections.Count());
		Assert.IsNotNull(a.Stream);
		Assert.IsNotNull(b.Stream);
		Assert.AreEqual(address, peer.ConnectedAddress);
	}

	[TestMethod]
	public void Maintains_PeerConnectedAddress()
	{
		var address1 = "/ip4/127.0.0.1/tcp/4007";
		var address2 = "/ip4/127.0.0.2/tcp/4007";

		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var manager = new ConnectionManager(notificationService);
		var peer = new Peer { Id = aId };
		var a = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peer, RemoteAddress = address1, Stream = Stream.Null };
		var b = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peer, RemoteAddress = address2, Stream = Stream.Null };

		Assert.AreSame(a, manager.Add(a));
		Assert.IsTrue(manager.IsConnected(peer));
		Assert.AreEqual(1, manager.Connections.Count());
		Assert.IsNotNull(a.Stream);
		Assert.AreEqual(address1, peer.ConnectedAddress);

		Assert.AreSame(b, manager.Add(b));
		Assert.IsTrue(manager.IsConnected(peer));
		Assert.AreEqual(2, manager.Connections.Count());
		Assert.IsNotNull(a.Stream);
		Assert.IsNotNull(b.Stream);
		Assert.AreEqual(address1, peer.ConnectedAddress);

		Assert.IsTrue(manager.Remove(a));
		Assert.IsTrue(manager.IsConnected(peer));
		Assert.AreEqual(1, manager.Connections.Count());
		Assert.IsNull(a.Stream);
		Assert.IsNotNull(b.Stream);
		Assert.AreEqual(address2, peer.ConnectedAddress);

		Assert.IsTrue(manager.Remove(b));
		Assert.IsFalse(manager.IsConnected(peer));
		Assert.AreEqual(0, manager.Connections.Count());
		Assert.IsNull(a.Stream);
		Assert.IsNull(b.Stream);
		Assert.IsNull(peer.ConnectedAddress);
	}

	[TestMethod]
	public void Add_Duplicate_ExistingIsDead()
	{
		var address = "/ip6/::1/tcp/4007";

		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var manager = new ConnectionManager(notificationService);
		var peer = new Peer { Id = aId, ConnectedAddress = address };
		var a = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peer, RemoteAddress = address, Stream = Stream.Null };
		var b = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peer, RemoteAddress = address, Stream = Stream.Null };

		Assert.AreSame(a, manager.Add(a));
		Assert.IsTrue(manager.IsConnected(peer));
		Assert.AreEqual(1, manager.Connections.Count());
		Assert.IsNotNull(a.Stream);
		Assert.AreEqual(address, peer.ConnectedAddress);

		a.Stream = null;
		Assert.AreSame(b, manager.Add(b));
		Assert.IsTrue(manager.IsConnected(peer));
		Assert.AreEqual(1, manager.Connections.Count());
		Assert.IsNull(a.Stream);
		Assert.IsNotNull(b.Stream);
		Assert.AreEqual(address, peer.ConnectedAddress);
	}

	[TestMethod]
	public void Add_NotActive()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var manager = new ConnectionManager(notificationService);
		var peer = new Peer { Id = aId };
		var a = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peer, Stream = Stream.Null };
		var b = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peer, Stream = Stream.Null };

		Assert.AreSame(a, manager.Add(a));
		Assert.IsTrue(manager.IsConnected(peer));
		Assert.AreEqual(1, manager.Connections.Count());
		Assert.IsNotNull(a.Stream);
		a.Stream = null;

		Assert.AreSame(b, manager.Add(b));
		Assert.IsTrue(manager.IsConnected(peer));
		Assert.AreEqual(1, manager.Connections.Count());
		Assert.IsNull(a.Stream);
		Assert.IsNotNull(b.Stream);

		Assert.AreSame(b, manager.Connections.First());
	}

	[TestMethod]
	public void Remove_Connection()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var manager = new ConnectionManager(notificationService);
		var peer = new Peer { Id = aId };
		var a = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peer, Stream = Stream.Null };

		_ = manager.Add(a);
		Assert.IsTrue(manager.IsConnected(peer));
		Assert.AreEqual(1, manager.Connections.Count());
		Assert.IsNotNull(a.Stream);

		Assert.IsTrue(manager.Remove(a));
		Assert.IsFalse(manager.IsConnected(peer));
		Assert.AreEqual(0, manager.Connections.Count());
		Assert.IsNull(a.Stream);
	}

	[TestMethod]
	public void Remove_PeerId()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var manager = new ConnectionManager(notificationService);
		var peer = new Peer { Id = aId };
		var a = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peer, Stream = Stream.Null };

		_ = manager.Add(a);
		Assert.IsTrue(manager.IsConnected(peer));
		Assert.AreEqual(1, manager.Connections.Count());
		Assert.IsNotNull(a.Stream);

		Assert.IsTrue(manager.Remove(peer.Id));
		Assert.IsFalse(manager.IsConnected(peer));
		Assert.AreEqual(0, manager.Connections.Count());
		Assert.IsNull(a.Stream);
	}

	[TestMethod]
	public void Remove_DoesNotExist()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var manager = new ConnectionManager(notificationService);
		var peer = new Peer { Id = aId };
		var a = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peer, Stream = Stream.Null };

		Assert.IsFalse(manager.Remove(a));
		Assert.IsFalse(manager.IsConnected(peer));
		Assert.AreEqual(0, manager.Connections.Count());
		Assert.IsNull(a.Stream);
	}

	[TestMethod]
	public void Clear()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var manager = new ConnectionManager(notificationService);
		var peerA = new Peer { Id = aId };
		var peerB = new Peer { Id = bId };
		var a = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peerA, Stream = Stream.Null };
		var b = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peerB, Stream = Stream.Null };

		Assert.AreSame(a, manager.Add(a));
		Assert.AreSame(b, manager.Add(b));
		Assert.IsTrue(manager.IsConnected(peerA));
		Assert.IsTrue(manager.IsConnected(peerB));
		Assert.AreEqual(2, manager.Connections.Count());
		Assert.IsNotNull(a.Stream);
		Assert.IsNotNull(b.Stream);

		manager.Clear();
		Assert.IsFalse(manager.IsConnected(peerA));
		Assert.IsFalse(manager.IsConnected(peerB));
		Assert.AreEqual(0, manager.Connections.Count());
		Assert.IsNull(a.Stream);
		Assert.IsNull(b.Stream);
	}

	[TestMethod]
	public void PeerDisconnectedEvent_RemovingPeer()
	{
		bool gotEvent = false;
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var manager = new ConnectionManager(notificationService);
		notificationService.Subscribe<ConnectionManager.PeerDisconnected>(m => gotEvent = true);
		var peerA = new Peer { Id = aId };
		var a = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peerA, Stream = Stream.Null };
		_ = manager.Add(a);

		_ = manager.Remove(peerA.Id);
		Assert.IsTrue(gotEvent);
	}

	[TestMethod]
	public void PeerDisconnectedEvent_RemovingConnection()
	{
		int gotEvent = 0;
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var manager = new ConnectionManager(notificationService);
		var sub = notificationService.Subscribe<ConnectionManager.PeerDisconnected>(m => gotEvent += 1);
		var peerA = new Peer { Id = aId };
		var a = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peerA, Stream = Stream.Null };
		_ = manager.Add(a);

		_ = manager.Remove(a);
		Assert.AreEqual(1, gotEvent);
		sub.Dispose();
	}

	[TestMethod]
	public void PeerDisconnectedEvent_ConnectionClose()
	{
		int gotEvent = 0;
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();
		var manager = new ConnectionManager(notificationService);
		var sub = notificationService.Subscribe<ConnectionManager.PeerDisconnected>(m => gotEvent += 1);
		var peerA = new Peer { Id = aId };
		var a = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { RemotePeer = peerA, Stream = Stream.Null };
		_ = manager.Add(a);
		a.Dispose();
		Assert.AreEqual(1, gotEvent);
		sub.Dispose();
	}
}
