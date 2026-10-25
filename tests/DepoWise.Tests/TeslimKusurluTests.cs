using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ŞUBE DIŞI ÇIKIŞ → HEDEFTE TESLİM ALMA + KUSURLU (kullanıcı isteği 2026-10-25) ═══
///  TA1 — sağlam + kusurlu (fire) + eksik: sağlam hedef stoğuna, kusurlu stoktan düşer (açıklama hareket notunda), eksik yolda kalır
///  TA2 — kusurlu İADE: göndericinin stoğuna geri döner, hedef stoğa girmez
///  TA3 — kurallar: kusurlu varsa açıklama zorunlu; sağlam + kusurlu yoldakinden fazla olamaz
///  TA4 — yetki: gönderen şubeyle giriş yapan teslim alamaz; hedef şubeyle giriş yapan alır
///  TA5 — gönderim şoför/araç/fiş numarasını belgeye yazar (her zaman iki adımlı)
///  TA6 — iki platform + API kaynak kilidi (seçim kutusu yok, her zaman YoldaGonder, kusurlu alanları)
/// </summary>
public class TeslimKusurluTests : IDisposable
{
    private const string Co = "TKS";
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_tks_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s, _sA, _sB;
    private readonly string _a, _b, _m;
    private readonly StockService _stock;

