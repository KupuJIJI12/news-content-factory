using System.Text.RegularExpressions;

namespace NewsFactory.Llm;

public static partial class PromptRenderer
{
    public static string Render(string template, IReadOnlyDictionary<string, string> vars) =>
        Placeholder().Replace(template, m => vars.TryGetValue(m.Groups[1].Value, out var v)
            ? v
            : throw new KeyNotFoundException($"В шаблоне есть переменная {{{{{m.Groups[1].Value}}}}}, но значение не передано"));

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex Placeholder();
}
