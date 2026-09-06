using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Purchasing;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 3c-2 — KALAN KAÇAK KANALLAR (ADR-223) ═══
///
/// FAZ 3c malzeme birim fiyatının <b>stok hareketi</b> ve <b>şablon</b> taşıyıcılarını kapattı.
/// Bu tur, aynı fiyatın kalan taşıyıcılarını kapatır ve FAZ 3c'de üretilen bir GERÇEK HATAYI düzeltir.
///
///  KL1 — Koruma yokken davranış birebir bugünkü gibi (sahte yeşil önlemi)
///  KL2 — 🔴 Sipariş satırı fiyatı gizlenir; ham veri YERİNDE kalır
///  KL3 — 🔴 Sipariş TOPLAMI da gizlenir (miktar biliniyorken fiyat geri hesaplanabilirdi)
///  KL4 — 🔴 Fiyatı göremeyen kullanıcının GİRDİĞİ sipariş fiyatı yazılmaz
///  KL5 — 🔴 REGRESYON: mal kabulde SİPARİŞTEKİ fiyat korunur (sessiz veri kaybı yok)
///  KL6 — 🔴 Malzeme satırlı fatura DETAYI açılmaz; hizmet faturası açılır (gereksiz daraltma yok)
///  KL7 — 🔴 Malzeme satırlı fatura YAZILAMAZ (0 yazıp yanlış mali belge üretmek yerine açık ret)
///  KL8 — 🔴 Raporda türetilmiş malzeme maliyeti kolonları kaldırılır
/// </summary>
public class AlanKacakKanali2Tests : IDisposable
{
    private const string Co = "KL2";
    private const string Pass = "Kl2!2026";
    private static readonly long Gun = 1_700_000_000_000;

    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly AuthService _auth;
    private readonly PermissionService _perms;
    private readonly FieldProtectionService _koruma;
    private readonly PermissionSnapshotCache _cache = new();
    private readonly PurchaseOrderService _satinAlma;
    private readonly InvoiceService _fatura;
    private readonly InvoiceQueryService _faturaOku;
    private readonly ReportService _rapor;
    private readonly string _mat, _sube, _tedarikci, _cari, _personelId;
    private readonly SessionContext _admin;

    public AlanKacakKanali2Tests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_kacak2_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");

        var users = new UserService(_f);
        users.EnsureInitialAdmin(Co, "kl2_admin", Pass, RoleKeys.CompanyAdmin);
        _personelId = users.EnsureInitialAdmin(Co, "kl2_personel", Pass, RoleKeys.Staff);

        _auth = new AuthService(_f, null, _cache);
        _perms = new PermissionService(_f, null, _cache);
        _koruma = new FieldProtectionService(_f, null, _cache);
        _satinAlma = new PurchaseOrderService(_f);
        _fatura = new InvoiceService(_f, new StockService(_f), new PartyLedgerService(_f));
        _faturaOku = new InvoiceQueryService(_f);
        _rapor = new ReportService(_f);

        _admin = Oturum("kl2_admin");
        _sube = new BranchService(_f).Create(_admin, new NewBranch("Merkez"));
        _mat = new MaterialService(_f).Create(_admin, new NewMaterial("KL2-M", "Çimento", UnitPrice: 10m));
        _tedarikci = new LookupService(_f).AddSupplier(_admin, "ABC Yapı");
        _cari = new PartyService(_f).Create(_admin, new NewParty("C-1", "ABC Yapı", PartyTypes.Supplier));

