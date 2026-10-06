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

	[Fact]
	public void Parse_NoConnectTimeoutArgument_UsesTheDefaultOf15Seconds() {

		// Given: no command-line arguments
		var args = Array.Empty<string>();

		// When: the options are parsed
		var options = ProxyOptions.Parse(args);

		// Then: the tunnel connect timeout is 15 seconds
		options.ConnectTimeout.ShouldBe(TimeSpan.FromSeconds(15));
	}

	[Theory]
	[InlineData("5", 5.0)]
	[InlineData("2.5", 2.5)]
	[InlineData("0.5", 0.5)]
	public void Parse_ConnectTimeoutArgument_SetsTheTimeoutInSeconds(string value, double expectedSeconds) {

		// Given: an argument setting the connect timeout to the given number of seconds
		var args = new[] { "--connect-timeout", value };

		// When: the options are parsed
		var options = ProxyOptions.Parse(args);

		// Then: the connect timeout is that many seconds
		options.ConnectTimeout.ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
	}

	[Fact]
	public void Parse_NoInterceptArguments_LeavesInterceptionOff() {

		// Given: no command-line arguments
		var args = Array.Empty<string>();

		// When: the options are parsed
		var options = ProxyOptions.Parse(args);

		// Then: HTTPS interception is off, nothing is bypassed, and the CA folder is the default one in the user's profile
		options.Intercept.ShouldBeFalse();
		options.InterceptBypass.ShouldBeEmpty();
		options.CaDirectory.ShouldBe(ProxyOptions.DefaultCaDirectory);
	}

	[Fact]
	public void Parse_UntrustCaArgument_IsSetAndDefaultsToOff() {

		// Given: no arguments, and separately the --untrust-ca argument
		var none = Array.Empty<string>();
		var untrust = new[] { "--untrust-ca" };

		// When: both are parsed
		var defaults = ProxyOptions.Parse(none);
		var withFlag = ProxyOptions.Parse(untrust);

		// Then: removing the trusted root is off by default and on with the flag
		defaults.UntrustCa.ShouldBeFalse();
		withFlag.UntrustCa.ShouldBeTrue();
	}

	[Fact]
	public void Parse_InterceptArguments_AreApplied() {

		// Given: arguments that turn interception on, bypass two hosts (one a wildcard), and choose a CA folder
		var args = new[] { "--intercept", "--intercept-bypass", "bank.example, *.secure.example", "--ca-dir", "my-ca" };

		// When: the options are parsed
		var options = ProxyOptions.Parse(args);

		// Then: interception is on, the bypass list holds both hosts (trimmed), and the CA folder is the chosen one
		options.Intercept.ShouldBeTrue();
		options.InterceptBypass.ShouldBe(new[] { "bank.example", "*.secure.example" });
		options.CaDirectory.ShouldBe("my-ca");
	}

	[Theory]
	[InlineData("--intercept-bypass", "bank.example")]
	[InlineData("--intercept", "--intercept-bypass")]
	[InlineData("--intercept", "--ca-dir")]
	public void Parse_InvalidInterceptArguments_Throws(params string[] args) {

		// Given: a bypass list without --intercept, or an option missing its value
		// (supplied by the test data)

		// When: the options are parsed
		var parse = () => ProxyOptions.Parse(args);

		// Then: parsing fails with an ArgumentException
		Should.Throw<ArgumentException>(parse);
	}

	[Theory]
	[InlineData("--connect-timeout")]
	[InlineData("--connect-timeout", "0")]
	[InlineData("--connect-timeout", "-3")]
	[InlineData("--connect-timeout", "soon")]
	[InlineData("--connect-timeout", "99999")]
	public void Parse_InvalidConnectTimeout_Throws(params string[] args) {

		// Given: a connect timeout argument that is missing, zero, negative, not a number, or absurdly large
		// (supplied by the test data)

		// When: the options are parsed
		var parse = () => ProxyOptions.Parse(args);

		// Then: parsing fails with an ArgumentException
		Should.Throw<ArgumentException>(parse);
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
