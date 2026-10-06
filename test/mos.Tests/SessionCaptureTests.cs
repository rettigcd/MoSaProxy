using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace mos.Tests;

public class SessionCaptureTests {

	static ProxyOptions CaptureTo(TempFolder folder, long? maxBody = null) =>
		new() { Port = 0, CaptureDirectory = folder.Path, CaptureMaxBodyBytes = maxBody };

	/// <summary>Reads a capture file once the exchange's metadata file (written last) exists.</summary>
	static async Task<byte[]> ReadCapturedAsync(TestProxy proxy, string name) {
		await TempFolder.WaitForFileAsync(Path.Combine(proxy.CaptureRunDirectory!, "1_m.xml"));
		return File.ReadAllBytes(Path.Combine(proxy.CaptureRunDirectory!, name));
	}

	static string Text(byte[] bytes) => Encoding.Latin1.GetString(bytes);

	[Fact]
	public async Task Get_WithCaptureEnabled_WritesRequestResponseAndMetadataFiles() {

		// Given: an origin that answers "hello" with a custom header, and a proxy capturing into a new folder
		using var folder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(async c => { c.Response.Headers["X-Origin"] = "yes"; await c.Response.WriteAsync("hello"); });
		await using var proxy = await TestProxy.StartAsync(CaptureTo(folder));

		// When: a client requests a page on the origin through the proxy
		await proxy.Client.GetAsync(origin.UrlFor("/page?a=1"));

		// Then: the run folder holds 1_c.txt with the request line and headers, 1_s.txt with the status line, headers and body, and 1_m.xml
		var request = Text(await ReadCapturedAsync(proxy, "1_c.txt"));
		request.ShouldStartWith($"GET {origin.UrlFor("/page?a=1")} HTTP/1.1\r\n");
		request.ShouldContain($"Host: 127.0.0.1:{origin.Port}\r\n");
		request.ShouldEndWith("\r\n\r\n");

		var response = Text(await ReadCapturedAsync(proxy, "1_s.txt"));
		response.ShouldStartWith("HTTP/1.1 200 OK\r\n");
		response.ShouldContain("X-Origin: yes\r\n");
		response.ShouldEndWith("\r\n\r\nhello");

		var meta = XDocument.Load(Path.Combine(proxy.CaptureRunDirectory!, "1_m.xml")).Root!;
		meta.Attribute("Number")!.Value.ShouldBe("1");
		meta.Element("Url")!.Value.ShouldBe(origin.UrlFor("/page?a=1"));
		meta.Element("Status")!.Value.ShouldBe("200");
	}

	[Fact]
	public async Task Post_WithBody_CapturesTheBodyAfterTheRequestHeaders() {

		// Given: an origin that accepts requests and a proxy capturing into a new folder
		using var folder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => Task.CompletedTask);
		await using var proxy = await TestProxy.StartAsync(CaptureTo(folder));

		// When: a client posts the text "name=value" through the proxy
		await proxy.Client.PostAsync(origin.UrlFor("/form"), new StringContent("name=value", Encoding.UTF8, "application/x-www-form-urlencoded"));

