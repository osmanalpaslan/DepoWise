using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Çalışan Yönetimi (#6): kullanıcı hesabı ile personel kaydı bağı. users.personnel_id NULL olabilir
/// (hesabı personele bağlı olmayan süper admin/admin gibi kullanıcılar için). Bir personele en fazla BİR
/// kullanıcı bağlanır → kısmi tekil index (silinmemiş kayıtlar arasında personnel_id benzersiz).
/// </summary>
public sealed class Migration033_UserPersonnelLink : IMigration
{
    public int Version => 33;
    public string Name => "user_personnel_link";

    public void Up(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
ALTER TABLE users ADD COLUMN personnel_id TEXT NULL;
CREATE UNIQUE INDEX ux_users_personnel ON users(personnel_id) WHERE personnel_id IS NOT NULL AND is_deleted=0;";
        cmd.ExecuteNonQuery();
    }
}
