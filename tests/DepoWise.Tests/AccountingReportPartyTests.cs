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
/// G4-4b — CARİ FİLTRESİ × ŞUBE KAPSAMI (kullanıcı isteği 2026-08-12).
///
/// <b>Bu dosyanın asıl işi:</b> cari filtresinin şube kapsamını <b>BYPASS ETMEDİĞİNİ</b> kanıtlamak.
/// İki filtre birlikte çalışır ve <b>ikisi de daraltır</b>:
/// <code>
/// SONUÇ = (İZİNLİ ŞUBELER ∩ İSTENEN ŞUBELER) × (İSTENEN CARİ ?? TÜM CARİLER)
/// </code>
///
/// <b>FAIL-OPEN NÖBETÇİSİ:</b> boş şube kesişiminde filtre KALKMAMALI (bir önceki turda
/// <c>ReportScope.BranchSql</c>'de bulunan ve kapatılan hatanın tekrar etmemesi için).
/// </summary>
public class AccountingReportPartyTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly PartyService _parties;
    private readonly PartyLedgerService _ledger;
    private readonly InvoiceService _invoices;
    private readonly FinanceService _finance;
    private readonly ReportService _reports;
    private readonly SessionContext _admin;
    private readonly string _ankara, _duzce, _karaman;
    private string _cariA = "", _cariB = "", _kAnk = "", _kDuz = "";
    private const string CoA = "A";

    public AccountingReportPartyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_g44b_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        var materials = new MaterialService(_factory, _clock);
        var stock = new StockService(_factory, _clock);
        _parties = new PartyService(_factory, _clock);
        _ledger = new PartyLedgerService(_factory, _clock);
        _invoices = new InvoiceService(_factory, stock, _ledger, _clock);
        _finance = new FinanceService(_factory, _ledger, _clock);
        _reports = new ReportService(_factory, _clock);

        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin(CoA, "admin", "Test!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_factory, _clock);
        _ankara = branches.Create(_admin, new NewBranch("ANKARA"));
        _duzce = branches.Create(_admin, new NewBranch("DÜZCE"));
        _karaman = branches.Create(_admin, new NewBranch("KARAMAN"));

        Veri(materials, stock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    /// <summary>
    /// İKİ CARİ × İKİ ŞUBE matrisi — cari ve şube filtrelerinin BAĞIMSIZ daralttığını gösterebilmek için:
    /// <list type="bullet">
    ///   <item>A carisi: ANKARA 1000 · DÜZCE 400</item>
    ///   <item>B carisi: ANKARA 250 · DÜZCE 60</item>
    /// </list>
    /// </summary>
    private void Veri(MaterialService materials, StockService stock)
    {
        var m = materials.Create(_admin, new NewMaterial("M-1", "M-1"));
        _cariA = _parties.Create(_admin, new NewParty("C-A", "ABC Ltd.", PartyTypes.Both));
        _cariB = _parties.Create(_admin, new NewParty("C-B", "XYZ A.Ş.", PartyTypes.Both));
        _kAnk = _finance.CreateAccount(_admin, new NewFinanceAccount("K-ANK", "Ankara Kasa", FinanceAccountKinds.Cash, BranchId: _ankara));
        _kDuz = _finance.CreateAccount(_admin, new NewFinanceAccount("K-DUZ", "Düzce Kasa", FinanceAccountKinds.Cash, BranchId: _duzce));

        stock.ReceiveIn(_admin, new[] { new StockLine(m, 500m) }, Op(), _ankara);
        stock.ReceiveIn(_admin, new[] { new StockLine(m, 500m) }, Op(), _duzce);

        void Fatura(string party, string branch, decimal tutar) =>
            _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Sales, party,
                new[] { new NewInvoiceLine(m, null, null, 1m, tutar) }, Op(), BranchId: branch));

        Fatura(_cariA, _ankara, 1000m);
        Fatura(_cariA, _duzce, 400m);
        Fatura(_cariB, _ankara, 250m);
        Fatura(_cariB, _duzce, 60m);
    }

    private static SessionContext Kullanici(string id, params string[] scope) =>
        new(id, CoA, new[] { RoleKeys.Staff }, new PermissionSet(new[]
        {
            new ModulePermission(PartyService.Module, true, true, true, true),
            new ModulePermission(InvoiceService.Module, true, true, true, true),
            new ModulePermission(FinanceService.Module, true, true, true, true),
        }, new[] { SpecialButtons.BranchSelect }))
        { ScopeBranchIds = scope.Length == 0 ? null : scope };

    /// <summary>⚠️ Tarih AÇIKÇA verilir: RequiresDate raporlarında Run "bu ay"a düşer (sistem saati).</summary>
    private static ReportRequest Istek(IReadOnlyList<string>? branchIds = null, IReadOnlyList<string>? partyIds = null)
        => new(true, 1_600_000_000_000, 1_800_000_000_000, branchIds, PartyIds: partyIds);

    private static decimal Toplam(TableModel t, int col)
        => t.TotalRow is null ? 0m : Convert.ToDecimal(t.TotalRow[col] ?? 0m);

    /// <summary>Cari ekstre BAKİYE toplamı (kolon 9).</summary>
    private decimal Ekstre(SessionContext s, IReadOnlyList<string>? br = null, IReadOnlyList<string>? pt = null)
        => Toplam(_reports.Run(s, "acc-statement", Istek(br, pt)), 9);

    // ═════════════════════════════════════════════════════════════════════════
    // A — KATALOG / PARİTE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// P1 — Cari filtresi ANLAMLI raporlarda AÇIK, anlamsız olanda KAPALI.
    /// <c>acc-cash</c> hesap özetidir; cariye bağlı değildir → körlemesine yayılmadı.
    /// </summary>
    [Fact]
    public void P1_Party_Bayragi_Dogru_Raporlarda()
    {
        foreach (var k in new[] { "acc-statement", "acc-balances", "acc-invoices", "acc-open-invoices", "acc-payments" })
            Assert.True(ReportCatalog.ByKey(k)!.UsesParty, $"{k}: cari filtresi AÇIK olmalı.");

        Assert.False(ReportCatalog.ByKey("acc-cash")!.UsesParty);   // hesap özeti cariye bağlı DEĞİL

        // Ön muhasebe dışına SIZMADI.
        var partili = ReportCatalog.All.Where(d => d.UsesParty).Select(d => d.Key).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "acc-balances", "acc-invoices", "acc-open-invoices", "acc-payments", "acc-statement" }, partili);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // B — CARİ FİLTRESİ TEK BAŞINA
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>P2 — Cari SEÇİLMEZSE mevcut davranış korunur: tüm cariler.</summary>
    [Fact]
    public void P2_Cari_Secilmezse_Tum_Cariler()
    {
        Assert.Equal(1710m, Ekstre(_admin));                       // 1000+400+250+60
        Assert.Equal(2, _reports.Run(_admin, "acc-balances", Istek()).Rows.Count);
    }

    /// <summary>P3 — ⭐ Cari seçilince YALNIZ o cari gelir.</summary>
    [Fact]
    public void P3_Cari_Secilince_Yalniz_O_Cari()
    {
        Assert.Equal(1400m, Ekstre(_admin, pt: new[] { _cariA }));  // 1000 + 400
        Assert.Equal(310m, Ekstre(_admin, pt: new[] { _cariB }));   // 250 + 60
        Assert.Single(_reports.Run(_admin, "acc-balances", Istek(partyIds: new[] { _cariA })).Rows);
    }

    /// <summary>P4 — Seçim TEMİZLENİNCE tüm cariler davranışına döner (web "Clearable" semantiği).</summary>
    [Fact]
    public void P4_Secim_Temizlenince_Tum_Cariler()
    {
        Assert.Equal(1400m, Ekstre(_admin, pt: new[] { _cariA }));
        Assert.Equal(1710m, Ekstre(_admin, pt: null));              // temizlendi
        Assert.Equal(1710m, Ekstre(_admin, pt: Array.Empty<string>()));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // C — ⭐ CARİ × ŞUBE BİRLİKTE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>P5 — ⭐ Cari + TEK şube: yalnız o carinin o şubedeki verisi.</summary>
    [Fact]
    public void P5_Cari_Ve_Tek_Sube()
    {
        Assert.Equal(1000m, Ekstre(_admin, new[] { _ankara }, new[] { _cariA }));
        Assert.Equal(400m, Ekstre(_admin, new[] { _duzce }, new[] { _cariA }));
        Assert.Equal(250m, Ekstre(_admin, new[] { _ankara }, new[] { _cariB }));
    }

    /// <summary>P6 — ⭐ Cari + ÇOKLU şube: seçilen şubelerin BİRLEŞİĞİ, yalnız o cari için.</summary>
    [Fact]
    public void P6_Cari_Ve_Coklu_Sube()
    {
        Assert.Equal(1400m, Ekstre(_admin, new[] { _ankara, _duzce }, new[] { _cariA }));
        Assert.Equal(1000m, Ekstre(_admin, new[] { _ankara, _karaman }, new[] { _cariA }));   // KARAMAN'da veri yok
    }

    /// <summary>
    /// P7 — ⭐ CARİ SEÇİMİ ŞUBE KAPSAMINI BYPASS ETMEZ: DÜZCE kullanıcısı cariyi seçse bile
    /// ANKARA verisini GÖREMEZ.
    /// </summary>
    [Fact]
    public void P7_Cari_Secimi_Kapsami_Bypass_Etmez()
    {
        var duzceli = Kullanici("u1", _duzce);

        Assert.Equal(400m, Ekstre(duzceli, pt: new[] { _cariA }));                       // yalnız DÜZCE
        Assert.Equal(400m, Ekstre(duzceli, new[] { _duzce }, new[] { _cariA }));
        Assert.Equal(0m, Ekstre(duzceli, new[] { _ankara }, new[] { _cariA }));          // ⭐ ANKARA SIZMIYOR
    }

    /// <summary>
    /// P8 — ⭐ FAIL-OPEN NÖBETÇİSİ: yetkisiz şube istendiğinde kesişim BOŞ olur ve filtre
    /// KALKMAZ — sonuç boştur, "tüm veriler" DEĞİL. (Önceki turda kapatılan hata tekrar etmesin.)
    /// </summary>
    [Fact]
    public void P8_Bos_Kesisim_Fail_Open_Olmaz()
    {
        var duzceli = Kullanici("u2", _duzce);

        Assert.Empty(_reports.Run(duzceli, "acc-statement", Istek(new[] { _karaman }, new[] { _cariA })).Rows);
        Assert.Empty(_reports.Run(duzceli, "acc-statement", Istek(new[] { _ankara })).Rows);
        Assert.Empty(_reports.Run(duzceli, "acc-invoices", Istek(new[] { _ankara, _karaman })).Rows);
        Assert.Empty(_reports.Run(duzceli, "acc-balances", Istek(new[] { _ankara })).Rows);
    }

    /// <summary>P9 — ⭐ Karışık istek: yetkisiz şube DÜŞER, yetkili olan KALIR.</summary>
    [Fact]
    public void P9_Karisik_Istekte_Yetkisiz_Duser()
    {
        var duzceli = Kullanici("u3", _duzce);
        Assert.Equal(400m, Ekstre(duzceli, new[] { _ankara, _duzce }, new[] { _cariA }));   // 1400 DEĞİL
    }

    /// <summary>P10 — ⭐ Yönetici A+B birleşik; yetkisiz C toplama GİRMEZ.</summary>
    [Fact]
    public void P10_Yonetici_Coklu_Sube()
    {
        var yonetici = Kullanici("u4", _ankara, _duzce);

        Assert.Equal(1400m, Ekstre(yonetici, pt: new[] { _cariA }));                                  // varsayılan = yetkili tümü
        Assert.Equal(1400m, Ekstre(yonetici, new[] { _ankara, _duzce }, new[] { _cariA }));
        Assert.Equal(1400m, Ekstre(yonetici, new[] { _ankara, _duzce, _karaman }, new[] { _cariA }));  // C düşer, DEĞİŞMEZ
        Assert.Equal(1000m, Ekstre(yonetici, new[] { _ankara }, new[] { _cariA }));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D — DİĞER RAPORLARDA CARİ FİLTRESİ
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>P11 — Fatura özeti cari + şube ile birlikte daralır.</summary>
    [Fact]
    public void P11_Fatura_Ozeti_Cari_Ve_Sube()
    {
        Assert.Equal(4, _reports.Run(_admin, "acc-invoices", Istek()).Rows.Count);
        Assert.Equal(2, _reports.Run(_admin, "acc-invoices", Istek(partyIds: new[] { _cariA })).Rows.Count);
        Assert.Single(_reports.Run(_admin, "acc-invoices", Istek(new[] { _ankara }, new[] { _cariA })).Rows);
    }

    /// <summary>P12 — Açık faturalar cari + şube ile daralır (kalan toplamı doğru).</summary>
    [Fact]
    public void P12_Acik_Faturalar_Cari_Ve_Sube()
    {
        var hepsi = _reports.Run(_admin, "acc-open-invoices", Istek());
        Assert.Equal(4, hepsi.Rows.Count);
        Assert.Equal(1710m, Toplam(hepsi, 9));

        var sadeceA = _reports.Run(_admin, "acc-open-invoices", Istek(partyIds: new[] { _cariA }));
        Assert.Equal(2, sadeceA.Rows.Count);
        Assert.Equal(1400m, Toplam(sadeceA, 9));

        var aAnkara = _reports.Run(_admin, "acc-open-invoices", Istek(new[] { _ankara }, new[] { _cariA }));
        Assert.Single(aAnkara.Rows);
        Assert.Equal(1000m, Toplam(aAnkara, 9));
    }

    /// <summary>P13 — Tahsilat raporu cari + şube ile daralır.</summary>
    [Fact]
    public void P13_Tahsilat_Cari_Ve_Sube()
    {
        _finance.Add(_admin, new NewFinanceEntry(_kAnk, FinanceTxnTypes.Receipt, 100m, Op(), PartyId: _cariA, BranchId: _ankara));
        _finance.Add(_admin, new NewFinanceEntry(_kDuz, FinanceTxnTypes.Receipt, 30m, Op(), PartyId: _cariB, BranchId: _duzce));

        Assert.Equal(2, _reports.Run(_admin, "acc-payments", Istek()).Rows.Count);
        Assert.Equal(100m, Toplam(_reports.Run(_admin, "acc-payments", Istek(partyIds: new[] { _cariA })), 9));
        Assert.Equal(30m, Toplam(_reports.Run(_admin, "acc-payments", Istek(new[] { _duzce })), 9));

        // ⭐ DÜZCE kullanıcısı A carisini seçse bile ANKARA tahsilatını göremez.
        Assert.Empty(_reports.Run(Kullanici("u5", _duzce), "acc-payments", Istek(partyIds: new[] { _cariA })).Rows);
    }

    /// <summary>
    /// P14 — ⭐ RAPOR = EKRAN: cari+şube filtreli bakiye, <c>PartyLedgerService.Balance</c> ile AYNI.
    /// İkinci finansal gerçeklik yok.
    /// </summary>
    [Fact]
    public void P14_Rapor_Ekranla_Ayni()
    {
        var duzceli = Kullanici("u6", _duzce);
        var rapor = Toplam(_reports.Run(duzceli, "acc-balances", Istek(partyIds: new[] { _cariA })), 4);
        var ekran = _ledger.Balance(duzceli, _cariA).Balance;
        Assert.Equal(ekran, rapor);
        Assert.Equal(400m, rapor);
    }

    /// <summary>P15 — Var olmayan/başka firmanın cari kimliği veri sızdırmaz (fail-closed).</summary>
    [Fact]
    public void P15_Gecersiz_Cari_Sizdirmaz()
    {
        Assert.Empty(_reports.Run(_admin, "acc-statement", Istek(partyIds: new[] { "yabanci-cari-id" })).Rows);
        Assert.Empty(_reports.Run(_admin, "acc-invoices", Istek(partyIds: new[] { "yabanci-cari-id" })).Rows);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
