using System.Data.Common;

namespace DepoWise.Infrastructure.Database;

/// <summary>
/// PostgreSQL geçişi — Faz 2 Adım 4 (2026-07-23): şema SORGULAMA (introspection) — lehçe-duyarlı.
///
/// Eski kod her yerde SQLite'a özel <c>PRAGMA table_info</c> / <c>sqlite_master</c> kullanıyordu; PostgreSQL'de
/// bunlar YOK, karşılığı <c>information_schema</c>'dır. Bu yardımcı ikisini de bilir. Migration'lar (idempotent
/// "kolon var mı?" kontrolleri) ve BusinessSyncService (generic snapshot/upsert için kolon/PK listesi) buraya bağlıdır.
/// SQLite tarafında eski davranış BİREBİR korunur (569 test), PostgreSQL'de eşdeğeri çalışır.
/// </summary>
public static class DbIntrospect
{
    /// <summary>Tabloda bu kolon var mı? (migration idempotency)</summary>
    public static bool ColumnExists(DbConnection conn, DbTransaction? tx, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        // SQLite: CreateCommand() bağlantının açık transaction'ını OTOMATİK atar; null ile ezersek
        // "pending transaction" hatası olur. Bu yüzden yalnız gerçek tx verilmişse ata.
        if (tx != null) cmd.Transaction = tx;
        if (SqlDialect.IsSqlite(conn))
        {
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        cmd.CommandText = "SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name=@t AND column_name=@c LIMIT 1;";
        cmd.AddWithValue("@t", table);
        cmd.AddWithValue("@c", column);
        return cmd.ExecuteScalar() is not null and not DBNull;
    }

    /// <summary>Tablo var mı?</summary>
    public static bool TableExists(DbConnection conn, DbTransaction? tx, string table)
    {
        using var cmd = conn.CreateCommand();
        if (tx != null) cmd.Transaction = tx;
        if (SqlDialect.IsSqlite(conn))
            cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n LIMIT 1;";
        else
            cmd.CommandText = "SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_name=@n LIMIT 1;";
        cmd.AddWithValue("@n", table);
        return cmd.ExecuteScalar() is not null and not DBNull;
    }

    /// <summary>Tablonun kolon adları (küçük harf duyarsız küme).</summary>
    public static HashSet<string> ColumnNames(DbConnection conn, string table)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        if (SqlDialect.IsSqlite(conn))
        {
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var r = cmd.ExecuteReader();
            while (r.Read()) set.Add(r.GetString(1));
        }
        else
        {
            cmd.CommandText = "SELECT column_name FROM information_schema.columns WHERE table_schema='public' AND table_name=@t;";
            cmd.AddWithValue("@t", table);
            using var r = cmd.ExecuteReader();
            while (r.Read()) set.Add(r.GetString(0));
        }
        return set;
    }

    /// <summary>Birincil anahtar kolonları (sıralı).</summary>
    public static List<string> PrimaryKey(DbConnection conn, string table)
    {
        var list = new List<string>();
        using var cmd = conn.CreateCommand();
        if (SqlDialect.IsSqlite(conn))
        {
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var r = cmd.ExecuteReader();
            var byOrder = new SortedDictionary<int, string>();
            while (r.Read())
            {
                var pkIndex = r.GetInt32(5); // pk: 0 = değil, >0 = PK sırası
                if (pkIndex > 0) byOrder[pkIndex] = r.GetString(1);
            }
            list.AddRange(byOrder.Values);
        }
        else
        {
            cmd.CommandText = @"
SELECT kcu.column_name
FROM information_schema.table_constraints tc
JOIN information_schema.key_column_usage kcu
  ON kcu.constraint_name = tc.constraint_name AND kcu.table_schema = tc.table_schema
WHERE tc.table_schema='public' AND tc.table_name=@t AND tc.constraint_type='PRIMARY KEY'
ORDER BY kcu.ordinal_position;";
            cmd.AddWithValue("@t", table);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r.GetString(0));
        }
        return list;
    }

    /// <summary>Kullanıcı tabloları (sistem tabloları hariç).</summary>
    public static List<string> ListTables(DbConnection conn)
    {
        var list = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SqlDialect.IsSqlite(conn)
            ? "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';"
            : "SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE';";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }
}
