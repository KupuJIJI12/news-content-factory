using NewsFactory.Domain;

namespace NewsFactory.Llm.Tests;

public class PromptVariablesTests
{
    [Theory]
    [InlineData(LlmTask.Relevance)]
    [InlineData(LlmTask.Clustering)]
    [InlineData(LlmTask.Scoring)]
    [InlineData(LlmTask.Rewrite)]
    [InlineData(LlmTask.FactCheck)]
    public void Defaults_are_valid(LlmTask task) => Assert.Empty(PromptVariables.Validate(task, DefaultPrompts.Get(task)));

    [Fact]
    public void Unknown_variable_is_error() =>
        Assert.Contains(PromptVariables.Validate(LlmTask.Scoring, "{{city}} {{sources}} {{weather}}"), e => e.Contains("{{weather}}"));

    [Fact]
    public void Missing_required_is_error() =>
        Assert.Contains(PromptVariables.Validate(LlmTask.FactCheck, "Проверь {{draft}}"), e => e.Contains("{{sources}}"));

    [Fact]
    public void Hint_is_optional_for_rewrite() => Assert.Empty(PromptVariables.Validate(LlmTask.Rewrite, "{{city}} {{sources}}"));
}
