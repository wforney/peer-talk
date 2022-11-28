namespace PeerTalk.Discovery
{
    using Ipfs;
    using Makaretu.Dns;
    using Microsoft.Extensions.Logging;
    using SharedCode.Notifications;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    /// <summary>
    ///   Discovers peers using Multicast DNS according to
    ///   <see href="https://github.com/libp2p/specs/blob/master/discovery/mdns.md"/>
    /// </summary>
    public class MdnsNext : Mdns
    {
        /// <summary>
        ///   Creates a new instance of the class.  Sets the <see cref="Mdns.ServiceName"/>
        ///   to "_p2p._udp".
        /// </summary>
        public MdnsNext(ILogger<MdnsNext> logger, INotificationService notificationService) : base(logger, notificationService)
        {
            ServiceName = "_p2p._udp";
        }

        /// <inheritdoc />
        public override ServiceProfile BuildProfile()
        {
            var profile = new ServiceProfile(
                instanceName: SafeLabel(LocalPeer.Id.ToBase32()),
                serviceName: ServiceName,
                port: 0
            );

            // The TXT records contain the multi addresses.
            _ = profile.Resources.RemoveAll(r => r is TXTRecord);
            foreach (var address in LocalPeer.Addresses)
            {
                profile.Resources.Add(new TXTRecord
                {
                    Name = profile.FullyQualifiedName,
                    Strings = { $"dnsaddr={address}" }
                });
            }

            return profile;
        }

        /// <inheritdoc />
        public override IEnumerable<MultiAddress> GetAddresses(Message message)
        {
            return message.AdditionalRecords
                .OfType<TXTRecord>()
                .SelectMany(t => t.Strings)
                .Where(s => s.StartsWith("dnsaddr="))
                .Select(s => s.Substring(8))
                .Select(s => MultiAddress.TryCreate(s))
                .Where(a => a != null);
        }

        /// <summary>
        ///   Creates a safe DNS label.
        /// </summary>
        /// <param name="label"></param>
        /// <param name="maxLength"></param>
        /// <returns></returns>
        public static string SafeLabel(string label, int maxLength = 63)
        {
            if (label.Length <= maxLength)
            {
                return label;
            }

            var sb = new StringBuilder();
            while (label.Length > maxLength)
            {
                _ = sb.Append(label.Substring(0, maxLength));
                _ = sb.Append('.');
                label = label.Substring(maxLength);
            }

            _ = sb.Append(label);
            return sb.ToString();
        }
    }
}
