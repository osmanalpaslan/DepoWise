using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-06 (AR-GE 2026-10-17, 05 F-035) — MASAÜSTÜ İŞLEM GEÇMİŞİ SUNUCUYA ═══
///
/// <b>Sorun.</b> Masaüstünde yapılan işlemlerin geçmişi (kim, ne zaman, neyi değiştirdi — <c>audit_logs</c>) yalnız o
/// bilgisayarda kalıyordu: yönetici web'den göremiyor, bilgisayar bozulunca kanıt kayboluyordu.
///
/// <b>Çözüm.</b> <c>audit_logs</c> eşitleme listesine YALNIZ GÖNDERİM yönünde eklendi (BusinessSyncService.YalnizGonderim):
/// sunucuda yalnız eklenir (LedgerTables), masaüstüne inmez. Bu migration: <c>source_device</c> kolonu (sunucu, satırın hangi
/// bilgisayardan geldiğini yazar) + mevcut masaüstü veritabanlarında yerel değişiklik sayacı (Migration100 yeni kurulumda
/// listeyi zaten kapsar). Yalnız ekleme.
/// </summary>
public sealed class Migration103_AuditPush : IMigration
{
    public int Version => 103;
    public string Name => "audit_push";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (!DbIntrospect.ColumnExists(conn, tx, "audit_logs", "source_device"))
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "ALTER TABLE audit_logs ADD COLUMN source_device TEXT NULL;";
            c.ExecuteNonQuery();
        }
        Migration100_LocalChangeSeq.YerelSayacKur(conn, tx, "audit_logs");
    }
}
