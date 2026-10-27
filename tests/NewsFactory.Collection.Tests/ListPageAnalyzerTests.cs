using System.Text.Json;
using NewsFactory.Collection.AutoSetup;
using NewsFactory.Collection.Html;

namespace NewsFactory.Collection.Tests;

public class ListPageAnalyzerTests
{
    private static HtmlSourceConfig Known(string name) =>
        JsonDocument.Parse(File.ReadAllText("Fixtures/html/configs.json")).RootElement.GetProperty(name)
            .Deserialize<HtmlSourceConfig>(JsonSerializerOptions.Web)!;

    private static HashSet<string> Urls(string html, Uri page, HtmlSourceConfig config) =>
        ListPageAnalyzer.ListItems(html, page, config).Select(i => i.Url).ToHashSet();

    private static double Jaccard(HashSet<string> a, HashSet<string> b) => (double)a.Intersect(b).Count() / a.Union(b).Count();

    [Theory]
    [InlineData("otradny", "Fixtures/html/otradny-list.html")]
    [InlineData("63ru", "Fixtures/html/63ru-list.html")]
    public void Finds_the_news_list_of_a_real_page(string name, string fixture)
    {
        var known = Known(name);
        var page = new Uri(known.ListUrl);
        var html = File.ReadAllText(fixture);

        var best = ListPageAnalyzer.Suggest(html, page).First();
        var expected = Urls(html, page, known);
        var actual = Urls(html, page, best.Config);
        Assert.True(Jaccard(expected, actual) >= 0.8,
            $"{best.Config.ItemSelector} | {best.Config.LinkSelector}: {actual.Count} vs {expected.Count} ссылок");
        Assert.All(ListPageAnalyzer.ListItems(html, page, best.Config), i => Assert.True(i.Title.Length >= 15, i.Title));
        Assert.Equal(page.ToString(), best.Config.ListUrl);
    }

    [Fact]
    public void Detects_date_selector_on_otradny()
    {
        var best = ListPageAnalyzer.Suggest(File.ReadAllText("Fixtures/html/otradny-list.html"), new Uri("https://otradny.org/sitenews/")).First();
        Assert.Equal("p.date", best.Config.DateSelector);
    }

    [Fact]
    public void Page_without_news_list_gives_nothing()
    {
        const string html = """
            <html><body><nav><ul class="menu"><li><a href="/a">О нас</a></li><li><a href="/b">Контакты</a></li>
            <li><a href="/c">Услуги</a></li><li><a href="/d">Цены</a></li></ul></nav><p>Добро пожаловать на наш сайт!</p></body></html>
            """;
        Assert.Empty(ListPageAnalyzer.Suggest(html, new Uri("https://card.example/")));
    }
}
