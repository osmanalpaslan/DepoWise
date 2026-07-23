using DepoWise.Application.Security;
using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Kimlik/yetki temeli: login_attempts (brute-force kilidi) tablosu + sistem rollerinin seed'i.
/// Roller company_id = NULL (tüm firmalar için sistem rolü).
/// </summary>
public sealed class Migration002_AuthSeed : IMigration
{
    public int Version => 2;
    public string Name => "auth_seed";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        Exec(conn, tx, @"
CREATE TABLE login_attempts (
    id TEXT PRIMARY KEY,
    company_id TEXT NULL,
    username TEXT NOT NULL,
    success BIGINT NOT NULL,
    attempted_at BIGINT NOT NULL
);
CREATE INDEX ix_login_attempts_user ON login_attempts(username, attempted_at);

CREATE TABLE sessions (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    company_id TEXT NOT NULL,
    created_at BIGINT NOT NULL,
    expires_at BIGINT NOT NULL,
    revoked_at BIGINT NULL,
    FOREIGN KEY (user_id) REFERENCES users(id)
);
CREATE INDEX ix_sessions_user ON sessions(user_id);
");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var (key, name, isSystem) in RoleKeys.Seed)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO roles(id, company_id, role_key, name, is_system, created_at, updated_at, version, is_deleted)
VALUES(@id, NULL, @key, @name, @sys, @now, @now, 1, 0);";
            cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.AddWithValue("@key", key);
            cmd.AddWithValue("@name", name);
            cmd.AddWithValue("@sys", isSystem ? 1 : 0);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
