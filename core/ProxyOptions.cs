namespace MoSaProxy;

/// <summary>
/// The settings the proxy runs with, and their defaults. Turning command-line arguments into these settings is
/// <see cref="ProxyOptionsParser"/>'s job, so this class holds no user-interface text.
/// </summary>
public sealed class ProxyOptions {

	/// <summary>Port to listen on (loopback only). 0 picks any free port.</summary>
	public int Port { get; init; } = 8080;

	/// <summary>Optional path of a JSON Lines file that every exchange is appended to.</summary>
	public string? LogPath { get; init; }

	/// <summary>
	/// The log file is rotated when a write would take it past this many bytes: it becomes name.1.ext, older rotated files shift up
	/// (name.2.ext and so on), and the oldest beyond <see cref="LogKeep"/> is deleted. 0 means never rotate. Default 50 MB.
	/// </summary>
	public long LogMaxBytes { get; init; } = 50L * 1024 * 1024;

	/// <summary>How many rotated log files to keep next to the current one (0 means a rotated file is simply discarded).</summary>
	public int LogKeep { get; init; } = 5;

	/// <summary>
	/// When stopping, how long running exchanges, busy tunnels and closing WebSockets get to finish before they are aborted.
	/// 0 stops at once. Default 10 seconds.
	/// </summary>
	public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(10);

	/// <summary>How long to wait for the origin to start responding before answering 504.</summary>
	public TimeSpan UpstreamTimeout { get; init; } = TimeSpan.FromSeconds(100);

	/// <summary>How long to wait when opening a connection to the origin for a CONNECT tunnel.</summary>
	public TimeSpan ConnectTimeout { get; init; } = DefaultConnectTimeout;

	public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(15);

	/// <summary>
	/// With interception on, how long to wait for the first bytes of a CONNECT tunnel to tell TLS, plain HTTP and other protocols apart.
	/// A client that says nothing for this long (some protocols have the server speak first) is relayed blindly.
	/// </summary>
	public TimeSpan SniffTimeout { get; init; } = TimeSpan.FromSeconds(3);

	/// <summary>A tunnel with no data moving in either direction for this long is closed.</summary>
	public TimeSpan TunnelIdleTimeout { get; init; } = TimeSpan.FromMinutes(5);

	/// <summary>Optional folder for per-exchange capture files. Each run gets its own sub-folder.</summary>
	public string? CaptureDirectory { get; init; }

	/// <summary>Optional cap on how many bytes of each request/response body are written to capture files.</summary>
	public long? CaptureMaxBodyBytes { get; init; }

	/// <summary>
	/// Decrypt HTTPS: answer CONNECT, present a generated certificate to the client, and forward the decrypted requests.
	/// Off by default (CONNECT is then a blind tunnel). Clients must trust the root certificate in <see cref="CaDirectory"/>.
	/// </summary>
	public bool Intercept { get; init; }

	/// <summary>Hosts that are never intercepted (they stay blind tunnels). Exact names, or "*.example.com" for subdomains.</summary>
	public IReadOnlyList<string> InterceptBypass { get; init; } = Array.Empty<string>();

	/// <summary>Folder holding the root certificate used for interception.</summary>
	public string CaDirectory { get; init; } = DefaultCaDirectory;

	/// <summary>Remove the MoSaProxy root certificate from the Windows user's trusted roots, then exit (the proxy is not started).</summary>
	public bool UntrustCa { get; init; }

	/// <summary>Pack an existing capture run folder into a Fiddler .saz archive, then exit (the proxy is not started).</summary>
	public string? ExportSazFolder { get; init; }

	/// <summary>Where <see cref="ExportSazFolder"/> is written; null means next to the folder, with ".saz" added.</summary>
	public string? ExportSazOutput { get; init; }

	/// <summary>With capture on, also write this run's folder as a .saz archive when the proxy stops.</summary>
	public bool SazOnShutdown { get; init; }

	/// <summary>Start a browser that uses the proxy once it is listening.</summary>
	public bool OpenBrowser { get; init; }

	/// <summary>The page the browser opens first (see <see cref="OpenBrowser"/>); null means the browser's own start page.</summary>
	public string? OpenUrl { get; init; }

	/// <summary>
	/// Offer HTTP/2 (as well as HTTP/1.1) to clients on intercepted HTTPS connections. On by default;
	/// <c>--no-http2</c> turns it off, which is useful when debugging a problem that might be HTTP/2 specific.
	/// </summary>
	public bool EnableHttp2 { get; init; } = true;

	/// <summary>Still "mos" (from before the rename to MoSaProxy) so an existing CA folder keeps being found; see the naming note in CertificateAuthority.</summary>
	public static string DefaultCaDirectory =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mos", "ca");

	/// <summary>Replaces the normal check of an origin server's certificate. Meant for tests; null means normal validation.</summary>
	public System.Net.Security.RemoteCertificateValidationCallback? OriginCertificateValidation { get; init; }
}