        // Kısıtlı kullanıcı: modül yetkileri TAM — sınanan şey ALAN yetkisidir, modül yetkisi değil.
        _perms.SaveForUser(SuperAdmin(), _personelId,
            new[]
            {
                Tam("materials"), Tam("stock"), Tam("purchasing"), Tam("invoices"), Tam("parties"), Tam("reports"),
                // ⚠️ TEST KURULUMU (ürün hatası değil): ADR-181'den beri rapor çalıştırmak için
                // rapor KALEMİ yetkisi de gerekir. Kalem verilmezse test, alan yetkisini değil
                // rapor yetkisini ölçerdi.
                Tam(AppModules.ReportItemKey("maintenance")),
            },
            Array.Empty<string>());
    }

    // ── yardımcılar ─────────────────────────────────────────────────────────────────────────

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static SessionContext SuperAdmin() => new("sa", Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
    private static ModulePermission Tam(string m) => new(m, true, true, true, true);

    private SessionContext Oturum(string ad)
    {
        var r = _auth.Login(Co, ad, Pass);
        Assert.True(r.Success, "Giriş başarısız: " + ad);
        return r.Session!;
    }

    private void FiyatiKoru(bool korumali = true)
        => _koruma.Set(SuperAdmin(), FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice, korumali);

    private string Siparis(decimal fiyat = 77.55m, string no = "PO-1")
        => _satinAlma.Create(_admin, new NewPurchaseOrder(no, _tedarikci, null, _sube, null, Gun, null,
            new[] { new NewPurchaseOrderLine(_mat, 4m, fiyat, "TRY") }));

    /// <summary>Veritabanındaki HAM sipariş satırı fiyatı — maskeleme değil, gerçek kayıt.</summary>
    private decimal? HamSiparisFiyati(string orderId)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT unit_price FROM purchase_order_lines WHERE order_id=@o;";
        cmd.AddWithValue("@o", orderId);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : Money.Parse((string)v);
    }

    private decimal? HamHareketFiyati()
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT unit_price FROM stock_movements ORDER BY created_at DESC LIMIT 1;";
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : Money.Parse((string)v);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }

    // ══════════════════ KL1 — GERİYE UYUMLULUK ══════════════════

    /// <summary>⭐ Koruma yokken sipariş fiyatı ve toplamı AYNEN görünür. Bu test olmadan diğerleri
    /// sahte yeşil olurdu (alan zaten hiç gelmiyor olsaydı da geçerlerdi).</summary>
    [Fact]
    public void KL1_Koruma_Yokken_Siparis_Fiyati_Ve_Toplami_Gorunur()
    {
        var id = Siparis();
        var s = Oturum("kl2_personel");

        Assert.Equal(77.55m, _satinAlma.Lines(s, id).Single().UnitPrice);
        var satir = _satinAlma.List(s).Single(o => o.Id == id);
        Assert.Equal(310.20m, satir.TotalAmount);          // 4 × 77,55
        Assert.Contains("310", satir.TotalDisplay);
    }

    // ══════════════════ KL2 / KL3 — OKUMA ══════════════════

    [Fact]
    public void KL2_Korumali_Siparis_Satirinda_Fiyat_Gizlenir_Veri_Yerinde()
    {
        var id = Siparis();
        FiyatiKoru();
        var s = Oturum("kl2_personel");

        Assert.Null(_satinAlma.Lines(s, id).Single().UnitPrice);
        Assert.Equal("—", _satinAlma.Lines(s, id).Single().PriceDisplay);
        Assert.Equal(77.55m, HamSiparisFiyati(id));        // koruma yalnız GÖRÜNÜMÜ etkiler
        Assert.Equal(77.55m, _satinAlma.Lines(_admin, id).Single().UnitPrice);   // yetkili görmeye devam eder
    }

    [Fact]
    public void KL3_Korumali_Siparis_Toplami_Da_Gizlenir()
    {
        var id = Siparis();
        FiyatiKoru();
        var s = Oturum("kl2_personel");

        var satir = _satinAlma.List(s).Single(o => o.Id == id);
        Assert.Equal(0m, satir.TotalAmount);               // hiç hesaplanmadı
        Assert.Equal("—", satir.TotalDisplay);
    }

    // ══════════════════ KL4 — YAZMA ══════════════════

    [Fact]
    public void KL4_Fiyati_Goremeyen_Kullanicinin_Girdigi_Siparis_Fiyati_Yazilmaz()
    {
        FiyatiKoru();
        var s = Oturum("kl2_personel");

        var id = _satinAlma.Create(s, new NewPurchaseOrder("PO-KL4", _tedarikci, null, _sube, null, Gun, null,
            new[] { new NewPurchaseOrderLine(_mat, 3m, 999.99m, "TRY") }));

        Assert.Null(HamSiparisFiyati(id));                 // gönderilen değer YOK SAYILDI
        Assert.Null(_satinAlma.Lines(_admin, id).Single().UnitPrice);   // yönetici de 999,99 görmez
    }

    // ══════════════════ KL5 — REGRESYON (FAZ 3c'de üretilen gerçek hata) ══════════════════

    /// <summary>
    /// 🔴 FAZ 3c'nin yazma kapısı, mal kabulde SUNUCUNUN sipariş satırından okuduğu fiyatı da
    /// siliyordu: fiyatı göremeyen depo görevlisi mal kabul edince, siparişte YAZILI olan fiyat
    /// stok hareketine geçmiyordu. Bu güvenlik değil, <b>sessiz veri kaybı</b>dır. Kapı artık
    /// yalnız KULLANICININ gönderdiği fiyata uygulanır.
    /// </summary>
    [Fact]
    public void KL5_Mal_Kabulde_Siparisteki_Fiyat_Korunur()
    {
        var id = Siparis(88.80m, "PO-KL5");
        var lineId = _satinAlma.Lines(_admin, id).Single().Id;
        FiyatiKoru();
        var s = Oturum("kl2_personel");

        _satinAlma.Receive(s, id, new[] { new ReceiveLine(lineId, 4m) }, Guid.NewGuid().ToString("N"));

        Assert.Equal(88.80m, HamHareketFiyati());          // fiyat kaybolmadı
    }

    // ══════════════════ KL6 / KL7 — FATURA ══════════════════

    [Fact]
    public void KL6_Malzemeli_Fatura_Detayi_Acilmaz_Hizmet_Faturasi_Acilir()
    {
        var malzemeli = _fatura.Create(_admin, Fatura(malzemeli: true)).Id;
        var hizmet = _fatura.Create(_admin, Fatura(malzemeli: false)).Id;
        FiyatiKoru();
        var s = Oturum("kl2_personel");

        Assert.Throws<ForbiddenException>(() => _faturaOku.Get(s, malzemeli));
        var acilan = _faturaOku.Get(s, hizmet);            // hizmet faturası GEREKSİZ yere kapatılmaz
        Assert.Single(acilan.Lines);
    }

    [Fact]
    public void KL7_Fiyati_Goremeyen_Kullanici_Malzemeli_Fatura_Yazamaz()
    {
        FiyatiKoru();
        var s = Oturum("kl2_personel");

        // Malzeme satırı → açık ret (0 yazıp yanlış genel toplam üretmek YASAK).
        Assert.Throws<ForbiddenException>(() => _fatura.Create(s, Fatura(malzemeli: true)));
        // Hizmet satırı → eskisi gibi yazılır.
        Assert.NotNull(_fatura.Create(s, Fatura(malzemeli: false)).Id);
    }

    private NewInvoice Fatura(bool malzemeli) => new(
        InvoiceDirections.Purchase, _cari,
        new[]
        {
            malzemeli
                ? new NewInvoiceLine(_mat, null, "adet", 2m, 50m)
                : new NewInvoiceLine(null, "Nakliye hizmeti", "adet", 1m, 250m),
        },
        Guid.NewGuid().ToString("N"), BranchId: _sube, InvoiceDate: Gun, AffectsStock: false);

    // ══════════════════ KL8 — RAPOR (türetilmiş maliyet) ══════════════════

    /// <summary>
    /// 🔴 Bakım raporundaki "Malzeme Maliyeti" = miktar × birim fiyat; miktar aynı satırda görünür,
    /// yani tutar birim fiyatı GERİ HESAPLANABİLİR kılar. Korumalıyken kolon TAMAMEN kaldırılır
    /// ("0 ₺" yazmak yanlış bilgi olurdu).
    /// </summary>
    [Fact]
    public void KL8_Korumaliyken_Rapor_Malzeme_Maliyeti_Kolonu_Kaldirilir()
    {
        var istek = new ReportRequest(Executed: true, FromDate: Gun - 86_400_000, ToDate: Gun + 86_400_000);

        var acik = _rapor.Run(_admin, "maintenance", istek);
        Assert.Contains("Malzeme Maliyeti", acik.Headers);          // önce GERÇEKTEN var

        FiyatiKoru();
        var s = Oturum("kl2_personel");
        var kapali = _rapor.Run(s, "maintenance", istek);

        Assert.DoesNotContain("Malzeme Maliyeti", kapali.Headers);
        Assert.Equal(acik.Headers.Count - 1, kapali.Headers.Count);  // yalnız O kolon gitti
        // Kolon sayısı ile satır genişliği tutarlı kalmalı (tablo kaymamalı).
        foreach (var satir in kapali.Rows) Assert.Equal(kapali.Headers.Count, satir.Count);
    }
}
