using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

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
