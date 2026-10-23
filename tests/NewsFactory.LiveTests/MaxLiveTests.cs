using Microsoft.Extensions.Options;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Publishing;
using NewsFactory.Publishing.Max;

namespace NewsFactory.LiveTests;

public class MaxLiveTests
{
    [Fact]
    public async Task Publishes_text_and_image_to_test_channel()
    {
        Env.Require("MAX_BOT_TOKEN", "MAX_TEST_CHAT_ID");
        var publisher = new MaxPublisher(new HttpClient { Timeout = TimeSpan.FromSeconds(60) },
            Options.Create(new MaxOptions { BotToken = Env.Get("MAX_BOT_TOKEN")! }));
        var post = new PostContent(
            $"[ТЕСТ] NewsContentFactory {DateTimeOffset.Now:yyyy-MM-dd HH:mm}",
            "Это тестовый пост. В нём есть **жирный текст** и список:\n\n- первый пункт\n- второй пункт",
            ["#тест", "#NewsContentFactory"],
            new PostImage(Path.GetFullPath("Fixtures/test.jpg"), "тестовая подпись"));

        var result = await publisher.PublishAsync(new Channel { Platform = Platform.Max, ExternalChatId = Env.Get("MAX_TEST_CHAT_ID")! }, post, default);

        Env.Log($"Опубликовано, id сообщения: {result.ExternalMessageId}");
        Assert.NotNull(result.ExternalMessageId);
    }
}
