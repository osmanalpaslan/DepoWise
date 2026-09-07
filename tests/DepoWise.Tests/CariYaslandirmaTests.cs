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
/// ═══ A2 — CARİ YAŞLANDIRMA (VADE ANALİZİ), kullanıcı isteği 2026-09-07 ═══
///
/// <para>Bu testler raporun <b>DAVRANIŞINI</b> doğrular, kaynak metnini değil: gerçek fatura ve
/// tahsilat kaydedilir, rapor çalıştırılır ve <b>tutarların doğru kovaya düştüğü</b> okunur.</para>
///
/// <para>Korunan değişmezler:
/// <list type="number">
///   <item>Gecikme günü → doğru kova (vadesiz · vadesi gelmemiş · 1-30 · 31-60 · 61-90 · 90+).
///     Sınır günleri (30/31, 60/61, 90/91) AYRI AYRI denenir — kova sınırı en sık hata yeridir.</item>
///   <item>Kapanmış fatura yaşlandırmaya GİRMEZ; kısmi tahsilat KALANI küçültür.</item>
///   <item>Alış ve satış AYRI satırdır (alacakla borç tek kovada toplanmaz).</item>
///   <item>Şube kapsamı ve cari filtresi uygulanır.</item>
///   <item>Toplam satırı, satırların toplamına EŞİTTİR (ikinci bir hesap üretilmez).</item>
/// </list></para>
/// </summary>
public class CariYaslandirmaTests : IDisposable
{
    private const string CoA = "A";
    private const long Gun = 86_400_000L;

    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly StockService _stock;
    private readonly PartyService _parties;
    private readonly InvoiceService _invoices;
    private readonly FinanceService _finance;
    private readonly ReportService _reports;
    private readonly SessionContext _admin;
    private readonly string _ankara, _duzce;
    private string _m = "", _cariA = "", _cariB = "", _kasa = "";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private long Simdi => _clock.UtcNow.ToUnixTimeMilliseconds();
    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    public CariYaslandirmaTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_yaslandirma_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();

        _materials = new MaterialService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _parties = new PartyService(_factory, _clock);
        var ledger = new PartyLedgerService(_factory, _clock);
        _invoices = new InvoiceService(_factory, _stock, ledger, _clock);
        _finance = new FinanceService(_factory, ledger, _clock);
        _reports = new ReportService(_factory, _clock);

        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin(CoA, "admin", "Test!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_factory, _clock);
        _ankara = branches.Create(_admin, new NewBranch("ANKARA"));
        _duzce = branches.Create(_admin, new NewBranch("DÜZCE"));

        _m = _materials.Create(_admin, new NewMaterial("M-1", "M-1"));
        _cariA = _parties.Create(_admin, new NewParty("C-A", "A Ltd.", PartyTypes.Both));
        _cariB = _parties.Create(_admin, new NewParty("C-B", "B Ltd.", PartyTypes.Both));
        _kasa = _finance.CreateAccount(_admin,
            new NewFinanceAccount("K-1", "Kasa", FinanceAccountKinds.Cash, BranchId: _ankara));

        _stock.ReceiveIn(_admin, new[] { new StockLine(_m, 100000m) }, Op(), _ankara);
        _stock.ReceiveIn(_admin, new[] { new StockLine(_m, 100000m) }, Op(), _duzce);
    }

    /// <summary>Fatura yazar. <paramref name="gecikmeGun"/>: pozitif = vadesi O KADAR gün önce doldu.</summary>
    private string Fatura(string cari, decimal tutar, long? gecikmeGun, string yon = InvoiceDirections.Sales,
        string? sube = null) =>
        _invoices.Create(_admin, new NewInvoice(yon, cari,
            new[] { new NewInvoiceLine(_m, null, null, 1m, tutar) }, Op(),
            BranchId: sube ?? _ankara,
            DueDate: gecikmeGun is null ? null : Simdi - gecikmeGun.Value * Gun)).Id;

    private static ReportRequest Istek(IReadOnlyList<string>? subeler = null, IReadOnlyList<string>? cariler = null)
        => new(true, BranchIds: subeler, PartyIds: cariler);

