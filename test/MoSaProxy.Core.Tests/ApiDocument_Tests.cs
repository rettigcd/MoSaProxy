using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;

namespace MoSaProxy.Tests;

/// <summary>
/// The API describes itself in OpenAPI, and the checked-in documents in <c>docs/api</c> are produced from that description. These tests
/// fetch the description from the real listener and fail when the checked-in files no longer match. Set the environment variable
/// <c>MOSAPROXY_UPDATE_API_DOCS=1</c> to rewrite them instead.
/// </summary>
public class ApiDocument_Tests {
	static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	/// <summary>The repository's <c>docs/api</c> folder, found by walking up from the test's folder to the one that holds docs/plan.md.</summary>
	static string DocsFolder() {
		for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
			if (File.Exists(Path.Combine(folder.FullName, "docs", "plan.md"))) return Path.Combine(folder.FullName, "docs", "api");
		throw new DirectoryNotFoundException("Could not find the repository's docs folder above " + AppContext.BaseDirectory);
	}

	static async Task<JsonNode> FetchDocumentAsync() {
		await using var api = await TestApi.StartAsync();
		string json = await api.Client.GetStringAsync("openapi/v1.json");
		return JsonNode.Parse(json)!;
	}

	static string Normalize(string text) => text.Replace("\r\n", "\n");

	/// <summary>Writes the file when asked to, otherwise reads what is checked in.</summary>
	static string CheckedIn(string name, string generated) {
		string path = Path.Combine(DocsFolder(), name);
		if (Environment.GetEnvironmentVariable("MOSAPROXY_UPDATE_API_DOCS") == "1") {
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, generated);
		}
		return File.Exists(path) ? Normalize(File.ReadAllText(path)) : "";
	}

	[Fact]
	public async Task OpenApiDocument_FromTheRealListener_MatchesTheCheckedInFile() {

		// Given: the OpenAPI description that the running API serves
		var document = await FetchDocumentAsync();
		string generated = Normalize(document.ToJsonString(Pretty)) + "\n";   // (the serializer writes the machine's own line breaks)

		// When: it is compared with docs/api/openapi.json
		string checkedIn = CheckedIn("openapi.json", generated);

		// Then: they are the same; if not, an endpoint changed and the files need regenerating (MOSAPROXY_UPDATE_API_DOCS=1)
		checkedIn.ShouldBe(generated, "docs/api/openapi.json is out of date. Run the tests with MOSAPROXY_UPDATE_API_DOCS=1 to regenerate it.");
	}

	[Fact]
	public async Task EndpointReference_FromTheRealListener_MatchesTheCheckedInFile() {

		// Given: the endpoint reference written from the OpenAPI description
		var document = await FetchDocumentAsync();
		string generated = ApiMarkdown.From(document);

		// When: it is compared with docs/api/endpoints.md
		string checkedIn = CheckedIn("endpoints.md", generated);

		// Then: they are the same; if not, an endpoint changed and the files need regenerating (MOSAPROXY_UPDATE_API_DOCS=1)
		checkedIn.ShouldBe(generated, "docs/api/endpoints.md is out of date. Run the tests with MOSAPROXY_UPDATE_API_DOCS=1 to regenerate it.");
	}

	[Fact]
	public async Task OpenApiDocument_EveryEndpoint_HasANameASummaryADescriptionAndATag() {

		// Given: the OpenAPI description that the running API serves
		var document = await FetchDocumentAsync();

		// When: every operation in it is looked at
		var operations = document["paths"]!.AsObject().SelectMany(p => p.Value!.AsObject().Select(m => (Name: $"{m.Key.ToUpperInvariant()} {p.Key}", Operation: m.Value!))).ToList();

		// Then: there are some, and each has an operation id, a summary, a description and a tag, so the reference is useful
		operations.ShouldNotBeEmpty();
		foreach (var (name, operation) in operations) {
			operation["operationId"].ShouldNotBeNull(name + " has no operation id");
			operation["summary"].ShouldNotBeNull(name + " has no summary");
			operation["description"].ShouldNotBeNull(name + " has no description");
			operation["tags"]!.AsArray().Count.ShouldBeGreaterThan(0, name + " has no tag");
		}
	}

	[Fact]
	public async Task OpenApiDocument_IsOpenApi3_WithFixedInfoAndNoServers() {

		// Given: the OpenAPI description that the running API serves
		var document = await FetchDocumentAsync();

		// When: its top level is read
		string version = document["openapi"]!.GetValue<string>();

		// Then: it is OpenAPI 3.x with the fixed title and no server address, which would change with the port
		version.ShouldStartWith("3.");
		document["info"]!["title"]!.GetValue<string>().ShouldBe("MoSaProxy local API");
		document["servers"].ShouldBeNull();
	}
}
