using Microsoft.AspNetCore.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace MoSaProxy.Tests;

/// <summary>Reads Server-Sent Events from an open response, one event at a time.</summary>
sealed class SseReader(StreamReader reader) : IDisposable {
	public sealed record Event(string? Id, string Name, string Data);

	public static async Task<SseReader> OpenAsync(HttpClient client, string url, string? lastEventId = null) {
		var request = new HttpRequestMessage(HttpMethod.Get, url);
		if (lastEventId is not null) request.Headers.Add("Last-Event-ID", lastEventId);
		var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
		response.EnsureSuccessStatusCode();
		response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");
		return new SseReader(new StreamReader(await response.Content.ReadAsStreamAsync()));
	}

	/// <summary>The next event (comment lines are skipped), or fails the test if none arrives within the timeout.</summary>
	public async Task<Event> NextAsync(int seconds = 5) {
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
		string? id = null, name = null, data = null;
		while (true) {
			string? line = await reader.ReadLineAsync(timeout.Token) ?? throw new EndOfStreamException("The stream ended.");
			if (line.Length == 0) { if (name is not null) return new Event(id, name, data ?? ""); continue; }
			if (line.StartsWith(':')) continue;
			if (line.StartsWith("id: ")) id = line[4..];
			else if (line.StartsWith("event: ")) name = line[7..];
			else if (line.StartsWith("data: ")) data = line[6..];
		}
	}

	public void Dispose() => reader.Dispose();
}

public class ApiStream_Tests {

	static Task<TestOrigin> StartOriginAsync() => TestOrigin.StartAsync(async c => await c.Response.WriteAsync("hi"));

	[Fact]
	public async Task Stream_WhenARequestIsCaptured_AnnouncesItsStartAndFinishInOrder() {

		// Given: an open event stream
		await using var origin = await StartOriginAsync();
		await using var api = await TestApi.StartAsync();
		using var stream = await SseReader.OpenAsync(api.Client, "api/stream");

		// When: a request goes through the proxy
		await api.Proxy.Client.GetAsync(origin.UrlFor("/one"));
		var started = await stream.NextAsync();
		var finished = await stream.NextAsync();

		// Then: the stream says exchange 1 started and then finished, with ids that grow
		started.Name.ShouldBe("exchange-started");
		started.Data.ShouldBe("{\"id\":1}");
		finished.Name.ShouldBe("exchange-finished");
		finished.Data.ShouldBe("{\"id\":1}");
		long.Parse(finished.Id!).ShouldBeGreaterThan(long.Parse(started.Id!));
	}

	[Fact]
	public async Task Stream_WhenAnExchangeFinishes_ItCanAlreadyBeRead() {

		// Given: an open event stream
		await using var origin = await StartOriginAsync();
		await using var api = await TestApi.StartAsync();
		using var stream = await SseReader.OpenAsync(api.Client, "api/stream");

		// When: the finish event arrives
		await api.Proxy.Client.GetAsync(origin.UrlFor("/readable"));
		int finishedId = await NextIdOfAsync(stream, "exchange-finished");
		var detail = await api.Client.GetAsync($"api/exchanges/{finishedId}");

		// Then: the exchange is already available from the API
		detail.IsSuccessStatusCode.ShouldBeTrue();
	}

	/// <summary>Reads events until one with the given name, and returns the exchange id in its data.</summary>
	static async Task<int> NextIdOfAsync(SseReader stream, string name) {
		while (true) {
			var e = await stream.NextAsync();
			if (e.Name == name) return JsonDocument.Parse(e.Data).RootElement.GetProperty("id").GetInt32();
		}
	}

