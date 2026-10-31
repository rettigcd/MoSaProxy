namespace MoSaProxy;

/// <summary>What a list of captured exchanges shows for each one. Built from the exchange's capture metadata (<c>N_m.xml</c>).</summary>
public sealed record ExchangeSummary(
	int Id, DateTimeOffset Timestamp, string Method, string Scheme, string Host, int Port, string Path, string Url,
	int? Status, string ClientHttpVersion, string? OriginHttpVersion, string Mode,
	long RequestBytes, long ResponseBytes, double DurationMs, string? Error);

/// <summary>The request line or status line of a captured message and its headers, in the order they were sent.</summary>
public sealed record HttpHead(string FirstLine, IReadOnlyList<KeyValuePair<string, string>> Headers);

/// <summary>Everything known about one captured exchange except its bodies, which are read as streams from <see cref="ExchangeStore"/>.</summary>
public sealed record ExchangeDetail(
	ExchangeSummary Summary, string Client, int ClientPort, string? ClientTlsVersion,
	bool ClientConnectionReused, bool OriginConnectionReused, int? OriginConnectionId,
	int? ClientMessages, int? OriginMessages,
	string RequestSource, string ResponseSource, bool RequestBodyTruncated, bool ResponseBodyTruncated,
	IReadOnlyDictionary<string, DateTimeOffset> Times, HttpHead? Request, HttpHead? Response, bool HasWebSocketLog);

/// <summary>Which exchanges <see cref="ExchangeStore.List"/> returns. Every filter that is set must match.</summary>
public sealed record ExchangeQuery {
	/// <summary>Only exchanges numbered above this one, so a caller can ask for what is new since the last it saw.</summary>
	public int? After { get; init; }

	/// <summary>At most this many exchanges, oldest first.</summary>
	public int Limit { get; init; } = 100;

	/// <summary>The host contains this text (any case).</summary>
	public string? Host { get; init; }

	/// <summary>The method equals this (any case).</summary>
	public string? Method { get; init; }

	/// <summary>The status equals this.</summary>
	public int? Status { get; init; }

	/// <summary>The mode equals this: plain, tunnel, intercept, websocket, websocket-closed or rejected (any case).</summary>
	public string? Mode { get; init; }

	/// <summary>The URL contains this text (any case).</summary>
	public string? Text { get; init; }
}