    public TeslimKusurluTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "tks", "Teslim!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var sb = new BranchService(_f);
        _a = sb.Create(_s, new NewBranch("Merkez"));
        _b = sb.Create(_s, new NewBranch("Şantiye", "site"));
        _sA = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty) { OperatingBranchId = _a };
        _sB = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty) { OperatingBranchId = _b };
        _m = new MaterialService(_f).Create(_s, new NewMaterial("TK-1", "Boru"));
        _stock = new StockService(_f);
        _stock.ReceiveIn(_s, new[] { new StockLine(_m, 20m) }, Op(), _a);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _yol, _yol + "-wal", _yol + "-shm" }) try { File.Delete(p); } catch { }
    }

    private void Exec(string sql) { using var c = _f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
    private object? Tek(string sql) { using var c = _f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar(); }
    private static string Op() => "op-" + Guid.NewGuid().ToString("N");
    private decimal At(string loc) => _stock.GetBalanceAt(_s, _m, loc);
    private string Gonder(decimal q) => _stock.YoldaGonder(_sA, new[] { new StockLine(_m, q) }, _a, _b, Op()).DocumentId;

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    [Fact]
    public void TA1_Saglam_Kusurlu_Fire_Eksik()
    {
        var doc = Gonder(10m);
        _stock.TeslimAl(_sB, doc, new[] { new TeslimSatiri(_m, 6m, 2m) }, Op(), "ezik geldi");
        Assert.Equal(10m, At(_a));
        Assert.Equal(6m, At(_b));                                   // yalnız sağlam
        Assert.Equal(2m, At(StockBalanceWriter.Transit));           // eksik yolda
        Assert.Equal(18m, _stock.GetBalance(_s, _m));               // kusurlu 2 fire ile düştü
        var k = Assert.Single(Assert.Single(_stock.Yoldakiler(_s)).Kalemler);
        Assert.Equal(2m, k.Kusurlu);
        Assert.Equal(6m, k.TeslimAlinan);
        Assert.Equal(2m, k.Yolda);
        Assert.Equal("eksik", (string)Tek($"SELECT transit_status FROM stock_documents WHERE id='{doc}';")!);
        Assert.Equal("Kusurlu teslim: ezik geldi", (string)Tek($"SELECT note FROM stock_movements WHERE document_id='{doc}' AND operation_id LIKE '%:kus';")!);
    }

    [Fact]
    public void TA2_Kusurlu_Iade_Gondericiye_Doner()
    {
        var doc = Gonder(10m);
        _stock.TeslimAl(_sB, doc, new[] { new TeslimSatiri(_m, 7m, 3m, KusurluIade: true) }, Op(), "yanlış ölçü");
        Assert.Equal(13m, At(_a));                                  // 20 − 10 + 3 iade
        Assert.Equal(7m, At(_b));
        Assert.Equal(0m, At(StockBalanceWriter.Transit));
        Assert.Equal(20m, _stock.GetBalance(_s, _m));               // iade firma toplamını değiştirmez
        Assert.Equal("teslim", (string)Tek($"SELECT transit_status FROM stock_documents WHERE id='{doc}';")!);
    }

    [Fact]
    public void TA3_Kurallar()
    {
        var doc = Gonder(5m);
        Assert.Throws<ArgumentException>(() => _stock.TeslimAl(_sB, doc, new[] { new TeslimSatiri(_m, 4m, 1m) }, Op(), null));   // açıklama yok
        Assert.Throws<ArgumentException>(() => _stock.TeslimAl(_sB, doc, new[] { new TeslimSatiri(_m, 4m, 2m) }, Op(), "x"));    // 6 > 5
        Assert.Equal(5m, At(StockBalanceWriter.Transit));           // hiçbir şey yazılmadı
    }

    [Fact]
    public void TA4_Yalniz_Hedef_Teslim_Alir()
    {
        var doc = Gonder(4m);
        var ex = Assert.Throws<ForbiddenException>(() => _stock.TeslimAl(_sA, doc, new[] { new TeslimSatiri(_m, 4m) }, Op(), null));   // gönderen
        Assert.Contains("hedef şube/şantiyeyle giriş", ex.Message);
        Assert.Equal(0m, At(_b));
        _stock.TeslimAl(_sB, doc, new[] { new TeslimSatiri(_m, 4m) }, Op(), null);
        Assert.Equal(4m, At(_b));
    }

    [Fact]
    public void TA5_Gonderim_Sofor_Ve_Fis_Bilgisini_Tasir()
    {
        var doc = _stock.YoldaGonder(_sA, new[] { new StockLine(_m, 1m) }, _a, _b, Op(), "not", invoiceNo: "IRS-77").DocumentId;
        Assert.Equal("IRS-77", (string)Tek($"SELECT invoice_no FROM stock_documents WHERE id='{doc}';")!);
    }

    [Fact]
    public void TA6_Iki_Platform_Ve_Api_Kaynak_Kilidi()
    {
        var m = File.ReadAllText(Path.Combine(Kok(), "src/DepoWise.Desktop/ViewModels/StockEntryViewModel.cs"));
        Assert.DoesNotContain("private bool _yoldaTransfer", m);
        Assert.DoesNotContain("DesktopServices.Stock.Transfer(_session, exitLines", m);
        Assert.Contains("new TeslimSatiri(k.Kalem.MaterialId, k.Alinan, k.Kusurlu, k.KusurluIade)", m);
        var v = File.ReadAllText(Path.Combine(Kok(), "src/DepoWise.Desktop/Views/StockEntryView.axaml"));
        Assert.DoesNotContain("Yolda (teslimde girsin)", v);
        Assert.Contains("Value=\"{Binding Kusurlu}\"", v);
        var w = File.ReadAllText(Path.Combine(Kok(), "src/DepoWise.Web/Components/Pages/Stock.razor"));
        Assert.DoesNotContain("private bool _yolda;", w);
        Assert.DoesNotContain("yolda = _yolda", w);
        Assert.Contains("@bind-Value=\"k.Kusurlu\"", w);
        Assert.Contains("defectNote =", w);
        var p = File.ReadAllText(Path.Combine(Kok(), "src/DepoWise.Api/Program.cs"));
        Assert.DoesNotContain("if (d.Yolda == true)", p);
        Assert.Contains("new DepoWise.Infrastructure.Materials.TeslimSatiri(l.MaterialId, l.Quantity, l.Defective ?? 0m, l.ReturnDefective == true)", p);
    }
}
