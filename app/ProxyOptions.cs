namespace mos;

public sealed class ProxyOptions {

	/// <summary>Port to listen on (loopback only). 0 picks any free port.</summary>
	public int Port { get; init; } = 8080;

	/// <summary>Optional path of a JSON Lines file that every exchange is appended to.</summary>
	public string? LogPath { get; init; }

	/// <summary>How long to wait for the origin to start responding before answering 504.</summary>
	public TimeSpan UpstreamTimeout { get; init; } = TimeSpan.FromSeconds(100);

	/// <summary>How long to wait when opening a connection to the origin for a CONNECT tunnel.</summary>
	public TimeSpan ConnectTimeout { get; init; } = DefaultConnectTimeout;

	public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(15);

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

	public static string DefaultCaDirectory =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mos", "ca");

	/// <summary>Replaces the normal check of an origin server's certificate. Meant for tests; null means normal validation.</summary>
	public System.Net.Security.RemoteCertificateValidationCallback? OriginCertificateValidation { get; init; }

	public static ProxyOptions Parse(string[] args) {
		int port = 8080;
		string? logPath = null;
		string? captureDirectory = null;
		long? captureMaxBody = null;
		var connectTimeout = DefaultConnectTimeout;
		bool intercept = false;
		var bypass = new List<string>();
		string caDirectory = DefaultCaDirectory;
		for (int i = 0; i < args.Length; i++) {
			switch (args[i]) {
				case "--port":
					if (i + 1 >= args.Length || !int.TryParse(args[++i], out port) || port < 0 || port > 65535)
						throw new ArgumentException("--port requires a number from 0 to 65535.");
					break;
				case "--log":
					if (i + 1 >= args.Length) throw new ArgumentException("--log requires a file path.");
					logPath = args[++i];
					break;
				case "--capture":
					if (i + 1 >= args.Length) throw new ArgumentException("--capture requires a folder path.");
					captureDirectory = args[++i];
					break;
				case "--capture-max-body":
					if (i + 1 >= args.Length || !long.TryParse(args[++i], out long max) || max < 0)
						throw new ArgumentException("--capture-max-body requires a number of bytes (0 or more).");
					captureMaxBody = max;
					break;
				case "--connect-timeout":
					if (i + 1 >= args.Length
						|| !double.TryParse(args[++i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double seconds)
						|| seconds <= 0 || seconds > 3600)
						throw new ArgumentException("--connect-timeout requires a number of seconds greater than 0 (up to 3600).");
					connectTimeout = TimeSpan.FromSeconds(seconds);
					break;
				case "--intercept":
					intercept = true;
					break;
				case "--intercept-bypass":
					if (i + 1 >= args.Length) throw new ArgumentException("--intercept-bypass requires a comma-separated list of hosts.");
					bypass.AddRange(args[++i].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
					break;
				case "--ca-dir":
					if (i + 1 >= args.Length) throw new ArgumentException("--ca-dir requires a folder path.");
					caDirectory = args[++i];
					break;
				default:
					throw new ArgumentException($"Unknown argument '{args[i]}'. Usage: mos [--port N] [--log FILE] [--capture DIR] [--capture-max-body BYTES] [--connect-timeout SECONDS] [--intercept] [--intercept-bypass HOSTS] [--ca-dir DIR]");
			}
		}
		if (bypass.Count > 0 && !intercept) throw new ArgumentException("--intercept-bypass only makes sense together with --intercept.");
		return new ProxyOptions {
			Port = port, LogPath = logPath, CaptureDirectory = captureDirectory, CaptureMaxBodyBytes = captureMaxBody,
			ConnectTimeout = connectTimeout, Intercept = intercept, InterceptBypass = bypass, CaDirectory = caDirectory,
		};
	}
}
