namespace NewsFactory.Llm.Tests;

public class LlmJsonTests
{
    [Fact]
    public void Parses_json_inside_markdown_fence_with_trailing_text()
    {
        var r = LlmJson.Parse<ScoringResult>("Вот ответ:\n```json\n{\"score\": 7, \"reason\": \"важно {для} всех\"}\n```\nНадеюсь, помог!");
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(7, r.Value!.Score);
        Assert.Equal("важно {для} всех", r.Value.Reason);
    }

    [Fact]
    public void Handles_escaped_quotes_and_braces_in_strings()
    {
        var r = LlmJson.Parse<RelevanceResult>("""{"relevant": false, "reason": "трасса \"Отрадный } Богатое\""} хвост""");
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal("трасса \"Отрадный } Богатое\"", r.Value!.Reason);
    }

    [Fact]
    public void Out_of_range_score_is_error() =>
        Assert.False(LlmJson.Parse<ScoringResult>("""{"score": 11, "reason": "x"}""").Ok);

    [Fact]
    public void Missing_required_field_is_error() =>
        Assert.False(LlmJson.Parse<ScoringResult>("""{"score": 5}""").Ok);

    [Fact]
    public void Wrong_hashtag_count_is_error() =>
        Assert.False(LlmJson.Parse<RewriteResult>(
            """{"title":"Заголовок","body":"%s","hashtags":["#a"]}""".Replace("%s", new string('т', 60))).Ok);

    [Fact]
    public void Nested_fact_issues_are_validated() =>
        Assert.False(LlmJson.Parse<FactCheckResult>("""{"issues":[{"claim":"x"}]}""").Ok);

    [Fact]
    public void Empty_fact_issues_are_ok() =>
        Assert.True(LlmJson.Parse<FactCheckResult>("""{"issues":[]}""").Ok);

    [Fact]
    public void Not_json_is_error_with_message() =>
        Assert.NotEmpty(LlmJson.Parse<ScoringResult>("не знаю").Errors);

    [Fact]
    public void Extra_validation_runs()
    {
        var r = LlmJson.Parse<ClusteringResult>("""{"storyId": 99, "reason":"x"}""",
            c => c.StoryId is 99 ? ["storyId 99 нет в списке"] : []);
        Assert.Equal(["storyId 99 нет в списке"], r.Errors);
    }
}
