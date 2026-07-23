using DepoWise.Infrastructure.Database;
using System;

namespace DepoWise.Desktop;

/// <summary>
/// ADR-085 — "Makine tanımı sıfırlama" izleme (bu MAKİNENİN kendi durumu).
///
/// machine_resets tablosu SUNUCUDA "bu makine için en son istenen sıfırlama zamanı" tutarken, BU
/// MAKİNENİN kendi yerel SQLite dosyasındaki AYNI tablo "bu makinenin en son UYGULADIĞI zaman"ı tutar
/// (ADR-084/company_local_resets ile AYNI iki-anlamlı desen — bkz. Migration046_MachineReset).
/// Karşılaştırma: sunucu &gt; yerel ise henüz uygulanmamıştır → LoginViewModel yerel makine önbelleğini
/// (firma/şube) temizler ve login ekranına döner.
/// </summary>
public static class MachineResetLocalService
{
    /// <summary>Bu makinenin en son UYGULADIĞI sıfırlama zamanı (hiç uygulamadıysa null).</summary>
    public static long? GetAppliedAt(string machineName)
    {
        try
        {
            using var conn = DesktopServices.Factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT requested_at FROM machine_resets WHERE machine_name=$n;";
            cmd.AddWithValue("$n", machineName);
            var v = cmd.ExecuteScalar();
            return v is null or DBNull ? null : Convert.ToInt64(v);
        }
        catch { return null; }
    }

    /// <summary>Bu makine sıfırlamayı uyguladı — sunucudan gelen zamanı yerel iz olarak kaydeder
    /// (bir daha aynı istek için tekrar uygulanmasın).</summary>
    public static void MarkApplied(string machineName, long requestedAt, string appliedBy)
    {
        using var conn = DesktopServices.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO machine_resets(machine_name, requested_at, requested_by) VALUES($n,$at,$by) " +
            "ON CONFLICT(machine_name) DO UPDATE SET requested_at=$at, requested_by=$by;";
        cmd.AddWithValue("$n", machineName);
        cmd.AddWithValue("$at", requestedAt);
        cmd.AddWithValue("$by", appliedBy);
        cmd.ExecuteNonQuery();
    }
}
