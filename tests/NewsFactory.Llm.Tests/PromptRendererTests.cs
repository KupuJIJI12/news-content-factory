namespace NewsFactory.Llm.Tests;

public class PromptRendererTests
{
    [Fact]
    public void Replaces_variables() =>
        Assert.Equal("Город Отрадный", PromptRenderer.Render("Город {{city}}",
            new Dictionary<string, string> { ["city"] = "Отрадный", ["unused"] = "x" }));

    [Fact]
    public void Unknown_variable_throws()
    {
        var ex = Assert.Throws<KeyNotFoundException>(() => PromptRenderer.Render("{{sources}}", new Dictionary<string, string>()));
        Assert.Contains("sources", ex.Message);
    }

    [Fact]
    public void Values_are_not_rendered_recursively() =>
        Assert.Equal("a {{city}}", PromptRenderer.Render("a {{x}}", new Dictionary<string, string> { ["x"] = "{{city}}" }));
}
