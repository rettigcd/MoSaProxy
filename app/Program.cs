using System.Net;
using System.Net.Sockets;
using System.Text;

int port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 8080;

var client = new HttpClient(new SocketsHttpHandler {
	UseProxy = false,
	AllowAutoRedirect = false,
	UseCookies = false,
	AutomaticDecompression = DecompressionMethods.None,
}) { Timeout = Timeout.InfiniteTimeSpan };

var listener = new TcpListener(IPAddress.Any, port);
listener.Start();
Console.WriteLine($"HTTP proxy listening on port {port}");

while (true) {
	var tcp = await listener.AcceptTcpClientAsync();
	_ = Task.Run(() => HandleAsync(tcp));
}

async Task HandleAsync(TcpClient tcp) {
	using var _ = tcp;
	try {
		var stream = tcp.GetStream();

		string? requestLine = await ReadLineAsync(stream);
		if (string.IsNullOrEmpty(requestLine)) return;

		var parts = requestLine.Split(' ');
		if (parts.Length != 3) { await RespondAsync(stream, 400, "Bad Request"); return; }
		string method = parts[0];

		var headers = new List<(string Name, string Value)>();
		while (true) {
			string? line = await ReadLineAsync(stream);
			if (string.IsNullOrEmpty(line)) break;
			int colon = line.IndexOf(':');
			if (colon > 0) headers.Add((line[..colon].Trim(), line[(colon + 1)..].Trim()));
		}

		if (method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase)) {
			Console.WriteLine($"CONNECT {parts[1]} (rejected: HTTP only)");
			await RespondAsync(stream, 405, "Method Not Allowed", "This proxy only supports plain HTTP.");
			return;
		}

		if (!Uri.TryCreate(parts[1], UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp) {
			Console.WriteLine($"{method} {parts[1]} (rejected: not an absolute http:// URL)");
			await RespondAsync(stream, 400, "Bad Request", "Expected an absolute http:// URL.");
			return;
		}

		if (headers.Any(h => h.Name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))) {
			Console.WriteLine($"{method} {uri} (rejected: chunked request body)");
			await RespondAsync(stream, 501, "Not Implemented", "Chunked request bodies are not supported.");
			return;
		}

		Console.WriteLine($"{method} {uri}");

		using var request = new HttpRequestMessage(new HttpMethod(method), uri);

		long contentLength = 0;
		var contentHeaders = new List<(string Name, string Value)>();
		foreach (var (name, value) in headers) {
			if (HopByHop.Contains(name) || name.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
			if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) long.TryParse(value, out contentLength);
			if (!request.Headers.TryAddWithoutValidation(name, value))
				contentHeaders.Add((name, value));
		}

		if (contentLength > 0 || contentHeaders.Count > 0) {
			var body = new MemoryStream();
			if (contentLength > 0) await CopyExactAsync(stream, body, contentLength);
			body.Position = 0;
			request.Content = new StreamContent(body);
			foreach (var (name, value) in contentHeaders)
				request.Content.Headers.TryAddWithoutValidation(name, value);
		}

		using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

		var head = new StringBuilder();
		head.Append($"HTTP/1.1 {(int)response.StatusCode} {response.ReasonPhrase}\r\n");
		foreach (var h in response.Headers.Concat(response.Content.Headers)) {
			if (HopByHop.Contains(h.Key)) continue;
			foreach (var v in h.Value) head.Append($"{h.Key}: {v}\r\n");
		}
		// One request per connection; the body is delimited by Content-Length or by closing.
		head.Append("Connection: close\r\n\r\n");
		await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));

		await using var responseBody = await response.Content.ReadAsStreamAsync();
		await responseBody.CopyToAsync(stream);
	}
	catch (Exception ex) {
		Console.WriteLine($"Error: {ex.Message}");
		try { await RespondAsync(tcp.GetStream(), 502, "Bad Gateway"); } catch { }
	}
}

static async Task<string?> ReadLineAsync(NetworkStream stream) {
	var sb = new StringBuilder();
	var buf = new byte[1];
	while (await stream.ReadAsync(buf) == 1) {
		if (buf[0] == '\n') return sb.ToString().TrimEnd('\r');
		sb.Append((char)buf[0]);
	}
	return sb.Length > 0 ? sb.ToString() : null;
}

static async Task CopyExactAsync(Stream from, Stream to, long count) {
	var buf = new byte[8192];
	while (count > 0) {
		int n = await from.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, count)));
		if (n == 0) throw new EndOfStreamException("Client closed before sending full body.");
		await to.WriteAsync(buf.AsMemory(0, n));
		count -= n;
	}
}

static async Task RespondAsync(NetworkStream stream, int code, string reason, string body = "") {
	var bytes = Encoding.UTF8.GetBytes(body);
	var head = $"HTTP/1.1 {code} {reason}\r\nContent-Type: text/plain\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
	await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
	await stream.WriteAsync(bytes);
}

static class HopByHop {
	static readonly HashSet<string> names = new(StringComparer.OrdinalIgnoreCase) {
		"Connection", "Proxy-Connection", "Keep-Alive", "Proxy-Authenticate",
		"Proxy-Authorization", "TE", "Trailer", "Transfer-Encoding", "Upgrade",
	};
	public static bool Contains(string name) => names.Contains(name);
}
/*
Run in PowerShell:
& "C:\Program Files\Google\Chrome\Application\chrome.exe" ` --user-data-dir="$env:TEMP\chrome-mos" ` --proxy-server="http://localhost:8080"

Navigate to:
http://httpforever.com/


*/