using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Purchasing;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-25 (AR-GE 2026-10-18, 05 F-072 / F-148) — FATURAYI MAL KABULDEN OLUŞTUR + EŞLEŞTİRME ═══
///  KF1 — Taslak: cari tedarikçi eşlemesinden, satırlar kabulden (miktar + fiyat), not sipariş/kabul/irsaliye; eşlemesiz uyarı
///  KF2 — Kabule bağlı fatura stok YAZMAZ (istemci "stoğu etkilesin" dese bile), bağ kaydedilir, cari borcu oluşur
///  KF3 — Aynı kabul ikinci kez faturalanamaz; tekrar gönderim zararsız; fatura iptal edilince yeniden faturalanır
///  KF4 — Eşleştirme: miktar kabulden fazla / kabulde olmayan malzeme / satış faturası / geri alınmış kabul reddedilir
///  KF5 — Faturalı kabul geri alınamaz; kabul listesi fatura no'yu gösterir, düğme yalnız faturalanabilir kabulde
///  KF6 — Firma ayrımı: başka firmanın kabulü taslak ve kayıtta reddedilir
///  KF7 — Migration107 tekrar çalışabilir; eski şemada bağlı kayıt anlaşılır hata, liste bozulmaz
///  KF8 — İki platform + API (kaynak kilidi)
/// </summary>
public class KabuldenFaturaTests : IDisposable
{
    private const string Co = "KFT";
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_kft_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly string _sube, _mat, _mat2, _tedarikci, _cari;
    private readonly StockService _stock;
    private readonly PurchaseOrderService _po;
    private readonly InvoiceService _fatura;

    public KabuldenFaturaTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "kft", "Kabul!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _sube = new BranchService(_f).Create(_s, new NewBranch("KFT Depo"));
        _mat = new MaterialService(_f).Create(_s, new NewMaterial("KFT-M", "Filtre"));
        _mat2 = new MaterialService(_f).Create(_s, new NewMaterial("KFT-N", "Conta"));
        _tedarikci = "sup-" + Guid.NewGuid().ToString("N");
        Exec($"INSERT INTO suppliers(id,company_id,name,created_at,updated_at,version,is_deleted) VALUES('{_tedarikci}','{Co}','Örnek Tedarik',1,1,1,0);");
        _cari = new PartyService(_f).Create(_s, new NewParty("C-1", "Örnek Tedarik Ltd.", PartyTypes.Both, SupplierId: _tedarikci));
        _stock = new StockService(_f);
        _po = new PurchaseOrderService(_f);
        _fatura = new InvoiceService(_f, _stock, new PartyLedgerService(_f));
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

    /// <summary>İki kalemli sipariş (10 × 5 TL, 4 × 2,5 TL) + kısmi mal kabul (6 ve 4).</summary>
    private (string Siparis, string Kabul) SiparisVeKabul(string? tedarikci = null)
    {
        var sp = _po.Create(_s, new NewPurchaseOrder("", SupplierId: tedarikci ?? _tedarikci, BranchId: _sube, Lines: new[]
            { new NewPurchaseOrderLine(_mat, 10m, 5m), new NewPurchaseOrderLine(_mat2, 4m, 2.5m) }));
        var satirlar = _po.Lines(_s, sp);
        var kabul = _po.Receive(_s, sp, new[]
        {
            new ReceiveLine(satirlar.Single(l => l.MaterialId == _mat).Id, 6m),
            new ReceiveLine(satirlar.Single(l => l.MaterialId == _mat2).Id, 4m),
        }, Op(), irsaliyeNo: "IRS-77");
        return (sp, kabul);
    }

    private NewInvoice TaslaktanFatura(KabulFaturaTaslagi t, string? op = null, bool stok = true) => new(
        InvoiceDirections.Purchase, t.PartyId ?? _cari,
        t.Lines.Select(l => new NewInvoiceLine(l.MaterialId, null, l.Unit, l.Quantity, l.UnitPrice, VatRate: 20m)).ToList(),
        op ?? Op(), ExternalNo: "SAT-123", BranchId: t.BranchId, AffectsStock: stok, ReceiptDocumentId: t.ReceiptDocumentId);

    private string Bakiye(string mat) => Tek($"SELECT group_concat(quantity) FROM stock_balances WHERE material_id='{mat}';") as string ?? "";

