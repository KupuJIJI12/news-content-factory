using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NewsFactory.Llm;

public sealed record LlmParseResult<T>(T? Value, IReadOnlyList<string> Errors)
{
    public bool Ok => Errors.Count == 0;
}

public static class LlmJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static LlmParseResult<T> Parse<T>(string raw, Func<T, IEnumerable<string>>? extraValidation = null)
    {
        var json = ExtractObject(raw);
        if (json is null) return Fail<T>("Ответ не содержит JSON-объекта");

        var result = ParseObject(json, extraValidation);
        if (result.Ok) return result;

        // Models without structured output sometimes answer in the shape of a JSON schema:
        // {"type":"object","properties":{...}}. Accept the data if it sits in "properties", otherwise say so.
        JsonNode? root;
        try { root = JsonNode.Parse(json); } catch (JsonException) { return result; }
        if (root is JsonObject obj && obj["properties"] is JsonObject props && obj.ContainsKey("type"))
        {
            var inner = ParseObject(props.ToJsonString(), extraValidation);
            if (inner.Ok) return inner;
            return new LlmParseResult<T>(default,
                ["Пришла JSON-схема вместо ответа: верни сами значения полей, без \"type\" и \"properties\"", .. result.Errors]);
        }
        return result;
    }

    private static LlmParseResult<T> ParseObject<T>(string json, Func<T, IEnumerable<string>>? extraValidation)
    {
        T? value;
        try { value = JsonSerializer.Deserialize<T>(json, Options); }
        catch (JsonException ex) { return Fail<T>($"Ответ не является корректным JSON: {ex.Message}"); }
        if (value is null) return Fail<T>("Пустой JSON");

        var errors = new List<string>();
        Validate(value, errors);
        if (errors.Count == 0 && extraValidation is not null) errors.AddRange(extraValidation(value));
        return new LlmParseResult<T>(errors.Count == 0 ? value : default, errors);
    }

    private static void Validate(object value, List<string> errors)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
        errors.AddRange(results.Select(r => r.ErrorMessage ?? "Ошибка проверки"));
        foreach (var prop in value.GetType().GetProperties())
        {
            if (prop.PropertyType == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(prop.PropertyType)) continue;
            if (prop.GetValue(value) is not IEnumerable items) continue;
            foreach (var item in items)
                if (item is not null && item.GetType().Namespace == typeof(LlmJson).Namespace) Validate(item, errors);
        }
    }

    /// <summary>Strips markdown fences and returns the first balanced {...} block, honouring JSON strings.</summary>
    private static string? ExtractObject(string raw)
    {
        var start = raw.IndexOf('{');
        if (start < 0) return null;
        int depth = 0;
        var inString = false;
        for (var i = start; i < raw.Length; i++)
        {
            var c = raw[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}' when --depth == 0: return raw[start..(i + 1)];
            }
        }
        return null;
    }

    private static LlmParseResult<T> Fail<T>(string error) => new(default, [error]);
}