		// Then: the captured request starts with the POST line and ends with exactly that body
		var request = Text(await ReadCapturedAsync(proxy, "1_c.txt"));
		request.ShouldStartWith("POST ");
		request.ShouldEndWith("\r\n\r\nname=value");
	}

	[Fact]
	public async Task Get_BinaryResponse_IsCapturedByteForByte() {

		// Given: an origin that returns 100,000 random bytes (like an image) and a proxy capturing into a new folder
		using var folder = new TempFolder();
		var data = new byte[100_000];
		new Random(7).NextBytes(data);
		await using var origin = await TestOrigin.StartAsync(c => { c.Response.ContentType = "image/png"; return c.Response.Body.WriteAsync(data).AsTask(); });
		await using var proxy = await TestProxy.StartAsync(CaptureTo(folder));

		// When: a client downloads it through the proxy
		await proxy.Client.GetByteArrayAsync(origin.UrlFor("/pic.png"));

		// Then: the bytes after the response headers in 1_s.txt are identical to what the origin sent
		var file = await ReadCapturedAsync(proxy, "1_s.txt");
		int bodyStart = Text(file).IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
		file[bodyStart..].ShouldBe(data);
	}

	[Fact]
	public async Task Get_ThreeRequests_AreNumberedInOrderStartingAtOne() {

		// Given: an origin and a proxy capturing into a new folder
		using var folder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => Task.CompletedTask);
		await using var proxy = await TestProxy.StartAsync(CaptureTo(folder));

		// When: a client makes three requests, one after another
		await proxy.Client.GetAsync(origin.UrlFor("/first"));
		await proxy.Client.GetAsync(origin.UrlFor("/second"));
		await proxy.Client.GetAsync(origin.UrlFor("/third"));

		// Then: exchanges 1, 2 and 3 exist, and each one's request file names its own path
		await TempFolder.WaitForFileAsync(Path.Combine(proxy.CaptureRunDirectory!, "3_m.xml"));
		Text(File.ReadAllBytes(Path.Combine(proxy.CaptureRunDirectory!, "1_c.txt"))).ShouldContain("/first");
		Text(File.ReadAllBytes(Path.Combine(proxy.CaptureRunDirectory!, "2_c.txt"))).ShouldContain("/second");
		Text(File.ReadAllBytes(Path.Combine(proxy.CaptureRunDirectory!, "3_c.txt"))).ShouldContain("/third");
	}

	[Fact]
	public void Begin_TwoRunsInTheSameFolder_UseSeparateSubfoldersThatBothStartAtOne() {

		// Given: a capture base folder
		using var folder = new TempFolder();

		// When: two separate runs each capture one exchange
		var firstRun = new FileSessionCapture(folder.Path);
		var secondRun = new FileSessionCapture(folder.Path);
		var first = firstRun.BeginExchange();
		var second = secondRun.BeginExchange();
		first.Complete(new ExchangeRecord());
		second.Complete(new ExchangeRecord());

		// Then: the runs use different sub-folders and each one's first exchange is numbered 1
		firstRun.RunDirectory.ShouldNotBe(secondRun.RunDirectory);
		first.Number.ShouldBe(1);
		second.Number.ShouldBe(1);
		File.Exists(Path.Combine(firstRun.RunDirectory, "1_m.xml")).ShouldBeTrue();
		File.Exists(Path.Combine(secondRun.RunDirectory, "1_m.xml")).ShouldBeTrue();
	}

	[Fact]
	public async Task Connect_Tunnel_IsCapturedAsTheRequestAndTheEstablishedResponse() {

		// Given: an echoing origin and a proxy capturing into a new folder
		using var folder = new TempFolder();
		await using var origin = TestTcpOrigin.Echo();
		await using var proxy = await TestProxy.StartAsync(CaptureTo(folder));
		using var client = await RawClient.ConnectAsync(proxy.Port);

		// When: a client opens a tunnel, sends some data through it, and closes
		await client.SendAsync($"CONNECT 127.0.0.1:{origin.Port} HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\n\r\n");
		await client.ReadUntilAsync("\r\n\r\n");
		await client.SendAsync("secret bytes");
		await client.ReadExactlyAsync(12);
		client.Dispose();

		// Then: the capture holds the CONNECT request and the 200 response, but not the tunneled bytes, and the metadata says it was a tunnel
		var request = Text(await ReadCapturedAsync(proxy, "1_c.txt"));
		request.ShouldStartWith($"CONNECT 127.0.0.1:{origin.Port} HTTP/1.1\r\n");
		request.ShouldNotContain("secret bytes");
		var response = Text(await ReadCapturedAsync(proxy, "1_s.txt"));
		response.ShouldBe("HTTP/1.1 200 Connection Established\r\n\r\n");
		XDocument.Load(Path.Combine(proxy.CaptureRunDirectory!, "1_m.xml")).Root!.Element("Mode")!.Value.ShouldBe("tunnel");
	}

	[Fact]
	public async Task Get_BodyLargerThanTheCap_IsTruncatedInTheFileButFullyDeliveredToTheClient() {

		// Given: an origin returning 100 bytes and a proxy that captures at most 10 bytes of any body
		using var folder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => c.Response.Body.WriteAsync(new byte[100]).AsTask());
		await using var proxy = await TestProxy.StartAsync(CaptureTo(folder, maxBody: 10));

		// When: a client downloads it through the proxy
		var received = await proxy.Client.GetByteArrayAsync(origin.UrlFor("/"));

		// Then: the client gets all 100 bytes, the capture file holds only 10 body bytes, and the metadata says it was truncated
		received.Length.ShouldBe(100);
		var file = await ReadCapturedAsync(proxy, "1_s.txt");
		(file.Length - (Text(file).IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)).ShouldBe(10);
		XDocument.Load(Path.Combine(proxy.CaptureRunDirectory!, "1_m.xml")).Root!.Element("ResponseBodyTruncated")!.Value.ShouldBe("true");
	}

	[Fact]
	public async Task Get_CaptureCannotStart_StillForwardsTheRequest() {

		// Given: an origin that answers "hello" and a proxy whose capture throws every time an exchange begins
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"));
		await using var proxy = await TestProxy.StartAsync(capture: new ThrowingCapture());

		// When: a client requests a page through the proxy
		var response = await proxy.Client.GetAsync(origin.UrlFor("/"));

		// Then: the client still gets the origin's 200 and body
		response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
		(await response.Content.ReadAsStringAsync()).ShouldBe("hello");
	}

	[Fact]
	public async Task Read_CaptureFileCannotBeWritten_StopsCopyingButKeepsReading() {

		// Given: a tee stream over a 5-byte source whose capture destination fails on every write
		var tee = new TeeReadStream(new MemoryStream(new byte[5]), new FailingWriteStream(), maxBytes: null);

		// When: all of the source is read through the tee
		var buffer = new byte[5];
		int read = await tee.ReadAsync(buffer);

		// Then: the read still returns the 5 bytes and does not throw
		read.ShouldBe(5);
	}

	sealed class ThrowingCapture : ISessionCapture {
		public IExchangeCapture BeginExchange() => throw new IOException("disk full");
	}

	sealed class FailingWriteStream : Stream {
		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new IOException("disk full");
		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("disk full");
	}
}
