using System.Text;
using System.Xml.Linq;

namespace MoSaProxy;

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
	bool _responseGenerated;
	byte[]? _wireRequest, _wireResponse;     // the exact bytes, when known; they replace the files built from parsed data

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

	public void WriteResponseHead(string statusLine, IEnumerable<KeyValuePair<string, string>> headers, bool generated = false) {
		_responseGenerated = generated;
		WriteHead(ref _server, statusLine, headers);
	}

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

	public void SetWireBytes(byte[]? request, byte[]? response) {
		_wireRequest = request;
		_wireResponse = response;
	}

	/// <summary>
	/// Replaces a file written from parsed data with the exact bytes. The body limit applies to the body only, so the whole head is kept.
	/// Returns whether the bytes were cut at the limit. A file that could not be created stays disabled.
	/// </summary>
	bool ReplaceWithWireBytes(string name, bool fileWasOpen, byte[]? bytes) {
		if (bytes is null || !fileWasOpen) return false;
		bool truncated = false;
		if (_maxBodyBytes is { } max) {
			int headLength = bytes.AsSpan().IndexOf("\r\n\r\n"u8);
			if (headLength >= 0 && bytes.Length - (headLength + 4) > max) {
				bytes = bytes[..(headLength + 4 + (int)max)];
				truncated = true;
			}
		}
		try { File.WriteAllBytes(Path.Combine(_directory, name), bytes); }
		catch (Exception ex) {
			Console.Error.WriteLine($"Capture write failed: {ex.Message}");
			_limits?.Report($"writing capture file {name}", ex);
		}
		return truncated;
	}

	public void Complete(ExchangeRecord record) {
		try {
			bool clientOpen = _client is not null, serverOpen = _server is not null;
			_client?.Dispose();
			_server?.Dispose();
			bool requestFromWire = clientOpen && _wireRequest is not null, responseFromWire = serverOpen && _wireResponse is not null;
			bool requestTruncated = ReplaceWithWireBytes($"{Number}_c.txt", clientOpen, _wireRequest);
			bool responseTruncated = ReplaceWithWireBytes($"{Number}_s.txt", serverOpen, _wireResponse);
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
				new XElement("RequestSource", requestFromWire ? CaptureSource.Wire : CaptureSource.Parsed),
				new XElement("ResponseSource", responseFromWire ? CaptureSource.Wire : _responseGenerated ? CaptureSource.Generated : CaptureSource.Parsed),
				new XElement("RequestBodyTruncated", (_wireRequest is null ? _requestTee?.Truncated : requestTruncated) ?? false),
				new XElement("ResponseBodyTruncated", (_wireResponse is null ? _responseTee?.Truncated : responseTruncated) ?? false),
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
