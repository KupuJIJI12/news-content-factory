using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;

namespace NewsFactory.Collection.Vk;

public sealed class VkCollector(HttpClient http, IOptions<VkOptions> options) : ISourceCollector
{
    private const int TitleMax = 120;
    private readonly VkOptions _o = options.Value;

    public SourceType Type => SourceType.Vk;

    public async Task<IReadOnlyList<CollectedItem>> CollectAsync(Source source, CancellationToken ct)
    {
        var config = JsonSerializer.Deserialize<VkSourceConfig>(source.Config ?? "", JsonSerializerOptions.Web)
                     ?? throw new InvalidOperationException($"У источника {source.Name} нет конфигурации VK");
        if (config.OwnerId is null)
        {
            var resolved = await CallAsync("utils.resolveScreenName", $"screen_name={Uri.EscapeDataString(config.ScreenName)}", ct);
            if (resolved["type"]?.GetValue<string>() != "group")
                throw new InvalidOperationException($"{config.ScreenName} — не сообщество VK");
            config = config with { OwnerId = -resolved["object_id"]!.GetValue<long>() };
            source.Config = JsonSerializer.Serialize(config, JsonSerializerOptions.Web);
        }

        var wall = await CallAsync("wall.get", $"owner_id={config.OwnerId}&count=20", ct);
        var result = new List<CollectedItem>();
        foreach (var post in wall["items"]!.AsArray().OfType<JsonObject>())
        {
            if (post["marked_as_ads"]?.GetValue<int>() == 1) continue;
            var origin = post;
            var text = post["text"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(text) && post["copy_history"]?.AsArray().FirstOrDefault() is JsonObject repost)
            {
                origin = repost;
                text = repost["text"]?.GetValue<string>() ?? "";
            }
            text = text.Trim();
            if (text.Length == 0) continue;

            var owner = post["owner_id"]!.GetValue<long>();
            var id = post["id"]!.GetValue<long>();
            result.Add(new CollectedItem(
                ExternalId: $"{owner}_{id}",
                Url: $"https://vk.ru/wall{owner}_{id}",
                Title: Title(text),
                Text: text,
                PublishedAt: DateTimeOffset.FromUnixTimeSeconds(post["date"]!.GetValue<long>()),
                ImageUrls: Photos(origin)));
        }
        return result;
    }

    private async Task<JsonNode> CallAsync(string method, string query, CancellationToken ct)
    {
        var url = $"{_o.ApiBaseUrl}{method}?{query}&access_token={Uri.EscapeDataString(_o.ServiceToken)}&v={_o.ApiVersion}";
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
        if (json["error"] is JsonObject error)
            throw new VkApiException(error["error_code"]?.GetValue<int>() ?? 0, error["error_msg"]?.GetValue<string>() ?? "");
        return json["response"]!;
    }

    private static List<string> Photos(JsonObject post) =>
        (post["attachments"]?.AsArray() ?? [])
            .OfType<JsonObject>()
            .Where(a => a["type"]?.GetValue<string>() == "photo")
            .Select(a => a["photo"]?["sizes"]?.AsArray()
                .OfType<JsonObject>()
                .MaxBy(s => (s["width"]?.GetValue<long>() ?? 0) * (s["height"]?.GetValue<long>() ?? 0))?["url"]?.GetValue<string>())
            .OfType<string>()
            .ToList();

    internal static string Title(string text)
    {
        var end = text.IndexOfAny(['.', '!', '?', '\n']);
        var sentence = (end < 0 ? text : text[..end]).Trim();
        if (end >= 0 && text[end] is '!' or '?') sentence += text[end];
        if (sentence.Length <= TitleMax) return sentence;
        var cut = sentence.LastIndexOf(' ', TitleMax);
        return sentence[..(cut > 0 ? cut : TitleMax)].TrimEnd(',', ' ', ';', ':') + "…";
    }
}
