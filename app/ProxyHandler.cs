using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;

namespace mos;

/// <summary>Forwards plain-HTTP proxy requests to their origin, logs each exchange and captures its files.</summary>
public sealed class ProxyHandler : IDisposable {
	readonly HttpClient _client;
	readonly IExchangeLog _log;
	readonly ISessionCapture _capture;
	readonly ProxyOptions _options;

	public ProxyHandler(IExchangeLog log, ISessionCapture capture, ProxyOptions options) {
		_log = log;
		_capture = capture;
		_options = options;
		_client = new HttpClient(new SocketsHttpHandler {
			UseProxy = false,
			AllowAutoRedirect = false,
			UseCookies = false,
			AutomaticDecompression = System.Net.DecompressionMethods.None,
		}) { Timeout = Timeout.InfiniteTimeSpan };
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

		Uri.TryCreate(rawTarget, UriKind.Absolute, out var uri);
		var counted = new CountingStream(request.Body);
		long responseBytes = 0;
		string mode = "plain";
		string? error = null;
		string? originVersion = null;

		try {
			capture.WriteRequestHead(request.Method, rawTarget, request.Protocol,
				request.Headers.Select(h => KeyValuePair.Create(h.Key, h.Value.ToString())));

			if (HttpMethods.IsConnect(request.Method)) {
				mode = "rejected";
				error = "CONNECT is not supported yet";
				await RespondAsync(context, capture, 405, "This proxy does not support HTTPS (CONNECT) yet.");
			}
			else if (uri is null || uri.Scheme != Uri.UriSchemeHttp) {
				mode = "rejected";
				error = "target is not an absolute http:// URL";
				await RespondAsync(context, capture, 400, "Expected an absolute http:// URL.");
			}
			else {
				responseBytes = await ForwardAsync(context, capture, uri, counted, v => originVersion = v);
			}
		}
		catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) {
			error = "client disconnected";
		}
		catch (OperationCanceledException) {
			error = "origin timed out";
			await TryRespondAsync(context, capture, 504, "Origin did not respond in time.");
		}
		catch (HttpRequestException ex) {
			error = $"origin unreachable: {ex.InnerException?.Message ?? ex.Message}";
			await TryRespondAsync(context, capture, 502, "Could not reach the origin.");
		}
		catch (Exception ex) {
			error = ex.Message;
			if (context.Response.HasStarted) context.Abort();
			else await TryRespondAsync(context, capture, 502, "Proxy error.");
		}
		finally {
			var record = new ExchangeRecord {
				Timestamp = DateTimeOffset.UtcNow,
				Client = context.Connection.RemoteIpAddress?.ToString() ?? "",
				Scheme = uri?.Scheme ?? "",
				Host = uri?.Host ?? "",
				Port = uri?.Port ?? 0,
				Method = request.Method,
				Url = uri?.ToString() ?? rawTarget,
				ClientHttpVersion = request.Protocol,
				OriginHttpVersion = originVersion,
				Status = context.Response.StatusCode,
				RequestHeaders = HeaderRules.ForLog(request.Headers),
				ResponseHeaders = HeaderRules.ForLog(context.Response.Headers),
				RequestBytes = counted.BytesRead,
				ResponseBytes = responseBytes,
				DurationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
				Mode = mode,
				Error = error,
			};
			_log.Write(record);
			capture.Complete(record);
		}
	}

	async Task<long> ForwardAsync(HttpContext context, IExchangeCapture capture, Uri uri, CountingStream requestBody, Action<string> reportOriginVersion) {
		var request = context.Request;
		using var upstream = new HttpRequestMessage(new HttpMethod(request.Method), uri);

		bool hasBody = request.ContentLength > 0 || request.Headers.ContainsKey("Transfer-Encoding");
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
		using var response = await _client.SendAsync(upstream, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
		timeout.CancelAfter(Timeout.InfiniteTimeSpan);
		reportOriginVersion($"HTTP/{response.Version}");

		// The origin's response, as the origin sent it (before the proxy drops hop-by-hop headers).
		capture.WriteResponseHead($"HTTP/{response.Version} {(int)response.StatusCode} {response.ReasonPhrase}",
			response.Headers.Concat(response.Content.Headers).SelectMany(h => h.Value.Select(v => KeyValuePair.Create(h.Key, v))));

		context.Response.StatusCode = (int)response.StatusCode;
		context.Features.GetRequiredFeature<IHttpResponseFeature>().ReasonPhrase = response.ReasonPhrase;
		foreach (var header in response.Headers.Concat(response.Content.Headers)) {
			if (HeaderRules.IsHopByHop(header.Key)) continue;
			context.Response.Headers.Append(header.Key, header.Value.ToArray());
		}

		var counted = new CountingStream(context.Response.Body);
		await using var body = capture.TeeResponseBody(await response.Content.ReadAsStreamAsync(timeout.Token));
		await body.CopyToAsync(counted, context.RequestAborted);
		return counted.BytesWritten;
	}

	/// <summary>Sends a response the proxy generated itself and records it in the capture.</summary>
	static async Task RespondAsync(HttpContext context, IExchangeCapture capture, int status, string message) {
		var bytes = Encoding.UTF8.GetBytes(message);
		capture.WriteResponseHead($"HTTP/1.1 {status} {ReasonPhrases.GetReasonPhrase(status)}", new[] {
			KeyValuePair.Create("Content-Type", "text/plain"),
			KeyValuePair.Create("Content-Length", bytes.Length.ToString()),
		});
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
