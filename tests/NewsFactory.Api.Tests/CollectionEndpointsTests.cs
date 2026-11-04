using NewsFactory.Api.Admin.Collection;
using NewsFactory.Api.Admin.Counters;
using NewsFactory.TestSupport;

namespace NewsFactory.Api.Tests;

[Collection("db")]
public class CollectionEndpointsTests(PostgresFixture db)
{
    [Fact]
    public async Task Toggle_pause_round_trip_and_show_in_counters()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);
        Assert.False((await host.GetJsonAsync<CollectionState>("/api/collection")).Paused);

        (await host.PutJsonAsync("/api/collection", new { paused = true })).EnsureSuccessStatusCode();
        Assert.True((await host.GetJsonAsync<CollectionState>("/api/collection")).Paused);
        Assert.True((await host.GetJsonAsync<Counters>("/api/counters")).CollectionPaused);

        (await host.PutJsonAsync("/api/collection", new { paused = false })).EnsureSuccessStatusCode();
        Assert.False((await host.GetJsonAsync<Counters>("/api/counters")).CollectionPaused);
    }
}
