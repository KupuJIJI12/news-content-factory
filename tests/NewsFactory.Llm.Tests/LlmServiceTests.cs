using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;
using NewsFactory.Llm.Clients;
using NewsFactory.TestSupport;

namespace NewsFactory.Llm.Tests;

internal sealed class ScriptedChatClient(params string[] answers) : IChatCompletionClient, ILlmClientResolver
{
    private int _i;
    public List<ChatRequest> Requests { get; } = [];

    public Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(new ChatCompletion(answers[Math.Min(_i++, answers.Length - 1)], 100, 20));
    }

    public IChatCompletionClient Resolve(LlmProvider provider) => this;
}

[Collection("db")]
public class LlmServiceTests(PostgresFixture db)
{
    private static readonly TimeZoneInfo Samara = TimeZoneInfo.FindSystemTimeZoneById("Europe/Samara");
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-24T10:00:00Z"));

    private async Task SeedAsync(params (int Version, bool Active, string Text)[] templates)
    {
        await db.ResetAsync();
        await using var ctx = db.CreateContext(_time);
        var provider = new LlmProvider { Name = "YandexGPT", Kind = LlmProviderKind.OpenAiCompatible, BaseUrl = "https://x", ApiKeyEnvVar = "K" };
        ctx.Add(new LlmTaskSetting { Task = LlmTask.Scoring, Provider = provider, Model = "gpt://f/yandexgpt-lite/latest", Temperature = 0.1 });
        foreach (var (v, a, t) in templates.Length > 0 ? templates : [(1, true, "Оцени новость города {{city}}:\n{{sources}}")])
            ctx.Add(new PromptTemplate { Task = LlmTask.Scoring, Version = v, IsActive = a, Text = t });
        await ctx.SaveChangesAsync();
    }

    private LlmService Service(NewsFactoryDbContext ctx, ScriptedChatClient client) =>
        new(ctx, client, new LlmBudgetGuard(ctx, _time, Samara));

    private static readonly Dictionary<string, string> Vars = new() { ["city"] = "Отрадный", ["sources"] = "текст" };

    [Fact]
    public async Task Renders_active_template_routes_to_task_model_and_logs_call()
    {
        await SeedAsync();
        var client = new ScriptedChatClient("""{"score":7,"reason":"ok"}""");
        await using var ctx = db.CreateContext(_time);
        var r = await Service(ctx, client).CompleteAsync<ScoringResult>(LlmTask.Scoring, Vars, new LlmCallContext(StoryId: 5));

        Assert.Equal(7, r.Score);
        var req = client.Requests.Single();
        Assert.Equal("gpt://f/yandexgpt-lite/latest", req.Model);
        Assert.Equal(0.1, req.Temperature);
        Assert.Equal("system", req.Messages[0].Role);
        Assert.Contains("JSON", req.Messages[0].Content);
        Assert.Equal("Оцени новость города Отрадный:\nтекст", req.Messages[1].Content);
        Assert.NotNull(req.JsonSchema);

        var call = await ctx.LlmCalls.SingleAsync();
        Assert.True(call.Success);
        Assert.Equal((LlmTask.Scoring, 100, 20, 5L), (call.Task, call.PromptTokens, call.CompletionTokens, call.StoryId!.Value));
        Assert.NotNull(JsonNode.Parse(call.Request));
    }

    [Fact]
    public async Task Retries_once_with_validation_error_then_succeeds()
    {
        await SeedAsync();
        var client = new ScriptedChatClient("""{"score":11,"reason":"x"}""", """{"score":8,"reason":"ok"}""");
        await using var ctx = db.CreateContext(_time);
        var r = await Service(ctx, client).CompleteAsync<ScoringResult>(LlmTask.Scoring, Vars, new LlmCallContext());

        Assert.Equal(8, r.Score);
        var retry = client.Requests[1].Messages;
        Assert.Equal("assistant", retry[^2].Role);
        Assert.Contains("Ответ не прошёл проверку", retry[^1].Content);
        Assert.Equal([false, true], await ctx.LlmCalls.OrderBy(c => c.Id).Select(c => c.Success).ToListAsync());
    }

    [Fact]
    public async Task Throws_after_second_invalid_answer()
    {
        await SeedAsync();
        var client = new ScriptedChatClient("""{"score":11,"reason":"x"}""");
        await using var ctx = db.CreateContext(_time);
        var ex = await Assert.ThrowsAsync<LlmResponseInvalidException>(() =>
            Service(ctx, client).CompleteAsync<ScoringResult>(LlmTask.Scoring, Vars, new LlmCallContext()));
        Assert.Equal(LlmTask.Scoring, ex.Task);
        Assert.Equal(2, await ctx.LlmCalls.CountAsync(c => !c.Success));
    }

