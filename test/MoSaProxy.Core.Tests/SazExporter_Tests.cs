using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace MoSaProxy.Tests;

public class SazExporter_Tests {

	static readonly DateTimeOffset Noon = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

	/// <summary>Captures two exchanges (a GET, and a WebSocket with one message) into a new run folder and returns its path.</summary>
	static string BuildRunFolder(TempFolder folder) {
		var capture = new FileSessionCapture(folder.Path);

		var get = capture.BeginExchange();
		get.WriteRequestHead("GET", "http://a.test/one", "HTTP/1.1", new[] { KeyValuePair.Create("Host", "a.test") });
		get.WriteResponseHead("HTTP/1.1 200 OK", new[] { KeyValuePair.Create("Content-Type", "text/plain") });
		get.WriteResponseBody("hello"u8);
		get.Complete(new ExchangeRecord {
			Timestamp = Noon, Client = "127.0.0.1", Method = "GET", Url = "http://a.test/one", Scheme = "http", Host = "a.test", Port = 80,
			ClientHttpVersion = "HTTP/1.1", OriginHttpVersion = "HTTP/1.1", Status = 200, ResponseBytes = 5, DurationMs = 250, Mode = "plain",
		});

		var socket = capture.BeginExchange();
		socket.WriteRequestHead("GET", "/chat", "HTTP/1.1", new[] { KeyValuePair.Create("Upgrade", "websocket") });
		socket.WriteResponseHead("HTTP/1.1 101 Switching Protocols", Array.Empty<KeyValuePair<string, string>>());
		socket.WriteWebSocketMessage(true, System.Net.WebSockets.WebSocketMessageType.Text, "hi"u8, 2);
		socket.Complete(new ExchangeRecord {
			Timestamp = Noon.AddSeconds(5), Client = "127.0.0.1", Method = "GET", Url = "https://a.test/chat", Scheme = "https", Host = "a.test", Port = 443,
			ClientHttpVersion = "HTTP/1.1", Status = 101, RequestBytes = 2, DurationMs = 1000, Mode = "websocket-closed", ClientMessages = 1, OriginMessages = 0,
		});
		return capture.RunDirectory;
	}

	static ZipArchive Open(string path) => ZipFile.OpenRead(path);

	static XElement ReadXml(ZipArchive zip, string name) {
		using var stream = zip.GetEntry(name)!.Open();
		return XDocument.Load(stream).Root!;
	}

	static byte[] ReadBytes(ZipArchive zip, string name) {
		using var stream = zip.GetEntry(name)!.Open();
		using var copy = new MemoryStream();
		stream.CopyTo(copy);
		return copy.ToArray();
	}

	[Fact]
	public void Export_RunFolder_WritesTheFiddlerArchiveLayout() {

		// Given: a capture run folder holding two exchanges, one of them a WebSocket with messages
		using var folder = new TempFolder();
		string run = BuildRunFolder(folder);
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		int sessions = SazExporter.Export(run, saz);

		// Then: two sessions were written, in a zip with a "raw" folder of numbered request, response, metadata and WebSocket files,
		// plus the content-types file and a readable index page
		sessions.ShouldBe(2);
		using var zip = Open(saz);
		zip.Entries.Select(e => e.FullName).Order().ShouldBe(new[] {
			"[Content_Types].xml", "_index.htm", "raw/", "raw/1_c.txt", "raw/1_m.xml", "raw/1_s.txt", "raw/2_c.txt", "raw/2_m.xml", "raw/2_s.txt", "raw/2_w.txt",
		}.Order());
		var types = ReadXml(zip, "[Content_Types].xml");
		types.Elements().Select(e => e.Attribute("Extension")!.Value).Order().ShouldBe(new[] { "htm", "txt", "xml" });
		Encoding.UTF8.GetString(ReadBytes(zip, "_index.htm")).ShouldContain("a.test");
	}

	[Fact]
	public void Export_RequestResponseAndWebSocketFiles_AreCopiedUnchangedExceptTheRequestLineTarget() {

		// Given: a capture run folder
		using var folder = new TempFolder();
		string run = BuildRunFolder(folder);
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(run, saz);

		// Then: every response and WebSocket file, and the first request (which already has an absolute URL), is byte-for-byte the one in the folder
		using var zip = Open(saz);
		foreach (var name in new[] { "1_c.txt", "1_s.txt", "2_s.txt", "2_w.txt" })
			ReadBytes(zip, $"raw/{name}").ShouldBe(File.ReadAllBytes(Path.Combine(run, name)));
	}

	[Fact]
	public void Export_Metadata_IsRewrittenInFiddlersSessionForm() {

		// Given: a capture run folder whose first exchange ended at 12:00:00 after 250 ms
		using var folder = new TempFolder();
		string run = BuildRunFolder(folder);
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported and the first session's metadata is read
		SazExporter.Export(run, saz);
		using var zip = Open(saz);
		var session = ReadXml(zip, "raw/1_m.xml");
		var timers = session.Element("SessionTimers")!;
		var flags = session.Element("SessionFlags")!.Elements("SessionFlag").ToDictionary(f => f.Attribute("N")!.Value, f => f.Attribute("V")!.Value);

		// Then: it is a Session with its number and bit flags, the twelve named timers (request side at the start, response side at the end),
		// and flags for the client address and MoSaProxy's own details
		session.Name.LocalName.ShouldBe("Session");
		session.Attribute("SID")!.Value.ShouldBe("1");
		session.Attribute("BitFlags")!.Value.ShouldBe("0");
		foreach (var timer in new[] { "ClientConnected", "ClientBeginRequest", "GotRequestHeaders", "ClientDoneRequest", "ServerConnected", "FiddlerBeginRequest",
			"ServerGotRequest", "ServerBeginResponse", "GotResponseHeaders", "ServerDoneResponse", "ClientBeginResponse", "ClientDoneResponse" })
			timers.Attribute(timer).ShouldNotBeNull(timer);
		DateTimeOffset.Parse(timers.Attribute("ClientBeginRequest")!.Value).ShouldBe(Noon.AddMilliseconds(-250));
		DateTimeOffset.Parse(timers.Attribute("ClientDoneResponse")!.Value).ShouldBe(Noon);
		flags["x-clientip"].ShouldBe("127.0.0.1");
		flags["x-mosaproxy-mode"].ShouldBe("plain");
		flags["x-mosaproxy-client-protocol"].ShouldBe("HTTP/1.1");
		flags["x-mosaproxy-origin-protocol"].ShouldBe("HTTP/1.1");
	}

