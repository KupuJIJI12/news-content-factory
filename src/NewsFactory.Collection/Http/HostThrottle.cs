namespace NewsFactory.Collection.Http;

/// <summary>Spaces requests to the same host at least <c>minInterval</c> apart.</summary>
public sealed class HostThrottle(TimeProvider time, TimeSpan minInterval)
{
    private readonly Dictionary<string, DateTimeOffset> _next = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public Task WaitAsync(string host, CancellationToken ct)
    {
        TimeSpan delay;
        lock (_lock)
        {
            var now = time.GetUtcNow();
            var slot = _next.TryGetValue(host, out var next) && next > now ? next : now;
            _next[host] = slot + minInterval;
            delay = slot - now;
        }
        return delay > TimeSpan.Zero ? Task.Delay(delay, time, ct) : Task.CompletedTask;
    }
}
