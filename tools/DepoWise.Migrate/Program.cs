using System.Data.Common;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using Npgsql;

// DepoWise canlı geçiş aracı (tek seferlik): bir SQLite KOPYASINDAKİ tüm veriyi hedef PostgreSQL'e aktarır.
// Kullanım:
//   dotnet run --project tools/DepoWise.Migrate -- <sqlite-kopya-yolu> "<pg-baglanti-dizesi>"
// Hedef PG şeması yoksa migration'lar ÇALIŞTIRILIR; sonra veri kopyalanır.
// 🔒 Kaynak SQLite salt-okunur; canlı sunucuya/DB'ye DOKUNMAZ (bir KOPYA verilmelidir).

if (args.Length < 2)
{
    Console.Error.WriteLine("Kullanım: DepoWise.Migrate <sqlite-kopya-yolu> \"<pg-baglanti-dizesi>\"");
    return 2;
}

var sqlitePath = args[0];
var pgConn = args[1];

if (!File.Exists(sqlitePath))
{
    Console.Error.WriteLine($"SQLite kopya dosyası bulunamadı: {sqlitePath}");
    return 2;
}

var sqlite = new SqliteConnectionFactory(sqlitePath);
var pg = new PgFactory(pgConn);

Console.WriteLine($"Kaynak (SQLite): {sqlitePath}");
Console.WriteLine($"Hedef  (PG)    : {pg.DatabasePath}");

Console.WriteLine("Hedef şema hazırlanıyor (migration'lar)...");
var applied = new MigrationRunner(pg).Run();
Console.WriteLine($"  migration tamam (sürüm {new MigrationRunner(pg).CurrentVersion()}).");

Console.WriteLine("Veri kopyalanıyor...");
var report = SqliteToPgCopier.Copy(sqlite, pg);

foreach (var kv in report.RowsPerTable.OrderByDescending(k => k.Value).Where(k => k.Value > 0))
    Console.WriteLine($"  {kv.Key,-32} {kv.Value,8}");
Console.WriteLine($"TOPLAM {report.TotalRows} satır kopyalandı.");
Console.WriteLine("BİTTİ.");
return 0;

sealed class PgFactory : IDbConnectionFactory
{
    private readonly string _cs;
    public PgFactory(string cs)
    {
        _cs = cs;
        var b = new NpgsqlConnectionStringBuilder(cs);
        DatabasePath = $"postgres://{b.Host}/{b.Database}";
    }
    public string DatabasePath { get; }
    public DbConnection Create() { var c = new NpgsqlConnection(_cs); c.Open(); return c; }
}
