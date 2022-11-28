namespace PeerTalk.Multiplex;

using Ipfs;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SharedCode.Notifications;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

[TestClass]
public class MuxerTest
{
	[TestMethod]
	public void Defaults()
	{
		var logger = Mock.Of<ILogger<Muxer>>();
		var notificationService = new NotificationService();
		var muxer = new Muxer(logger, notificationService);
		Assert.AreEqual(true, muxer.Initiator);
		Assert.AreEqual(false, muxer.Receiver);
	}

	[TestMethod]
	public void InitiatorReceiver()
	{
		var logger = Mock.Of<ILogger<Muxer>>();
		var notificationService = new NotificationService();
		var muxer = new Muxer(logger, notificationService) { Initiator = true };
		Assert.AreEqual(true, muxer.Initiator);
		Assert.AreEqual(false, muxer.Receiver);
		Assert.AreEqual(0, muxer.NextStreamId & 1);

		muxer.Receiver = true;
		Assert.AreEqual(false, muxer.Initiator);
		Assert.AreEqual(true, muxer.Receiver);
		Assert.AreEqual(1, muxer.NextStreamId & 1);
	}

	[TestMethod]
	public async Task NewStream_Send()
	{
		var channel = new MemoryStream();
		var logger = Mock.Of<ILogger<Muxer>>();
		var notificationService = new NotificationService();
		var muxer = new Muxer(logger, notificationService) { Channel = channel, Initiator = true };
		var nextId = muxer.NextStreamId;
		var stream = await muxer.CreateStreamAsync("foo");

		// Correct stream id is assigned.
		Assert.AreEqual(nextId, stream.Id);
		Assert.AreEqual(nextId + 2, muxer.NextStreamId);
		Assert.AreEqual("foo", stream.Name);

		// Substreams are managed.
		Assert.AreEqual(1, muxer.Substreams.Count);
		Assert.AreSame(stream, muxer.Substreams[stream.Id]);

		// NewStream message is sent.
		channel.Position = 0;
		Assert.AreEqual(stream.Id << 3, channel.ReadVarint32());
		Assert.AreEqual(3, channel.ReadVarint32());
		var name = new byte[3];
		_ = channel.Read(name, 0, 3);
		Assert.AreEqual("foo", Encoding.UTF8.GetString(name));
		Assert.AreEqual(channel.Length, channel.Position);
	}

	[TestMethod]
	public async Task NewStream_Receive()
	{
		var channel = new MemoryStream();
		var logger = Mock.Of<ILogger<Muxer>>();
		var notificationService = new NotificationService();
		var muxer1 = new Muxer(logger, notificationService) { Channel = channel, Initiator = true };
		var foo = await muxer1.CreateStreamAsync("foo");
		var bar = await muxer1.CreateStreamAsync("bar");

		channel.Position = 0;
		var muxer2 = new Muxer(logger, notificationService) { Channel = channel };
		int n = 0;
		var sub = notificationService.Subscribe<Muxer.SubstreamCreated>(m => ++n);
		await muxer2.ProcessRequestsAsync();
		Assert.AreEqual(2, n);
		sub.Dispose();
	}

	[TestMethod]
	public async Task NewStream_AlreadyAssigned()
	{
		var channel = new MemoryStream();
		var logger = Mock.Of<ILogger<Muxer>>();
		var notificationService = new NotificationService();
		var muxer1 = new Muxer(logger, notificationService) { Channel = channel, Initiator = true };
		var foo = await muxer1.CreateStreamAsync("foo");
		var muxer2 = new Muxer(logger, notificationService) { Channel = channel, Initiator = true };
		var bar = await muxer2.CreateStreamAsync("bar");

		channel.Position = 0;
		var muxer3 = new Muxer(logger, notificationService) { Channel = channel };
		await muxer3.ProcessRequestsAsync(new CancellationTokenSource(500).Token);

		// The channel is closed because of 2 new streams with same id.
		Assert.IsFalse(channel.CanRead);
		Assert.IsFalse(channel.CanWrite);
	}

	[TestMethod]
	public async Task NewStream_Event()
	{
		var channel = new MemoryStream();
		var logger = Mock.Of<ILogger<Muxer>>();
		var notificationService = new NotificationService();
		var muxer1 = new Muxer(logger, notificationService) { Channel = channel, Initiator = true };
		var foo = await muxer1.CreateStreamAsync("foo");
		var bar = await muxer1.CreateStreamAsync("bar");

		channel.Position = 0;
		var muxer2 = new Muxer(logger, notificationService) { Channel = channel };
		int createCount = 0;
		var sub = notificationService.Subscribe<Muxer.SubstreamCreated>(m => ++createCount);
		await muxer2.ProcessRequestsAsync();
		Assert.AreEqual(2, createCount);
		sub.Dispose();
	}

	[TestMethod]
	public async Task CloseStream_Event()
	{
		var channel = new MemoryStream();
		var logger = Mock.Of<ILogger<Muxer>>();
		var notificationService = new NotificationService();
		var muxer1 = new Muxer(logger, notificationService) { Channel = channel, Initiator = true };
		using (var foo = await muxer1.CreateStreamAsync("foo"))
		using (var bar = await muxer1.CreateStreamAsync("bar"))
		{
			// open and close a stream.
		}

		channel.Position = 0;
		var muxer2 = new Muxer(logger, notificationService) { Channel = channel };
		int closeCount = 0;
		var sub = notificationService.Subscribe<Muxer.SubstreamClosed>(m => ++closeCount);
		await muxer2.ProcessRequestsAsync();
		Assert.AreEqual(2, closeCount);
		sub.Dispose();
	}

	[TestMethod]
	public async Task AcquireWrite()
	{
		var logger = Mock.Of<ILogger<Muxer>>();
		var notificationService = new NotificationService();
		var muxer = new Muxer(logger, notificationService);
		var tasks = new List<Task<string>>
			{
				Task.Run(async () =>
				{
					using (await muxer.AcquireWriteAccessAsync())
					{
						await Task.Delay(100);
					}

					return "step 1";
				}),
				Task.Run(async () =>
				{
					using (await muxer.AcquireWriteAccessAsync())
					{
						await Task.Delay(50);
					}

					return "step 2";
				}),
			};

		var done = await Task.WhenAll(tasks);
		Assert.AreEqual("step 1", done[0]);
		Assert.AreEqual("step 2", done[1]);
	}
}
