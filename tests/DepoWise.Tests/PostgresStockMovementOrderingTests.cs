using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// KD-1'in ASIL KANITI — bu üç sorgu PostgreSQL'de <c>42703: column sm.rowid does not exist</c> ile
/// patlıyordu (canlıda üç uç da 500 veriyordu). Düzeltmeden ÖNCE bu testler kırmızıdır.
///
/// ⚠️ Yalnız DEPOWISE_PG_URL (doğrulanmış BOŞ test veritabanı) ile koşar; yoksa ATLANIR.
/// Canlı veritabanına ASLA bağlanmaz (bkz. PostgresTestGuard).
/// </summary>
[Collection("PostgresSchema")]
public class PostgresStockMovementOrderingTests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private sealed record Fixture(StockService Stock, SessionContext Admin, string MaterialId);

    private static Fixture Setup()
    {
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();

        var clock = new TestClock();
        var users = new UserService(factory, clock);
        var uid = users.EnsureInitialAdmin("A", "admin_a", "admin123", RoleKeys.CompanyAdmin);
        var admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var materials = new MaterialService(factory, clock);
        var opening = new OpeningStockService(factory, clock);
        var stock = new StockService(factory, clock);

        var mat = materials.Create(admin, new NewMaterial("M-1", "Filtre"));
        opening.RecordOpening(admin, mat, 100m, "op-1");
        // AYNI milisaniyede birden çok hareket → ikincil sıralama anahtarı kullanılır (rowid burada YOK).
        stock.IssueOut(admin, new[] { new StockLine(mat, 1m) }, "op-2", personnelId: null);
        stock.IssueOut(admin, new[] { new StockLine(mat, 2m) }, "op-3", personnelId: null);
        stock.IssueOut(admin, new[] { new StockLine(mat, 3m) }, "op-4", personnelId: null);
        return new Fixture(stock, admin, mat);
    }

    [SkippableFact]   // /api/stock
    public void PostgreSQLde_son_hareketler_PATLAMAZ()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = Setup();
        var rows = f.Stock.RecentMovements(f.Admin);
        Assert.True(rows.Count >= 4);
    }

    [SkippableFact]   // /api/stock/movements
    public void PostgreSQLde_hareket_aramasi_PATLAMAZ()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = Setup();
        var rows = f.Stock.SearchMovements(f.Admin, null, null, "M-1", 1000);
        Assert.NotEmpty(rows);
    }

    [SkippableFact]   // /api/materials/{id}/movements
    public void PostgreSQLde_malzeme_hareketleri_PATLAMAZ()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = Setup();
        var rows = f.Stock.RecentForMaterial(f.Admin, f.MaterialId, 100);
        Assert.True(rows.Count >= 4);
    }

    [SkippableFact]
    public void PostgreSQLde_siralama_DETERMINISTIK()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = Setup();
        var a = f.Stock.RecentMovements(f.Admin).Select(r => $"{r.CreatedAt}|{r.Quantity}").ToList();
        var b = f.Stock.RecentMovements(f.Admin).Select(r => $"{r.CreatedAt}|{r.Quantity}").ToList();
        Assert.Equal(a, b);
    }

    [SkippableFact]
    public void PostgreSQLde_firma_izolasyonu_stok_bakiyesinde_KORUNUR()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = Setup();
        var other = new SessionContext("baska-kullanici", "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Equal(0m, f.Stock.GetBalance(other, f.MaterialId));   // T-1: yabancı firma 0 görür
        Assert.Equal(94m, f.Stock.GetBalance(f.Admin, f.MaterialId));  // 100 - 1 - 2 - 3
    }
}
