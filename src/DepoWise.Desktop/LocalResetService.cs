using System;
using System.Data.Common;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Desktop;

/// <summary>
/// ADR-084 — "Yerel sıfırlama" isteği takibi (bu MAKİNENİN kendi durumu).
///
/// company_local_resets tablosu SUNUCUDA "en son istenen zaman" tutarken, BU MAKİNENİN kendi yerel
/// SQLite dosyasındaki AYNI tablo "bu makinenin en son UYGULADIĞI zaman"ı tutar (aynı şema, iki ayrı
/// dosya, iki ayrı anlam — ADR-084 dokümantasyonuna bakın). Karşılaştırma: sunucu > yerel ise henüz
/// uygulanmamıştır → wipe uygulanır ve yerel satır sunucunun zamanına eşitlenir.
///
/// Silme mantığı ADR-083'teki <see cref="LocalPurgeService.PurgeLocalCompany"/> ile AYNIDIR (firma dahil
/// TÜM company_id'li kayıtlar silinir) — tek fark, bu işlemden sonra giriş ENGELLENMEZ: normal senkron
/// akışı (yeni makinenin ilk girişiyle birebir aynı yol) verileri sıfırdan yeniden doldurur.
/// </summary>
public static class LocalResetService
{
    /// <summary>Bu makinenin bu firma için en son UYGULADIĞI sıfırlama zamanı (hiç uygulamadıysa null).</summary>
    public static long? GetAppliedAt(string companyId)
    {
        try
        {
            using var conn = DesktopServices.Factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT requested_at FROM company_local_resets WHERE company_id=$c;";
            cmd.AddWithValue("$c", companyId);
            var v = cmd.ExecuteScalar();
            return v is null or System.DBNull ? null : Convert.ToInt64(v);
        }
        catch { return null; }
    }

    /// <summary>Bu makine sıfırlamayı uyguladı — sunucudan gelen zamanı yerel iz olarak kaydeder
    /// (bir daha aynı istek için tekrar wipe yapılmasın).</summary>
    public static void MarkApplied(string companyId, long requestedAt, string appliedBy)
    {
        using var conn = DesktopServices.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO company_local_resets(company_id, requested_at, requested_by) VALUES($c,$at,$by) " +
            "ON CONFLICT(company_id) DO UPDATE SET requested_at=$at, requested_by=$by;";
        cmd.AddWithValue("$c", companyId);
        cmd.AddWithValue("$at", requestedAt);
        cmd.AddWithValue("$by", appliedBy);
        cmd.ExecuteNonQuery();
    }
}
