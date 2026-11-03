namespace NewsFactory.Domain.Tests;

public class HashtagTextTests
{
    [Theory]
    [InlineData("Текст.\n\n#безопасность #отрадный #лес", "Текст.")]
    [InlineData("Текст.\n#a #b\n\n#c", "Текст.")]
    [InlineData("Текст.\n\n#a, #b.", "Текст.")]
    public void Strips_trailing_hashtag_lines(string body, string expected) =>
        Assert.Equal(expected, HashtagText.StripTrailing(body));

    [Theory]
    [InlineData("Звоните #112 сразу.")]
    [InlineData("#Отрадный готовится к зиме.\n\nВторой абзац.")]
    [InlineData("Список:\n- #1 пункт")]
    public void Keeps_hashtags_inside_the_text(string body) =>
        Assert.Equal(body, HashtagText.StripTrailing(body));

    [Fact]
    public void Body_of_only_hashtags_becomes_empty() =>
        Assert.Equal("", HashtagText.StripTrailing("#a #b"));
}
