using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G4-4 — ÖN MUHASEBE RAPORLARI (kullanıcı isteği 2026-08-12).
///
/// <b>Bu dosyanın koruduğu değişmezler:</b>
/// <list type="number">
///   <item><b>Şube kapsamı:</b> her rapor <c>ReportScope</c> → <c>BranchAccess</c> üzerinden geçer;
///     yetkisiz şube isteğe elle yazılsa bile veri sızmaz.</item>
///   <item><b>İkinci gerçeklik yok:</b> rapor toplamları ekran servisleriyle AYNI değeri verir.</item>
///   <item><b>"Firma toplamı"</b> = kullanıcının ERİŞEBİLDİĞİ şubelerin toplamı.</item>
///   <item><b>Çoklu şube</b> = seçilen şubelerin birleşiği.</item>
/// </list>
/// </summary>
public class AccountingReportTests : IDisposable
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
    private readonly ReportService _reports;
    private readonly SessionContext _admin;
    private readonly string _ankara, _duzce, _karaman;
    private string _cari = "", _kAnk = "", _kDuz = "";
    private const string CoA = "A";

    public AccountingReportTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_g44_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _parties = new PartyService(_factory, _clock);
        _ledger = new PartyLedgerService(_factory, _clock);
        _invoices = new InvoiceService(_factory, _stock, _ledger, _clock);
        _finance = new FinanceService(_factory, _ledger, _clock);
        _reports = new ReportService(_factory, _clock);

        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin(CoA, "admin", "Test!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_factory, _clock);
        _ankara = branches.Create(_admin, new NewBranch("ANKARA"));
        _duzce = branches.Create(_admin, new NewBranch("DÜZCE"));
        _karaman = branches.Create(_admin, new NewBranch("KARAMAN"));

        Veri();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    /// <summary>ANKARA'da 1000, DÜZCE'de 400 satış faturası; ANKARA'ya 300 tahsilat.</summary>
    private void Veri()
    {
        var m = _materials.Create(_admin, new NewMaterial("M-1", "M-1"));
        _cari = _parties.Create(_admin, new NewParty("C-001", "Örnek Ltd.", PartyTypes.Both));
        _kAnk = _finance.CreateAccount(_admin, new NewFinanceAccount("K-ANK", "Ankara Kasa", FinanceAccountKinds.Cash, BranchId: _ankara));
        _kDuz = _finance.CreateAccount(_admin, new NewFinanceAccount("K-DUZ", "Düzce Kasa", FinanceAccountKinds.Cash, BranchId: _duzce));

        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 100m) }, Op(), _ankara);
        _stock.ReceiveIn(_admin, new[] { new StockLine(m, 100m) }, Op(), _duzce);

        FAnkara = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Sales, _cari,
            new[] { new NewInvoiceLine(m, null, null, 1m, 1000m) }, Op(), BranchId: _ankara)).Id;
        FDuzce = _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Sales, _cari,
            new[] { new NewInvoiceLine(m, null, null, 1m, 400m) }, Op(), BranchId: _duzce)).Id;

        _finance.Add(_admin, new NewFinanceEntry(_kAnk, FinanceTxnTypes.Receipt, 300m, Op(),
            PartyId: _cari, BranchId: _ankara,
            Allocations: new[] { new InvoiceAllocationInput(FAnkara, 300m) }));
    }

    private string FAnkara = "", FDuzce = "";

    private static SessionContext Kullanici(string id, params string[] scope) =>
        new(id, CoA, new[] { RoleKeys.Staff }, new PermissionSet(new[]
        {
            new ModulePermission(PartyService.Module, true, true, true, true),
            new ModulePermission(InvoiceService.Module, true, true, true, true),
            new ModulePermission(FinanceService.Module, true, true, true, true),
        }, new[] { SpecialButtons.BranchSelect }))   // rapor şube seçimi yetkisi (ReportScope.CanSelectBranches)
        { ScopeBranchIds = scope.Length == 0 ? null : scope };

    /// <summary>
    /// Test isteği. ⚠️ TARİH AÇIKÇA VERİLİR: <c>RequiresDate</c> raporlarında <c>ReportService.Run</c>
    /// tarih gelmezse "BU AY"a düşürür ve bunu SİSTEM saatinden alır — test saati (2023) ile gerçek
    /// tarih (2026) uyuşmadığı için veri aralık dışında kalırdı. Ürün davranışı doğrudur; test
    /// gerçek bir aralık vermek zorundadır.
    /// </summary>
    private static ReportRequest Istek(IReadOnlyList<string>? branchIds = null, IReadOnlyList<string>? partyIds = null)
        => new(true, 1_600_000_000_000, 1_800_000_000_000, branchIds, PartyIds: partyIds);

    /// <summary>Toplam satırındaki sayısal hücreyi okur.</summary>
    private static decimal Toplam(TableModel t, int col)
        => t.TotalRow is null ? 0m : Convert.ToDecimal(t.TotalRow[col] ?? 0m);

    // ═════════════════════════════════════════════════════════════════════════
    // A — KATALOG
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>R1 — Altı ön muhasebe raporu KATALOGDA ve şube filtresi AÇIK.</summary>
    [Fact]
    public void R1_Raporlar_Katalogda()
    {
        foreach (var key in new[] { "acc-statement", "acc-balances", "acc-invoices",
                                    "acc-open-invoices", "acc-payments", "acc-cash" })
        {
            var d = ReportCatalog.ByKey(key);
            Assert.NotNull(d);
            Assert.Equal(ReportCategory.Accounting, d!.Category);
            Assert.True(d.UsesBranch, $"{key}: şube filtresi AÇIK olmalı.");
        }
        Assert.Equal("Ön Muhasebe", ReportCatalog.CategoryLabel(ReportCategory.Accounting));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // B — CARİ EKSTRE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>R2 — Admin firma geneli: iki faturanın cari hareketi + tahsilat.</summary>
    [Fact]
    public void R2_Ekstre_Firma_Geneli()
    {
        var t = _reports.Run(_admin, "acc-statement", Istek());
        Assert.Equal(3, t.Rows.Count);                     // 1000 + 400 fatura, 300 tahsilat
        Assert.Equal(1400m, Toplam(t, 7));                 // BORÇ
        Assert.Equal(300m, Toplam(t, 8));                  // ALACAK
        Assert.Equal(1100m, Toplam(t, 9));                 // BAKİYE
    }

    /// <summary>R3 — ⭐ TEK ŞUBE: yalnız o şubenin hareketleri ve bakiyesi.</summary>
    [Fact]
    public void R3_Ekstre_Tek_Sube()
    {
        var ank = _reports.Run(_admin, "acc-statement", Istek(new[] { _ankara }));
        Assert.Equal(2, ank.Rows.Count);                   // fatura + tahsilat
        Assert.Equal(700m, Toplam(ank, 9));                // 1000 − 300

        var duz = _reports.Run(_admin, "acc-statement", Istek(new[] { _duzce }));
        Assert.Single(duz.Rows);
        Assert.Equal(400m, Toplam(duz, 9));
    }

    /// <summary>R4 — ⭐ ÇOKLU ŞUBE: seçilen şubelerin BİRLEŞİĞİ (A+B = A + B).</summary>
    [Fact]
    public void R4_Ekstre_Coklu_Sube()
    {
        var t = _reports.Run(_admin, "acc-statement", Istek(new[] { _ankara, _duzce }));
        Assert.Equal(3, t.Rows.Count);
        Assert.Equal(1100m, Toplam(t, 9));                 // 700 + 400
    }

    /// <summary>
    /// R5 — ⭐ NORMAL KULLANICI: yalnız kendi şubesini raporlar. Başka şube İSTESE BİLE gelmez.
    /// </summary>
    [Fact]
    public void R5_Normal_Kullanici_Kendi_Subesi()
    {
        var duzceli = Kullanici("u1", _duzce);

        var varsayilan = _reports.Run(duzceli, "acc-statement", Istek());
        Assert.Single(varsayilan.Rows);
        Assert.Equal(400m, Toplam(varsayilan, 9));

        // ⭐ Elle ANKARA istese bile kesişim boş → ANKARA verisi GELMEZ.
        var zorlama = _reports.Run(duzceli, "acc-statement", Istek(new[] { _ankara }));
        Assert.Empty(zorlama.Rows);
    }

    /// <summary>R6 — ⭐ YETKİSİZ ŞUBE karışık istekte DÜŞER (A+B istenir, yalnız B gelir).</summary>
    [Fact]
    public void R6_Yetkisiz_Sube_Karisik_Istekte_Duser()
    {
        var duzceli = Kullanici("u2", _duzce);
        var t = _reports.Run(duzceli, "acc-statement", Istek(new[] { _ankara, _duzce }));
        Assert.Single(t.Rows);
        Assert.Equal(400m, Toplam(t, 9));                  // 1100 DEĞİL
    }

    /// <summary>R7 — Cari filtresi çalışır; başka cari istenirse boş döner.</summary>
    [Fact]
    public void R7_Cari_Filtresi()
    {
        var t = _reports.Run(_admin, "acc-statement", Istek(partyIds: new[] { _cari }));
        Assert.Equal(3, t.Rows.Count);
        Assert.Empty(_reports.Run(_admin, "acc-statement", Istek(partyIds: new[] { "yok" })).Rows);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // C — CARİ BAKİYE ÖZETİ
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// R8 — ⭐ RAPOR = EKRAN: bakiye özeti, <c>PartyLedgerService.Balance</c> ile AYNI değeri verir
    /// (ikinci finansal gerçeklik yok).
    /// </summary>
    [Fact]
    public void R8_Bakiye_Ozeti_Ekranla_Ayni()
    {
        var t = _reports.Run(_admin, "acc-balances", Istek());
        Assert.Single(t.Rows);
        Assert.Equal(_ledger.Balance(_admin, _cari).Balance, Toplam(t, 4));

        var duzceli = Kullanici("u3", _duzce);
        var td = _reports.Run(duzceli, "acc-balances", Istek());
        Assert.Equal(_ledger.Balance(duzceli, _cari).Balance, Toplam(td, 4));
        Assert.Equal(400m, Toplam(td, 4));
    }

    /// <summary>R9 — Çoklu şube bakiyesi seçilenlerin toplamıdır; yetkisiz şube GİRMEZ.</summary>
    [Fact]
    public void R9_Bakiye_Coklu_Sube()
    {
        Assert.Equal(700m, Toplam(_reports.Run(_admin, "acc-balances", Istek(new[] { _ankara })), 4));
        Assert.Equal(1100m, Toplam(_reports.Run(_admin, "acc-balances", Istek(new[] { _ankara, _duzce })), 4));
        Assert.Equal(0m, Toplam(_reports.Run(_admin, "acc-balances", Istek(new[] { _karaman })), 4));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D — FATURA RAPORLARI
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>R10 — Fatura özeti: tutar/ödenen/kalan doğru; kalan SAKLANMAZ, tahsislerden gelir.</summary>
    [Fact]
    public void R10_Fatura_Ozeti()
    {
        var t = _reports.Run(_admin, "acc-invoices", Istek());
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal(1400m, Toplam(t, 8));                 // TUTAR
        Assert.Equal(300m, Toplam(t, 9));                  // ÖDENEN
        Assert.Equal(1100m, Toplam(t, 10));                // KALAN
    }

    /// <summary>R11 — ⭐ Fatura raporu şube kapsamına uyar.</summary>
    [Fact]
    public void R11_Fatura_Sube_Kapsami()
    {
        Assert.Single(_reports.Run(_admin, "acc-invoices", Istek(new[] { _duzce })).Rows);
        Assert.Empty(_reports.Run(Kullanici("u4", _karaman), "acc-invoices", Istek()).Rows);
        Assert.Single(_reports.Run(Kullanici("u5", _ankara), "acc-invoices", Istek()).Rows);
    }

    /// <summary>R12 — Açık faturalar: kapanmamış olanlar; tam kapanan listeye GİRMEZ.</summary>
    [Fact]
    public void R12_Acik_Faturalar()
    {
        var t = _reports.Run(_admin, "acc-open-invoices", Istek());
        Assert.Equal(2, t.Rows.Count);                     // 700 + 400 kalan
        Assert.Equal(1100m, Toplam(t, 9));

        // ANKARA faturasının kalanını kapat → açık listeden çıkar.
        _finance.Add(_admin, new NewFinanceEntry(_kAnk, FinanceTxnTypes.Receipt, 700m, Op(),
            PartyId: _cari, BranchId: _ankara,
            Allocations: new[] { new InvoiceAllocationInput(FAnkara, 700m) }));

        var t2 = _reports.Run(_admin, "acc-open-invoices", Istek());
        Assert.Single(t2.Rows);
        Assert.Equal(400m, Toplam(t2, 9));
    }

    /// <summary>R13 — İPTAL edilmiş fatura kalan hesabına girmez ve açık listede yer almaz.</summary>
    [Fact]
    public void R13_Iptal_Fatura_Kalana_Girmez()
    {
        _invoices.Cancel(_admin, FDuzce, "yanlış kesildi");

        var ozet = _reports.Run(_admin, "acc-invoices", Istek());
        Assert.Equal(2, ozet.Rows.Count);                  // kayıt DURUYOR (silinmez)
        Assert.Equal(1000m, Toplam(ozet, 8));              // iptal TUTAR toplamına girmez
        Assert.Equal(700m, Toplam(ozet, 10));              // kalan yalnız ANKARA

        Assert.Single(_reports.Run(_admin, "acc-open-invoices", Istek()).Rows);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // E — TAHSİLAT/ÖDEME VE KASA
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>R14 — Tahsilat/ödeme özeti: yalnız cari etkileyen hareketler.</summary>
    [Fact]
    public void R14_Tahsilat_Odeme_Ozeti()
    {
        var t = _reports.Run(_admin, "acc-payments", Istek());
        Assert.Single(t.Rows);
        Assert.Equal(300m, Toplam(t, 9));                  // TAHSİLAT
        Assert.Equal(0m, Toplam(t, 10));                   // ÖDEME

        // İç transfer ve açılış BU RAPORDA yer almaz (Kasa raporundadır).
        _finance.Add(_admin, new NewFinanceEntry(_kDuz, FinanceTxnTypes.Opening, 5000m, Op()));
        _finance.Transfer(_admin, new NewFinanceTransfer(_kDuz, _kAnk, 100m, Op()));
        Assert.Single(_reports.Run(_admin, "acc-payments", Istek()).Rows);
    }

    /// <summary>R15 — ⭐ Tahsilat raporu şube kapsamına uyar.</summary>
    [Fact]
    public void R15_Tahsilat_Sube_Kapsami()
    {
        Assert.Single(_reports.Run(_admin, "acc-payments", Istek(new[] { _ankara })).Rows);
        Assert.Empty(_reports.Run(_admin, "acc-payments", Istek(new[] { _duzce })).Rows);
        Assert.Empty(_reports.Run(Kullanici("u6", _duzce), "acc-payments", Istek()).Rows);
    }

    /// <summary>
    /// R16 — ⭐ RAPOR = EKRAN: kasa raporu bakiyesi <c>FinanceQueryService.Balance</c> ile AYNI.
    /// </summary>
    [Fact]
    public void R16_Kasa_Raporu_Ekranla_Ayni()
    {
        var fq = new FinanceQueryService(_factory);
        var t = _reports.Run(_admin, "acc-cash", Istek());

        Assert.Equal(2, t.Rows.Count);
        Assert.Equal(fq.Balance(_admin, _kAnk) + fq.Balance(_admin, _kDuz), Toplam(t, 6));
        Assert.Equal(300m, Toplam(t, 6));                  // yalnız ANKARA tahsilatı
    }

    /// <summary>R17 — ⭐ Kasa raporu şube kapsamına uyar: yetkisiz şubenin hesabı GÖRÜNMEZ.</summary>
    [Fact]
    public void R17_Kasa_Sube_Kapsami()
    {
        Assert.Single(_reports.Run(_admin, "acc-cash", Istek(new[] { _ankara })).Rows);

        var duzceli = Kullanici("u7", _duzce);
        var t = _reports.Run(duzceli, "acc-cash", Istek());
        Assert.Single(t.Rows);                             // yalnız DÜZCE kasası
        Assert.Empty(_reports.Run(duzceli, "acc-cash", Istek(new[] { _ankara })).Rows);
    }

    /// <summary>R18 — İptal edilen hareket bakiyeye girmez ama defterde durur.</summary>
    [Fact]
    public void R18_Ters_Kayit_Rapora_Yansir()
    {
        var oncesi = Toplam(_reports.Run(_admin, "acc-cash", Istek()), 6);
        Assert.Equal(300m, oncesi);

        var txn = _reports.Run(_admin, "acc-payments", Istek());
        Assert.Single(txn.Rows);

        // Tahsilatı ters çevir → kasa bakiyesi 0, tahsilat toplamı 0, fatura kalanı geri artar.
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM finance_transactions WHERE company_id=@c AND txn_type='receipt' AND is_reversed=0 LIMIT 1;";
        cmd.AddWithValue("@c", CoA);
        var id = (string)cmd.ExecuteScalar()!;
        _finance.Reverse(_admin, id, "hatalı tahsilat");

        Assert.Equal(0m, Toplam(_reports.Run(_admin, "acc-cash", Istek()), 6));
        Assert.Equal(0m, Toplam(_reports.Run(_admin, "acc-payments", Istek()), 9));
        Assert.Equal(1400m, Toplam(_reports.Run(_admin, "acc-invoices", Istek()), 10));   // kalan geri arttı
    }

    // ═════════════════════════════════════════════════════════════════════════
    // F — YETKİ
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>R19 — Modül yetkisi olmayan kullanıcı ön muhasebe raporu ÇALIŞTIRAMAZ.</summary>
    [Fact]
    public void R19_Yetkisiz_Rapor_Calistiramaz()
    {
        var yetkisiz = new SessionContext("nobody", CoA, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _reports.Run(yetkisiz, "acc-statement", Istek()));
        Assert.Throws<ForbiddenException>(() => _reports.Run(yetkisiz, "acc-invoices", Istek()));
        Assert.Throws<ForbiddenException>(() => _reports.Run(yetkisiz, "acc-cash", Istek()));
    }

    /// <summary>
    /// R20 — ⭐ FİRMA TOPLAMI = ERİŞİLEBİLİR ŞUBELERİN TOPLAMI.
    /// İki şubeye yetkili yönetici KARAMAN'ı asla toplamına katamaz.
    /// </summary>
    [Fact]
    public void R20_Firma_Toplami_Erisilebilir_Subelerden()
    {
        var yonetici = Kullanici("u8", _ankara, _duzce);

        Assert.Equal(1100m, Toplam(_reports.Run(yonetici, "acc-balances", Istek()), 4));
        Assert.Equal(1100m, Toplam(_reports.Run(yonetici, "acc-balances",
            Istek(new[] { _ankara, _duzce, _karaman })), 4));   // KARAMAN düşer, toplam DEĞİŞMEZ
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
