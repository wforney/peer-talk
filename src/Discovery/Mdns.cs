namespace PeerTalk.Discovery
{
	using Ipfs;
	using Makaretu.Dns;
	using Microsoft.Extensions.Logging;
	using SharedCode.Notifications;
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading.Tasks;

	/// <summary>
	/// Base class to discover peers using Multicast DNS.
	/// </summary>
	public abstract class Mdns : IPeerDiscovery
	{
		private readonly ILogger _logger;
		private readonly INotificationService _notificationService;

		/// <summary>
		/// Initializes a new instance of the <see cref="Mdns"/> class.
		/// </summary>
		/// <param name="logger">The logger.</param>
		/// <param name="notificationService">The notification service.</param>
		/// <exception cref="ArgumentNullException">logger</exception>
		/// <exception cref="ArgumentNullException">notificationService</exception>
		public Mdns(ILogger logger, INotificationService notificationService)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
		}

		/// <summary>
		///  The local peer.
		/// </summary>
		public Peer LocalPeer { get; set; }

		/// <summary>
		///   The Muticast Domain Name Service to use.
		/// </summary>
		public MulticastService MulticastService { get; set; }

		/// <summary>
		///   The service name for our peers.
		/// </summary>
		/// <value>
		///   Defaults to "ipfs".
		/// </value>
		public string ServiceName { get; set; } = "ipfs";

		/// <summary>
		///   Determines if the local peer responds to a query.
		/// </summary>
		/// <value>
		///   <b>true</b> to answer queries.  Defaults to <b>true</b>.
		/// </value>
		public bool Broadcast { get; set; } = true;

		/// <inheritdoc />
		public Task StartAsync()
		{
			MulticastService.NetworkInterfaceDiscovered += (s, e) =>
			{
				try
				{
					var profile = BuildProfile();
					var discovery = new ServiceDiscovery(MulticastService);
					OnServiceDiscovery(discovery);
					discovery.ServiceInstanceDiscovered += OnServiceInstanceDiscovered;

					if (Broadcast && !(profile is null))
					{
						_logger.LogDebug("Advertising {FullyQualifiedName}", profile.FullyQualifiedName);
						discovery.Advertise(profile);
					}

					// Ask all peers to broadcast discovery info.
					discovery.QueryServiceInstances(ServiceName);
				}
				catch (Exception ex)
				{
					// eat it
					_logger.LogDebug(ex, "Failed to send query");
				}
			};

			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public Task StopAsync() => Task.CompletedTask;

		void OnServiceInstanceDiscovered(object sender, ServiceInstanceDiscoveryEventArgs e)
		{
			try
			{
				var msg = e.Message;

				// Is it our service?
				var qsn = new DomainName($"{ServiceName}.local");
				if (!e.ServiceInstanceName.BelongsTo(qsn))
				{
					return;
				}

				var addresses = GetAddresses(msg)
					.Where(a => a.PeerId != LocalPeer.Id)
					.ToArray();
				if (addresses.Length > 0)
				{
					_notificationService.Publish(new PeerDiscovered(new Peer { Id = addresses[0].PeerId, Addresses = addresses }));
				}
			}
			catch (Exception ex)
			{
				// eat it
				_logger.LogError(ex, "OnServiceInstanceDiscovered error");
			}
		}

		/// <summary>
		///   Build the profile which contains the DNS records that are needed
		///   to locate and connect to the local peer.
		/// </summary>
		/// <returns>
		///   Describes the service.
		/// </returns>
		public abstract ServiceProfile BuildProfile();

		/// <summary>
		///   Get the addresses of the peer in the DNS message.
		/// </summary>
		/// <param name="message">
		///   An answer describing a peer.
		/// </param>
		/// <returns>
		///   All the addresses of the peer.
		/// </returns>
		public abstract IEnumerable<MultiAddress> GetAddresses(Message message);

		/// <summary>
		///   Allows derived class to modify the service discovery behavior.
		/// </summary>
		/// <param name="discovery"></param>
		protected virtual void OnServiceDiscovery(ServiceDiscovery discovery)
		{
		}
	}
}
