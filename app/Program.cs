using MoSaProxy;

ProxyOptions options;
try { options = ProxyOptionsParser.Parse(args); }
catch (ArgumentException ex) {
	Console.Error.WriteLine(ex.Message);
	return 1;
}

if (options.UntrustCa) {
	if (!OperatingSystem.IsWindows()) {
		Console.Error.WriteLine("--untrust-ca is only supported on Windows.");
		return 1;
	}
	try {
		int removed = new RootTrust(new WindowsUserRootStore()).RemoveAll();
		Console.WriteLine(removed == 0
			? "No MoSaProxy root certificate was installed as trusted for your Windows user."
			: $"Removed {removed} MoSaProxy root certificate(s) from your Windows user's trusted roots.");
		return 0;
	}
	catch (Exception ex) {
		Console.Error.WriteLine($"Could not remove the MoSaProxy root certificate: {ex.Message}");
		return 1;
	}
}

if (options.ExportSazFolder is not null) {
	string output = options.ExportSazOutput ?? SazExporter.DefaultOutputPath(options.ExportSazFolder);
	return TryExportSaz(options.ExportSazFolder, output) ? 0 : 1;
}

var console = ColorConsole.Default;
var sinks = new List<IExchangeLog> { new ConsoleExchangeLog() };
if (options.LogPath is not null) sinks.Add(new JsonLinesExchangeLog(options.LogPath, options.LogMaxBytes, options.LogKeep));

// The first Ctrl+C starts a graceful stop (the host's own handler keeps the process alive); a second one means "stop now".
using var skipDrain = new CancellationTokenSource();
int interrupts = 0;
Console.CancelKeyPress += (_, _) => { if (Interlocked.Increment(ref interrupts) > 1) skipDrain.Cancel(); };

// Nothing is capped, but if the system itself runs out of something (disk space, memory, sockets, file handles) the proxy says
// which limit it was and stops, instead of carrying on while quietly losing data.
var limits = new ResourceLimitMonitor();
var limitReporter = new ResourceLimitConsoleReporter(limits, console);   // prints the limit when the monitor raises LimitReached
var log = new CompositeExchangeLog(sinks.ToArray(), ex => limits.Report("writing the log", ex));

var proxy = await ProxyHost.StartAsync(options, log, limits: limits);
StopResult result;
try {
	console.WriteLine($"HTTP proxy listening on 127.0.0.1:{proxy.Port}. Press Ctrl+C to stop.");
	if (proxy.CaptureRunDirectory is not null) console.WriteLine($"Capturing sessions to {proxy.CaptureRunDirectory}");
	bool rootTrusted = false;
	if (proxy.Authority is { } authority) {
		bool interactive = OperatingSystem.IsWindows() && !Console.IsInputRedirected;
		rootTrusted = InterceptionSetup.Run(authority, proxy.Port, ChromeLauncher.FindChrome(), new RootTrust(new WindowsUserRootStore()), Console.In, console, interactive);
	}

	// The browser is started last, once the proxy is listening and any trust question has been answered.
	if (options.OpenBrowser) {
		// Without interception HTTPS is only tunnelled, so there is no certificate to reject. With it, a Chrome that is not told to
		// ignore certificate errors would refuse every page unless Windows already trusts the root.
		bool ignoreCertificateErrors = options.Intercept && !rootTrusted;
		string chrome = ChromeLauncher.FindChrome();
		if (ChromeLauncher.TryStart(chrome, proxy.Port, ignoreCertificateErrors, options.OpenUrl, out string? openError))
			console.WriteLine($"Opened Chrome, using the proxy{(ignoreCertificateErrors ? " (it ignores certificate errors; use it only for browsing through this proxy)" : "")}.");
		else
			console.WriteLine($"{Fg.Red}Could not open Chrome:{Fg.Restore} {openError}");
	}

	// When and how to stop is the host's policy (drain on a stop request, stop at once on a limit); this only tells the user.
	result = await proxy.RunUntilStoppedAsync(options.DrainTimeout, skipDrain.Token, reason => {
		if (reason == StopReason.LimitReached) {
			console.WriteLine($"{Fg.Red}Stopping now.{Fg.Restore}");
			return;
		}
		var activity = proxy.Activity;
		console.WriteLine($"{Fg.Yellow}Stopping.{Fg.Restore} No new connections are accepted. Waiting up to {options.DrainTimeout.TotalSeconds:0.#} s for " +
			$"{activity.Exchanges} request(s), {activity.Tunnels} tunnel(s) and {activity.WebSockets} WebSocket(s) to finish (Ctrl+C again to stop now).");
	});
}
finally {
	await proxy.DisposeAsync();
}

// A limit hit at any time during the run is reported, including one hit while the proxy was draining after a stop request.
var report = result.Report;
if (result.Limit is not null) console.WriteLine($"{Fg.Red}Stopped because a limit was reached:{Fg.Restore} {result.Limit}.");
else if (report.Drained) console.WriteLine($"{Fg.Green}Stopped.{Fg.Restore} Everything that was in progress finished.");
else console.WriteLine($"{Fg.Yellow}Stopped.{Fg.Restore} The wait ended with work still running; aborted {report.Exchanges} request(s), {report.Tunnels} tunnel(s) and {report.WebSockets} WebSocket(s).");

// Only now are all the exchanges' files complete, so the archive can be packed.
if (options.SazOnShutdown && proxy.CaptureRunDirectory is { } runDirectory)
	TryExportSaz(runDirectory, SazExporter.DefaultOutputPath(runDirectory));
return result.Limit is not null ? 2 : 0;

static bool TryExportSaz(string runDirectory, string outputPath) {
	try {
		int sessions = SazExporter.Export(runDirectory, outputPath);
		Console.WriteLine($"Wrote {sessions} session(s) to {Path.GetFullPath(outputPath)}");
		return true;
	}
	catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) {
		Console.Error.WriteLine($"Could not write the .saz archive: {ex.Message}");
		return false;
	}
}