	[Fact]
	public void Export_Metadata_IsWrittenLikeFiddlersFile() {

		// Given: a capture run folder
		using var folder = new TempFolder();
		string run = BuildRunFolder(folder);
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported and a session's metadata file is read as bytes
		SazExporter.Export(run, saz);
		using var zip = Open(saz);
		var bytes = ReadBytes(zip, "raw/1_m.xml");
		string text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);

		// Then: it starts with a UTF-8 byte-order mark, every line break is CRLF, and nothing follows the closing tag
		bytes.Take(3).ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
		text.ShouldStartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Session ");
		text.Replace("\r\n", "").ShouldNotContain("\n");
		text.ShouldEndWith("</Session>");
	}

	[Fact]
	public void Export_WebSocketSession_CarriesTheMessageCountsAsFlags() {

		// Given: a capture run folder whose second exchange is a closed WebSocket with 1 client message and 0 origin messages
		using var folder = new TempFolder();
		string run = BuildRunFolder(folder);
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported and the second session's flags are read
		SazExporter.Export(run, saz);
		using var zip = Open(saz);
		var flags = ReadXml(zip, "raw/2_m.xml").Element("SessionFlags")!.Elements("SessionFlag").ToDictionary(f => f.Attribute("N")!.Value, f => f.Attribute("V")!.Value);

		// Then: the counts and mode are recorded
		flags["x-mosaproxy-client-messages"].ShouldBe("1");
		flags["x-mosaproxy-origin-messages"].ShouldBe("0");
		flags["x-mosaproxy-mode"].ShouldBe("websocket-closed");
	}

	[Fact]
	public void Export_ExchangeWithoutMetadata_StillGetsASession() {

		// Given: a capture run folder where the first exchange's metadata file is missing (for example the proxy was killed mid-request)
		using var folder = new TempFolder();
		string run = BuildRunFolder(folder);
		File.Delete(Path.Combine(run, "1_m.xml"));
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(run, saz);

		// Then: the archive still has a metadata file for that session, numbered correctly
		using var zip = Open(saz);
		ReadXml(zip, "raw/1_m.xml").Attribute("SID")!.Value.ShouldBe("1");
	}

	[Fact]
	public void Export_ExistingArchive_IsReplacedAndNoTemporaryFileIsLeft() {

		// Given: a capture run folder and an archive that already exists at the destination
		using var folder = new TempFolder();
		string run = BuildRunFolder(folder);
		string saz = Path.Combine(folder.Path, "out.saz");
		File.WriteAllText(saz, "old contents");

		// When: it is exported there
		SazExporter.Export(run, saz);

		// Then: the file is now a valid archive, and no ".tmp" file was left behind
		using (var zip = Open(saz)) zip.Entries.Count.ShouldBeGreaterThan(0);
		File.Exists(saz + ".tmp").ShouldBeFalse();
	}

	[Fact]
	public void Export_MoreThanNineExchanges_IndexListsThemInNumericOrder() {

		// Given: a capture run folder with 11 exchanges
		using var folder = new TempFolder();
		var capture = new FileSessionCapture(folder.Path);
		for (int i = 0; i < 11; i++) {
			var exchange = capture.BeginExchange();
			exchange.WriteRequestHead("GET", "/", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
			exchange.Complete(new ExchangeRecord { Timestamp = Noon, Method = "GET", Url = $"http://a.test/{i + 1}" });
		}
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(capture.RunDirectory, saz);

		// Then: the index page lists exchange 9 before exchange 10 (numeric, not alphabetical, order)
		using var zip = Open(saz);
		var index = Encoding.UTF8.GetString(ReadBytes(zip, "_index.htm"));
		index.IndexOf(@"raw\09_c.txt", StringComparison.Ordinal).ShouldBeLessThan(index.IndexOf(@"raw\10_c.txt", StringComparison.Ordinal));
		index.IndexOf(@"raw\02_c.txt", StringComparison.Ordinal).ShouldBeLessThan(index.IndexOf(@"raw\10_c.txt", StringComparison.Ordinal));
	}

	[Fact]
	public void Export_Metadata_HasPipeInfoAndTunnelInfoWhereFiddlerWritesThem() {

		// Given: a capture run folder with a GET and a CONNECT tunnel that moved 100 bytes up and 200 down
		using var folder = new TempFolder();
		var capture = new FileSessionCapture(folder.Path);
		var get = capture.BeginExchange();
		get.WriteRequestHead("GET", "/", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		get.Complete(new ExchangeRecord { Timestamp = Noon, Method = "GET", Url = "http://a.test/", Scheme = "http", Mode = "plain" });
		var connect = capture.BeginExchange();
		connect.WriteRequestHead("CONNECT", "a.test:443", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		connect.Complete(new ExchangeRecord { Timestamp = Noon, Method = "CONNECT", Url = "a.test:443", Scheme = "https", Mode = "tunnel", RequestBytes = 100, ResponseBytes = 200 });
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(capture.RunDirectory, saz);

		// Then: both sessions have a PipeInfo element after the timers, only the CONNECT has TunnelInfo (with its byte counts), and SessionFlags comes last
		using var zip = Open(saz);
		var getSession = ReadXml(zip, "raw/1_m.xml");
		getSession.Elements().Select(x => x.Name.LocalName).ShouldBe(new[] { "SessionTimers", "PipeInfo", "SessionFlags" });
		var connectSession = ReadXml(zip, "raw/2_m.xml");
		connectSession.Elements().Select(x => x.Name.LocalName).ShouldBe(new[] { "SessionTimers", "PipeInfo", "TunnelInfo", "SessionFlags" });
		connectSession.Element("TunnelInfo")!.Attribute("BytesEgress")!.Value.ShouldBe("100");
		connectSession.Element("TunnelInfo")!.Attribute("BytesIngress")!.Value.ShouldBe("200");
	}

	[Theory]
	[InlineData("CONNECT", "intercept", "https", "2100")]  // IsDecryptingTunnel + ResponseGeneratedByFiddler
	[InlineData("CONNECT", "tunnel", "https", "1100")]     // IsBlindTunnel + ResponseGeneratedByFiddler
	[InlineData("CONNECT", "rejected", "https", "100")]    // ResponseGeneratedByFiddler only
	[InlineData("GET", "intercept", "https", "19")]        // IsHTTPS + ClientPipeReused + ServerPipeReused: the CONNECT used both connections first
	[InlineData("GET", "plain", "http", "0")]
	public void Export_BitFlags_FollowTheKindOfSession(string method, string mode, string scheme, string expected) {

		// Given: a capture run folder with one exchange of the given kind
		using var folder = new TempFolder();
		var capture = new FileSessionCapture(folder.Path);
		var exchange = capture.BeginExchange();
		exchange.WriteRequestHead(method, "/", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		exchange.Complete(new ExchangeRecord { Timestamp = Noon, Method = method, Url = "x", Scheme = scheme, Mode = mode });
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(capture.RunDirectory, saz);

		// Then: the session's BitFlags is the hex value Fiddler wrote for that kind of session
		using var zip = Open(saz);
		ReadXml(zip, "raw/1_m.xml").Attribute("BitFlags")!.Value.ShouldBe(expected);
	}

	[Fact]
	public void Export_IndexPage_IsInFiddlersForm() {

		// Given: a capture run folder with a response (cache and type headers, 12,345 body bytes) and a CONNECT tunnel
		using var folder = new TempFolder();
		var capture = new FileSessionCapture(folder.Path);
		var get = capture.BeginExchange();
		get.WriteRequestHead("GET", "https://a.test/p?q=1&r=2", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		get.WriteResponseHead("HTTP/1.1 200 OK", new[] {
			KeyValuePair.Create("Cache-Control", "max-age=900"), KeyValuePair.Create("Expires", "Tue, 27 Oct 2026 12:27:47 GMT"), KeyValuePair.Create("Content-Type", "text/html; charset=utf-8") });
		get.Complete(new ExchangeRecord { Timestamp = Noon, Method = "GET", Url = "https://a.test/p?q=1&r=2", Scheme = "https", Host = "a.test", Status = 200, ResponseBytes = 12345 });
		var connect = capture.BeginExchange();
		connect.WriteRequestHead("CONNECT", "b.test:443", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		connect.Complete(new ExchangeRecord { Timestamp = Noon, Method = "CONNECT", Url = "b.test:443", Scheme = "https", Host = "b.test", Status = 200 });
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(capture.RunDirectory, saz);

		// Then: the index starts with a byte-order mark, has Fiddler's twelve columns, and each row has the links, result, protocol, host, URL,
		// body (with a thousands separator), caching and content type Fiddler would show; a tunnel is "Tunnel to" with HTTP
		using var zip = Open(saz);
		var bytes = ReadBytes(zip, "_index.htm");
		bytes.Take(3).ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
		string index = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
		index.ShouldStartWith("<html><head><style>body,thead,td,a,p{font-family:verdana,sans-serif;font-size: 10px;}</style></head><body><table cols=12><thead><tr>");
		index.ShouldContain("<th>&nbsp;</th><th>#</th><th>Result</th><th>Protocol</th><th>Host</th><th>URL</th><th>Body</th><th>Caching</th><th>Content-Type</th><th>Process</th><th>Comments</th><th>Custom</th>");
		index.ShouldContain(@"<tr><td><a href='raw\1_c.txt'>C</a>&nbsp;<a href='raw\1_s.txt'>S</a>&nbsp;<a href='raw\1_m.xml'>M</a></td>"
			+ "<td>1</td><td>200</td><td>HTTPS</td><td>a.test</td><td>/p?q=1&amp;r=2</td><td>12,345</td><td>max-age=900; Expires: Tue, 27 Oct 2026 12:27:47 GMT</td><td>text/html; charset=utf-8</td><td></td><td></td><td></td></tr>");
		index.ShouldContain("<td>2</td><td>200</td><td>HTTP</td><td>Tunnel to</td><td>b.test:443</td><td>0</td>");
		index.ShouldEndWith("</tbody></table></body></html>");
		index.ShouldNotContain("\n");
	}

	[Fact]
	public void Export_UrlWithMarkup_IsEncodedInTheIndexPage() {

		// Given: a capture run folder with an exchange whose URL contains HTML
		using var folder = new TempFolder();
		var capture = new FileSessionCapture(folder.Path);
		var exchange = capture.BeginExchange();
		exchange.WriteRequestHead("GET", "/", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		exchange.Complete(new ExchangeRecord { Timestamp = Noon, Method = "GET", Url = "http://a.test/<script>alert(1)</script>" });
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(capture.RunDirectory, saz);

		// Then: the index page shows the text without letting it act as markup
		using var zip = Open(saz);
		var index = Encoding.UTF8.GetString(ReadBytes(zip, "_index.htm"));
		index.ShouldNotContain("<script>");
		index.ShouldContain("&lt;script&gt;");
	}

	[Fact]
	public void Export_FolderDoesNotExist_Throws() {

		// Given: a path that is not a folder
		using var folder = new TempFolder();

		// When: it is exported
		var export = () => { SazExporter.Export(Path.Combine(folder.Path, "missing"), Path.Combine(folder.Path, "out.saz")); };

		// Then: the error says the capture folder was not found
		Should.Throw<DirectoryNotFoundException>(export).Message.ShouldContain("Capture folder not found");
	}

	[Fact]
	public void Export_FolderWithoutCapturedExchanges_Throws() {

		// Given: an existing folder with no capture files in it
		using var folder = new TempFolder();

		// When: it is exported
		var export = () => { SazExporter.Export(folder.Path, Path.Combine(folder.Path, "out.saz")); };

		// Then: the error says nothing was captured, and no archive was created
		Should.Throw<InvalidOperationException>(export).Message.ShouldContain("No captured exchanges");
		File.Exists(Path.Combine(folder.Path, "out.saz")).ShouldBeFalse();
	}

	[Fact]
	public void DefaultOutputPath_RunFolder_IsTheFolderPathWithSazAdded() {

		// Given: run folder paths, with and without a trailing separator
		// (supplied below)

		// When: the default archive paths are worked out
		var plain = SazExporter.DefaultOutputPath(@"C:\captures\20261006-120000");
		var trailing = SazExporter.DefaultOutputPath(@"C:\captures\20261006-120000\");

		// Then: both are the folder path plus ".saz"
		plain.ShouldBe(@"C:\captures\20261006-120000.saz");
		trailing.ShouldBe(@"C:\captures\20261006-120000.saz");
	}

	[Fact]
	public void Export_Entries_AreInFiddlersOrder() {

		// Given: a capture run folder with two sessions, the second having WebSocket messages
		using var folder = new TempFolder();
		string run = BuildRunFolder(folder);
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(run, saz);

		// Then: the entries are the raw folder, each session's request, response and metadata (then WebSocket messages), the index, and the content types last
		using var zip = Open(saz);
		zip.Entries.Select(e => e.FullName).ShouldBe(new[] {
			"raw/", "raw/1_c.txt", "raw/1_s.txt", "raw/1_m.xml", "raw/2_c.txt", "raw/2_s.txt", "raw/2_m.xml", "raw/2_w.txt", "_index.htm", "[Content_Types].xml",
		});
	}

	[Fact]
	public void Export_ContentTypes_IsWrittenLikeFiddlersFile() {

		// Given: a capture run folder
		using var folder = new TempFolder();
		string run = BuildRunFolder(folder);
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(run, saz);

		// Then: [Content_Types].xml is the 293 bytes Fiddler writes: no byte-order mark, a space before "?>", CRLF between lines, nothing after the last tag
		using var zip = Open(saz);
		var bytes = ReadBytes(zip, "[Content_Types].xml");
		bytes.Length.ShouldBe(293);
		Encoding.UTF8.GetString(bytes).ShouldBe(
			"<?xml version=\"1.0\" encoding=\"utf-8\" ?>\r\n" +
			"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\r\n" +
			"<Default Extension=\"htm\" ContentType=\"text/html\" />\r\n" +
			"<Default Extension=\"xml\" ContentType=\"application/xml\" />\r\n" +
			"<Default Extension=\"txt\" ContentType=\"text/plain\" />\r\n" +
			"</Types>");
	}

	[Fact]
	public void Export_RecordedTimes_AreUsedForTheSessionTimers() {

		// Given: a capture run folder whose exchange recorded when each stage happened, 10 ms apart
		using var folder = new TempFolder();
		var capture = new FileSessionCapture(folder.Path);
		var exchange = capture.BeginExchange();
		exchange.WriteRequestHead("POST", "/", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		var t0 = Noon.AddSeconds(-1);
		exchange.Complete(new ExchangeRecord {
			Timestamp = Noon, Method = "POST", Url = "http://a.test/", Scheme = "http", DurationMs = 70,
			Times = new ExchangeTimes {
				ClientConnected = t0, GotRequestHeaders = t0.AddMilliseconds(10), ClientDoneRequest = t0.AddMilliseconds(20), FiddlerBeginRequest = t0.AddMilliseconds(30),
				GotResponseHeaders = t0.AddMilliseconds(40), ClientBeginResponse = t0.AddMilliseconds(50), ClientDoneResponse = t0.AddMilliseconds(60),
			},
		});
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(capture.RunDirectory, saz);

		// Then: each timer has its own moment, and the ones the proxy cannot observe take the nearest moment it can
		using var zip = Open(saz);
		var timers = ReadXml(zip, "raw/1_m.xml").Element("SessionTimers")!;
		DateTimeOffset At(string name) => DateTimeOffset.Parse(timers.Attribute(name)!.Value);
		At("ClientConnected").ShouldBe(t0);
		At("ClientBeginRequest").ShouldBe(t0.AddMilliseconds(10));
		At("GotRequestHeaders").ShouldBe(t0.AddMilliseconds(10));
		At("ClientDoneRequest").ShouldBe(t0.AddMilliseconds(20));
		At("FiddlerBeginRequest").ShouldBe(t0.AddMilliseconds(30));
		At("ServerGotRequest").ShouldBe(t0.AddMilliseconds(30));
		At("GotResponseHeaders").ShouldBe(t0.AddMilliseconds(40));
		At("ServerBeginResponse").ShouldBe(t0.AddMilliseconds(40));
		At("ClientBeginResponse").ShouldBe(t0.AddMilliseconds(50));
		At("ClientDoneResponse").ShouldBe(t0.AddMilliseconds(60));
		At("ServerDoneResponse").ShouldBe(t0.AddMilliseconds(60));
	}

	[Fact]
	public void Export_Connect_HasNoServerResponseTimers() {

		// Given: a capture run folder with a CONNECT tunnel
		using var folder = new TempFolder();
		var capture = new FileSessionCapture(folder.Path);
		var connect = capture.BeginExchange();
		connect.WriteRequestHead("CONNECT", "b.test:443", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		connect.Complete(new ExchangeRecord { Timestamp = Noon, Method = "CONNECT", Url = "b.test:443", Scheme = "https", Mode = "tunnel" });
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(capture.RunDirectory, saz);

		// Then: the three server-response timers are Fiddler's "never happened" value, as in a Fiddler-written tunnel
		using var zip = Open(saz);
		var timers = ReadXml(zip, "raw/1_m.xml").Element("SessionTimers")!;
		foreach (var name in new[] { "ServerBeginResponse", "GotResponseHeaders", "ServerDoneResponse" })
			timers.Attribute(name)!.Value.ShouldBe("0001-01-01T00:00:00", name);
		timers.Attribute("ClientDoneResponse")!.Value.ShouldNotBe("0001-01-01T00:00:00");
	}

	[Fact]
	public async Task Export_RealProxyCapture_HasTimersThatFollowTheExchange() {

		// Given: a proxy capturing a request to an origin that takes 300 ms to answer
		using var captureFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(async c => { await Task.Delay(300); await c.Response.WriteAsync("slow"); });
		string run;
		await using (var proxy = await TestProxy.StartAsync(new ProxyOptions { Port = 0, CaptureDirectory = captureFolder.Path })) {
			await proxy.Client.GetAsync(origin.UrlFor("/slow"));
			await TempFolder.WaitForFileAsync(Path.Combine(proxy.CaptureRunDirectory!, "1_m.xml"));
			run = proxy.CaptureRunDirectory!;
		}
		string saz = Path.Combine(captureFolder.Path, "real.saz");

		// When: the run folder is exported
		SazExporter.Export(run, saz);

		// Then: the timers are in order, and the wait for the origin shows between sending the request and getting the response headers
		using var zip = Open(saz);
		var timers = ReadXml(zip, "raw/1_m.xml").Element("SessionTimers")!;
		DateTimeOffset At(string name) => DateTimeOffset.Parse(timers.Attribute(name)!.Value);
		At("ClientConnected").ShouldBeLessThanOrEqualTo(At("GotRequestHeaders"));
		At("GotRequestHeaders").ShouldBeLessThanOrEqualTo(At("FiddlerBeginRequest"));
		At("FiddlerBeginRequest").ShouldBeLessThanOrEqualTo(At("GotResponseHeaders"));
		At("GotResponseHeaders").ShouldBeLessThanOrEqualTo(At("ClientBeginResponse"));
		At("ClientBeginResponse").ShouldBeLessThanOrEqualTo(At("ClientDoneResponse"));
		(At("GotResponseHeaders") - At("FiddlerBeginRequest")).TotalMilliseconds.ShouldBeGreaterThan(250);
	}

	[Theory]
	[InlineData("https", "a.test", 443, "POST /form?x=1 HTTP/1.1", "POST https://a.test/form?x=1 HTTP/1.1")]
	[InlineData("https", "a.test", 8443, "GET /p%20q HTTP/1.1", "GET https://a.test:8443/p%20q HTTP/1.1")]
	[InlineData("http", "a.test", 80, "GET / HTTP/1.1", "GET http://a.test/ HTTP/1.1")]
	[InlineData("https", "a.test", 443, "GET /page HTTP/2", "GET https://a.test/page HTTP/2")]
	[InlineData("http", "a.test", 80, "GET http://a.test/x HTTP/1.1", "GET http://a.test/x HTTP/1.1")]   // already absolute: unchanged
	[InlineData("https", "a.test", 443, "OPTIONS * HTTP/1.1", "OPTIONS * HTTP/1.1")]                      // not a path: unchanged
	public void Export_PathOnlyRequestLine_BecomesAbsoluteAndTheRestIsUnchanged(string scheme, string host, int port, string requestLine, string expected) {

		// Given: a capture run folder whose request line, headers and body were captured as the client sent them
		using var folder = new TempFolder();
		var capture = new FileSessionCapture(folder.Path);
		var exchange = capture.BeginExchange();
		exchange.WriteRequestHead("GET", "/", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		exchange.Complete(new ExchangeRecord { Timestamp = Noon, Method = "GET", Url = $"{scheme}://{host}/", Scheme = scheme, Host = host, Port = port });
		string rest = "\r\nHost: a.test\r\nContent-Length: 4\r\n\r\nbody";
		File.WriteAllBytes(Path.Combine(capture.RunDirectory, "1_c.txt"), Encoding.Latin1.GetBytes(requestLine + rest));
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(capture.RunDirectory, saz);

		// Then: the request line has the absolute URL Fiddler writes, and the headers and body are exactly as captured
		using var zip = Open(saz);
		Encoding.Latin1.GetString(ReadBytes(zip, "raw/1_c.txt")).ShouldBe(expected + rest);
	}

	[Fact]
	public void Export_LargeRequestBody_IsCopiedWhole() {

		// Given: a captured request whose body is far larger than the part read to find the request line
		using var folder = new TempFolder();
		var capture = new FileSessionCapture(folder.Path);
		var exchange = capture.BeginExchange();
		exchange.WriteRequestHead("POST", "/", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		exchange.Complete(new ExchangeRecord { Timestamp = Noon, Method = "POST", Url = "https://a.test/up", Scheme = "https", Host = "a.test", Port = 443 });
		var body = new byte[300_000];
		new Random(1).NextBytes(body);
		var head = Encoding.Latin1.GetBytes("POST /up HTTP/1.1\r\nHost: a.test\r\n\r\n");
		File.WriteAllBytes(Path.Combine(capture.RunDirectory, "1_c.txt"), head.Concat(body).ToArray());
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(capture.RunDirectory, saz);

		// Then: the archive holds the rewritten line followed by the whole head and body, byte for byte
		using var zip = Open(saz);
		var expected = Encoding.Latin1.GetBytes("POST https://a.test/up HTTP/1.1\r\nHost: a.test\r\n\r\n").Concat(body).ToArray();
		ReadBytes(zip, "raw/1_c.txt").ShouldBe(expected);
	}

	[Fact]
	public void Export_ConnectionFacts_BecomeFiddlersSessionFlags() {

		// Given: a capture run folder with a response of 12,345 bytes that reused origin connection 7, and a CONNECT
		using var folder = new TempFolder();
		var capture = new FileSessionCapture(folder.Path);
		var get = capture.BeginExchange();
		get.WriteRequestHead("GET", "/", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		get.Complete(new ExchangeRecord {
			Timestamp = Noon, Method = "GET", Url = "https://a.test/", Scheme = "https", Mode = "intercept", ResponseBytes = 12345,
			ClientPort = 51234, ClientTlsVersion = "Tls13", OriginConnectionReused = true, OriginConnectionId = 7,
		});
		var fresh = capture.BeginExchange();
		fresh.WriteRequestHead("GET", "/", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		fresh.Complete(new ExchangeRecord { Timestamp = Noon, Method = "GET", Url = "http://a.test/", Scheme = "http", OriginConnectionId = 8 });
		var connect = capture.BeginExchange();
		connect.WriteRequestHead("CONNECT", "b.test:443", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		connect.Complete(new ExchangeRecord { Timestamp = Noon, Method = "CONNECT", Url = "b.test:443", Scheme = "https", Mode = "tunnel", ResponseBytes = 999 });
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(capture.RunDirectory, saz);

		// Then: the first session has the client port, TLS version, transfer length (with a thousands separator) and the reused server pipe;
		// a request that opened its own connection has no x-serversocket and a plain one reports "None" as its TLS version; a CONNECT reports a body length of 0
		using var zip = Open(saz);
		Dictionary<string, string> Flags(string name) => ReadXml(zip, name).Element("SessionFlags")!.Elements("SessionFlag").ToDictionary(x => x.Attribute("N")!.Value, x => x.Attribute("V")!.Value);
		var first = Flags("raw/1_m.xml");
		first["x-clientport"].ShouldBe("51234");
		first["https-client-version"].ShouldBe("Tls13");
		first["x-responsebodytransferlength"].ShouldBe("12,345");
		first["x-serversocket"].ShouldBe("REUSE ServerPipe#7");
		Flags("raw/2_m.xml").ContainsKey("x-serversocket").ShouldBeFalse();
		Flags("raw/2_m.xml")["https-client-version"].ShouldBe("None");
		Flags("raw/3_m.xml")["x-responsebodytransferlength"].ShouldBe("0");
	}

	[Fact]
	public async Task Export_RealProxyCapture_CarriesTheConnectionFlags() {

		// Given: a proxy capturing two requests from one client over one kept-alive connection
		using var captureFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("from the origin"));
		string run;
		await using (var proxy = await TestProxy.StartAsync(new ProxyOptions { Port = 0, CaptureDirectory = captureFolder.Path })) {
			await proxy.Client.GetAsync(origin.UrlFor("/one"));
			await proxy.Client.GetAsync(origin.UrlFor("/two"));
			await TempFolder.WaitForFileAsync(Path.Combine(proxy.CaptureRunDirectory!, "2_m.xml"));
			run = proxy.CaptureRunDirectory!;
		}
		string saz = Path.Combine(captureFolder.Path, "real.saz");

		// When: the run folder is exported
		SazExporter.Export(run, saz);

		// Then: both sessions have the same client port and a 15-byte transfer length, and only the second names a reused server pipe
		using var zip = Open(saz);
		Dictionary<string, string> Flags(string name) => ReadXml(zip, name).Element("SessionFlags")!.Elements("SessionFlag").ToDictionary(x => x.Attribute("N")!.Value, x => x.Attribute("V")!.Value);
		var first = Flags("raw/1_m.xml");
		var second = Flags("raw/2_m.xml");
		int.Parse(first["x-clientport"]).ShouldBeGreaterThan(0);
		second["x-clientport"].ShouldBe(first["x-clientport"]);
		first["x-responsebodytransferlength"].ShouldBe("15");
		first.ContainsKey("x-serversocket").ShouldBeFalse();
		second["x-serversocket"].ShouldStartWith("REUSE ServerPipe#");
	}

	[Theory]
	[InlineData("", "POST / HTTP/1.1\r\nHost: not-http.invalid\r\nContent-Length: 0\r\nX-MoSaProxy-Note: the client sent bytes that are not an HTTP request; they follow as the body\r\n\r\n")]
	[InlineData(")\u0002\u0097\u0001binary", "POST / HTTP/1.1\r\nHost: not-http.invalid\r\nContent-Length: 10\r\nX-MoSaProxy-Note: the client sent bytes that are not an HTTP request; they follow as the body\r\n\r\n)\u0002\u0097\u0001binary")]
	[InlineData("GET /x HTTP/1.1\r\nHost: a.test\r\n", "GET /x HTTP/1.1\r\nHost: a.test\r\n\r\n")]
	[InlineData("GET /x HTTP/1.1\r\nHost: a.te", "GET /x HTTP/1.1\r\nHost: a.te\r\n\r\n")]
	public void Export_RequestWithNoEndOfHeaders_IsMadeLoadable(string captured, string expected) {

		// Given: a captured request that has no blank line after its headers (empty, not HTTP at all, or cut off part way)
		using var folder = new TempFolder();
		var capture = new FileSessionCapture(folder.Path);
		var exchange = capture.BeginExchange();
		exchange.WriteRequestHead("GET", "/", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		exchange.Complete(new ExchangeRecord { Timestamp = Noon, Mode = "rejected" });
		File.WriteAllBytes(Path.Combine(capture.RunDirectory, "1_c.txt"), Encoding.Latin1.GetBytes(captured));
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(capture.RunDirectory, saz);

		// Then: the archive's request ends its headers (Fiddler rejects the session otherwise): a cut-off request gets its blank line,
		// and anything that is not HTTP is a POST carrying the bytes as its body
		using var zip = Open(saz);
		Encoding.Latin1.GetString(ReadBytes(zip, "raw/1_c.txt")).ShouldBe(expected);
	}

	[Fact]
	public void Export_ExchangeWithControlCharactersInItsText_StillWritesItsMetadata() {

		// Given: an exchange recorded with a "method" made of binary bytes (the XML metadata file cannot hold control characters)
		using var folder = new TempFolder();
		var capture = new FileSessionCapture(folder.Path);
		var exchange = capture.BeginExchange();
		exchange.WriteRequestHead("GET", "/", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		exchange.Complete(new ExchangeRecord { Timestamp = Noon, Method = ")\u0002\u0097\u0001\u0014chrome", Mode = "rejected" });

		// When: its metadata is read back
		var meta = System.Xml.Linq.XDocument.Load(Path.Combine(capture.RunDirectory, "1_m.xml")).Root!;

		// Then: the file exists and holds the method with the unwritable characters replaced
		meta.Element("Mode")!.Value.ShouldBe("rejected");
		meta.Element("Method")!.Value.ShouldContain("chrome");
		meta.Element("Method")!.Value.ShouldContain("�");
	}

	[Theory]
	[InlineData(9, "raw/1_c.txt", "raw/9_c.txt")]
	[InlineData(10, "raw/01_c.txt", "raw/10_c.txt")]
	[InlineData(100, "raw/001_c.txt", "raw/100_c.txt")]
	[InlineData(1000, "raw/0001_c.txt", "raw/1000_c.txt")]
	public void Export_FileNames_ArePaddedToTheWidthOfTheHighestSessionNumber(int sessions, string firstName, string lastName) {

		// Given: a capture run folder with the given number of sessions
		using var folder = new TempFolder();
		var capture = new FileSessionCapture(folder.Path);
		for (int i = 0; i < sessions; i++) {
			var exchange = capture.BeginExchange();
			exchange.WriteRequestHead("GET", "/", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
			exchange.Complete(new ExchangeRecord { Timestamp = Noon, Method = "GET", Url = "http://a.test/", Scheme = "http" });
		}
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(capture.RunDirectory, saz);

		// Then: the numbers in the names have as many digits as the highest number (Fiddler writes 1_c.txt for 6 sessions, 001_c.txt for 375, 0001_c.txt for 1100)
		using var zip = Open(saz);
		zip.GetEntry(firstName).ShouldNotBeNull(firstName);
		zip.GetEntry(lastName).ShouldNotBeNull(lastName);
	}

	[Fact]
	public void Export_SessionWithNoResponseFile_GetsAnEmptyOne() {

		// Given: a capture run folder whose exchange has no response file (the client gave up before an answer)
		using var folder = new TempFolder();
		var capture = new FileSessionCapture(folder.Path);
		var exchange = capture.BeginExchange();
		exchange.WriteRequestHead("GET", "/hang", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		exchange.Complete(new ExchangeRecord { Timestamp = Noon, Method = "GET", Url = "http://a.test/hang", Scheme = "http", Mode = "plain" });
		File.Delete(Path.Combine(capture.RunDirectory, "1_s.txt"));
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(capture.RunDirectory, saz);

		// Then: the archive has an empty response file for the session, as Fiddler writes
		using var zip = Open(saz);
		ReadBytes(zip, "raw/1_s.txt").ShouldBeEmpty();
	}

	[Fact]
	public void Export_PlainHttpAndWebSocket_HaveFiddlersFlags() {

		// Given: a plain HTTP request that reused neither connection, and a WebSocket session
		using var folder = new TempFolder();
		var capture = new FileSessionCapture(folder.Path);
		var plain = capture.BeginExchange();
		plain.WriteRequestHead("GET", "/", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		plain.Complete(new ExchangeRecord { Timestamp = Noon, Method = "GET", Url = "http://a.test/", Scheme = "http", Mode = "plain" });
		var socket = capture.BeginExchange();
		socket.WriteRequestHead("GET", "/ws", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		socket.Complete(new ExchangeRecord { Timestamp = Noon, Method = "GET", Url = "http://a.test/ws", Scheme = "http", Mode = "websocket-closed" });
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(capture.RunDirectory, saz);

		// Then: the plain request has BitFlags 0 and "None" as its TLS version (a request that did not arrive over TLS), and the WebSocket has IsWebSocketTunnel (0x40000)
		using var zip = Open(saz);
		var first = ReadXml(zip, "raw/1_m.xml");
		first.Attribute("BitFlags")!.Value.ShouldBe("0");
		first.Element("SessionFlags")!.Elements("SessionFlag").Single(f => f.Attribute("N")!.Value == "https-client-version").Attribute("V")!.Value.ShouldBe("None");
		ReadXml(zip, "raw/2_m.xml").Attribute("BitFlags")!.Value.ShouldBe("40000");
	}

	[Fact]
	public void Export_Archive_CarriesAZipCommentNamingMoSaProxy() {

		// Given: a capture run folder
		using var folder = new TempFolder();
		string run = BuildRunFolder(folder);
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(run, saz);

		// Then: the zip comment says which program wrote it, in the form Fiddler uses ("Name (vX.Y.Z) Session Archive.")
		using var zip = Open(saz);
		zip.Comment.ShouldMatch(@"^MoSaProxy \(v\d+\.\d+\.\d+\) Session Archive\.$");
	}

	[Fact]
	public void Export_ReusedConnections_AreMarkedInPipeInfoAndBitFlags() {

		// Given: a capture run folder whose exchange reused both the client's connection and the origin's
		using var folder = new TempFolder();
		var capture = new FileSessionCapture(folder.Path);
		var exchange = capture.BeginExchange();
		exchange.WriteRequestHead("GET", "/", "HTTP/1.1", Array.Empty<KeyValuePair<string, string>>());
		exchange.Complete(new ExchangeRecord { Timestamp = Noon, Method = "GET", Url = "http://a.test/", Scheme = "http", ClientConnectionReused = true, OriginConnectionReused = true });
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(capture.RunDirectory, saz);

		// Then: PipeInfo says both were reused, and BitFlags has ClientPipeReused (0x8) and ServerPipeReused (0x10)
		using var zip = Open(saz);
		var session = ReadXml(zip, "raw/1_m.xml");
		session.Element("PipeInfo")!.Attribute("CltReuse")!.Value.ShouldBe("true");
		session.Element("PipeInfo")!.Attribute("Reused")!.Value.ShouldBe("true");
		session.Attribute("BitFlags")!.Value.ShouldBe("18");
	}

	[Fact]
	public async Task Export_TwoRequestsOnOneConnection_SecondIsMarkedAsReused() {

		// Given: a proxy capturing two requests that its client sends one after the other on a kept-alive connection
		using var captureFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("from the origin"));
		string run;
		await using (var proxy = await TestProxy.StartAsync(new ProxyOptions { Port = 0, CaptureDirectory = captureFolder.Path })) {
			await proxy.Client.GetAsync(origin.UrlFor("/one"));
			await proxy.Client.GetAsync(origin.UrlFor("/two"));
			await TempFolder.WaitForFileAsync(Path.Combine(proxy.CaptureRunDirectory!, "2_m.xml"));
			run = proxy.CaptureRunDirectory!;
		}
		string saz = Path.Combine(captureFolder.Path, "real.saz");

		// When: the run folder is exported
		SazExporter.Export(run, saz);

		// Then: the first session started fresh connections, and the second reused both
		using var zip = Open(saz);
		ReadXml(zip, "raw/1_m.xml").Element("PipeInfo")!.Attributes().ShouldBeEmpty();
		var second = ReadXml(zip, "raw/2_m.xml").Element("PipeInfo")!;
		second.Attribute("CltReuse")!.Value.ShouldBe("true");
		second.Attribute("Reused")!.Value.ShouldBe("true");
	}

	[Fact]
	public async Task Export_RealProxyCapture_ProducesAnArchiveWithTheRequestAndResponse() {

		// Given: a proxy capturing into a new folder, and one request that went through it
		using var captureFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("from the origin"));
		string run;
		await using (var proxy = await TestProxy.StartAsync(new ProxyOptions { Port = 0, CaptureDirectory = captureFolder.Path })) {
			await proxy.Client.GetAsync(origin.UrlFor("/page"));
			await TempFolder.WaitForFileAsync(Path.Combine(proxy.CaptureRunDirectory!, "1_m.xml"));
			run = proxy.CaptureRunDirectory!;
		}
		string saz = Path.Combine(captureFolder.Path, "real.saz");

		// When: the run folder is exported
		int sessions = SazExporter.Export(run, saz);

		// Then: the archive holds that one session, with the request line and the origin's response (chunked, as it was sent) inside it
		sessions.ShouldBe(1);
		using var zip = Open(saz);
		Encoding.Latin1.GetString(ReadBytes(zip, "raw/1_c.txt")).ShouldStartWith($"GET {origin.UrlFor("/page")} HTTP/1.1\r\n");
		Encoding.Latin1.GetString(ReadBytes(zip, "raw/1_s.txt")).ShouldEndWith("\r\n\r\nf\r\nfrom the origin\r\n0\r\n\r\n");
		ReadXml(zip, "raw/1_m.xml").Element("SessionFlags")!.Elements("SessionFlag")
			.Single(f => f.Attribute("N")!.Value == "x-mosaproxy-mode").Attribute("V")!.Value.ShouldBe("plain");
	}
}
