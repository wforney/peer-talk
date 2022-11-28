﻿namespace PeerTalk.Protocols;

using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.IO;
using System.Threading.Tasks;

[TestClass]
public class MessageTest
{
	[TestMethod]
	public async Task Encoding()
	{
		var ms = new MemoryStream();
		var message = new Message(Mock.Of<ILogger<Message>>());
		await message.WriteAsync("a", ms);
		var buf = ms.ToArray();
		Assert.AreEqual(3, buf.Length);
		Assert.AreEqual(2, buf[0]);
		Assert.AreEqual((byte)'a', buf[1]);
		Assert.AreEqual((byte)'\n', buf[2]);
	}

	[TestMethod]
	public async Task RoundTrip()
	{
		var msg = "/foobar/0.42.0";
		var ms = new MemoryStream();
		var message = new Message(Mock.Of<ILogger<Message>>());
		await message.WriteAsync(msg, ms);
		ms.Position = 0;
		var result = await message.ReadStringAsync(ms);
		Assert.AreEqual(msg, result);
	}
}
