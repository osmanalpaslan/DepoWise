using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G4-2 — FATURA (kullanıcı isteği 2026-08-12).
///
/// <b>Bu dosyanın koruduğu değişmezler:</b>
/// <list type="number">
///   <item><b>Paralel defter yok:</b> fatura stok/cari tablolarına doğrudan yazmaz; etkiler
///     <c>StockService</c> ve <c>PartyLedgerService</c> üzerinden oluşur ve fatura yalnız
///     üretilen belgelerin KİMLİĞİNİ referans olarak tutar.</item>
///   <item><b>Tek transaction:</b> herhangi bir aşama hata verirse fatura=0, cari=0, stok=0 kalır
///     (kısmi kayıt yok) — I02.</item>
///   <item><b>Idempotency:</b> aynı <c>operation_id</c> iki kez → fatura=1, cari=1, stok=1 — I01.</item>
///   <item><b>Silme yok:</b> iptal ters kayıtla yürür, çift iptal engellenir, tutar değiştirilemez.</item>
/// </list>
/// </summary>
public class InvoiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly StockService _stock;
    private readonly PartyService _parties;
    private readonly PartyLedgerService _ledger;
    private readonly InvoiceService _invoices;
    private readonly SessionContext _admin;
    private readonly string _depo;
    private const string CoA = "A";

    public InvoiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_g42_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _parties = new PartyService(_factory, _clock);
        _ledger = new PartyLedgerService(_factory, _clock);
        _invoices = new InvoiceService(_factory, _stock, _ledger, _clock);

        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin(CoA, "admin", "Test!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _depo = new BranchService(_factory, _clock).Create(_admin, new NewBranch("ANA DEPO"));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private string Mat(string code) => _materials.Create(_admin, new NewMaterial(code, code));
    private string Cari(string code = "C-001") => _parties.Create(_admin, new NewParty(code, "Test Cari", PartyTypes.Both));
    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    /// <summary>Verilen tabloda firma içindeki satır sayısı — "kısmi kayıt kalmadı" ispatı için.</summary>
    private long Count(string table, string where = "1=1")
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE company_id='{CoA}' AND {where};";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private decimal Balance(string partyId) => _ledger.Balance(_admin, partyId).Balance;

    /// <summary>Malzemenin TOPLAM stogu — dogrudan defterden okunur (servis onbellegine guvenilmez).</summary>
    private decimal StockOf(string materialId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(CAST(quantity AS REAL)),0) FROM stock_balances WHERE company_id=@c AND material_id=@m;";
        cmd.AddWithValue("@c", CoA); cmd.AddWithValue("@m", materialId);
        return Convert.ToDecimal(cmd.ExecuteScalar());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // A — TOPLAM HESABI (saf fonksiyon; veritabanı yok)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>A1 — İskonto matrahtan düşer, KDV İSKONTOLU tutar üzerinden hesaplanır.</summary>
    [Fact]
    public void A1_Iskonto_Sonrasi_KDV()
    {
        var a = InvoiceService.LineAmounts(new NewInvoiceLine("m", null, "adet", 10m, 100m, DiscountRate: 10m, VatRate: 20m));
        Assert.Equal(1000m, a.Gross);
        Assert.Equal(100m, a.Discount);
        Assert.Equal(900m, a.Net);
        Assert.Equal(180m, a.Vat);      // 900 × %20 — 1000 × %20 DEĞİL
        Assert.Equal(1080m, a.Total);
    }

    /// <summary>A2 — Tevkifat KDV ÜZERİNDEN alınır (Türkiye KDV tevkifatı).</summary>
    [Fact]
    public void A2_Tevkifat_KDV_Uzerinden()
    {
        var a = InvoiceService.LineAmounts(new NewInvoiceLine("m", null, null, 1m, 1000m, VatRate: 20m, WithholdingRate: 50m));
        Assert.Equal(200m, a.Vat);
        Assert.Equal(100m, a.Withholding);   // 200'ün yarısı
        Assert.Equal(1100m, a.Total);        // 1000 + 200 − 100
    }

    /// <summary>A3 — Oranlar KODDA SABİT DEĞİLDİR: %1, %10, %20 aynı fonksiyondan geçer.</summary>
    [Theory]
    [InlineData(1, 1010)]
    [InlineData(10, 1100)]
    [InlineData(20, 1200)]
    public void A3_KDV_Orani_Yapilandirilabilir(int rate, int expected)
    {
        var t = InvoiceService.Totals(new[] { new NewInvoiceLine("m", null, null, 1m, 1000m, VatRate: rate) });
        Assert.Equal(expected, t.GrandTotal);
    }

    /// <summary>A4 — Kuruş yuvarlaması: her satır 2 basamağa yuvarlanır, toplam satırların toplamıdır.</summary>
    [Fact]
    public void A4_Kurus_Yuvarlamasi()
    {
        var t = InvoiceService.Totals(new[]
        {
            new NewInvoiceLine("m1", null, null, 3m, 33.333m, VatRate: 20m),
            new NewInvoiceLine("m2", null, null, 1m, 0.005m, VatRate: 20m),
        });
        // 3 × 33.333 = 99.999 → 100.00 ; 0.005 → 0.01
        Assert.Equal(100.01m, t.Subtotal);
        Assert.Equal(20.00m, t.VatTotal);
        Assert.Equal(120.01m, t.GrandTotal);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // B — ALIŞ / SATIŞ AKIŞI
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>B1 — ALIŞ: stok GİRER, cariye BORÇLANIRIZ (bakiye negatif = biz borçluyuz).</summary>
    [Fact]
    public void B1_Alis_Faturasi_Stok_Girer_Cari_Alacaklanir()
    {
        var m = Mat("M-1"); var c = Cari();
        var r = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Purchase, c,
            new[] { new NewInvoiceLine(m, null, "adet", 10m, 100m, VatRate: 20m) }, Op(), BranchId: _depo));

        Assert.False(r.AlreadyExisted);
        Assert.NotNull(r.StockDocumentId);
        Assert.NotNull(r.LedgerEntryId);
        Assert.Equal(10m, StockOf(m));
        Assert.Equal(-1200m, Balance(c));   // 1000 + %20 KDV; alacak → negatif
    }

    /// <summary>B2 — SATIŞ: stok ÇIKAR, cari BİZE borçlanır (bakiye pozitif).</summary>
    [Fact]
    public void B2_Satis_Faturasi_Stok_Cikar_Cari_Borclanir()
    {
        var m = Mat("M-1"); var c = Cari();
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 50m) }, Op(), _depo);

        _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Sales, c,
            new[] { new NewInvoiceLine(m, null, "adet", 20m, 150m, VatRate: 20m) }, Op(), BranchId: _depo));

        Assert.Equal(30m, StockOf(m));
        Assert.Equal(3600m, Balance(c));    // 3000 + %20
    }

    /// <summary>B3 — Fatura numarası seri kataloğundan üretilir ve İLERLER (aynı numara iki kez verilmez).</summary>
    [Fact]
    public void B3_Numara_Seriden_Uretilir_Ve_Ilerler()
    {
        var m = Mat("M-1"); var c = Cari();
        var r1 = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Purchase, c,
            new[] { new NewInvoiceLine(m, null, null, 1m, 100m) }, Op(), BranchId: _depo));
        var r2 = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Purchase, c,
            new[] { new NewInvoiceLine(m, null, null, 1m, 100m) }, Op(), BranchId: _depo));

        Assert.Equal("A00000001", r1.InvoiceNo);
        Assert.Equal("A00000002", r2.InvoiceNo);
        Assert.NotEqual(r1.InvoiceNo, r2.InvoiceNo);
    }

    /// <summary>B4 — Hizmet/masraf faturası: <c>AffectsStock=false</c> → stok belgesi ÜRETİLMEZ, cari yine borçlanır.</summary>
    [Fact]
    public void B4_Stok_Etkilemeyen_Fatura()
    {
        var c = Cari();
        var r = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Purchase, c,
            new[] { new NewInvoiceLine(null, "Nakliye hizmeti", null, 1m, 500m, VatRate: 20m) }, Op(),
            AffectsStock: false));

        Assert.Null(r.StockDocumentId);
        Assert.Equal(0, Count("stock_documents"));
        Assert.Equal(-600m, Balance(c));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // C — KRİTİK: IDEMPOTENCY VE ATOMİKLİK
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// I01 — ⭐ KRİTİK: aynı <c>operation_id</c> iki kez gönderilir → fatura=1, cari=1, stok=1.
    /// Ağ tekrarı/çift tıklama cariyi İKİ KEZ borçlandıramaz.
    /// </summary>
    [Fact]
    public void I01_Ayni_OperationId_Ikinci_Kez_Yazmaz()
    {
        var m = Mat("M-1"); var c = Cari();
        var op = Op();
        var dto = new NewInvoice(InvoiceDirections.Purchase, c,
            new[] { new NewInvoiceLine(m, null, null, 10m, 100m, VatRate: 20m) }, op, BranchId: _depo);

        var r1 = _invoices.Create(_admin, dto);
        var r2 = _invoices.Create(_admin, dto);

        Assert.False(r1.AlreadyExisted);
        Assert.True(r2.AlreadyExisted);
        Assert.Equal(r1.Id, r2.Id);
        Assert.Equal(1, Count("invoices"));
        Assert.Equal(1, Count("party_ledger"));
        Assert.Equal(1, Count("stock_documents"));
        Assert.Equal(10m, StockOf(m));       // 20 DEĞİL
        Assert.Equal(-1200m, Balance(c));    // −2400 DEĞİL
    }

    /// <summary>
    /// I02 — ⭐ KRİTİK: aşamanın ortasında hata (stok yetersiz) → fatura=0, cari=0, stok belgesi=0.
    /// Kısmi kayıt oluşmaz; cari borcu yazılıp stok yazılmadan kalmaz.
    /// </summary>
    [Fact]
    public void I02_Ortada_Hata_Hicbir_Kayit_Birakmaz()
    {
        var m = Mat("M-1"); var c = Cari();
        // Stokta hiç yok; satış faturası çıkış yapamayacak → negatif stok engeli devreye girer.
        Assert.ThrowsAny<Exception>(() => _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Sales, c,
            new[] { new NewInvoiceLine(m, null, null, 5m, 100m, VatRate: 20m) }, Op(), BranchId: _depo)));

        Assert.Equal(0, Count("invoices"));
        Assert.Equal(0, Count("invoice_lines"));
        Assert.Equal(0, Count("party_ledger"));
        Assert.Equal(0, Count("stock_documents"));
        Assert.Equal(0, Count("stock_movements"));
        Assert.Equal(0m, Balance(c));
        Assert.Equal(0m, StockOf(m));
    }

    /// <summary>I03 — Doğrulama hatasında da hiçbir kayıt kalmaz (numara serisi bile ilerlemez).</summary>
    [Fact]
    public void I03_Dogrulama_Hatasi_Seri_Ilerletmez()
    {
        var c = Cari();
        Assert.Throws<ArgumentException>(() => _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Purchase, c,
            new[] { new NewInvoiceLine("m", null, null, 0m, 100m) }, Op())));

        Assert.Equal(0, Count("invoices"));
        Assert.Equal(0, Count("invoice_series"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D — İPTAL (SİLME YOK)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>D1 — İptal: fatura SİLİNMEZ; ters stok ve ters cari kayıtları oluşur, etki sıfırlanır.</summary>
    [Fact]
    public void D1_Iptal_Ters_Kayit_Uretir_Silmez()
    {
        var m = Mat("M-1"); var c = Cari();
        var r = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Purchase, c,
            new[] { new NewInvoiceLine(m, null, null, 10m, 100m, VatRate: 20m) }, Op(), BranchId: _depo));

        _invoices.Cancel(_admin, r.Id, "Yanlış cariye kesildi");

        Assert.Equal(1, Count("invoices"));                 // kayıt DURUYOR
        Assert.Equal(1, Count("invoices", "status='cancelled'"));
        Assert.Equal(2, Count("party_ledger"));             // asıl + ters
        Assert.Equal(2, Count("stock_documents"));          // asıl + ters
        Assert.Equal(0m, Balance(c));                       // etki sıfırlandı
        Assert.Equal(0m, StockOf(m));
    }

    /// <summary>D2 — ÇİFT İPTAL ENGELLENİR: ikinci iptal hata verir, ikinci ters kayıt oluşmaz.</summary>
    [Fact]
    public void D2_Cift_Iptal_Engellenir()
    {
        var m = Mat("M-1"); var c = Cari();
        var r = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Purchase, c,
            new[] { new NewInvoiceLine(m, null, null, 10m, 100m) }, Op(), BranchId: _depo));

        _invoices.Cancel(_admin, r.Id, "gerekçe");
        Assert.Throws<ArgumentException>(() => _invoices.Cancel(_admin, r.Id, "tekrar"));

        Assert.Equal(2, Count("party_ledger"));   // 3 DEĞİL
        Assert.Equal(0m, Balance(c));
    }

    /// <summary>D3 — İptal gerekçesi ZORUNLU (boş gerekçeyle izlenebilirlik kaybolur).</summary>
    [Fact]
    public void D3_Iptal_Gerekce_Zorunlu()
    {
        var m = Mat("M-1"); var c = Cari();
        var r = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Purchase, c,
            new[] { new NewInvoiceLine(m, null, null, 1m, 100m) }, Op(), BranchId: _depo));
        Assert.Throws<ArgumentException>(() => _invoices.Cancel(_admin, r.Id, "   "));
    }

    /// <summary>D4 — İptal edilmiş fatura DÜZENLENEMEZ.</summary>
    [Fact]
    public void D4_Iptal_Edilmis_Fatura_Duzenlenemez()
    {
        var m = Mat("M-1"); var c = Cari();
        var r = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Purchase, c,
            new[] { new NewInvoiceLine(m, null, null, 1m, 100m) }, Op(), BranchId: _depo));
        _invoices.Cancel(_admin, r.Id, "gerekçe");
        Assert.Throws<ArgumentException>(() => _invoices.UpdateInfo(_admin, r.Id, "X-1", null, "not"));
    }

    /// <summary>
    /// D5 — Alış faturası iptali stok ÇIKIŞI demektir; mal tüketilmişse iptal BAŞARISIZ olur
    /// (negatif stok engeli) ve fatura yürürlükte kalır — yarım iptal yoktur.
    /// </summary>
    [Fact]
    public void D5_Tuketilmis_Mal_Iptali_Reddedilir()
    {
        var m = Mat("M-1"); var c = Cari();
        var r = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Purchase, c,
            new[] { new NewInvoiceLine(m, null, null, 10m, 100m) }, Op(), BranchId: _depo));
        _stock.IssueOut(_admin, new[] { new StockLine(m, 10m) }, Op(), _depo);

        Assert.ThrowsAny<Exception>(() => _invoices.Cancel(_admin, r.Id, "gerekçe"));

        Assert.Equal(1, Count("invoices", "status='active'"));   // iptal olmadı
        Assert.Equal(1, Count("party_ledger"));                  // ters cari kaydı YAZILMADI
    }

    // ═════════════════════════════════════════════════════════════════════════
    // E — DÜZENLEME POLİTİKASI
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>E1 — Bilgi alanları düzenlenebilir; tutar ve satır DEĞİŞTİRİLEMEZ (API'de böyle bir yol yoktur).</summary>
    [Fact]
    public void E1_Bilgi_Alanlari_Duzenlenir_Tutar_Degismez()
    {
        var m = Mat("M-1"); var c = Cari();
        var r = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Purchase, c,
            new[] { new NewInvoiceLine(m, null, null, 10m, 100m, VatRate: 20m) }, Op(), BranchId: _depo));

        _invoices.UpdateInfo(_admin, r.Id, "SATICI-2026-77", 1_700_500_000_000, "vade uzatıldı");

        Assert.Equal(1, Count("invoices", "external_no='SATICI-2026-77'"));
        Assert.Equal(1, Count("invoices", "grand_total='1200'"));   // tutar aynı
        Assert.Equal(-1200m, Balance(c));                           // cari etkisi aynı
    }

    // ═════════════════════════════════════════════════════════════════════════
    // F — YETKİ VE FİRMA SINIRI
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>F1 — Yetkisiz kullanıcı fatura KESEMEZ (deny-by-default; servis katmanında).</summary>
    [Fact]
    public void F1_Yetkisiz_Fatura_Kesemez()
    {
        var m = Mat("M-1"); var c = Cari();
        var staff = new SessionContext("st", CoA, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _invoices.Create(staff, new NewInvoice(
            InvoiceDirections.Purchase, c, new[] { new NewInvoiceLine(m, null, null, 1m, 100m) }, Op(), BranchId: _depo)));
        Assert.Equal(0, Count("invoices"));
    }

    /// <summary>F2 — Yalnız görüntüleme yetkisi olan fatura KESEMEZ.</summary>
    [Fact]
    public void F2_Sadece_Goruntuleme_Kesemez()
    {
        var m = Mat("M-1"); var c = Cari();
        var staff = new SessionContext("st", CoA, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission(InvoiceService.Module, true, false, false, false) }));
        Assert.Throws<ForbiddenException>(() => _invoices.Create(staff, new NewInvoice(
            InvoiceDirections.Purchase, c, new[] { new NewInvoiceLine(m, null, null, 1m, 100m) }, Op(), BranchId: _depo)));
    }

    /// <summary>F3 — BAŞKA FİRMANIN carisine fatura kesilemez (tenant sızıntısı).</summary>
    [Fact]
    public void F3_Baska_Firmanin_Carisine_Kesilemez()
    {
        var m = Mat("M-1");
        var other = new SessionContext("adB", "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var foreignParty = _parties.Create(other, new NewParty("C-B", "B Firması Carisi", PartyTypes.Both));

        Assert.Throws<ForbiddenException>(() => _invoices.Create(_admin, new NewInvoice(
            InvoiceDirections.Purchase, foreignParty, new[] { new NewInvoiceLine(m, null, null, 1m, 100m) }, Op(), BranchId: _depo)));
        Assert.Equal(0, Count("invoices"));
    }

    /// <summary>F4 — Başka firmanın faturası iptal edilemez.</summary>
    [Fact]
    public void F4_Baska_Firmanin_Faturasi_Iptal_Edilemez()
    {
        var m = Mat("M-1"); var c = Cari();
        var r = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Purchase, c,
            new[] { new NewInvoiceLine(m, null, null, 1m, 100m) }, Op(), BranchId: _depo));
        var other = new SessionContext("adB", "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _invoices.Cancel(other, r.Id, "gerekçe"));
        Assert.Equal(1, Count("invoices", "status='active'"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // G — DOĞRULAMA
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>G1 — Satırsız fatura kesilemez.</summary>
    [Fact]
    public void G1_Satirsiz_Fatura_Reddedilir()
        => Assert.Throws<ArgumentException>(() => _invoices.Create(_admin,
            new NewInvoice(InvoiceDirections.Purchase, Cari(), Array.Empty<NewInvoiceLine>(), Op())));

    /// <summary>G2 — Negatif/sıfır miktar reddedilir.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void G2_Gecersiz_Miktar_Reddedilir(int qty)
        => Assert.Throws<ArgumentException>(() => _invoices.Create(_admin, new NewInvoice(
            InvoiceDirections.Purchase, Cari(), new[] { new NewInvoiceLine("m", null, null, qty, 100m) }, Op())));

    /// <summary>G3 — %100'ü aşan oran reddedilir (veri hatası sessizce toplamı bozmasın).</summary>
    [Fact]
    public void G3_Asiri_Oran_Reddedilir()
        => Assert.Throws<ArgumentException>(() => _invoices.Create(_admin, new NewInvoice(
            InvoiceDirections.Purchase, Cari(), new[] { new NewInvoiceLine("m", null, null, 1m, 100m, VatRate: 120m) }, Op())));

    /// <summary>G4 — Vade, fatura tarihinden ÖNCE olamaz.</summary>
    [Fact]
    public void G4_Vade_Fatura_Tarihinden_Once_Olamaz()
        => Assert.Throws<ArgumentException>(() => _invoices.Create(_admin, new NewInvoice(
            InvoiceDirections.Purchase, Cari(), new[] { new NewInvoiceLine("m", null, null, 1m, 100m) }, Op(),
            InvoiceDate: 2_000_000_000_000, DueDate: 1_000_000_000_000)));

    /// <summary>G5 — Malzemesiz VE açıklamasız satır reddedilir (ne alındığı belirsiz kalmasın).</summary>
    [Fact]
    public void G5_Bos_Satir_Reddedilir()
        => Assert.Throws<ArgumentException>(() => _invoices.Create(_admin, new NewInvoice(
            InvoiceDirections.Purchase, Cari(), new[] { new NewInvoiceLine(null, null, null, 1m, 100m) }, Op())));

    /// <summary>G6 — operation_id ZORUNLU: idempotency anahtarsız fatura kesilemez.</summary>
    [Fact]
    public void G6_OperationId_Zorunlu()
        => Assert.Throws<ArgumentException>(() => _invoices.Create(_admin, new NewInvoice(
            InvoiceDirections.Purchase, Cari(), new[] { new NewInvoiceLine("m", null, null, 1m, 100m) }, "")));

    /// <summary>G7 — Toplamı sıfır olan fatura kesilemez (0 TL'lik cari hareketi anlamsızdır).</summary>
    [Fact]
    public void G7_Sifir_Tutarli_Fatura_Reddedilir()
        => Assert.Throws<ArgumentException>(() => _invoices.Create(_admin, new NewInvoice(
            InvoiceDirections.Purchase, Cari(), new[] { new NewInvoiceLine("m", "bedelsiz", null, 1m, 0m) }, Op())));

    // ═════════════════════════════════════════════════════════════════════════
    // H — DEFTER SINIRI
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// H1 — Faturanın cari hareketi KAYNAK BELGEYE bağlıdır (<c>source_type=invoice</c>) ve
    /// elle girilebilir türlerden DEĞİLDİR — kullanıcı aynı borcu elle ikinci kez giremez.
    /// </summary>
    [Fact]
    public void H1_Cari_Hareketi_Belge_Kaynakli()
    {
        var m = Mat("M-1"); var c = Cari();
        var r = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Purchase, c,
            new[] { new NewInvoiceLine(m, null, null, 1m, 100m) }, Op(), BranchId: _depo));

        Assert.Equal(1, Count("party_ledger", $"source_type='invoice' AND source_id='{r.Id}'"));
        Assert.DoesNotContain(PartyDocTypes.Invoice, PartyDocTypes.ManualEntry);
        Assert.Throws<ArgumentException>(() => _ledger.Add(_admin, new NewLedgerEntry(
            c, PartyDocTypes.Invoice, 100m, false, null, null, null, null, "TRY", null, null, null, Op())));
    }

    /// <summary>
    /// H2 — Fatura stok belgesini KENDİ yazmaz: oluşan hareket normal stok defterindedir ve
    /// fatura yalnız belgenin kimliğini referanslar (kopya stok gerçekliği yok).
    /// </summary>
    [Fact]
    public void H2_Stok_Hareketi_Normal_Defterde()
    {
        var m = Mat("M-1"); var c = Cari();
        var r = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Purchase, c,
            new[] { new NewInvoiceLine(m, null, null, 7m, 100m) }, Op(), BranchId: _depo));

        Assert.Equal(1, Count("stock_documents", $"id='{r.StockDocumentId}'"));
        Assert.Equal(1, Count("stock_movements", $"document_id='{r.StockDocumentId}'"));
        Assert.Equal(7m, StockOf(m));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
