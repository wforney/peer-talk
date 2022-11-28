namespace PeerTalk.Discovery
{
	using Ipfs;
	using Microsoft.Extensions.Logging;
	using SharedCode.Notifications;
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading.Tasks;

	/// <summary>
	/// Discovers the pre-configured peers.
	/// </summary>
	public class Bootstrap : IPeerDiscovery
	{
		private readonly ILogger<Bootstrap> _logger;
		private readonly INotificationService _notificationService;

		/// <summary>
		/// Initializes a new instance of the <see cref="Bootstrap" /> class.
		/// </summary>
		/// <param name="logger">The logger.</param>
		/// <param name="notificationService">The notification service.</param>
		/// <exception cref="ArgumentNullException">logger</exception>
		/// <exception cref="ArgumentNullException">notificationService</exception>
		public Bootstrap(ILogger<Bootstrap> logger, INotificationService notificationService)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
		}

		/// <summary>
		/// The addresses of the pre-configured peers.
		/// </summary>
		/// <value>
		/// Each address must end with the ipfs protocol and the public ID of the peer. For example "/ip4/104.131.131.82/tcp/4001/ipfs/QmaCpDMGvV2BGHeYERUEnRQAwe3N8SzbUtfsmvsqQLuvuJ"
		/// </value>
		public IEnumerable<MultiAddress> Addresses { get; set; }

		/// <inheritdoc />
		public Task StartAsync()
		{
			_logger.LogDebug("Starting");
			if (Addresses is null)
			{
				_logger.LogWarning("No bootstrap addresses");
				return Task.CompletedTask;
			}

			var peers = Addresses
				.Where(a => a.HasPeerId)
				.GroupBy(
					a => a.PeerId,
					a => a,
					(key, g) => new Peer { Id = key, Addresses = g.ToList() });
			foreach (var peer in peers)
			{
				try
				{
					_notificationService.Publish(new PeerDiscovered(peer));
				}
				catch (Exception e)
				{
					_logger.LogError(e, "{Error}", e.Message);
					continue; // silently ignore
				}
			}

			return Task.CompletedTask;
		}

		/// <inheritdoc />
		public Task StopAsync()
		{
			_logger.LogDebug("Stopping");
			return Task.CompletedTask;
		}
	}
}
