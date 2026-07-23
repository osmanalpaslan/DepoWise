using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Tanım Düzenle: "sabit tanımlar" (kullanıcı isteği 2026-07-19 — "hem + butonu alanları hem de + butonu
/// olmayan sabit tanımlar alanları olmalı; sabit tanımları silme/düzenleme yapamasınlar ama yeni tanım
/// ekleyebilsinler"). Her tanım satırı ayrı ayrı kilitlenebilir (`is_locked`); kilitli satır yeniden
/// adlandırılamaz/silinemez, kilit YALNIZ admin tarafından açılıp kapatılır (bkz. <c>LookupService.SetLocked</c>).
/// Yeni tanım ekleme (+ butonu) kilitten bağımsız — her zaman açık. Varsayılan 0 (kilitsiz); hiçbir mevcut
/// satır bu migration ile otomatik kilitlenmez.
/// </summary>
public sealed class Migration051_LookupLocked : IMigration
{
    public int Version => 51;
    public string Name => "lookup_locked";

    private static readonly string[] Tables =
    {
        "material_categories", "brands", "units", "suppliers",
        "vehicle_types", "vehicle_categories", "vehicle_models", "branches",
    };

    public void Up(DbConnection conn, DbTransaction tx)
    {
        foreach (var table in Tables)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN is_locked INTEGER NOT NULL DEFAULT 0;";
            cmd.ExecuteNonQuery();
        }
    }
}
