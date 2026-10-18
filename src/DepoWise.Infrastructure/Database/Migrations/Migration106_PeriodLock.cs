using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-30 (AR-GE 2026-10-18, 05 F-076) — DÖNEM KİLİDİ ═══
/// <c>companies.period_lock_date</c> (UTC gün başı ms, NULL = kilit yok). Yalnız ekleme; mevcut firmalarda kilit YOK →
/// davranış değişmez. Sunucu otoriteli: yönetici web/masaüstünden API ile ayarlar, masaüstü giriş paketiyle alır.
/// </summary>
public sealed class Migration106_PeriodLock : IMigration
{
    public int Version => 106;
    public string Name => "period_lock";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (DbIntrospect.ColumnExists(conn, tx, "companies", "period_lock_date")) return;
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "ALTER TABLE companies ADD COLUMN period_lock_date BIGINT NULL;";
        c.ExecuteNonQuery();
    }
}
