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

/// <summary>
/// Appends one JSON object per line to a file. With <c>maxBytes</c> set, the file is rotated before a write would take it past that size:
/// "traffic.jsonl" becomes "traffic.1.jsonl", the old "traffic.1.jsonl" becomes "traffic.2.jsonl", and so on up to <c>keep</c> rotated files;
/// the oldest one beyond that is deleted. A single line larger than the limit is still written (into a fresh file), never dropped.
/// </summary>
public sealed class JsonLinesExchangeLog : IExchangeLog {
	static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
	readonly string _path;
	readonly long _maxBytes;
	readonly int _keep;
	readonly object _lock = new();
	long _size;           // bytes in the current file
	long _rotateAfter;    // after a failed rotation, do not try again (or complain again) until the file has grown this big

	/// <param name="maxBytes">Rotate before a write would pass this size; 0 means never rotate.</param>
	/// <param name="keep">How many rotated files to keep; 0 means the old file is discarded when rotating.</param>
	public JsonLinesExchangeLog(string path, long maxBytes = 0, int keep = 5) {
		_path = Path.GetFullPath(path);
		_maxBytes = maxBytes;
		_keep = keep;
		var directory = Path.GetDirectoryName(_path);
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
		_size = File.Exists(_path) ? new FileInfo(_path).Length : 0;
	}

	public void Write(ExchangeRecord record) {
		var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine);
		lock (_lock) {
			if (_maxBytes > 0 && _size > 0 && _size + bytes.Length > _maxBytes && _size >= _rotateAfter) Rotate();
			using (var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
				stream.Write(bytes);
			_size += bytes.Length;
		}
	}

	void Rotate() {
		try {
			string directory = Path.GetDirectoryName(_path)!;
			string name = Path.GetFileNameWithoutExtension(_path);
			string extension = Path.GetExtension(_path);
			string Rotated(int number) => Path.Combine(directory, $"{name}.{number}{extension}");

			if (_keep == 0) {
				File.Delete(_path);
			}
			else {
				File.Delete(Rotated(_keep));                       // the oldest one falls off the end
				for (int number = _keep - 1; number >= 1; number--)
					if (File.Exists(Rotated(number))) File.Move(Rotated(number), Rotated(number + 1));
				File.Move(_path, Rotated(1));
			}
			_size = 0;
			_rotateAfter = 0;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			// For example a viewer has a rotated file open. Losing log lines would be worse than a big file, so keep appending.
			Console.Error.WriteLine($"Log rotation failed ({ex.Message}); continuing to append to {_path}.");
			_rotateAfter = _size + _maxBytes;
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
