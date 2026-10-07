using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;

namespace MoSaProxy;

/// <summary>Forwards plain-HTTP proxy requests to their origin, logs each exchange and captures its files.</summary>
public sealed class ProxyHandler : IDisposable {
	readonly HttpClient _client;
	readonly IExchangeLog _log;
	readonly ISessionCapture _capture;
	readonly ProxyOptions _options;
	readonly WebSocketProxy _webSockets;
	readonly ProxyActivity _activity;

	readonly ResourceLimitMonitor _limits;
	readonly WireTaps? _wireTaps;

	public ProxyHandler(IExchangeLog log, ISessionCapture capture, ProxyOptions options, ProxyActivity activity, ResourceLimitMonitor limits,
		CancellationToken stopping = default, CancellationToken shutdown = default, WireTaps? wireTaps = null) {
		_wireTaps = wireTaps;
		_log = log;
		_activity = activity;
		_limits = limits;
		_capture = capture;
		_options = options;
		var handler = new SocketsHttpHandler {
			UseProxy = false,
			AllowAutoRedirect = false,
			UseCookies = false,
			AutomaticDecompression = System.Net.DecompressionMethods.None,
		};
		if (wireTaps is not null)
			handler.PlaintextStreamFilter = (context, _) => ValueTask.FromResult(wireTaps.TapOrigin(context));
		if (options.OriginCertificateValidation is { } validation)
			handler.SslOptions.RemoteCertificateValidationCallback = validation;
		_client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
		_webSockets = new WebSocketProxy(_client, options, limits, stopping, shutdown);
	}

	public void Dispose() => _client.Dispose();

