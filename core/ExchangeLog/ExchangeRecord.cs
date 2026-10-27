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

	/// <summary>HTTP/1.x only: the client sent this request on a connection that had already carried one.</summary>
	public bool ClientConnectionReused { get; init; }

	/// <summary>HTTP/1.x only: the proxy sent this request on an origin connection that had already carried one.</summary>
	public bool OriginConnectionReused { get; init; }

	/// <summary>The number of the origin connection the request was sent on (HTTP/1.x only), counting from 1; null when not known.</summary>
	public int? OriginConnectionId { get; init; }

	/// <summary>The client's source port on its connection to the proxy; 0 when not known.</summary>
	public int ClientPort { get; init; }

	/// <summary>The TLS version negotiated with the client inside an intercepted tunnel (for example "Tls13"); null outside one.</summary>
	public string? ClientTlsVersion { get; init; }

	/// <summary>When the stages of the exchange happened, where the proxy observed them. Not part of the JSON log.</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public ExchangeTimes? Times { get; init; }

	/// <summary>WebSocket only: complete messages the client sent to the origin (RequestBytes counts their bytes).</summary>
	public int? ClientMessages { get; init; }

	/// <summary>WebSocket only: complete messages the origin sent to the client (ResponseBytes counts their bytes).</summary>
	public int? OriginMessages { get; init; }
}
