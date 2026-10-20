using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-41 (AR-GE 2026-10-19, 05 F-116) — YASAL PERİYODİK KONTROL ALANLARI ═══
/// <c>vehicle_inspections</c> ve <c>equipment_inspections</c>'a <c>inspector</c> (yetkili kişi / firma) ve <c>report_no</c>
/// (kontrol raporu numarası) eklenir; yeni belge türü "periodic" (Periyodik Kontrol). Sonraki tarih uyarısı mevcut belge
/// uyarı akışından gelir. Denetimde "kim kontrol etti, rapor hangisi" kanıtı. Yalnız ekleme.
/// </summary>
public sealed class Migration115_PeriodicControl : IMigration
{
    public int Version => 115;
    public string Name => "periodic_control";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        foreach (var tablo in new[] { "vehicle_inspections", "equipment_inspections" })
            foreach (var kolon in new[] { "inspector", "report_no" })
            {
                if (DbIntrospect.ColumnExists(conn, tx, tablo, kolon)) continue;
                using var c = conn.CreateCommand();
                c.Transaction = tx;
                c.CommandText = $"ALTER TABLE {tablo} ADD COLUMN {kolon} TEXT NULL;";
                c.ExecuteNonQuery();
            }
    }
}
