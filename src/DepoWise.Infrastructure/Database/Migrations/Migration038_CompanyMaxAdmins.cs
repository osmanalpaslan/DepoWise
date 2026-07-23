using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Firma kotası ayrıştırıldı: 'max_admins' (admin kotası) eklendi. Artık admin ve NORMAL (personel)
/// kullanıcı sayısı AYRI kotalanır: max_admins = admin, max_users = normal (0 = sınırsız). Eski %20 admin
/// kuralı kaldırıldı. machine_quota zaten mevcut (Migration021). Idempotent.
/// </summary>
public sealed class Migration038_CompanyMaxAdmins : IMigration
{
    public int Version => 38;
    public string Name => "company_max_admins";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (ColumnExists(conn, tx, "companies", "max_admins")) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "ALTER TABLE companies ADD COLUMN max_admins INTEGER NOT NULL DEFAULT 0;";
        cmd.ExecuteNonQuery();
    }

    private static bool ColumnExists(DbConnection conn, DbTransaction tx, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
