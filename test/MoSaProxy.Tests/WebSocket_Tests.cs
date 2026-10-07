using System.Net;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace MoSaProxy.Tests;

public class WebSocket_Tests {

	static ProxyOptions Intercepting(TempFolder caFolder, string? captureDirectory = null) => new() {
		Port = 0,
		Intercept = true,
		CaDirectory = caFolder.Path,
		CaptureDirectory = captureDirectory,
		OriginCertificateValidation = (_, _, _, _) => true,
	};

	/// <summary>A WebSocket server that echoes every message, remembers the close frames it gets, and can close first.</summary>
	sealed class EchoServer(string? agreedSubProtocol = null, bool closeFirst = false) {
		readonly List<string> _events = new();
		public IReadOnlyList<string> Events { get { lock (_events) return _events.ToArray(); } }

		public async Task Handle(HttpContext context) {
			var requested = context.WebSockets.WebSocketRequestedProtocols;
			string? chosen = agreedSubProtocol is not null && requested.Contains(agreedSubProtocol) ? agreedSubProtocol : null;
			using var socket = await context.WebSockets.AcceptWebSocketAsync(chosen);

			if (closeFirst) {
				await socket.SendAsync("bye"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
				await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "server done", CancellationToken.None);
				return;
			}

			var buffer = new byte[64 * 1024];
			while (true) {
				var received = await socket.ReceiveAsync(buffer, context.RequestAborted);
				if (received.MessageType == WebSocketMessageType.Close) {
					lock (_events) _events.Add($"close {(int?)socket.CloseStatus} {socket.CloseStatusDescription}");
					await socket.CloseAsync(socket.CloseStatus ?? WebSocketCloseStatus.NormalClosure, socket.CloseStatusDescription, CancellationToken.None);
					return;
				}
				await socket.SendAsync(buffer.AsMemory(0, received.Count), received.MessageType, received.EndOfMessage, context.RequestAborted);
			}
		}
	}

	static async Task SendTextAsync(ClientWebSocket socket, string text) =>
		await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

	/// <summary>Reads one whole message (it may arrive in several pieces).</summary>
	static async Task<(WebSocketMessageType Type, byte[] Bytes)> ReceiveMessageAsync(WebSocket socket) {
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
		var buffer = new byte[16 * 1024];
		using var message = new MemoryStream();
		while (true) {
			var received = await socket.ReceiveAsync(buffer, cts.Token);
			if (received.MessageType == WebSocketMessageType.Close) return (WebSocketMessageType.Close, Array.Empty<byte>());
			message.Write(buffer, 0, received.Count);
			if (received.EndOfMessage) return (received.MessageType, message.ToArray());
		}
	}

	[Fact]
	public async Task Wss_ThroughInterceptingProxy_EchoesTextAndBinaryMessages() {

		// Given: an HTTPS origin with a WebSocket echo endpoint, and an intercepting proxy
		using var caFolder = new TempFolder();
		var server = new EchoServer();
		await using var origin = await TestOrigin.StartAsync(server.Handle, https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));
		using var socket = proxy.CreateWebSocket(o => { o.SetRequestHeader("Origin", "https://app.test"); o.SetRequestHeader("X-Custom", "kept"); });

		// When: the client opens a wss:// connection through the proxy, sends a text message and a binary message
		await socket.ConnectAsync(new Uri(origin.UrlFor("/chat").Replace("https://", "wss://")), CancellationToken.None);
		await SendTextAsync(socket, "hello");
		var text = await ReceiveMessageAsync(socket);
		await socket.SendAsync(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, true, CancellationToken.None);
		var binary = await ReceiveMessageAsync(socket);

