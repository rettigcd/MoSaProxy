using System.Text;
using System.Xml.Linq;
using Shouldly;

namespace MoSaProxy.Tests;

public class FileExchangeCapture_Tests {

	static readonly KeyValuePair<string, string>[] Headers = [KeyValuePair.Create("Host", "a.test")];

	static ExchangeRecord Record() => new() { Method = "POST", Url = "http://a.test/x", Status = 200 };

	[Fact]
	public async Task ExpectedWireBytes_NotWrittenWhileTheExchangeRuns_ThenReplacedAtTheEnd() {

		// Given: a capture told that the exact request bytes will be supplied, and a request whose head and body have been handled
		using var folder = new TempFolder();
		var session = new FileSessionCapture(folder.Path, null, null);
		var capture = session.BeginExchange();
		capture.ExpectWireBytes(request: true);
		capture.WriteRequestHead("POST", "/x", "HTTP/1.1", Headers);
		await using (var body = capture.TeeRequestBody(new MemoryStream("abcde"u8.ToArray()))) await body.CopyToAsync(Stream.Null);
		string file = Path.Combine(session.RunDirectory, "1_c.txt");

		// When / Then (before the end): nothing has been written to the request file yet, because it would only be overwritten
		new FileInfo(file).Length.ShouldBe(0);

		// When: the exact bytes arrive and the exchange completes
		var exact = "POST /x HTTP/1.1\r\nHOST:a.test\r\nContent-Length: 5\r\n\r\nabcde"u8.ToArray();
		capture.SetWireBytes(exact, null);
		capture.Complete(Record());

		// Then: the file holds exactly those bytes, and the metadata says so
		File.ReadAllBytes(file).ShouldBe(exact);
		XDocument.Load(Path.Combine(session.RunDirectory, "1_m.xml")).Root!.Element("RequestSource")!.Value.ShouldBe(CaptureSource.Wire);
	}

	[Fact]
	public async Task ExpectedWireBytes_NeverArrive_FileKeepsTheHeadAndTheMetadataSaysTheBodyIsMissing() {

		// Given: a capture told that the exact request bytes will be supplied, and a request with a 5-byte body that has been read
		using var folder = new TempFolder();
		var session = new FileSessionCapture(folder.Path, null, null);
		var capture = session.BeginExchange();
		capture.ExpectWireBytes(request: true);
		capture.WriteRequestHead("POST", "/x", "HTTP/1.1", Headers);
		await using (var body = capture.TeeRequestBody(new MemoryStream("abcde"u8.ToArray()))) await body.CopyToAsync(Stream.Null);

		// When: the exchange completes without the exact bytes ever being supplied
		capture.Complete(Record());

		// Then: the file has the head that was kept (no body), and the metadata reports the source as parsed and the body as not saved
		Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(session.RunDirectory, "1_c.txt"))).ShouldBe("POST /x HTTP/1.1\r\nHost: a.test\r\n\r\n");
		var meta = XDocument.Load(Path.Combine(session.RunDirectory, "1_m.xml")).Root!;
		meta.Element("RequestSource")!.Value.ShouldBe(CaptureSource.Parsed);
		meta.Element("RequestBodyTruncated")!.Value.ShouldBe("true");
	}

	[Fact]
	public async Task NothingExpected_FilesAreWrittenAsTheExchangeRuns() {

		// Given: a capture that was not told to expect wire bytes (an HTTP/2 exchange, for example)
		using var folder = new TempFolder();
		var session = new FileSessionCapture(folder.Path, null, null);
		var capture = session.BeginExchange();

		// When: a request head and body are handled
		capture.WriteRequestHead("POST", "/x", "HTTP/2", Headers);
		await using (var body = capture.TeeRequestBody(new MemoryStream("abcde"u8.ToArray()))) await body.CopyToAsync(Stream.Null);
		capture.Complete(Record());

		// Then: the file holds the head and the body that were written along the way
		Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(session.RunDirectory, "1_c.txt"))).ShouldBe("POST /x HTTP/2\r\nHost: a.test\r\n\r\nabcde");
	}
}
