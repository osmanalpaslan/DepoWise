using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Assignments;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Equipment;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ 2026-10-11 — ZİMMET TOPLU TESLİM (kullanıcı isteği: "bir personele aynı anda birden fazla malzeme
/// zimmetlenebilsin"). Kilitler: tek işlemde N kalem · tek stok çıkış belgesi · YA HEPSİ YA HİÇBİRİ (stok yetmezse /
/// ekipman başkasındaysa hiçbir satır ve stok hareketi kalmaz) · idempotent retry · aynı ekipman iki kez yok ·
/// malzemede depo zorunlu · Depo Çıkışları raporunda "Zimmet" kaynağı ve teslim alan.
/// </summary>
public class ZimmetTopluTeslimTests : IDisposable
{
    private const string Co = "ZTT";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly AssignmentService _svc;
    private readonly StockService _stock;
    private readonly string _depo, _m1, _m2, _ekp, _ali, _veli;
    private readonly SessionContext _admin;

    public ZimmetTopluTeslimTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_ztt_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
            cmd.AddWithValue("@i", Co);
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _depo = new DepoWise.Infrastructure.Organization.BranchService(_f).Create(_admin, new NewBranch("Merkez Depo"));
        var mats = new MaterialService(_f);
        _m1 = mats.Create(_admin, new NewMaterial("MT-1", "Matkap Ucu"));
        _m2 = mats.Create(_admin, new NewMaterial("EL-2", "Eldiven"));
        _ekp = new EquipmentService(_f).Create(_admin, new NewEquipment("EKP-9", "Kırıcı"));
        var pers = new PersonnelService(_f, new ScopeResolver(_f));
        _ali = pers.Create(_admin, new NewPersonnel("Ali Usta", null, null, null));
        _veli = pers.Create(_admin, new NewPersonnel("Veli Usta", null, null, null));
        _stock = new StockService(_f);
        _stock.ReceiveIn(_admin, new[] { new StockLine(_m1, 10m), new StockLine(_m2, 20m) }, "op-acilis", branchId: _depo);
        _svc = new AssignmentService(_f);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    private decimal Stok(string mat) => _stock.GetBalancesByLocation(_admin, mat).TryGetValue(_depo, out var q) ? q : 0m;
    private int Satir(string sql)
    {
        using var c = _f.Create();
        using var k = c.CreateCommand();
        k.CommandText = sql;
        k.AddWithValue("@c", Co);
        return Convert.ToInt32(k.ExecuteScalar());
    }
    private static AssignmentLine M(string id, decimal q) => new("material", id, q);
    private static AssignmentLine E(string id) => new("equipment", id, 1m);

    [Fact]
    public void ZTT1_Tek_islemde_birden_fazla_kalem_teslim_edilir()
    {
        var grup = _svc.IssueMany(_admin, _ali, new[] { M(_m1, 3m), M(_m2, 5m), E(_ekp) }, _depo, null, "toplu", "op-toplu");
        var kimde = _svc.Holdings(_admin, personnelId: _ali);
        Assert.Equal(3, kimde.Count);
        Assert.Equal(3m, kimde.Single(h => h.AssetId == _m1).Quantity);
        Assert.Equal(5m, kimde.Single(h => h.AssetId == _m2).Quantity);
        Assert.Contains(kimde, h => h.AssetId == _ekp);
        Assert.Equal(7m, Stok(_m1));
        Assert.Equal(15m, Stok(_m2));
        // malzemeler TEK stok çıkış belgesinde; tüm defter satırları aynı grupta
        Assert.Equal(1, Satir("SELECT COUNT(DISTINCT document_id) FROM stock_movements WHERE company_id=@c AND operation_id LIKE 'assign:op-toplu:%';"));
        Assert.Equal(3, Satir($"SELECT COUNT(*) FROM assignment_movements WHERE company_id=@c AND group_id='{grup}';"));
    }

    [Fact]
    public void ZTT2_Bir_kalemde_stok_yetmezse_HICBIRI_kaydedilmez()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
            _svc.IssueMany(_admin, _ali, new[] { M(_m1, 3m), M(_m2, 999m), E(_ekp) }, _depo, null, null, "op-yetmez"));
        Assert.NotNull(ex);
        Assert.Empty(_svc.Holdings(_admin, personnelId: _ali));
        Assert.Equal(10m, Stok(_m1));   // ilk kalemin stok düşümü de GERİ ALINDI
        Assert.Equal(20m, Stok(_m2));
        Assert.Equal(0, Satir("SELECT COUNT(*) FROM assignment_movements WHERE company_id=@c;"));
    }

    [Fact]
    public void ZTT3_Ayni_islem_tekrar_gonderilirse_ikinci_kez_yazilmaz()
    {
        var kalemler = new[] { M(_m1, 2m), M(_m2, 2m) };
        _svc.IssueMany(_admin, _ali, kalemler, _depo, null, null, "op-retry");
        _svc.IssueMany(_admin, _ali, kalemler, _depo, null, null, "op-retry");
        Assert.Equal(8m, Stok(_m1));    // tek düşüm
        Assert.Equal(2, Satir("SELECT COUNT(*) FROM assignment_movements WHERE company_id=@c;"));
    }

    [Fact]
    public void ZTT4_Ekipman_baskasindaysa_toplu_teslim_tamamen_reddedilir()
    {
        _svc.Issue(_admin, "equipment", _ekp, _veli, 1m, null, null, null, "op-veli");
        Assert.Throws<ArgumentException>(() =>
            _svc.IssueMany(_admin, _ali, new[] { M(_m1, 1m), E(_ekp) }, _depo, null, null, "op-cakisma"));
        Assert.Equal(10m, Stok(_m1));
        Assert.Empty(_svc.Holdings(_admin, personnelId: _ali));
    }

    [Fact]
    public void ZTT5_Gecersiz_listeler_reddedilir()
    {
        Assert.Throws<ArgumentException>(() => _svc.IssueMany(_admin, _ali, Array.Empty<AssignmentLine>(), _depo, null, null, "op-bos"));
        Assert.Throws<ArgumentException>(() => _svc.IssueMany(_admin, _ali, new[] { E(_ekp), E(_ekp) }, _depo, null, null, "op-cift"));
        Assert.Throws<ArgumentException>(() => _svc.IssueMany(_admin, _ali, new[] { M(_m1, 1m) }, null, null, null, "op-depo"));
        var cok = Enumerable.Range(0, AssignmentService.MaxTopluKalem + 1).Select(_ => M(_m1, 0.01m)).ToArray();
        Assert.Throws<ArgumentException>(() => _svc.IssueMany(_admin, _ali, cok, _depo, null, null, "op-cok"));
        Assert.Equal(10m, Stok(_m1));
    }

    [Fact]
    public void ZTT6_Yalniz_ekipman_listesinde_depo_gerekmez()
    {
        _svc.IssueMany(_admin, _ali, new[] { E(_ekp) }, null, null, null, "op-ekp");
        Assert.Single(_svc.Holdings(_admin, personnelId: _ali));
    }

    [Fact]
    public void ZTT7_Depo_Cikislari_raporunda_Zimmet_kaynagi_ve_teslim_alan_gorunur()
    {
        _svc.IssueMany(_admin, _ali, new[] { M(_m1, 3m), M(_m2, 5m) }, _depo, null, null, "op-rapor");
        var t = new ReportService(_f).Run(_admin, "stock-issues",
            new ReportRequest(true, 0, 4_102_444_800_000L), ReportLimits.DefaultMaxRows);
        Assert.Equal(2, t.Rows.Count);
        int kaynak = t.Headers.ToList().IndexOf("Çıkış Kaynağı");
        Assert.All(t.Rows, r => Assert.Equal("Ali Usta", (string)r[1]!));
        Assert.All(t.Rows, r => Assert.Equal("Zimmet", (string)r[kaynak]!));
    }
}
