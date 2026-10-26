namespace NewsFactory.Processing.Tests;

public class SourceTextBuilderTests
{
    [Fact]
    public void Orders_by_date_and_numbers_sources()
    {
        var text = SourceTextBuilder.Build([
            new SourceText("A", "Заголовок A", "Текст A", DateTimeOffset.Parse("2026-10-23T12:00:00Z")),
            new SourceText("B", "Заголовок B", "Текст B", DateTimeOffset.Parse("2026-10-23T10:00:00Z")),
        ]);
        Assert.Equal("### Источник 1: B\nЗаголовок B\nТекст B\n\n### Источник 2: A\nЗаголовок A\nТекст A", text);
    }

    [Fact]
    public void Truncates_each_item_and_total()
    {
        var big = string.Join(' ', Enumerable.Repeat("слово", 1000)) + " конец";
        var text = SourceTextBuilder.Build(
            Enumerable.Range(0, 6).Select(i => new SourceText($"S{i}", "T", big, null)).ToList(), perItemMax: 3000, totalMax: 10000);
        Assert.True(text.Length <= 10000);
        Assert.DoesNotContain("конец", text);
        Assert.Contains("слово…", text);
        Assert.Contains("### Источник 3:", text);
        Assert.DoesNotContain("### Источник 4:", text);
    }

    [Fact]
    public void Single_huge_item_is_cut_to_total()
    {
        var text = SourceTextBuilder.Build([new SourceText("S", "T", new string('а', 50_000), null)], perItemMax: 60_000, totalMax: 1000);
        Assert.True(text.Length <= 1000);
    }
}