    private TableModel Rapor(ReportRequest? istek = null) => _reports.Run(_admin, "acc-aging", istek ?? Istek());

    /// <summary>Bir satırdaki kova hücresini decimal olarak okur (boş hücre = 0).</summary>
    private static decimal Hucre(IReadOnlyList<object?> satir, int kolon)
        => satir[kolon] is decimal d ? d : 0m;

    // Kolon indeksleri: 0 TÜR · 1 KOD · 2 CARİ · 3 VADESİZ · 4 GELMEMİŞ · 5 1-30 · 6 31-60 · 7 61-90 · 8 90+ · 9 TOPLAM
    private const int Vadesiz = 3, Gelmemis = 4, K30 = 5, K60 = 6, K90 = 7, K90Plus = 8, ToplamAcik = 9;

    // ═════════════════════════════════════════════════════════════════════════
    // 1) KOVA SINIRLARI — en sık hata yeri
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void YAS1_Her_Tutar_Dogru_Kovaya_Duser()
    {
        Fatura(_cariA, 100m, null);      // vadesiz
        Fatura(_cariA, 200m, -5);        // vadesine 5 gün var  → gelmemiş
        Fatura(_cariA, 300m, 1);         // 1 gün gecikme       → 1-30
        Fatura(_cariA, 400m, 45);        // 45 gün              → 31-60
        Fatura(_cariA, 500m, 75);        // 75 gün              → 61-90
        Fatura(_cariA, 600m, 200);       // 200 gün             → 90+

        var satir = Assert.Single(Rapor().Rows);
        Assert.Equal(100m, Hucre(satir, Vadesiz));
        Assert.Equal(200m, Hucre(satir, Gelmemis));
        Assert.Equal(300m, Hucre(satir, K30));
        Assert.Equal(400m, Hucre(satir, K60));
        Assert.Equal(500m, Hucre(satir, K90));
        Assert.Equal(600m, Hucre(satir, K90Plus));
        Assert.Equal(2100m, Hucre(satir, ToplamAcik));
    }

    [Fact]
    public void YAS2_Kova_Sinirlari_Tam_Gunlerde_Dogru()
    {
        // 30 → 1-30 · 31 → 31-60 · 60 → 31-60 · 61 → 61-90 · 90 → 61-90 · 91 → 90+
        Fatura(_cariA, 10m, 30);
        Fatura(_cariA, 20m, 31);
        Fatura(_cariA, 40m, 60);
        Fatura(_cariA, 80m, 61);
        Fatura(_cariA, 160m, 90);
        Fatura(_cariA, 320m, 91);

        var satir = Assert.Single(Rapor().Rows);
        Assert.Equal(10m, Hucre(satir, K30));
        Assert.Equal(60m, Hucre(satir, K60));     // 20 + 40
        Assert.Equal(240m, Hucre(satir, K90));    // 80 + 160
        Assert.Equal(320m, Hucre(satir, K90Plus));
    }

    [Fact]
    public void YAS3_Vadesi_Bugun_Dolan_Fatura_Henuz_Gecikmis_Sayilmaz()
    {
        Fatura(_cariA, 50m, 0);           // vade tam BUGÜN
        var satir = Assert.Single(Rapor().Rows);
        Assert.Equal(50m, Hucre(satir, Gelmemis));
        Assert.Equal(0m, Hucre(satir, K30));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 2) TAHSİLAT — kapanmış fatura düşer, kısmi ödeme kalanı küçültür
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void YAS4_Kapanan_Fatura_Yaslandirmaya_Girmez_Kismi_Odeme_Kalani_Kucultur()
    {
        var tamOdenen = Fatura(_cariA, 1000m, 45);
        var yariOdenen = Fatura(_cariA, 1000m, 45);

        _finance.Add(_admin, new NewFinanceEntry(_kasa, FinanceTxnTypes.Receipt, 1000m, Op(),
            PartyId: _cariA, BranchId: _ankara,
            Allocations: new[] { new InvoiceAllocationInput(tamOdenen, 1000m) }));
        _finance.Add(_admin, new NewFinanceEntry(_kasa, FinanceTxnTypes.Receipt, 400m, Op(),
            PartyId: _cariA, BranchId: _ankara,
            Allocations: new[] { new InvoiceAllocationInput(yariOdenen, 400m) }));

        var satir = Assert.Single(Rapor().Rows);
        Assert.Equal(600m, Hucre(satir, K60));          // yalnız kalan
        Assert.Equal(600m, Hucre(satir, ToplamAcik));
    }

