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
    [InlineData(LlmTask.SourceSetup)]
    public void Defaults_are_valid(LlmTask task) => Assert.Empty(PromptVariables.Validate(task, DefaultPrompts.Get(task)));

    [Fact]
    public void Unknown_variable_is_error() =>
        Assert.Contains(PromptVariables.Validate(LlmTask.Scoring, "{{city}} {{sources}} {{weather}}"), e => e.Contains("{{weather}}"));

    [Fact]
    public void Missing_required_is_error() =>
        Assert.Contains(PromptVariables.Validate(LlmTask.FactCheck, "Проверь {{draft}}"), e => e.Contains("{{sources}}"));

    [Fact]
    public void Hint_is_optional_for_rewrite() => Assert.Empty(PromptVariables.Validate(LlmTask.Rewrite, "{{city}} {{sources}}"));

    [Fact]
    public void Every_variable_and_task_has_a_description()
    {
        foreach (var task in Enum.GetValues<LlmTask>())
        {
            Assert.False(string.IsNullOrWhiteSpace(PromptVariables.DescribeTask(task)));
            foreach (var v in PromptVariables.For(task))
                Assert.False(string.IsNullOrWhiteSpace(v.Description), $"{task}: {{{{{v.Name}}}}} без описания");
        }
        Assert.Contains(PromptVariables.For(LlmTask.Relevance), v => v.Name == "title" && v.Required && v.Description.Contains("Заголовок"));
        Assert.Contains(PromptVariables.For(LlmTask.Rewrite), v => v.Name == "hint" && !v.Required);
    }
}
