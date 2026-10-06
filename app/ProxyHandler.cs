using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace mos;

/// <summary>Forwards plain-HTTP proxy requests to their origin and logs each exchange.</summary>
public sealed class ProxyHandler : IDisposable {
	readonly HttpClient _client;
	readonly IExchangeLog _log;
	readonly ProxyOptions _options;

	public ProxyHandler(IExchangeLog log, ProxyOptions options) {
		_log = log;
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

		Uri.TryCreate(rawTarget, UriKind.Absolute, out var uri);
		var counted = new CountingStream(request.Body);
		long responseBytes = 0;
		string mode = "plain";
		string? error = null;
		string? originVersion = null;

		try {
			if (HttpMethods.IsConnect(request.Method)) {
				mode = "rejected";
				error = "CONNECT is not supported yet";
				await RespondAsync(context, 405, "This proxy does not support HTTPS (CONNECT) yet.");
			}
			else if (uri is null || uri.Scheme != Uri.UriSchemeHttp) {
				mode = "rejected";
				error = "target is not an absolute http:// URL";
				await RespondAsync(context, 400, "Expected an absolute http:// URL.");
			}
			else {
				responseBytes = await ForwardAsync(context, uri, counted, v => originVersion = v);
			}
		}
		catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) {
			error = "client disconnected";
		}
		catch (OperationCanceledException) {
			error = "origin timed out";
			await TryRespondAsync(context, 504, "Origin did not respond in time.");
		}
		catch (HttpRequestException ex) {
			error = $"origin unreachable: {ex.InnerException?.Message ?? ex.Message}";
			await TryRespondAsync(context, 502, "Could not reach the origin.");
		}
		catch (Exception ex) {
			error = ex.Message;
			if (context.Response.HasStarted) context.Abort();
			else await TryRespondAsync(context, 502, "Proxy error.");
		}
		finally {
			_log.Write(new ExchangeRecord {
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
			});
		}
	}

	async Task<long> ForwardAsync(HttpContext context, Uri uri, CountingStream requestBody, Action<string> reportOriginVersion) {
		var request = context.Request;
		using var upstream = new HttpRequestMessage(new HttpMethod(request.Method), uri);

		bool hasBody = request.ContentLength > 0 || request.Headers.ContainsKey("Transfer-Encoding");
		if (hasBody) upstream.Content = new StreamContent(requestBody);

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

		context.Response.StatusCode = (int)response.StatusCode;
		context.Features.GetRequiredFeature<IHttpResponseFeature>().ReasonPhrase = response.ReasonPhrase;
		foreach (var header in response.Headers.Concat(response.Content.Headers)) {
			if (HeaderRules.IsHopByHop(header.Key)) continue;
			context.Response.Headers.Append(header.Key, header.Value.ToArray());
		}

		var counted = new CountingStream(context.Response.Body);
		await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
		await body.CopyToAsync(counted, context.RequestAborted);
		return counted.BytesWritten;
	}

	static async Task RespondAsync(HttpContext context, int status, string message) {
		context.Response.StatusCode = status;
		context.Response.ContentType = "text/plain";
		await context.Response.WriteAsync(message);
	}

	static async Task TryRespondAsync(HttpContext context, int status, string message) {
		if (context.Response.HasStarted) { context.Abort(); return; }
		try { await RespondAsync(context, status, message); } catch { }
	}
}
