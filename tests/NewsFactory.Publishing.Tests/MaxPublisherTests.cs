using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Publishing.Max;
using NewsFactory.TestSupport;

namespace NewsFactory.Publishing.Tests;

public class MaxPublisherTests
{
    private const string Messages = "https://platform-api2.max.ru/messages?chat_id=-100";
    private const string Uploads = "https://platform-api2.max.ru/uploads?type=image";
    private const string UploadUrl = "https://upload.max.example/u/abc";
    private const string Sent = """{"message":{"body":{"mid":"mid.123","text":"x"}}}""";
    private readonly StubHttpHandler _handler = new();

    private MaxPublisher Publisher() => new(new HttpClient(_handler), Options.Create(new MaxOptions
    {
        BotToken = "tok", AttachmentRetryDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero],
    }));

    private static Channel Channel(string chatId) => new() { Platform = Platform.Max, ExternalChatId = chatId };

    private static PostContent Post(PostImage? image = null, string body = "Текст поста") => new("Заголовок", body, ["#Отрадный"], image);

    private static string ImageFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "nf-test-" + Guid.NewGuid() + ".jpg");
        File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF, 0xD9]);
        return path;
    }

    [Fact]
    public async Task Sends_text_message_to_channel()
    {
        _handler.Map(Messages, HttpStatusCode.OK, Sent);
        var r = await Publisher().PublishAsync(Channel("-100"), Post(), default);
        var req = _handler.Requests.Single();
        Assert.Equal(Messages, req.RequestUri!.ToString());
        Assert.Equal("tok", req.Headers.GetValues("Authorization").Single());
        var body = JsonNode.Parse(_handler.Bodies.Single())!;
        Assert.Equal("markdown", body["format"]!.GetValue<string>());
        Assert.Equal("**Заголовок**\n\nТекст поста\n\n#Отрадный", body["text"]!.GetValue<string>());
        Assert.Null(body["attachments"]);
        Assert.Equal("mid.123", r.ExternalMessageId);
    }

    [Fact]
    public async Task Uploads_image_then_attaches_token()
    {
        _handler.Map(Uploads, HttpStatusCode.OK, $$"""{"url":"{{UploadUrl}}"}""")
            .Map(UploadUrl, HttpStatusCode.OK, """{"photos":{"anyKey":{"token":"ph1"}}}""")
            .Map(Messages, HttpStatusCode.OK, Sent);
        await Publisher().PublishAsync(Channel("-100"), Post(new PostImage(ImageFile(), "otradny.org")), default);

        Assert.Equal([Uploads, UploadUrl, Messages], _handler.Requests.Select(r => r.RequestUri!.ToString()));
        Assert.Contains("name=data", _handler.Bodies[1]);
        var attachment = JsonNode.Parse(_handler.Bodies[2])!["attachments"]![0]!;
        Assert.Equal("image", attachment["type"]!.GetValue<string>());
        Assert.Equal("ph1", attachment["payload"]!["token"]!.GetValue<string>());
    }

    [Fact]
    public async Task Retries_when_attachment_not_ready()
    {
        var calls = 0;
        _handler.Map(Uploads, HttpStatusCode.OK, $$"""{"url":"{{UploadUrl}}","token":"pre"}""")
            .Map(UploadUrl, HttpStatusCode.OK, "{}")
            .Map(Messages, _ => ++calls == 1
                ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"code":"attachment.not.ready","message":"wait"}""") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Sent) });
        var r = await Publisher().PublishAsync(Channel("-100"), Post(new PostImage(ImageFile(), null)), default);
        Assert.Equal("mid.123", r.ExternalMessageId);
        Assert.Equal(2, calls);
        Assert.Equal("pre", JsonNode.Parse(_handler.Bodies.Last())!["attachments"]![0]!["payload"]!["token"]!.GetValue<string>());
    }

    [Fact]
    public async Task Server_error_is_transient()
    {
        _handler.Map(Messages, HttpStatusCode.ServiceUnavailable, "{}");
        await Assert.ThrowsAsync<PublisherTransientException>(() => Publisher().PublishAsync(Channel("-100"), Post(), default));
    }

    [Fact]
    public async Task Too_many_requests_is_transient()
    {
        _handler.Map(Messages, HttpStatusCode.TooManyRequests, "{}");
        await Assert.ThrowsAsync<PublisherTransientException>(() => Publisher().PublishAsync(Channel("-100"), Post(), default));
    }

    [Fact]
    public async Task Unauthorized_is_permanent()
    {
        _handler.Map(Messages, HttpStatusCode.Unauthorized, """{"code":"verify.token","message":"Invalid access_token"}""");
        var ex = await Assert.ThrowsAsync<PublisherPermanentException>(() => Publisher().PublishAsync(Channel("-100"), Post(), default));
        Assert.Contains("Invalid access_token", ex.Message);
    }

    [Fact]
    public async Task Connection_refused_on_send_is_transient()
    {
        _handler.Throw(Messages, new HttpRequestException(HttpRequestError.ConnectionError, "refused", new SocketException()));
        await Assert.ThrowsAsync<PublisherTransientException>(() => Publisher().PublishAsync(Channel("-100"), Post(), default));
    }

    [Fact]
    public async Task Timeout_on_send_is_ambiguous()
    {
        _handler.Throw(Messages, new TaskCanceledException("timeout"));
        await Assert.ThrowsAsync<PublisherAmbiguousException>(() => Publisher().PublishAsync(Channel("-100"), Post(), default));
    }

    [Fact]
    public async Task Upload_failure_is_transient()
    {
        _handler.Throw(Uploads, new TaskCanceledException("timeout"));
        await Assert.ThrowsAsync<PublisherTransientException>(() =>
            Publisher().PublishAsync(Channel("-100"), Post(new PostImage(ImageFile(), null)), default));
        Assert.DoesNotContain(_handler.Requests, r => r.RequestUri!.ToString() == Messages);
    }

    [Fact]
    public async Task Too_long_post_fails_before_any_request()
    {
        await Assert.ThrowsAsync<PostTooLongException>(() =>
            Publisher().PublishAsync(Channel("-100"), Post(body: new string('а', 4100)), default));
        Assert.Empty(_handler.Requests);
    }
}
