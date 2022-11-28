namespace PeerTalk;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PeerTalk.Protocols;
using PeerTalkTests;
using SharedCode.Notifications;
using System.IO;

[TestClass]
public class PeerConnectionTest
{
	[TestMethod]
	public void Disposing()
	{
		var closeCount = 0;
		var stream = new MemoryStream();
		var sp = TestSetup.GetScopedServiceProvider();
		var connection = sp.GetRequiredService<PeerConnection>();
		connection.Stream = stream;
		var sub = sp.GetRequiredService<INotificationService>().Subscribe<PeerConnection.Closed>(m => ++closeCount);
		Assert.IsTrue(connection.IsActive);
		Assert.IsNotNull(connection.Stream);

		connection.Dispose();
		Assert.IsFalse(connection.IsActive);
		Assert.IsNull(connection.Stream);

		// Can be disposed multiple times.
		connection.Dispose();

		Assert.IsFalse(connection.IsActive);
		Assert.AreEqual(1, closeCount);
	}

	[TestMethod]
	public void Stats()
	{
		var stream = new MemoryStream();
		var sp = TestSetup.GetScopedServiceProvider();
		var connection = sp.GetRequiredService<PeerConnection>();
		connection.Stream = stream;
		Assert.AreEqual(0, connection.BytesRead);
		Assert.AreEqual(0, connection.BytesWritten);

		var buffer = new byte[] { 1, 2, 3 };
		connection.Stream.Write(buffer, 0, 3);
		Assert.AreEqual(0, connection.BytesRead);
		Assert.AreEqual(3, connection.BytesWritten);

		stream.Position = 0;
		_ = connection.Stream.ReadByte();
		_ = connection.Stream.ReadByte();
		Assert.AreEqual(2, connection.BytesRead);
		Assert.AreEqual(3, connection.BytesWritten);
	}

	[TestMethod]
	public void Protocols()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var connection = sp.GetRequiredService<PeerConnection>();
		Assert.AreEqual(0, connection.Protocols.Count);

		connection.AddProtocol(sp.GetRequiredService<Identify1>());
		Assert.AreEqual(1, connection.Protocols.Count);

		connection.AddProtocols(new IPeerProtocol[] { sp.GetRequiredService<Mplex67>(), sp.GetRequiredService<Plaintext1>() });
		Assert.AreEqual(3, connection.Protocols.Count);
	}

	[TestMethod]
	public void CreatesOneStatsStream()
	{
		var a = new MemoryStream();
		var b = new MemoryStream();
		var sp = TestSetup.GetScopedServiceProvider();
		var connection = sp.GetRequiredService<PeerConnection>();
		Assert.AreEqual(null, connection.Stream);

		connection.Stream = a;
		Assert.AreNotSame(a, connection.Stream);

		connection.Stream = b;
		Assert.AreSame(b, connection.Stream);
	}
}
