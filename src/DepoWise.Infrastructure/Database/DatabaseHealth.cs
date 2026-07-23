using System.Diagnostics;
using DepoWise.Application.Common;
using System.Data.Common;

namespace DepoWise.Infrastructure.Database;

/// <summary>
/// Açılış health kontrolü: gerçek DB yolunda write/read yapar, journal_mode ve
/// foreign_keys durumunu raporlar. COMODO testi bu kanıtı kullanır.
/// </summary>
public sealed class DatabaseHealth : IDatabaseHealth
{
    private readonly IDbConnectionFactory _factory;

    public DatabaseHealth(IDbConnectionFactory factory) => _factory = factory;

    public Task<HealthResult> CheckAsync(CancellationToken ct = default)
    {
        var host = Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule?.FileName ?? "dotnet");
        try
        {
            using var conn = _factory.Create();

            // Lehçe-duyarlı: journal_mode/foreign_keys PRAGMA'ları SQLite'a özeldir. PostgreSQL'de
            // WAL zaten varsayılan (kalıcılık motor düzeyinde) ve FK'ler her zaman zorunludur (kapatılamaz)
            // → sunucuda bunları "n/a (postgres)" + true olarak raporla, PRAGMA çalıştırma.
            bool sqlite = SqlDialect.IsSqlite(conn);
            string journal = sqlite ? ScalarText(conn, "PRAGMA journal_mode;") : "postgres";
            bool fk = sqlite ? (ScalarLong(conn, "PRAGMA foreign_keys;") == 1) : true;

            // ts: Unix-ms (~1.7e12) → PostgreSQL 32-bit INTEGER'ı taşar, BIGINT şart (SQLite'ta da 64-bit).
            // id kolonu KALDIRILDI: SQLite'ta AUTOINCREMENT'e, PG'de PK-NULL sorununa yol açıyordu; MAX(ts) yeter.
            Execute(conn, "CREATE TABLE IF NOT EXISTS _health_check(ts BIGINT NOT NULL);");
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using (var ins = conn.CreateCommand())
            {
                ins.CommandText = "INSERT INTO _health_check(ts) VALUES(@ts);";
                ins.AddWithValue("@ts", ts);
                ins.ExecuteNonQuery();
            }
            long readBack = ScalarLong(conn, "SELECT MAX(ts) FROM _health_check;");
            bool writeRead = readBack == ts;

            return Task.FromResult(new HealthResult(
                Ok: writeRead && fk,
                Host: host,
                DatabasePath: _factory.DatabasePath,
                JournalMode: journal,
                ForeignKeysOn: fk,
                WriteReadOk: writeRead));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HealthResult(
                Ok: false, Host: host, DatabasePath: _factory.DatabasePath,
                JournalMode: "unknown", ForeignKeysOn: false, WriteReadOk: false, Error: ex.Message));
        }
    }

    private static void Execute(DbConnection c, string sql)
    {
        using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
    }
    private static string ScalarText(DbConnection c, string sql)
    {
        using var cmd = c.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar()?.ToString() ?? "";
    }
    private static long ScalarLong(DbConnection c, string sql)
    {
        using var cmd = c.CreateCommand(); cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v is null || v is DBNull ? 0 : Convert.ToInt64(v);
    }
}
