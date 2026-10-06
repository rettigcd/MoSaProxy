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

	ProxyHost(WebApplication app, ProxyHandler handler, int port, string? captureRunDirectory) {
		_app = app;
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

		var builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.ConfigureKestrel(kestrel => {
			kestrel.AddServerHeader = false;
			kestrel.Limits.MaxRequestBodySize = null;
			kestrel.Listen(IPAddress.Loopback, options.Port);
		});

		var app = builder.Build();
		var handler = new ProxyHandler(log, capture, options);
		app.Run(handler.HandleAsync);
		await app.StartAsync();

		var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
		return new ProxyHost(app, handler, new Uri(address).Port, runDirectory);
	}

	public Task WaitForShutdownAsync() => _app.WaitForShutdownAsync();

	public async ValueTask DisposeAsync() {
		await _app.StopAsync();
		await _app.DisposeAsync();
		_handler.Dispose();
	}
}
