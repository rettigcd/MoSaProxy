namespace MoSaProxy;

/// <summary>
/// When the stages of one exchange happened, as the proxy saw them. A stage that was not reached, or that the proxy cannot observe, is null.
/// The names follow Fiddler's session timers so that the .saz export can use them directly.
/// </summary>
public sealed record ExchangeTimes {
	/// <summary>The client's connection to the proxy was accepted (earlier than the request when the connection is reused).</summary>
	public DateTimeOffset? ClientConnected { get; init; }

	/// <summary>The request line and headers had been read.</summary>
	public DateTimeOffset? GotRequestHeaders { get; init; }

	/// <summary>The request body had been read completely (the same as <see cref="GotRequestHeaders"/> when there is no body).</summary>
	public DateTimeOffset? ClientDoneRequest { get; init; }

	/// <summary>The proxy was about to send the request to the origin.</summary>
	public DateTimeOffset? FiddlerBeginRequest { get; init; }

	/// <summary>The origin's response headers had arrived.</summary>
	public DateTimeOffset? GotResponseHeaders { get; init; }

	/// <summary>The first response bytes were written to the client.</summary>
	public DateTimeOffset? ClientBeginResponse { get; init; }

	/// <summary>The response had been written to the client completely.</summary>
	public DateTimeOffset? ClientDoneResponse { get; init; }
}
