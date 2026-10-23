using NewsFactory.Domain;

namespace NewsFactory.Llm;

public sealed class LlmResponseInvalidException(LlmTask task, IReadOnlyList<string> errors)
    : Exception($"LLM ({task}) дважды вернула невалидный ответ: {string.Join("; ", errors)}")
{
    public LlmTask Task { get; } = task;
    public IReadOnlyList<string> Errors { get; } = errors;
}

public sealed class LlmBudgetExceededException(DateTimeOffset resumeAt)
    : Exception($"Дневной бюджет токенов исчерпан, продолжение после {resumeAt:O}")
{
    public DateTimeOffset ResumeAt { get; } = resumeAt;
}
