using NewsFactory.Collection.Text;

namespace NewsFactory.Collection.Tests;

public class UrlNormalizerTests
{
    [Theory]
    [InlineData("https://otradny.org/sitenews/123.html", "https://otradny.org/sitenews/123.html")]
    [InlineData("http://otradny.org/sitenews/123.html", "https://otradny.org/sitenews/123.html")]
    [InlineData("https://WWW.Otradny.org/sitenews/123.html", "https://otradny.org/sitenews/123.html")]
    [InlineData("https://otradny.org/sitenews/123.html#comments", "https://otradny.org/sitenews/123.html")]
    [InlineData("https://63.ru/text/news/2026/10/27/123/", "https://63.ru/text/news/2026/10/27/123")]
    [InlineData("https://x.ru/news?utm_source=vk&id=5&utm_medium=social&yclid=9", "https://x.ru/news?id=5")]
    [InlineData("https://x.ru/news.php?ELEMENT_ID=77&from=main", "https://x.ru/news.php?ELEMENT_ID=77")]
    [InlineData("https://x.ru/", "https://x.ru/")]
    public void Normalizes_variants_of_one_address(string input, string expected) =>
        Assert.Equal(expected, UrlNormalizer.Normalize(input));

    [Fact]
    public void Different_articles_stay_different() =>
        Assert.NotEqual(UrlNormalizer.Normalize("https://x.ru/news.php?id=1"), UrlNormalizer.Normalize("https://x.ru/news.php?id=2"));

    [Fact]
    public void Non_url_ids_are_left_as_is() => Assert.Equal("-12345_678", UrlNormalizer.Normalize("-12345_678"));
}
