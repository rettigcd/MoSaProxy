using Shouldly;

namespace MoSaProxy.Tests;

public class ProxyOptionsParser_Tests {

	[Fact]
	public void Parse_NoArguments_GivesTheSameSettingsAsAFreshProxyOptions() {

		// Given: no command-line arguments, and a ProxyOptions created directly (which holds the one definition of every default)
		var args = Array.Empty<string>();
		var fresh = new ProxyOptions();

		// When: the options are parsed
		var parsed = ProxyOptionsParser.Parse(args);

		// Then: every setting equals the default declared in ProxyOptions, so the parser has no defaults of its own that could drift
		static string Describe(object? value) => value is IEnumerable<string> list ? "[" + string.Join(", ", list) + "]" : value?.ToString() ?? "null";
		var differences = new List<string>();
		foreach (var property in typeof(ProxyOptions).GetProperties()) {
			string fromParser = Describe(property.GetValue(parsed));
			string declared = Describe(property.GetValue(fresh));
			if (fromParser != declared) differences.Add($"{property.Name}: the parser gave {fromParser}, ProxyOptions declares {declared}");
		}
		differences.ShouldBeEmpty();
	}

	[Fact]
	public void Parse_UnknownArgument_ErrorNamesItAndShowsTheUsage() {

		// Given: an argument the program does not have
		var args = new[] { "--bogus" };

		// When: the options are parsed
		var parse = () => { ProxyOptionsParser.Parse(args); };

		// Then: the error names the argument and ends with the usage line
		var message = Should.Throw<ArgumentException>(parse).Message;
		message.ShouldStartWith("Unknown argument '--bogus'.");
		message.ShouldEndWith(ProxyOptionsParser.Usage);
		ProxyOptionsParser.Usage.ShouldStartWith("Usage: MoSaProxy");
	}

	[Theory]
	[InlineData("--port")]
	[InlineData("--log")]
	[InlineData("--no-capture")]
	[InlineData("--capture-max-body")]
	[InlineData("--connect-timeout")]
	[InlineData("--intercept")]
	[InlineData("--intercept-bypass")]
	[InlineData("--ca-dir")]
	[InlineData("--untrust-ca")]
	[InlineData("--no-http2")]
	[InlineData("--saz")]
	[InlineData("--open")]
	[InlineData("--log-max-mb")]
	[InlineData("--log-keep")]
	[InlineData("--drain-timeout")]
	public void Usage_MentionsEveryOptionTheParserAccepts(string option) {

		// Given: an option the parser accepts (supplied by the test data)

		// When: the usage line is read
		var usage = ProxyOptionsParser.Usage;

		// Then: it mentions the option, so the help cannot silently fall behind the options
		usage.ShouldContain(option);
	}

	[Fact]
	public void Parse_DecimalNumbers_AreReadWithADotWhateverTheRegionalSettings() {

		// Given: a machine set to German regional settings (where "2.5" would otherwise be read as 25) and an argument of 2.5 megabytes
		var original = System.Globalization.CultureInfo.CurrentCulture;
		System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

		try {
			// When: the options are parsed
			var options = ProxyOptionsParser.Parse(new[] { "--log-max-mb", "2.5" });

			// Then: it is 2.5 megabytes
			options.LogMaxBytes.ShouldBe(2_621_440);
		}
		finally {
			System.Globalization.CultureInfo.CurrentCulture = original;
		}
	}

	[Fact]
	public void Parse_NoArguments_UsesDefaults() {

		// Given: no command-line arguments
		var args = Array.Empty<string>();

		// When: the options are parsed
		var options = ProxyOptionsParser.Parse(args);

		// Then: the port is 8080 and there is no log file
		options.Port.ShouldBe(8080);
		options.LogPath.ShouldBeNull();
	}

	[Fact]
	public void Parse_PortAndLogArguments_AreApplied() {

		// Given: arguments setting the port to 9000 and a log file path
		var args = new[] { "--port", "9000", "--log", "traffic.jsonl" };

		// When: the options are parsed
		var options = ProxyOptionsParser.Parse(args);

		// Then: the options carry that port and path
		options.Port.ShouldBe(9000);
		options.LogPath.ShouldBe("traffic.jsonl");
	}

	[Fact]
	public void Parse_CaptureArguments_AreApplied() {

		// Given: arguments setting a 1,000-byte body cap
		var args = new[] { "--capture-max-body", "1000" };

		// When: the options are parsed
		var options = ProxyOptionsParser.Parse(args);

		// Then: the options carry the cap
		options.CaptureMaxBodyBytes.ShouldBe(1000);
	}

	[Fact]
	public void Parse_NoCaptureArguments_UsesTheTemporaryFolderAndNoBodyCap() {

		// Given: no command-line arguments
		var args = Array.Empty<string>();

		// When: the options are parsed
		var options = ProxyOptionsParser.Parse(args);

		// Then: no folder is chosen (the run uses its own temporary one) and there is no body cap
		options.CaptureDirectory.ShouldBeNull();
		options.CaptureMaxBodyBytes.ShouldBeNull();
	}

