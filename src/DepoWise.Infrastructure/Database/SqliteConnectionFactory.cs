using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database;

public interface IDbConnectionFactory
{
    /// <summary>Açık ve (sağlayıcıya göre) ayarlanmış bir bağlantı döndürür. PostgreSQL geçişi Faz 2:
    /// dönüş tipi artık taban <c>DbConnection</c> — SQLite (masaüstü) ve Npgsql (sunucu) ortak tabanı.</summary>
    DbConnection Create();
    string DatabasePath { get; }
}

/// <summary>
/// Yerel SQLite bağlantı üreticisi. Kural (CLAUDE.md / analiz §3):
/// Cache=Private (SHARED YASAK — UI donması), WAL, foreign_keys=ON, busy_timeout=5000.
/// </summary>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    public string DatabasePath { get; }

    public SqliteConnectionFactory(string databasePath)
    {
        DatabasePath = databasePath;
    }

    public static SqliteConnectionFactory ForEnvironment(string environment)
        => new(AppPaths.DatabasePath(environment));

    // Dönüş tipi taban DbConnection (arayüz gereği); içeride SQLite kurulur (PRAGMA/Türkçe fonksiyon+collation
    // SQLite'a özeldir, burada kalır). Çağıranlar taban tipi görür → PostgreSQL sağlayıcısı da takılabilir.
    public DbConnection Create()
    {
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            DefaultTimeout = 5
        }.ToString();

        var conn = new SqliteConnection(connStr);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "PRAGMA journal_mode=WAL;" +
                "PRAGMA foreign_keys=ON;" +
                "PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();
        }

        // R12 — Türkçe duyarsız arama: SQLite'ın 2 argümanlı like()'ını Türkçe kültürle override et.
        // Böylece TÜM sorgulardaki `col LIKE @term` otomatik İ/ı/ş/ç/ğ/ü/ö duyarsız çalışır (ekstra kod gerekmez).
        conn.CreateFunction<string?, string?, long>("like", (pattern, value) => SqlLikeTr(pattern, value) ? 1L : 0L);

        // Türkçe sıralama (kullanıcı isteği 2026-07-18, madde 5): SQLite'ın NOCASE'i yalnız ASCII'dir → "Çınar"
        // "Zeytin"den SONRA gelirdi. TRNOCASE, Türkçe kültürle harf-duyarsız karşılaştırır → Ç, C'den hemen sonra.
        conn.CreateCollation("TRNOCASE", (x, y) => string.Compare(x, y, Tr, CompareOptions.IgnoreCase));
        return conn;
    }

    private static readonly CultureInfo Tr = new("tr-TR");

    /// <summary>SQL LIKE (yalın % / _ joker) — her iki tarafı Türkçe küçük harfe indirip eşleştirir.</summary>
    internal static bool SqlLikeTr(string? pattern, string? value)
    {
        if (pattern is null || value is null) return false;
        var lp = pattern.ToLower(Tr);
        var lv = value.ToLower(Tr);
        var sb = new StringBuilder("^");
        foreach (var ch in lp)
        {
            switch (ch)
            {
                case '%': sb.Append(".*"); break;
                case '_': sb.Append('.'); break;
                default: sb.Append(Regex.Escape(ch.ToString())); break;
            }
        }
        sb.Append('$');
        return Regex.IsMatch(lv, sb.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant);
    }
}
