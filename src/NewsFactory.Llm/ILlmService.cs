using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Llm.Clients;

namespace NewsFactory.Llm;

public sealed record LlmCallContext(long? StoryId = null, long? RawItemId = null);

public interface ILlmService
{
    Task<T> CompleteAsync<T>(LlmTask task, IReadOnlyDictionary<string, string> vars, LlmCallContext ctx,
        Func<T, IEnumerable<string>>? validate = null, CancellationToken ct = default);
}

public interface ILlmClientResolver
{
    IChatCompletionClient Resolve(LlmProvider provider);
}
