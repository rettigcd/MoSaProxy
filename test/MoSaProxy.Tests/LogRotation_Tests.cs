using System.Text.Json;
using Shouldly;

namespace MoSaProxy.Tests;

public class LogRotation_Tests {

	static ExchangeRecord Record(int number) => new() { Method = "GET", Url = $"http://a.test/{number}", Status = 200 };

	/// <summary>How many bytes one line of the log takes (every test record has the same length, up to 3 digits).</summary>
	static int LineSize(TempFolder folder) {
		string probe = Path.Combine(folder.Path, "probe.jsonl");
		new JsonLinesExchangeLog(probe).Write(Record(100));
		return (int)new FileInfo(probe).Length;
	}

	static string[] Lines(string path) => File.ReadAllLines(path);

	/// <summary>The record numbers in a file, in order.</summary>
	static int[] Numbers(string path) => Lines(path)
		.Select(line => int.Parse(JsonDocument.Parse(line).RootElement.GetProperty("Url").GetString()!.Split('/').Last())).ToArray();

	static string Rotated(TempFolder folder, int number) => Path.Combine(folder.Path, $"traffic.{number}.jsonl");

	[Fact]
	public void Write_BelowTheLimit_NeverRotates() {

		// Given: a log that allows about ten lines per file
		using var folder = new TempFolder();
		int line = LineSize(folder);
		var log = new JsonLinesExchangeLog(Path.Combine(folder.Path, "traffic.jsonl"), maxBytes: line * 10L + 5, keep: 3);

		// When: three records are written
		for (int i = 101; i <= 103; i++) log.Write(Record(i));

		// Then: they are all in the one file and no rotated file exists
		Numbers(Path.Combine(folder.Path, "traffic.jsonl")).ShouldBe(new[] { 101, 102, 103 });
		File.Exists(Rotated(folder, 1)).ShouldBeFalse();
	}

	[Fact]
	public void Write_WouldPassTheLimit_MovesTheCurrentFileToNumberOneAndStartsANewOne() {

		// Given: a log that allows two and a half lines per file
		using var folder = new TempFolder();
		int line = LineSize(folder);
		var log = new JsonLinesExchangeLog(Path.Combine(folder.Path, "traffic.jsonl"), maxBytes: line * 5L / 2, keep: 3);

		// When: three records are written
		for (int i = 101; i <= 103; i++) log.Write(Record(i));

		// Then: the first two are in traffic.1.jsonl and the third started a fresh traffic.jsonl
		Numbers(Rotated(folder, 1)).ShouldBe(new[] { 101, 102 });
		Numbers(Path.Combine(folder.Path, "traffic.jsonl")).ShouldBe(new[] { 103 });
	}

	[Fact]
	public void Write_ManyRotations_KeepsOnlyTheNewestRotatedFilesNewestFirst() {

		// Given: a log that holds one line per file and keeps two rotated files
		using var folder = new TempFolder();
		int line = LineSize(folder);
		var log = new JsonLinesExchangeLog(Path.Combine(folder.Path, "traffic.jsonl"), maxBytes: line * 3L / 2, keep: 2);

		// When: five records are written
		for (int i = 101; i <= 105; i++) log.Write(Record(i));

		// Then: record 105 is current, 104 is .1 (newest rotated), 103 is .2, and 101 and 102 were discarded
		Numbers(Path.Combine(folder.Path, "traffic.jsonl")).ShouldBe(new[] { 105 });
		Numbers(Rotated(folder, 1)).ShouldBe(new[] { 104 });
		Numbers(Rotated(folder, 2)).ShouldBe(new[] { 103 });
		File.Exists(Rotated(folder, 3)).ShouldBeFalse();
	}

	[Fact]
	public void Write_KeepIsZero_DiscardsTheFileInsteadOfRotatingIt() {

		// Given: a log that holds one line per file and keeps no rotated files
		using var folder = new TempFolder();
		int line = LineSize(folder);
		var log = new JsonLinesExchangeLog(Path.Combine(folder.Path, "traffic.jsonl"), maxBytes: line * 3L / 2, keep: 0);

		// When: three records are written
		for (int i = 101; i <= 103; i++) log.Write(Record(i));

		// Then: only the newest record remains, and nothing was rotated
		Numbers(Path.Combine(folder.Path, "traffic.jsonl")).ShouldBe(new[] { 103 });
		File.Exists(Rotated(folder, 1)).ShouldBeFalse();
	}

