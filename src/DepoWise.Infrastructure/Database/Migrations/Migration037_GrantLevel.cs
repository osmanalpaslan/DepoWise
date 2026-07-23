using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Firma Yetki Kontrol modeli: "Global kilit" kaldırıldı; her ekran için firma bazında DÜZEY:
/// Serbest (satır yok) / Admin / Süper Admin. company_grant_limits'e 'level' kolonu eklenir
/// (var olan satırlar 'admin' = eski "kısıtlı" anlamı korunur). Eski DİNAMİK global kilitler
/// her firmaya 'admin' düzeyli satıra çevrilir (davranış korunur), sonra global ayar silinir.
/// Idempotent.
/// </summary>
public sealed class Migration037_GrantLevel : IMigration
{
    public int Version => 37;
    public string Name => "company_grant_level";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        // 1) level kolonu (var olan satırlar = 'admin')
        if (!ColumnExists(conn, tx, "company_grant_limits", "level"))
            Exec(conn, tx, "ALTER TABLE company_grant_limits ADD COLUMN level TEXT NOT NULL DEFAULT 'admin';");

        // 2) Eski dinamik global kilitler → her firmaya 'admin' düzeyli satır, sonra global ayarı sil.
        var raw = Scalar(conn, tx, "SELECT setting_value FROM app_settings WHERE company_id IS NULL AND setting_key='global_grant_limits';");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            foreach (var key in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $@"
INSERT INTO company_grant_limits(id, company_id, module_key, level, created_at)
SELECT {SqlDialect.NewHexId(conn)}, c.id, @k, 'admin', {SqlDialect.NowMs(conn)}
FROM companies c WHERE c.is_deleted=0 ON CONFLICT DO NOTHING;";
                ins.AddWithValue("@k", key);
                ins.ExecuteNonQuery();
            }
            Exec(conn, tx, "DELETE FROM app_settings WHERE company_id IS NULL AND setting_key='global_grant_limits';");
        }
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

    private static string? Scalar(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd.ExecuteScalar() as string;
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
