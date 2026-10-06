using System.Text.Json;

namespace mos;

/// <summary>One request/response pair as seen by the proxy, independent of HTTP version.</summary>
public sealed record ExchangeRecord {
	public DateTimeOffset Timestamp { get; init; }
	public string Client { get; init; } = "";
	public string Scheme { get; init; } = "";
	public string Host { get; init; } = "";
	public int Port { get; init; }
	public string Method { get; init; } = "";
	public string Url { get; init; } = "";
	public string ClientHttpVersion { get; init; } = "";
	public string? OriginHttpVersion { get; init; }
	public int? Status { get; init; }
	public IReadOnlyDictionary<string, string> RequestHeaders { get; init; } = new Dictionary<string, string>();
	public IReadOnlyDictionary<string, string> ResponseHeaders { get; init; } = new Dictionary<string, string>();
	public long RequestBytes { get; init; }
	public long ResponseBytes { get; init; }
	public double DurationMs { get; init; }
	public string Mode { get; init; } = "plain";
	public string? Error { get; init; }

	/// <summary>WebSocket only: complete messages the client sent to the origin (RequestBytes counts their bytes).</summary>
	public int? ClientMessages { get; init; }

	/// <summary>WebSocket only: complete messages the origin sent to the client (ResponseBytes counts their bytes).</summary>
	public int? OriginMessages { get; init; }
}

public interface IExchangeLog {
	void Write(ExchangeRecord record);
}

/// <summary>
/// One line per exchange on the console. The method is blue for the common request methods (GET, POST, PUT, DELETE, PATCH),
/// and the status is coloured: green 2xx, cyan 3xx, yellow 4xx, red 5xx or no response.
/// </summary>
public sealed class ConsoleExchangeLog(ColorConsole? console = null) : IExchangeLog {
	readonly ColorConsole _console = console ?? ColorConsole.Default;

	public void Write(ExchangeRecord r) {
		string status = r.Status?.ToString() ?? "---";
		var methodColor = MethodColor(r.Method);
		var statusColor = StatusColor(r.Status);
		// A WebSocket's closing record also says how many messages went each way.
		string messages = r.ClientMessages is { } sent ? $" [closed: client sent {sent} message(s), origin sent {r.OriginMessages}]" : "";
		if (r.Error is null)
			_console.WriteLine($"{methodColor}{r.Method}{Fg.Restore} {r.Url} -> {statusColor}{status}{Fg.Restore} ({r.DurationMs:0} ms, {r.ResponseBytes} bytes){messages}");
		else
			_console.WriteLine($"{methodColor}{r.Method}{Fg.Restore} {r.Url} -> {statusColor}{status}{Fg.Restore} ({r.DurationMs:0} ms, {r.ResponseBytes} bytes){messages} {Fg.Red}[{r.Error}]{Fg.Restore}");
	}

	/// <summary>Blue for GET, POST, PUT, DELETE and PATCH; null (leave the console's colour) for any other method such as CONNECT, HEAD or OPTIONS.</summary>
	public static ForegroundColor? MethodColor(string method) => method switch {
		"GET" or "POST" or "PUT" or "DELETE" or "PATCH" => Fg.Blue,
		_ => null,
	};

	static ForegroundColor StatusColor(int? status) => status switch {
		>= 200 and < 300 => Fg.Green,
		>= 300 and < 400 => Fg.Cyan,
		>= 400 and < 500 => Fg.Yellow,
		_ => Fg.Red,
	};
}

public sealed class JsonLinesExchangeLog : IExchangeLog {
	static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
	readonly string _path;
	readonly object _lock = new();

	public JsonLinesExchangeLog(string path) {
		_path = path;
		var directory = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
	}

	public void Write(ExchangeRecord record) {
		string line = JsonSerializer.Serialize(record, JsonOptions);
		lock (_lock) {
			using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
			using var writer = new StreamWriter(stream);
			writer.WriteLine(line);
		}
	}
}

public sealed class CompositeExchangeLog(params IExchangeLog[] logs) : IExchangeLog {
	public void Write(ExchangeRecord record) {
		foreach (var log in logs) {
			try { log.Write(record); }
			catch (Exception ex) { Console.Error.WriteLine($"Log sink failed: {ex.Message}"); }
		}
	}
}
