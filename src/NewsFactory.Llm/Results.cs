using System.ComponentModel.DataAnnotations;

namespace NewsFactory.Llm;

public sealed record RelevanceResult(
    bool Relevant,
    [property: Required(ErrorMessage = "Поле reason обязательно")] string Reason);

/// <summary>StoryId must be one of the candidates — checked by the caller via extraValidation.</summary>
public sealed record ClusteringResult(
    long? StoryId,
    [property: Required(ErrorMessage = "Поле reason обязательно")] string Reason,
    string? NewSummary);

public sealed record ScoringResult(
    [property: Range(1, 10, ErrorMessage = "Поле score должно быть от 1 до 10")] int Score,
    [property: Required(ErrorMessage = "Поле reason обязательно")] string Reason);

public sealed record RewriteResult(
    [property: Required(ErrorMessage = "Поле title обязательно"), MinLength(5, ErrorMessage = "Заголовок слишком короткий")] string Title,
    [property: Required(ErrorMessage = "Поле body обязательно"), MinLength(50, ErrorMessage = "Текст слишком короткий")] string Body,
    [property: Required(ErrorMessage = "Поле hashtags обязательно"),
               MinLength(3, ErrorMessage = "Нужно 3–4 хэштега"), MaxLength(4, ErrorMessage = "Нужно 3–4 хэштега")] string[] Hashtags);

public sealed record LlmFactIssue(
    [property: Required(ErrorMessage = "Поле claim обязательно")] string Claim,
    [property: Required(ErrorMessage = "Поле problem обязательно")] string Problem);

public sealed record FactCheckResult(
    [property: Required(ErrorMessage = "Поле issues обязательно")] LlmFactIssue[] Issues);
