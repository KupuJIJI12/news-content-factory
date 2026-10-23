using NewsFactory.Collection.Text;

namespace NewsFactory.Collection.Tests;

public class HtmlTextTests
{
    [Fact]
    public void Paragraphs_lists_and_entities() =>
        Assert.Equal("Первый абзац.\n\nВторой &.\n\n- пункт 1\n- пункт 2",
            HtmlText.ToPlainText("<p>Первый   абзац.</p><script>x()</script><p>Второй &amp;.</p><ul><li>пункт 1</li><li>пункт 2</li></ul>"));

    [Fact]
    public void Br_and_divs_and_plain_text() =>
        Assert.Equal("строка 1\nстрока 2\n\nблок", HtmlText.ToPlainText("строка 1<br>строка 2<div>блок</div>"));
}
