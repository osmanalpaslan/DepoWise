using DepoWise.Application.Requests;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Purchasing;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-26 (AR-GE 2026-10-18, 05 F-071) — TALEPTEN TEK TIKLA KARŞILAMA ═══
///  TK1 — Durum tablosu: istenen, kaynak depo stoğu, toplam stok, kalan; kaynak verilmezse talebin gönderen şubesi
///  TK2 — Stoktan Gönder: gerçek transfer (kaynak düşer, hedef artar), belge talebe bağlı, gönderilen/yüzde güncellenir; tekrar zararsız
///  TK3 — Kurallar: kalandan fazla, talepte olmayan malzeme, kaynakta stok yok → reddedilir ve HİÇBİR ŞEY değişmez
///  TK4 — Sipariş Aç: talebe bağlı sipariş (teslim = hedef şube); siparişteki miktar kalandan düşer; iptal edilen sipariş sayılmaz
///  TK5 — Onaylanmamış talep ve iptal edilmiş operasyon karşılanamaz
///  TK6 — Firma ayrımı: başka firmanın talebi görülemez, gönderilemez, sipariş açılamaz
///  TK7 — Migration108 tekrar çalışabilir; eski şemada durum okunur, gönderim anlaşılır hata verir
///  TK8 — İki platform + API (kaynak kilidi)
/// </summary>
public class TalepKarsilamaTests : IDisposable
{
    private const string Co = "TKS";
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_tks_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly string _merkez, _santiye, _m1, _m2;
    private readonly StockService _stock;
    private readonly PurchaseOrderService _po;
    private readonly RequestService _talep;
    private readonly RequestOperationsService _ops;
    private readonly TalepKarsilamaService _k;

    public TalepKarsilamaTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "tks", "Talep!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var subeler = new BranchService(_f);
        _merkez = subeler.Create(_s, new NewBranch("Merkez Depo"));
        _santiye = subeler.Create(_s, new NewBranch("Şantiye 1"));
        var mat = new MaterialService(_f);
        _m1 = mat.Create(_s, new NewMaterial("TK-1", "Filtre"));
        _m2 = mat.Create(_s, new NewMaterial("TK-2", "Conta"));
        _stock = new StockService(_f);
        _po = new PurchaseOrderService(_f);
        _talep = new RequestService(_f, _stock);
        _ops = new RequestOperationsService(_f);
        _k = new TalepKarsilamaService(_f, _stock, _po);
        _stock.ReceiveIn(_s, new[] { new StockLine(_m1, 6m) }, Op(), _merkez);   // merkezde 6 filtre var, conta yok
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

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    /// <summary>Onaylı talep: 10 filtre + 4 conta; gönderen = merkez, gidecek = şantiye.</summary>
    private string OnayliTalep(bool onayla = true)
    {
        var h = _talep.Create(_s, new NewRequest(new[] { new RequestItemInput(_m1, 10m), new RequestItemInput(_m2, 4m) }, SubmitImmediately: true));
        if (!onayla) return h.Id;
        _talep.Approve(_s, h.Id);
        _ops.UpdateShipmentInfo(_s, h.Id, _merkez, _santiye, null);
        return h.Id;
    }

    private decimal Stok(string mat, string sube) => _stock.GetBalanceAt(_s, mat, sube);
    private TalepKalemDurumu Kalem(TalepKarsilama d, string mat) => d.Kalemler.Single(x => x.MaterialId == mat);

    // ══════════════════ TK1 — DURUM ══════════════════

    [Fact]
    public void TK1_Durum_Tablosu()
    {
        var id = OnayliTalep();
        var d = _k.Durum(_s, id);
        Assert.Equal(_merkez, d.FromBranchId);
        Assert.Equal(_santiye, d.ToBranchId);
        var f = Kalem(d, _m1);
        Assert.Equal(10m, f.Istenen);
        Assert.Equal(6m, f.KaynakStok);
        Assert.Equal(6m, f.ToplamStok);
        Assert.Equal(10m, f.Kalan);
        Assert.Equal(6m, f.Gonderilebilir);
        Assert.Equal(0m, Kalem(d, _m2).Gonderilebilir);
        Assert.Equal(0m, d.Yuzde);
        Assert.Equal(0m, Kalem(_k.Durum(_s, id, _santiye), _m1).KaynakStok);   // başka kaynak seçilebilir
    }

    // ══════════════════ TK2 — STOKTAN GÖNDER ══════════════════

