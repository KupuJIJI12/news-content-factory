using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;

namespace NewsFactory.Llm;

/// <summary>Section "Llm:Throttle" of the configuration.</summary>
public sealed class LlmThrottleOptions
{
    /// <summary>Minimum pause between the starts of two requests to one provider.</summary>
    public int MinIntervalMs { get; set; } = 2000;

    /// <summary>Pauses before retrying after 429 Too Many Requests; when they are used up the work is postponed.</summary>
    public int[] RateLimitBackoffSeconds { get; set; } = [15, 60];

    /// <summary>How long a job waits before trying again after the provider kept refusing.</summary>
    public int PostponeMinutes { get; set; } = 10;
}

/// <summary>
/// Process-wide gate in front of each LLM provider: one request at a time, a minimum pause between requests,
/// and on 429 a longer pause for everybody before one retry. Keeps GigaChat's free tier from rejecting bursts.
/// </summary>
public sealed class LlmThrottle(IOptions<LlmThrottleOptions> options, TimeProvider time)
{
    private sealed class Gate
    {
        public readonly SemaphoreSlim Lock = new(1, 1);
        public DateTimeOffset NextStart = DateTimeOffset.MinValue;
    }

    private readonly ConcurrentDictionary<long, Gate> _gates = new();
    private readonly LlmThrottleOptions _o = options.Value;

    /// <summary>Replaced in tests so pauses do not take real time.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (d, ct) => Task.Delay(d, time, ct);

    public async Task<T> RunAsync<T>(long providerId, Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        var gate = _gates.GetOrAdd(providerId, _ => new Gate());
        await gate.Lock.WaitAsync(ct);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var wait = gate.NextStart - time.GetUtcNow();
                if (wait > TimeSpan.Zero) await Delay(wait, ct);
                gate.NextStart = time.GetUtcNow().AddMilliseconds(_o.MinIntervalMs);
                try
                {
                    return await call(ct);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (attempt >= _o.RateLimitBackoffSeconds.Length)
                        throw new LlmRateLimitedException(time.GetUtcNow().AddMinutes(_o.PostponeMinutes), ex);
                    // Everybody waits, not only this call: the next request to the provider starts after the pause.
                    gate.NextStart = time.GetUtcNow().AddSeconds(_o.RateLimitBackoffSeconds[attempt]);
                }
            }
        }
        finally
        {
            gate.Lock.Release();
        }
    }
}
