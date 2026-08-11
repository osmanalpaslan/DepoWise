using System.Threading.Tasks;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;   // AddWithValue (DbCommand extension)

namespace DepoWise.Desktop;

/// <summary>
/// Şubeler SUNUCU-OTORİTELİ: yerel kopyayı sunucununkiyle aynalar (sunucudakileri upsert, sunucuda ARTIK
/// OLMAYAN yerel şubeleri is_deleted=1). Çevrimdışıysa (sunucu null) DOKUNMAZ — yerelde olanla devam edilir,
/// böylece internet olmadan da daha önce inmiş depolarla stok işlemi yapılabilir.
///
/// SNK-12 (2026-08-11): Eskiden yalnız GİRİŞTE ve masaüstünden yapılan şube işlemlerinden sonra çağrılıyordu.
/// Oturum açık kalırken web'de yeni bir depo açılırsa masaüstü bunu ÖĞRENMİYORDU → kullanıcı o depoya stok
/// işlemi yapamıyordu (<c>EnsureLocationOwned</c> yerelde bilinmeyen depoyu reddeder).
/// Çözüm mevcut mekanizmayı KULLANIR, yeni protokol eklemez: aynalama artık normal senkron turunda da
/// çağrılır ve <see cref="MinInterval"/> ile kısılır (şube listesi küçük ve nadir değişir → 15 sn'lik
/// senkron kadansında her turda indirmek israf olurdu).
/// </summary>
public static class BranchMirror
{
    /// <summary>İki aynalama arasındaki en kısa süre. Senkron turu bundan sık çağırsa bile atlanır.</summary>
    public static readonly System.TimeSpan MinInterval = System.TimeSpan.FromMinutes(2);

    private static System.DateTimeOffset _lastRefresh = System.DateTimeOffset.MinValue;

    /// <summary>Kısıtlamayı sıfırlar (test ve "hemen tazele" akışları için).</summary>
    public static void ResetThrottle() => _lastRefresh = System.DateTimeOffset.MinValue;

    /// <summary>
    /// Sunucudaki şube listesini yerele aynalar.
    /// <paramref name="force"/> = true → kısıtlama YOK (giriş, masaüstünden şube ekleme/silme sonrası).
    /// false → en fazla <see cref="MinInterval"/>'de bir (senkron turu).
    /// </summary>
    public static async Task RefreshAsync(string companyId, bool force = true)
    {
        if (!force && System.DateTimeOffset.UtcNow - _lastRefresh < MinInterval) return;
        try
        {
            var online = await ServerAuthClient.GetLoginBranchesAsync(companyId);
            if (online is null) return; // çevrimdışı → yerelde zaten olanla devam
            _lastRefresh = System.DateTimeOffset.UtcNow;

            var rows = new System.Collections.Generic.List<(string Id, string Name, string? Code)>();
            foreach (var b in online)
            {
                if (b.Id == BranchConstants.AllBranchesId) continue;
                rows.Add((b.Id, b.Name, b.Code));
            }
            Apply(DesktopServices.Factory, companyId, rows);
        }
        catch { }
    }

    /// <summary>Saf aynalama Infrastructure'dadır (<see cref="DepoWise.Infrastructure.Organization.BranchMirrorApply"/>) —
    /// Avalonia bağımlılığı olmadan test edilebilsin diye. Burası yalnız AĞ tarafıdır.</summary>
    public static void Apply(IDbConnectionFactory factory, string companyId,
        System.Collections.Generic.IReadOnlyList<(string Id, string Name, string? Code)> rows)
        => DepoWise.Infrastructure.Organization.BranchMirrorApply.Run(factory, companyId, rows);
}
