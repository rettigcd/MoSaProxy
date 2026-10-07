using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace MoSaProxy.Tests;

public class ExchangeLog_Tests {

	[Fact]
	public async Task Get_ThroughProxy_LogsOneRecordWithRequestAndResponseDetails() {

		// Given: an origin that returns 200 with a 5-byte body, and a running proxy with an in-memory log
		await using var origin = await TestOrigin.StartAsync(c => c.Response.WriteAsync("hello"));
		await using var proxy = await TestProxy.StartAsync();

		// When: a client requests a URL on the origin through the proxy
		var url = origin.UrlFor("/page?a=1");
		await (await proxy.Client.GetAsync(url)).Content.ReadAsStringAsync();

		// Then: exactly one record was logged, with the method, full URL, status, byte count and protocol versions
		var record = (await proxy.Log.WaitForAsync()).Single();
		record.Method.ShouldBe("GET");
		record.Url.ShouldBe(url);
		record.Host.ShouldBe("127.0.0.1");
		record.Port.ShouldBe(origin.Port);
		record.Status.ShouldBe(200);
		record.ResponseBytes.ShouldBe(5);
		record.ClientHttpVersion.ShouldBe("HTTP/1.1");
		record.OriginHttpVersion.ShouldBe("HTTP/1.1");
		record.Mode.ShouldBe("plain");
		record.Error.ShouldBeNull();
	}

	[Fact]
	public async Task Post_WithBody_LogsRequestByteCount() {

		// Given: an origin that accepts requests and a running proxy
		await using var origin = await TestOrigin.StartAsync(c => Task.CompletedTask);
		await using var proxy = await TestProxy.StartAsync();

		// When: a client posts a 1,234-byte body through the proxy
		await proxy.Client.PostAsync(origin.UrlFor("/up"), new ByteArrayContent(new byte[1234]));

		// Then: the logged record shows 1,234 request bytes
		(await proxy.Log.WaitForAsync()).Single().RequestBytes.ShouldBe(1234);
	}

	[Fact]
	public async Task Get_RequestCarriesCredentials_LogKeepsThemAndOriginReceivesThem() {

		// Given: an origin that records headers and a running proxy
		await using var origin = await TestOrigin.StartAsync(c => { c.Response.Headers["Set-Cookie"] = "session=secret"; return Task.CompletedTask; });
		await using var proxy = await TestProxy.StartAsync();

		// When: a client sends a request with Authorization and Cookie headers
		var request = new HttpRequestMessage(HttpMethod.Get, origin.UrlFor("/"));
		request.Headers.TryAddWithoutValidation("Authorization", "Bearer topsecret");
		request.Headers.TryAddWithoutValidation("Cookie", "a=b");
		await proxy.Client.SendAsync(request);

		// Then: the log shows the real Authorization, Cookie and Set-Cookie values (nothing is redacted), and the origin got them too
		var record = (await proxy.Log.WaitForAsync()).Single();
		record.RequestHeaders["Authorization"].ShouldBe("Bearer topsecret");
		record.RequestHeaders["Cookie"].ShouldBe("a=b");
		record.ResponseHeaders["Set-Cookie"].ShouldBe("session=secret");
		origin.Seen.Single().Headers.Authorization.ToString().ShouldBe("Bearer topsecret");
	}

	[Fact]
	public async Task Get_OriginIsUnreachable_LogsErrorWith502() {

		// Given: a running proxy and a URL on a loopback port where nothing is listening
		await using var proxy = await TestProxy.StartAsync();
		int deadPort;
		using (var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0)) {
			probe.Start();
			deadPort = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
		}

		// When: a client requests that URL through the proxy
		await proxy.Client.GetAsync($"http://127.0.0.1:{deadPort}/");

		// Then: the logged record has status 502 and an error message
		var record = (await proxy.Log.WaitForAsync()).Single();
		record.Status.ShouldBe(502);
		record.Error.ShouldNotBeNull();
	}

	[Fact]
	public void ConsoleLog_Write_PrintsMethodUrlAndStatusOnOneLine() {

		// Given: a console log writing to an in-memory writer and a record for GET http://a.test/x answered with 200
		var writer = new StringWriter();
		var log = new ConsoleExchangeLog(new ColorConsole(writer));
		var record = new ExchangeRecord { Method = "GET", Url = "http://a.test/x", Status = 200, DurationMs = 12, ResponseBytes = 34 };

		// When: the record is written
		log.Write(record);

		// Then: one line contains the method, URL, status, duration and byte count
		writer.ToString().TrimEnd().ShouldBe("GET http://a.test/x -> 200 (12 ms, 34 bytes)");
	}

	[Fact]
	public void JsonLinesLog_WriteTwoRecords_AppendsTwoParsableLines() {

		// Given: a JSON Lines log writing to a new temp file
		var path = Path.Combine(Path.GetTempPath(), $"mosaproxy-test-{Guid.NewGuid():N}", "log.jsonl");
		var log = new JsonLinesExchangeLog(path);

		try {
			// When: two records are written
			log.Write(new ExchangeRecord { Method = "GET", Url = "http://a.test/1", Status = 200 });
			log.Write(new ExchangeRecord { Method = "POST", Url = "http://a.test/2", Status = 201 });

			// Then: the file has two lines, each valid JSON carrying that record's method and URL
			var lines = File.ReadAllLines(path);
			lines.Length.ShouldBe(2);
			var first = JsonDocument.Parse(lines[0]).RootElement;
			first.GetProperty("Method").GetString().ShouldBe("GET");
			first.GetProperty("Url").GetString().ShouldBe("http://a.test/1");
			JsonDocument.Parse(lines[1]).RootElement.GetProperty("Status").GetInt32().ShouldBe(201);
		}
		finally {
			Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
		}
	}
}
