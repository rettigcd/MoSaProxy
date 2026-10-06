using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace mos.Tests;

/// <summary>What the fake origin saw for one request.</summary>
public sealed record SeenRequest(string Method, string PathAndQuery, IHeaderDictionary Headers, byte[] Body, string ConnectionId);

/// <summary>A small in-process HTTP server on loopback that stands in for a real web site.</summary>
public sealed class TestOrigin : IAsyncDisposable {
	readonly WebApplication _app;
	readonly List<SeenRequest> _seen = new();

	TestOrigin(WebApplication app, int port) { _app = app; Port = port; }

	public int Port { get; }
	public IReadOnlyList<SeenRequest> Seen { get { lock (_seen) return _seen.ToArray(); } }

	public static async Task<TestOrigin> StartAsync(Func<HttpContext, Task> respond) {
		var builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
		var app = builder.Build();
		TestOrigin? origin = null;
		app.Run(async context => {
			using var buffer = new MemoryStream();
			await context.Request.Body.CopyToAsync(buffer);
			var seen = new SeenRequest(context.Request.Method, context.Request.Path + context.Request.QueryString,
				new HeaderDictionary(context.Request.Headers.ToDictionary(h => h.Key, h => h.Value)), buffer.ToArray(), context.Connection.Id);
			lock (origin!._seen) origin._seen.Add(seen);
			await respond(context);
		});
		await app.StartAsync();
		var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
		origin = new TestOrigin(app, new Uri(address).Port);
		return origin;
	}

	public string UrlFor(string path) => $"http://127.0.0.1:{Port}{path}";

	public async ValueTask DisposeAsync() {
		await _app.StopAsync();
		await _app.DisposeAsync();
	}
}
