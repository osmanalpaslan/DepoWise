using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Sayım (StockCount) derin senaryolar: pozitif fark, sıfır fark, çok satır, IDEMPOTENT retry.
/// Temel sayım (fark -3, gerekçe zorunlu) StockOperationTests'te; bu dosya ek kapsam (QA raporu B3).</summary>
public class StockCountTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly StockService _stock;
    private readonly SessionContext _admin;

    public StockCountTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_scount_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private string Mat(string code) => _materials.Create(_admin, new NewMaterial(code, code));

    private int CountDocs()
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_documents WHERE doc_type='count';";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [Fact]
    public void Sayim_PozitifFark_BakiyeyiArttirir()
    {
        var m = Mat("M-1");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 5m) }, "in");                          // sistem 5
        _stock.Count(_admin, new[] { new CountLine(m, 8m) }, "Fazla bulundu", "op-c1");          // sayım 8 → +3
        Assert.Equal(8m, _stock.GetBalance(_admin, m));
    }

    [Fact]
    public void Sayim_FarkYok_BakiyeDegismez_AmaSatirKaydeder()
    {
        var m = Mat("M-1");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 5m) }, "in");
        _stock.Count(_admin, new[] { new CountLine(m, 5m) }, "Kontrol", "op-c2");                // fark 0
        Assert.Equal(5m, _stock.GetBalance(_admin, m));

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT diff_qty FROM stock_count_lines WHERE material_id=@m;";
        cmd.AddWithValue("@m", m);
        Assert.Equal(0m, Money.Parse((string)cmd.ExecuteScalar()!));                             // count satırı yazıldı (fark 0)
    }

    [Fact]
    public void Sayim_CokSatir_HerBirineAyriUygular()
    {
        var a = Mat("A"); var b = Mat("B");
        _stock.ReceiveIn(_admin, new[] { new StockLine(a, 10m), new StockLine(b, 10m) }, "in");
        _stock.Count(_admin, new[] { new CountLine(a, 7m), new CountLine(b, 12m) }, "Sayım", "op-c3");
        Assert.Equal(7m, _stock.GetBalance(_admin, a));    // -3
        Assert.Equal(12m, _stock.GetBalance(_admin, b));   // +2
    }

    [Fact]
    public void Sayim_AyniOperationId_IdempotentRetry_TekBelge_CiftHareketYok()
    {
        var m = Mat("M-1");
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 5m) }, "in");
        _stock.Count(_admin, new[] { new CountLine(m, 8m) }, "Fazla", "op-dup");                 // +3 → 8
        Assert.Equal(8m, _stock.GetBalance(_admin, m));
        Assert.Equal(1, CountDocs());

        // Ağ retry: aynı operationId ile tekrar → mevcut belge döner, İKİNCİ belge/hareket ÜRETİLMEZ.
        _stock.Count(_admin, new[] { new CountLine(m, 8m) }, "Fazla", "op-dup");
        Assert.Equal(8m, _stock.GetBalance(_admin, m));    // hâlâ 8
        Assert.Equal(1, CountDocs());              // hâlâ tek sayım belgesi (idempotent)
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
