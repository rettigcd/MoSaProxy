using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace MoSaProxy;

/// <summary>
/// Packs a capture run folder (N_c.txt, N_s.txt, N_m.xml, N_w.txt per exchange) into a Fiddler Session Archive (.saz): a zip file with
/// a "raw" folder holding the same numbered files, a [Content_Types].xml, and a human-readable _index.htm.
/// The request, response and WebSocket files are copied as they are. Each N_m.xml is rewritten from MoSaProxy's own
/// metadata into the "Session" form that Fiddler writes.
/// </summary>
public static class SazExporter {

	static readonly Regex RequestFile = new(@"^(\d+)_c\.txt$", RegexOptions.Compiled);

	// Byte for byte as Fiddler writes it: a space before "?>", CRLF between the lines, and nothing after the closing tag.
	const string ContentTypes =
		"<?xml version=\"1.0\" encoding=\"utf-8\" ?>\r\n" +
		"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\r\n" +
		"<Default Extension=\"htm\" ContentType=\"text/html\" />\r\n" +
		"<Default Extension=\"xml\" ContentType=\"application/xml\" />\r\n" +
		"<Default Extension=\"txt\" ContentType=\"text/plain\" />\r\n" +
		"</Types>";

	/// <summary>The zip comment. Fiddler writes "Fiddler (v6.0.20262.10021) Session Archive. See https://api.getfiddler.com/r/?fiddler"; this names MoSaProxy as the writer instead.</summary>
	static readonly string ArchiveComment =
		$"MoSaProxy (v{typeof(SazExporter).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}) Session Archive.";

	/// <summary>The default archive path for a run folder: the folder's own path with ".saz" added.</summary>
	public static string DefaultOutputPath(string runDirectory) => runDirectory.TrimEnd('\\', '/') + ".saz";

