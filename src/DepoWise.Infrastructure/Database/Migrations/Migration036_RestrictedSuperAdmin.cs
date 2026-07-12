using DepoWise.Application.Security;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// "Kısıtlı Süper Admin" (role-restricted-super-admin) sistem rolü — Admin ile Süper Admin arası.
/// Yalnız süper admin atar; admin bypass'ı yoktur (yalnız açıkça verilen yetkiler + devredilen
/// süper-admin-only ekranlar). Global rol (company_id NULL), tüm firmalarda çözülür.
/// Idempotent: rol zaten varsa tekrar eklemez.
/// </summary>
public sealed class Migration036_RestrictedSuperAdmin : IMigration
{
    public int Version => 36;
    public string Name => "restricted_super_admin_role";

    public void Up(SqliteConnection conn, SqliteTransaction tx)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var check = conn.CreateCommand();
        check.Transaction = tx;
        check.CommandText = "SELECT id FROM roles WHERE role_key=$k AND is_deleted=0;";
        check.Parameters.AddWithValue("$k", RoleKeys.RestrictedSuperAdmin);
        if (check.ExecuteScalar() is not null) return;

        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = @"INSERT INTO roles(id, company_id, role_key, name, is_system, created_at, updated_at, version, is_deleted)
VALUES($id, NULL, $k, 'Kısıtlı Süper Admin', 1, $now, $now, 1, 0);";
        ins.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        ins.Parameters.AddWithValue("$k", RoleKeys.RestrictedSuperAdmin);
        ins.Parameters.AddWithValue("$now", now);
        ins.ExecuteNonQuery();
    }
}
