using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// KD-1 — STOK HAREKETİ SIRALAMASI İKİ LEHÇEDE DE ÇALIŞIR (Paket 1, 2026-08-09).
///
/// Hata: sorgular <c>ORDER BY sm.created_at DESC, sm.rowid DESC</c> kullanıyordu. <c>rowid</c> SQLite'a
/// özeldir; PostgreSQL'de <c>42703: column sm.rowid does not exist</c> → <c>/api/stock</c>,
/// <c>/api/stock/movements</c> ve <c>/api/materials/{id}/movements</c> canlıda **500** veriyordu.
///
/// Düzeltme: <see cref="SqlDialect.RowTieBreaker"/> — SQLite'ta <c>rowid</c> (davranış birebir korunur),
/// PostgreSQL'de birincil anahtar (<c>id</c>) ile DETERMİNİSTİK sıralama.
///
/// SQLite tarafı burada; PostgreSQL tarafı <see cref="PostgresStockMovementOrderingTests"/>.
/// </summary>
public class StockMovementOrderingTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly StockService _stock;
    private readonly MaterialService _materials;
    private readonly OpeningStockService _opening;
    private readonly SessionContext _admin;
    private readonly string _mat;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public StockMovementOrderingTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_order_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _mat = _materials.Create(_admin, new NewMaterial("M-1", "Filtre"));
        _opening.RecordOpening(_admin, _mat, 100m, "op-1");
        // AYNI milisaniyede birden çok hareket → ikincil sıralama anahtarı devreye girer (saat İLERLETİLMEZ).
        _stock.IssueOut(_admin, new[] { new StockLine(_mat, 1m) }, "op-2", personnelId: null);
        _stock.IssueOut(_admin, new[] { new StockLine(_mat, 2m) }, "op-3", personnelId: null);
        _stock.IssueOut(_admin, new[] { new StockLine(_mat, 3m) }, "op-4", personnelId: null);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SQLite_stok_hareketleri_listesi_calisir()
    {
        var rows = _stock.RecentMovements(_admin);
        Assert.True(rows.Count >= 4);
    }

    [Fact]
    public void SQLite_malzeme_hareketleri_listesi_calisir()
    {
        var rows = _stock.RecentForMaterial(_admin, _mat, 100);
        Assert.True(rows.Count >= 4);
    }

    [Fact]
    public void SQLite_arama_ile_hareket_listesi_calisir()
    {
        var rows = _stock.SearchMovements(_admin, null, null, "M-1", 500);
        Assert.NotEmpty(rows);
    }

    [Fact]
    public void Ayni_milisaniyedeki_hareketlerde_siralama_DETERMINISTIK()
    {
        // Aynı sorgu iki kez → AYNI sıra (kararlı sıralama; rastgele değil).
        var a = _stock.RecentMovements(_admin).Select(r => $"{r.CreatedAt}|{r.Quantity}").ToList();
        var b = _stock.RecentMovements(_admin).Select(r => $"{r.CreatedAt}|{r.Quantity}").ToList();
        Assert.Equal(a, b);
    }
}
