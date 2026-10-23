using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;

namespace NewsFactory.Llm.Clients;

public sealed class LlmClientFactory(IHttpClientFactory http, IConfiguration config, TimeProvider time)
{
    public const string HttpClientName = "llm";
    private readonly ConcurrentDictionary<long, GigaChatTokenProvider> _tokens = new();

    public IChatCompletionClient Create(LlmProvider provider)
    {
        var key = config[provider.ApiKeyEnvVar];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException($"Не задана переменная {provider.ApiKeyEnvVar} с ключом провайдера {provider.Name}");
        var client = http.CreateClient(HttpClientName);
        return provider.Kind switch
        {
            LlmProviderKind.OpenAiCompatible => new OpenAiCompatibleClient(client, provider, key),
            LlmProviderKind.GigaChat => new GigaChatClient(client, provider,
                _tokens.GetOrAdd(provider.Id, _ => new GigaChatTokenProvider(http.CreateClient(HttpClientName), provider, key, time))),
            _ => throw new NotSupportedException(provider.Kind.ToString()),
        };
    }
}
