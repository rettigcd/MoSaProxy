using Shouldly;

namespace MoSaProxy.Tests;

public class ResourceLimitConsoleReporter_Tests {

	static IOException DiskFull() => new("There is not enough space on the disk.", unchecked((int)0x80070070));

	[Fact]
	public void LimitReached_DiskFull_PrintsTheLimitInWordsOnce() {

		// Given: a monitor whose limit is reported to a console reporter that writes to an in-memory console
		var output = new StringWriter();
		var monitor = new ResourceLimitMonitor();
		_ = new ResourceLimitConsoleReporter(monitor, new ColorConsole(output));

		// When: a full disk is reported twice, from two different places
		monitor.Report("writing capture file 12_s.txt", DiskFull());
		monitor.Report("writing the log", DiskFull());

		// Then: one line is printed, naming the limit and where it was hit (the second report adds nothing)
		output.ToString().TrimEnd().ShouldBe("Limit reached: the disk is full (while writing capture file 12_s.txt).");
	}

	[Fact]
	public void Report_OrdinaryError_PrintsNothing() {

		// Given: a console reporter listening to a monitor
		var output = new StringWriter();
		var monitor = new ResourceLimitMonitor();
		_ = new ResourceLimitConsoleReporter(monitor, new ColorConsole(output));

		// When: an error that is not a limit is reported
		monitor.Report("writing a file", new UnauthorizedAccessException("denied"));

		// Then: nothing is printed
		output.ToString().ShouldBeEmpty();
	}

	[Fact]
	public void LimitReached_MonitorWithNoReporter_PrintsNothing() {

		// Given: a monitor, and a console that no reporter is attached to
		var output = new StringWriter();
		_ = new ColorConsole(output);
		var monitor = new ResourceLimitMonitor();

		// When: a limit is reported
		monitor.Report("testing", DiskFull());

		// Then: nothing reaches the console, because the monitor itself never prints
		output.ToString().ShouldBeEmpty();
	}
}
