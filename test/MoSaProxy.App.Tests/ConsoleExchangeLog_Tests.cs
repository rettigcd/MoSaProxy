using Shouldly;

namespace MoSaProxy.Tests;

public class ConsoleExchangeLog_Tests {

	[Fact]
	public void Write_PrintsMethodUrlAndStatusOnOneLine() {

		// Given: a console log writing to an in-memory writer and a record for GET http://a.test/x answered with 200
		var writer = new StringWriter();
		var log = new ConsoleExchangeLog(new ColorConsole(writer));
		var record = new ExchangeRecord { Method = "GET", Url = "http://a.test/x", Status = 200, DurationMs = 12, ResponseBytes = 34 };

		// When: the record is written
		log.Write(record);

		// Then: one line contains the method, URL, status, duration and byte count
		writer.ToString().TrimEnd().ShouldBe("GET http://a.test/x -> 200 (12 ms, 34 bytes)");
	}
}
