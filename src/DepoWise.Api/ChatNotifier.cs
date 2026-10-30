using System.Collections.Concurrent;

namespace DepoWise.Api;

/// <summary>
/// ⭐ 2026-10-30 — ANLIK SOHBET SİNYALİ (kullanıcı isteği: "sohbet anlık olsun"; aralıklı yoklama 3/20 sn idi).
///
/// <see cref="SyncNotifier"/> ile AYNI desen, ama anahtar FİRMA + KULLANICI: yalnız ilgili kişi uyanır. Mesaj gönderilince alıcı ve
/// gönderenin (diğer cihazları) işareti, mesaj okununca karşı tarafınki ("Görüldü") ilerletilir. <c>/api/chat/wait</c> ucunda bekleyen
/// istemci anında uyanır ve normal, yetki/firma denetimli uçlardan çeker — işaret İÇERİK taşımaz.
///
/// <para><b>Sürüm</b> kullanıcı başına artan bir sayıdır: <c>max(önceki+1, şimdi_ms)</c>. Sunucu yeniden başlayınca sayılar sıfırlanır ama
/// ilk yeni olay şimdiki zamanı alır → istemcinin elindeki eski sürümü geçer; olay kaçmaz. Bellek içidir ve tek makine içindir (API tek
/// makinede). Kaçan bir sinyal veri kaybettirmez: istemciler yedek olarak seyrek yoklamayı sürdürür.</para>
/// </summary>
public static class ChatNotifier
{
    private sealed class Durum
    {
        public long Surum;
        public TaskCompletionSource Sinyal = Yeni();
    }

    private static readonly ConcurrentDictionary<string, Durum> _durum = new(StringComparer.Ordinal);

    private static TaskCompletionSource Yeni() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string Anahtar(string companyId, string userId) => companyId + "|" + userId;

    /// <summary>Kullanıcının sohbet sürümü (olay yoksa 0).</summary>
    public static long Surum(string companyId, string userId)
        => _durum.TryGetValue(Anahtar(companyId, userId), out var d) ? Interlocked.Read(ref d.Surum) : 0;

    /// <summary>Kullanıcı için yeni olay: sürümü ilerletir ve bekleyenleri uyandırır.</summary>
    public static void Bildir(string? companyId, string? userId)
    {
        if (string.IsNullOrEmpty(companyId) || string.IsNullOrEmpty(userId)) return;
        var d = _durum.GetOrAdd(Anahtar(companyId, userId), _ => new Durum());
        TaskCompletionSource eski;
        lock (d)
        {
            var simdi = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Interlocked.Exchange(ref d.Surum, Math.Max(d.Surum + 1, simdi));
            eski = d.Sinyal;
            d.Sinyal = Yeni();
        }
        eski.TrySetResult();
    }

    /// <summary>
    /// Kullanıcının sürümü <paramref name="since"/>'ten büyükse HEMEN, değilse olay gelene ya da <paramref name="sure"/> dolana kadar
    /// bekler; güncel sürümü döner. Sürüm okuma ile işarete abone olma AYNI kilitte yapılır → aradaki olay kaçmaz.
    /// </summary>
    public static async Task<long> BekleAsync(string companyId, string userId, long since, TimeSpan sure, CancellationToken ct)
    {
        var d = _durum.GetOrAdd(Anahtar(companyId, userId), _ => new Durum());
        Task sinyal;
        lock (d)
        {
            if (d.Surum > since) return d.Surum;
            sinyal = d.Sinyal.Task;
        }
        try { await Task.WhenAny(sinyal, Task.Delay(sure, ct)); }
        catch (OperationCanceledException) { }
        return Surum(companyId, userId);
    }
}
