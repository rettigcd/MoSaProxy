using System.Text;
using Shouldly;

namespace MoSaProxy.Tests;

public class Http1Framing_Tests {

	static byte[] B(string text) => Encoding.Latin1.GetBytes(text);

	[Fact]
	public void FindRequest_GetWithoutBody_EndsAtTheBlankLine() {

		// Given: two bodiless requests back to back
		string first = "GET /a HTTP/1.1\r\nHost: x\r\n\r\n";
		var bytes = B(first + "GET /b HTTP/1.1\r\nHost: x\r\n\r\n");

		// When: the first request is looked for at the start
		var found = Http1Framing.FindRequest(bytes, 0);

		// Then: it is exactly the first request, and complete
		found.ShouldNotBeNull();
		found.Value.Start.ShouldBe(0);
		found.Value.Length.ShouldBe(first.Length);
		found.Value.StartLine.ShouldBe("GET /a HTTP/1.1");
		found.Value.Complete.ShouldBeTrue();
	}

	[Fact]
	public void FindRequest_PostWithContentLength_IncludesTheBodyButNotTheNextRequest() {

		// Given: a POST with a 4-byte body followed by another request
		string post = "POST /p HTTP/1.1\r\nContent-Length: 4\r\n\r\nabcd";
		var bytes = B(post + "GET /next HTTP/1.1\r\n\r\n");

		// When: the request is looked for
		var found = Http1Framing.FindRequest(bytes, 0);

		// Then: it ends after the body
		found!.Value.Length.ShouldBe(post.Length);
	}

	[Fact]
	public void FindRequest_ChunkedBodyWithExtensionAndTrailer_EndsAfterTheTrailer() {

		// Given: a chunked request whose chunk has an extension and whose body is followed by a trailer line
		string request = "POST /c HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n3;ext=1\r\nabc\r\n0\r\nX-Trailer: 1\r\n\r\n";
		var bytes = B(request + "GET /next HTTP/1.1\r\n\r\n");

		// When: the request is looked for
		var found = Http1Framing.FindRequest(bytes, 0);

		// Then: it ends after the trailer's blank line
		found!.Value.Length.ShouldBe(request.Length);
		found.Value.Complete.ShouldBeTrue();
	}

	[Fact]
	public void FindRequest_BodyNotFullyArrived_IsIncompleteAndRunsToTheEnd() {

		// Given: a request that promises 10 body bytes but only 3 have arrived
		var bytes = B("POST /p HTTP/1.1\r\nContent-Length: 10\r\n\r\nabc");

		// When: the request is looked for
		var found = Http1Framing.FindRequest(bytes, 0);

		// Then: it is reported as incomplete, covering everything that arrived
		found!.Value.Complete.ShouldBeFalse();
		found.Value.Length.ShouldBe(bytes.Length);
	}

	[Fact]
	public void FindRequest_HeadNotFullyArrived_FindsNothing() {

		// Given: only part of a request head
		var bytes = B("GET /a HTTP/1.1\r\nHost: x\r\n");

		// When / Then: no request is found yet
		Http1Framing.FindRequest(bytes, 0).ShouldBeNull();
	}

	[Fact]
	public void FindResponse_HeadRequest_HasNoBodyEvenWithAContentLength() {

		// Given: the response to a HEAD request, which carries Content-Length: 5 but no body, followed by another response
		string head = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\n";
		var bytes = B(head + "HTTP/1.1 204 No Content\r\n\r\n");

		// When: the response to a HEAD request is looked for
		var found = Http1Framing.FindResponse(bytes, 0, "HEAD");

		// Then: it ends at the blank line
		found!.Value.Length.ShouldBe(head.Length);
	}

	[Theory]
	[InlineData("HTTP/1.1 204 No Content\r\nContent-Length: 5\r\n\r\n")]
	[InlineData("HTTP/1.1 304 Not Modified\r\nContent-Length: 5\r\n\r\n")]
	public void FindResponse_StatusWithoutBody_EndsAtTheBlankLine(string response) {

		// Given: a 204 or 304 response whose headers mention a length, followed by more bytes
		var bytes = B(response + "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");

		// When: it is looked for
		var found = Http1Framing.FindResponse(bytes, 0, "GET");

		// Then: it ends at the blank line
		found!.Value.Length.ShouldBe(response.Length);
	}

	[Fact]
	public void FindResponse_InterimResponseThenFinal_CoversBoth() {

		// Given: a 100 Continue followed by the final 200 response
		string interim = "HTTP/1.1 100 Continue\r\n\r\n";
		string final = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok";
		var bytes = B(interim + final);

		// When: the response is looked for
		var found = Http1Framing.FindResponse(bytes, 0, "POST");

		// Then: it covers the interim response and the final one
		found!.Value.Length.ShouldBe(interim.Length + final.Length);
		found.Value.StartLine.ShouldBe("HTTP/1.1 200 OK");
	}

	[Fact]
	public void FindResponse_NoLengthAndNotChunked_RunsToTheEndAndIsIncomplete() {

		// Given: a response that is delimited only by the connection closing
		var bytes = B("HTTP/1.1 200 OK\r\nConnection: close\r\n\r\nsome body");

		// When: it is looked for
		var found = Http1Framing.FindResponse(bytes, 0, "GET");

		// Then: it covers everything that arrived, and is not known to be complete
		found!.Value.Length.ShouldBe(bytes.Length);
		found.Value.Complete.ShouldBeFalse();
	}
}
