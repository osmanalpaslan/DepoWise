using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ I (2026-09-04) — İNDEKS DENETİMİ + N+1 KORUMASI ═══
///
/// <b>Bulgu:</b> <c>stock_movements</c> ve <c>vehicle_maintenances</c>, en sık çalışan sorgularını
/// destekleyen indekse sahip değildi. Mevcut indeksler <c>material_id</c> / <c>vehicle_id</c> ile
/// başlıyordu; oysa liste ve rapor sorgularının hepsi <c>company_id</c> ile süzüp
/// <c>created_at DESC</c> ile sıralıyor.
///
/// <b>Neden LST-01'den sonra kritik:</b> sayfalama her sayfada bir <c>COUNT(*)</c> daha çalıştırır.
/// İndekssiz bir tabloda bu, tarama sayısını ikiye katlar — yani sayfalama düzeltmesi indeks olmadan
/// performansı iyileştirmek yerine kötüleştirebilirdi.
///
///  IDX1 — Liste sorgularının indeksleri VAR (Migration091)
///  IDX2 — Migration091 yalnız CREATE INDEX içerir (canlı veri kanıtı)
///  NP1  — Stok hareketi sayfalaması satır sayısından BAĞIMSIZ sayıda SQL çalıştırır
///  NP2  — Bakım sayfalaması için aynısı
/// </summary>
public class IndeksVeNPlusBirTests : IDisposable
{
    private const string Co = "IDX";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _admin;
    private readonly string _mat, _sube;
    private static readonly long Gun = 1_700_000_000_000;

    public IndeksVeNPlusBirTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_idx_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();

        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _sube = new DepoWise.Infrastructure.Organization.BranchService(_f)
            .Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _mat = new MaterialService(_f).Create(_admin, new NewMaterial("M-1", "Çimento", UnitPrice: 10m));
    }

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private bool IndeksVar(string ad)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=@n;";
        cmd.AddWithValue("@n", ad);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    // ══════════════ İNDEKS ══════════════

    [Fact]
    public void IDX1_Liste_Sorgularinin_Indeksleri_Var()
    {
        Assert.True(IndeksVar("ix_stock_movements_company"),
            "stock_movements(company_id, created_at) indeksi yok — liste ve rapor sorguları tabloyu tarar.");
        Assert.True(IndeksVar("ix_vehicle_maintenances_company"),
            "vehicle_maintenances(company_id, created_at) indeksi yok — bakım listesi tabloyu tarar.");
    }

    /// <summary>Canlı veri kanıtı: indeks migration'ı hiçbir satırı okumaz/yazmaz/silmez.</summary>
    [Fact]
    public void IDX2_Migration091_Yalniz_Index_Olusturur()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);
        var kaynak = File.ReadAllText(Path.Combine(kok!.FullName,
            "src", "DepoWise.Infrastructure", "Database", "Migrations", "Migration091_ListIndexes.cs"));

        var i = kaynak.IndexOf("cmd.CommandText", StringComparison.Ordinal);
        Assert.True(i > 0);
        var sql = kaynak[i..].ToUpperInvariant();
        Assert.Contains("CREATE INDEX IF NOT EXISTS", sql);
        foreach (var yasak in new[] { "UPDATE ", "DELETE ", "DROP ", "INSERT ", "ALTER " })
            Assert.DoesNotContain(yasak, sql);
    }

    // ══════════════ N+1 ══════════════

    /// <summary>
    /// ⭐ N+1 KORUMASI — sayfalama, satır sayısından BAĞIMSIZ sayıda SQL çalıştırmalıdır.
    ///
    /// Bir liste yolu N+1'e düştüğünde (satır başına ek sorgu) ekran küçük veride normal görünür,
    /// büyük veride kilitlenir; üstelik hiçbir test kırılmaz. Bu sayaç ileride biri döngü içine sorgu
    /// eklerse KIRILIR. Sonucun doğruluğu ayrıca LST-01 testlerinde kontrol edilir — sayaç tek başına
    /// yeterli kanıt değildir.
    /// </summary>
    [Fact]
    public void NP1_Stok_Hareketi_Sayfalamasi_Satir_Sayisindan_Bagimsiz()
    {
        var stock = new StockService(_f);
        for (int i = 0; i < 5; i++)
            stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 1m, 10m) }, $"op-az-{i}", branchId: _sube, docDate: Gun);

        var azFabrika = new SayanFabrika(_f);
        new StockService(azFabrika).SearchMovementsGrid(_admin, null, null, null, null, null, null, 1, 100);
        var azSorgu = azFabrika.KomutSayisi;

        for (int i = 0; i < 40; i++)
            stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 1m, 10m) }, $"op-cok-{i}", branchId: _sube, docDate: Gun);

        var cokFabrika = new SayanFabrika(_f);
        var sonuc = new StockService(cokFabrika).SearchMovementsGrid(_admin, null, null, null, null, null, null, 1, 100);
        Assert.Equal(45, sonuc.TotalCount);   // veri gerçekten büyüdü

        Assert.Equal(azSorgu, cokFabrika.KomutSayisi);
    }

    [Fact]
    public void NP2_Bakim_Sayfalamasi_Satir_Sayisindan_Bagimsiz()
    {
        var arac = new DepoWise.Infrastructure.Vehicles.VehicleService(_f)
            .Create(_admin, new DepoWise.Infrastructure.Vehicles.NewVehicle("ARC-1"));
        var def = new DepoWise.Infrastructure.Maintenance.MaintenanceDefinitionService(_f)
            .Create(_admin, new DepoWise.Infrastructure.Maintenance.NewMaintenanceDefinition("Yağ", 100m, "day", null, null));
        var maint = new DepoWise.Infrastructure.Maintenance.MaintenanceService(_f);
        for (int i = 0; i < 3; i++)
            maint.Save(_admin, new DepoWise.Infrastructure.Maintenance.NewMaintenance(arac, def, PerformedDate: Gun,
                StockLocationId: _sube), $"op-m-az-{i}");

        var azFabrika = new SayanFabrika(_f);
        new DepoWise.Infrastructure.Maintenance.MaintenanceService(azFabrika).SearchMaintenancesGrid(_admin, 1, 100);
        var azSorgu = azFabrika.KomutSayisi;

        for (int i = 0; i < 25; i++)
            maint.Save(_admin, new DepoWise.Infrastructure.Maintenance.NewMaintenance(arac, def, PerformedDate: Gun,
                StockLocationId: _sube), $"op-m-cok-{i}");

        var cokFabrika = new SayanFabrika(_f);
        var sonuc = new DepoWise.Infrastructure.Maintenance.MaintenanceService(cokFabrika)
            .SearchMaintenancesGrid(_admin, 1, 100);
        Assert.Equal(28, sonuc.TotalCount);

        Assert.Equal(azSorgu, cokFabrika.KomutSayisi);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
