using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Doğrudan stok değişikliği uyarı + log (madde 1.2-1.5, kullanıcı isteği 2026-08-06): devam →
/// stok SAYIM/DÜZELTME hareketiyle güncellenir (doğrudan bakiye yazımı YOK) + log; iptal → yalnız log.</summary>
public class StockChangeLogTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly StockService _stock;
    private readonly StockChangeLogService _log;
    private readonly SessionContext _admin;

    public StockChangeLogTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_scl_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _log = new StockChangeLogService(_factory, _stock, _clock);

        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    private string MatWithStock(string code, decimal qty)
    {
        var m = _materials.Create(_admin, new NewMaterial(code, code));
        if (qty != 0) _stock.ReceiveIn(_admin, new[] { new StockLine(m, qty) }, "op-" + code);
        return m;
    }

    [Fact]
    public void Devam_StokuSayimDuzeltmesiyleGunceller_HareketDefterineYazar()
    {
        var m = MatWithStock("M-CONT", 10m);
        _log.Record(_admin, m, newQuantity: 25m, continued: true, warningText: StockChangeLogService.WarningMessage);

        Assert.Equal(25m, _stock.GetBalance(m));                    // bakiye güncellendi
        var moves = _stock.SearchMovements(_admin, null, null, null);
        Assert.Contains(moves, x => x.MovementType == "adjustment"); // doğrudan yazım DEĞİL, adjustment hareketi
    }

    [Fact]
    public void Devam_LogKaydiOlusturur_DevamEttiOutcomeyle()
    {
        var m = MatWithStock("M-LOG", 5m);
        _log.Record(_admin, m, newQuantity: 8m, continued: true, warningText: "uyarı");

        var rows = _log.List(_admin);
        Assert.Single(rows);
        Assert.Equal("continued", rows[0].Outcome);
        Assert.Equal(5m, rows[0].OldQuantity);
        Assert.Equal(8m, rows[0].NewQuantity);
        Assert.Equal("M-LOG", rows[0].MaterialCode);
    }

    [Fact]
    public void Iptal_StokuDEGISTIRMEZ_YalnizLogYazar()
    {
        var m = MatWithStock("M-CANC", 12m);
        _log.Record(_admin, m, newQuantity: 3m, continued: false, warningText: "uyarı");

        Assert.Equal(12m, _stock.GetBalance(m));   // stok değişmedi
        var moves = _stock.SearchMovements(_admin, null, null, null);
        Assert.DoesNotContain(moves, x => x.MovementType == "adjustment");
        var rows = _log.List(_admin);
        Assert.Single(rows);
        Assert.Equal("cancelled", rows[0].Outcome);
    }

    [Fact]
    public void TarihAraligiVeLimit_SistemLoguIleAyniDavranir()
    {
        var m = MatWithStock("M-FILT", 0m);
        _clock.UtcNow = DateTimeOffset.FromUnixTimeMilliseconds(1_000_000);
        _log.Record(_admin, m, 1m, false, null);
        _clock.UtcNow = DateTimeOffset.FromUnixTimeMilliseconds(3_000_000);
        _log.Record(_admin, m, 2m, false, null);

        Assert.Single(_log.List(_admin, fromMs: 2_000_000, toMs: null)); // yalnız aralıktaki
        Assert.Equal(2, _log.List(_admin).Count);                         // filtresiz tümü
        Assert.Single(_log.List(_admin, limit: 1));                       // kayıt sayısı sınırı
    }

    [Fact]
    public void Goruntuleme_YetkiGerektirir_StockChangeLogModulu()
    {
        var m = MatWithStock("M-PERM", 0m);
        _log.Record(_admin, m, 1m, false, null);

        // stock_change_log VIEW yetkisi olmayan (ama materials görebilen) kullanıcı listeyi göremez.
        var limited = new PermissionSet(new[]
        {
            new ModulePermission("materials", CanView: true, CanCreate: true, CanEdit: true, CanDelete: true),
        });
        var staff = new SessionContext("u2", "A", new[] { RoleKeys.Staff }, limited);
        Assert.Throws<ForbiddenException>(() => _log.List(staff));
    }
}
