using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// İÇE / DIŞA AKTARIM YETKİ AYRIMI (2026-07-26, kullanıcı isteği): tek <c>import_export</c> modülü
/// artık YALNIZ İÇE AKTARIM (import) yetkisidir; DIŞA AKTARIM için ayrı <c>export</c> modülü eklendi
/// (deny-by-default; menü + liste ekranlarındaki "Excel'e Aktar" butonları buna tabi).
///
/// GERİYE DÖNÜK UYUM: eskiden <c>import_export</c> olan kullanıcı hem içe hem dışa aktarabiliyordu.
/// Bölünme sonrası dışa aktarımı SESSİZCE kaybetmesin diye, mevcut <c>import_export</c> izni olan her
/// kullanıcıya eşdeğer <c>export</c> izni (aynı bayraklar) verilir. Admin/süper admin zaten bypass'lıdır.
/// Idempotent: zaten <c>export</c> satırı olan kullanıcı atlanır. Portable (SQLite + PostgreSQL).
/// </summary>
public sealed class Migration056_ExportPermission : IMigration
{
    public int Version => 56;
    public string Name => "export_permission";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        // 1) import_export izni olup henüz export satırı OLMAYAN kullanıcıları topla.
        var rows = new List<(string CompanyId, string UserId, long V, long C, long E, long D)>();
        using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = @"
SELECT p.company_id, p.user_id, p.can_view, p.can_create, p.can_edit, p.can_delete
FROM user_permissions p
WHERE p.module_key = 'import_export'
  AND NOT EXISTS (SELECT 1 FROM user_permissions e WHERE e.user_id = p.user_id AND e.module_key = 'export');";
            using var r = read.ExecuteReader();
            while (r.Read())
                rows.Add((r.GetString(0), r.GetString(1),
                    r.GetInt64(2), r.GetInt64(3), r.GetInt64(4), r.GetInt64(5)));
        }

        if (rows.Count == 0) return;   // yapılacak bir şey yok (idempotent)

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var row in rows)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = @"
INSERT INTO user_permissions(id, company_id, user_id, module_key, can_view, can_create, can_edit, can_delete, created_at, updated_at, version)
VALUES(@id, @c, @u, 'export', @v, @cr, @e, @d, @now, @now, 1);";
            ins.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            ins.AddWithValue("@c", row.CompanyId);
            ins.AddWithValue("@u", row.UserId);
            ins.AddWithValue("@v", row.V);
            ins.AddWithValue("@cr", row.C);
            ins.AddWithValue("@e", row.E);
            ins.AddWithValue("@d", row.D);
            ins.AddWithValue("@now", now);
            ins.ExecuteNonQuery();
        }
    }
}
