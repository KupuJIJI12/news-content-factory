using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Text;
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

    /// <summary>
    /// Returns the first {...} block, honouring JSON strings: text around it (markdown fences, comments) is dropped.
    /// GigaChat sometimes breaks the ending (<c>}])</c> instead of <c>}]}</c>, or stops early): a closing bracket that
    /// does not belong is skipped, a missing one is added. The result is still parsed and validated as usual,
    /// so a repair never makes up missing fields.
    /// </summary>
    private static string? ExtractObject(string raw)
    {
        var start = raw.IndexOf('{');
        if (start < 0) return null;
        var sb = new StringBuilder();
        var open = new Stack<char>();
        var inString = false;
        for (var i = start; i < raw.Length; i++)
        {
            var c = raw[i];
            if (inString)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < raw.Length) sb.Append(raw[++i]);
                else if (c == '"') inString = false;
                continue;
            }
            switch (c)
            {
                case '"': inString = true; sb.Append(c); break;
                case '{': open.Push('}'); sb.Append(c); break;
                case '[': open.Push(']'); sb.Append(c); break;
                case '}' or ']':
                    if (!open.Contains(c)) continue; // a stray closer, e.g. "]" with no array open
                    while (open.Peek() != c) sb.Append(open.Pop()); // close what the model forgot inside
                    sb.Append(open.Pop());
                    if (open.Count == 0) return sb.ToString();
                    break;
                case ')': continue; // never valid outside a string
                default: sb.Append(c); break;
            }
        }
        if (inString) sb.Append('"');
        while (open.Count > 0) sb.Append(open.Pop());
        return sb.ToString();
    }

    private static LlmParseResult<T> Fail<T>(string error) => new(default, [error]);
}
