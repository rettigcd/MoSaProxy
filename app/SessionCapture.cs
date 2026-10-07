using System.Text;
using System.Xml.Linq;

namespace mos;

/// <summary>Source of per-exchange capture files (Fiddler-style N_c.txt / N_s.txt / N_m.xml).</summary>
public interface ISessionCapture {
	/// <summary>Starts capturing a new exchange. The exchange number is assigned here, in arrival order.</summary>
	IExchangeCapture BeginExchange();
}

/// <summary>
/// Capture of one exchange. Implementations must never throw: a capture problem must not break forwarding.
/// </summary>
public interface IExchangeCapture {
	int Number { get; }
	void WriteRequestHead(string method, string target, string protocol, IEnumerable<KeyValuePair<string, string>> headers);
	/// <summary>Returns a stream that reads from <paramref name="source"/> and copies what it reads into the request file.</summary>
	Stream TeeRequestBody(Stream source);
	void WriteResponseHead(string statusLine, IEnumerable<KeyValuePair<string, string>> headers);
	/// <summary>Returns a stream that reads from <paramref name="source"/> and copies what it reads into the response file.</summary>
	Stream TeeResponseBody(Stream source);
	void WriteResponseBody(ReadOnlySpan<byte> body);
	/// <summary>
	/// Records one WebSocket message (or close frame) in the exchange's N_w.txt file. For a message that is longer than
	/// the capture limit, <paramref name="payload"/> is the part that was kept and <paramref name="totalBytes"/> the full size.
	/// </summary>
	void WriteWebSocketMessage(bool fromClient, System.Net.WebSockets.WebSocketMessageType type, ReadOnlySpan<byte> payload, long totalBytes, string? note = null);
	/// <summary>Closes the files and writes the metadata file.</summary>
	void Complete(ExchangeRecord record);
}

public sealed class NullSessionCapture : ISessionCapture {
	public static readonly NullSessionCapture Instance = new();
	public IExchangeCapture BeginExchange() => NullExchangeCapture.Instance;
}

public sealed class NullExchangeCapture : IExchangeCapture {
	public static readonly NullExchangeCapture Instance = new();
	public int Number => 0;
	public void WriteRequestHead(string method, string target, string protocol, IEnumerable<KeyValuePair<string, string>> headers) { }
	public Stream TeeRequestBody(Stream source) => source;
	public void WriteResponseHead(string statusLine, IEnumerable<KeyValuePair<string, string>> headers) { }
	public Stream TeeResponseBody(Stream source) => source;
	public void WriteResponseBody(ReadOnlySpan<byte> body) { }
	public void WriteWebSocketMessage(bool fromClient, System.Net.WebSockets.WebSocketMessageType type, ReadOnlySpan<byte> payload, long totalBytes, string? note = null) { }
	public void Complete(ExchangeRecord record) { }
}

/// <summary>Writes each run's exchanges into a new sub-folder of <c>baseDirectory</c>, numbered from 1.</summary>
public sealed class FileSessionCapture : ISessionCapture {
	readonly long? _maxBodyBytes;
	int _last;

	readonly ResourceLimitMonitor? _limits;

	public FileSessionCapture(string baseDirectory, long? maxBodyBytes = null, ResourceLimitMonitor? limits = null) {
		_maxBodyBytes = maxBodyBytes;
		_limits = limits;
		Directory.CreateDirectory(baseDirectory);
		string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
		string run = Path.Combine(baseDirectory, stamp);
		for (int i = 2; Directory.Exists(run); i++) run = Path.Combine(baseDirectory, $"{stamp}-{i}");
		Directory.CreateDirectory(run);
		RunDirectory = run;
	}

	public string RunDirectory { get; }

	public IExchangeCapture BeginExchange() {
		int number = Interlocked.Increment(ref _last);
		return new FileExchangeCapture(RunDirectory, number, _maxBodyBytes, _limits);
	}
}

