namespace PeerTalk.Discovery;

using Ipfs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PeerTalkTests;
using SharedCode.Notifications;
using System.Linq;
using System.Threading.Tasks;

[TestClass]
public class BootstrapTest
{
	[TestMethod]
	public async Task NullList()
	{
		var logger = Mock.Of<ILogger<Bootstrap>>();
		var notificationService = new SharedCode.Notifications.NotificationService();
		var bootstrap = new Bootstrap(logger, notificationService) { Addresses = null };
		int found = 0;
		_ = notificationService.Subscribe<PeerDiscovered>(m => ++found);
		await bootstrap.StartAsync();
		Assert.AreEqual(0, found);
	}

	[TestMethod]
	public async Task Discovered()
	{
		var logger = Mock.Of<ILogger<Bootstrap>>();
		var notificationService = new SharedCode.Notifications.NotificationService();
		var bootstrap = new Bootstrap(logger, notificationService)
		{
			Addresses = new MultiAddress[]
			{
				"/ip4/104.131.131.82/tcp/4001/ipfs/QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ",
				"/ip4/104.131.131.83/tcp/4001/p2p/QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ"
			}
		};
		int found = 0;
		_ = notificationService.Subscribe<PeerDiscovered>(m =>
		{
			Assert.IsNotNull(m.Peer);
			Assert.AreEqual("QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ", m.Peer.Id.ToBase58());
			CollectionAssert.AreEqual(bootstrap.Addresses.ToArray(), m.Peer.Addresses.ToArray());
			++found;
		});
		await bootstrap.StartAsync();
		Assert.AreEqual(1, found);
	}

	[TestMethod]
	public async Task Discovered_Multiple_Peers()
	{
		var logger = Mock.Of<ILogger<Bootstrap>>();
		var notificationService = new SharedCode.Notifications.NotificationService();
		var bootstrap = new Bootstrap(logger, notificationService)
		{
			Addresses = new MultiAddress[]
			{
				"/ip4/104.131.131.82/tcp/4001/ipfs/QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ",
				"/ip4/127.0.0.1/tcp/4001/ipfs/QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h",
				"/ip4/104.131.131.83/tcp/4001/p2p/QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ",
				"/ip6/::/tcp/4001/p2p/QmdpwjdB94eNm2Lcvp9JqoCxswo3AKQqjLuNZyLixmCM1h"
			}
		};
		int found = 0;
		_ = notificationService.Subscribe<PeerDiscovered>(m =>
		{
			Assert.IsNotNull(m.Peer);
			++found;
		});
		await bootstrap.StartAsync();
		Assert.AreEqual(2, found);
	}

	/* No longer applies due to event handlers being replaced by messaging.
		[TestMethod]
		public async Task Stop_Removes_EventHandlers()
		{
			var sp = TestSetup.GetScopedServiceProvider();
			var bootstrap = sp.GetRequiredService<Bootstrap>();
			bootstrap.Addresses = new MultiAddress[]
				{
					"/ip4/104.131.131.82/tcp/4001/ipfs/QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ"
				};
			int found = 0;
			var sub = sp.GetRequiredService<INotificationService>().Subscribe<PeerDiscovered>(m =>
			{
				Assert.IsNotNull(m.Peer);
				++found;
			});
			await bootstrap.StartAsync();
			Assert.AreEqual(1, found);
			await bootstrap.StopAsync();
			sub.Dispose();

			await bootstrap.StartAsync();
			Assert.AreEqual(1, found);
		}
	*/

	[TestMethod]
	public async Task Missing_ID_Is_Ignored()
	{
		var sp = TestSetup.GetScopedServiceProvider();
		var bootstrap = sp.GetRequiredService<Bootstrap>();
		bootstrap.Addresses = new MultiAddress[]
			{
				"/ip4/104.131.131.82/tcp/4002",
				"/ip4/104.131.131.82/tcp/4001/ipfs/QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ"
			};
		int found = 0;
		var sub = sp.GetRequiredService<INotificationService>().Subscribe<PeerDiscovered>(m =>
		{
			Assert.IsNotNull(m.Peer);
			Assert.IsNotNull(m.Peer.Addresses);
			Assert.AreEqual(bootstrap.Addresses.Last(), m.Peer.Addresses.First());
			++found;
		});
		await bootstrap.StartAsync();
		Assert.AreEqual(1, found);

		sub.Dispose();
	}
}