		// Then: both come back unchanged, and the origin saw a WebSocket upgrade for that path carrying the client's own headers
		text.Type.ShouldBe(WebSocketMessageType.Text);
		Encoding.UTF8.GetString(text.Bytes).ShouldBe("hello");
		binary.Type.ShouldBe(WebSocketMessageType.Binary);
		binary.Bytes.ShouldBe(new byte[] { 1, 2, 3 });
		var seen = origin.Seen.Single();
		seen.PathAndQuery.ShouldBe("/chat");
		seen.Headers["Origin"].ToString().ShouldBe("https://app.test");
		seen.Headers["X-Custom"].ToString().ShouldBe("kept");
		seen.Headers.Upgrade.ToString().ShouldBe("websocket");
	}

	[Fact]
	public async Task Wss_ClientOffersSubProtocols_TheOriginsChoiceIsPassedBack() {

		// Given: an origin that agrees to the "chat" subprotocol, and an intercepting proxy
		using var caFolder = new TempFolder();
		var server = new EchoServer(agreedSubProtocol: "chat");
		await using var origin = await TestOrigin.StartAsync(server.Handle, https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));
		using var socket = proxy.CreateWebSocket(o => { o.AddSubProtocol("superchat"); o.AddSubProtocol("chat"); });

		// When: the client offers "superchat" and "chat" when connecting
		await socket.ConnectAsync(new Uri(origin.UrlFor("/").Replace("https://", "wss://")), CancellationToken.None);

		// Then: the client ends up with the subprotocol the origin picked
		socket.SubProtocol.ShouldBe("chat");
	}

	[Fact]
	public async Task Wss_OneMegabyteMessage_ArrivesIntact() {

		// Given: an HTTPS origin with a WebSocket echo endpoint, an intercepting proxy, and a 1 MB binary message
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(new EchoServer().Handle, https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));
		using var socket = proxy.CreateWebSocket();
		await socket.ConnectAsync(new Uri(origin.UrlFor("/").Replace("https://", "wss://")), CancellationToken.None);
		var data = new byte[1024 * 1024];
		new Random(11).NextBytes(data);

		// When: the client sends it and reads the echo
		await socket.SendAsync(data, WebSocketMessageType.Binary, true, CancellationToken.None);
		var echoed = await ReceiveMessageAsync(socket);

		// Then: the same 1 MB comes back as a single message
		echoed.Type.ShouldBe(WebSocketMessageType.Binary);
		echoed.Bytes.ShouldBe(data);
	}

	[Fact]
	public async Task Wss_ClientCloses_CloseCodeAndReasonReachTheOriginAndTheClientCompletes() {

		// Given: an HTTPS origin with a WebSocket echo endpoint, an intercepting proxy with an in-memory log, and a connection that has exchanged two messages
		using var caFolder = new TempFolder();
		var server = new EchoServer();
		await using var origin = await TestOrigin.StartAsync(server.Handle, https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));
		using var socket = proxy.CreateWebSocket();
		await socket.ConnectAsync(new Uri(origin.UrlFor("/chat").Replace("https://", "wss://")), CancellationToken.None);
		await SendTextAsync(socket, "one");
		await ReceiveMessageAsync(socket);
		await SendTextAsync(socket, "two!");
		await ReceiveMessageAsync(socket);

		// When: the client closes the connection with code 1000 and the reason "bye"
		await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

		// Then: the origin received that close code and reason, the client's close completed, and the log has the CONNECT, the established
		// handshake (status 101), and a closing record with the message and byte counts for each direction
		socket.CloseStatus.ShouldBe(WebSocketCloseStatus.NormalClosure);
		server.Events.ShouldBe(new[] { "close 1000 bye" });
		var records = await proxy.Log.WaitForAsync(3);
		records.Select(r => r.Mode).ShouldBe(new[] { "intercept", "websocket", "websocket-closed" });
		records[1].Status.ShouldBe(101);
		records[1].ClientMessages.ShouldBeNull();
		var closed = records[2];
		closed.Status.ShouldBe(101);
		closed.Url.ShouldBe(origin.UrlFor("/chat"));
		closed.ClientMessages.ShouldBe(2);
		closed.OriginMessages.ShouldBe(2);
		closed.RequestBytes.ShouldBe(7);   // "one" + "two!"
		closed.ResponseBytes.ShouldBe(7);
		closed.Error.ShouldBeNull();
	}

	[Fact]
	public async Task Wss_OriginClosesFirst_ClientSeesTheMessageAndTheClose() {

		// Given: an HTTPS origin that sends "bye" and then closes with the reason "server done", and an intercepting proxy
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(new EchoServer(closeFirst: true).Handle, https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));
		using var socket = proxy.CreateWebSocket();
		await socket.ConnectAsync(new Uri(origin.UrlFor("/").Replace("https://", "wss://")), CancellationToken.None);

		// When: the client reads the message and then waits for the next one
		var message = await ReceiveMessageAsync(socket);
		var next = await ReceiveMessageAsync(socket);
		await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "ok", CancellationToken.None);

		// Then: it got "bye" followed by the close, carrying the origin's code and reason
		Encoding.UTF8.GetString(message.Bytes).ShouldBe("bye");
		next.Type.ShouldBe(WebSocketMessageType.Close);
		socket.CloseStatus.ShouldBe(WebSocketCloseStatus.NormalClosure);
		socket.CloseStatusDescription.ShouldBe("server done");
		var records = await proxy.Log.WaitForAsync(3);
		records[2].Mode.ShouldBe("websocket-closed");
		records[2].OriginMessages.ShouldBe(1);
	}

	[Fact]
	public async Task Wss_OriginRefusesTheHandshake_ClientGetsTheOriginsStatus() {

		// Given: an HTTPS origin that answers every request with 403 (it never accepts a WebSocket), and an intercepting proxy
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(c => { c.Response.StatusCode = 403; return Task.CompletedTask; }, https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));
		using var socket = proxy.CreateWebSocket();

		// When: the client tries to open a WebSocket
		var connect = () => socket.ConnectAsync(new Uri(origin.UrlFor("/").Replace("https://", "wss://")), CancellationToken.None);

		// Then: the connection fails with the origin's 403, and the log marks it rejected with that status
		await Should.ThrowAsync<WebSocketException>(connect);
		socket.HttpStatusCode.ShouldBe(HttpStatusCode.Forbidden);
		var records = await proxy.Log.WaitForAsync(2);
		records[1].Status.ShouldBe(403);
		records[1].Mode.ShouldBe("rejected");
	}

	[Fact]
	public async Task Wss_OriginIsUnreachable_ClientGets502() {

		// Given: an intercepting proxy and a loopback port where nothing is listening
		using var caFolder = new TempFolder();
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));
		int deadPort;
		using (var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)) {
			probe.Start();
			deadPort = ((IPEndPoint)probe.LocalEndpoint).Port;
		}
		using var socket = proxy.CreateWebSocket();

		// When: the client tries to open a wss:// connection to that port
		var connect = () => socket.ConnectAsync(new Uri($"wss://127.0.0.1:{deadPort}/"), CancellationToken.None);

		// Then: it fails with 502 Bad Gateway
		await Should.ThrowAsync<WebSocketException>(connect);
		socket.HttpStatusCode.ShouldBe(HttpStatusCode.BadGateway);
	}

	[Fact]
	public async Task Ws_PlainWebSocketThroughATunnel_WorksAndIsLoggedWithAnHttpUrl() {

		// Given: a plain-HTTP origin with a WebSocket echo endpoint, and an intercepting proxy (ws:// is plain HTTP inside the CONNECT tunnel)
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(new EchoServer().Handle);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));
		using var socket = proxy.CreateWebSocket();

		// When: the client opens a ws:// connection, sends a message and closes
		await socket.ConnectAsync(new Uri(origin.UrlFor("/chat").Replace("http://", "ws://")), CancellationToken.None);
		await SendTextAsync(socket, "hi");
		var echoed = await ReceiveMessageAsync(socket);
		await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);

		// Then: the echo arrives, and the closing record names the plain http:// address of the endpoint
		Encoding.UTF8.GetString(echoed.Bytes).ShouldBe("hi");
		var closed = (await proxy.Log.WaitForAsync(3)).Last();
		closed.Mode.ShouldBe("websocket-closed");
		closed.Url.ShouldBe(origin.UrlFor("/chat"));
		closed.Scheme.ShouldBe("http");
	}

	[Fact]
	public async Task Get_AbsoluteFormWebSocketUpgradeWithoutATunnel_IsAcceptedWith101() {

		// Given: a plain-HTTP origin with a WebSocket echo endpoint, and a proxy that does not intercept anything
		await using var origin = await TestOrigin.StartAsync(new EchoServer().Handle);
		await using var proxy = await TestProxy.StartAsync();
		using var client = await RawClient.ConnectAsync(proxy.Port);

		// When: a client sends a WebSocket upgrade request straight to the proxy (full http:// URL, no CONNECT), using the sample key from RFC 6455
		await client.SendAsync($"GET {origin.UrlFor("/chat")} HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
			"Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n");
		var answer = await client.ReadUntilAsync("\r\n\r\n");

		// Then: the proxy switches protocols, and the accept value is the one RFC 6455 gives for that key
		answer.ShouldStartWith("HTTP/1.1 101");
		answer.ShouldContain("Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=");
	}

	[Fact]
	public async Task Wss_OverHttp2_ClientCanOpenAWebSocketWithExtendedConnect() {

		// Given: an HTTPS origin with a WebSocket echo endpoint, an intercepting proxy, and a client that insists on HTTP/2 (RFC 8441)
		using var caFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(new EchoServer().Handle, https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder));
		// (the proxy and certificate settings live on the HTTP client handed to ConnectAsync, so none are set on the socket's own options)
		using var socket = new ClientWebSocket();
		socket.Options.HttpVersion = HttpVersion.Version20;
		socket.Options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;

		// When: it opens the WebSocket through the proxy and sends a message
		await socket.ConnectAsync(new Uri(origin.UrlFor("/chat").Replace("https://", "wss://")), proxy.Http2Client, CancellationToken.None);
		await SendTextAsync(socket, "over h2");
		var echoed = await ReceiveMessageAsync(socket);

		// Then: the echo arrives, and the log shows the client side of the handshake was HTTP/2
		Encoding.UTF8.GetString(echoed.Bytes).ShouldBe("over h2");
		var handshake = (await proxy.Log.WaitForAsync(2)).Single(r => r.Mode == "websocket");
		handshake.ClientHttpVersion.ShouldBe("HTTP/2");
	}

	[Fact]
	public async Task Wss_WithCapture_WritesTheHandshakeAndEveryMessageToFiles() {

		// Given: an HTTPS origin with a WebSocket echo endpoint, and an intercepting proxy capturing into a new folder
		using var caFolder = new TempFolder();
		using var captureFolder = new TempFolder();
		await using var origin = await TestOrigin.StartAsync(new EchoServer().Handle, https: true);
		await using var proxy = await TestProxy.StartAsync(Intercepting(caFolder, captureFolder.Path));
		using var socket = proxy.CreateWebSocket();

		// When: the client connects, sends the text "hello" and the bytes 0x01 0x02 0x03, and closes
		await socket.ConnectAsync(new Uri(origin.UrlFor("/chat").Replace("https://", "wss://")), CancellationToken.None);
		await SendTextAsync(socket, "hello");
		await ReceiveMessageAsync(socket);
		await socket.SendAsync(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, true, CancellationToken.None);
		await ReceiveMessageAsync(socket);
		await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

		// Then: exchange 2 (exchange 1 is the CONNECT) has the upgrade request, the 101 response, and a messages file listing both
		// directions with the text readable and the binary message as hex
		await TempFolder.WaitForFileAsync(Path.Combine(proxy.CaptureRunDirectory!, "2_m.xml"));
		string Read(string name) => Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(proxy.CaptureRunDirectory!, name)));
		Read("2_c.txt").ShouldStartWith("GET /chat HTTP/1.1\r\n");
		Read("2_c.txt").ToLowerInvariant().ShouldContain("upgrade: websocket");
		Read("2_s.txt").ShouldStartWith("HTTP/1.1 101 Switching Protocols\r\n");
		var messages = Read("2_w.txt");
		messages.ShouldContain("client -> origin text 5 bytes\r\nhello");
		messages.ShouldContain("origin -> client text 5 bytes\r\nhello");
		messages.ShouldContain("client -> origin binary 3 bytes\r\n  0000: 010203");
		messages.ShouldContain("client -> origin close 3 bytes status 1000\r\nbye");
	}
}