	[Fact]
	public async Task Stream_WithoutALastEventId_DoesNotReplayEarlierEvents() {

		// Given: a request already captured before anyone listens
		await using var origin = await StartOriginAsync();
		await using var api = await TestApi.StartAsync();
		await api.CaptureOneAsync(origin, "/early", 1);

		// When: a stream is opened, then capture is switched off
		using var stream = await SseReader.OpenAsync(api.Client, "api/stream");
		await api.Client.PutAsJsonAsync("api/capture", new ApiCaptureRequest(false));
		var first = await stream.NextAsync();

		// Then: the first event is the switch, not the earlier exchange
		first.Name.ShouldBe("capture");
		first.Data.ShouldBe("{\"enabled\":false}");
	}

	[Fact]
	public async Task Stream_WithALastEventId_ReplaysWhatWasMissedThenContinuesLive() {

		// Given: a stream that saw the first request's start, then went away
		await using var origin = await StartOriginAsync();
		await using var api = await TestApi.StartAsync();
		string lastSeen;
		using (var first = await SseReader.OpenAsync(api.Client, "api/stream")) {
			await api.Proxy.Client.GetAsync(origin.UrlFor("/one"));
			lastSeen = (await first.NextAsync()).Id!;
		}
		await api.CaptureOneAsync(origin, "/two", 2);

		// When: it reconnects with that id
		using var again = await SseReader.OpenAsync(api.Client, "api/stream", lastSeen);
		var events = new List<SseReader.Event>();
		for (int i = 0; i < 3; i++) events.Add(await again.NextAsync());
		await api.Proxy.Client.GetAsync(origin.UrlFor("/three"));
		var live = await again.NextAsync();

		// Then: it gets the first request's finish and the second's start and finish, then the third live
		events.Select(e => e.Name + e.Data).ShouldBe(new[] { "exchange-finished{\"id\":1}", "exchange-started{\"id\":2}", "exchange-finished{\"id\":2}" });
		live.Name.ShouldBe("exchange-started");
		live.Data.ShouldBe("{\"id\":3}");
	}

	[Fact]
	public async Task Stream_WithTheTokenInTheQueryString_Works() {

		// Given: a client with no token header (as a browser's EventSource is)
		await using var api = await TestApi.StartAsync();
		using var anonymous = api.NewClient(token: null);
		using var stream = await SseReader.OpenAsync(anonymous, $"api/stream?token={api.Api.Token}");

		// When: capture is switched off
		await api.Client.PutAsJsonAsync("api/capture", new ApiCaptureRequest(false));

		// Then: the event arrives
		(await stream.NextAsync()).Name.ShouldBe("capture");
	}

	[Fact]
	public async Task Stream_WithLastEventIdQueryParameter_ReplaysLikeTheHeader() {

		// Given: a captured request, and the id of its first event
		await using var origin = await StartOriginAsync();
		await using var api = await TestApi.StartAsync();
		using var watcher = await SseReader.OpenAsync(api.Client, "api/stream");
		await api.Proxy.Client.GetAsync(origin.UrlFor("/one"));
		await watcher.NextAsync();
		await watcher.NextAsync();

		// When: a stream is opened asking to resume after event 0
		using var replay = await SseReader.OpenAsync(api.Client, "api/stream?lastEventId=0");

		// Then: it starts with the exchange's start
		var first = await replay.NextAsync();
		first.Name.ShouldBe("exchange-started");
	}

	[Fact]
	public async Task EventFeed_WhenAskedForEventsItNoLongerRemembers_SaysSome_AreMissed() {

		// Given: a feed that remembers only two events, after four happened
		await using var proxy = await TestProxy.StartAsync();
		using var feed = new ApiEventFeed(proxy.Events, capacity: 2);
		foreach (bool on in new[] { false, true, false, true }) proxy.CaptureEnabled = on;

		// When: it is asked for the events after the first
		var (events, missed, _) = feed.After(1);
		var (recent, recentMissed, _) = feed.After(2);

		// Then: from 1 some are gone, so it says so; from 2 nothing is gone
		missed.ShouldBeTrue();
		events.Select(e => e.Id).ShouldBe(new long[] { 3, 4 });
		recentMissed.ShouldBeFalse();
		recent.Select(e => e.Id).ShouldBe(new long[] { 3, 4 });
	}
}
