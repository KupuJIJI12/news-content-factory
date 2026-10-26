using NewsFactory.Publishing.Max;

namespace NewsFactory.Publishing.Tests;

public class MaxMarkupConverterTests
{
    [Fact]
    public void Formats_title_bullets_credit_and_hashtags() =>
        Assert.Equal("**Заголовок**\n\nАбзац **важно**.\n\n• раз\n• два\n\nФото: otradny.org\n\n#Отрадный #новости",
            MaxMarkupConverter.Convert(new PostContent("Заголовок", "Абзац **важно**.\n\n- раз\n- два",
                ["#Отрадный", "#новости"], new PostImage("/x.jpg", "otradny.org"))));

    [Fact]
    public void No_credit_line_without_image() =>
        Assert.DoesNotContain("Фото:", MaxMarkupConverter.Convert(new PostContent("T", "B", ["#a"], null)));

    [Fact]
    public void No_credit_line_for_image_without_credit() =>
        Assert.DoesNotContain("Фото:", MaxMarkupConverter.Convert(new PostContent("T", "B", ["#a"], new PostImage("/x.jpg", null))));

    [Fact]
    public void Throws_when_longer_than_4000()
    {
        var ex = Assert.Throws<PostTooLongException>(() =>
            MaxMarkupConverter.Convert(new PostContent("T", new string('а', 4000), ["#a"], null)));
        Assert.Contains("4000", ex.Message);
    }

    [Fact]
    public void Exactly_4000_is_allowed()
    {
        var overhead = "**T**\n\n".Length + "\n\n#a".Length;
        Assert.Equal(4000, MaxMarkupConverter.Convert(new PostContent("T", new string('а', 4000 - overhead), ["#a"], null)).Length);
    }
}