	[Fact]
	public void Parse_NoConnectTimeoutArgument_UsesTheDefaultOf15Seconds() {

		// Given: no command-line arguments
		var args = Array.Empty<string>();

		// When: the options are parsed
		var options = ProxyOptionsParser.Parse(args);

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
		var options = ProxyOptionsParser.Parse(args);

		// Then: the connect timeout is that many seconds
		options.ConnectTimeout.ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
	}

	[Fact]
	public void Parse_NoInterceptArguments_LeavesInterceptionOff() {

		// Given: no command-line arguments
		var args = Array.Empty<string>();

		// When: the options are parsed
		var options = ProxyOptionsParser.Parse(args);

		// Then: HTTPS interception is off, nothing is bypassed, and the CA folder is the default one in the user's profile
		options.Intercept.ShouldBeFalse();
		options.InterceptBypass.ShouldBeEmpty();
		options.CaDirectory.ShouldBe(ProxyOptions.DefaultCaDirectory);
	}

	[Fact]
	public void Parse_NoCapture_StartsWithCaptureOffAndOtherwiseItIsOn() {

		// Given: arguments with --no-capture, and no arguments at all
		var off = new[] { "--no-capture" };
		var none = Array.Empty<string>();

		// When: both are parsed
		var withFlag = ProxyOptionsParser.Parse(off);
		var without = ProxyOptionsParser.Parse(none);

		// Then: capture starts off only when asked
		withFlag.CaptureEnabled.ShouldBeFalse();
		without.CaptureEnabled.ShouldBeTrue();
	}

	[Fact]
	public void Parse_SazWithAFile_SetsTheArchivePath() {

		// Given: --saz naming the archive to write, followed by another option
		var args = new[] { "--saz", "my.saz", "--port", "9000" };

		// When: the options are parsed
		var options = ProxyOptionsParser.Parse(args);

		// Then: the path is set and the following option is still understood
		options.SazPath.ShouldBe("my.saz");
		options.Port.ShouldBe(9000);
	}

	[Fact]
	public void Parse_NoSazArgument_WritesNoArchive() {

		// Given: no command-line arguments
		var args = Array.Empty<string>();

		// When: the options are parsed
		var options = ProxyOptionsParser.Parse(args);

		// Then: no archive is written
		options.SazPath.ShouldBeNull();
	}

	[Theory]
	[InlineData("--saz")]
	[InlineData("--saz", "--port", "9000")]
	[InlineData("--capture", "captures")]
	[InlineData("--export-saz", "run1")]
	public void Parse_SazWithoutAFileOrRemovedOptions_Throws(params string[] args) {

		// Given: --saz with no file after it, and the removed --capture and --export-saz options
		// (supplied by the test data)

		// When: the options are parsed
		var parse = () => ProxyOptionsParser.Parse(args);

		// Then: parsing fails, because there is nothing to write to, or the option no longer exists
		Should.Throw<ArgumentException>(parse);
	}

	[Fact]
	public void Parse_OpenAlone_OpensTheBrowserAtItsOwnStartPage() {

		// Given: --open with nothing after it
		var args = new[] { "--open" };

		// When: the options are parsed
		var options = ProxyOptionsParser.Parse(args);

		// Then: the browser is to be opened, with no page chosen
		options.OpenBrowser.ShouldBeTrue();
		options.OpenUrl.ShouldBeNull();
	}

	[Fact]
	public void Parse_OpenWithAUrl_OpensThatPage() {

		// Given: --open followed by a URL, then another option
		var args = new[] { "--open", "http://example.com/", "--port", "9000" };

		// When: the options are parsed
		var options = ProxyOptionsParser.Parse(args);

		// Then: the URL is the page to open, and the next option is still read as an option
		options.OpenBrowser.ShouldBeTrue();
		options.OpenUrl.ShouldBe("http://example.com/");
		options.Port.ShouldBe(9000);
	}

	[Fact]
	public void Parse_OpenFollowedByAnotherOption_TakesNoUrl() {

		// Given: --open directly followed by another option
		var args = new[] { "--open", "--no-http2" };

		// When: the options are parsed
		var options = ProxyOptionsParser.Parse(args);

		// Then: no page is chosen, and --no-http2 is still read
		options.OpenBrowser.ShouldBeTrue();
		options.OpenUrl.ShouldBeNull();
	}

	[Fact]
	public void Parse_NoOpen_DoesNotOpenTheBrowser() {

		// Given: no --open
		var args = new[] { "--port", "9000" };

		// When: the options are parsed
		var options = ProxyOptionsParser.Parse(args);

		// Then: the browser is not opened
		options.OpenBrowser.ShouldBeFalse();
	}