    // ══════════════════ KF1 — TASLAK ══════════════════

    [Fact]
    public void KF1_Taslak_Kabulden_Dolar()
    {
        var (sp, kabul) = SiparisVeKabul();
        var t = _fatura.KabulTaslagi(_s, kabul);
        Assert.Equal(_cari, t.PartyId);
        Assert.Contains("Örnek Tedarik Ltd.", t.PartyText);
        Assert.Null(t.Uyari);
        Assert.Equal(_sube, t.BranchId);
        Assert.Equal(2, t.Lines.Count);
        var a = t.Lines.Single(l => l.MaterialId == _mat);
        Assert.Equal(6m, a.Quantity);
        Assert.Equal(5m, a.UnitPrice);
        Assert.Equal(2.5m, t.Lines.Single(l => l.MaterialId == _mat2).UnitPrice);
        var siparisNo = _po.List(_s).Single(o => o.Id == sp).OrderNo;
        Assert.Contains(siparisNo, t.Not);
        Assert.Contains("irsaliye IRS-77", t.Not);

        // Tedarikçi bir cariye bağlı değilse cari boş gelir ve ne yapılacağı söylenir
        var digerTed = "sup-" + Guid.NewGuid().ToString("N");
        Exec($"INSERT INTO suppliers(id,company_id,name,created_at,updated_at,version,is_deleted) VALUES('{digerTed}','{Co}','Eşlenmemiş',1,1,1,0);");
        var (_, kabul2) = SiparisVeKabul(digerTed);
        var t2 = _fatura.KabulTaslagi(_s, kabul2);
        Assert.Null(t2.PartyId);
        Assert.Contains("Tedarikçi", t2.Uyari);
    }

    // ══════════════════ KF2 — STOK YAZMAZ ══════════════════

    [Fact]
    public void KF2_Kabule_Bagli_Fatura_Stok_Yazmaz()
    {
        var (_, kabul) = SiparisVeKabul();
        var once = Bakiye(_mat);
        var t = _fatura.KabulTaslagi(_s, kabul);
        var r = _fatura.Create(_s, TaslaktanFatura(t, stok: true));   // istemci "stoğu etkilesin" dese bile
        Assert.Null(r.StockDocumentId);
        Assert.Equal(once, Bakiye(_mat));
        Assert.Equal(0L, Convert.ToInt64(Tek($"SELECT affects_stock FROM invoices WHERE id='{r.Id}';")));
        Assert.Equal(kabul, Tek($"SELECT receipt_document_id FROM invoices WHERE id='{r.Id}';"));
        Assert.NotNull(r.LedgerEntryId);   // cari borcu yine oluşur
        Assert.Equal(48m, Money.Parse((string)Tek($"SELECT grand_total FROM invoices WHERE id='{r.Id}';")!));   // (6×5 + 4×2,5) × 1,20
    }

    // ══════════════════ KF3 — İKİNCİ FATURA YOK ══════════════════

    [Fact]
    public void KF3_Ayni_Kabul_Ikinci_Kez_Faturalanamaz()
    {
        var (_, kabul) = SiparisVeKabul();
        var t = _fatura.KabulTaslagi(_s, kabul);
        var op = Op();
        var ilk = _fatura.Create(_s, TaslaktanFatura(t, op));
        Assert.True(_fatura.Create(_s, TaslaktanFatura(t, op)).AlreadyExisted);   // tekrar gönderim zararsız
        var ex = Assert.Throws<ArgumentException>(() => _fatura.Create(_s, TaslaktanFatura(t)));
        Assert.Contains(ilk.InvoiceNo, ex.Message);
        Assert.Throws<ArgumentException>(() => _fatura.KabulTaslagi(_s, kabul));
        Assert.Equal(1L, Convert.ToInt64(Tek($"SELECT COUNT(*) FROM invoices WHERE receipt_document_id='{kabul}';")));

        _fatura.Cancel(_s, ilk.Id, "yanlış KDV");
        var yeniden = _fatura.Create(_s, TaslaktanFatura(_fatura.KabulTaslagi(_s, kabul)));
        Assert.False(yeniden.AlreadyExisted);
    }

    // ══════════════════ KF4 — EŞLEŞTİRME ══════════════════

