using NewsFactory.Domain;

namespace NewsFactory.LiveTests;

/// <summary>YandexGPT is optional (not routed by default); this checks the integration still works.</summary>
public class YandexLiveTests
{
    [Fact]
    public async Task Rewrites_fixture_story()
    {
        Env.Require("YANDEX_API_KEY", "YANDEX_FOLDER_ID");
        var folder = Env.Get("YANDEX_FOLDER_ID");
        string lite = $"gpt://{folder}/yandexgpt-lite/latest", pro = $"gpt://{folder}/yandexgpt/latest";
        await LlmLiveScenario.RunAsync("YandexGPT", new Dictionary<LlmTask, string>
        {
            [LlmTask.Relevance] = lite, [LlmTask.Clustering] = lite, [LlmTask.Scoring] = lite,
            [LlmTask.Rewrite] = pro, [LlmTask.FactCheck] = pro,
        });
    }
}
