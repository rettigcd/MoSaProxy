namespace MoSaProxy;

/// <summary>
/// The console side of <see cref="ResourceLimitMonitor"/>: subscribes to its <see cref="ResourceLimitMonitor.LimitReached"/> event
/// and prints the limit in red. The monitor itself knows nothing about the console.
/// </summary>
public sealed class ResourceLimitConsoleReporter {
	readonly ColorConsole _console;

	/// <param name="monitor">The monitor to listen to. The reporter stays subscribed for as long as the monitor lives.</param>
	/// <param name="console">Where to print; null means the real console.</param>
	public ResourceLimitConsoleReporter(ResourceLimitMonitor monitor, ColorConsole? console = null) {
		_console = console ?? ColorConsole.Default;
		monitor.LimitReached += Print;
	}

	void Print(string limit) => _console.WriteLine($"{Fg.Red}Limit reached: {limit}.{Fg.Restore}");
}
