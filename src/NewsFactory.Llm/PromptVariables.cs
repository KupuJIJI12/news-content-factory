using System.Text.RegularExpressions;
using NewsFactory.Domain;

namespace NewsFactory.Llm;

/// <summary>Which {{variables}} each task's prompt must / may use (the stages pass exactly these).</summary>
public static partial class PromptVariables
{
    private static readonly Dictionary<LlmTask, (string[] Required, string[] Optional)> Map = new()
    {
        [LlmTask.Relevance] = (["city", "title", "text"], []),
        [LlmTask.Clustering] = (["city", "title", "text", "stories"], []),
        [LlmTask.Scoring] = (["city", "sources"], []),
        [LlmTask.Rewrite] = (["city", "sources"], ["hint"]),
        [LlmTask.FactCheck] = (["sources", "draft"], []),
    };

    public static IReadOnlySet<string> Required(LlmTask task) => Map[task].Required.ToHashSet();

    public static IReadOnlySet<string> Allowed(LlmTask task) => Map[task].Required.Concat(Map[task].Optional).ToHashSet();

    public static IReadOnlyList<string> Validate(LlmTask task, string template)
    {
        var used = Placeholder().Matches(template).Select(m => m.Groups[1].Value).ToHashSet();
        var allowed = Allowed(task);
        return used.Where(v => !allowed.Contains(v)).Select(v => $"Неизвестная переменная {{{{{v}}}}}")
            .Concat(Required(task).Where(v => !used.Contains(v)).Select(v => $"Нет обязательной переменной {{{{{v}}}}}"))
            .ToList();
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex Placeholder();
}
