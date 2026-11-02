using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MoSaProxy;

/// <summary>
/// The local API: a second Kestrel listener, on loopback only, that lets a front end (the browser UI) read what the proxy captured and
/// switch capture on and off. It is a thin layer over <see cref="ProxyHost"/> (its <see cref="ProxyHost.Exchanges"/>, events and
/// capture switch). It shows credentials and cookies, and a web page on another site could try to call a local server, so every
/// request is checked: the <c>Host</c> header must name this listener (against DNS rebinding), a <c>Origin</c> header must be this
/// listener's own, and a per-run token must be given.
/// </summary>
public sealed class ApiHost : IAsyncDisposable {
	public const string TokenHeader = "X-MoSaProxy-Token";
	const int DefaultPageSize = 100, MaxPageSize = 1000;

	readonly WebApplication _app;
	readonly byte[] _token;

	ApiHost(WebApplication app, int port, string token) {
		_app = app;
		Port = port;
		Token = token;
		_token = Encoding.UTF8.GetBytes(token);
	}

	/// <summary>The port the API listens on (loopback).</summary>
	public int Port { get; }

	/// <summary>The per-run secret every request must carry, in the <see cref="TokenHeader"/> header or the <c>token</c> query parameter (an event stream cannot set headers).</summary>
	public string Token { get; }

	/// <summary>The API's address, with the token, as it can be opened or given to a client.</summary>
	public string Url => $"http://127.0.0.1:{Port}/?token={Token}";

	/// <param name="port">0 picks a free port.</param>
	/// <param name="token">The secret; a random one is made if none is given.</param>
	public static async Task<ApiHost> StartAsync(ProxyHost proxy, int port = 0, string? token = null) {
		token ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
		var builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.ConfigureKestrel(kestrel => {
			kestrel.AddServerHeader = false;
			kestrel.Listen(IPAddress.Loopback, port);
		});
		builder.Services.AddOpenApi("v1", options => options.AddDocumentTransformer((document, _, _) => {
			// Fixed values, so the document does not change with the port or the build.
			document.Info = new() { Title = "MoSaProxy local API", Version = "1", Description = "Read what the proxy captured and switch capture on and off." };
			document.Servers = null;
			return Task.CompletedTask;
		}));

		var app = builder.Build();
		ApiHost? host = null;
		app.Use(async (context, next) => {
			if (host!.Reject(context) is { } status) {
				context.Response.StatusCode = status;
				return;
			}
			await next();
		});
		app.MapOpenApi();
		MapEndpoints(app.MapGroup("/api"), proxy);
		await app.StartAsync();

		int boundPort = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;
		host = new ApiHost(app, boundPort, token);
		return host;
	}

	/// <summary>The status to refuse a request with, or null to let it through.</summary>
	int? Reject(HttpContext context) {
		var request = context.Request;
		string[] hosts = [$"127.0.0.1:{Port}", $"localhost:{Port}", $"[::1]:{Port}"];
		// A request with another Host header reached this listener under a name that is not ours: the sign of DNS rebinding.
		if (!hosts.Contains(request.Host.Value, StringComparer.OrdinalIgnoreCase)) return StatusCodes.Status403Forbidden;
		// A page on another site that calls this API carries its own site as the Origin.
		if (request.Headers.Origin is { Count: > 0 } origin && !hosts.Select(h => "http://" + h).Contains(origin.ToString(), StringComparer.OrdinalIgnoreCase))
			return StatusCodes.Status403Forbidden;
		string given = request.Headers[TokenHeader].FirstOrDefault() ?? request.Query["token"].FirstOrDefault() ?? "";
		// Compared in constant time, so the token cannot be found out a character at a time.
		if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), _token)) return StatusCodes.Status401Unauthorized;
		return null;
	}

	static void MapEndpoints(RouteGroupBuilder api, ProxyHost proxy) {
		api.MapGet("/status", () => new ApiStatus(
				typeof(ApiHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0", proxy.Port, proxy.Options.Intercept, proxy.Options.EnableHttp2,
				proxy.CaptureEnabled, proxy.Exchanges?.Count ?? 0, proxy.CaptureRunDirectory, proxy.Options.LogPath))
			.WithTags("Status").WithName("GetStatus").WithSummary("How the proxy is set up and what it has captured")
			.WithDescription("The proxy's port, whether it intercepts HTTPS and offers HTTP/2, whether capture is on, how many exchanges are captured, and the capture folder and log file.");

		api.MapGet("/capture", () => new ApiCaptureState(proxy.CaptureEnabled, proxy.Exchanges?.Count ?? 0))
			.WithTags("Capture").WithName("GetCapture").WithSummary("Whether capture is on")
			.WithDescription("While capture is off, traffic is still forwarded and logged; only the captured exchanges stop growing.");

		api.MapPut("/capture", (ApiCaptureRequest request) => {
				proxy.CaptureEnabled = request.Enabled;
				return new ApiCaptureState(proxy.CaptureEnabled, proxy.Exchanges?.Count ?? 0);
			})
			.WithTags("Capture").WithName("SetCapture").WithSummary("Turn capture on or off")
			.WithDescription("An exchange already running is not affected: it is captured to its end if it began while capture was on, and not at all if it began while off.");

		api.MapGet("/exchanges", Results<Ok<ApiExchangePage>, ProblemHttpResult> (int? after, int? limit, string? host, string? method, int? status, string? mode, string? q) => {
				int size = limit ?? DefaultPageSize;
				if (size < 1 || size > MaxPageSize) return TypedResults.Problem($"limit must be from 1 to {MaxPageSize}.", statusCode: StatusCodes.Status400BadRequest);
				var items = proxy.Exchanges?.List(new ExchangeQuery { After = after, Limit = size, Host = host, Method = method, Status = status, Mode = mode, Text = q }) ?? [];
				return TypedResults.Ok(new ApiExchangePage(items, items.Count == size ? items[^1].Id : null));
			})
			.WithTags("Exchanges").WithName("ListExchanges").WithSummary("A page of captured exchanges, oldest first")
			.WithDescription("Only exchanges that have finished are listed. Pass the previous page's next value as after to get the following page, or the highest number already seen to get what is new. host, mode and q match text contained in the value (any case); method must match exactly (any case).");

		api.MapGet("/exchanges/{id:int}", Results<Ok<ExchangeDetail>, NotFound<ProblemDetails>> (int id) =>
				proxy.Exchanges?.Detail(id) is { } detail
					? TypedResults.Ok(detail)
					: TypedResults.NotFound(new ProblemDetails { Title = "No such exchange", Detail = $"There is no finished captured exchange number {id}.", Status = StatusCodes.Status404NotFound }))
			.WithTags("Exchanges").WithName("GetExchange").WithSummary("One captured exchange")
			.WithDescription("The summary, the request and response heads with their headers in the order captured, timings, where the captured bytes came from, and whether a WebSocket log exists.");
	}

	public async ValueTask DisposeAsync() {
		await _app.StopAsync();
		await _app.DisposeAsync();
	}
}
