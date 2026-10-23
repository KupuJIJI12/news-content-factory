using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Schema;
using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;
using NewsFactory.Llm.Clients;

namespace NewsFactory.Llm;

public sealed class LlmService(NewsFactoryDbContext db, ILlmClientResolver clients, LlmBudgetGuard budget) : ILlmService
{
    private const int MaxAttempts = 2;
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    public async Task<T> CompleteAsync<T>(LlmTask task, IReadOnlyDictionary<string, string> vars, LlmCallContext ctx,
        Func<T, IEnumerable<string>>? validate = null, CancellationToken ct = default)
    {
        await budget.EnsureAvailableAsync(ct);

        var setting = await db.LlmTaskSettings.Include(s => s.Provider).SingleOrDefaultAsync(s => s.Task == task, ct)
                      ?? throw new InvalidOperationException($"Не настроена модель для задачи {task}");
        var template = await db.PromptTemplates.Where(p => p.Task == task && p.IsActive)
                           .OrderByDescending(p => p.Version).FirstOrDefaultAsync(ct)
                       ?? throw new InvalidOperationException($"Нет активного промпта для задачи {task}");
        var client = clients.Resolve(setting.Provider!);

        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(Json, typeof(T));
        var messages = new List<ChatMessage>
        {
            new("system", "Отвечай только JSON-объектом, соответствующим схеме:\n" + schema.ToJsonString()),
            new("user", PromptRenderer.Render(template.Text, vars)),
        };

        IReadOnlyList<string> errors = [];
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var call = new LlmCall
            {
                Task = task, ProviderId = setting.ProviderId, Model = setting.Model,
                StoryId = ctx.StoryId, RawItemId = ctx.RawItemId,
                Request = JsonSerializer.Serialize(messages, Json),
            };
            db.LlmCalls.Add(call);
            var sw = Stopwatch.StartNew();
            ChatCompletion completion;
            try
            {
                completion = await client.CompleteAsync(new ChatRequest(setting.Model, messages.ToList(), setting.Temperature, schema), ct);
            }
            catch (Exception ex)
            {
                call.LatencyMs = sw.ElapsedMilliseconds;
                call.Error = ex.Message;
                await db.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            call.LatencyMs = sw.ElapsedMilliseconds;
            call.Response = completion.Content;
            call.PromptTokens = completion.PromptTokens;
            call.CompletionTokens = completion.CompletionTokens;
            var parsed = LlmJson.Parse(completion.Content, validate);
            call.Success = parsed.Ok;
            call.Error = parsed.Ok ? null : string.Join("; ", parsed.Errors);
            await db.SaveChangesAsync(ct);
            if (parsed.Ok) return parsed.Value!;

            errors = parsed.Errors;
            messages.Add(new ChatMessage("assistant", completion.Content));
            messages.Add(new ChatMessage("user",
                $"Ответ не прошёл проверку: {string.Join("; ", errors)}. Пришли исправленный JSON."));
        }

        throw new LlmResponseInvalidException(task, errors);
    }
}
