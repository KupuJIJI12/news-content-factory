using NewsFactory.Collection;
using NewsFactory.Domain.Entities;

namespace NewsFactory.Api.Admin.Sources;

/// <summary>Dry-runs a collector on an unsaved source configuration.</summary>
public sealed class SourceTester(IEnumerable<ISourceCollector> collectors)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private const int PreviewLength = 300;

    public async Task<SourceTestResult> RunAsync(SourceInput input, CancellationToken ct)
    {
        var source = new Source { Name = input.Name ?? "", Type = input.Type, Url = input.Url ?? "", Config = input.Config };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        try
        {
            var collector = collectors.FirstOrDefault(c => c.Type == input.Type)
                            ?? throw new InvalidOperationException($"Нет сборщика для типа {input.Type}");
            var items = await collector.CollectAsync(source, cts.Token);
            return new SourceTestResult(true, null, items.Select(i => new SourceTestItem(i.Title, i.Url, i.PublishedAt,
                i.Text.Length <= PreviewLength ? i.Text : i.Text[..PreviewLength], i.ImageUrls.Count)).ToList());
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return new SourceTestResult(false, cts.IsCancellationRequested ? "Сборщик не уложился в 60 секунд" : ex.Message, []);
        }
    }
}
