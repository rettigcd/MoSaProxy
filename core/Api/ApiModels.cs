namespace MoSaProxy;

/// <summary>What <c>GET /api/status</c> answers: how the proxy is set up and what it has captured.</summary>
public sealed record ApiStatus(
	string Version, int ProxyPort, bool Intercept, bool Http2, bool CaptureEnabled, int ExchangesCaptured, string? CaptureFolder, string? LogFile);

/// <summary>What <c>GET /api/capture</c> and <c>PUT /api/capture</c> answer.</summary>
public sealed record ApiCaptureState(bool Enabled, int ExchangesCaptured);

/// <summary>The body of <c>PUT /api/capture</c>.</summary>
public sealed record ApiCaptureRequest(bool Enabled);

/// <summary>What <c>GET /api/intercept</c> and <c>PUT /api/intercept</c> answer.</summary>
/// <param name="Enabled">Whether tunnels opened now are decrypted.</param>
/// <param name="Bypass">Hosts that are never decrypted: an exact name, or *.suffix.</param>
/// <param name="CaTrusted">Whether this machine's user trusts the root certificate that signs the decrypted connections; null when that is not known (the certificate does not exist yet, or the trust store could not be read).</param>
/// <param name="CaSubject">The root certificate's subject, or null before it exists.</param>
/// <param name="CaFile">The root certificate file (public part) to give to a client that must trust it, or null before it exists.</param>
public sealed record ApiInterceptState(bool Enabled, IReadOnlyList<string> Bypass, bool? CaTrusted, string? CaSubject, string? CaFile);

/// <summary>The body of <c>PUT /api/intercept</c>. A field that is left out is left as it is.</summary>
public sealed record ApiInterceptRequest(bool? Enabled, IReadOnlyList<string>? Bypass);

/// <summary>One page of <c>GET /api/exchanges</c>. <see cref="Next"/> is the number to pass as <c>after</c> for the page after this one, or null when this page was the last.</summary>
public sealed record ApiExchangePage(IReadOnlyList<ExchangeSummary> Items, int? Next);
