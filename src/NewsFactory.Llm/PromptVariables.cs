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

    private static readonly Dictionary<string, string> Descriptions = new()
    {
        ["city"] = "Название города, например «Отрадный»",
        ["title"] = "Заголовок новости из источника",
        ["text"] = "Текст новости из источника",
        ["stories"] = "Список недавних сюжетов, к которым новость может относиться (номер и краткое описание)",
        ["sources"] = "Тексты всех источников сюжета с названиями источников и датами",
        ["draft"] = "Готовый пост (заголовок и текст), который нужно проверить",
        ["hint"] = "Пожелание модератора к новой версии; пусто, если пожелания нет",
    };

    private static readonly Dictionary<LlmTask, string> TaskDescriptions = new()
    {
        [LlmTask.Relevance] = "Решает, относится ли новость регионального источника к городу",
        [LlmTask.Clustering] = "Решает, про тот же ли случай новость, что и один из недавних сюжетов, чтобы склеить их",
        [LlmTask.Scoring] = "Оценивает интерес сюжета для жителей по шкале от 1 до 10",
        [LlmTask.Rewrite] = "Пишет пост по источникам сюжета в редакционном стиле",
        [LlmTask.FactCheck] = "Сверяет готовый пост с источниками и ищет неточности",
    };

    public sealed record Variable(string Name, string Description, bool Required);

    /// <summary>Variables of a task's prompt with what the system substitutes for each (shown in the prompt editor).</summary>
    public static IReadOnlyList<Variable> For(LlmTask task) =>
        Map[task].Required.Select(v => new Variable(v, Descriptions[v], true))
            .Concat(Map[task].Optional.Select(v => new Variable(v, Descriptions[v], false)))
            .ToList();

    public static string DescribeTask(LlmTask task) => TaskDescriptions[task];

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
