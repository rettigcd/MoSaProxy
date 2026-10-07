namespace MoSaProxy;

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
