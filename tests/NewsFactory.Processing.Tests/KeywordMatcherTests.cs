namespace NewsFactory.Processing.Tests;

public class KeywordMatcherTests
{
    [Theory]
    [InlineData("В ОТРАДНОМ отключат воду", true)]
    [InlineData("Отрадненский район", true)]
    [InlineData("«Отрадный» — так назвали сквер", true)]
    [InlineData("Безотрадный день", false)]
    [InlineData("Самара и Тольятти", false)]
    public void Matches_stem_at_word_start(string text, bool expected) =>
        Assert.Equal(expected, KeywordMatcher.Matches(text, ["отрадн"]));

    [Fact]
    public void Yo_equals_ye() => Assert.True(KeywordMatcher.Matches("Новый ЁЛОЧНЫЙ базар", ["елочн"]));
}