	public async Task HandleAsync(HttpContext context) {
		var request = context.Request;
		long started = Stopwatch.GetTimestamp();
		string rawTarget = context.Features.GetRequiredFeature<IHttpRequestFeature>().RawTarget;

		IExchangeCapture capture;
		try { capture = _capture.BeginExchange(); }
		catch (Exception ex) {
			Console.Error.WriteLine($"Capture unavailable: {ex.Message}");
			capture = NullExchangeCapture.Instance;
		}

		// Inside an intercepted (decrypted) tunnel the target is just a path; the host comes from the CONNECT request.
		Uri? interceptBase = null;
		if (context.Features.Get<IConnectionItemsFeature>()?.Items.TryGetValue(TunnelHandler.InterceptTargetKey, out var interceptItem) == true)
			interceptBase = interceptItem as Uri;
		Uri? uri;
		if (interceptBase is not null)
			uri = rawTarget.StartsWith('/') && Uri.TryCreate(interceptBase, rawTarget, out var combined) ? combined : null;
		else
			Uri.TryCreate(rawTarget, UriKind.Absolute, out uri);
		// Counted so a shutdown can report what it is waiting for: a WebSocket is long-lived, any other request is an exchange.
		bool isWebSocket = context.WebSockets.IsWebSocketRequest && uri is not null && (interceptBase is not null || uri.Scheme == Uri.UriSchemeHttp);
		using var tracking = _activity.Track(isWebSocket ? ActivityKind.WebSocket : ActivityKind.Exchange);
		var counted = new CountingStream(request.Body);
		var connectionWire = _wireTaps?.ForConnection(context.Connection.Id);
		ExchangeWire? exchangeWire = null;
		long responseBytes = 0;
		string mode = interceptBase is null ? "plain" : "intercept";
		string? error = null;
		string? originVersion = null;
		int? statusOverride = null;
		bool nothingSent = false;      // the exchange ended before any response reached the client, so there is no status to report
		long? requestBytesOverride = null;
		int? clientMessages = null, originMessages = null;

		ExchangeRecord MakeRecord(string recordMode, int? recordStatus, long requestBytes, long recordResponseBytes, string? recordError) => new() {
			Timestamp = DateTimeOffset.UtcNow,
			Client = context.Connection.RemoteIpAddress?.ToString() ?? "",
			Scheme = uri?.Scheme ?? "",
			Host = uri?.Host ?? "",
			Port = uri?.Port ?? 0,
			Method = request.Method,
			Url = uri?.ToString() ?? rawTarget,
			ClientHttpVersion = request.Protocol,
			OriginHttpVersion = originVersion,
			Status = recordStatus,
			RequestHeaders = HeaderRules.ForLog(request.Headers),
			ResponseHeaders = HeaderRules.ForLog(context.Response.Headers),
			RequestBytes = requestBytes,
			ResponseBytes = recordResponseBytes,
			DurationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
			Mode = recordMode,
			Error = recordError,
			ClientMessages = clientMessages,
			OriginMessages = originMessages,
		};

		try {
			capture.WriteRequestHead(request.Method, rawTarget, request.Protocol,
				request.Headers.Select(h => KeyValuePair.Create(h.Key, h.Value.ToString())));

			if (isWebSocket) {
				// A WebSocket connection (an HTTP/1.1 upgrade, or HTTP/2 extended CONNECT) lives for as long as the client keeps it.
				// The handshake is logged as soon as it succeeds, and a second record with the totals when the connection closes.
				mode = "websocket";
				connectionWire?.StopRecording();      // from here the connection carries WebSocket frames, not HTTP messages
				var summary = await _webSockets.RunAsync(context, capture, uri!, () => _log.Write(MakeRecord("websocket", 101, 0, 0, null)));
				mode = summary.Status == 101 ? "websocket-closed" : "rejected";
				statusOverride = summary.Status == 0 ? null : summary.Status;
				requestBytesOverride = summary.ClientBytes;
				responseBytes = summary.OriginBytes;
				error = summary.Error;
				if (summary.Status == 101) { clientMessages = summary.ClientMessages; originMessages = summary.OriginMessages; }
			}
			else if (HttpMethods.IsConnect(request.Method)) {
				// CONNECT tunnels are taken over by TunnelHandler before Kestrel parses the request;
				// one that still arrives here has nowhere to go.
				mode = "rejected";
				error = "CONNECT reached the HTTP handler";
				await RespondAsync(context, capture, 405, "CONNECT is handled at the connection level.");
			}
			else if (uri is null || (interceptBase is null && uri.Scheme != Uri.UriSchemeHttp)) {
				mode = "rejected";
				error = interceptBase is null ? "target is not an absolute http:// URL" : "request target is not a path";
				await RespondAsync(context, capture, 400, interceptBase is null ? "Expected an absolute http:// URL." : "Expected a request path.");
			}
			else {
				responseBytes = await ForwardAsync(context, capture, uri, counted, v => originVersion = v, w => exchangeWire = w);
			}
		}
		catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) {
			error = "client disconnected";
			// (the default 200 of an unanswered response would be misleading)
			nothingSent = !context.Response.HasStarted;
		}
		catch (OperationCanceledException) {
			error = "origin timed out";
			await TryRespondAsync(context, capture, 504, "Origin did not respond in time.");
		}
		catch (HttpRequestException ex) {
			_limits.Report("connecting to an origin", ex);   // out of sockets, for example, as opposed to an origin that is just down
			error = $"origin unreachable: {ex.InnerException?.Message ?? ex.Message}";
			await TryRespondAsync(context, capture, 502, "Could not reach the origin.");
		}
		catch (Exception ex) {
			_limits.Report("forwarding a request", ex);
			error = ex.Message;
			if (context.Response.HasStarted) context.Abort();
			else await TryRespondAsync(context, capture, 502, "Proxy error.");
		}
		finally {
			var record = MakeRecord(mode, nothingSent ? null : statusOverride ?? context.Response.StatusCode, requestBytesOverride ?? counted.BytesRead, responseBytes, error);
			_log.Write(record);
			if (connectionWire is not null && !isWebSocket) {
				try { capture.SetWireBytes(connectionWire.TakeNextRequest(), exchangeWire?.TakeResponse()); }
				catch (Exception ex) when (ex is not OutOfMemoryException) { Console.Error.WriteLine($"Wire capture failed: {ex.Message}"); }
			}
			capture.Complete(record);
		}
	}

	async Task<long> ForwardAsync(HttpContext context, IExchangeCapture capture, Uri uri, CountingStream requestBody, Action<string> reportOriginVersion, Action<ExchangeWire> reportWire) {
		var request = context.Request;
		using var upstream = new HttpRequestMessage(new HttpMethod(request.Method), uri);

		// Towards an https origin, ask for HTTP/2 and let the TLS handshake (ALPN) fall back to HTTP/1.1 if the origin
		// does not offer it. Plain http origins always get HTTP/1.1 (no HTTP/2 without TLS).
		upstream.Version = uri.Scheme == Uri.UriSchemeHttps ? System.Net.HttpVersion.Version20 : System.Net.HttpVersion.Version11;
		upstream.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

		// HTTP/2 has no chunked encoding or required Content-Length, so ask Kestrel whether a body can follow; fall back to the headers.
		bool hasBody = context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody
			?? (request.ContentLength > 0 || request.Headers.ContainsKey("Transfer-Encoding"));
		if (hasBody) upstream.Content = new StreamContent(capture.TeeRequestBody(requestBody));

		var drop = HeaderRules.HeadersToDrop(request.Headers);
		foreach (var (name, values) in request.Headers) {
			if (drop.Contains(name) || name.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
			string?[] v = values.ToArray();
			if (!upstream.Headers.TryAddWithoutValidation(name, v))
				upstream.Content?.Headers.TryAddWithoutValidation(name, v);
		}

		// The timeout covers waiting for the origin's response headers; streaming the body is not time-limited.
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
		timeout.CancelAfter(_options.UpstreamTimeout);
		if (_wireTaps is not null) {
			// The tap on the origin connection reads this to learn which exchange a request belongs to (see ExchangeWire).
			var wire = new ExchangeWire(request.Method);
			wire.MakeCurrent();
			reportWire(wire);
		}
		using var response = await _client.SendAsync(upstream, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
		timeout.CancelAfter(Timeout.InfiniteTimeSpan);
		reportOriginVersion(HttpVersions.Format(response.Version));

		// The origin's response, as the origin sent it (before the proxy drops hop-by-hop headers).
		// HTTP/2 has no reason phrase, so the standard one for the status code is written (as a text capture of HTTP/2 conventionally shows it).
		string reason = string.IsNullOrEmpty(response.ReasonPhrase) ? ReasonPhrases.GetReasonPhrase((int)response.StatusCode) : response.ReasonPhrase;
		capture.WriteResponseHead($"{HttpVersions.Format(response.Version)} {(int)response.StatusCode} {reason}",
			response.Headers.Concat(response.Content.Headers).SelectMany(h => h.Value.Select(v => KeyValuePair.Create(h.Key, v))));

		context.Response.StatusCode = (int)response.StatusCode;
		// Only HTTP/1.x responses carry a reason phrase; to an HTTP/2 client it does not exist.
		if (!HttpProtocol.IsHttp2(request.Protocol))
			context.Features.GetRequiredFeature<IHttpResponseFeature>().ReasonPhrase = response.ReasonPhrase;
		foreach (var header in response.Headers.Concat(response.Content.Headers)) {
			if (HeaderRules.IsHopByHop(header.Key)) continue;
			context.Response.Headers.Append(header.Key, header.Value.ToArray());
		}

		var counted = new CountingStream(context.Response.Body);
		await using var body = capture.TeeResponseBody(await response.Content.ReadAsStreamAsync(timeout.Token));
		await body.CopyToAsync(counted, context.RequestAborted);

		// Trailers (headers after the body, as used by gRPC) can only be passed on when the client connection supports them.
		if (response.TrailingHeaders.Any() && context.Response.SupportsTrailers())
			foreach (var trailer in response.TrailingHeaders)
				context.Response.AppendTrailer(trailer.Key, trailer.Value.ToArray());
		return counted.BytesWritten;
	}

	/// <summary>Sends a response the proxy generated itself and records it in the capture.</summary>
	static async Task RespondAsync(HttpContext context, IExchangeCapture capture, int status, string message) {
		var bytes = Encoding.UTF8.GetBytes(message);
		capture.WriteResponseHead($"HTTP/1.1 {status} {ReasonPhrases.GetReasonPhrase(status)}", new[] {
			KeyValuePair.Create("Content-Type", "text/plain"),
			KeyValuePair.Create("Content-Length", bytes.Length.ToString()),
		}, generated: true);
		capture.WriteResponseBody(bytes);
		context.Response.StatusCode = status;
		context.Response.ContentType = "text/plain";
		await context.Response.WriteAsync(message);
	}

	static async Task TryRespondAsync(HttpContext context, IExchangeCapture capture, int status, string message) {
		if (context.Response.HasStarted) { context.Abort(); return; }
		try { await RespondAsync(context, capture, status, message); } catch { }
	}
}
