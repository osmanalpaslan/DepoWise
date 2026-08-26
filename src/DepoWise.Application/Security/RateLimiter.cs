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

    /// <summary>
    /// ⭐ DEN-2026-08-26 — DURUM SÖZLÜĞÜ SINIRSIZ BÜYÜYORDU.
    ///
    /// Anahtarlar istemci IP'sinden üretilir ("pub:" + ip). Süresi dolan pencereler hiç TEMİZLENMİYORDU:
    /// farklı IP'lerden gelen her istek kalıcı bir satır bırakıyordu. Sunucu bellek sınırı 207 MB olduğu
    /// için IP çeşitlendiren bir istek seli sözlüğü büyütüp süreci düşürebilirdi (klasik sınırsız-önbellek
    /// sızıntısı).
    ///
    /// Çözüm: sözlük eşiği aşınca PENCERESİ DOLMUŞ (artık hiçbir kararı etkilemeyen) satırlar atılır.
    /// Karar mantığı DEĞİŞMEZ — atılanlar zaten "now - WindowStart >= _window" ile sıfırlanacak olanlardır.
    /// </summary>
    private const int PurgeThreshold = 5_000;

    private void PurgeExpired(DateTimeOffset now)
    {
        if (_state.Count < PurgeThreshold) return;
        var eskiler = _state.Where(kv => now - kv.Value.WindowStart >= _window).Select(kv => kv.Key).ToList();
        foreach (var k in eskiler) _state.Remove(k);
    }

    public RateLimitResult Check(string key)
    {
        lock (_lock)
        {
            var now = _now();
            PurgeExpired(now);
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

    /// <summary>Yalnız test/teşhis: sözlükte tutulan anahtar sayısı.</summary>
    public int TrackedKeys { get { lock (_lock) return _state.Count; } }
}
