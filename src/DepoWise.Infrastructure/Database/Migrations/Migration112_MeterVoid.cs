using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-36 (AR-GE 2026-10-19, 05 F-078) — SAYAÇ OKUMASINI "GEÇERSİZ SAY" ═══
/// <c>fuel_distributions.meter_void</c>, <c>vehicle_maintenances.meter_void</c>, <c>vehicle_meter_logs.is_void</c> (0/1):
/// kayıt (litre, bakım, maliyet) GEÇERLİ kalır ama sayacı yanlış okunmuşsa aracın sayaç hesabına GİRMEZ — yanlış bakım
/// uyarısı ve kilitli başlangıç sayacı sorunu kaydı iptal etmeden çözülür. Varsayılan 0 (davranış aynen). Yalnız ekleme.
/// </summary>
public sealed class Migration112_MeterVoid : IMigration
{
    public int Version => 112;
    public string Name => "meter_void";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        void Ekle(string tablo, string kolon)
        {
            if (DbIntrospect.ColumnExists(conn, tx, tablo, kolon)) return;
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = $"ALTER TABLE {tablo} ADD COLUMN {kolon} BIGINT NOT NULL DEFAULT 0;";
            c.ExecuteNonQuery();
        }
        Ekle("fuel_distributions", "meter_void");
        Ekle("vehicle_maintenances", "meter_void");
        Ekle("vehicle_meter_logs", "is_void");
    }
}
