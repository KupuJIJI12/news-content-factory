using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using NewsFactory.Domain.Entities;

namespace NewsFactory.Llm.Clients;

/// <summary>Exchanges the GigaChat authorization key for an access token and caches it until shortly before expiry.</summary>
public sealed class GigaChatTokenProvider(HttpClient http, LlmProvider provider, string authKey, TimeProvider time)
{
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _validUntil;

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_token is not null && time.GetUtcNow() < _validUntil) return _token;
            using var request = new HttpRequestMessage(HttpMethod.Post, provider.AuthUrl)
            {
                Content = new FormUrlEncodedContent([new("scope", provider.AuthScope ?? "GIGACHAT_API_PERS")]),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authKey);
            request.Headers.Add("RqUID", Guid.NewGuid().ToString());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"GigaChat OAuth ответил {(int)response.StatusCode}", null, response.StatusCode);
            var json = JsonNode.Parse(text)!;
            _token = json["access_token"]!.GetValue<string>();
            _validUntil = DateTimeOffset.FromUnixTimeMilliseconds(json["expires_at"]!.GetValue<long>()) - Skew;
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }
}
