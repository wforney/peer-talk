namespace PeerTalk.Routing;

using Ipfs;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SharedCode.Notifications;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

[TestClass]
public class DistributedQueryTest
{
	[TestMethod]
	public async Task Cancelling()
	{
		var loggerFactory = Mock.Of<ILoggerFactory>();
		var notificationService = new NotificationService();
		var dquery = new DistributedQuery<Peer>(Mock.Of<ILogger<DistributedQuery<Peer>>>(), notificationService)
		{
			Dht = new Dht1(loggerFactory, notificationService)
		};
		var cts = new CancellationTokenSource();
		cts.Cancel();
		await dquery.RunAsync(cts.Token);
		Assert.AreEqual(0, dquery.Answers.Count());
	}

	[TestMethod]
	public void UniqueId()
	{
		var notificationService = new NotificationService();
		var q1 = new DistributedQuery<Peer>(Mock.Of<ILogger<DistributedQuery<Peer>>>(), notificationService);
		var q2 = new DistributedQuery<Peer>(Mock.Of<ILogger<DistributedQuery<Peer>>>(), notificationService);
		Assert.AreNotEqual(q1.Id, q2.Id);
	}
}
