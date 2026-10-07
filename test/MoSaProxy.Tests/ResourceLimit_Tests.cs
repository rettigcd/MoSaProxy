using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace MoSaProxy.Tests;

public class ResourceLimit_Tests {

	static IOException DiskFull() => new("There is not enough space on the disk.", unchecked((int)0x80070070));

	[Theory]
	[InlineData(0x80070070, "the disk is full")]               // ERROR_DISK_FULL
	[InlineData(0x80070027, "the disk is full")]               // ERROR_HANDLE_DISK_FULL
	[InlineData(0x80070004, "too many files are open")]        // ERROR_TOO_MANY_OPEN_FILES
	public void Describe_WindowsIoErrorCodes_NameTheLimit(uint hresult, string expected) {

		// Given: an IOException carrying a Windows error code (supplied by the test data)
		var exception = new IOException("some message", unchecked((int)hresult));

		// When: it is described
		var description = ResourceLimits.Describe(exception);

		// Then: it names the limit
		description.ShouldBe(expected);
	}

	[Theory]
	[InlineData("No space left on device", "the disk is full")]
	[InlineData("Too many open files", "too many files are open")]
	public void Describe_IoErrorMessagesFromOtherSystems_NameTheLimit(string message, string expected) {

		// Given: an IOException that only has the operating system's message (supplied by the test data)
		var exception = new IOException(message);

		// When: it is described
		var description = ResourceLimits.Describe(exception);

		// Then: it names the limit
		description.ShouldBe(expected);
	}

	[Fact]
	public void Describe_OutOfMemory_NamesTheLimit() {

		// Given: an out-of-memory exception
		var exception = new OutOfMemoryException();

		// When: it is described
		var description = ResourceLimits.Describe(exception);

		// Then: it says the process is out of memory
		description.ShouldBe("the process is out of memory");
	}

	[Theory]
	[InlineData(SocketError.TooManyOpenSockets)]
	[InlineData(SocketError.NoBufferSpaceAvailable)]
	public void Describe_SocketExhaustion_NamesTheLimit(SocketError error) {

		// Given: a socket exception meaning the system has no sockets or buffers left (supplied by the test data)
		var exception = new SocketException((int)error);

		// When: it is described
		var description = ResourceLimits.Describe(exception);

		// Then: it says the operating system ran out of sockets or network buffers
		description.ShouldBe("the operating system has run out of sockets or network buffers");
	}

	[Fact]
	public void Describe_LimitWrappedInsideAnotherException_IsStillRecognized() {

		// Given: socket exhaustion wrapped the way an HTTP client reports it (an HttpRequestException around a SocketException)
		var exception = new HttpRequestException("An error occurred while sending the request.", new SocketException((int)SocketError.NoBufferSpaceAvailable));

		// When: it is described
		var description = ResourceLimits.Describe(exception);

		// Then: the limit inside is found
		description.ShouldBe("the operating system has run out of sockets or network buffers");
	}

	[Theory]
	[InlineData("access")]
	[InlineData("notfound")]
	[InlineData("argument")]
	[InlineData("socket-refused")]
	[InlineData("io-other")]
	public void Describe_OrdinaryErrors_AreNotLimits(string kind) {

		// Given: an ordinary failure that is not about running out of anything (supplied by the test data)
		Exception exception = kind switch {
			"access" => new UnauthorizedAccessException("Access to the path is denied."),
			"notfound" => new FileNotFoundException("missing"),
			"argument" => new ArgumentException("bad"),
			"socket-refused" => new SocketException((int)SocketError.ConnectionRefused),
			_ => new IOException("The process cannot access the file because it is being used by another process.", unchecked((int)0x80070020)),
		};

		// When: it is described
		var description = ResourceLimits.Describe(exception);

		// Then: it is not a limit
		description.ShouldBeNull();
	}

	[Fact]
	public void Report_OrdinaryError_IsIgnored() {

		// Given: a monitor writing to an in-memory console
		var output = new StringWriter();
		var monitor = new ResourceLimitMonitor(new ColorConsole(output));

		// When: an ordinary error is reported
		var wasLimit = monitor.Report("writing a file", new UnauthorizedAccessException("denied"));

		// Then: it says it was not a limit, nothing is printed, and no limit is recorded
		wasLimit.ShouldBeFalse();
		output.ToString().ShouldBeEmpty();
		monitor.Reached.ShouldBeNull();
		monitor.WhenReached.IsCompleted.ShouldBeFalse();
	}

	[Fact]
	public async Task Report_DiskFull_IsReportedOnceInWordsAndRemembered() {

		// Given: a monitor writing to an in-memory console
		var output = new StringWriter();
		var monitor = new ResourceLimitMonitor(new ColorConsole(output));

		// When: a full disk is reported twice, from two different places
		var first = monitor.Report("writing capture file 12_s.txt", DiskFull());
		var second = monitor.Report("writing the log", DiskFull());

		// Then: both were recognized as a limit, but only the first is printed, the limit is remembered with where it happened,
		// and the "reached" task completes with that description
		first.ShouldBeTrue();
		second.ShouldBeTrue();
		output.ToString().TrimEnd().ShouldBe("Limit reached: the disk is full (while writing capture file 12_s.txt).");
		monitor.Reached.ShouldBe("the disk is full (while writing capture file 12_s.txt)");
		(await monitor.WhenReached).ShouldBe("the disk is full (while writing capture file 12_s.txt)");
	}