	/// <summary>Writes the archive and returns how many exchanges (sessions) it holds. An existing file at <paramref name="outputPath"/> is replaced.</summary>
	public static int Export(string runDirectory, string outputPath) {
		if (!Directory.Exists(runDirectory)) throw new DirectoryNotFoundException($"Capture folder not found: {runDirectory}");

		var numbers = Directory.GetFiles(runDirectory)
			.Select(path => RequestFile.Match(Path.GetFileName(path)))
			.Where(match => match.Success)
			.Select(match => int.Parse(match.Groups[1].Value))
			.Order()
			.ToList();
		if (numbers.Count == 0) throw new InvalidOperationException($"No captured exchanges (N_c.txt files) found in {runDirectory}");

		// Build beside the final name and rename, so a failed export never leaves a half-written archive under the real name.
		string temp = outputPath + ".tmp";
		var rows = new List<IndexRow>();
		try {
			using (var file = File.Create(temp))
			using (var zip = new ZipArchive(file, ZipArchiveMode.Create)) {
				zip.Comment = ArchiveComment;
				// Fiddler's order: the raw/ folder entry, each session's request, response, metadata (then WebSocket messages), the index, and the content types last.
				zip.CreateEntry("raw/", CompressionLevel.Optimal);
				foreach (int number in numbers) {
					var exchange = ReadExchange(number, runDirectory);
					AddRequestFile(zip, runDirectory, exchange);
					AddFile(zip, runDirectory, number, "s.txt");
					AddBytes(zip, ArchivePath(number, "m.xml"), FiddlerMetadata(exchange));
					AddFile(zip, runDirectory, number, "w.txt");
					var (caching, contentType) = ReadIndexHeaders(Path.Combine(runDirectory, $"{number}_s.txt"));
					rows.Add(new IndexRow(exchange, ReadRequestLine(Path.Combine(runDirectory, $"{number}_c.txt")), caching, contentType));
				}
				AddBytes(zip, "_index.htm", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(IndexPage(rows))).ToArray());   // with a byte-order mark, as Fiddler writes it
				AddText(zip, "[Content_Types].xml", ContentTypes);
			}
			File.Move(temp, outputPath, overwrite: true);
		}
		finally {
			if (File.Exists(temp)) File.Delete(temp);
		}
		return numbers.Count;
	}

	/// <summary>The path inside the archive. Fiddler zero-pads the session number to at least three digits (raw/001_c.txt).</summary>
	static string ArchivePath(int number, string suffix) => $"raw/{number:D3}_{suffix}";

	// ---- reading MoSaProxy's own metadata -------------------------------------------------------------------------------

	sealed record Exchange(
		int Number, DateTimeOffset End, double DurationMs, string Client, string Method, string Url, string Host, int Port, string Scheme,
		string ClientProtocol, string OriginProtocol, string Status, long RequestBytes, long ResponseBytes, string Mode, string Error,
		string ClientMessages, string OriginMessages, bool Truncated, string RequestSource, string ResponseSource, bool ClientReused, bool OriginReused,
		IReadOnlyDictionary<string, DateTimeOffset> Times, int ClientPort, int OriginConnectionId, string ClientTlsVersion);

	record IndexRow(Exchange Exchange, string RequestLine, string Caching, string ContentType);

	static Exchange ReadExchange(int number, string directory) {
		string metaPath = Path.Combine(directory, $"{number}_m.xml");
		XElement? root = null;
		try { if (File.Exists(metaPath)) root = XDocument.Load(metaPath).Root; }
		catch (XmlException) { /* unreadable metadata: fall back to what the files themselves tell us */ }

		string Text(string name) => root?.Element(name)?.Value ?? "";
		long Number(string name) => long.TryParse(Text(name), out var value) ? value : 0;

		var end = DateTimeOffset.TryParse(Text("Timestamp"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
			? parsed : new DateTimeOffset(File.GetLastWriteTimeUtc(Path.Combine(directory, $"{number}_c.txt")), TimeSpan.Zero);
		double duration = double.TryParse(Text("DurationMs"), System.Globalization.CultureInfo.InvariantCulture, out var ms) ? ms : 0;
		bool truncated = Text("RequestBodyTruncated") == "true" || Text("ResponseBodyTruncated") == "true";

		return new Exchange(number, end, duration, Text("Client"), Text("Method"), Text("Url"), Text("Host"), (int)Number("Port"), Text("Scheme"),
			Text("ClientHttpVersion"), Text("OriginHttpVersion"), Text("Status"), Number("RequestBytes"), Number("ResponseBytes"),
			Text("Mode"), Text("Error"), Text("ClientMessages"), Text("OriginMessages"), truncated, Text("RequestSource"), Text("ResponseSource"),
			Text("ClientConnectionReused") == "true", Text("OriginConnectionReused") == "true", ReadTimes(root),
			(int)Number("ClientPort"), (int)Number("OriginConnectionId"), Text("ClientTlsVersion"));
	}

	/// <summary>The moments the proxy observed (the Times element of the capture metadata), by Fiddler's timer names. Empty when none were recorded.</summary>
	static Dictionary<string, DateTimeOffset> ReadTimes(XElement? root) {
		var times = new Dictionary<string, DateTimeOffset>();
		foreach (var attribute in root?.Element("Times")?.Attributes() ?? [])
			if (DateTimeOffset.TryParse(attribute.Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at)) times[attribute.Name.LocalName] = at;
		return times;
	}

	static string ReadRequestLine(string requestFile) {
		try {
			using var reader = new StreamReader(requestFile, Encoding.Latin1);
			// (a request made of anything but HTTP has no line worth showing, and could hold control characters)
			string line = reader.ReadLine() ?? "";
			return line.Length < 8192 && RequestLineText.IsMatch(line) ? line : "";
		}
		catch (IOException) { return ""; }
	}

	// ---- Fiddler's _m.xml ------------------------------------------------------------------------------------------

	/// <summary>The bits of Fiddler's SessionFlags enum that MoSaProxy can report (values from Telerik's FiddlerCore API reference).</summary>
	[Flags]
	enum FiddlerBits : uint {
		None = 0,
		IsHTTPS = 0x1,
		ClientPipeReused = 0x8,
		ServerPipeReused = 0x10,
		ResponseGeneratedByFiddler = 0x100,
		IsBlindTunnel = 0x1000,
		IsDecryptingTunnel = 0x2000,
	}

	/// <summary>
	/// Fiddler's BitFlags value, written in hex. A CONNECT's response is made by the proxy itself, so it carries ResponseGeneratedByFiddler and
	/// the kind of tunnel; any other session is IsHTTPS when its URL is https, plus the two pipe-reuse bits. Fiddler also sets streaming bits
	/// that MoSaProxy does not record, and one it writes on every decrypted request (0x2) that the published list does not explain;
	/// those are left out (docs/specs/saz-format.md section 6.2).
	/// </summary>
	static FiddlerBits BitFlags(Exchange e) {
		if (e.Method == "CONNECT") return FiddlerBits.ResponseGeneratedByFiddler | e.Mode switch {
			"intercept" => FiddlerBits.IsDecryptingTunnel,
			"tunnel" => FiddlerBits.IsBlindTunnel,
			_ => FiddlerBits.None,
		};
		return (e.Scheme == "https" ? FiddlerBits.IsHTTPS : FiddlerBits.None)
			| (e.ClientReused ? FiddlerBits.ClientPipeReused : FiddlerBits.None)
			| (e.OriginReused ? FiddlerBits.ServerPipeReused : FiddlerBits.None);
	}

	/// <summary>
	/// Fiddler's per-session metadata: a Session element with the twelve named timers and the session flags.
	/// The timers use the moments the proxy observed (see <see cref="ExchangeTimes"/>). Where it observed none (an exchange recorded before
	/// they were kept, or a CONNECT), the request-side timers are the start time and the response-side timers the end time. Some timers
	/// cannot be observed and are approximated: ClientBeginRequest is when the headers were read, ServerConnected is when the request
	/// was about to be sent, ServerBeginResponse is when the response headers arrived, and ServerDoneResponse is when the client got
	/// the last byte (the body is copied as it arrives). DNS, connect and TLS times are 0, and a CONNECT has no server response timers.
	/// </summary>
	static byte[] FiddlerMetadata(Exchange e) {
		DateTimeOffset begin = e.End - TimeSpan.FromMilliseconds(e.DurationMs);
		DateTimeOffset At(string name, DateTimeOffset fallback) => e.Times.TryGetValue(name, out var at) ? at : fallback;
		bool tunnel = e.Method == "CONNECT";

		var gotHeaders = At("GotRequestHeaders", begin);
		var doneRequest = At("ClientDoneRequest", gotHeaders);
		var requestToOrigin = At("FiddlerBeginRequest", doneRequest);
		var gotResponse = At("GotResponseHeaders", e.End);
		var doneResponse = At("ClientDoneResponse", e.End);

		XAttribute T(string name, DateTimeOffset time) => new(name, time.ToString("O"));
		XAttribute Unset(string name) => new(name, "0001-01-01T00:00:00");   // a stage that never happened
		XAttribute ResponseT(string name, DateTimeOffset time) => tunnel ? Unset(name) : T(name, time);

		var flags = new List<KeyValuePair<string, string>>();
		void Flag(string name, string value) { if (value.Length > 0) flags.Add(KeyValuePair.Create(name, value)); }
		Flag("x-clientip", e.Client);
		if (e.ClientPort > 0) Flag("x-clientport", e.ClientPort.ToString());
		Flag("https-client-version", e.ClientTlsVersion);
		// the response body as it crossed the wire, with thousands separators; a tunnel's bytes are in TunnelInfo, not here
		Flag("x-responsebodytransferlength", (e.Method == "CONNECT" ? 0 : e.ResponseBytes).ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
		// Fiddler names the origin connection a request reused, and writes nothing for a request that opened its own
		if (e.OriginReused && e.OriginConnectionId > 0) Flag("x-serversocket", $"REUSE ServerPipe#{e.OriginConnectionId}");
		Flag("x-mosaproxy-mode", e.Mode);
		Flag("x-mosaproxy-client-protocol", e.ClientProtocol);
		Flag("x-mosaproxy-origin-protocol", e.OriginProtocol);
		Flag("x-mosaproxy-error", e.Error);
		Flag("x-mosaproxy-client-messages", e.ClientMessages);
		Flag("x-mosaproxy-origin-messages", e.OriginMessages);
		Flag("x-mosaproxy-request-source", e.RequestSource);
		Flag("x-mosaproxy-response-source", e.ResponseSource);
		if (e.Truncated) Flag("x-mosaproxy-body-truncated", "true");

		var session = new XElement("Session",
			new XAttribute("SID", e.Number),
			new XAttribute("BitFlags", ((uint)BitFlags(e)).ToString("x")),
			new XElement("SessionTimers",
				T("ClientConnected", At("ClientConnected", gotHeaders)), T("ClientBeginRequest", gotHeaders), T("GotRequestHeaders", gotHeaders), T("ClientDoneRequest", doneRequest),
				new XAttribute("GatewayTime", 0), new XAttribute("DNSTime", 0), new XAttribute("TCPConnectTime", 0), new XAttribute("HTTPSHandshakeTime", 0),
				T("ServerConnected", requestToOrigin), T("FiddlerBeginRequest", requestToOrigin), T("ServerGotRequest", requestToOrigin > doneRequest ? requestToOrigin : doneRequest),
				ResponseT("ServerBeginResponse", gotResponse), ResponseT("GotResponseHeaders", gotResponse), ResponseT("ServerDoneResponse", doneResponse),
				T("ClientBeginResponse", At("ClientBeginResponse", gotResponse)), T("ClientDoneResponse", doneResponse)),
			new XElement("PipeInfo", e.ClientReused ? new XAttribute("CltReuse", "true") : null, e.OriginReused ? new XAttribute("Reused", "true") : null),
			e.Method == "CONNECT" ? new XElement("TunnelInfo", new XAttribute("BytesEgress", e.RequestBytes), new XAttribute("BytesIngress", e.ResponseBytes)) : null,
			new XElement("SessionFlags", flags.Select(f => new XElement("SessionFlag", new XAttribute("N", f.Key), new XAttribute("V", f.Value)))));

		using var stream = new MemoryStream();
		var settings = new XmlWriterSettings { Indent = true, IndentChars = "  ", NewLineChars = "\r\n", NewLineHandling = NewLineHandling.Replace, Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true) };   // as Fiddler writes it: BOM, CRLF
		using (var writer = XmlWriter.Create(stream, settings)) new XDocument(new XDeclaration("1.0", "utf-8", null), session).Save(writer);
		return stream.ToArray();
	}

	// ---- _index.htm ----------------------------------------------------------------------------------------------

	/// <summary>
	/// The page Fiddler writes for people who unzip the archive: one line, twelve columns, each row linking its three files (with backslashes).
	/// MoSaProxy does not record the client process, comments or custom columns, so those cells are empty.
	/// </summary>
	static string IndexPage(IReadOnlyList<IndexRow> rows) {
		string H(string text) => WebUtility.HtmlEncode(text);
		var page = new StringBuilder();
		page.Append("<html><head><style>body,thead,td,a,p{font-family:verdana,sans-serif;font-size: 10px;}</style></head><body><table cols=12><thead><tr>")
			.Append("<th>&nbsp;</th><th>#</th><th>Result</th><th>Protocol</th><th>Host</th><th>URL</th><th>Body</th><th>Caching</th><th>Content-Type</th><th>Process</th><th>Comments</th><th>Custom</th>")
			.Append("</tr></thead><tbody>");
		foreach (var row in rows) {
			var e = row.Exchange;
			bool tunnel = e.Method == "CONNECT";
			string url = string.IsNullOrEmpty(e.Url) ? row.RequestLine : e.Url;
			string host = e.Host, path = url.Length == 0 ? "(not an HTTP request)" : url;
			if (tunnel) host = "Tunnel to";
			else if (url.IndexOf("://", StringComparison.Ordinal) is var scheme and > 0) {
				// split by hand: Uri would percent-escape the path, and Fiddler shows it as it was sent
				string rest = url[(scheme + 3)..];
				int slash = rest.IndexOf('/');
				host = slash < 0 ? rest : rest[..slash];
				path = slash < 0 ? "/" : rest[slash..];
			}
			string Link(string suffix) => ArchivePath(e.Number, suffix).Replace('/', (char)92);   // Fiddler's links use backslashes
			page.Append($"<tr><td><a href='{Link("c.txt")}'>C</a>&nbsp;<a href='{Link("s.txt")}'>S</a>&nbsp;<a href='{Link("m.xml")}'>M</a></td>")
				.Append($"<td>{e.Number}</td><td>{H(e.Status)}</td><td>{(tunnel ? "HTTP" : H(e.Scheme.ToUpperInvariant()))}</td><td>{H(host)}</td><td>{H(path)}</td>")
				.Append($"<td>{e.ResponseBytes.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}</td><td>{H(row.Caching)}</td><td>{H(row.ContentType)}</td><td></td><td></td><td></td></tr>");
		}
		return page.Append("</tbody></table></body></html>").ToString();
	}

	/// <summary>The response headers Fiddler's index shows, read from the start of the response file: Cache-Control, then "; Expires: ..." if there is one, and Content-Type.</summary>
	static (string Caching, string ContentType) ReadIndexHeaders(string responseFile) {
		try {
			using var reader = new StreamReader(responseFile, Encoding.Latin1);
			string? line = reader.ReadLine();           // the status line
			string cacheControl = "", expires = "", contentType = "";
			while ((line = reader.ReadLine()) is { Length: > 0 }) {
				int colon = line.IndexOf(':');
				if (colon <= 0) continue;
				string name = line[..colon], value = line[(colon + 1)..].Trim();
				if (name.Equals("Cache-Control", StringComparison.OrdinalIgnoreCase) && cacheControl.Length == 0) cacheControl = value;
				else if (name.Equals("Expires", StringComparison.OrdinalIgnoreCase) && expires.Length == 0) expires = value;
				else if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) && contentType.Length == 0) contentType = value;
			}
			string caching = cacheControl.Length > 0 && expires.Length > 0 ? $"{cacheControl}; Expires: {expires}"
				: cacheControl.Length > 0 ? cacheControl : expires.Length > 0 ? $"Expires: {expires}" : "";
			return (caching, contentType);
		}
		catch (IOException) { return ("", ""); }
	}

	// ---- zip helpers ---------------------------------------------------------------------------------------------

	/// <summary>Copies a capture file into the archive unchanged, if the exchange has one.</summary>
	static void AddFile(ZipArchive zip, string runDirectory, int number, string suffix) {
		string source = Path.Combine(runDirectory, $"{number}_{suffix}");
		if (File.Exists(source)) zip.CreateEntryFromFile(source, ArchivePath(number, suffix), CompressionLevel.Optimal);
	}

	/// <summary>
	/// Copies the request file into the archive. Fiddler writes the request line of a request that arrived inside a tunnel with the absolute
	/// URL (<c>GET https://host/path HTTP/1.1</c>), where the client sent only the path; the capture file holds what the client sent, so the
	/// first line is rewritten here and every other byte is copied unchanged.
	/// </summary>
	static void AddRequestFile(ZipArchive zip, string runDirectory, Exchange e) {
		string source = Path.Combine(runDirectory, $"{e.Number}_c.txt");
		if (!File.Exists(source)) return;
		using var input = File.OpenRead(source);
		var start = new byte[65536];
		int read = input.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);

		// Fiddler rejects a session whose request has no end of headers (it reports an error and leaves the session out). A request that is
		// short enough to have been read whole and has none (the client sent something that is not HTTP, or stopped part way through its
		// headers) is made loadable, so that the session appears.
		if (read < start.Length && start.AsSpan(0, read).IndexOf("\r\n\r\n"u8) < 0) {
			using var repaired = zip.CreateEntry(ArchivePath(e.Number, "c.txt"), CompressionLevel.Optimal).Open();
			repaired.Write(LoadableRequest(start.AsSpan(0, read)));
			return;
		}

		int eol = start.AsSpan(0, read).IndexOf("\r\n"u8);
		string? line = eol > 0 ? AbsoluteRequestLine(Encoding.Latin1.GetString(start, 0, eol), e) : null;

		using var output = zip.CreateEntry(ArchivePath(e.Number, "c.txt"), CompressionLevel.Optimal).Open();
		if (line is null) output.Write(start, 0, read);
		else {
			output.Write(Encoding.Latin1.GetBytes(line));
			output.Write(start, eol, read - eol);
		}
		input.CopyTo(output);
	}

	static readonly Regex RequestLineText = new(@"^[A-Za-z]+ \S+ HTTP/\d(\.\d)?$", RegexOptions.Compiled);

	/// <summary>
	/// A request that has no end of headers, in a form Fiddler can load. If it starts like an HTTP request (the client stopped part way
	/// through its headers) the missing blank line is added. Otherwise it is not HTTP at all (empty, or another protocol's bytes sent through
	/// a tunnel), so it is shown as a POST with a note, and the bytes the client sent as the body.
	/// </summary>
	static byte[] LoadableRequest(ReadOnlySpan<byte> request) {
		int eol = request.IndexOf("\r\n"u8);
		string firstLine = Encoding.Latin1.GetString(eol < 0 ? request : request[..eol]);
		if (RequestLineText.IsMatch(firstLine)) {
			var terminator = request.EndsWith("\r\n"u8) ? "\r\n"u8 : "\r\n\r\n"u8;
			return [.. request, .. terminator];
		}
		var head = Encoding.Latin1.GetBytes(
			"POST / HTTP/1.1\r\nHost: not-http.invalid\r\n" +
			$"Content-Length: {request.Length}\r\n" +
			"X-MoSaProxy-Note: the client sent bytes that are not an HTTP request; they follow as the body\r\n\r\n");
		return [.. head, .. request];
	}

	/// <summary>The request line with an absolute URL, or null when it needs no change (it already has one, or it is not a request for a path).</summary>
	static string? AbsoluteRequestLine(string line, Exchange e) {
		var parts = line.Split(' ');
		if (parts.Length != 3 || !parts[1].StartsWith('/') || parts[0] == "CONNECT" || e.Host.Length == 0) return null;
		if (e.Scheme is not ("http" or "https")) return null;
		int defaultPort = e.Scheme == "https" ? 443 : 80;
		string authority = e.Port == 0 || e.Port == defaultPort ? e.Host : $"{e.Host}:{e.Port}";
		return $"{parts[0]} {e.Scheme}://{authority}{parts[1]} {parts[2]}";
	}

	static void AddText(ZipArchive zip, string name, string text) => AddBytes(zip, name, new UTF8Encoding(false).GetBytes(text));

	static void AddBytes(ZipArchive zip, string name, byte[] bytes) {
		var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
		using var stream = entry.Open();
		stream.Write(bytes);
	}
}
