namespace DepoWise.Application.Security;

public sealed record RateLimitResult(bool Allowed, int Remaining, int RetrySeconds);

/// <summary>
/// Sabit pencere rate limiter (login/sync/admin). Fail-closed: limit aşılırsa reddedilir.
/// Web `ratelimit.ts` ile aynı mantık. Saat enjekte edilebilir (deterministik test).
/// </summary>
public sealed class RateLimiter
{
    private readonly int _max;
    private readonly TimeSpan _window;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, (int Count, DateTimeOffset WindowStart)> _state = new();
    private readonly object _lock = new();

    public RateLimiter(int max, TimeSpan window, Func<DateTimeOffset>? now = null)
    {
        _max = max;
        _window = window;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Önceden tanımlı limitler (analiz §9).</summary>
    public static RateLimiter Login(Func<DateTimeOffset>? now = null) => new(5, TimeSpan.FromMinutes(5), now);
    public static RateLimiter SyncPush(Func<DateTimeOffset>? now = null) => new(60, TimeSpan.FromMinutes(1), now);
    public static RateLimiter Admin(Func<DateTimeOffset>? now = null) => new(30, TimeSpan.FromMinutes(1), now);

    public RateLimitResult Check(string key)
    {
        lock (_lock)
        {
            var now = _now();
            if (!_state.TryGetValue(key, out var e) || now - e.WindowStart >= _window)
                e = (0, now);

            if (e.Count >= _max)
            {
                var retry = (int)Math.Ceiling((e.WindowStart + _window - now).TotalSeconds);
                return new RateLimitResult(false, 0, Math.Max(retry, 1));
            }

            e = (e.Count + 1, e.WindowStart);
            _state[key] = e;
            return new RateLimitResult(true, _max - e.Count, 0);
        }
    }

    public void Reset(string key)
    {
        lock (_lock) _state.Remove(key);
    }
}
