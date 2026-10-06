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
	readonly CancellationTokenSource _shutdown;

	ProxyHost(WebApplication app, ProxyHandler handler, CancellationTokenSource shutdown, int port, string? captureRunDirectory) {
		_app = app;
		_shutdown = shutdown;
		_handler = handler;
		Port = port;
		CaptureRunDirectory = captureRunDirectory;
	}

	/// <summary>The port actually bound (useful when the options asked for port 0).</summary>
	public int Port { get; }

	/// <summary>The folder this run's capture files go into, or null when capture is off or supplied by the caller.</summary>
	public string? CaptureRunDirectory { get; }

	public static async Task<ProxyHost> StartAsync(ProxyOptions options, IExchangeLog log, ISessionCapture? capture = null) {
		string? runDirectory = null;
		if (capture is null) {
			if (options.CaptureDirectory is null) capture = NullSessionCapture.Instance;
			else {
				var files = new FileSessionCapture(options.CaptureDirectory, options.CaptureMaxBodyBytes);
				runDirectory = files.RunDirectory;
				capture = files;
			}
		}

		var shutdown = new CancellationTokenSource();
		var tunnels = new TunnelHandler(log, capture, options, shutdown.Token);
		var builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.ConfigureKestrel(kestrel => {
			kestrel.AddServerHeader = false;
			kestrel.Limits.MaxRequestBodySize = null;
			// The tunnel handler sits in front of Kestrel's HTTP handling and takes over CONNECT connections.
			kestrel.Listen(IPAddress.Loopback, options.Port, listen => listen.Use(next => connection => tunnels.HandleAsync(connection, next)));
		});

		var app = builder.Build();
		var handler = new ProxyHandler(log, capture, options);
		app.Run(handler.HandleAsync);
		await app.StartAsync();

		var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
		return new ProxyHost(app, handler, shutdown, new Uri(address).Port, runDirectory);
	}

	public Task WaitForShutdownAsync() => _app.WaitForShutdownAsync();

	public async ValueTask DisposeAsync() {
		_shutdown.Cancel(); // end any open tunnels so stopping does not wait for them
		await _app.StopAsync();
		await _app.DisposeAsync();
		_handler.Dispose();
	}
}
