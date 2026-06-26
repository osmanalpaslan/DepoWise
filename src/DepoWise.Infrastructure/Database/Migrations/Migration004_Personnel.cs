using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Personel kartı + kullanıcı şube/şantiye kapsamı (user_scopes).
/// user_scopes: bir kullanıcının erişebileceği şubeler; satır yoksa admin tüm firma şubelerini görür.
/// </summary>
public sealed class Migration004_Personnel : IMigration
{
    public int Version => 4;
    public string Name => "personnel_and_scopes";

    public void Up(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE personnel (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    branch_id TEXT NULL,
    full_name TEXT NOT NULL,
    title TEXT NULL,
    phone TEXT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id),
    FOREIGN KEY (branch_id) REFERENCES branches(id)
);
CREATE INDEX ix_personnel_company ON personnel(company_id, is_deleted);
CREATE INDEX ix_personnel_branch ON personnel(branch_id);

CREATE TABLE user_scopes (
    user_id TEXT NOT NULL,
    company_id TEXT NOT NULL,
    branch_id TEXT NOT NULL,
    PRIMARY KEY (user_id, branch_id),
    FOREIGN KEY (user_id) REFERENCES users(id),
    FOREIGN KEY (branch_id) REFERENCES branches(id)
);
CREATE INDEX ix_user_scopes_user ON user_scopes(user_id);";
        cmd.ExecuteNonQuery();
    }
}
