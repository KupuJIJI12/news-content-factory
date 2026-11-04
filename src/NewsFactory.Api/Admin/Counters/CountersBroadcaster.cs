using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace NewsFactory.Api.Admin.Counters;

public sealed class CountersOptions
{
    public int CountersIntervalSeconds { get; set; } = 5;
}

/// <summary>Polls counters and pushes them to admins when they change.</summary>
public sealed class CountersBroadcaster(
    IServiceScopeFactory scopes,
    IHubContext<AdminHub> hub,
    TimeProvider time,
    IOptions<CountersOptions> options,
    ILogger<CountersBroadcaster> log) : BackgroundService
{
    private string? _last;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.CountersIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogWarning(ex, "Не удалось обновить счётчики админки");
            }
            try { await Task.Delay(interval, time, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    internal async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var counters = await scope.ServiceProvider.GetRequiredService<CountersService>().GetAsync(ct);
        var json = JsonSerializer.Serialize(counters, JsonSerializerOptions.Web);
        if (json == _last) return;
        await hub.Clients.All.SendAsync(AdminHub.CountersChanged, counters, ct);
        _last = json;
    }
}
