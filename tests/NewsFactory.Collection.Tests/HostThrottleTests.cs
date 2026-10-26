using Microsoft.Extensions.Time.Testing;
using NewsFactory.Collection.Http;

namespace NewsFactory.Collection.Tests;

public class HostThrottleTests
{
    [Fact]
    public async Task Same_host_waits_min_interval_other_host_does_not()
    {
        var time = new FakeTimeProvider();
        var t = new HostThrottle(time, TimeSpan.FromSeconds(1));
        await t.WaitAsync("a.ru", default);
        var second = t.WaitAsync("a.ru", default);
        await t.WaitAsync("b.ru", default).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(second.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));
        await second.WaitAsync(TimeSpan.FromSeconds(1));
    }
}
