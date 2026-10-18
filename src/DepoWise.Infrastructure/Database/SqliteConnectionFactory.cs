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

    /// <summary>
    /// SQL LIKE (yalın % / _ joker) — her iki tarafı Türkçe küçük harfe indirip eşleştirir.
    ///
    /// ⭐ M-22 / F-111 (AR-GE 2026-10-18): bu işlev SATIR BAŞINA çağrılır. Eskiden her satırda deseni küçültüp yeni bir Regex
    /// kuruyordu (300 bin satırda malzeme araması 6,4 sn). Artık desen bir kez çözülüp önbelleğe alınır; yalnız <c>%</c>
    /// içeren desenler (en yaygın: <c>%arama%</c>, <c>arama%</c>) Regex'siz sıralı arama ile eşleşir; <c>_</c> içeren nadir
    /// desenler önbellekli Regex kullanır. Sonuç eskisiyle BİREBİR aynıdır (<see cref="SqlLikeTrEski"/> ile karşılaştırma testi).
    /// </summary>
    public static bool SqlLikeTr(string? pattern, string? value)
    {
        if (pattern is null || value is null) return false;
        return Desen(pattern)(value.ToLower(Tr));
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Func<string, bool>> _desenler = new(StringComparer.Ordinal);

    private static Func<string, bool> Desen(string pattern)
    {
        if (_desenler.TryGetValue(pattern, out var f)) return f;
        if (_desenler.Count > 512) _desenler.Clear();   // sınırsız büyümesin (aramalar kullanıcı yazımıdır)
        f = DesenKur(pattern.ToLower(Tr));
        _desenler[pattern] = f;
        return f;
    }

    private static Func<string, bool> DesenKur(string lp)
    {
        if (lp.Contains('_'))
        {
            var sb = new StringBuilder("^");
            foreach (var ch in lp)
                sb.Append(ch switch { '%' => ".*", '_' => ".", _ => Regex.Escape(ch.ToString()) });
            sb.Append('$');
            var re = new Regex(sb.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant);
            return re.IsMatch;
        }
        if (!lp.Contains('%')) return lv => string.Equals(lv, lp, StringComparison.Ordinal);

        // Yalnız %: parçalar sırayla aranır. İlk parça başa, son parça sona bağlıdır (desen % ile başlamıyor/bitmiyorsa).
        var parcalar = lp.Split('%');
        string bas = parcalar[0], son = parcalar[^1];
        var orta = parcalar.Length > 2 ? parcalar[1..^1].Where(p => p.Length > 0).ToArray() : Array.Empty<string>();
        if (orta.Length == 0 && bas.Length == 0) return lv => lv.EndsWith(son, StringComparison.Ordinal);       // %x, %
        if (orta.Length == 0 && son.Length == 0) return lv => lv.StartsWith(bas, StringComparison.Ordinal);     // x%
        if (bas.Length == 0 && son.Length == 0 && orta.Length == 1)
        { var x = orta[0]; return lv => lv.Contains(x, StringComparison.Ordinal); }                           // %x%
        return lv =>
        {
            if (lv.Length < bas.Length + son.Length) return false;
            if (!lv.StartsWith(bas, StringComparison.Ordinal) || !lv.EndsWith(son, StringComparison.Ordinal)) return false;
            int i = bas.Length, bitis = lv.Length - son.Length;
            foreach (var p in orta)
            {
                var k = lv.IndexOf(p, i, bitis - i, StringComparison.Ordinal);
                if (k < 0) return false;
                i = k + p.Length;
            }
            return true;
        };
    }

    /// <summary>M-22 öncesi uygulama — YALNIZ testte yeni uygulamanın birebir aynı sonuç verdiğini kanıtlamak için.</summary>
    public static bool SqlLikeTrEski(string? pattern, string? value)
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
