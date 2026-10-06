using Shouldly;

namespace mos.Tests;

public class ProxyOptionsTests {

	[Fact]
	public void Parse_NoArguments_UsesDefaults() {

		// Given: no command-line arguments
		var args = Array.Empty<string>();

		// When: the options are parsed
		var options = ProxyOptions.Parse(args);

		// Then: the port is 8080 and there is no log file
		options.Port.ShouldBe(8080);
		options.LogPath.ShouldBeNull();
	}

	[Fact]
	public void Parse_PortAndLogArguments_AreApplied() {

		// Given: arguments setting the port to 9000 and a log file path
		var args = new[] { "--port", "9000", "--log", "traffic.jsonl" };

		// When: the options are parsed
		var options = ProxyOptions.Parse(args);

		// Then: the options carry that port and path
		options.Port.ShouldBe(9000);
		options.LogPath.ShouldBe("traffic.jsonl");
	}

	[Fact]
	public void Parse_CaptureArguments_AreApplied() {

		// Given: arguments enabling capture into a folder with a 1,000-byte body cap
		var args = new[] { "--capture", "captures", "--capture-max-body", "1000" };

		// When: the options are parsed
		var options = ProxyOptions.Parse(args);

		// Then: the options carry the folder and the cap
		options.CaptureDirectory.ShouldBe("captures");
		options.CaptureMaxBodyBytes.ShouldBe(1000);
	}

	[Fact]
	public void Parse_NoCaptureArguments_LeavesCaptureOff() {

		// Given: no command-line arguments
		var args = Array.Empty<string>();

		// When: the options are parsed
		var options = ProxyOptions.Parse(args);

		// Then: capture is off and there is no body cap
		options.CaptureDirectory.ShouldBeNull();
		options.CaptureMaxBodyBytes.ShouldBeNull();
	}

	[Theory]
	[InlineData("--capture")]
	[InlineData("--capture-max-body")]
	[InlineData("--capture-max-body", "-5")]
	[InlineData("--capture-max-body", "lots")]
	[InlineData("--port", "abc")]
	[InlineData("--port", "70000")]
	[InlineData("--port")]
	[InlineData("--log")]
	[InlineData("--bind", "0.0.0.0")]
	public void Parse_InvalidArguments_Throws(params string[] args) {

		// Given: invalid arguments (a bad or missing value, or an unknown option such as --bind, which is deliberately unsupported)
		// (supplied by the test data)

		// When: the options are parsed
		var parse = () => ProxyOptions.Parse(args);

		// Then: parsing fails with an ArgumentException
		Should.Throw<ArgumentException>(parse);
	}
}
