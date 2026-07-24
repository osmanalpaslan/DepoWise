using System.Data.Common;

namespace DepoWise.Infrastructure.Database;

/// <summary>Kopya raporu — tablo başına yazılan satır sayısı + toplam.</summary>
public sealed record CopyReport(IReadOnlyDictionary<string, int> RowsPerTable, int TotalRows);

/// <summary>
/// PostgreSQL geçişi — CANLI GEÇİŞ aracı (2026-07-24): bir SQLite veritabanındaki TÜM veriyi (babanın
/// gerçek verisinin KOPYASI) hedef PostgreSQL'e (Neon) aktarır. 🔒 Kaynak SQLite salt-okunur açılır; canlı
/// veriye/servise DOKUNMAZ — çağıran bir KOPYA dosyası verir.
///
/// Yaklaşım:
///   • Hedef PG şeması ÖNCEDEN migration ile kurulmuş olmalı (53 migration). schema_migrations/sqlite_sequence
///     KOPYALANMAZ (PG'de zaten doğru / SQLite'a özel).
///   • Ekleme sırası TOPOLOJİK (ebeveyn tablolar önce) — FK'ler PG'de kapatılamadığından (Neon owner).
///   • Kendine-referanslı tablo (ör. material_categories.parent_id) satırları için: tablo bir kerede eklenemezse
///     satır-başı savepoint ile fixpoint (ebeveyn satır önce).
///   • IDENTITY kolonu (server_changes.seq) → <c>OVERRIDING SYSTEM VALUE</c> ile açık değer yazılır, sonra
///     sequence <c>setval</c> ile max+1'e ilerletilir (geçiş sonrası çakışma olmasın).
///   • Tümü TEK transaction (atomik: ya hepsi ya hiçbiri).
/// </summary>
public static class SqliteToPgCopier
{
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
        { "schema_migrations", "sqlite_sequence" };

    public static CopyReport Copy(IDbConnectionFactory sqliteFactory, IDbConnectionFactory pgFactory)
    {
        using var src = sqliteFactory.Create();
        using var dst = pgFactory.Create();
        if (SqlDialect.IsSqlite(dst)) throw new InvalidOperationException("Hedef PostgreSQL olmalı.");

        var tables = DbIntrospect.ListTables(src).Where(t => !Skip.Contains(t)).ToList();
        var identity = IdentityColumns(dst);                 // tablo -> identity kolon adı (varsa)
        var order = InsertOrder(dst, tables);                // ebeveynler önce

        var report = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int total = 0;

        ExecRaw(dst, "BEGIN;");
        try
        {
            // Hedefteki seed verisini (migration'ların açtığı sistem rolleri vb.) temizle → SQLite kaynağı
            // kendi seed'ini de taşıdığından PG birebir SQLite olur (aksi halde roles vb. çift kayıt/UK çakışması).
            // RESTART IDENTITY: server_changes.seq sequence'i sıfırlanır (kopya OVERRIDING + setval ile yönetir).
            if (tables.Count > 0)
                ExecRaw(dst, $"TRUNCATE {string.Join(",", tables.Select(t => $"\"{t}\""))} RESTART IDENTITY CASCADE;");

