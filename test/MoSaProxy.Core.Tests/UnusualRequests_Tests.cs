using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using Shouldly;

namespace MoSaProxy.Tests;

/// <summary>Requests that do not follow the usual path through the proxy are still logged and captured as the client sent them.</summary>
public class UnusualRequests_Tests {

	static byte[] B(string text) => Encoding.Latin1.GetBytes(text);

	static ProxyOptions Capturing(TempFolder folder) => new() {
		Port = 0, CaptureDirectory = folder.Path,
		Intercept = true, CaDirectory = Path.Combine(folder.Path, "ca"), OriginCertificateValidation = (_, _, _, _) => true,
	};

	static async Task<byte[]> CapturedAsync(TestProxy proxy, int number, string suffix) {
		await TempFolder.WaitForFileAsync(Path.Combine(proxy.CaptureRunDirectory!, $"{number}_m.xml"));
		await Task.Delay(50);
		return File.ReadAllBytes(Path.Combine(proxy.CaptureRunDirectory!, $"{number}_{suffix}.txt"));
	}

	static async Task<XElement> MetadataAsync(TestProxy proxy, int number) {
		string path = Path.Combine(proxy.CaptureRunDirectory!, $"{number}_m.xml");
		await TempFolder.WaitForFileAsync(path);
		for (int attempt = 0; ; attempt++) {
			try { return XDocument.Load(path).Root!; }
			catch (IOException) when (attempt < 50) { await Task.Delay(20); }
		}
	}

	[Fact]
	public async Task Connect_TheCapturedRequestIsExactlyWhatTheClientSent() {

		// Given: an intercepting, capturing proxy and a CONNECT request with unusual casing and spacing
		using var folder = new TempFolder();
		await using var origin = RawOrigin.Start(_ => B("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"), tls: true);
		await using var proxy = await TestProxy.StartAsync(Capturing(folder));
		string connect = $"CONNECT 127.0.0.1:{origin.Port} HTTP/1.1\r\nhost:   127.0.0.1:{origin.Port}\r\nUser-AGENT: raw\r\nProxy-Connection:keep-alive\r\n\r\n";

		// When: the client sends it and reads the proxy's answer
		using var tcp = new TcpClient();
		await tcp.ConnectAsync(IPAddress.Loopback, proxy.Port);
		var stream = tcp.GetStream();
		await stream.WriteAsync(B(connect));
		var reply = new byte[1024];
		Encoding.Latin1.GetString(reply, 0, await stream.ReadAsync(reply)).ShouldStartWith("HTTP/1.1 200");

		// Then: the _c file for the CONNECT exchange is those exact bytes, and the _s file is the proxy's own reply
		(await CapturedAsync(proxy, 1, "c")).ShouldBe(B(connect));
		(await CapturedAsync(proxy, 1, "s")).ShouldBe(B("HTTP/1.1 200 Connection Established\r\n\r\n"));
		(await MetadataAsync(proxy, 1)).Element("RequestSource")!.Value.ShouldBe(CaptureSource.Wire);
	}

	[Fact]
	public async Task PlainHttp_RequestKestrelRejects_IsStillLoggedAndCapturedWithTheAnswer() {

		// Given: a capturing proxy and a request with an invalid header name, which Kestrel refuses before the proxy's handler runs
		using var folder = new TempFolder();
		await using var proxy = await TestProxy.StartAsync(Capturing(folder));
		string bad = "GET http://127.0.0.1:9/fixed HTTP/1.1\r\nHost: x\r\nBad Header: x\r\n\r\n";

		// When: the client sends it, reads the answer, and closes the connection
		using (var tcp = new TcpClient()) {
			await tcp.ConnectAsync(IPAddress.Loopback, proxy.Port);
			var stream = tcp.GetStream();
			await stream.WriteAsync(B(bad));
			var reply = new byte[2048];
			Encoding.Latin1.GetString(reply, 0, await stream.ReadAsync(reply)).ShouldStartWith("HTTP/1.1 400");
		}

		// Then: one exchange was logged as rejected with status 400, and its files hold the exact request and the 400 that was sent back
		var record = (await proxy.Log.WaitForAsync()).Single();
		record.Mode.ShouldBe("rejected");
		record.Status.ShouldBe(400);
		record.Method.ShouldBe("GET");
		(await CapturedAsync(proxy, 1, "c")).ShouldBe(B(bad));
		Encoding.Latin1.GetString(await CapturedAsync(proxy, 1, "s")).ShouldStartWith("HTTP/1.1 400");
		var meta = await MetadataAsync(proxy, 1);
		meta.Element("RequestSource")!.Value.ShouldBe(CaptureSource.Wire);
		meta.Element("ResponseSource")!.Value.ShouldBe(CaptureSource.Wire);
	}

