using System.Collections.Concurrent;

namespace DepoWise.Application.Security;

/// <summary>
/// F0 (YET-01, 2026-08-10) — Süreç içi yetki fotoğrafı önbelleği.
///
/// <b>Bağımlılık eklenmedi:</b> <c>Microsoft.Extensions.Caching.Memory</c> paketi bu projelerde referanslı
/// DEĞİL; önbellek BCL <see cref="ConcurrentDictionary{TKey,TValue}"/> ile yazıldı (sıfır maliyet kuralı).
///
/// <b>Tenant izolasyonu:</b> anahtar <c>companyId + '|' + userId</c>. Aynı kullanıcı farklı firma
/// bağlamında (süper adminin çapraz-firma oturumu) AYRI girdidir; karışma olamaz.
///
/// <b>Yetki KAYBI gecikmemelidir:</b> TTL yalnız üst sınır güvencesidir. Yetki/rol yazan her nokta
/// <see cref="InvalidateUser"/> ya da <see cref="InvalidateAll"/> çağırır → etki ANINDA görünür.
///
/// <b>Devre dışı bırakılabilir:</b> Servislere bu nesne verilmezse (null) önbellek hiç devreye girmez ve
/// davranış F0 öncesiyle birebir aynıdır. Testler "önbelleksiz vs önbellekli" karşılaştırmasını böyle yapar.
/// </summary>
public sealed class PermissionSnapshotCache
{
    private sealed record Entry(PermissionSnapshot Snapshot, long ExpiresAtUnixMs);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly long _ttlMs;

    /// <summary>Varsayılan yaşam süresi: 90 sn (YET-01 kararı: 60–120 sn aralığı).</summary>
    public const int DefaultTtlSeconds = 90;

    public PermissionSnapshotCache(int ttlSeconds = DefaultTtlSeconds)
    {
        if (ttlSeconds < 1) throw new ArgumentOutOfRangeException(nameof(ttlSeconds));
        _ttlMs = ttlSeconds * 1000L;
    }

    /// <summary>Tanılama/test: o an önbellekte duran (süresi dolmamış) girdi sayısı.</summary>
    public int Count => _entries.Count;

    private static string Key(string companyId, string userId) => companyId + "|" + userId;

    /// <summary>
    /// Fotoğrafı önbellekten verir; yoksa/süresi dolduysa <paramref name="load"/> ile üretip saklar.
    /// <paramref name="load"/> <c>null</c> dönerse (kullanıcı yok/pasif/çapraz-firma reddi) HİÇBİR ŞEY
    /// saklanmaz — olumsuz sonuç önbelleğe alınmaz, fail-closed davranış her istekte yeniden değerlendirilir.
    /// </summary>
    public PermissionSnapshot? GetOrLoad(string companyId, string userId, Func<PermissionSnapshot?> load)
    {
        var key = Key(companyId, userId);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (_entries.TryGetValue(key, out var hit) && hit.ExpiresAtUnixMs > now)
            return hit.Snapshot;

        var fresh = load();
        if (fresh is null)
        {
            _entries.TryRemove(key, out _);   // eski girdi varsa artık geçersiz
            return null;
        }

        _entries[key] = new Entry(fresh, now + _ttlMs);
        return fresh;
    }

    /// <summary>Bir kullanıcının TÜM firma bağlamlarındaki girdilerini düşürür (yetki/rol değişimi).</summary>
    public void InvalidateUser(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return;
        var suffix = "|" + userId;
        foreach (var k in _entries.Keys)
            if (k.EndsWith(suffix, StringComparison.Ordinal))
                _entries.TryRemove(k, out _);
    }

    /// <summary>Tüm girdileri düşürür. ROL SEVİYESİ değişiminde kullanılır (Rol Yetki Kontrol):
    /// bir rolün kısıtı değişince o role sahip HERKES etkilenir; kimlerin etkilendiğini ayrıca
    /// sorgulamak yerine tamamı düşürülür — güvenli taraf (yetki kaybı gecikmez).</summary>
    public void InvalidateAll() => _entries.Clear();
}