    [Fact]
    public void YAS5_Tamami_Kapanan_Cari_Listede_Hic_Gorunmez()
    {
        var f = Fatura(_cariA, 500m, 10);
        _finance.Add(_admin, new NewFinanceEntry(_kasa, FinanceTxnTypes.Receipt, 500m, Op(),
            PartyId: _cariA, BranchId: _ankara,
            Allocations: new[] { new InvoiceAllocationInput(f, 500m) }));

        Assert.Empty(Rapor().Rows);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 3) ALIŞ / SATIŞ AYRIMI — alacakla borç aynı kovada toplanmaz
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void YAS6_Ayni_Cari_Icin_Alis_Ve_Satis_AYRI_Satirdir()
    {
        Fatura(_cariA, 700m, 100, InvoiceDirections.Sales);
        Fatura(_cariA, 250m, 100, InvoiceDirections.Purchase);

        var t = Rapor();
        Assert.Equal(2, t.Rows.Count);
        Assert.All(t.Rows, r => Assert.Equal("A Ltd.", (string)r[2]!));
        Assert.Contains(t.Rows, r => Hucre(r, K90Plus) == 700m);
        Assert.Contains(t.Rows, r => Hucre(r, K90Plus) == 250m);
        // Türler farklı olmalı — tek kovada toplanmadıklarının kanıtı.
        Assert.Equal(2, t.Rows.Select(r => (string)r[0]!).Distinct().Count());
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 4) FİLTRELER — cari ve şube
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void YAS7_Cari_Filtresi_Yalniz_Secileni_Getirir()
    {
        Fatura(_cariA, 100m, 40);
        Fatura(_cariB, 900m, 40);

        Assert.Equal(2, Rapor().Rows.Count);
        var yalnizB = Assert.Single(Rapor(Istek(cariler: new[] { _cariB })).Rows);
        Assert.Equal("B Ltd.", (string)yalnizB[2]!);
        Assert.Equal(900m, Hucre(yalnizB, ToplamAcik));
    }

    [Fact]
    public void YAS8_Sube_Kapsami_Uygulanir()
    {
        Fatura(_cariA, 100m, 40, sube: _ankara);
        Fatura(_cariA, 900m, 40, sube: _duzce);

        Assert.Equal(1000m, Hucre(Assert.Single(Rapor().Rows), ToplamAcik));
        Assert.Equal(900m, Hucre(Assert.Single(Rapor(Istek(new[] { _duzce })).Rows), ToplamAcik));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 5) TOPLAM SATIRI — ikinci bir hesap üretilmez
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void YAS9_Toplam_Satiri_Satirlarin_Toplamina_Esittir()
    {
        Fatura(_cariA, 100m, null);
        Fatura(_cariA, 200m, 15);
        Fatura(_cariB, 400m, 120);
        Fatura(_cariB, 800m, 70, InvoiceDirections.Purchase);

        var t = Rapor();
        Assert.NotNull(t.TotalRow);
        for (var kolon = Vadesiz; kolon <= ToplamAcik; kolon++)
        {
            var beklenen = t.Rows.Sum(r => Hucre(r, kolon));
            var gelen = t.TotalRow![kolon] is decimal d ? d : 0m;
            Assert.Equal(beklenen, gelen);
        }
        Assert.Equal(1500m, t.TotalRow![ToplamAcik]);
    }

    [Fact]
    public void YAS10_Kayit_Yoksa_Tablo_Bos_Ve_Toplam_Satiri_Yok()
    {
        var t = Rapor();
        Assert.Empty(t.Rows);
        Assert.Null(t.TotalRow);
        Assert.Equal(10, t.Headers.Count);
    }

    public void Dispose() => TestDbTemizlik.Bitir(_dbPath);
}
