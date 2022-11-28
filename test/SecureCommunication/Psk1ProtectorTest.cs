namespace PeerTalk.SecureCommunication;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PeerTalk.Cryptography;
using PeerTalk.Protocols;
using PeerTalk.Transports;
using PeerTalkTests;
using SharedCode.Notifications;
using System.IO;
using System.Threading.Tasks;

[TestClass]
public class Psk1ProtectorTest
{
	[TestMethod]
	public async Task Protect()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
		var message = sp.GetRequiredService<Message>();
		var notificationService = sp.GetRequiredService<INotificationService>();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		var transportRegistry = sp.GetRequiredService<TransportRegistry>();

		var psk = new PreSharedKey().Generate();
		var protector = new Psk1Protector { Key = psk };
		var connection = new PeerConnection(loggerFactory, message, notificationService, protocolRegistry) { Stream = Stream.Null };
		var protectedStream = await protector.ProtectAsync(connection);
		Assert.IsInstanceOfType(protectedStream, typeof(Psk1Stream));
	}
}
