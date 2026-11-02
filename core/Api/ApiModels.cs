namespace MoSaProxy;

/// <summary>What <c>GET /api/status</c> answers: how the proxy is set up and what it has captured.</summary>
public sealed record ApiStatus(
	string Version, int ProxyPort, bool Intercept, bool Http2, bool CaptureEnabled, int ExchangesCaptured, string? CaptureFolder, string? LogFile);

/// <summary>What <c>GET /api/capture</c> and <c>PUT /api/capture</c> answer.</summary>
public sealed record ApiCaptureState(bool Enabled, int ExchangesCaptured);

/// <summary>The body of <c>PUT /api/capture</c>.</summary>
public sealed record ApiCaptureRequest(bool Enabled);

/// <summary>One page of <c>GET /api/exchanges</c>. <see cref="Next"/> is the number to pass as <c>after</c> for the page after this one, or null when this page was the last.</summary>
public sealed record ApiExchangePage(IReadOnlyList<ExchangeSummary> Items, int? Next);
