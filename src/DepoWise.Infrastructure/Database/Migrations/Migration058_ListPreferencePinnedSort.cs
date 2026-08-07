using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Liste ekranı KİŞİSEL tercihine iki alan daha ekler (Birim 4 / ortak tablo bileşeni, kullanıcı isteği
/// 2026-08-07). Bu fazda UI'da AKTİF DEĞİL — yalnız GELECEĞE HAZIR altyapı (kullanıcı isteği: "gelecekte
/// aktif edilecek şekilde tasarlanmalıdır"):
///  • <c>pinned_json</c> — sabitlenen (pinned) kolon anahtarları (JSON dizi). İleride kolon dondurma.
///  • <c>sort_json</c>   — kullanıcının kaydettiği varsayılan sıralama ({"key":"...","desc":true|false}).
///
/// Aynı (user_id, list_key) satırında tutulur (bkz. Migration047/049). İkisi de NULL olabilir → o zaman
/// ekran kendi varsayılanını kullanır (sabitli kolon yok, sıralama yok). Idempotent (sütun varsa atlar);
/// lehçe-duyarlı (SQLite masaüstü ↔ PostgreSQL sunucu, bkz. DbIntrospect).
/// </summary>
public sealed class Migration058_ListPreferencePinnedSort : IMigration
{
    public int Version => 58;
    public string Name => "list_preference_pinned_sort";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        AddColumnIfMissing(conn, tx, "user_list_preferences", "pinned_json", "TEXT");
        AddColumnIfMissing(conn, tx, "user_list_preferences", "sort_json", "TEXT");
    }

    private static void AddColumnIfMissing(DbConnection conn, DbTransaction tx, string table, string col, string type)
    {
        if (DbIntrospect.ColumnExists(conn, tx, table, col)) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {col} {type};";
        cmd.ExecuteNonQuery();
    }
}