	[Fact]
	public void Report_CustomClassifier_DecidesWhatCountsAsALimit() {

		// Given: a monitor whose classifier treats an ArgumentException as a limit called "test limit"
		var monitor = new ResourceLimitMonitor(new ColorConsole(new StringWriter()), ex => ex is ArgumentException ? "test limit" : null);

		// When: an ArgumentException and an IOException (disk full) are reported
		var argument = monitor.Report("testing", new ArgumentException("x"));
		var disk = monitor.Report("testing", DiskFull());

		// Then: only the ArgumentException counts, because the custom classifier replaces the built-in one (so a full disk is not recognized here)
		argument.ShouldBeTrue();
		disk.ShouldBeFalse();
		monitor.Reached.ShouldBe("test limit (while testing)");
	}

	// ---- the places that feed the monitor ----------------------------------------------------------

	sealed class FailingLog(Exception failure) : IExchangeLog {
		public void Write(ExchangeRecord record) => throw failure;
	}

	sealed class CountingLog : IExchangeLog {
		public int Count { get; private set; }
		public void Write(ExchangeRecord record) => Count++;
	}

	[Fact]
	public void CompositeLog_OneSinkFails_TellsTheCallbackAndTheOtherSinksStillWrite() {

		// Given: a composite log with a sink that fails because the disk is full, followed by a sink that works
		var good = new CountingLog();
		var failures = new List<Exception>();
		var log = new CompositeExchangeLog(new IExchangeLog[] { new FailingLog(DiskFull()), good }, failures.Add);
		var originalError = Console.Error;
		Console.SetError(new StringWriter());

		try {
			// When: a record is written
			log.Write(new ExchangeRecord());
		}
		finally {
			Console.SetError(originalError);
		}

		// Then: the callback got the disk-full exception, and the working sink still received the record
		failures.Count.ShouldBe(1);
		ResourceLimits.Describe(failures[0]).ShouldBe("the disk is full");
		good.Count.ShouldBe(1);
	}

	sealed class DiskFullStream : Stream {
		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw DiskFull();
		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw DiskFull();
	}

	[Fact]
	public async Task TeeStream_CaptureFileCannotBeWritten_TellsTheCallbackOnceAndReadingContinues() {

		// Given: a tee over a 10-byte source whose capture file is on a full disk
		var failures = new List<Exception>();
		var tee = new TeeReadStream(new MemoryStream(new byte[10]), new DiskFullStream(), maxBytes: null, onSinkFailure: failures.Add);

		// When: the source is read in two pieces
		var buffer = new byte[5];
		int first = await tee.ReadAsync(buffer);
		int second = await tee.ReadAsync(buffer);

		// Then: both reads return their bytes (forwarding is unaffected), and the callback was told about the disk once, not on every read
		first.ShouldBe(5);
		second.ShouldBe(5);
		failures.Count.ShouldBe(1);
		ResourceLimits.Describe(failures[0]).ShouldBe("the disk is full");
	}

	[Fact]
	public void CaptureFile_WriteFails_ReportsTheFileToTheMonitor() {

		// Given: a capture whose monitor counts an ObjectDisposedException as a limit (the easiest write failure to cause on purpose),
		// and an exchange that has already been completed, so its files are closed
		using var folder = new TempFolder();
		var monitor = new ResourceLimitMonitor(new ColorConsole(new StringWriter()), ex => ex is ObjectDisposedException ? "test limit" : null);
		var capture = new FileSessionCapture(folder.Path, limits: monitor);
		var exchange = capture.BeginExchange();
		exchange.Complete(new ExchangeRecord());
		var originalError = Console.Error;
		Console.SetError(new StringWriter());

		try {
			// When: something is written to the closed request file
			exchange.WriteRequestHead("GET", "/", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		}
		finally {
			Console.SetError(originalError);
		}

		// Then: the monitor was told, naming the capture file
		monitor.Reached.ShouldNotBeNull();
		monitor.Reached.ShouldContain("writing capture file 1_c.txt");
	}

	[Fact]
	public async Task Proxy_LogWriteHitsALimitMidRequest_MonitorIsToldAndTheRequestStillSucceeds() {

		// Given: a proxy whose log fails with "disk full" (like a full disk under a log file), and a monitor wired to it as the program does
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"));
		var output = new StringWriter();
		var monitor = new ResourceLimitMonitor(new ColorConsole(output));
		var log = new CompositeExchangeLog(new IExchangeLog[] { new FailingLog(DiskFull()) }, ex => monitor.Report("writing the log", ex));
		var originalError = Console.Error;
		Console.SetError(new StringWriter());
		await using var host = await ProxyHost.StartAsync(new ProxyOptions { Port = 0 }, log, limits: monitor);
		using var client = new HttpClient(new HttpClientHandler { Proxy = new WebProxy($"http://127.0.0.1:{host.Port}"), UseProxy = true });

		try {
			// When: a request goes through the proxy
			var text = await client.GetStringAsync(origin.UrlFor("/"));

			// Then: the client still got its answer, and the monitor learned that the disk is full, with where it happened
			text.ShouldBe("hello");
			var reached = await monitor.WhenReached.WaitAsync(TimeSpan.FromSeconds(5));
			reached.ShouldBe("the disk is full (while writing the log)");
			output.ToString().ShouldContain("Limit reached: the disk is full (while writing the log).");
		}
		finally {
			Console.SetError(originalError);
		}
	}
}
