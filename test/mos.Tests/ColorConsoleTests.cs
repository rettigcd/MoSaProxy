using Shouldly;

namespace mos.Tests;

public class ColorConsoleTests {

	[Fact]
	public void WriteLine_ColouredMessage_WritesOnlyTheTextAndEndsTheLine() {

		// Given: a console that writes to an in-memory writer instead of the real console
		var writer = new StringWriter();
		var console = new ColorConsole(writer);

		// When: a message with a foreground colour, a background colour and their restores is written
		console.WriteLine($"{Fg.Green}OK{Fg.Restore} and {Bg.Red}bad{Bg.Restore} done");

		// Then: only the text is written (no colour codes), followed by one line break
		writer.ToString().ShouldBe("OK and bad done" + Environment.NewLine);
	}

	[Fact]
	public void Write_Message_DoesNotEndTheLine() {

		// Given: a console that writes to an in-memory writer
		var writer = new StringWriter();
		var console = new ColorConsole(writer);

		// When: two messages are written with Write, then one with WriteLine
		console.Write($"a{Fg.Red}b{Fg.Restore}");
		console.Write("c");
		console.WriteLine("d");

		// Then: they run together on one line
		writer.ToString().ShouldBe("abcd" + Environment.NewLine);
	}

	[Fact]
	public void WriteLine_PlainString_IsWrittenAsIs() {

		// Given: a console that writes to an in-memory writer
		var writer = new StringWriter();

		// When: a plain string that contains braces is written (no interpolation)
		new ColorConsole(writer).WriteLine("{not a hole} 100%");

		// Then: it appears exactly as given
		writer.ToString().ShouldBe("{not a hole} 100%" + Environment.NewLine);
	}

	[Fact]
	public void WriteLine_AlignmentAndFormat_PadAndFormatTheValues() {

		// Given: a console that writes to an in-memory writer
		var writer = new StringWriter();

		// When: values are written with a width (right-aligned and left-aligned) and with a number format
		var name = "ab";
		var ms = 12.345;
		new ColorConsole(writer).WriteLine($"[{name,5}] [{name,-5}] [{ms:0.0}] [{ms,8:0.00}]");

		// Then: the padding and number formats are applied
		writer.ToString().ShouldBe("[   ab] [ab   ] [12.3] [   12.35]" + Environment.NewLine);
	}

	[Fact]
	public void WriteLine_NullValue_WritesNothingForIt() {

		// Given: a console that writes to an in-memory writer and a null value
		var writer = new StringWriter();
		string? missing = null;

		// When: the null value is part of a message
		new ColorConsole(writer).WriteLine($"[{missing}]");

		// Then: it contributes an empty string
		writer.ToString().ShouldBe("[]" + Environment.NewLine);
	}

	[Fact]
	public async Task WriteLine_ManyThreadsAtOnce_NeverMixesMessages() {

		// Given: a console writing to one shared writer
		var writer = new StringWriter();
		var console = new ColorConsole(writer);

		// When: eight threads each write 200 messages that are made of several pieces
		await Task.WhenAll(Enumerable.Range(0, 8).Select(thread => Task.Run(() => {
			for (int i = 0; i < 200; i++)
				console.WriteLine($"{thread}:{Fg.Cyan}{i}{Fg.Restore}:{thread}");
		})));

		// Then: every line is a complete message (its first and last numbers agree), and none is missing
		var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
		lines.Length.ShouldBe(1600);
		lines.ShouldAllBe(line => line.Split(':')[0] == line.Split(':')[2]);
	}

	[Fact]
	public void WriteLine_NoColour_PrintsJustTheText() {

		// Given: a console that writes to an in-memory writer and a colour that is "none"
		var writer = new StringWriter();
		ForegroundColor? none = null;
		ForegroundColor? blue = Fg.Blue;

		// When: a message uses it, and another message uses a real colour held in the same kind of variable
		new ColorConsole(writer).WriteLine($"{none}plain{Fg.Restore} and {blue}coloured{Fg.Restore}");

		// Then: neither marker prints anything, so only the text appears
		writer.ToString().ShouldBe("plain and coloured" + Environment.NewLine);
	}

	[Theory]
	[InlineData("GET")]
	[InlineData("POST")]
	[InlineData("PUT")]
	[InlineData("DELETE")]
	[InlineData("PATCH")]
	public void MethodColor_CommonRequestMethods_AreBlue(string method) {

		// Given: a common request method (supplied by the test data)

		// When: its console colour is looked up
		var color = ConsoleExchangeLog.MethodColor(method);

		// Then: it is blue
		color.ShouldBe(Fg.Blue);
	}

	[Theory]
	[InlineData("CONNECT")]
	[InlineData("HEAD")]
	[InlineData("OPTIONS")]
	[InlineData("TRACE")]
	[InlineData("get")]
	public void MethodColor_OtherMethods_KeepTheConsolesOwnColour(string method) {

		// Given: a method that is not one of the five (supplied by the test data; HTTP methods are case-sensitive, so "get" is not "GET")

		// When: its console colour is looked up
		var color = ConsoleExchangeLog.MethodColor(method);

		// Then: there is no colour, meaning the text keeps the console's normal colour
		color.ShouldBeNull();
	}

	[Fact]
	public void ConsoleLog_StatusColours_DoNotChangeThePlainText() {

		// Given: a console log writing to an in-memory writer, and records for a success, a redirect, a client error and a server error with a message
		var writer = new StringWriter();
		var log = new ConsoleExchangeLog(new ColorConsole(writer));

		// When: the four records are written
		log.Write(new ExchangeRecord { Method = "GET", Url = "http://a.test/ok", Status = 200, DurationMs = 1, ResponseBytes = 2 });
		log.Write(new ExchangeRecord { Method = "GET", Url = "http://a.test/moved", Status = 302, DurationMs = 3, ResponseBytes = 4 });
		log.Write(new ExchangeRecord { Method = "GET", Url = "http://a.test/missing", Status = 404, DurationMs = 5, ResponseBytes = 6 });
		log.Write(new ExchangeRecord { Method = "GET", Url = "http://a.test/down", Status = 502, DurationMs = 7, ResponseBytes = 0, Error = "origin unreachable" });

		// Then: the text is one plain line per record, with the error in brackets at the end of the last one
		writer.ToString().ShouldBe(string.Join(Environment.NewLine,
			"GET http://a.test/ok -> 200 (1 ms, 2 bytes)",
			"GET http://a.test/moved -> 302 (3 ms, 4 bytes)",
			"GET http://a.test/missing -> 404 (5 ms, 6 bytes)",
			"GET http://a.test/down -> 502 (7 ms, 0 bytes) [origin unreachable]") + Environment.NewLine);
	}
}
