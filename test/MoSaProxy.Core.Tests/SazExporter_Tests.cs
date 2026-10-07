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
			"[Content_Types].xml", "_index.htm", "raw/1_c.txt", "raw/1_m.xml", "raw/1_s.txt", "raw/2_c.txt", "raw/2_m.xml", "raw/2_s.txt", "raw/2_w.txt",
		}.Order());
		var types = ReadXml(zip, "[Content_Types].xml");
		types.Elements().Select(e => e.Attribute("Extension")!.Value).Order().ShouldBe(new[] { "htm", "txt", "xml" });
		Encoding.UTF8.GetString(ReadBytes(zip, "_index.htm")).ShouldContain("a.test");
	}

	[Fact]
	public void Export_RequestResponseAndWebSocketFiles_AreCopiedUnchanged() {

		// Given: a capture run folder
		using var folder = new TempFolder();
		string run = BuildRunFolder(folder);
		string saz = Path.Combine(folder.Path, "out.saz");

		// When: it is exported
		SazExporter.Export(run, saz);

		// Then: every request, response and WebSocket file in the archive is byte-for-byte the one in the folder
		using var zip = Open(saz);
		foreach (var name in new[] { "1_c.txt", "1_s.txt", "2_c.txt", "2_s.txt", "2_w.txt" })
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
		flags["x-clientIP"].ShouldBe("127.0.0.1");
		flags["x-mosaproxy-mode"].ShouldBe("plain");
		flags["x-mosaproxy-client-protocol"].ShouldBe("HTTP/1.1");
		flags["x-mosaproxy-origin-protocol"].ShouldBe("HTTP/1.1");
		// no byte-order mark: the file starts with the XML declaration
		ReadBytes(zip, "raw/1_m.xml")[0].ShouldBe((byte)'<');
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
		index.IndexOf("raw/9_c.txt", StringComparison.Ordinal).ShouldBeLessThan(index.IndexOf("raw/10_c.txt", StringComparison.Ordinal));
		index.IndexOf("raw/2_c.txt", StringComparison.Ordinal).ShouldBeLessThan(index.IndexOf("raw/10_c.txt", StringComparison.Ordinal));
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
