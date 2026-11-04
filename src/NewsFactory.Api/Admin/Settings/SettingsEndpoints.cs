using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;
using NewsFactory.Llm;

namespace NewsFactory.Api.Admin.Settings;

public static class SettingsEndpoints
{
    public const int MinBudget = 1000;

    public static void MapSettingsEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/settings");

        g.MapGet("/channels", async (NewsFactoryDbContext db, CancellationToken ct) =>
            (await db.Channels.Include(c => c.City).OrderBy(c => c.Id).ToListAsync(ct)).Select(c => ChannelRules.ToDto(c, c.City!)).ToList());
        g.MapPut("/channels/{id:long}", async (long id, ChannelSettingsInput input, NewsFactoryDbContext db, CancellationToken ct) =>
        {
            var c = await db.Channels.Include(x => x.City).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Problems.NotFound("Канал");
            if (ChannelRules.Validate(input, nameRequired: true) is { Count: > 0 } errors) return Problems.Validation("Проверьте настройки канала", errors);
            ChannelRules.Apply(c, input, defaultName: c.Name);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ChannelRules.ToDto(c, c.City!));
        });

        g.MapGet("/secrets", (IConfiguration config) => new SecretsDto(
            Has(config, "MAX_BOT_TOKEN"), Has(config, "VK_SERVICE_TOKEN"), Has(config, "GIGACHAT_AUTH_KEY"), Has(config, "YANDEX_API_KEY")));

        g.MapGet("/llm/providers", async (NewsFactoryDbContext db, IConfiguration config, CancellationToken ct) =>
            (await db.LlmProviders.OrderBy(p => p.Id).ToListAsync(ct)).Select(p => ToDto(p, config)).ToList());
        g.MapPost("/llm/providers", async (LlmProviderInput input, NewsFactoryDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            if (ValidateProvider(input) is { Count: > 0 } errors) return Problems.Validation("Проверьте провайдера", errors);
            if (await db.LlmProviders.AnyAsync(p => p.Name == input.Name!.Trim(), ct)) return Problems.Conflict("conflict", "Провайдер с таким именем уже есть");
            var p = new LlmProvider();
            Apply(p, input);
            db.LlmProviders.Add(p);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/settings/llm/providers/{p.Id}", ToDto(p, config));
        });
        g.MapPut("/llm/providers/{id:long}", async (long id, LlmProviderInput input, NewsFactoryDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            var p = await db.LlmProviders.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return Problems.NotFound("Провайдер");
            if (ValidateProvider(input) is { Count: > 0 } errors) return Problems.Validation("Проверьте провайдера", errors);
            if (await db.LlmProviders.AnyAsync(x => x.Name == input.Name!.Trim() && x.Id != id, ct))
                return Problems.Conflict("conflict", "Провайдер с таким именем уже есть");
            Apply(p, input);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(p, config));
        });

        g.MapGet("/llm/tasks", async (NewsFactoryDbContext db, CancellationToken ct) =>
            (await db.LlmTaskSettings.Include(t => t.Provider).OrderBy(t => t.Task).ToListAsync(ct))
            .Select(t => new LlmTaskDto(t.Task, t.ProviderId, t.Provider!.Name, t.Model, t.Temperature)).ToList());
        g.MapPut("/llm/tasks/{task}", async (LlmTask task, LlmTaskInput input, NewsFactoryDbContext db, CancellationToken ct) =>
        {
            var provider = await db.LlmProviders.SingleOrDefaultAsync(p => p.Id == input.ProviderId, ct);
            var errors = new Dictionary<string, string[]>();
            if (provider is null) errors["providerId"] = ["Провайдер не найден"];
            else if (input.Model is null || !provider.Models.Contains(input.Model)) errors["model"] = ["Модели нет в списке провайдера"];
            if (input.Temperature is < 0 or > 1) errors["temperature"] = ["Температура — от 0 до 1"];
            if (errors.Count > 0) return Problems.Validation("Проверьте настройки задачи", errors);
            var setting = await db.LlmTaskSettings.SingleOrDefaultAsync(t => t.Task == task, ct);
            if (setting is null) db.LlmTaskSettings.Add(setting = new LlmTaskSetting { Task = task });
            setting.ProviderId = provider!.Id;
            setting.Model = input.Model!;
            setting.Temperature = input.Temperature;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new LlmTaskDto(task, provider.Id, provider.Name, setting.Model, setting.Temperature));
        });

        g.MapGet("/prompts", async (NewsFactoryDbContext db, CancellationToken ct) =>
        {
            var all = await db.PromptTemplates.ToListAsync(ct);
            return Enum.GetValues<LlmTask>().Select(t => new PromptTaskDto(t, PromptVariables.DescribeTask(t),
                PromptVariables.For(t).Select(v => new PromptVariableDto(v.Name, v.Description, v.Required)).ToList(),
                all.Where(p => p.Task == t).OrderByDescending(p => p.Version)
                    .Select(p => new PromptVersionDto(p.Id, p.Version, p.IsActive, p.CreatedAt, p.Text)).ToList())).ToList();
        });
        g.MapPost("/prompts/{task}", async (LlmTask task, PromptInput input, NewsFactoryDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text)) return Problems.Validation("Текст промпта пуст");
            if (PromptVariables.Validate(task, input.Text) is { Count: > 0 } problems)
                return Problems.Validation(string.Join("; ", problems), new Dictionary<string, string[]> { ["text"] = problems.ToArray() });
            var versions = await db.PromptTemplates.Where(p => p.Task == task).ToListAsync(ct);
            foreach (var v in versions) v.IsActive = false;
            var created = new PromptTemplate { Task = task, Version = versions.Count == 0 ? 1 : versions.Max(v => v.Version) + 1, Text = input.Text, IsActive = true };
            db.PromptTemplates.Add(created);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/settings/prompts/{created.Id}",
                new PromptVersionDto(created.Id, created.Version, true, created.CreatedAt, created.Text));
        });
        g.MapPut("/prompts/{id:long}/activate", async (long id, NewsFactoryDbContext db, CancellationToken ct) =>
        {
            var prompt = await db.PromptTemplates.SingleOrDefaultAsync(p => p.Id == id, ct);
            if (prompt is null) return Problems.NotFound("Промпт");
            foreach (var p in await db.PromptTemplates.Where(p => p.Task == prompt.Task).ToListAsync(ct)) p.IsActive = p.Id == id;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapGet("/llm/usage", async (NewsFactoryDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(await db.Cities.OrderBy(c => c.Id).Select(c => c.TimeZone).FirstAsync(ct));
            var local = TimeZoneInfo.ConvertTime(time.GetUtcNow(), tz);
            var dayStart = new DateTimeOffset(local.Date, local.Offset).ToUniversalTime();
            var monthLocal = new DateTime(local.Year, local.Month, 1);
            var monthStart = new DateTimeOffset(monthLocal, tz.GetUtcOffset(monthLocal)).ToUniversalTime();
            var today = await db.LlmCalls.Where(c => c.CreatedAt >= dayStart).SumAsync(c => (long)c.PromptTokens + c.CompletionTokens, ct);
            var month = await db.LlmCalls.Where(c => c.CreatedAt >= monthStart).SumAsync(c => (long)c.PromptTokens + c.CompletionTokens, ct);
            var value = await db.AppSettings.Where(s => s.Key == LlmBudgetGuard.SettingKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
            return new UsageDto(today, month,
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) ? b : LlmBudgetGuard.DefaultBudget);
        });
        g.MapPut("/llm/budget", async (BudgetInput input, NewsFactoryDbContext db, CancellationToken ct) =>
        {
            if (input.DailyTokenBudget < MinBudget) return Problems.Validation($"Бюджет — не меньше {MinBudget} токенов в сутки");
            var setting = await db.AppSettings.SingleOrDefaultAsync(s => s.Key == LlmBudgetGuard.SettingKey, ct);
            if (setting is null) db.AppSettings.Add(setting = new AppSetting { Key = LlmBudgetGuard.SettingKey });
            setting.Value = input.DailyTokenBudget.ToString(CultureInfo.InvariantCulture);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static bool Has(IConfiguration config, string key) => !string.IsNullOrWhiteSpace(config[key]);

    private static LlmProviderDto ToDto(LlmProvider p, IConfiguration config) => new(p.Id, p.Name, p.Kind, p.BaseUrl, p.ApiKeyEnvVar, p.Models,
        p.Headers, p.SupportsJsonSchema, p.AuthUrl, p.AuthScope, Has(config, p.ApiKeyEnvVar));

    private static Dictionary<string, string[]> ValidateProvider(LlmProviderInput i)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(i.Name)) errors["name"] = ["Укажите имя"];
        if (!Uri.TryCreate(i.BaseUrl, UriKind.Absolute, out var u) || u.Scheme != "https") errors["baseUrl"] = ["Нужен адрес https://…"];
        if (string.IsNullOrWhiteSpace(i.ApiKeyEnvVar)) errors["apiKeyEnvVar"] = ["Укажите имя переменной окружения с ключом"];
        if (i.Models is null || !i.Models.Any(m => !string.IsNullOrWhiteSpace(m))) errors["models"] = ["Нужна хотя бы одна модель"];
        if (i.Kind == LlmProviderKind.GigaChat && !Uri.TryCreate(i.AuthUrl, UriKind.Absolute, out _)) errors["authUrl"] = ["Для GigaChat нужен адрес OAuth"];
        return errors;
    }

    private static void Apply(LlmProvider p, LlmProviderInput i)
    {
        p.Name = i.Name!.Trim();
        p.Kind = i.Kind;
        p.BaseUrl = i.BaseUrl!.Trim();
        p.ApiKeyEnvVar = i.ApiKeyEnvVar!.Trim();
        p.Models = i.Models!.Select(m => m.Trim()).Where(m => m.Length > 0).Distinct().ToList();
        p.Headers = i.Headers?.ToDictionary() ?? [];
        p.SupportsJsonSchema = i.SupportsJsonSchema;
        p.AuthUrl = string.IsNullOrWhiteSpace(i.AuthUrl) ? null : i.AuthUrl.Trim();
        p.AuthScope = string.IsNullOrWhiteSpace(i.AuthScope) ? null : i.AuthScope.Trim();
    }
}
