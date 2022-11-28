namespace PeerTalk.PubSub
{
    using Ipfs;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    ///   Maintains the sequence of peer's that are interested in a topic.
    /// </summary>
    public class TopicManager
    {
        private static readonly IEnumerable<Peer> nopeers = Enumerable.Empty<Peer>();
        private readonly ConcurrentDictionary<string, HashSet<Peer>> topics = new ConcurrentDictionary<string, HashSet<Peer>>();

        /// <summary>
        ///   Get the peers interested in a topic.
        /// </summary>
        /// <param name="topic">
        ///   The topic of interest or <b>null</b> for all topics.
        /// </param>
        /// <returns>
        ///   A sequence of <see cref="Peer"/> that are interested
        ///   in the <paramref name="topic"/>.
        /// </returns>
        public IEnumerable<Peer> GetPeers(string topic) =>
            topic is null
                ? topics.Values.SelectMany(v => v)
                : topics.TryGetValue(topic, out HashSet<Peer> peers)
                    ? peers
                    : nopeers;

        /// <summary>
        ///   Gets the topics that a peer is interested in
        /// </summary>
        /// <param name="peer">
        ///   The <see cref="Peer"/>.
        /// </param>
        /// <returns>
        ///   A sequence of topics that the <paramref name="peer"/> is
        ///   interested in.
        /// </returns>
        public IEnumerable<string> GetTopics(Peer peer) =>
            topics
                .Where(kp => kp.Value.Contains(peer))
                .Select(kp => kp.Key);

        /// <summary>
        ///   Indicate that the <see cref="Peer"/> is interested in the
        ///   topic.
        /// </summary>
        /// <param name="topic">
        ///   The topic of interest.
        /// </param>
        /// <param name="peer">
        ///   A <see cref="Peer"/>
        /// </param>
        /// <remarks>
        ///   Duplicates are ignored.
        /// </remarks>
        public void AddInterest(string topic, Peer peer) =>
            topics.AddOrUpdate(
                topic,
                (key) => new HashSet<Peer> { peer },
                (key, peers) =>
                {
                    _ = peers.Add(peer);
                    return peers;
                });

        /// <summary>
        ///   Indicate that the <see cref="Peer"/> is not interested in the
        ///   topic.
        /// </summary>
        /// <param name="topic">
        ///   The topic of interest.
        /// </param>
        /// <param name="peer">
        ///   A <see cref="Peer"/>
        /// </param>
        public void RemoveInterest(string topic, Peer peer) =>
            topics.AddOrUpdate(
                topic,
                (key) => new HashSet<Peer>(),
                (Key, list) =>
                {
                    _ = list.Remove(peer);
                    return list;
                });

        /// <summary>
        ///   Indicates that the peer is not interested in anything.
        /// </summary>
        /// <param name="peer">
        ///   The <see cref="Peer"/>.s
        /// </param>
        public void Clear(Peer peer)
        {
            foreach (var topic in topics.Keys)
            {
                RemoveInterest(topic, peer);
            }
        }

        /// <summary>
        ///   Remove all topics.
        /// </summary>
        public void Clear()
        {
            topics.Clear();
        }
    }
}
