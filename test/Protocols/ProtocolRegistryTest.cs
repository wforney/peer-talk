namespace PeerTalk.Protocols;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PeerTalkTests;

[TestClass]
public class ProtocolRegistryTest
{
	[TestMethod]
	public void PreRegistered()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var protocolRegistry = sp.GetRequiredService<ProtocolRegistry>();
		CollectionAssert.Contains(protocolRegistry.Protocols.Keys, "/multistream/1.0.0");
		CollectionAssert.Contains(protocolRegistry.Protocols.Keys, "/plaintext/1.0.0");
	}
}
