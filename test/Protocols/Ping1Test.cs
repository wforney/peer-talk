namespace PeerTalk.Protocols;

using Ipfs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PeerTalkTests;
using System.Linq;
using System.Threading.Tasks;

[TestClass]
public class PingTest
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
		Id = "QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h",
		PublicKey = "CAASXjBcMA0GCSqGSIb3DQEBAQUAA0sAMEgCQQDlTSgVLprWaXfmxDr92DJE1FP0wOexhulPqXSTsNh5ot6j+UiuMgwb0shSPKzLx9AuTolCGhnwpTBYHVhFoBErAgMBAAE="
	};

	[TestMethod]
	public async Task MultiAddress()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var spB = TestSetup.GetScopedServiceProvider();

		var swarmB = spB.GetRequiredService<Swarm>();
		swarmB.LocalPeer = other;
		await swarmB.StartAsync();
		var pingB = spB.GetRequiredService<Ping1>();
		pingB.Swarm = swarmB;
		await pingB.StartAsync();
		var peerBAddress = await swarmB.StartListeningAsync("/ip4/127.0.0.1/tcp/0");

		var swarm = sp.GetRequiredService<Swarm>();
		swarm.LocalPeer = self;
		await swarm.StartAsync();
		var pingA = sp.GetRequiredService<Ping1>();
		pingA.Swarm = swarm;
		await pingA.StartAsync();
		try
		{
			_ = await swarm.ConnectAsync(peerBAddress);
			var result = await pingA.PingAsync(other.Id, 4);
			Assert.IsTrue(result.All(r => r.Success));
		}
		finally
		{
			await swarm.StopAsync();
			await swarmB.StopAsync();
			await pingB.StopAsync();
			await pingA.StopAsync();
		}
	}

	[TestMethod]
	public async Task PeerId()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var spB = TestSetup.GetScopedServiceProvider();

		var swarmB = spB.GetRequiredService<Swarm>();
		swarmB.LocalPeer = other;
		await swarmB.StartAsync();
		var pingB = spB.GetRequiredService<Ping1>();
		pingB.Swarm = swarmB;
		await pingB.StartAsync();
		var peerBAddress = await swarmB.StartListeningAsync("/ip4/127.0.0.1/tcp/0");

		var swarm = sp.GetRequiredService<Swarm>();
		swarm.LocalPeer = self;
		await swarm.StartAsync();
		var pingA = sp.GetRequiredService<Ping1>();
		pingA.Swarm = swarm;
		await pingA.StartAsync();
		try
		{
			var result = await pingA.PingAsync(peerBAddress, 4);
			Assert.IsTrue(result.All(r => r.Success));
		}
		finally
		{
			await swarm.StopAsync();
			await swarmB.StopAsync();
			await pingB.StopAsync();
			await pingA.StopAsync();
		}
	}
}
