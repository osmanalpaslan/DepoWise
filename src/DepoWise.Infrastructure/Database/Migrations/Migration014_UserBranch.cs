using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Kullanıcıya şube ataması: users.branch_id (nullable FK branches). Şube Tanım ekranında şubenin
/// detayında o şubeye atanmış kullanıcılar listelenir. NULL = şubesiz (merkez/atanmamış).
/// </summary>
public sealed class Migration014_UserBranch : IMigration
{
    public int Version => 14;
    public string Name => "user_branch";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
ALTER TABLE users ADD COLUMN branch_id TEXT NULL;
CREATE INDEX ix_users_branch ON users(branch_id);";
        cmd.ExecuteNonQuery();
    }
}
