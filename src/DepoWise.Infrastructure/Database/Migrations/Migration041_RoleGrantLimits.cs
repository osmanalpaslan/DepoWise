using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Rol Yetki Kontrol (yalnız Süper Admin): bir ekranın belirli bir ROLE verilmesini yasaklar.
/// Satır VARSA = o modül o rol için KAPALI (yetki ağacında görünmez, verilmiş olsa bile erişim reddedilir).
/// Satır yoksa = serbest. Platform geneli (firma bağımsız) — firma ekseni company_grant_limits'tedir.
/// Idempotent.
/// </summary>
public sealed class Migration041_RoleGrantLimits : IMigration
{
    public int Version => 41;
    public string Name => "role_grant_limits";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS role_grant_limits (
    id          TEXT PRIMARY KEY,
    role_key    TEXT NOT NULL,
    module_key  TEXT NOT NULL,
    created_at  BIGINT NOT NULL,
    UNIQUE(role_key, module_key)
);");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_role_grant_limits_role ON role_grant_limits(role_key);");
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