    [Fact]
    public async Task Extra_validation_failure_triggers_retry()
    {
        await SeedAsync();
        var client = new ScriptedChatClient("""{"score":3,"reason":"x"}""", """{"score":4,"reason":"x"}""");
        await using var ctx = db.CreateContext(_time);
        var r = await Service(ctx, client).CompleteAsync<ScoringResult>(LlmTask.Scoring, Vars, new LlmCallContext(),
            s => s.Score == 3 ? ["три нельзя"] : []);
        Assert.Equal(4, r.Score);
        Assert.Contains("три нельзя", client.Requests[1].Messages[^1].Content);
    }

    [Fact]
    public async Task Uses_highest_active_template_version()
    {
        await SeedAsync((1, true, "v1"), (2, true, "v2"), (3, false, "v3"));
        var client = new ScriptedChatClient("""{"score":7,"reason":"ok"}""");
        await using var ctx = db.CreateContext(_time);
        await Service(ctx, client).CompleteAsync<ScoringResult>(LlmTask.Scoring, Vars, new LlmCallContext());
        Assert.Equal("v2", client.Requests.Single().Messages[1].Content);
    }
}

[Collection("db")]
public class LlmBudgetGuardTests(PostgresFixture db)
{
    private static readonly TimeZoneInfo Samara = TimeZoneInfo.FindSystemTimeZoneById("Europe/Samara");

    private async Task SeedCallsAsync(DateTimeOffset at, int tokens, int budget)
    {
        await db.ResetAsync();
        await using var ctx = db.CreateContext(new FakeTimeProvider(at));
        ctx.Add(new AppSetting { Key = "DailyTokenBudget", Value = budget.ToString() });
        ctx.Add(new LlmCall { Task = LlmTask.Rewrite, Model = "m", Request = "[]", PromptTokens = tokens / 2, CompletionTokens = tokens - tokens / 2, Success = true });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Throws_with_next_local_midnight_when_budget_spent()
    {
        var now = DateTimeOffset.Parse("2026-10-24T10:00:00Z");
        await SeedCallsAsync(now.AddHours(-1), 1000, 1000);
        await using var ctx = db.CreateContext();
        var ex = await Assert.ThrowsAsync<LlmBudgetExceededException>(() =>
            new LlmBudgetGuard(ctx, new FakeTimeProvider(now), Samara).EnsureAvailableAsync(default));
        Assert.Equal(DateTimeOffset.Parse("2026-10-24T20:00:00Z"), ex.ResumeAt);
    }

    [Fact]
    public async Task Yesterdays_calls_do_not_count()
    {
        var now = DateTimeOffset.Parse("2026-10-24T10:00:00Z");
        // 2026-10-23T19:30Z = 23:30 по Самаре предыдущих суток
        await SeedCallsAsync(DateTimeOffset.Parse("2026-10-23T19:30:00Z"), 5000, 1000);
        await using var ctx = db.CreateContext();
        await new LlmBudgetGuard(ctx, new FakeTimeProvider(now), Samara).EnsureAvailableAsync(default);
    }

    [Fact]
    public async Task Default_budget_applies_without_setting()
    {
        await db.ResetAsync();
        await using var ctx = db.CreateContext();
        await new LlmBudgetGuard(ctx, TimeProvider.System, Samara).EnsureAvailableAsync(default);
    }
}

public class DefaultPromptsTests
{
    [Theory]
    [InlineData(LlmTask.Relevance, new[] { "city", "title", "text" })]
    [InlineData(LlmTask.Clustering, new[] { "city", "title", "text", "stories" })]
    [InlineData(LlmTask.Scoring, new[] { "city", "sources" })]
    [InlineData(LlmTask.Rewrite, new[] { "city", "sources", "hint" })]
    [InlineData(LlmTask.FactCheck, new[] { "sources", "draft" })]
    public void Every_default_prompt_renders_with_its_variables(LlmTask task, string[] vars)
    {
        var text = DefaultPrompts.Get(task);
        var rendered = PromptRenderer.Render(text, vars.ToDictionary(v => v, v => "<" + v + ">"));
        Assert.All(vars, v => Assert.Contains("<" + v + ">", rendered));
    }

    [Fact]
    public void Rewrite_prompt_contains_editorial_style_verbatim()
    {
        var text = DefaultPrompts.Get(LlmTask.Rewrite);
        Assert.Contains("Ты — выпускающий редактор сети городских новостных каналов.", text);
        Assert.Contains("Избегай канцеляризмов", text);
    }
}
