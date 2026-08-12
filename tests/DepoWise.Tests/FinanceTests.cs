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
/// G4-3 — KASA / BANKA (kullanıcı isteği 2026-08-12).
///
/// <b>Bu dosyanın koruduğu değişmezler:</b>
/// <list type="number">
///   <item><b>Paralel defter yok:</b> tahsilat/ödeme cari defterine <c>PartyLedgerService</c>
///     üzerinden yazar; stok defterine HİÇ dokunmaz.</item>
///   <item><b>Bakiye saklanmaz:</b> hesap bakiyesi de faturanın kalanı da hesaplanır.</item>
///   <item><b>Tek transaction:</b> herhangi bir aşama hata verirse kasa=0, cari=0, fatura kalanı
///     değişmemiş kalır (K04).</item>
///   <item><b>Idempotency:</b> aynı <c>operation_id</c> iki kez → kasa 1, cari 1, kapama 1 (K03).</item>
///   <item><b>Para yoktan var olmaz:</b> iç transfer net 0 (T01).</item>
/// </list>
/// </summary>
public class FinanceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly StockService _stock;
    private readonly PartyService _parties;
    private readonly PartyLedgerService _ledger;
    private readonly InvoiceService _invoices;
    private readonly FinanceService _finance;
    private readonly FinanceQueryService _fq;
    private readonly SessionContext _admin;
    private readonly string _depo;
    private const string CoA = "A";

    public FinanceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_g43_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _parties = new PartyService(_factory, _clock);
        _ledger = new PartyLedgerService(_factory, _clock);
        _invoices = new InvoiceService(_factory, _stock, _ledger, _clock);
        _finance = new FinanceService(_factory, _ledger, _clock);
        _fq = new FinanceQueryService(_factory);

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

    private string Kasa(string code = "KASA-1")
        => _finance.CreateAccount(_admin, new NewFinanceAccount(code, "Merkez Kasa", FinanceAccountKinds.Cash));

    private string Banka(string code = "BANKA-1")
        => _finance.CreateAccount(_admin, new NewFinanceAccount(code, "Ziraat Vadesiz", FinanceAccountKinds.Bank,
            BankName: "Ziraat Bankası", Iban: "TR330006100519786457841326"));

    private long Count(string table, string where = "1=1")
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE company_id='{CoA}' AND {where};";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private decimal Bakiye(string accountId) => _fq.Balance(_admin, accountId);
    private decimal Cari_Bakiye(string partyId) => _ledger.Balance(_admin, partyId).Balance;

    /// <summary>Faturanın kalanı — servis hesabıyla aynı yol (saklanan bir alan yok).</summary>
    private decimal Kalan(string invoiceId, decimal grandTotal)
        => grandTotal - _fq.PaidOf(_admin, invoiceId);

    /// <summary>10.000 TL'lik SATIŞ faturası kesip id + toplamını döndürür.</summary>
    private (string Id, decimal Total) SatisFaturasi(string partyId, decimal net = 10000m)
    {
        var m = Mat("M-" + Guid.NewGuid().ToString("N")[..6]);
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 1000m) }, Op(), _depo);
        var r = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Sales, partyId,
            new[] { new NewInvoiceLine(m, null, null, 1m, net) }, Op(), BranchId: _depo));
        return (r.Id, net);
    }

    /// <summary>10.000 TL'lik ALIŞ faturası.</summary>
    private (string Id, decimal Total) AlisFaturasi(string partyId, decimal net = 10000m)
    {
        var m = Mat("M-" + Guid.NewGuid().ToString("N")[..6]);
        var r = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Purchase, partyId,
            new[] { new NewInvoiceLine(m, null, null, 1m, net) }, Op(), BranchId: _depo));
        return (r.Id, net);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // A — HESAP TANIMI (CRUD)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>A1 — Kasa ve banka AYNI tabloda, tür ile ayrılır; alanlar korunur.</summary>
    [Fact]
    public void A1_Kasa_Ve_Banka_Ayni_Tabloda()
    {
        var k = Kasa(); var b = Banka();
        var ka = _fq.Account(_admin, k);
        var ba = _fq.Account(_admin, b);

        Assert.Equal(FinanceAccountKinds.Cash, ka.AccountKind);
        Assert.Equal("Kasa", ka.KindText);
        Assert.False(ka.IsBank);
        Assert.Equal(FinanceAccountKinds.Bank, ba.AccountKind);
        Assert.Equal("Banka", ba.KindText);
        Assert.True(ba.IsBank);
        Assert.Equal("TR330006100519786457841326", ba.Iban);
        Assert.Equal(2, Count("finance_accounts"));
    }

    /// <summary>A2 — Hesap kodu firma içinde benzersizdir.</summary>
    [Fact]
    public void A2_Tekrar_Eden_Kod_Reddedilir()
    {
        Kasa("K-1");
        Assert.Throws<ArgumentException>(() => Kasa("K-1"));
    }

    /// <summary>A3 — IBAN zorunlu değildir; yazıldıysa TR formatı doğrulanır.</summary>
    [Fact]
    public void A3_Gecersiz_Iban_Reddedilir()
    {
        Assert.Throws<ArgumentException>(() => _finance.CreateAccount(_admin,
            new NewFinanceAccount("B-X", "Hatalı", FinanceAccountKinds.Bank, Iban: "TR12")));
        // IBAN'sız banka hesabı KABUL EDİLİR (her hesabın IBAN'ı olmayabilir).
        var ok = _finance.CreateAccount(_admin, new NewFinanceAccount("B-Y", "IBAN'sız", FinanceAccountKinds.Bank));
        Assert.NotNull(_fq.Account(_admin, ok));
    }

    /// <summary>A4 — HAREKETİ OLAN hesap silinemez; pasif yapılabilir (geçmiş sahipsiz kalmasın).</summary>
    [Fact]
    public void A4_Hareketi_Olan_Hesap_Silinemez()
    {
        var k = Kasa(); var c = Cari();
        _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 100m, Op(), PartyId: c));

        Assert.Throws<InvalidOperationException>(() => _finance.DeleteAccount(_admin, k));
        _finance.SetAccountActive(_admin, k, false);
        Assert.False(_fq.Account(_admin, k).IsActive);
        Assert.Equal(1, Count("finance_accounts", "is_deleted=0"));
    }

    /// <summary>A5 — Pasif hesapla yeni işlem yapılamaz.</summary>
    [Fact]
    public void A5_Pasif_Hesapla_Islem_Yapilamaz()
    {
        var k = Kasa(); var c = Cari();
        _finance.SetAccountActive(_admin, k, false);
        Assert.Throws<InvalidOperationException>(() => _finance.Add(_admin,
            new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 100m, Op(), PartyId: c)));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // B — YÖN VE BAKİYE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// B1 — ⭐ TAHSİLAT YÖNÜ: kasa ARTAR, müşterinin bize borcu AZALIR.
    /// (Cari bakiyesi = Borç − Alacak; pozitif = cari bize borçlu.)
    /// </summary>
    [Fact]
    public void B1_Tahsilat_Kasa_Artar_Cari_Borcu_Azalir()
    {
        var k = Kasa(); var c = Cari();
        var f = SatisFaturasi(c);                      // cari +10.000 (bize borçlu)
        Assert.Equal(10000m, Cari_Bakiye(c));

        _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 4000m, Op(), PartyId: c));

        Assert.Equal(4000m, Bakiye(k));                // kasa arttı
        Assert.Equal(6000m, Cari_Bakiye(c));           // borcu azaldı
    }

    /// <summary>B2 — ⭐ ÖDEME YÖNÜ: kasa AZALIR, bizim borcumuz AZALIR (bakiye sıfıra yaklaşır).</summary>
    [Fact]
    public void B2_Odeme_Kasa_Azalir_Borcumuz_Azalir()
    {
        var k = Kasa(); var c = Cari();
        AlisFaturasi(c);                                // cari −10.000 (biz borçluyuz)
        Assert.Equal(-10000m, Cari_Bakiye(c));

        _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Payment, 4000m, Op(), PartyId: c));

        Assert.Equal(-4000m, Bakiye(k));                // kasa azaldı (açık görünür kalır)
        Assert.Equal(-6000m, Cari_Bakiye(c));           // borcumuz azaldı
    }

    /// <summary>B3 — Açılış bakiyesi cariye DOKUNMAZ.</summary>
    [Fact]
    public void B3_Acilis_Cariyi_Etkilemez()
    {
        var k = Kasa();
        _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Opening, 25000m, Op()));
        Assert.Equal(25000m, Bakiye(k));
        Assert.Equal(0, Count("party_ledger"));
    }

    /// <summary>B4 — Açılış/düzeltmeye cari bağlanamaz (yanlış kullanım engellenir).</summary>
    [Fact]
    public void B4_Acilisa_Cari_Baglanamaz()
    {
        var k = Kasa(); var c = Cari();
        Assert.Throws<ArgumentException>(() => _finance.Add(_admin,
            new NewFinanceEntry(k, FinanceTxnTypes.Opening, 100m, Op(), PartyId: c)));
    }

    /// <summary>B5 — Tahsilat/ödemede cari ZORUNLUDUR.</summary>
    [Fact]
    public void B5_Tahsilatta_Cari_Zorunlu()
    {
        var k = Kasa();
        Assert.Throws<ArgumentException>(() => _finance.Add(_admin,
            new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 100m, Op())));
    }

    /// <summary>B6 — Bakiye SAKLANMAZ: hareketlerden hesaplanır, ekstre yürüyen bakiye verir.</summary>
    [Fact]
    public void B6_Bakiye_Defterden_Hesaplanir()
    {
        var k = Kasa(); var c = Cari();
        _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Opening, 1000m, Op()));
        _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 500m, Op(), PartyId: c));
        _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Payment, 200m, Op(), PartyId: c));

        Assert.Equal(1300m, Bakiye(k));
        var st = _fq.Statement(_admin, k);
        Assert.Equal(3, st.Count);
        Assert.Equal(1300m, st[^1].RunningBalance);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // C — FATURA KAPAMA (kullanıcının istediği TEST 1 / TEST 2)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// K01 — ⭐ TEST 1: 10.000 TL fatura → 4.000 TL tahsilat → kalan 6.000 TL.
    /// Kalan SAKLANMAZ; tahsislerden hesaplanır.
    /// </summary>
    [Fact]
    public void K01_Kismi_Tahsilat_Kalani_Azaltir()
    {
        var k = Kasa(); var c = Cari();
        var f = SatisFaturasi(c);

        _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 4000m, Op(), PartyId: c,
            Allocations: new[] { new InvoiceAllocationInput(f.Id, 4000m) }));

        Assert.Equal(6000m, Kalan(f.Id, f.Total));
        Assert.Equal(4000m, Bakiye(k));
        Assert.Equal(6000m, Cari_Bakiye(c));
        Assert.Single(_fq.OpenInvoices(_admin, c));      // hâlâ açık
    }

    /// <summary>K02 — ⭐ TEST 2: 6.000 TL daha tahsilat → kalan 0; fatura açık listesinden ÇIKAR.</summary>
    [Fact]
    public void K02_Tam_Tahsilat_Kalani_Sifirlar()
    {
        var k = Kasa(); var c = Cari();
        var f = SatisFaturasi(c);

        _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 4000m, Op(), PartyId: c,
            Allocations: new[] { new InvoiceAllocationInput(f.Id, 4000m) }));
        _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 6000m, Op(), PartyId: c,
            Allocations: new[] { new InvoiceAllocationInput(f.Id, 6000m) }));

        Assert.Equal(0m, Kalan(f.Id, f.Total));
        Assert.Equal(10000m, Bakiye(k));
        Assert.Equal(0m, Cari_Bakiye(c));
        Assert.Empty(_fq.OpenInvoices(_admin, c));       // kapandı
    }

    /// <summary>K03 — FAZLA KAPAMA ENGELLENİR: kalandan büyük tahsis reddedilir.</summary>
    [Fact]
    public void K03_Fazla_Kapama_Engellenir()
    {
        var k = Kasa(); var c = Cari();
        var f = SatisFaturasi(c);
        _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 4000m, Op(), PartyId: c,
            Allocations: new[] { new InvoiceAllocationInput(f.Id, 4000m) }));

        Assert.Throws<InvalidOperationException>(() => _finance.Add(_admin,
            new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 7000m, Op(), PartyId: c,
                Allocations: new[] { new InvoiceAllocationInput(f.Id, 7000m) })));

        Assert.Equal(6000m, Kalan(f.Id, f.Total));       // değişmedi
        Assert.Equal(4000m, Bakiye(k));                  // kasa da değişmedi
    }

    /// <summary>K04 — BAĞIMSIZ cari tahsilatı: fatura kapatmaz, yalnız cari bakiyesini etkiler.</summary>
    [Fact]
    public void K04_Bagimsiz_Cari_Tahsilati()
    {
        var k = Kasa(); var c = Cari();
        var f = SatisFaturasi(c);

        _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 3000m, Op(), PartyId: c));

        Assert.Equal(10000m, Kalan(f.Id, f.Total));      // fatura kapanmadı
        Assert.Equal(7000m, Cari_Bakiye(c));             // cari bakiyesi etkilendi
        Assert.Equal(0, Count("invoice_allocations"));
    }

    /// <summary>K05 — TERS EŞLEŞME ENGELLENİR: satış faturası ödeme ile kapatılamaz.</summary>
    [Fact]
    public void K05_Ters_Eslesme_Engellenir()
    {
        var k = Kasa(); var c = Cari();
        var f = SatisFaturasi(c);
        Assert.Throws<ArgumentException>(() => _finance.Add(_admin,
            new NewFinanceEntry(k, FinanceTxnTypes.Payment, 1000m, Op(), PartyId: c,
                Allocations: new[] { new InvoiceAllocationInput(f.Id, 1000m) })));
    }

    /// <summary>K06 — BAŞKA CARİNİN faturası kapatılamaz.</summary>
    [Fact]
    public void K06_Baska_Carinin_Faturasi_Kapatilamaz()
    {
        var k = Kasa(); var c1 = Cari("C-1"); var c2 = Cari("C-2");
        var f = SatisFaturasi(c1);
        Assert.Throws<ArgumentException>(() => _finance.Add(_admin,
            new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 1000m, Op(), PartyId: c2,
                Allocations: new[] { new InvoiceAllocationInput(f.Id, 1000m) })));
    }

    /// <summary>K07 — İPTAL EDİLMİŞ fatura kapatılamaz.</summary>
    [Fact]
    public void K07_Iptal_Edilmis_Fatura_Kapatilamaz()
    {
        var k = Kasa(); var c = Cari();
        var f = SatisFaturasi(c);
        _invoices.Cancel(_admin, f.Id, "yanlış kesildi");
        Assert.Throws<InvalidOperationException>(() => _finance.Add(_admin,
            new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 1000m, Op(), PartyId: c,
                Allocations: new[] { new InvoiceAllocationInput(f.Id, 1000m) })));
    }

    /// <summary>K08 — Dağıtılan tutar işlem tutarını AŞAMAZ.</summary>
    [Fact]
    public void K08_Dagitim_Islem_Tutarini_Asamaz()
    {
        var k = Kasa(); var c = Cari();
        var f = SatisFaturasi(c);
        Assert.Throws<ArgumentException>(() => _finance.Add(_admin,
            new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 1000m, Op(), PartyId: c,
                Allocations: new[] { new InvoiceAllocationInput(f.Id, 5000m) })));
    }

    /// <summary>K09 — ALIŞ faturası ÖDEME ile kapanır (tedarikçiye ödeme).</summary>
    [Fact]
    public void K09_Alis_Faturasi_Odeme_Ile_Kapanir()
    {
        var k = Kasa(); var c = Cari();
        var f = AlisFaturasi(c);

        _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Payment, 10000m, Op(), PartyId: c,
            Allocations: new[] { new InvoiceAllocationInput(f.Id, 10000m) }));

        Assert.Equal(0m, Kalan(f.Id, f.Total));
        Assert.Equal(-10000m, Bakiye(k));
        Assert.Equal(0m, Cari_Bakiye(c));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D — KRİTİK: IDEMPOTENCY VE ATOMİKLİK
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// I01 — ⭐ TEST 3: aynı <c>operation_id</c> iki kez → hiçbir finansal değer ikinci kez değişmez.
    /// Kasa 1 hareket, cari 1 hareket, fatura 1 kapama.
    /// </summary>
    [Fact]
    public void I01_Ayni_OperationId_Ikinci_Kez_Yazmaz()
    {
        var k = Kasa(); var c = Cari();
        var f = SatisFaturasi(c);
        var op = Op();
        var dto = new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 4000m, op, PartyId: c,
            Allocations: new[] { new InvoiceAllocationInput(f.Id, 4000m) });

        var r1 = _finance.Add(_admin, dto);
        var r2 = _finance.Add(_admin, dto);

        Assert.False(r1.AlreadyExisted);
        Assert.True(r2.AlreadyExisted);
        Assert.Equal(r1.TransactionId, r2.TransactionId);
        Assert.Equal(1, Count("finance_transactions"));
        Assert.Equal(1, Count("invoice_allocations"));
        Assert.Equal(4000m, Bakiye(k));                  // 8000 DEĞİL
        Assert.Equal(6000m, Cari_Bakiye(c));             // 2000 DEĞİL
        Assert.Equal(6000m, Kalan(f.Id, f.Total));       // 2000 DEĞİL
    }

    /// <summary>
    /// I02 — ⭐ TEST 4: tahsilatın ortasında hata (fazla kapama) → kasa değişmedi, cari değişmedi,
    /// fatura değişmedi. Kısmi finansal kayıt oluşmaz.
    /// </summary>
    [Fact]
    public void I02_Ortada_Hata_Hicbir_Kayit_Birakmaz()
    {
        var k = Kasa(); var c = Cari();
        var f = SatisFaturasi(c);
        var cariOnce = Cari_Bakiye(c);

        Assert.ThrowsAny<Exception>(() => _finance.Add(_admin,
            new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 50000m, Op(), PartyId: c,
                Allocations: new[] { new InvoiceAllocationInput(f.Id, 50000m) })));

        Assert.Equal(0, Count("finance_transactions"));
        Assert.Equal(0, Count("invoice_allocations"));
        Assert.Equal(0m, Bakiye(k));
        Assert.Equal(cariOnce, Cari_Bakiye(c));
        Assert.Equal(f.Total, Kalan(f.Id, f.Total));
        Assert.Equal(1, Count("party_ledger"));          // yalnız faturanın kendi hareketi
    }

    /// <summary>I03 — Çok satırlı tahsiste ikinci satır hata verirse BİRİNCİSİ de yazılmaz.</summary>
    [Fact]
    public void I03_Coklu_Tahsiste_Kismi_Yazim_Yok()
    {
        var k = Kasa(); var c = Cari();
        var f1 = SatisFaturasi(c, 5000m);
        var f2 = SatisFaturasi(c, 5000m);

        Assert.ThrowsAny<Exception>(() => _finance.Add(_admin,
            new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 10000m, Op(), PartyId: c,
                Allocations: new[]
                {
                    new InvoiceAllocationInput(f1.Id, 5000m),
                    new InvoiceAllocationInput(f2.Id, 9000m),   // kalanı aşıyor → tüm işlem düşer
                })));

        Assert.Equal(0, Count("invoice_allocations"));
        Assert.Equal(0, Count("finance_transactions"));
        Assert.Equal(5000m, Kalan(f1.Id, f1.Total));     // birinci de kapanmadı
    }

    // ═════════════════════════════════════════════════════════════════════════
    // E — TERS KAYIT (SİLME DEĞİL)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// E1 — Ters kayıt: hareket SİLİNMEZ, karşı kayıt yazılır; kasa, cari ve fatura kalanı
    /// eski hâline döner.
    /// </summary>
    [Fact]
    public void E1_Ters_Kayit_Etkiyi_Geri_Alir()
    {
        var k = Kasa(); var c = Cari();
        var f = SatisFaturasi(c);
        var r = _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 4000m, Op(), PartyId: c,
            Allocations: new[] { new InvoiceAllocationInput(f.Id, 4000m) }));

        _finance.Reverse(_admin, r.TransactionId, "yanlış hesaba işlendi");

        Assert.Equal(2, Count("finance_transactions"));           // asıl + ters (kayıt DURUYOR)
        Assert.Equal(0m, Bakiye(k));                              // etki sıfırlandı
        Assert.Equal(10000m, Cari_Bakiye(c));                     // cari eski hâline döndü
        Assert.Equal(10000m, Kalan(f.Id, f.Total));               // faturanın kalanı geri arttı
        Assert.Equal(1, Count("invoice_allocations", "is_reversed=1"));
    }

    /// <summary>E2 — ÇİFT TERS KAYIT ENGELLENİR.</summary>
    [Fact]
    public void E2_Cift_Ters_Kayit_Engellenir()
    {
        var k = Kasa(); var c = Cari();
        var r = _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 1000m, Op(), PartyId: c));

        _finance.Reverse(_admin, r.TransactionId, "gerekçe");
        Assert.Throws<InvalidOperationException>(() => _finance.Reverse(_admin, r.TransactionId, "tekrar"));

        Assert.Equal(2, Count("finance_transactions"));           // 3 DEĞİL
        Assert.Equal(0m, Bakiye(k));
    }

    /// <summary>E3 — Ters kayıt hareketinin kendisi ayrıca iptal EDİLEMEZ (sonsuz zincir olmasın).</summary>
    [Fact]
    public void E3_Ters_Kaydin_Kendisi_Iptal_Edilemez()
    {
        var k = Kasa(); var c = Cari();
        var r = _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 1000m, Op(), PartyId: c));
        var ters = _finance.Reverse(_admin, r.TransactionId, "gerekçe");
        Assert.Throws<InvalidOperationException>(() => _finance.Reverse(_admin, ters, "yine"));
    }

    /// <summary>E4 — Gerekçe ZORUNLU.</summary>
    [Fact]
    public void E4_Gerekce_Zorunlu()
    {
        var k = Kasa(); var c = Cari();
        var r = _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 1000m, Op(), PartyId: c));
        Assert.Throws<ArgumentException>(() => _finance.Reverse(_admin, r.TransactionId, "   "));
    }

    /// <summary>E5 — İptal edilen hareket ekstrede GÖRÜNÜR (iz kalır) ama bakiyeye girmez.</summary>
    [Fact]
    public void E5_Iptal_Edilen_Ekstrede_Gorunur_Bakiyeye_Girmez()
    {
        var k = Kasa(); var c = Cari();
        var r = _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 1000m, Op(), PartyId: c));
        _finance.Reverse(_admin, r.TransactionId, "gerekçe");

        var st = _fq.Statement(_admin, k);
        Assert.Equal(2, st.Count);                    // ikisi de görünüyor
        Assert.All(st, x => Assert.True(x.Txn.IsReversed));
        Assert.Equal(0m, st[^1].RunningBalance);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // F — İÇ TRANSFER
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// T01 — ⭐ TEST 6: iç transfer → kaynak −X, hedef +X, NET 0. Para yoktan var olmaz/kaybolmaz.
    /// Cari HİÇ etkilenmez.
    /// </summary>
    [Fact]
    public void T01_Ic_Transfer_Net_Sifir()
    {
        var k = Kasa(); var b = Banka();
        _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Opening, 30000m, Op()));

        _finance.Transfer(_admin, new NewFinanceTransfer(k, b, 10000m, Op()));

        Assert.Equal(20000m, Bakiye(k));
        Assert.Equal(10000m, Bakiye(b));
        Assert.Equal(30000m, Bakiye(k) + Bakiye(b));   // NET DEĞİŞMEDİ
        Assert.Equal(0, Count("party_ledger"));        // cari etkilenmedi
    }

    /// <summary>T02 — Transferin iki bacağı AYNI grupta ve karşı hesabı işaret eder.</summary>
    [Fact]
    public void T02_Iki_Bacak_Ayni_Grupta()
    {
        var k = Kasa(); var b = Banka();
        var t = _finance.Transfer(_admin, new NewFinanceTransfer(k, b, 500m, Op()));

        Assert.NotEqual(t.OutTransactionId, t.InTransactionId);
        Assert.Equal(2, Count("finance_transactions", $"transfer_group_id='{t.GroupId}'"));
        Assert.Equal(1, Count("finance_transactions", $"transfer_group_id='{t.GroupId}' AND direction=-1"));
        Assert.Equal(1, Count("finance_transactions", $"transfer_group_id='{t.GroupId}' AND direction=1"));
    }

    /// <summary>T03 — Transfer iptali İKİ BACAĞI BİRLİKTE geri alır (yarım transfer kalmaz).</summary>
    [Fact]
    public void T03_Transfer_Iptali_Iki_Bacagi_Geri_Alir()
    {
        var k = Kasa(); var b = Banka();
        _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Opening, 30000m, Op()));
        var t = _finance.Transfer(_admin, new NewFinanceTransfer(k, b, 10000m, Op()));

        _finance.Reverse(_admin, t.OutTransactionId, "yanlış hesap");

        Assert.Equal(30000m, Bakiye(k));               // geri geldi
        Assert.Equal(0m, Bakiye(b));                   // hedef sıfırlandı
        Assert.Equal(0, Count("finance_transactions", "transfer_group_id='" + t.GroupId + "' AND is_reversed=0"));
        Assert.Equal(4, Count("finance_transactions", "transfer_group_id='" + t.GroupId + "'"));   // 2 asıl + 2 ters
    }

    /// <summary>T04 — Aynı hesaba transfer ve aynı operation_id ile ikinci transfer engellenir.</summary>
    [Fact]
    public void T04_Transfer_Dogrulamalari()
    {
        var k = Kasa(); var b = Banka();
        Assert.Throws<ArgumentException>(() => _finance.Transfer(_admin, new NewFinanceTransfer(k, k, 100m, Op())));

        var op = Op();
        var t1 = _finance.Transfer(_admin, new NewFinanceTransfer(k, b, 100m, op));
        var t2 = _finance.Transfer(_admin, new NewFinanceTransfer(k, b, 100m, op));
        Assert.True(t2.AlreadyExisted);
        Assert.Equal(t1.GroupId, t2.GroupId);
        Assert.Equal(2, Count("finance_transactions"));   // 4 DEĞİL
    }

    /// <summary>T05 — Transfer bacakları ELLE yazılamaz (iki gerçeklik oluşmasın).</summary>
    [Fact]
    public void T05_Transfer_Bacaklari_Elle_Yazilamaz()
    {
        var k = Kasa();
        Assert.Throws<ArgumentException>(() => _finance.Add(_admin,
            new NewFinanceEntry(k, FinanceTxnTypes.TransferIn, 100m, Op())));
        Assert.Throws<ArgumentException>(() => _finance.Add(_admin,
            new NewFinanceEntry(k, FinanceTxnTypes.TransferOut, 100m, Op())));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // G — YETKİ VE İZOLASYON
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>G1 — ⭐ TEST 7: yetkisiz kullanıcı işlem YAPAMAZ (deny-by-default, servis katmanı).</summary>
    [Fact]
    public void G1_Yetkisiz_Islem_Yapamaz()
    {
        var k = Kasa(); var c = Cari();
        var staff = new SessionContext("st", CoA, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _finance.Add(staff,
            new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 100m, Op(), PartyId: c)));
        Assert.Throws<ForbiddenException>(() => _fq.Accounts(staff));
        Assert.Equal(0, Count("finance_transactions"));
    }

    /// <summary>G2 — Yalnız görüntüleme yetkisi olan işlem yapamaz; ters kayıt için Edit gerekir.</summary>
    [Fact]
    public void G2_Sadece_Goruntuleme_Islem_Yapamaz()
    {
        var k = Kasa(); var c = Cari();
        var r = _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 100m, Op(), PartyId: c));
        var viewer = new SessionContext("st", CoA, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission(FinanceService.Module, true, false, false, false) }));

        Assert.NotEmpty(_fq.Accounts(viewer));        // görebilir
        Assert.Throws<ForbiddenException>(() => _finance.Add(viewer,
            new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 100m, Op(), PartyId: c)));
        Assert.Throws<ForbiddenException>(() => _finance.Reverse(viewer, r.TransactionId, "gerekçe"));
    }

    /// <summary>G3 — FİRMA İZOLASYONU: başka firmanın hesabına işlem yazılamaz, hesabı okunamaz.</summary>
    [Fact]
    public void G3_Firma_Izolasyonu()
    {
        var k = Kasa(); var c = Cari();
        var other = new SessionContext("adB", "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        Assert.Throws<ForbiddenException>(() => _fq.Account(other, k));
        Assert.Throws<ForbiddenException>(() => _finance.Add(other,
            new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 100m, Op(), PartyId: c)));
        Assert.Empty(_fq.Accounts(other));
    }

    /// <summary>G4 — Başka firmanın carisine tahsilat yapılamaz.</summary>
    [Fact]
    public void G4_Baska_Firmanin_Carisine_Tahsilat_Yapilamaz()
    {
        var k = Kasa();
        var other = new SessionContext("adB", "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var yabanciCari = _parties.Create(other, new NewParty("C-B", "B Carisi", PartyTypes.Both));

        Assert.Throws<ForbiddenException>(() => _finance.Add(_admin,
            new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 100m, Op(), PartyId: yabanciCari)));
        Assert.Equal(0, Count("finance_transactions"));
    }

    /// <summary>
    /// G5 — ŞUBE İZOLASYONU: belirli şubeyle çalışan kullanıcı BAŞKA şubenin hesabına yazamaz.
    /// (Aynı kural <c>StockService.EnforceOwnBranch</c> ile paylaşılır — ikinci scope sistemi yok.)
    /// </summary>
    [Fact]
    public void G5_Sube_Izolasyonu()
    {
        var branches = new BranchService(_factory, _clock);
        var subeA = branches.Create(_admin, new NewBranch("ŞUBE A"));
        var subeB = branches.Create(_admin, new NewBranch("ŞUBE B"));
        var hesapB = _finance.CreateAccount(_admin, new NewFinanceAccount("K-B", "B Kasası", FinanceAccountKinds.Cash, BranchId: subeB));
        var c = Cari();

        // ŞUBE A ile çalışan kullanıcı
        var subeliKullanici = new SessionContext("u", CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
            { OperatingBranchId = subeA };

        Assert.Throws<ForbiddenException>(() => _finance.Add(subeliKullanici,
            new NewFinanceEntry(hesapB, FinanceTxnTypes.Receipt, 100m, Op(), PartyId: c)));
        Assert.Equal(0, Count("finance_transactions"));

        // Kendi şubesinin hesabına yazabilir
        var hesapA = _finance.CreateAccount(_admin, new NewFinanceAccount("K-A", "A Kasası", FinanceAccountKinds.Cash, BranchId: subeA));
        _finance.Add(subeliKullanici, new NewFinanceEntry(hesapA, FinanceTxnTypes.Receipt, 100m, Op(), PartyId: c));
        Assert.Equal(1, Count("finance_transactions"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // H — DEFTER SINIRI
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// H1 — Para hareketi STOK defterine DOKUNMAZ. Tahsilat/ödeme stok hareketi üretmez.
    /// </summary>
    [Fact]
    public void H1_Para_Hareketi_Stoka_Dokunmaz()
    {
        var k = Kasa(); var c = Cari();
        var stokOnce = Count("stock_movements");

        _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 5000m, Op(), PartyId: c));
        _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Payment, 2000m, Op(), PartyId: c));
        _finance.Transfer(_admin, new NewFinanceTransfer(k, Banka(), 1000m, Op()));

        Assert.Equal(stokOnce, Count("stock_movements"));
        Assert.Equal(0, Count("stock_documents"));
    }

    /// <summary>
    /// H2 — Cari hareketi KAYNAK BELGEYE bağlıdır (<c>source_type=finance</c>) ve elle girilebilir
    /// türlerden DEĞİLDİR — kullanıcı aynı tahsilatı elle ikinci kez giremez.
    /// </summary>
    [Fact]
    public void H2_Cari_Hareketi_Belge_Kaynakli()
    {
        var k = Kasa(); var c = Cari();
        var r = _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 1000m, Op(), PartyId: c));

        Assert.Equal(1, Count("party_ledger", $"source_type='finance' AND source_id='{r.TransactionId}'"));
        Assert.DoesNotContain(PartyDocTypes.Receipt, PartyDocTypes.ManualEntry);
        Assert.DoesNotContain(PartyDocTypes.Payment, PartyDocTypes.ManualEntry);
        Assert.Throws<ArgumentException>(() => _ledger.Add(_admin, new NewLedgerEntry(
            c, PartyDocTypes.Receipt, 100m, false, null, null, null, null, "TRY", null, null, null, Op())));
    }

    /// <summary>H3 — Fatura tablosuna "ödenen" YAZILMAZ: kapama yalnız tahsis tablosundadır.</summary>
    [Fact]
    public void H3_Faturaya_Odenen_Yazilmaz()
    {
        var k = Kasa(); var c = Cari();
        var f = SatisFaturasi(c);
        _finance.Add(_admin, new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 4000m, Op(), PartyId: c,
            Allocations: new[] { new InvoiceAllocationInput(f.Id, 4000m) }));

        // Faturanın kendi satırı DEĞİŞMEDİ; kalan yalnız tahsis tablosundan hesaplanır.
        Assert.Equal(1, Count("invoices", "grand_total='10000'"));
        Assert.Equal(1, Count("invoice_allocations", "is_reversed=0"));
        Assert.Equal(6000m, Kalan(f.Id, f.Total));
    }

    /// <summary>H4 — Para birimi uyuşmazlığı reddedilir (hesap TRY, işlem başka).</summary>
    [Fact]
    public void H4_Para_Birimi_Uyusmali()
    {
        var k = Kasa(); var c = Cari();
        Assert.Throws<ArgumentException>(() => _finance.Add(_admin,
            new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 100m, Op(), PartyId: c, Currency: "USD")));
    }

    /// <summary>H5 — Negatif/sıfır tutar reddedilir.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void H5_Gecersiz_Tutar_Reddedilir(int amount)
    {
        var k = Kasa(); var c = Cari();
        Assert.Throws<ArgumentException>(() => _finance.Add(_admin,
            new NewFinanceEntry(k, FinanceTxnTypes.Receipt, amount, Op(), PartyId: c)));
    }

    /// <summary>H6 — operation_id ZORUNLU.</summary>
    [Fact]
    public void H6_OperationId_Zorunlu()
    {
        var k = Kasa(); var c = Cari();
        Assert.Throws<ArgumentException>(() => _finance.Add(_admin,
            new NewFinanceEntry(k, FinanceTxnTypes.Receipt, 100m, "", PartyId: c)));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
