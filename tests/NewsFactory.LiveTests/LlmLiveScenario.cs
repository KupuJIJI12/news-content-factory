using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NewsFactory.Api;
using NewsFactory.Domain;
using NewsFactory.Llm;
using NewsFactory.Llm.Clients;
using NewsFactory.TestSupport;

namespace NewsFactory.LiveTests;

/// <summary>Scoring → rewrite → fact check of a real Otradny story through a given provider; prints results.</summary>
internal static class LlmLiveScenario
{
    private sealed class SimpleFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { Timeout = TimeSpan.FromMinutes(2) };
    }

    public static async Task RunAsync(string providerName, IReadOnlyDictionary<LlmTask, string> models)
    {
        // Database is created only after the caller's variables check, so the test skips cleanly without Docker/Postgres.
        var db = new PostgresFixture();
        await db.InitializeAsync();
        try
        {
            var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            await using var ctx = db.CreateContext();
            await SeedData.ApplyAsync(ctx, config, default);

            var provider = await ctx.LlmProviders.SingleAsync(p => p.Name == providerName);
            foreach (var setting in await ctx.LlmTaskSettings.ToListAsync())
            {
                setting.ProviderId = provider.Id;
                setting.Model = models[setting.Task];
            }
            await ctx.SaveChangesAsync();

            var samara = TimeZoneInfo.FindSystemTimeZoneById("Europe/Samara");
            var llm = new LlmService(ctx, db.CreateFactory(), new LlmClientFactory(new SimpleFactory(), config, TimeProvider.System),
                new LlmBudgetGuard(ctx, TimeProvider.System, samara));
            var sources = "### Источник 1: otradny.org\nВ Отрадном поздравили работников транспорта\n"
                          + await File.ReadAllTextAsync("Fixtures/story.txt");

            var score = await llm.CompleteAsync<ScoringResult>(LlmTask.Scoring,
                new Dictionary<string, string> { ["city"] = "Отрадный", ["sources"] = sources }, new LlmCallContext());
            Env.Log($"[{providerName}] Оценка: {score.Score} — {score.Reason}");

            var rewrite = await llm.CompleteAsync<RewriteResult>(LlmTask.Rewrite,
                new Dictionary<string, string> { ["city"] = "Отрадный", ["sources"] = sources, ["hint"] = "" }, new LlmCallContext());
            Env.Log($"[{providerName}] Заголовок: {rewrite.Title}\n\n{rewrite.Body}\n\n{string.Join(' ', rewrite.Hashtags)}");

            var check = await llm.CompleteAsync<FactCheckResult>(LlmTask.FactCheck,
                new Dictionary<string, string> { ["sources"] = sources, ["draft"] = rewrite.Title + "\n\n" + rewrite.Body }, new LlmCallContext());
            foreach (var issue in check.Issues) Env.Log($"[{providerName}] Замечание: «{issue.Claim}» — {issue.Problem}");

            var tokens = await ctx.LlmCalls.SumAsync(c => c.PromptTokens + c.CompletionTokens);
            Env.Log($"[{providerName}] Токенов за прогон: {tokens}");

            Assert.InRange(score.Score, 1, 10);
            Assert.NotEmpty(rewrite.Title);
        }
        finally
        {
            await db.DisposeAsync();
        }
    }
}
