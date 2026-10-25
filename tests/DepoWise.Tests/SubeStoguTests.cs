using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ŞUBE STOĞU (kullanıcı bildirimi 2026-10-25) ═══
/// "Şu an stoğu olmayan şubelerde bile malzemeler yanlış stokla listeleniyor": malzeme listesi stok sütununda firmanın TÜM depolarının
/// toplamını gösteriyordu. Katalog ortaktır (her şube her malzemeyi görür), stok şubeye özgüdür.
///  ST1 — A'da tanımlanıp A'ya girilen malzeme: A listesinde 10, B listesinde VAR ve 0 ("Stok Yok"), Tüm Şubeler'de 10
///  ST2 — şubeler bağımsız: B'ye giriş / A'dan çıkış diğerini değiştirmez; toplam doğru
///  ST3 — liste yolları tutarlı: türetilmiş kolon filtresi (genel yol), tüm sonuç (dışa aktarım), özet şeridi aynı şube stoğunu kullanır
///  ST4 — ana ekran "düşük stok": şubenin KENDİ stoğuna göre (B'de düşük, A'da değil); Tüm Şubeler'de toplam
/// </summary>
public class SubeStoguTests : IDisposable
{
    private const string Co = "SST";
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_sst_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _tum, _sA, _sB;
    private readonly string _a, _b, _m;
    private readonly StockService _stock;
    private readonly MaterialService _mat;

    public SubeStoguTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        using (var c = _f.Create()) using (var cmd = c.CreateCommand())
        { cmd.CommandText = $"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);"; cmd.ExecuteNonQuery(); }
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "sst", "Stok!2026", RoleKeys.CompanyAdmin);
        _tum = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);   // Tüm Şubeler (çalışma şubesi yok)
        var sb = new BranchService(_f);
        _a = sb.Create(_tum, new NewBranch("Merkez"));
        _b = sb.Create(_tum, new NewBranch("Şantiye", "site"));
        _sA = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty) { OperatingBranchId = _a };
        _sB = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty) { OperatingBranchId = _b };
        _mat = new MaterialService(_f);
        _m = _mat.Create(_sA, new NewMaterial("SST-1", "Kablo", MinStock: 5m));   // A şubesinde tanımlandı
        _stock = new StockService(_f);
        _stock.ReceiveIn(_sA, new[] { new StockLine(_m, 10m) }, Op(), _a);       // yalnız A'ya giriş
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _yol, _yol + "-wal", _yol + "-shm" }) try { File.Delete(p); } catch { }
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");
    private MaterialGridRow Satir(SessionContext s) => Assert.Single(_mat.SearchGrid(s, new MaterialGridFilter(), 1, 50).Items);

    [Fact]
    public void ST1_Diger_Sube_Malzemeyi_Sifir_Stokla_Gorur()
    {
        Assert.Equal(10m, Satir(_sA).Stock);
        var b = Satir(_sB);                                  // katalog ortak: B de malzemeyi görür
        Assert.Equal(0m, b.Stock);                           // ama stoğu 0 (eskiden A'nın 10'unu gösteriyordu)
        Assert.Equal("Stok Yok", b.Status);
        Assert.Equal(10m, Satir(_tum).Stock);                // Tüm Şubeler = toplam
    }

    [Fact]
    public void ST2_Subeler_Birbirinden_Bagimsiz()
    {
        _stock.ReceiveIn(_sB, new[] { new StockLine(_m, 4m) }, Op(), _b);       // B kendi deposuna, malzemeyi yeniden oluşturmadan
        _stock.IssueOut(_sA, new[] { new StockLine(_m, 3m) }, Op(), _a);
        Assert.Equal(7m, Satir(_sA).Stock);
        Assert.Equal(4m, Satir(_sB).Stock);
        Assert.Equal(11m, Satir(_tum).Stock);
    }

    [Fact]
    public void ST3_Liste_Yollari_Ayni_Sube_Stogunu_Kullanir()
    {
        // türetilmiş kolon (stok) filtresi → genel yol; B'de stok 0 olduğu için "10" filtresi boş döner
        Assert.Empty(_mat.SearchGrid(_sB, new MaterialGridFilter(Stock: "10"), 1, 50).Items);
        Assert.Single(_mat.SearchGrid(_sA, new MaterialGridFilter(Stock: "10"), 1, 50).Items);
        Assert.Equal(0m, Assert.Single(_mat.SearchGridAll(_sB, new MaterialGridFilter())).Stock);   // dışa aktarım
        Assert.Single(_mat.SearchGrid(_sB, new MaterialGridFilter(), 1, 50, criticalOnly: true).Items);   // B'de kritik (0 ≤ 5)
        Assert.Empty(_mat.SearchGrid(_sA, new MaterialGridFilter(), 1, 50, criticalOnly: true).Items);   // A'da değil (10 > 5)
    }

    [Fact]
    public void ST4_Ana_Ekran_Dusuk_Stok_Sube_Bazli()
    {
        var dash = new DashboardService(_f, new MaintenanceService(_f), new InspectionService(_f));
        Assert.Equal(1, dash.GetSummary(_sB).LowStockCount);                 // B'de stok 0 ≤ min 5
        Assert.Equal(0, dash.GetSummary(_sA).LowStockCount);                 // A'da 10 > 5
        Assert.Equal(0, dash.GetSummary(_tum).LowStockCount);                // toplam 10 > 5
        Assert.Contains(dash.GetSummary(_sB).Alerts, a => a.EntityId == _m);
    }
}
