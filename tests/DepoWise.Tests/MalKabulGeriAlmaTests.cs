using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Purchasing;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-32 (AR-GE 2026-10-18, F-113) — MAL KABULDE İRSALİYE NO + KABULÜ GERİ ALMA ═══
///  MKG1 — İrsaliye no stok belgesine yazılır; belge siparişe bağlanır; kabul listesinde görünür
///  MKG2 — Geri alma: stok eski hâline döner, teslim alınan miktar düşer, tamamlanan sipariş yeniden açılır; tekrar çağrı zararsız
///  MKG3 — Mal kullanıldıysa (eksi stok) geri alma yapılmaz ve HİÇBİR ŞEY değişmez
///  MKG4 — Siparişe bağlı kabul stok ekranından iptal edilemez; bağsız (eski) belge eskisi gibi iptal edilir; yanlış sipariş reddedilir
///  MKG5 — Migration105 eski kabulleri işlem geçmişinden siparişe bağlar
///  MKG6 — İki platform: irsaliye alanı + kabul listesi + geri alma (kaynak kilidi)
/// </summary>
public class MalKabulGeriAlmaTests : IDisposable
{
    private const string Co = "MKG";
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_mkg_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly string _sube, _mat;
    private readonly StockService _stock;
    private readonly PurchaseOrderService _po;

    public MalKabulGeriAlmaTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "mkg", "admin123", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _sube = new BranchService(_f).Create(_s, new NewBranch("MKG Depo"));
        _mat = new MaterialService(_f).Create(_s, new NewMaterial("MKG-M", "Filtre"));
        _stock = new StockService(_f);
        _po = new PurchaseOrderService(_f);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _yol, _yol + "-wal", _yol + "-shm" }) try { File.Delete(p); } catch { }
    }

    private void Exec(string sql)
    {
        using var c = _f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
    }

    private object? Tek(string sql)
    {
        using var c = _f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar();
    }

    private (string Siparis, string Satir) Siparis(decimal miktar = 10m)
    {
        var id = _po.Create(_s, new NewPurchaseOrder("", BranchId: _sube, Lines: new[] { new NewPurchaseOrderLine(_mat, miktar, 5m) }));
        return (id, _po.Lines(_s, id).Single().Id);
    }

    private decimal Teslim(string siparis) => _po.Lines(_s, siparis).Single().ReceivedQty;
    private string Durum(string siparis) => _po.List(_s).Single(o => o.Id == siparis).Status;

    [Fact]
    public void MKG1_Irsaliye_No_Ve_Siparis_Bagi()
    {
        var (sp, satir) = Siparis();
        var belge = _po.Receive(_s, sp, new[] { new ReceiveLine(satir, 4m) }, "op-1", irsaliyeNo: " IRS-2026-15 ");
        Assert.Equal("IRS-2026-15", Tek($"SELECT invoice_no FROM stock_documents WHERE id='{belge}';"));
        Assert.Equal(sp, Tek($"SELECT purchase_order_id FROM stock_documents WHERE id='{belge}';"));
        var k = Assert.Single(_po.Receipts(_s, sp));
        Assert.Equal(belge, k.DocumentId);
        Assert.Equal(4m, k.ToplamMiktar);
        Assert.Contains("İrsaliye IRS-2026-15", k.Ozet);
        Assert.False(k.IptalMi);
    }

    [Fact]
    public void MKG2_Geri_Alma_Stok_Teslim_Ve_Durum()
    {
        var (sp, satir) = Siparis(10m);
        _po.Receive(_s, sp, new[] { new ReceiveLine(satir, 6m) }, "op-a");
        var son = _po.Receive(_s, sp, new[] { new ReceiveLine(satir, 4m) }, "op-b");
        Assert.Equal("closed", Durum(sp));
        Assert.Equal(10m, _stock.GetBalance(_s, _mat));

        _po.ReverseReceipt(_s, sp, son, "yanlış teslimat");
        Assert.Equal(6m, _stock.GetBalance(_s, _mat));              // defter: ters kayıt
        Assert.Equal(6m, Teslim(sp));                              // sipariş: teslim alınan düştü
        Assert.Equal("open", Durum(sp));                            // tamamlanan sipariş yeniden açıldı
        Assert.True(_po.Receipts(_s, sp).Single(r => r.DocumentId == son).IptalMi);
        Assert.Equal(1L, Convert.ToInt64(Tek($"SELECT COUNT(*) FROM audit_logs WHERE entity_type='purchase_order' AND entity_id='{sp}' AND action='reverse' AND after_json LIKE '%yanlış teslimat%';")));

        _po.ReverseReceipt(_s, sp, son, "tekrar");                 // zaten geri alınmış → değişiklik yok
        Assert.Equal(6m, _stock.GetBalance(_s, _mat));
        Assert.Equal(6m, Teslim(sp));
        Assert.Throws<ArgumentException>(() => _po.ReverseReceipt(_s, sp, son, "  "));   // gerekçe zorunlu
    }

    [Fact]
    public void MKG3_Mal_Kullanildiysa_Geri_Alinmaz_Hicbir_Sey_Degismez()
    {
        var (sp, satir) = Siparis(5m);
        var belge = _po.Receive(_s, sp, new[] { new ReceiveLine(satir, 5m) }, "op-c");
        _stock.IssueOut(_s, new[] { new StockLine(_mat, 3m) }, "cikis-1", _sube);
        Assert.ThrowsAny<Exception>(() => _po.ReverseReceipt(_s, sp, belge, "geri al"));
        Assert.Equal(2m, _stock.GetBalance(_s, _mat));
        Assert.Equal(5m, Teslim(sp));
        Assert.Equal("closed", Durum(sp));
        Assert.False(_po.Receipts(_s, sp).Single().IptalMi);
    }

    [Fact]
    public void MKG4_Stok_Ekrani_Bagli_Kabulu_Iptal_Edemez()
    {
        var (sp, satir) = Siparis();
        var belge = _po.Receive(_s, sp, new[] { new ReceiveLine(satir, 2m) }, "op-d");
        var ex = Assert.Throws<ForbiddenException>(() => _stock.ReverseDocument(_s, belge, "stok ekranından"));
        Assert.Contains("Satın Alma ekranından", ex.Message);
        Assert.Equal(2m, Teslim(sp));

        var (sp2, _) = Siparis();
        Assert.Throws<ForbiddenException>(() => _po.ReverseReceipt(_s, sp2, belge, "yanlış sipariş"));   // başka siparişin kabulü

        Exec($"UPDATE stock_documents SET purchase_order_id=NULL WHERE id='{belge}';");   // eski (bağsız) belge
        _stock.ReverseDocument(_s, belge, "eski belge");                                    // eski davranış
        Assert.Equal("cancelled", Tek($"SELECT status FROM stock_documents WHERE id='{belge}';"));
    }

    [Fact]
    public void MKG5_Migration_Eski_Kabulleri_Baglar()
    {
        var (sp, satir) = Siparis();
        var belge = _po.Receive(_s, sp, new[] { new ReceiveLine(satir, 1m) }, "op-e");
        Exec($"UPDATE stock_documents SET purchase_order_id=NULL WHERE id='{belge}';");
        using (var conn = _f.Create())
        using (var tx = conn.BeginTransaction())
        {
            new Migration105_StockDocPurchaseOrder().Up(conn, tx);
            tx.Commit();
        }
        Assert.Equal(sp, Tek($"SELECT purchase_order_id FROM stock_documents WHERE id='{belge}';"));
    }

    [Fact]
    public void MKG6_Iki_Platform_Baglantilari()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !File.Exists(Path.Combine(dir, "DepoWise.sln")); i++) dir = Directory.GetParent(dir)!.FullName;
        string Oku(params string[] p) => File.ReadAllText(Path.Combine(new[] { dir }.Concat(p).ToArray()));
        var vm = Oku("src", "DepoWise.Desktop", "ViewModels", "PurchasingViewModel.cs");
        Assert.Contains("irsaliyeNo: string.IsNullOrWhiteSpace(ReceiveIrsaliyeNo)", vm);
        Assert.Contains("DesktopServices.Purchasing.ReverseReceipt(", vm);
        Assert.Contains("ReverseReceiptCommand", Oku("src", "DepoWise.Desktop", "Views", "PurchasingView.axaml"));
        var web = Oku("src", "DepoWise.Web", "Components", "Pages", "Purchasing.razor");
        Assert.Contains("/receipts/{documentId}/reverse", web);
        Assert.Contains("irsaliyeNo = string.IsNullOrWhiteSpace(_irsaliyeNo)", web);
        Assert.Contains("CanReverseReceipt => CanReceive && Auth.CanEdit(\"stock\")", web);
        Assert.Contains("CanReverseReceipt => CanReceive && AccessControl.Can(_session, \"stock\", PermissionAction.Edit)", vm);
    }
}