	[Fact]
	public async Task PlainHttp_ClientHangsUpHalfWayThroughARequest_TheIncompleteRequestIsRecorded() {

		// Given: a capturing proxy and a request head that stops before its blank line
		using var folder = new TempFolder();
		await using var proxy = await TestProxy.StartAsync(Capturing(folder));
		string partial = "GET http://127.0.0.1:9/x HTTP/1.1\r\nHost: 127.0.0.1:9\r\n";

		// When: the client sends that much and closes the connection
		using (var tcp = new TcpClient()) {
			await tcp.ConnectAsync(IPAddress.Loopback, proxy.Port);
			await tcp.GetStream().WriteAsync(B(partial));
			await Task.Delay(1500);   // long enough, even when the machine is busy, for the proxy to have read it (a connection closed at once is dropped before any HTTP handling starts)
		}

		// Then: the bytes are recorded as one rejected exchange (Kestrel may answer 400 to the half request before it closes, or may not)
		var record = (await proxy.Log.WaitForAsync()).Single();
		record.Mode.ShouldBe("rejected");
		(record.Status is null or 400).ShouldBeTrue($"status was {record.Status}");
		record.Error.ShouldNotBeNull();
		(await CapturedAsync(proxy, 1, "c")).ShouldBe(B(partial));
	}

	[Fact]
	public async Task PlainHttp_ClientSendsBytesThatAreNotHttp_AreRecordedWithMetadataAndExportAsALoadableRequest() {

		// Given: a capturing proxy and a client that sends binary data (another protocol, as some browsers do through a tunnel)
		using var folder = new TempFolder();
		await using var proxy = await TestProxy.StartAsync(Capturing(folder));
		var binary = new byte[] { 0x29, 0x02, 0x97, 0x01, 0x0A, 0x14, (byte)'c', (byte)'h', (byte)'r', (byte)'o', (byte)'m', (byte)'e', 0x12, 0x0F, 0x00, 0x1A, 0x13 };

		// When: the client sends it, reads the answer, and closes the connection
		using (var tcp = new TcpClient()) {
			await tcp.ConnectAsync(IPAddress.Loopback, proxy.Port);
			var stream = tcp.GetStream();
			await stream.WriteAsync(binary);
			var reply = new byte[2048];
			await stream.ReadAsync(reply);
		}

		// Then: it is recorded as a rejected exchange whose metadata file was written (the binary bytes did not become the method)
		var record = (await proxy.Log.WaitForAsync()).Single();
		record.Mode.ShouldBe("rejected");
		var meta = await MetadataAsync(proxy, 1);
		meta.Element("Mode")!.Value.ShouldBe("rejected");
		meta.Element("Method")!.Value.ShouldBe("");

		// and the exported request has an end of headers, so Fiddler can load it, and still holds the bytes the client sent
		string saz = Path.Combine(folder.Path, "binary.saz");
		SazExporter.Export(proxy.CaptureRunDirectory!, saz);
		using var zip = System.IO.Compression.ZipFile.OpenRead(saz);
		using var entry = zip.GetEntry("raw/001_c.txt")!.Open();
		using var copy = new MemoryStream();
		entry.CopyTo(copy);
		var exported = copy.ToArray();
		Encoding.Latin1.GetString(exported).ShouldContain("\r\n\r\n");
		exported.AsSpan(exported.Length - binary.Length).SequenceEqual(binary).ShouldBeTrue();
	}

	[Fact]
	public async Task PlainHttp_StrayLineBreakAfterARequest_IsNotAnExchange() {

		// Given: a raw origin and a capturing proxy
		using var folder = new TempFolder();
		await using var origin = RawOrigin.Start(_ => B("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok"));
		await using var proxy = await TestProxy.StartAsync(Capturing(folder));
		string host = $"127.0.0.1:{origin.Port}";

		// When: a client makes one request, then sends a bare line break and closes the connection
		using (var tcp = new TcpClient()) {
			await tcp.ConnectAsync(IPAddress.Loopback, proxy.Port);
			var stream = tcp.GetStream();
			await stream.WriteAsync(B($"GET http://{host}/x HTTP/1.1\r\nHost: {host}\r\n\r\n"));
			await stream.ReadAsync(new byte[2048]);
			await stream.WriteAsync(B("\r\n"));
		}
		await proxy.Log.WaitForAsync();
		await Task.Delay(300);

		// Then: only the one real request was logged
		proxy.Log.Records.Count.ShouldBe(1);
	}

	[Fact]
	public async Task PlainHttp_ClientStopsSendingRightAfterItsRequest_IsLoggedAsDisconnected() {

		// Given: a raw origin and a proxy, and a client that finishes sending (half-closes) right after its request, as HTTP/1.0-style tools do
		await using var origin = RawOrigin.Start(_ => B("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok"));
		await using var proxy = await TestProxy.StartAsync();
		string host = $"127.0.0.1:{origin.Port}";
		using var client = await RawClient.ConnectAsync(proxy.Port);

		// When: it sends one request and at once says it will send no more
		await client.SendAsync($"GET http://{host}/x HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\n\r\n");
		client.FinishSending();

		// Then: the request is not lost without a trace: it is logged, as abandoned by the client
		// (Kestrel treats a client that stops sending as one that has gone away, so the proxy cannot answer it)
		var record = (await proxy.Log.WaitForAsync()).Single();
		record.Method.ShouldBe("GET");
		record.Error.ShouldBe("client disconnected");
	}
}
