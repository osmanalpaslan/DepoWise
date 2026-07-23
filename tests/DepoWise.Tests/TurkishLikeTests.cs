using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DepoWise.Tests;

/// <summary>R12 — LIKE araması Türkçe büyük/küçük harf duyarsız (İ/ı/ş/ç/ğ/ü/ö).</summary>
public class TurkishLikeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;

    public TurkishLikeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_trlike_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
    }

    [Theory]
    [InlineData("İstasyon Filtresi", "%istasyon%", true)]   // İ → i
    [InlineData("ŞAFT", "%şaft%", true)]                     // Ş → ş
    [InlineData("Çelik Halat", "%çelik%", true)]             // Ç → ç
    [InlineData("Motor Yağı", "%YAĞ%", true)]                // ağ ↔ AĞ
    [InlineData("Fren Balatası", "%balata%", true)]
    [InlineData("Conta", "%xyz%", false)]
    public void LikeAramasi_TurkceDuyarsiz(string value, string pattern, bool expected)
    {
        using var conn = _factory.Create();
        using (var c = conn.CreateCommand()) { c.CommandText = "CREATE TABLE t(name TEXT);"; c.ExecuteNonQuery(); }
        using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO t(name) VALUES($v);"; c.AddWithValue("$v", value); c.ExecuteNonQuery(); }
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM t WHERE name LIKE $p;";
        cmd.AddWithValue("$p", pattern);
        Assert.Equal(expected, Convert.ToInt64(cmd.ExecuteScalar()) > 0);
    }

    public void Dispose()
    {
        try { SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
