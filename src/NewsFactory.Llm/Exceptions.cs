using NewsFactory.Domain;

namespace NewsFactory.Llm;

public sealed class LlmResponseInvalidException(LlmTask task, IReadOnlyList<string> errors)
    : Exception($"LLM ({task}) дважды вернула невалидный ответ: {string.Join("; ", errors)}")
{
    public LlmTask Task { get; } = task;
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>The LLM cannot be used right now; the work should be retried at <see cref="ResumeAt"/> instead of failing.</summary>
public abstract class LlmPostponedException(DateTimeOffset resumeAt, string message, Exception? inner = null) : Exception(message, inner)
{
    public DateTimeOffset ResumeAt { get; } = resumeAt;
}

public sealed class LlmBudgetExceededException(DateTimeOffset resumeAt)
    : LlmPostponedException(resumeAt, $"Дневной бюджет токенов исчерпан, продолжение после {resumeAt:O}");

/// <summary>The provider kept answering 429 Too Many Requests after the throttle's pauses.</summary>
public sealed class LlmRateLimitedException(DateTimeOffset resumeAt, Exception inner)
    : LlmPostponedException(resumeAt, $"LLM ограничила частоту запросов (429), продолжение после {resumeAt:O}", inner);
