namespace PeerTalk
{
	using Microsoft.Extensions.DependencyInjection;
	using PeerTalk.Discovery;
	using PeerTalk.Protocols;
	using PeerTalk.SecureCommunication;
	using PeerTalk.Transports;
	using SharedCode.Notifications;

	/// <summary>
	/// The service collection extension method class.
	/// </summary>
	public static class ServiceCollectionExtensions
	{
		/// <summary>
		/// Adds the peer talk.
		/// </summary>
		/// <param name="services">The services.</param>
		/// <returns>IServiceCollection.</returns>
		public static IServiceCollection AddPeerTalk(this IServiceCollection services)
		{
			return services
				.AddScoped<AutoDialer>()
				.AddScoped<Bootstrap>()
				.AddScoped<PubSub.FloodRouter>()
				.AddScoped<Identify1>()
				.AddScoped<PubSub.LoopbackRouter>()
				.AddScoped<Message>()
				.AddScoped<Mplex67>()
				.AddScoped<Multistream1>()
				.AddScoped<INotificationService, NotificationService>()
				.AddScoped<PubSub.NotificationService>()
				.AddTransient<PeerConnection>()
				.AddScoped<Ping1>()
				.AddScoped<Plaintext1>()
				.AddScoped<ProtocolRegistry>()
				.AddScoped<Secio1>()
				.AddScoped<Swarm>()
				.AddScoped<TransportRegistry>();
		}
	}
}
