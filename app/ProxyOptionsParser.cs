using System.Globalization;

namespace MoSaProxy;

/// <summary>
/// The command-line side of <see cref="ProxyOptions"/>: turns the arguments into settings and words the error and usage text.
/// The settings themselves, and their defaults, live in <see cref="ProxyOptions"/>; this class starts from those defaults.
/// </summary>
public static class ProxyOptionsParser {

	/// <summary>Shown after an unknown argument.</summary>
	public const string Usage =
		"Usage: MoSaProxy [--port N] [--log FILE] [--capture-max-body BYTES] [--connect-timeout SECONDS] [--intercept] " +
		"[--intercept-bypass HOSTS] [--ca-dir DIR] [--untrust-ca] [--no-http2] [--saz FILE] [--open [URL]] [--log-max-mb N] " +
		"[--log-keep N] [--drain-timeout SECONDS]";

	/// <exception cref="ArgumentException">An argument is unknown, is missing its value, or has a value that is out of range. The message is meant for the user.</exception>
	public static ProxyOptions Parse(string[] args) {
		var defaults = new ProxyOptions();
		int port = defaults.Port;
		string? logPath = defaults.LogPath;
		long? captureMaxBody = defaults.CaptureMaxBodyBytes;
		var connectTimeout = defaults.ConnectTimeout;
		bool intercept = defaults.Intercept;
		var bypass = new List<string>(defaults.InterceptBypass);
		string caDirectory = defaults.CaDirectory;
		bool untrustCa = defaults.UntrustCa;
		bool http2 = defaults.EnableHttp2;
		long logMaxBytes = defaults.LogMaxBytes;
		int logKeep = defaults.LogKeep;
		var drainTimeout = defaults.DrainTimeout;
		string? sazPath = defaults.SazPath;
		bool openBrowser = defaults.OpenBrowser;
		string? openUrl = defaults.OpenUrl;

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
				case "--capture-max-body":
					if (i + 1 >= args.Length || !long.TryParse(args[++i], out long max) || max < 0)
						throw new ArgumentException("--capture-max-body requires a number of bytes (0 or more).");
					captureMaxBody = max;
					break;
				case "--connect-timeout":
					if (i + 1 >= args.Length || !TryParseNumber(args[++i], out double seconds) || seconds <= 0 || seconds > 3600)
						throw new ArgumentException("--connect-timeout requires a number of seconds greater than 0 (up to 3600).");
					connectTimeout = TimeSpan.FromSeconds(seconds);
					break;
				case "--open":
					openBrowser = true;
					if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) openUrl = args[++i];
					break;
				case "--saz":
					if (i + 1 >= args.Length || args[i + 1].StartsWith("--")) throw new ArgumentException("--saz requires the .saz file to write when the proxy stops.");
					sazPath = args[++i];
					break;
				case "--log-max-mb":
					if (i + 1 >= args.Length || !TryParseNumber(args[++i], out double megabytes) || megabytes < 0 || megabytes > 1_000_000)
						throw new ArgumentException("--log-max-mb requires a number of megabytes (0 means never rotate).");
					logMaxBytes = (long)(megabytes * 1024 * 1024);
					break;
				case "--log-keep":
					if (i + 1 >= args.Length || !int.TryParse(args[++i], out logKeep) || logKeep < 0 || logKeep > 1000)
						throw new ArgumentException("--log-keep requires a number from 0 to 1000.");
					break;
				case "--drain-timeout":
					if (i + 1 >= args.Length || !TryParseNumber(args[++i], out double drainSeconds) || drainSeconds < 0 || drainSeconds > 3600)
						throw new ArgumentException("--drain-timeout requires a number of seconds from 0 to 3600 (0 stops at once).");
					drainTimeout = TimeSpan.FromSeconds(drainSeconds);
					break;
				case "--no-http2":
					http2 = false;
					break;
				case "--untrust-ca":
					untrustCa = true;
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
					throw new ArgumentException($"Unknown argument '{args[i]}'. {Usage}");
			}
		}

		if (bypass.Count > 0 && !intercept) throw new ArgumentException("--intercept-bypass only makes sense together with --intercept.");

		return new ProxyOptions {
			Port = port, LogPath = logPath, CaptureMaxBodyBytes = captureMaxBody,
			ConnectTimeout = connectTimeout, Intercept = intercept, InterceptBypass = bypass, CaDirectory = caDirectory, UntrustCa = untrustCa, EnableHttp2 = http2,
			SazPath = sazPath, OpenBrowser = openBrowser, OpenUrl = openUrl,
			LogMaxBytes = logMaxBytes, LogKeep = logKeep, DrainTimeout = drainTimeout,
		};
	}

	/// <summary>Numbers on the command line always use "." as the decimal point, whatever the machine's regional settings.</summary>
	static bool TryParseNumber(string text, out double value) =>
		double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