    [Fact]
    public void KF4_Eslestirme_Kurallari()
    {
        var (sp, kabul) = SiparisVeKabul();
        var t = _fatura.KabulTaslagi(_s, kabul);

        var fazla = TaslaktanFatura(t) with
        {
            Lines = new[] { new NewInvoiceLine(_mat, null, null, 7m, 5m, VatRate: 20m) },
        };
        var ex = Assert.Throws<ArgumentException>(() => _fatura.Create(_s, fazla));
        Assert.Contains("fazla", ex.Message);

        var bolunmus = TaslaktanFatura(t) with   // aynı malzeme iki satırda: toplam (4+3=7) yine kabulden fazla
        {
            Lines = new[] { new NewInvoiceLine(_mat, null, null, 4m, 5m), new NewInvoiceLine(_mat, null, null, 3m, 5m) },
        };
        Assert.Throws<ArgumentException>(() => _fatura.Create(_s, bolunmus));

        var yabanci = new MaterialService(_f).Create(_s, new NewMaterial("KFT-X", "Kabulde yok"));
        Assert.Throws<ArgumentException>(() => _fatura.Create(_s, TaslaktanFatura(t) with
        {
            Lines = new[] { new NewInvoiceLine(yabanci, null, null, 1m, 5m) },
        }));
        Assert.Throws<ArgumentException>(() => _fatura.Create(_s, TaslaktanFatura(t) with { Direction = InvoiceDirections.Sales }));

        // Eksik miktar ve hizmet satırı serbest (kısmi fatura, nakliye bedeli)
        var kismi = _fatura.Create(_s, TaslaktanFatura(t) with
        {
            Lines = new[] { new NewInvoiceLine(_mat, null, null, 5m, 5m), new NewInvoiceLine(null, "Nakliye", null, 1m, 30m) },
        });
        Assert.False(kismi.AlreadyExisted);

        // Geri alınmış kabul faturalaştırılamaz
        var (sp2, kabul2) = SiparisVeKabul();
        var t2 = _fatura.KabulTaslagi(_s, kabul2);
        _po.ReverseReceipt(_s, sp2, kabul2, "yanlış kabul");
        Assert.Contains("geri alınmış", Assert.Throws<ArgumentException>(() => _fatura.KabulTaslagi(_s, kabul2)).Message);
        Assert.Contains("geri alınmış", Assert.Throws<ArgumentException>(() => _fatura.Create(_s, TaslaktanFatura(t2))).Message);
        Assert.Equal(0L, Convert.ToInt64(Tek($"SELECT COUNT(*) FROM invoices WHERE receipt_document_id='{kabul2}';")));
    }

    // ══════════════════ KF5 — GERİ ALMA VE LİSTE ══════════════════

    [Fact]
    public void KF5_Faturali_Kabul_Geri_Alinamaz_Ve_Listede_Gorunur()
    {
        var (sp, kabul) = SiparisVeKabul();
        Assert.True(_po.Receipts(_s, sp).Single().Faturalanabilir);
        var r = _fatura.Create(_s, TaslaktanFatura(_fatura.KabulTaslagi(_s, kabul)));
        var satir = _po.Receipts(_s, sp).Single();
        Assert.Equal(r.InvoiceNo, satir.FaturaNo);
        Assert.False(satir.Faturalanabilir);
        Assert.Contains("Fatura " + r.InvoiceNo, satir.Ozet);

        var ex = Assert.Throws<InvalidOperationException>(() => _po.ReverseReceipt(_s, sp, kabul, "yanlış"));
        Assert.Contains("faturayı iptal", ex.Message);
        Assert.Equal("active", Tek($"SELECT status FROM stock_documents WHERE id='{kabul}';"));

        _fatura.Cancel(_s, r.Id, "iade");
        Assert.True(_po.Receipts(_s, sp).Single().Faturalanabilir);
        _po.ReverseReceipt(_s, sp, kabul, "yanlış");
        Assert.Equal("cancelled", Tek($"SELECT status FROM stock_documents WHERE id='{kabul}';"));
    }

    // ══════════════════ KF6 — FİRMA AYRIMI ══════════════════

