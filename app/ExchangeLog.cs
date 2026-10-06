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
}

public interface IExchangeLog {
	void Write(ExchangeRecord record);
}

/// <summary>One line per exchange on the console, with the status coloured (green 2xx, cyan 3xx, yellow 4xx, red 5xx or no response).</summary>
public sealed class ConsoleExchangeLog(ColorConsole? console = null) : IExchangeLog {
	readonly ColorConsole _console = console ?? ColorConsole.Default;

	public void Write(ExchangeRecord r) {
		string status = r.Status?.ToString() ?? "---";
		var statusColor = StatusColor(r.Status);
		if (r.Error is null)
			_console.WriteLine($"{r.Method} {r.Url} -> {statusColor}{status}{Fg.Restore} ({r.DurationMs:0} ms, {r.ResponseBytes} bytes)");
		else
			_console.WriteLine($"{r.Method} {r.Url} -> {statusColor}{status}{Fg.Restore} ({r.DurationMs:0} ms, {r.ResponseBytes} bytes) {Fg.Red}[{r.Error}]{Fg.Restore}");
	}

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