    [Fact]
    public void TK2_Stoktan_Gonder_Gercek_Transfer()
    {
        var id = OnayliTalep();
        var op = Op();
        var r = _k.StoktanGonder(_s, id, _merkez, _santiye, new[] { new TalepKarsilamaSatiri(_m1, 6m) }, op);
        Assert.Equal(0m, Stok(_m1, _merkez));
        Assert.Equal(6m, Stok(_m1, _santiye));
        Assert.Equal(id, Tek($"SELECT request_id FROM stock_documents WHERE id='{r.DocumentId}';"));
        Assert.Equal("transfer", Tek($"SELECT doc_type FROM stock_documents WHERE id='{r.DocumentId}';"));
        var d = _k.Durum(_s, id);
        Assert.Equal(6m, Kalem(d, _m1).Gonderilen);
        Assert.Equal(4m, Kalem(d, _m1).Kalan);
        Assert.Equal(43m, d.Yuzde);   // 6 / 14

        // Tekrar gönderim (ağ yeniden denemesi): aynı belge, ikinci hareket yok
        Assert.Equal(r.DocumentId, _k.StoktanGonder(_s, id, _merkez, _santiye, new[] { new TalepKarsilamaSatiri(_m1, 6m) }, op).DocumentId);
        Assert.Equal(6m, Stok(_m1, _santiye));
        Assert.Equal(1L, Convert.ToInt64(Tek($"SELECT COUNT(*) FROM stock_documents WHERE request_id='{id}';")));
    }

    // ══════════════════ TK3 — KURALLAR ══════════════════

    [Fact]
    public void TK3_Kurallar_Ve_Hicbir_Sey_Degismez()
    {
        var id = OnayliTalep();
        _stock.ReceiveIn(_s, new[] { new StockLine(_m1, 20m) }, Op(), _merkez);   // merkezde 26 filtre
        var ex = Assert.Throws<ArgumentException>(() =>
            _k.StoktanGonder(_s, id, _merkez, _santiye, new[] { new TalepKarsilamaSatiri(_m1, 11m) }, Op()));
        Assert.Contains("kalanından", ex.Message);
        var yabanci = new MaterialService(_f).Create(_s, new NewMaterial("TK-X", "Talepte yok"));
        _stock.ReceiveIn(_s, new[] { new StockLine(yabanci, 5m) }, Op(), _merkez);
        Assert.Contains("Talepte olmayan", Assert.Throws<ArgumentException>(() =>
            _k.StoktanGonder(_s, id, _merkez, _santiye, new[] { new TalepKarsilamaSatiri(yabanci, 1m) }, Op())).Message);
        // Kaynakta conta yok → eksi stok kalkanı; filtre satırı da YAZILMAZ (tek işlem)
        Assert.ThrowsAny<Exception>(() => _k.StoktanGonder(_s, id, _merkez, _santiye,
            new[] { new TalepKarsilamaSatiri(_m1, 2m), new TalepKarsilamaSatiri(_m2, 1m) }, Op()));
        Assert.Equal(26m, Stok(_m1, _merkez));
        Assert.Equal(0m, Stok(_m1, _santiye));
        Assert.Equal(0L, Convert.ToInt64(Tek($"SELECT COUNT(*) FROM stock_documents WHERE request_id='{id}';")));
    }

    // ══════════════════ TK4 — SİPARİŞ AÇ ══════════════════

    [Fact]
    public void TK4_Siparis_Ac_Kalan_Icin()
    {
        var id = OnayliTalep();
        _k.StoktanGonder(_s, id, _merkez, _santiye, new[] { new TalepKarsilamaSatiri(_m1, 6m) }, Op());
        var sp = _k.SiparisAc(_s, id, null, new[] { new TalepKarsilamaSatiri(_m1, 4m, 5m), new TalepKarsilamaSatiri(_m2, 4m) });
        var satir = _po.List(_s).Single(o => o.Id == sp);
        Assert.Equal(id, satir.RequestId);
        Assert.Equal(_santiye, Tek($"SELECT branch_id FROM purchase_orders WHERE id='{sp}';"));
        Assert.Equal(2, _po.Lines(_s, sp).Count);
        var d = _k.Durum(_s, id);
        Assert.Equal(0m, Kalem(d, _m1).Kalan);
        Assert.Equal(4m, Kalem(d, _m2).Sipariste);
        Assert.Contains("kalanından", Assert.Throws<ArgumentException>(() =>
            _k.SiparisAc(_s, id, null, new[] { new TalepKarsilamaSatiri(_m2, 1m) })).Message);   // aynı mal ikinci kez sipariş edilmez

        _po.Cancel(_s, sp, "yanlış tedarikçi");
        Assert.Equal(4m, Kalem(_k.Durum(_s, id), _m2).Kalan);   // iptal edilen sipariş sayılmaz
    }

