using System.Collections.Concurrent;

namespace DepoWise.Api;

/// <summary>
/// ⭐ 2026-10-04 — ANLIK EŞİTLEME SİNYALİ (kullanıcı isteği: "sunucu değişiklik algılarsa haber versin,
/// 15 saniyede bir eşitleme yapmak zorunda kalmasın").
///
/// Firma başına tek bir "uyandırma" sinyali tutar. Kimliği doğrulanmış her BAŞARILI yazma isteği
/// (GET dışı) o firmanın sinyalini tetikler; <c>/api/sync/wait</c> ucunda bekleyen masaüstleri anında
/// uyanır, sürümü kontrol eder ve yalnız gerçekten değişiklik varsa çeker.
///
/// Bellek içidir ve tek makine içindir (API tek makinede çalışıyor). Kaçan bir sinyal veri kaybettirmez:
/// bekleme ucu ayrıca belirli aralıklarla sürümü kendisi de kontrol eder (güvenlik ağı). Veritabanı
/// bağlantısı bekleme süresince TUTULMAZ.
/// </summary>
public static class SyncNotifier
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource> _sinyal = new(StringComparer.Ordinal);

    private static TaskCompletionSource Yeni() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Firmada değişiklik oldu: mevcut sinyali tetikler, yerine yenisini koyar (atomik takas).</summary>
    public static void Notify(string? companyId)
    {
        if (string.IsNullOrEmpty(companyId)) return;
        while (true)
        {
            var mevcut = _sinyal.GetOrAdd(companyId, _ => Yeni());
            if (_sinyal.TryUpdate(companyId, Yeni(), mevcut)) { mevcut.TrySetResult(); return; }
        }
    }

    /// <summary>Firmanın sinyalini en fazla <paramref name="sure"/> bekler. true = sinyal geldi.</summary>
    public static async Task<bool> WaitAsync(string companyId, TimeSpan sure, CancellationToken ct)
    {
        var tcs = _sinyal.GetOrAdd(companyId, _ => Yeni());
        try
        {
            var bitti = await Task.WhenAny(tcs.Task, Task.Delay(sure, ct));
            return bitti == tcs.Task;
        }
        catch (OperationCanceledException) { return false; }
    }
}
