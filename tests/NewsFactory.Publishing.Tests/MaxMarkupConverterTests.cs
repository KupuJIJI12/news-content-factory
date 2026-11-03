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

    [Fact]
    public void Hashtags_already_at_the_end_of_body_are_not_repeated() =>
        Assert.Equal("**T**\n\nТекст.\n\n#лес #отрадный",
            MaxMarkupConverter.Render(new PostContent("T", "Текст.\n\n#лес #отрадный", ["#лес", "#отрадный"], null)));

    [Fact]
    public void Trailing_hashtags_in_body_stay_when_there_is_no_hashtag_list() =>
        Assert.Equal("**T**\n\nТекст.\n\n#лес", MaxMarkupConverter.Render(new PostContent("T", "Текст.\n\n#лес", [], null)));

    [Fact]
    public void Channel_link_goes_after_text_and_credit_and_before_hashtags() =>
        Assert.Equal("**T**\n\nТекст.\n\nФото: x.ru\n\n[Отрадный Live в Max](https://max.ru/otradny)\n\n#лес",
            MaxMarkupConverter.Render(new PostContent("T", "Текст.", ["#лес"], new PostImage("/a.jpg", "x.ru"),
                new PostChannelLink("Отрадный Live", "https://max.ru/otradny"))));

    [Fact]
    public void Square_brackets_in_channel_name_do_not_break_the_link() =>
        Assert.Contains("[Новости (Отрадный) в Max](https://max.ru/o)",
            MaxMarkupConverter.Render(new PostContent("T", "B", [], null, new PostChannelLink("Новости [Отрадный]", "https://max.ru/o"))));
}