            foreach (var table in order)
            {
                int n = CopyTable(src, dst, table, identity.ContainsKey(table));
                report[table] = n; total += n;
            }
            // IDENTITY sequence'lerini max+1'e ilerlet (açık değerler yazıldı → sequence geride kaldı).
            foreach (var (table, col) in identity)
                if (report.TryGetValue(table, out var n) && n > 0)
                    ExecRaw(dst, $"SELECT setval(pg_get_serial_sequence('{table}','{col}'), " +
                                 $"(SELECT COALESCE(MAX(\"{col}\"),1) FROM \"{table}\"));");
            ExecRaw(dst, "COMMIT;");
        }
        catch
        {
            try { ExecRaw(dst, "ROLLBACK;"); } catch { /* yut */ }
            throw;
        }
        return new CopyReport(report, total);
    }

    private static int CopyTable(DbConnection src, DbConnection dst, string table, bool hasIdentity)
    {
        // 1) SQLite'tan tüm satırları oku (kolon adları + değerler).
        string[] colNames;
        var rows = new List<object?[]>();
        using (var read = src.CreateCommand())
        {
            read.CommandText = $"SELECT * FROM \"{table}\";";
            using var r = read.ExecuteReader();
            colNames = new string[r.FieldCount];
            for (int i = 0; i < r.FieldCount; i++) colNames[i] = r.GetName(i);
            while (r.Read())
            {
                var vals = new object?[r.FieldCount];
                for (int i = 0; i < r.FieldCount; i++) { var v = r.GetValue(i); vals[i] = v is DBNull ? null : v; }
                rows.Add(vals);
            }
        }
        if (rows.Count == 0) return 0;

        var colList = string.Join(",", colNames.Select(c => $"\"{c}\""));
        var paramList = string.Join(",", Enumerable.Range(0, colNames.Length).Select(i => $"@p{i}"));
        var overriding = hasIdentity ? "OVERRIDING SYSTEM VALUE " : "";
        var sql = $"INSERT INTO \"{table}\" ({colList}) {overriding}VALUES ({paramList});";

        // 2) HIZLI YOL: tüm satırlar tek savepoint. (Ebeveynler önce geldiği için normalde tek geçişte biter.)
        ExecRaw(dst, "SAVEPOINT cp;");
        try
        {
            foreach (var vals in rows) InsertRow(dst, sql, vals);
            ExecRaw(dst, "RELEASE SAVEPOINT cp;");
            return rows.Count;
        }
        catch (DbException)
        {
            ExecRaw(dst, "ROLLBACK TO SAVEPOINT cp;");   // kendine-referans sırası → satır fixpoint
        }

        // 3) KURTARMA: satır-başı savepoint, ilerleme durana dek (self-ref ebeveyn satır önce).
        var pending = new List<object?[]>(rows);
        int inserted = 0, sp = 0;
        while (pending.Count > 0)
        {
            int before = pending.Count;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var name = $"cr{sp++}";
                ExecRaw(dst, $"SAVEPOINT {name};");
                try { InsertRow(dst, sql, pending[i]); ExecRaw(dst, $"RELEASE SAVEPOINT {name};"); inserted++; pending.RemoveAt(i); }
                catch (DbException) { ExecRaw(dst, $"ROLLBACK TO SAVEPOINT {name};"); }
            }
            if (pending.Count == before) InsertRow(dst, sql, pending[0]);   // gerçek hata yükselsin
        }
        ExecRaw(dst, "RELEASE SAVEPOINT cp;");
        return inserted;
    }

    private static void InsertRow(DbConnection dst, string sql, object?[] vals)
    {
        using var cmd = dst.CreateCommand();
        cmd.CommandText = sql;
        for (int i = 0; i < vals.Length; i++) cmd.AddWithValue($"@p{i}", vals[i] ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>PG'de GENERATED ... AS IDENTITY kolonları: tablo -> kolon adı.</summary>
    private static Dictionary<string, string> IdentityColumns(DbConnection dst)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = dst.CreateCommand();
        cmd.CommandText =
            "SELECT table_name, column_name FROM information_schema.columns " +
            "WHERE table_schema='public' AND is_identity='YES';";
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetString(0)] = r.GetString(1);
        return map;
    }

    /// <summary>Topolojik ekleme sırası: bir tablo, REFERANS ETTİĞİ (ebeveyn) tablolardan SONRA gelir.
    /// Kendine-referans yok sayılır (satır-retry halleder); döngü kalırsa sona eklenir.</summary>
    private static List<string> InsertOrder(DbConnection dst, List<string> tables)
    {
        var set = new HashSet<string>(tables, StringComparer.OrdinalIgnoreCase);
        // child -> parents (yalnız kümedeki tablolar, self hariç)
        var parents = tables.ToDictionary(t => t, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        using (var cmd = dst.CreateCommand())
        {
            cmd.CommandText = @"
SELECT con.conrelid::regclass::text, con.confrelid::regclass::text
FROM pg_constraint con WHERE con.contype='f' AND con.connamespace='public'::regnamespace;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var child = Strip(r.GetString(0)); var parent = Strip(r.GetString(1));
                if (!child.Equals(parent, StringComparison.OrdinalIgnoreCase)
                    && set.Contains(child) && set.Contains(parent))
                    parents[child].Add(parent);
            }
        }
        var order = new List<string>();
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool progress = true;
        while (order.Count < tables.Count && progress)
        {
            progress = false;
            foreach (var t in tables)
            {
                if (done.Contains(t)) continue;
                if (parents[t].All(done.Contains)) { order.Add(t); done.Add(t); progress = true; }
            }
        }
        foreach (var t in tables) if (!done.Contains(t)) order.Add(t);   // döngü kalırsa (beklenmez) sona
        return order;
    }

    private static string Strip(string name)
    {
        var s = name; var dot = s.LastIndexOf('.'); if (dot >= 0) s = s[(dot + 1)..];
        return s.Trim('"');
    }

    private static void ExecRaw(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
