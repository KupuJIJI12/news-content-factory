using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;

namespace NewsFactory.Publishing.Max;

/// <summary>Publishes to a Max channel: optional image upload, then POST /messages with markdown text.</summary>
public sealed class MaxPublisher(HttpClient http, IOptions<MaxOptions> options) : IPublisher
{
    private readonly MaxOptions _o = options.Value;

    public Platform Platform => Platform.Max;

    public async Task<PublishResult> PublishAsync(Channel channel, PostContent content, CancellationToken ct)
    {
        var text = MaxMarkupConverter.Convert(content);
        var token = content.Image is null ? null : await UploadImageAsync(content.Image.LocalPath, ct);

        var body = new JsonObject { ["text"] = text, ["format"] = "markdown" };
        if (token is not null)
            body["attachments"] = new JsonArray(new JsonObject { ["type"] = "image", ["payload"] = new JsonObject { ["token"] = token } });
        var url = $"{_o.BaseUrl.TrimEnd('/')}/messages?chat_id={Uri.EscapeDataString(channel.ExternalChatId)}";

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = Request(HttpMethod.Post, url);
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                response = await http.SendAsync(request, ct);
            }
            catch (HttpRequestException ex) when (ex.HttpRequestError is HttpRequestError.ConnectionError
                                                      or HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError)
            {
                throw new PublisherTransientException($"Max недоступен: {ex.Message}", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                throw new PublisherAmbiguousException($"Обрыв во время отправки в Max: {ex.Message}", ex);
            }

            using (response)
            {
                var payload = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                    return new PublishResult(TryParse(payload)?["message"]?["body"]?["mid"]?.GetValue<string>());

                if (response.StatusCode == HttpStatusCode.BadRequest && payload.Contains("attachment.not.ready", StringComparison.Ordinal))
                {
                    if (attempt >= _o.AttachmentRetryDelays.Length)
                        throw new PublisherTransientException("Max так и не обработал картинку (attachment.not.ready)");
                    await Task.Delay(_o.AttachmentRetryDelays[attempt], ct);
                    continue;
                }

                var message = $"Max ответил {(int)response.StatusCode}: {Describe(payload)}";
                throw IsTransient(response.StatusCode) ? new PublisherTransientException(message) : new PublisherPermanentException(message);
            }
        }
    }

    private async Task<string> UploadImageAsync(string path, CancellationToken ct)
    {
        try
        {
            using var request = Request(HttpMethod.Post, $"{_o.BaseUrl.TrimEnd('/')}/uploads?type=image");
            using var slot = await http.SendAsync(request, ct);
            var slotJson = await ReadOkJson(slot, ct);
            var uploadUrl = slotJson["url"]?.GetValue<string>() ?? throw new PublisherTransientException("Max не вернул url для загрузки");
            var preToken = slotJson["token"]?.GetValue<string>();

            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(await File.ReadAllBytesAsync(path, ct));
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "data", Path.GetFileName(path));
            using var upload = Request(HttpMethod.Post, uploadUrl);
            upload.Content = form;
            using var uploaded = await http.SendAsync(upload, ct);
            var uploadedJson = await ReadOkJson(uploaded, ct);

            var token = (uploadedJson["photos"] as JsonObject)?.Select(p => p.Value?["token"]?.GetValue<string>()).FirstOrDefault(t => t is not null)
                        ?? uploadedJson["token"]?.GetValue<string>()
                        ?? preToken;
            return token ?? throw new PublisherTransientException("Max не вернул токен загруженной картинки");
        }
        catch (PublisherTransientException) { throw; }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            throw new PublisherTransientException($"Не удалось загрузить картинку в Max: {ex.Message}", ex);
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Authorization", _o.BotToken);
        return request;
    }

    private static async Task<JsonNode> ReadOkJson(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new PublisherTransientException($"Max ответил {(int)response.StatusCode} при загрузке: {Describe(text)}");
        return TryParse(text) ?? new JsonObject();
    }

    private static bool IsTransient(HttpStatusCode code) => code == HttpStatusCode.TooManyRequests || (int)code >= 500;

    private static JsonNode? TryParse(string text)
    {
        try { return JsonNode.Parse(text); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static string Describe(string payload) =>
        TryParse(payload)?["message"] is JsonValue v && v.TryGetValue<string>(out var m)
            ? m
            : payload.Length <= 300 ? payload : payload[..300];
}