sealed class FileExchangeCapture : IExchangeCapture {
	// Headers are written as Latin-1 so each character maps to exactly one byte.
	static readonly Encoding HeaderEncoding = Encoding.Latin1;

	readonly string _directory;
	readonly long? _maxBodyBytes;
	FileStream? _client;
	FileStream? _server;
	TeeReadStream? _requestTee;
	TeeReadStream? _responseTee;
	FileStream? _messages;            // WebSocket messages, N_w.txt; opened on the first message
	bool _messagesOpened;
	readonly object _messagesLock = new();   // the two directions of a WebSocket write from different threads

	readonly ResourceLimitMonitor? _limits;

	public FileExchangeCapture(string directory, int number, long? maxBodyBytes, ResourceLimitMonitor? limits) {
		_directory = directory;
		_maxBodyBytes = maxBodyBytes;
		_limits = limits;
		Number = number;
		_client = Open($"{number}_c.txt");
		_server = Open($"{number}_s.txt");
	}

	public int Number { get; }

	FileStream? Open(string name) {
		try { return new FileStream(Path.Combine(_directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024, useAsync: true); }
		catch (Exception ex) {
			Console.Error.WriteLine($"Capture disabled for {name}: {ex.Message}");
			_limits?.Report($"creating capture file {name}", ex);
			return null;
		}
	}

	public void WriteRequestHead(string method, string target, string protocol, IEnumerable<KeyValuePair<string, string>> headers) =>
		WriteHead(ref _client, $"{method} {target} {protocol}", headers);

	public void WriteResponseHead(string statusLine, IEnumerable<KeyValuePair<string, string>> headers) =>
		WriteHead(ref _server, statusLine, headers);

	void WriteHead(ref FileStream? file, string firstLine, IEnumerable<KeyValuePair<string, string>> headers) {
		if (file is null) return;
		try {
			var text = new StringBuilder(firstLine).Append("\r\n");
			foreach (var (name, value) in headers) text.Append(name).Append(": ").Append(value).Append("\r\n");
			text.Append("\r\n");
			file.Write(HeaderEncoding.GetBytes(text.ToString()));
		}
		catch (Exception ex) { Fail(ref file, ex); }
	}

	public Stream TeeRequestBody(Stream source) => _requestTee = new TeeReadStream(source, _client, _maxBodyBytes, ex => _limits?.Report($"writing capture file {Number}_c.txt", ex));
	public Stream TeeResponseBody(Stream source) => _responseTee = new TeeReadStream(source, _server, _maxBodyBytes, ex => _limits?.Report($"writing capture file {Number}_s.txt", ex));

	public void WriteResponseBody(ReadOnlySpan<byte> body) {
		if (_server is null) return;
		try { _server.Write(body); }
		catch (Exception ex) { Fail(ref _server, ex); }
	}

	public void WriteWebSocketMessage(bool fromClient, System.Net.WebSockets.WebSocketMessageType type, ReadOnlySpan<byte> payload, long totalBytes, string? note = null) {
		lock (_messagesLock) {
			if (!_messagesOpened) { _messagesOpened = true; _messages = Open($"{Number}_w.txt"); }
			if (_messages is null) return;
			try {
				var text = new StringBuilder();
				text.Append('[').Append(DateTimeOffset.UtcNow.ToString("O")).Append("] ")
					.Append(fromClient ? "client -> origin" : "origin -> client").Append(' ')
					.Append(type.ToString().ToLowerInvariant()).Append(' ').Append(totalBytes).Append(" bytes");
				if (payload.Length < totalBytes) text.Append(" (first ").Append(payload.Length).Append(" kept)");
				if (note is not null) text.Append(' ').Append(note);
				text.Append("\r\n");
				if (type == System.Net.WebSockets.WebSocketMessageType.Binary) {
					// 32 bytes per line, offset first, so binary messages stay readable and diffable
					for (int offset = 0; offset < payload.Length; offset += 32) {
						var line = payload.Slice(offset, Math.Min(32, payload.Length - offset));
						text.Append("  ").Append(offset.ToString("x4")).Append(": ").Append(Convert.ToHexString(line).ToLowerInvariant()).Append("\r\n");
					}
				}
				else if (payload.Length > 0) {
					text.Append(Encoding.UTF8.GetString(payload)).Append("\r\n");
				}
				text.Append("\r\n");
				_messages.Write(Encoding.UTF8.GetBytes(text.ToString()));
			}
			catch (Exception ex) { Fail(ref _messages, ex); }
		}
	}

	public void Complete(ExchangeRecord record) {
		try {
			_client?.Dispose();
			_server?.Dispose();
			lock (_messagesLock) _messages?.Dispose();
			var meta = new XElement("Exchange",
				new XAttribute("Number", Number),
				new XElement("Timestamp", record.Timestamp.ToString("O")),
				new XElement("Client", record.Client),
				new XElement("Method", record.Method),
				new XElement("Url", record.Url),
				new XElement("Scheme", record.Scheme),
				new XElement("Host", record.Host),
				new XElement("Port", record.Port),
				new XElement("ClientHttpVersion", record.ClientHttpVersion),
				new XElement("OriginHttpVersion", record.OriginHttpVersion ?? ""),
				new XElement("Status", record.Status?.ToString() ?? ""),
				new XElement("RequestBytes", record.RequestBytes),
				new XElement("ResponseBytes", record.ResponseBytes),
				new XElement("RequestBodyTruncated", _requestTee?.Truncated ?? false),
				new XElement("ResponseBodyTruncated", _responseTee?.Truncated ?? false),
				new XElement("DurationMs", Math.Round(record.DurationMs, 1)),
				new XElement("Mode", record.Mode),
				new XElement("Error", record.Error ?? ""));
			if (record.ClientMessages is not null) meta.Add(new XElement("ClientMessages", record.ClientMessages));
			if (record.OriginMessages is not null) meta.Add(new XElement("OriginMessages", record.OriginMessages));
			// Write to a temp name and rename, so a reader never sees a half-written metadata file.
			string final = Path.Combine(_directory, $"{Number}_m.xml");
			string temp = final + ".tmp";
			meta.Save(temp);
			File.Move(temp, final, overwrite: true);
		}
		catch (Exception ex) {
			Console.Error.WriteLine($"Capture metadata failed for exchange {Number}: {ex.Message}");
			_limits?.Report($"saving capture metadata for exchange {Number}", ex);
		}
	}

	void Fail(ref FileStream? file, Exception ex) {
		Console.Error.WriteLine($"Capture write failed: {ex.Message}");
		_limits?.Report($"writing capture file {Path.GetFileName(file?.Name)}", ex);
		try { file?.Dispose(); } catch { }
		file = null;
	}
}

/// <summary>
/// Passes reads through to <c>inner</c> and copies the bytes read into <c>sink</c>. A failing sink is dropped
/// silently (the copy stops, the read keeps working). Copying stops after <c>maxBytes</c> and sets <see cref="Truncated"/>.
/// </summary>
public sealed class TeeReadStream(Stream inner, Stream? sink, long? maxBytes, Action<Exception>? onSinkFailure = null) : Stream {
	long _copied;
	bool _sinkFailed;

	public bool Truncated { get; private set; }

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
		int n = await inner.ReadAsync(buffer, cancellationToken);
		if (n > 0 && sink is not null && !_sinkFailed) {
			var chunk = buffer[..n];
			if (maxBytes is long max && _copied + n > max) {
				chunk = chunk[..(int)Math.Max(0, max - _copied)];
				Truncated = true;
			}
			try {
				if (chunk.Length > 0) await sink.WriteAsync(chunk, cancellationToken);
				_copied += chunk.Length;
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex) {
				_sinkFailed = true;
				onSinkFailure?.Invoke(ex);
			}
		}
		return n;
	}

	public override bool CanRead => true;
	public override bool CanSeek => false;
	public override bool CanWrite => false;
	public override long Length => throw new NotSupportedException();
	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use ReadAsync.");
	public override void Flush() { }
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
