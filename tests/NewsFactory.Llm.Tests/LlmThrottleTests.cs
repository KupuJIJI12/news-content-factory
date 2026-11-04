using System.Net;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace NewsFactory.Llm.Tests;

public class LlmThrottleTests
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-11-03T12:00:00Z"));
    private readonly List<TimeSpan> _delays = [];

    private LlmThrottle Throttle(int minIntervalMs = 2000, int[]? backoff = null)
    {
        var throttle = new LlmThrottle(Options.Create(new LlmThrottleOptions
        {
            MinIntervalMs = minIntervalMs, RateLimitBackoffSeconds = backoff ?? [15, 60], PostponeMinutes = 10,
        }), _time);
        // No real waiting in tests: record the pause and move the clock.
        throttle.Delay = (d, _) => { _delays.Add(d); _time.Advance(d); return Task.CompletedTask; };
        return throttle;
    }

    private static HttpRequestException TooMany() => new("LLM ответила 429: Too Many Requests", null, HttpStatusCode.TooManyRequests);

    [Fact]
    public async Task Calls_to_one_provider_are_spaced_by_the_minimum_interval()
    {
        var throttle = Throttle();
        for (var i = 0; i < 3; i++) await throttle.RunAsync(1, _ => Task.FromResult(i), default);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)], _delays);
    }

    [Fact]
    public async Task Different_providers_do_not_wait_for_each_other()
    {
        var throttle = Throttle();
        await throttle.RunAsync(1, _ => Task.FromResult(0), default);
        await throttle.RunAsync(2, _ => Task.FromResult(0), default);
        Assert.Empty(_delays);
    }

    [Fact]
    public async Task Concurrent_calls_run_one_at_a_time()
    {
        var throttle = Throttle(minIntervalMs: 0);
        var running = 0;
        var maxRunning = 0;
        async Task<int> Call(CancellationToken _)
        {
            maxRunning = Math.Max(maxRunning, Interlocked.Increment(ref running));
            await Task.Delay(20);
            Interlocked.Decrement(ref running);
            return 0;
        }
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => throttle.RunAsync(1, Call, default)));
        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public async Task Rate_limited_call_waits_and_retries()
    {
        var throttle = Throttle();
        var attempts = 0;
        var result = await throttle.RunAsync(1, _ => ++attempts == 1 ? throw TooMany() : Task.FromResult("ok"), default);
        Assert.Equal(("ok", 2), (result, attempts));
        Assert.Equal([TimeSpan.FromSeconds(15)], _delays);
    }

    [Fact]
    public async Task Persistent_rate_limit_postpones_the_work()
    {
        var throttle = Throttle();
        var attempts = 0;
        var ex = await Assert.ThrowsAsync<LlmRateLimitedException>(() =>
            throttle.RunAsync<string>(1, _ => { attempts++; throw TooMany(); }, default));
        Assert.Equal(3, attempts);
        Assert.Equal([TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60)], _delays);
        Assert.Equal(_time.GetUtcNow().AddMinutes(10), ex.ResumeAt);
        Assert.IsAssignableFrom<LlmPostponedException>(ex);
    }

    [Fact]
    public async Task Other_errors_are_not_retried()
    {
        var throttle = Throttle();
        var attempts = 0;
        await Assert.ThrowsAsync<HttpRequestException>(() => throttle.RunAsync<string>(1, _ =>
        {
            attempts++;
            throw new HttpRequestException("LLM ответила 500", null, HttpStatusCode.InternalServerError);
        }, default));
        Assert.Equal(1, attempts);
    }
}
