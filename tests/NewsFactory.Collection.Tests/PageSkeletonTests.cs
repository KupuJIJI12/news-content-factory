using NewsFactory.Collection.AutoSetup;

namespace NewsFactory.Collection.Tests;

public class PageSkeletonTests
{
    [Fact]
    public void Keeps_tags_classes_and_short_text_and_drops_scripts()
    {
        const string html = """
            <html><head><script>var secret = 1;</script><style>.x{}</style></head>
            <body><div class="list"><div class="item" id="n1"><a href="/news/1.html">Очень длинный заголовок новости, который не поместится целиком в скелет страницы, потому что он длиннее восьмидесяти символов</a></div></div></body></html>
            """;
        var skeleton = PageSkeleton.Build(html);
        Assert.Contains("div.list", skeleton);
        Assert.Contains("div.item#n1", skeleton);
        Assert.Contains("a[href=/news/1.html]", skeleton);
        Assert.Contains("Очень длинный заголовок", skeleton);
        Assert.DoesNotContain("secret", skeleton);
        Assert.DoesNotContain("восьмидесяти символов", skeleton);
    }

    [Fact]
    public void Is_limited_in_size()
    {
        var html = "<html><body>" + string.Concat(Enumerable.Range(0, 5000).Select(i => $"<div class=\"c{i}\">текст {i}</div>")) + "</body></html>";
        Assert.True(PageSkeleton.Build(html).Length <= PageSkeleton.MaxLength);
    }
}
