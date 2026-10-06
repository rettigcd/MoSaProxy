using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace mos;

/// <summary>Hosts the proxy on a Kestrel listener bound to loopback only.</summary>
public sealed class ProxyHost : IAsyncDisposable {
	readonly WebApplication _app;
	readonly ProxyHandler _handler;

	ProxyHost(WebApplication app, ProxyHandler handler, int port) {
		_app = app;
		_handler = handler;
		Port = port;
	}

	/// <summary>The port actually bound (useful when the options asked for port 0).</summary>
	public int Port { get; }

	public static async Task<ProxyHost> StartAsync(ProxyOptions options, IExchangeLog log) {
		var builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.ConfigureKestrel(kestrel => {
			kestrel.AddServerHeader = false;
			kestrel.Limits.MaxRequestBodySize = null;
			kestrel.Listen(IPAddress.Loopback, options.Port);
		});

		var app = builder.Build();
		var handler = new ProxyHandler(log, options);
		app.Run(handler.HandleAsync);
		await app.StartAsync();

		var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
		return new ProxyHost(app, handler, new Uri(address).Port);
	}

	public Task WaitForShutdownAsync() => _app.WaitForShutdownAsync();

	public async ValueTask DisposeAsync() {
		await _app.StopAsync();
		await _app.DisposeAsync();
		_handler.Dispose();
	}
}
