using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Liste ekranı KİŞİSEL tercihine iki alan ekler (kullanıcı isteği 2026-07-18):
///  • <c>page_size</c> — "sayfa boyutu daha önce değiştirilmemişse varsayılan 25; değiştirmişse hatırlansın".
///  • <c>widths_json</c> — "kolon genişliklerini manuel ayarlayabilmeli" ayarı kişiye özel hatırlansın
///    (kolon anahtarı → piksel genişlik JSON).
///
/// Aynı (user_id, list_key) satırında tutulur (bkz. Migration047). İkisi de NULL olabilir → o zaman ekran
/// kendi varsayılanını kullanır (page_size=25, kolonlar otomatik genişlik). Idempotent (sütun varsa atlar).
/// </summary>
public sealed class Migration049_ListPreferenceExtras : IMigration
{
    public int Version => 49;
    public string Name => "list_preference_extras";

    public void Up(SqliteConnection conn, SqliteTransaction tx)
    {
        AddColumnIfMissing(conn, tx, "user_list_preferences", "page_size", "INTEGER");
        AddColumnIfMissing(conn, tx, "user_list_preferences", "widths_json", "TEXT");
    }

    private static void AddColumnIfMissing(SqliteConnection conn, SqliteTransaction tx, string table, string col, string type)
    {
        bool exists = false;
        using (var check = conn.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name=$n;";
            check.Parameters.AddWithValue("$n", col);
            exists = System.Convert.ToInt64(check.ExecuteScalar()) > 0;
        }
        if (exists) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {col} {type};";
        cmd.ExecuteNonQuery();
    }
}
