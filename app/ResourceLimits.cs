using System.Net.Sockets;

namespace MoSaProxy;

/// <summary>Recognizes the failures that mean the system has run out of something (as opposed to an ordinary error with one request or file).</summary>
public static class ResourceLimits {

	/// <summary>
	/// A plain-language name for the limit this exception means was hit, or null when it is some other kind of failure.
	/// Looks through inner exceptions, because the original error is often wrapped (for example in an HttpRequestException).
	/// </summary>
	public static string? Describe(Exception? exception) {
		for (var e = exception; e is not null; e = e.InnerException) {
			if (e is OutOfMemoryException)
				return "the process is out of memory";

			if (e is SocketException { SocketErrorCode: SocketError.TooManyOpenSockets or SocketError.NoBufferSpaceAvailable })
				return "the operating system has run out of sockets or network buffers";

			if (e is IOException io) {
				// On Windows the error code is in the low 16 bits of an HResult of the form 0x8007xxxx.
				bool win32 = (io.HResult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000);
				int code = io.HResult & 0xFFFF;
				if (win32 && (code == 112 /* ERROR_DISK_FULL */ || code == 39 /* ERROR_HANDLE_DISK_FULL */)
					|| io.Message.Contains("No space left on device", StringComparison.OrdinalIgnoreCase))
					return "the disk is full";
				if (win32 && code == 4 /* ERROR_TOO_MANY_OPEN_FILES */
					|| io.Message.Contains("Too many open files", StringComparison.OrdinalIgnoreCase))
					return "too many files are open";
			}
		}
		return null;
	}
}

/// <summary>
/// Notices when a limit has been hit and remembers the first one. It prints nothing itself: it raises <see cref="LimitReached"/>
/// (a console front end subscribes and prints, see <see cref="ResourceLimitConsoleReporter"/>) and completes
/// <see cref="WhenReached"/>, which the program waits on in order to stop. Ordinary errors are ignored here:
/// <see cref="Report"/> says whether it was a limit.
/// </summary>
public sealed class ResourceLimitMonitor(Func<Exception, string?>? classify = null) {
	readonly TaskCompletionSource<string> _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
	readonly Func<Exception, string?> _classify = classify ?? ResourceLimits.Describe;

	/// <summary>
	/// Raised once, for the first limit that is hit, with the description ("the disk is full (while writing capture file 12_s.txt)").
	/// It runs on the thread that hit the limit, so a handler should be quick. A handler that throws is ignored: it must not
	/// break the code that was already failing.
	/// </summary>
	public event Action<string>? LimitReached;

	/// <summary>Completes, with the description, when the first limit is hit. Never completes if none is.</summary>
	public Task<string> WhenReached => _reached.Task;

	/// <summary>The description of the first limit that was hit, or null.</summary>
	public string? Reached => _reached.Task.IsCompletedSuccessfully ? _reached.Task.Result : null;

	/// <summary>
	/// Call this from a place that failed. If the failure means a limit was hit, it is recorded (and <see cref="LimitReached"/>
	/// raised, once) and true is returned; otherwise nothing happens and false is returned.
	/// </summary>
	/// <param name="whileDoing">What was being done, for the message: "writing capture file 12_s.txt".</param>
	public bool Report(string whileDoing, Exception exception) {
		string? limit = _classify(exception);
		if (limit is null) return false;

		string message = $"{limit} (while {whileDoing})";
		if (_reached.TrySetResult(message)) RaiseLimitReached(message);
		return true;
	}

	void RaiseLimitReached(string message) {
		var handlers = LimitReached;
		if (handlers is null) return;
		foreach (var handler in handlers.GetInvocationList().Cast<Action<string>>()) {
			try { handler(message); }
			catch { /* a failing subscriber must not break the code that hit the limit */ }
		}
	}
}
