using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Özel buton ("+") izinleri: deny-by-default. Bir kullanıcıya yalnız verilen buton anahtarları kaydedilir.
/// Kayıt yoksa buton gizli (admin bypass değerlendiricide). Modül yetkileri user_permissions'ta ayrı.
/// </summary>
public sealed class Migration015_UserButtonPermissions : IMigration
{
    public int Version => 15;
    public string Name => "user_button_permissions";

    public void Up(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE user_button_permissions (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    button_key TEXT NOT NULL,
    created_at INTEGER NOT NULL,
    FOREIGN KEY (user_id) REFERENCES users(id)
);
CREATE UNIQUE INDEX ux_user_buttons ON user_button_permissions(user_id, button_key);";
        cmd.ExecuteNonQuery();
    }
}
