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
	public void Complete(ExchangeRecord record) { }
}

/// <summary>Writes each run's exchanges into a new sub-folder of <c>baseDirectory</c>, numbered from 1.</summary>
public sealed class FileSessionCapture : ISessionCapture {
	readonly long? _maxBodyBytes;
	int _last;

	public FileSessionCapture(string baseDirectory, long? maxBodyBytes = null) {
		_maxBodyBytes = maxBodyBytes;
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
		return new FileExchangeCapture(RunDirectory, number, _maxBodyBytes);
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

	public FileExchangeCapture(string directory, int number, long? maxBodyBytes) {
		_directory = directory;
		_maxBodyBytes = maxBodyBytes;
		Number = number;
		_client = Open($"{number}_c.txt");
		_server = Open($"{number}_s.txt");
	}

	public int Number { get; }

	FileStream? Open(string name) {
		try { return new FileStream(Path.Combine(_directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024, useAsync: true); }
		catch (Exception ex) { Console.Error.WriteLine($"Capture disabled for {name}: {ex.Message}"); return null; }
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

	public Stream TeeRequestBody(Stream source) => _requestTee = new TeeReadStream(source, _client, _maxBodyBytes);
	public Stream TeeResponseBody(Stream source) => _responseTee = new TeeReadStream(source, _server, _maxBodyBytes);

	public void WriteResponseBody(ReadOnlySpan<byte> body) {
		if (_server is null) return;
		try { _server.Write(body); }
		catch (Exception ex) { Fail(ref _server, ex); }
	}

	public void Complete(ExchangeRecord record) {
		try {
			_client?.Dispose();
			_server?.Dispose();
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
			// Write to a temp name and rename, so a reader never sees a half-written metadata file.
			string final = Path.Combine(_directory, $"{Number}_m.xml");
			string temp = final + ".tmp";
			meta.Save(temp);
			File.Move(temp, final, overwrite: true);
		}
		catch (Exception ex) { Console.Error.WriteLine($"Capture metadata failed for exchange {Number}: {ex.Message}"); }
	}

	static void Fail(ref FileStream? file, Exception ex) {
		Console.Error.WriteLine($"Capture write failed: {ex.Message}");
		try { file?.Dispose(); } catch { }
		file = null;
	}
}

/// <summary>
/// Passes reads through to <c>inner</c> and copies the bytes read into <c>sink</c>. A failing sink is dropped
/// silently (the copy stops, the read keeps working). Copying stops after <c>maxBytes</c> and sets <see cref="Truncated"/>.
/// </summary>
public sealed class TeeReadStream(Stream inner, Stream? sink, long? maxBytes) : Stream {
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
			catch { _sinkFailed = true; }
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
