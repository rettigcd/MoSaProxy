using System.Text;
using System.Text.Json.Nodes;

namespace MoSaProxy.Tests;

/// <summary>Turns the API's OpenAPI document into the readable endpoint reference (<c>docs/api/endpoints.md</c>). Used by <see cref="ApiDocument_Tests"/>.</summary>
static class ApiMarkdown {

	public static string From(JsonNode document) {
		var text = new StringBuilder();
		text.Append("# MoSaProxy local API: endpoints\n\n");
		text.Append("Generated from [openapi.json](openapi.json) by the `ApiDocument_Tests` test: do not edit by hand. ");
		text.Append("To regenerate both files after changing an endpoint, run the tests with the environment variable `MOSAPROXY_UPDATE_API_DOCS=1`.\n\n");
		text.Append("Every request must carry the per-run token in the `X-MoSaProxy-Token` header (or a `token` query parameter), a `Host` header naming the listener, ");
		text.Append("and, if it has an `Origin`, the listener's own. Otherwise it is refused with 401 or 403.\n\n");

		var operations = new List<(string Tag, string Path, string Method, JsonNode Operation)>();
		foreach (var (path, item) in document["paths"]!.AsObject())
			foreach (var (method, operation) in item!.AsObject())
				operations.Add((operation!["tags"]?[0]?.GetValue<string>() ?? "Other", path, method, operation));

		foreach (var group in operations.GroupBy(o => o.Tag)) {
			text.Append($"## {group.Key}\n\n");
			foreach (var (_, path, method, operation) in group) {
				text.Append($"### `{method.ToUpperInvariant()} {path}`\n\n");
				text.Append($"**{operation["summary"]}** (`{operation["operationId"]}`)\n\n");
				if (operation["description"] is { } description) text.Append(description.GetValue<string>()).Append("\n\n");

				if (operation["parameters"] is JsonArray parameters && parameters.Count > 0) {
					text.Append("| Parameter | In | Type | Required |\n|---|---|---|---|\n");
					foreach (var parameter in parameters)
						text.Append($"| `{parameter!["name"]}` | {parameter["in"]} | {TypeOf(parameter["schema"])} | {(parameter["required"]?.GetValue<bool>() == true ? "yes" : "no")} |\n");
					text.Append('\n');
				}
				if (operation["requestBody"]?["content"] is JsonObject body)
					foreach (var (contentType, media) in body) text.Append($"Request body: `{contentType}`, {TypeOf(media!["schema"])}\n\n");

				text.Append("| Response | Content |\n|---|---|\n");
				foreach (var (status, response) in operation["responses"]!.AsObject()) {
					var contents = response!["content"] is JsonObject content ? string.Join("<br>", content.Select(c => $"`{c.Key}` {TypeOf(c.Value!["schema"])}")) : "";
					text.Append($"| {status} {response["description"]} | {contents} |\n");
				}
				text.Append('\n');
			}
		}

		text.Append("## Models\n\n");
		foreach (var (name, schema) in document["components"]!["schemas"]!.AsObject().OrderBy(s => s.Key, StringComparer.Ordinal)) {
			text.Append($"### {name}\n\n");
			if (schema!["properties"] is JsonObject properties) {
				text.Append("| Property | Type |\n|---|---|\n");
				foreach (var (property, definition) in properties) text.Append($"| `{property}` | {TypeOf(definition)} |\n");
				text.Append('\n');
			}
			else text.Append($"{TypeOf(schema)}\n\n");
		}
		return text.ToString();
	}

	/// <summary>A short name for a schema: a model name, a primitive, an array or a map, with ? for one that may be null.</summary>
	static string TypeOf(JsonNode? schema) {
		if (schema is null) return "";
		if (schema["$ref"] is { } reference) return reference.GetValue<string>().Split('/')[^1];
		if (schema["oneOf"] is JsonArray options) return string.Join(" or ", options.Select(TypeOf));
		string type = "";
		bool nullable = false;
		if (schema["type"] is JsonArray types) {
			nullable = types.Any(t => t!.GetValue<string>() == "null");
			// (a number that may also be written as a string is just a number to a reader of this reference)
			type = types.Select(t => t!.GetValue<string>()).FirstOrDefault(t => t is not ("null" or "string") || types.Count == 1) ?? "string";
		}
		else type = schema["type"]?.GetValue<string>() ?? "any";
		if (type == "array") type = TypeOf(schema["items"]) + "[]";
		else if (type == "object" && schema["additionalProperties"] is { } values) type = "map of " + TypeOf(values);
		else if (schema["format"] is { } format) type += $" ({format.GetValue<string>()})";
		return nullable ? type + "?" : type;
	}
}
