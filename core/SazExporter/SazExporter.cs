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

	const string ContentTypes =
		"<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
		"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
		"<Default Extension=\"htm\" ContentType=\"text/html\" />" +
		"<Default Extension=\"xml\" ContentType=\"application/xml\" />" +
		"<Default Extension=\"txt\" ContentType=\"text/plain\" />" +
		"</Types>";

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
				AddText(zip, "[Content_Types].xml", ContentTypes);
				foreach (int number in numbers) {
					foreach (var suffix in new[] { "c.txt", "s.txt", "w.txt" }) {
						string source = Path.Combine(runDirectory, $"{number}_{suffix}");
						if (File.Exists(source)) zip.CreateEntryFromFile(source, $"raw/{number}_{suffix}", CompressionLevel.Optimal);
					}
					var exchange = ReadExchange(number, runDirectory);
					AddBytes(zip, $"raw/{number}_m.xml", FiddlerMetadata(exchange));
					rows.Add(new IndexRow(exchange, ReadRequestLine(Path.Combine(runDirectory, $"{number}_c.txt"))));
				}
				AddText(zip, "_index.htm", IndexPage(rows));
			}
			File.Move(temp, outputPath, overwrite: true);
		}
		finally {
			if (File.Exists(temp)) File.Delete(temp);
		}
		return numbers.Count;
	}

	// ---- reading MoSaProxy's own metadata -------------------------------------------------------------------------------

	sealed record Exchange(
		int Number, DateTimeOffset End, double DurationMs, string Client, string Method, string Url, string Host, int Port, string Scheme,
		string ClientProtocol, string OriginProtocol, string Status, long RequestBytes, long ResponseBytes, string Mode, string Error,
		string ClientMessages, string OriginMessages, bool Truncated, string RequestSource, string ResponseSource);

	record IndexRow(Exchange Exchange, string RequestLine);

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
			Text("Mode"), Text("Error"), Text("ClientMessages"), Text("OriginMessages"), truncated, Text("RequestSource"), Text("ResponseSource"));
	}

	static string ReadRequestLine(string requestFile) {
		try {
			using var reader = new StreamReader(requestFile, Encoding.Latin1);
			return reader.ReadLine() ?? "";
		}
		catch (IOException) { return ""; }
	}

	// ---- Fiddler's _m.xml ------------------------------------------------------------------------------------------

	/// <summary>
	/// Fiddler's per-session metadata: a Session element with the twelve named timers and the session flags.
	/// MoSaProxy only knows when an exchange finished and how long it took, so the request-side timers are all the start time and
	/// the response-side timers all the end time; timings Fiddler measures separately (DNS, connect, TLS) are 0.
	/// </summary>
	static byte[] FiddlerMetadata(Exchange e) {
		DateTimeOffset begin = e.End - TimeSpan.FromMilliseconds(e.DurationMs);
		XAttribute T(string name, DateTimeOffset time) => new(name, time.ToString("O"));

		var flags = new List<KeyValuePair<string, string>>();
		void Flag(string name, string value) { if (value.Length > 0) flags.Add(KeyValuePair.Create(name, value)); }
		Flag("x-clientIP", e.Client);
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
			new XAttribute("BitFlags", 0),
			new XElement("SessionTimers",
				T("ClientConnected", begin), T("ClientBeginRequest", begin), T("GotRequestHeaders", begin), T("ClientDoneRequest", begin),
				new XAttribute("GatewayTime", 0), new XAttribute("DNSTime", 0), new XAttribute("TCPConnectTime", 0), new XAttribute("HTTPSHandshakeTime", 0),
				T("ServerConnected", begin), T("FiddlerBeginRequest", begin), T("ServerGotRequest", begin),
				T("ServerBeginResponse", e.End), T("GotResponseHeaders", e.End), T("ServerDoneResponse", e.End),
				T("ClientBeginResponse", e.End), T("ClientDoneResponse", e.End)),
			new XElement("SessionFlags", flags.Select(f => new XElement("SessionFlag", new XAttribute("N", f.Key), new XAttribute("V", f.Value)))));

		using var stream = new MemoryStream();
		var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) };
		using (var writer = XmlWriter.Create(stream, settings)) new XDocument(new XDeclaration("1.0", "utf-8", null), session).Save(writer);
		return stream.ToArray();
	}

	// ---- _index.htm ----------------------------------------------------------------------------------------------

	static string IndexPage(IReadOnlyList<IndexRow> rows) {
		string H(string text) => WebUtility.HtmlEncode(text);
		var page = new StringBuilder();
		page.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>MoSaProxy session archive</title>")
			.Append("<style>body{font-family:sans-serif}table{border-collapse:collapse}td,th{border:1px solid #ccc;padding:2px 8px;text-align:left}</style></head><body>")
			.Append("<h3>MoSaProxy session archive</h3><table><tr><th>#</th><th>Result</th><th>Protocol</th><th>Host</th><th>URL</th><th>Body</th><th>Mode</th></tr>");
		foreach (var row in rows) {
			var e = row.Exchange;
			string url = string.IsNullOrEmpty(e.Url) ? row.RequestLine : e.Url;
			page.Append("<tr>")
				.Append($"<td><a href=\"raw/{e.Number}_c.txt\">{e.Number}</a></td>")
				.Append($"<td>{H(e.Status)}</td><td>{H(e.Scheme.ToUpperInvariant())}</td><td>{H(e.Host)}</td><td>{H(url)}</td>")
				.Append($"<td>{e.ResponseBytes}</td><td>{H(e.Mode)}</td></tr>");
		}
		return page.Append("</table></body></html>").ToString();
	}

	// ---- zip helpers ---------------------------------------------------------------------------------------------

	static void AddText(ZipArchive zip, string name, string text) => AddBytes(zip, name, new UTF8Encoding(false).GetBytes(text));

	static void AddBytes(ZipArchive zip, string name, byte[] bytes) {
		var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
		using var stream = entry.Open();
		stream.Write(bytes);
	}
}
