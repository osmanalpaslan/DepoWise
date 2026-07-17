using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Liste ekranı kolon tercihleri — KİŞİSEL (kullanıcı isteği 2026-07-17): "bu ayar işlemleri her
/// kullanıcıya özel olsun. farklı kullanıcıda bu özelleştirme görünmesin." Anahtar (user_id, list_key) —
/// firma değil, doğrudan kullanıcı (aynı firmadaki iki kullanıcı bile birbirinin tercihini görmez).
///
/// list_key ekran anahtarıdır (örn. "materials", "vehicles") — yeni bir liste ekranı için migration
/// GEREKMEZ, yalnız yeni bir list_key değeriyle satır eklenir.
/// </summary>
public sealed class Migration047_UserListPreferences : IMigration
{
    public int Version => 47;
    public string Name => "user_list_preferences";

    public void Up(SqliteConnection conn, SqliteTransaction tx)
    {
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS user_list_preferences(
    user_id TEXT NOT NULL,
    list_key TEXT NOT NULL,
    columns_json TEXT NOT NULL,
    updated_at INTEGER NOT NULL,
    PRIMARY KEY (user_id, list_key)
);");
    }

    private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
