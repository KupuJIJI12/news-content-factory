using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace NewsFactory.Llm;

/// <summary>
/// JSON schema of an LLM result type (for providers with structured output) and a filled-in example of it
/// (for the prompt: models without structured output, e.g. GigaChat, copy a schema back instead of answering).
/// </summary>
public static class LlmExample
{
    // Strict number handling: the Web defaults allow numbers as strings, which makes the schema say "string | integer".
    private static readonly JsonSerializerOptions SchemaOptions =
        new(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.Strict, TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    public static JsonNode SchemaFor<T>() => SchemaFor(typeof(T));

    public static JsonNode SchemaFor(Type type) => JsonSchemaExporter.GetJsonSchemaAsNode(SchemaOptions, type);

    public static JsonNode For<T>() => FromSchema(SchemaFor<T>());

    public static JsonNode FromSchema(JsonNode schema) => Build(schema) ?? new JsonObject();

    private static JsonNode? Build(JsonNode? schema)
    {
        if (schema is not JsonObject s) return JsonValue.Create("…");
        if (s["enum"] is JsonArray values && values.Count > 0) return values[0]?.DeepClone();
        return MainType(s["type"]) switch
        {
            "object" => new JsonObject((s["properties"] as JsonObject ?? [])
                .Select(p => KeyValuePair.Create(p.Key, Build(p.Value)))),
            "array" => new JsonArray(Build(s["items"])),
            "integer" => JsonValue.Create(Mid(s, 5)),
            "number" => JsonValue.Create((double)Mid(s, 5)),
            "boolean" => JsonValue.Create(true),
            "null" => null,
            _ => JsonValue.Create("…"),
        };
    }

    /// <summary>A neutral value the model will not mistake for an answer: the middle of the range, if one is declared.</summary>
    private static int Mid(JsonObject s, int fallback) =>
        s["minimum"] is { } min && s["maximum"] is { } max ? (min.GetValue<int>() + max.GetValue<int>()) / 2 : fallback;

    /// <summary>"type" may be a string or an array such as ["integer", "null"]: take the first non-null one.</summary>
    private static string? MainType(JsonNode? type) => type switch
    {
        JsonValue v => v.GetValue<string>(),
        JsonArray a => a.Select(t => t?.GetValue<string>()).FirstOrDefault(t => t != "null") ?? "null",
        _ => null,
    };
}
