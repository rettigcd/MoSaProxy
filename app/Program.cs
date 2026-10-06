using mos;

ProxyOptions options;
try { options = ProxyOptions.Parse(args); }
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
			? "No mos root certificate was installed as trusted for your Windows user."
			: $"Removed {removed} mos root certificate(s) from your Windows user's trusted roots.");
		return 0;
	}
	catch (Exception ex) {
		Console.Error.WriteLine($"Could not remove the mos root certificate: {ex.Message}");
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

var proxy = await ProxyHost.StartAsync(options, new CompositeExchangeLog(sinks.ToArray()));
ShutdownReport report;
try {
	console.WriteLine($"HTTP proxy listening on 127.0.0.1:{proxy.Port}. Press Ctrl+C to stop.");
	if (proxy.CaptureRunDirectory is not null) console.WriteLine($"Capturing sessions to {proxy.CaptureRunDirectory}");
	if (proxy.Authority is { } authority) {
		bool interactive = OperatingSystem.IsWindows() && !Console.IsInputRedirected;
		InterceptionSetup.Run(authority, proxy.Port, ChromeLauncher.FindChrome(), new RootTrust(new WindowsUserRootStore()), Console.In, console, interactive);
	}

	await proxy.WaitForStopRequestAsync();
	var activity = proxy.Activity;
	console.WriteLine($"{Fg.Yellow}Stopping.{Fg.Restore} No new connections are accepted. Waiting up to {options.DrainTimeout.TotalSeconds:0.#} s for " +
		$"{activity.Exchanges} request(s), {activity.Tunnels} tunnel(s) and {activity.WebSockets} WebSocket(s) to finish (Ctrl+C again to stop now).");
	report = await proxy.StopAsync(options.DrainTimeout, skipDrain.Token);
}
finally {
	await proxy.DisposeAsync();
}

if (report.Drained) console.WriteLine($"{Fg.Green}Stopped.{Fg.Restore} Everything that was in progress finished.");
else console.WriteLine($"{Fg.Yellow}Stopped.{Fg.Restore} The wait ended with work still running; aborted {report.Exchanges} request(s), {report.Tunnels} tunnel(s) and {report.WebSockets} WebSocket(s).");

// Only now are all the exchanges' files complete, so the archive can be packed.
if (options.SazOnShutdown && proxy.CaptureRunDirectory is { } runDirectory)
	TryExportSaz(runDirectory, SazExporter.DefaultOutputPath(runDirectory));
return 0;

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
