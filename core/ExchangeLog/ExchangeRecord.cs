namespace MoSaProxy;

public interface IExchangeLog {
	void Write(ExchangeRecord record);
}

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
