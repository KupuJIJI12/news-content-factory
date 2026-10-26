using NewsFactory.Domain;
using NewsFactory.Llm;

namespace NewsFactory.TestSupport;

/// <summary>In-memory ILlmService: scripted responders per task, applies the caller's validate like the real service.</summary>
public sealed class FakeLlmService : ILlmService
{
    private readonly Dictionary<LlmTask, Func<IReadOnlyDictionary<string, string>, object>> _responders = [];
    private readonly Lock _lock = new();

    public List<(LlmTask Task, IReadOnlyDictionary<string, string> Vars, LlmCallContext Ctx)> Calls { get; } = [];

    public FakeLlmService Respond<T>(LlmTask task, Func<IReadOnlyDictionary<string, string>, T> responder) where T : notnull
    {
        _responders[task] = v => responder(v);
        return this;
    }

    public FakeLlmService Throw(LlmTask task, Exception exception)
    {
        _responders[task] = _ => throw exception;
        return this;
    }

    public Task<T> CompleteAsync<T>(LlmTask task, IReadOnlyDictionary<string, string> vars, LlmCallContext ctx,
        Func<T, IEnumerable<string>>? validate = null, CancellationToken ct = default)
    {
        lock (_lock) Calls.Add((task, vars, ctx));
        if (!_responders.TryGetValue(task, out var responder))
            throw new InvalidOperationException($"FakeLlmService: нет ответа для задачи {task}");
        var value = (T)responder(vars);
        var errors = validate?.Invoke(value).ToList() ?? [];
        if (errors.Count > 0) throw new LlmResponseInvalidException(task, errors);
        return Task.FromResult(value);
    }
}
