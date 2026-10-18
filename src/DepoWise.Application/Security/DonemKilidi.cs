namespace DepoWise.Application.Security;

/// <summary>
/// ⭐ M-30 (AR-GE 2026-10-18, 05 F-076) — DÖNEM KİLİDİ (ay kapanışı).
///
/// Yönetici bir "kapanış tarihi" verir; o gün DAHİL öncesine tarihlenen kayıt girilemez, mevcut kayıt iptal/düzeltme ile
/// değiştirilemez → kapanmış ayın raporu sonradan değişmez. Tarih firma başınadır (<c>companies.period_lock_date</c>, UTC gün
/// başı) ve oturumla taşınır (<see cref="SessionContext.DonemKilidi"/>); masaüstü ve API AYNI kontrolden geçer.
/// Kilit yoksa (null) hiçbir şey değişmez (bugünkü davranış).
/// </summary>
public static class DonemKilidi
{
    public const long GunMs = 86_400_000L;

    /// <summary>İşlem tarihi kapanmış döneme düşüyorsa anlaşılır bir hata fırlatır (API 400).</summary>
    public static void Dogrula(SessionContext s, long? tarihMs, string islem = "kayıt")
    {
        if (tarihMs is not { } t || s.DonemKilidi is not { } k) return;
        if (t < k + GunMs)
            throw new ArgumentException($"{Tarih(k)} tarihine kadarki dönem kapatıldı; bu tarihli {islem} yapılamaz.");
    }

    /// <summary>Kilit tarihinin gösterimi (gün.ay.yıl, UTC gün).</summary>
    public static string Tarih(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("dd.MM.yyyy");
}