    // ══════════════════ TK5 — ONAY / İPTAL ══════════════════

    [Fact]
    public void TK5_Onaysiz_Ve_Iptal_Edilmis_Talep()
    {
        var bekleyen = OnayliTalep(onayla: false);
        Assert.Contains("ONAYLANMIŞ", Assert.Throws<InvalidOperationException>(() =>
            _k.StoktanGonder(_s, bekleyen, _merkez, _santiye, new[] { new TalepKarsilamaSatiri(_m1, 1m) }, Op())).Message);
        Assert.Throws<InvalidOperationException>(() => _k.SiparisAc(_s, bekleyen, null, new[] { new TalepKarsilamaSatiri(_m1, 1m) }));

        var id = OnayliTalep();
        _ops.ChangeStatus(_s, id, RequestOperationStatus.CancelledOps, "vazgeçildi");
        Assert.Contains("iptal", Assert.Throws<InvalidOperationException>(() =>
            _k.StoktanGonder(_s, id, _merkez, _santiye, new[] { new TalepKarsilamaSatiri(_m1, 1m) }, Op())).Message);
        Assert.Equal(6m, Stok(_m1, _merkez));
    }

    // ══════════════════ TK6 — FİRMA AYRIMI ══════════════════

    [Fact]
    public void TK6_Baska_Firmanin_Talebi()
    {
        var id = OnayliTalep();
        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('TKB','TKB',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin("TKB", "tkb", "Talep!2026", RoleKeys.CompanyAdmin);
        var b = new SessionContext(uid, "TKB", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _k.Durum(b, id));
        Assert.Throws<ForbiddenException>(() => _k.StoktanGonder(b, id, _merkez, _santiye, new[] { new TalepKarsilamaSatiri(_m1, 1m) }, Op()));
        Assert.Throws<ForbiddenException>(() => _k.SiparisAc(b, id, null, new[] { new TalepKarsilamaSatiri(_m1, 1m) }));
        Assert.Equal(6m, Stok(_m1, _merkez));
    }

    // ══════════════════ TK7 — MIGRATION / ESKİ ŞEMA ══════════════════

    [Fact]
    public void TK7_Migration_Ve_Eski_Sema()
    {
        using (var c = _f.Create()) new Migration108_StockDocRequest().Up(c, null!);   // tekrar çalışabilir
        var id = OnayliTalep();
        Exec("DROP INDEX ix_stock_documents_request;");
        Exec("ALTER TABLE stock_documents DROP COLUMN request_id;");
        Assert.Equal(0m, Kalem(_k.Durum(_s, id), _m1).Gonderilen);
        Assert.Contains("desteklemiyor", Assert.Throws<InvalidOperationException>(() =>
            _k.StoktanGonder(_s, id, _merkez, _santiye, new[] { new TalepKarsilamaSatiri(_m1, 1m) }, Op())).Message);
        Assert.Equal(6m, Stok(_m1, _merkez));
    }

    // ══════════════════ TK8 — KAYNAK KİLİDİ ══════════════════

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void TK8_Iki_Platform_Ve_Api()
    {
        var api = Oku("src/DepoWise.Api/Program.cs");
        Assert.Contains("app.MapGet(\"/api/request-ops/{id}/fulfilment\"", api);
        Assert.Contains("app.MapPost(\"/api/request-ops/{id}/ship\"", api);
        Assert.Contains("app.MapPost(\"/api/request-ops/{id}/order\"", api);

        var mvm = Oku("src/DepoWise.Desktop/ViewModels/RequestOperationsViewModel.cs");
        Assert.Contains("DesktopServices.TalepKarsilama.StoktanGonder(", mvm);
        Assert.Contains("DesktopServices.TalepKarsilama.SiparisAc(", mvm);
        var axaml = Oku("src/DepoWise.Desktop/Views/RequestOperationsView.axaml");
        Assert.Contains("StoktanGonderCommand", axaml);
        Assert.Contains("SiparisAcCommand", axaml);

        var web = Oku("src/DepoWise.Web/Components/Pages/RequestOperations.razor");
        Assert.Contains("/fulfilment", web);
        Assert.Contains("/ship\"", web);
        Assert.Contains("/order\"", web);
    }
}
