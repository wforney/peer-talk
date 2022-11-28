namespace PeerTalkTests;

using Microsoft.Extensions.DependencyInjection;
using PeerTalk;
using System;

public static class TestSetup
{
	public static IServiceProvider GetScopedServiceProvider()
	{
		var serviceProvider = GetServiceProvider();
		var serviceScope = serviceProvider.CreateScope();

		return serviceScope.ServiceProvider;
	}

	public static IServiceProvider GetServiceProvider()
	{
		var services = new ServiceCollection()
			.AddLogging()
			.AddPeerTalk();

		return services.BuildServiceProvider();
	}
}
