using NewsFactory.Domain;

namespace NewsFactory.LiveTests;

public class GigaChatLiveTests
{
    [Fact]
    public async Task Rewrites_fixture_story_with_default_models()
    {
        Env.Require("GIGACHAT_AUTH_KEY");
        await LlmLiveScenario.RunAsync("GigaChat", new Dictionary<LlmTask, string>
        {
            [LlmTask.Relevance] = "GigaChat-2", [LlmTask.Clustering] = "GigaChat-2", [LlmTask.Scoring] = "GigaChat-2",
            [LlmTask.Rewrite] = "GigaChat-2-Max", [LlmTask.FactCheck] = "GigaChat-2-Pro",
        });
    }
}
