using Microsoft.Extensions.Configuration;
using NewsFactory.Api;
using NewsFactory.Domain;
using NewsFactory.Llm;
using NewsFactory.Llm.Clients;
using NewsFactory.TestSupport;

namespace NewsFactory.LiveTests;

[Collection("db")]
public class YandexLiveTests(PostgresFixture db)
{
    private sealed class SimpleFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { Timeout = TimeSpan.FromMinutes(2) };
    }

    [Fact]
    public async Task Rewrites_fixture_story()
    {
        Env.Require("YANDEX_API_KEY", "YANDEX_FOLDER_ID");
        await db.ResetAsync();
        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        await using var ctx = db.CreateContext();
        await SeedData.ApplyAsync(ctx, config, default);

        var samara = TimeZoneInfo.FindSystemTimeZoneById("Europe/Samara");
        var llm = new LlmService(ctx, new LlmClientFactory(new SimpleFactory(), config, TimeProvider.System),
            new LlmBudgetGuard(ctx, TimeProvider.System, samara));
        var sources = "### Источник 1: otradny.org\nВ Отрадном поздравили работников транспорта\n" + await File.ReadAllTextAsync("Fixtures/story.txt");

        var score = await llm.CompleteAsync<ScoringResult>(LlmTask.Scoring,
            new Dictionary<string, string> { ["city"] = "Отрадный", ["sources"] = sources }, new LlmCallContext());
        Env.Log($"Оценка: {score.Score} — {score.Reason}");

        var rewrite = await llm.CompleteAsync<RewriteResult>(LlmTask.Rewrite,
            new Dictionary<string, string> { ["city"] = "Отрадный", ["sources"] = sources, ["hint"] = "" }, new LlmCallContext());
        Env.Log($"Заголовок: {rewrite.Title}\n\n{rewrite.Body}\n\n{string.Join(' ', rewrite.Hashtags)}");

        var check = await llm.CompleteAsync<FactCheckResult>(LlmTask.FactCheck,
            new Dictionary<string, string> { ["sources"] = sources, ["draft"] = rewrite.Title + "\n\n" + rewrite.Body }, new LlmCallContext());
        foreach (var issue in check.Issues) Env.Log($"Замечание: «{issue.Claim}» — {issue.Problem}");

        Assert.InRange(score.Score, 1, 10);
        Assert.NotEmpty(rewrite.Title);
    }
}
