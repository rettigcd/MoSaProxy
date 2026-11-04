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
	static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(15);

	readonly WebApplication _app;
	readonly byte[] _token;
	readonly ApiEventFeed _feed;

	ApiHost(WebApplication app, int port, string token, ApiEventFeed feed) {
		_app = app;
		_feed = feed;
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
	/// <param name="rootStore">Where to look to see whether the root certificate is trusted; the current Windows user's store by default (on other systems the answer is "not known").</param>
	public static async Task<ApiHost> StartAsync(ProxyHost proxy, int port = 0, string? token = null, IRootStore? rootStore = null) {
		rootStore ??= OperatingSystem.IsWindows() ? new WindowsUserRootStore() : null;
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
		MapUi(app);
		var feed = new ApiEventFeed(proxy.Events);
		MapEndpoints(app.MapGroup("/api"), proxy, feed, app.Lifetime.ApplicationStopping, rootStore);
		await app.StartAsync();

		int boundPort = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;
		host = new ApiHost(app, boundPort, token, feed);
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
		// The UI's script and style are public: they hold nothing private, and a <script src> cannot carry the token.
		if (HttpMethods.IsGet(request.Method) && UiPublicFiles.Contains(request.Path.Value ?? "")) return null;
		string given = request.Headers[TokenHeader].FirstOrDefault() ?? request.Query["token"].FirstOrDefault() ?? "";
		// Compared in constant time, so the token cannot be found out a character at a time.
		if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), _token)) return StatusCodes.Status401Unauthorized;
		return null;
	}

	static readonly string[] UiPublicFiles = ["/app.js", "/app.css"];

	/// <summary>
	/// Serves the browser UI: the few files embedded in the assembly, each at its own path and nothing else, so no request can reach
	/// anything on disk. The page shows captured traffic, so it is locked down: it may load only its own script and style.
	/// </summary>
	static void MapUi(WebApplication app) {
		void Serve(string path, string resource, string contentType) {
			using var stream = typeof(ApiHost).Assembly.GetManifestResourceStream("ui/" + resource) ?? throw new InvalidOperationException("The UI file is missing from the assembly: " + resource);
			var content = new MemoryStream();
			stream.CopyTo(content);
			byte[] bytes = content.ToArray();
			app.MapGet(path, (HttpContext context) => {
					var headers = context.Response.Headers;
					headers.CacheControl = "no-store";
					headers["X-Content-Type-Options"] = "nosniff";
					headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' blob:; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
					return Results.Bytes(bytes, contentType);
				})
				.ExcludeFromDescription();
		}
		Serve("/", "index.html", "text/html; charset=utf-8");
		Serve("/app.js", "app.js", "text/javascript; charset=utf-8");
		Serve("/app.css", "app.css", "text/css; charset=utf-8");
	}

	static void MapEndpoints(RouteGroupBuilder api, ProxyHost proxy, ApiEventFeed feed, CancellationToken stopping, IRootStore? rootStore) {
		api.MapGet("/status", () => new ApiStatus(
				typeof(ApiHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0", proxy.Port, proxy.InterceptEnabled, proxy.Options.EnableHttp2,
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

		api.MapGet("/intercept", () => InterceptState(proxy, rootStore))
			.WithTags("Intercept").WithName("GetIntercept").WithSummary("Whether HTTPS interception is on")
			.WithDescription("Whether tunnels opened now are decrypted, the hosts that are never decrypted, and whether this machine trusts the root certificate that signs the decrypted connections (a client that does not trust it shows certificate errors). The certificate fields are empty until interception has been switched on once.");

		api.MapPut("/intercept", Results<Ok<ApiInterceptState>, ProblemHttpResult> (ApiInterceptRequest request) => {
				// Everything is checked before anything changes.
				if (request.Bypass is not null && request.Bypass.Select(h => h.Trim()).FirstOrDefault(h => h.Length > 0 && !ProxyHost.IsValidBypassPattern(h)) is { } bad)
					return TypedResults.Problem($"'{bad}' is not a host name or a *.suffix pattern.", statusCode: StatusCodes.Status400BadRequest);
				if (request.Enabled is { } enabled) {
					try { proxy.SetInterceptEnabled(enabled); }
					catch (InvalidOperationException ex) { return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict); }
				}
				if (request.Bypass is not null) proxy.SetInterceptBypass(request.Bypass);
				return TypedResults.Ok(InterceptState(proxy, rootStore));
			})
			.ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict)
			.WithTags("Intercept").WithName("SetIntercept").WithSummary("Turn HTTPS interception on or off, or change the hosts it skips")
			.WithDescription("Either field may be left out. It applies to tunnels opened from now on; a tunnel already open keeps the mode it began with. The first time interception is switched on the root certificate is made (409 if it cannot be). This never changes what the machine trusts: install the certificate in CaFile into the client's trust store yourself. Bypass replaces the whole list; entries are host names or *.suffix.");

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

		api.MapGet("/exchanges/{id:int}/request",(int id) =>
				proxy.Exchanges?.OpenRequest(id) is { } stream ? TypedResults.Stream(stream, "application/octet-stream") : NoSuchExchange(id, "a request"))
			.Produces<byte[]>(StatusCodes.Status200OK, "application/octet-stream").ProducesProblem(StatusCodes.Status404NotFound)
			.WithTags("Exchanges").WithName("GetRequestBytes").WithSummary("The request exactly as captured")
			.WithDescription("The request head and body as the bytes captured, including any chunk framing and content coding. For a CONNECT it is whatever followed the request.");

		api.MapGet("/exchanges/{id:int}/response", (int id) =>
				proxy.Exchanges?.OpenResponse(id) is { } stream ? TypedResults.Stream(stream, "application/octet-stream") : NoSuchExchange(id, "a response"))
			.Produces<byte[]>(StatusCodes.Status200OK, "application/octet-stream").ProducesProblem(StatusCodes.Status404NotFound)
			.WithTags("Exchanges").WithName("GetResponseBytes").WithSummary("The response exactly as captured")
			.WithDescription("The response head and body as the bytes captured, including any chunk framing and content coding.");

		api.MapGet("/exchanges/{id:int}/request/body", (int id, bool? decode, HttpContext context) => BodyResult(proxy, id, response: false, decode == true, context))
			.Produces<byte[]>(StatusCodes.Status200OK, "application/octet-stream").ProducesProblem(StatusCodes.Status404NotFound)
			.WithTags("Exchanges").WithName("GetRequestBody").WithSummary("The request body")
			.WithDescription("Just the body, served with the request's own Content-Type. With decode=true the chunk framing and any gzip, deflate or brotli coding are removed, and the X-MoSaProxy-Decoded response header says whether that worked (it stays false for a coding that is not known or a body that is damaged, and the body is then as captured).");

		api.MapGet("/exchanges/{id:int}/response/body", (int id, bool? decode, HttpContext context) => BodyResult(proxy, id, response: true, decode == true, context))
			.Produces<byte[]>(StatusCodes.Status200OK, "application/octet-stream").ProducesProblem(StatusCodes.Status404NotFound)
			.WithTags("Exchanges").WithName("GetResponseBody").WithSummary("The response body")
			.WithDescription("Just the body, served with the response's own Content-Type. With decode=true the chunk framing and any gzip, deflate or brotli coding are removed, and the X-MoSaProxy-Decoded response header says whether that worked (it stays false for a coding that is not known or a body that is damaged, and the body is then as captured).");

		api.MapGet("/exchanges/{id:int}/websocket", (int id) =>
				proxy.Exchanges?.OpenWebSocketLog(id) is { } stream ? TypedResults.Stream(stream, "text/plain; charset=utf-8") : NoSuchExchange(id, "a WebSocket log"))
			.Produces<string>(StatusCodes.Status200OK, "text/plain").ProducesProblem(StatusCodes.Status404NotFound)
			.WithTags("Exchanges").WithName("GetWebSocketLog").WithSummary("The log of a WebSocket exchange's messages")
			.WithDescription("The readable log the proxy wrote for the messages sent each way. Only exchanges whose detail says a WebSocket log exists have one.");

		api.MapGet("/export/saz", (string? ids) => ExportSaz(proxy, ids))
			.Produces<byte[]>(StatusCodes.Status200OK, "application/zip").ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
			.WithTags("Export").WithName("ExportSaz").WithSummary("Download the captured exchanges as a .saz archive")
			.WithDescription("A Fiddler Session Archive of every finished exchange, or only those listed in ids (comma-separated numbers; ones that do not exist or have not finished are left out). 404 if that leaves nothing. The archive is built when asked for, so it holds what was captured up to then.");

		api.MapPost("/stop", () => {
				proxy.RequestStop();
				return TypedResults.Accepted((string?)null);
			})
			.WithTags("Status").WithName("StopProxy").WithSummary("Start the graceful shutdown")
			.WithDescription("Does what the first Ctrl+C does: the proxy stops taking new connections, waits for running exchanges to finish (the drain period), and the program exits, deleting the capture folder. Answers 202 at once and does not wait; the API itself goes away when the program exits.");

		api.MapGet("/stream", (HttpContext context, CancellationToken _) => StreamEvents(context, feed, stopping))
			.Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
			.WithTags("Stream").WithName("StreamEvents").WithSummary("Live events as exchanges are captured")
			.WithDescription("Server-Sent Events, kept open. Each event has an id that only grows, a name and JSON data. Events: exchange-started {id}; exchange-finished {id} (the exchange can now be read); websocket-message {exchange, fromClient, type, totalBytes}; capture {enabled}; intercept {enabled}; bypass {hosts}; reset {} (sent when the events asked for after Last-Event-ID are no longer remembered: fetch the exchange list again). A connection with no Last-Event-ID (header, or the lastEventId query parameter) gets only what happens from then on; one with it first gets the events it missed. A comment line is sent every 15 seconds to keep the connection alive. A browser's EventSource cannot set headers, so pass the token as the token query parameter.");
	}

	static IResult ExportSaz(ProxyHost proxy, string? ids) {
		if (proxy.Exchanges is not { } store || proxy.CaptureRunDirectory is not { } folder) return TypedResults.NotFound(NotFoundProblem("There is nothing captured to export."));
		var wanted = new List<int>();
		foreach (string part in (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
			if (!int.TryParse(part, out int number)) return TypedResults.Problem($"'{part}' in ids is not an exchange number.", statusCode: StatusCodes.Status400BadRequest);
			wanted.Add(number);
		}
		// Only exchanges that have finished: one still running has request bytes but no metadata yet.
		var finished = store.List(new ExchangeQuery { Limit = int.MaxValue }).Select(e => e.Id).Where(n => ids is null || wanted.Contains(n)).ToList();
		if (finished.Count == 0) return TypedResults.NotFound(NotFoundProblem("No finished captured exchange matches."));

		string path = Path.Combine(Path.GetTempPath(), $"MoSaProxy-export-{Guid.NewGuid():N}.saz");
		SazExporter.Export(folder, path, finished);
		// (deleted as soon as the response has been sent)
		var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.DeleteOnClose);
		return TypedResults.File(file, "application/zip", "mosaproxy.saz");
	}

	static ProblemDetails NotFoundProblem(string detail) => new() { Title = "Nothing to export", Detail = detail, Status = StatusCodes.Status404NotFound };

	static async Task StreamEvents(HttpContext context, ApiEventFeed feed, CancellationToken stopping) {
		using var cancel = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stopping);
		var token = cancel.Token;
		var response = context.Response;
		response.ContentType = "text/event-stream";
		response.Headers.CacheControl = "no-cache";
		string? given = context.Request.Headers["Last-Event-ID"].FirstOrDefault() ?? context.Request.Query["lastEventId"].FirstOrDefault();
		long last = long.TryParse(given, out long id) && id >= 0 ? id : feed.Last;
		try {
			await response.WriteAsync(": connected\n\n", token);
			await response.Body.FlushAsync(token);
			while (!token.IsCancellationRequested) {
				var (events, missed, next) = feed.After(last);
				var text = new StringBuilder();
				if (missed) text.Append("event: reset\ndata: {}\n\n");
				foreach (var e in events) {
					text.Append($"id: {e.Id}\nevent: {e.Name}\ndata: {e.Json}\n\n");
					last = e.Id;
				}
				if (text.Length > 0) {
					await response.WriteAsync(text.ToString(), token);
					await response.Body.FlushAsync(token);
				}
				if (events.Count > 0) continue;       // (more may have arrived while writing)
				if (await Task.WhenAny(next, Task.Delay(KeepAlive, token)) != next) {
					await response.WriteAsync(": keep-alive\n\n", token);
					await response.Body.FlushAsync(token);
				}
			}
		}
		catch (OperationCanceledException) { }       // the reader left, or the API is stopping
	}

	static ApiInterceptState InterceptState(ProxyHost proxy, IRootStore? rootStore) {
		var authority = proxy.Authority;
		bool? trusted = null;
		if (authority is not null && rootStore is not null) {
			try { trusted = new RootTrust(rootStore).Check(authority) == RootTrustState.Trusted; }
			catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException) { }
		}
		return new ApiInterceptState(proxy.InterceptEnabled, proxy.InterceptBypass, trusted, authority?.Root.Subject, authority?.RootCertificatePath);
	}

	static IResult BodyResult(ProxyHost proxy, int id, bool response, bool decode, HttpContext context) {
		if (proxy.Exchanges?.Body(id, response, decode) is not { } body) return NoSuchExchange(id, response ? "a response" : "a request");
		if (decode) context.Response.Headers["X-MoSaProxy-Decoded"] = body.Decoded ? "true" : "false";
		string type = body.ContentType is { } t && System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(t, out _) ? t : "application/octet-stream";
		return TypedResults.Bytes(body.Bytes, type);
	}

	static IResult NoSuchExchange(int id, string what) =>
		TypedResults.NotFound(new ProblemDetails { Title = "No such " + what, Detail = $"There is no finished captured exchange number {id} with {what}.", Status = StatusCodes.Status404NotFound });

	public async ValueTask DisposeAsync() {
		_feed.Dispose();
		await _app.StopAsync();
		await _app.DisposeAsync();
	}
}