    [Fact]
    public void KF6_Baska_Firmanin_Kabulu_Reddedilir()
    {
        var (_, kabul) = SiparisVeKabul();
        var t = _fatura.KabulTaslagi(_s, kabul);
        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('KFB','KFB',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin("KFB", "kfb", "Kabul!2026", RoleKeys.CompanyAdmin);
        var b = new SessionContext(uid, "KFB", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var bSube = new BranchService(_f).Create(b, new NewBranch("B Depo"));
        var bCari = new PartyService(_f).Create(b, new NewParty("B-1", "B Cari", PartyTypes.Both));
        Assert.Throws<ForbiddenException>(() => _fatura.KabulTaslagi(b, kabul));
        Assert.Throws<ForbiddenException>(() => _fatura.Create(b, new NewInvoice(InvoiceDirections.Purchase, bCari,
            new[] { new NewInvoiceLine(null, "Hizmet", null, 1m, 10m) }, Op(), BranchId: bSube, ReceiptDocumentId: kabul)));
        Assert.Equal(0L, Convert.ToInt64(Tek($"SELECT COUNT(*) FROM invoices WHERE receipt_document_id='{kabul}';")));
        Assert.Equal(2, t.Lines.Count);
    }

    // ══════════════════ KF7 — MIGRATION / ESKİ ŞEMA ══════════════════

    [Fact]
    public void KF7_Migration_Ve_Eski_Sema()
    {
        using (var c = _f.Create()) new Migration107_InvoiceReceiptLink().Up(c, null!);   // tekrar çalışabilir
        var (sp, kabul) = SiparisVeKabul();
        var t = _fatura.KabulTaslagi(_s, kabul);
        Exec("DROP INDEX ix_invoices_receipt;");
        Exec("ALTER TABLE invoices DROP COLUMN receipt_document_id;");
        var ex = Assert.Throws<InvalidOperationException>(() => _fatura.Create(_s, TaslaktanFatura(t)));
        Assert.Contains("desteklemiyor", ex.Message);
        Assert.Null(_po.Receipts(_s, sp).Single().FaturaNo);   // liste bozulmaz
        // Bağsız fatura eski şemada eskisi gibi kesilir
        _fatura.Create(_s, new NewInvoice(InvoiceDirections.Purchase, _cari,
            new[] { new NewInvoiceLine(null, "Hizmet", null, 1m, 10m) }, Op(), BranchId: _sube, AffectsStock: false));
        using (var c = _f.Create()) new Migration107_InvoiceReceiptLink().Up(c, null!);
        var bagli = _fatura.Create(_s, TaslaktanFatura(t));
        Assert.Equal(kabul, Tek($"SELECT receipt_document_id FROM invoices WHERE id='{bagli.Id}';"));
    }

    // ══════════════════ KF8 — KAYNAK KİLİDİ ══════════════════

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void KF8_Iki_Platform_Ve_Api()
    {
        var api = Oku("src/DepoWise.Api/Program.cs");
        Assert.Contains("app.MapGet(\"/api/invoices/from-receipt/{documentId}\"", api);
        Assert.Contains("d.AffectsStock ?? true, d.ReceiptDocumentId));", api);
        Assert.Contains("invoiceNo = r.FaturaNo, canInvoice = r.Faturalanabilir", api);

        Assert.Contains("NavigateTo(\"invoices\", \"kabul:\" + r.DocumentId)", Oku("src/DepoWise.Desktop/ViewModels/PurchasingViewModel.cs"));
        Assert.Contains("InvoiceReceiptCommand", Oku("src/DepoWise.Desktop/Views/PurchasingView.axaml"));
        var mvm = Oku("src/DepoWise.Desktop/ViewModels/InvoicesViewModel.cs");
        Assert.Contains("DesktopServices.Invoices.KabulTaslagi(_session, belgeId)", mvm);
        Assert.Contains("ReceiptDocumentId: KabulBelgesi", mvm);
        Assert.Contains("IsEnabled=\"{Binding StokKutusuAcik}\"", Oku("src/DepoWise.Desktop/Views/InvoicesView.axaml"));

        var web = Oku("src/DepoWise.Web/Components/Pages/Invoices.razor");
        Assert.Contains("/api/invoices/from-receipt/", web);
        Assert.Contains("receiptDocumentId = _kabulBelgesi", web);
        Assert.Contains("[SupplyParameterFromQuery(Name = \"kabul\")]", web);
        Assert.Contains("/invoices?kabul=", Oku("src/DepoWise.Web/Components/Pages/Purchasing.razor"));
    }
}