	[Fact]
	public void Write_NoLimit_NeverRotates() {

		// Given: a log with the limit set to 0 (rotation off)
		using var folder = new TempFolder();
		var log = new JsonLinesExchangeLog(Path.Combine(folder.Path, "traffic.jsonl"), maxBytes: 0, keep: 3);

		// When: 200 records are written
		for (int i = 0; i < 200; i++) log.Write(Record(i % 900 + 100));

		// Then: they are all in the one file
		Lines(Path.Combine(folder.Path, "traffic.jsonl")).Length.ShouldBe(200);
		File.Exists(Rotated(folder, 1)).ShouldBeFalse();
	}

	[Fact]
	public void Write_FileAlreadyOverTheLimitAtStartup_IsRotatedBeforeTheFirstWrite() {

		// Given: a log file left from an earlier run that already holds two lines, and a limit of two and a half lines
		using var folder = new TempFolder();
		int line = LineSize(folder);
		string path = Path.Combine(folder.Path, "traffic.jsonl");
		var earlier = new JsonLinesExchangeLog(path);
		earlier.Write(Record(101));
		earlier.Write(Record(102));
		var log = new JsonLinesExchangeLog(path, maxBytes: line * 5L / 2, keep: 3);

		// When: this run writes its first record
		log.Write(Record(103));

		// Then: the earlier lines were rotated away and the new record starts a fresh file
		Numbers(Rotated(folder, 1)).ShouldBe(new[] { 101, 102 });
		Numbers(path).ShouldBe(new[] { 103 });
	}

	[Fact]
	public void Write_LineLargerThanTheLimit_IsStillWritten() {

		// Given: a limit smaller than a single line
		using var folder = new TempFolder();
		var log = new JsonLinesExchangeLog(Path.Combine(folder.Path, "traffic.jsonl"), maxBytes: 50, keep: 3);

		// When: two records are written
		log.Write(Record(101));
		log.Write(Record(102));

		// Then: neither was dropped: the first sits in .1 and the second is current
		Numbers(Rotated(folder, 1)).ShouldBe(new[] { 101 });
		Numbers(Path.Combine(folder.Path, "traffic.jsonl")).ShouldBe(new[] { 102 });
	}

	[Fact]
	public void Write_RotationIsBlocked_KeepsAppendingAndLosesNothing() {

		// Given: a log that holds one line per file, where the file it needs to replace is open elsewhere (like a viewer holding it)
		if (!OperatingSystem.IsWindows()) return;
		using var folder = new TempFolder();
		int line = LineSize(folder);
		File.WriteAllText(Rotated(folder, 1), "held open\n");
		using var held = new FileStream(Rotated(folder, 1), FileMode.Open, FileAccess.Read, FileShare.None);
		string path = Path.Combine(folder.Path, "traffic.jsonl");
		var log = new JsonLinesExchangeLog(path, maxBytes: line * 3L / 2, keep: 1);
		var stderr = new StringWriter();
		var originalError = Console.Error;
		Console.SetError(stderr);

		try {
			// When: three records are written
			for (int i = 101; i <= 103; i++) log.Write(Record(i));
		}
		finally {
			Console.SetError(originalError);
		}

		// Then: all three are in the current file (nothing lost), the failure was reported once, and the held file was left alone
		Numbers(path).ShouldBe(new[] { 101, 102, 103 });
		stderr.ToString().Split("Log rotation failed").Length.ShouldBe(2);
		held.Length.ShouldBeGreaterThan(0);
	}

	[Fact]
	public async Task Write_FromManyThreadsWhileRotating_LosesAndCorruptsNothing() {

		// Given: a log that rotates often and keeps plenty of files, and eight threads that each write 50 records
		using var folder = new TempFolder();
		int line = LineSize(folder);
		var log = new JsonLinesExchangeLog(Path.Combine(folder.Path, "traffic.jsonl"), maxBytes: line * 7L, keep: 100);

		// When: they all write at the same time
		await Task.WhenAll(Enumerable.Range(0, 8).Select(thread => Task.Run(() => {
			for (int i = 0; i < 50; i++) log.Write(Record(100 + thread * 50 + i % 50));
		})));

		// Then: across the current and rotated files there are exactly 400 lines, each one valid JSON
		var files = Directory.GetFiles(folder.Path, "traffic*.jsonl");
		var all = files.SelectMany(File.ReadAllLines).ToList();
		all.Count.ShouldBe(400);
		all.ShouldAllBe(text => JsonDocument.Parse(text).RootElement.GetProperty("Method").GetString() == "GET");
	}
}
