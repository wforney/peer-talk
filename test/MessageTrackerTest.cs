namespace PeerTalk;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

[TestClass]
    public class MessageTrackerTest
    {
        [TestMethod]
        public void Tracking()
        {
            var tracker = new MessageTracker();
            var now = DateTime.Now;
            Assert.IsFalse(tracker.RecentlySeen("a", now));
            Assert.IsTrue(tracker.RecentlySeen("a", now));
            Assert.IsFalse(tracker.RecentlySeen("a", now + tracker.Recent));
        }

    }
