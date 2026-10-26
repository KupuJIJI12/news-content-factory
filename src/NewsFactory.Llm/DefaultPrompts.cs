using NewsFactory.Domain;

namespace NewsFactory.Llm;

public static class DefaultPrompts
{
    public static string Get(LlmTask task)
    {
        var name = $"NewsFactory.Llm.Prompts.{task.ToString().ToLowerInvariant()}.md";
        using var stream = typeof(DefaultPrompts).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Нет встроенного промпта {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