	[Fact]
	public void Parse_NoRotationOrDrainArguments_UsesTheDefaults() {

		// Given: no command-line arguments
		var args = Array.Empty<string>();

		// When: the options are parsed
		var options = ProxyOptionsParser.Parse(args);

		// Then: the log rotates at 50 MB keeping 5 files, and stopping waits up to 10 seconds
		options.LogMaxBytes.ShouldBe(50L * 1024 * 1024);
		options.LogKeep.ShouldBe(5);
		options.DrainTimeout.ShouldBe(TimeSpan.FromSeconds(10));
	}

	[Fact]
	public void Parse_RotationAndDrainArguments_AreApplied() {

		// Given: arguments setting a 2.5 MB limit, 3 kept files and a 4.5 second drain
		var args = new[] { "--log-max-mb", "2.5", "--log-keep", "3", "--drain-timeout", "4.5" };

		// When: the options are parsed
		var options = ProxyOptionsParser.Parse(args);

		// Then: the values are used (2.5 MB is 2,621,440 bytes)
		options.LogMaxBytes.ShouldBe(2_621_440);
		options.LogKeep.ShouldBe(3);
		options.DrainTimeout.ShouldBe(TimeSpan.FromSeconds(4.5));
	}

	[Fact]
	public void Parse_ZeroValues_TurnRotationOffKeepNothingAndStopAtOnce() {

		// Given: arguments that set the limit, the kept files and the drain to zero
		var args = new[] { "--log-max-mb", "0", "--log-keep", "0", "--drain-timeout", "0" };

		// When: the options are parsed
		var options = ProxyOptionsParser.Parse(args);

		// Then: all three are accepted as zero
		options.LogMaxBytes.ShouldBe(0);
		options.LogKeep.ShouldBe(0);
		options.DrainTimeout.ShouldBe(TimeSpan.Zero);
	}

	[Theory]
	[InlineData("--log-max-mb")]
	[InlineData("--log-max-mb", "-1")]
	[InlineData("--log-max-mb", "lots")]
	[InlineData("--log-keep")]
	[InlineData("--log-keep", "-1")]
	[InlineData("--log-keep", "5000")]
	[InlineData("--log-keep", "1.5")]
	[InlineData("--drain-timeout")]
	[InlineData("--drain-timeout", "-1")]
	[InlineData("--drain-timeout", "soon")]
	[InlineData("--drain-timeout", "99999")]
	public void Parse_InvalidRotationOrDrainArguments_Throws(params string[] args) {

		// Given: a missing, negative, non-numeric or out-of-range value (supplied by the test data)

		// When: the options are parsed
		var parse = () => ProxyOptionsParser.Parse(args);

		// Then: parsing fails with an ArgumentException
		Should.Throw<ArgumentException>(parse);
	}

	[Fact]
	public void Parse_NoHttp2Argument_TurnsHttp2OffAndItDefaultsToOn() {

		// Given: no arguments, and separately the --no-http2 argument
		var none = Array.Empty<string>();
		var off = new[] { "--no-http2" };

		// When: both are parsed
		var defaults = ProxyOptionsParser.Parse(none);
		var withFlag = ProxyOptionsParser.Parse(off);

		// Then: HTTP/2 is offered by default and not offered with the flag
		defaults.EnableHttp2.ShouldBeTrue();
		withFlag.EnableHttp2.ShouldBeFalse();
	}

	[Fact]
	public void Parse_UntrustCaArgument_IsSetAndDefaultsToOff() {

		// Given: no arguments, and separately the --untrust-ca argument
		var none = Array.Empty<string>();
		var untrust = new[] { "--untrust-ca" };

		// When: both are parsed
		var defaults = ProxyOptionsParser.Parse(none);
		var withFlag = ProxyOptionsParser.Parse(untrust);

		// Then: removing the trusted root is off by default and on with the flag
		defaults.UntrustCa.ShouldBeFalse();
		withFlag.UntrustCa.ShouldBeTrue();
	}

	[Fact]
	public void Parse_InterceptArguments_AreApplied() {

		// Given: arguments that turn interception on, bypass two hosts (one a wildcard), and choose a CA folder
		var args = new[] { "--intercept", "--intercept-bypass", "bank.example, *.secure.example", "--ca-dir", "my-ca" };

		// When: the options are parsed
		var options = ProxyOptionsParser.Parse(args);

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
		var parse = () => ProxyOptionsParser.Parse(args);

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
		var parse = () => ProxyOptionsParser.Parse(args);

		// Then: parsing fails with an ArgumentException
		Should.Throw<ArgumentException>(parse);
	}

	[Theory]
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
		var parse = () => ProxyOptionsParser.Parse(args);

		// Then: parsing fails with an ArgumentException
		Should.Throw<ArgumentException>(parse);
	}
}
