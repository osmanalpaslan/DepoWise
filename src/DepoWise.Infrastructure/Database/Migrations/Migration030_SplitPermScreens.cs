using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// #15 — Bazı ekranlar ayrı yetki aldı: vehicle_templates (eski: vehicles), quota_monitor (eski: users).
/// Mevcut kullanıcıların ÜST modül izinleri yeni anahtarlara KOPYALANIR → erişim korunur (deny-by-default'la
/// menüden düşmesin). Admin/süper admin zaten bypass; bu yalnız açık izinli Personel'leri etkiler.
/// Idempotent: yeni anahtar zaten varsa tekrar eklemez.
/// </summary>
public sealed class Migration030_SplitPermScreens : IMigration
{
    public int Version => 30;
    public string Name => "split_perm_screens";

    public void Up(SqliteConnection conn, SqliteTransaction tx)
    {
        CopyPerm(conn, tx, "vehicles", "vehicle_templates");
        CopyPerm(conn, tx, "users", "quota_monitor");
    }

    private static void CopyPerm(SqliteConnection conn, SqliteTransaction tx, string from, string to)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO user_permissions(id, company_id, user_id, module_key, can_view, can_create, can_edit, can_delete, created_at, updated_at, version)
SELECT lower(hex(randomblob(16))), company_id, user_id, $to, can_view, can_create, can_edit, can_delete, created_at, updated_at, 1
FROM user_permissions p
WHERE p.module_key = $from
  AND NOT EXISTS (SELECT 1 FROM user_permissions q WHERE q.user_id = p.user_id AND q.module_key = $to);";
        cmd.Parameters.AddWithValue("$from", from);
        cmd.Parameters.AddWithValue("$to", to);
        cmd.ExecuteNonQuery();
    }
}
