using ClosedXML.Excel;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-52 (2/2) (AR-GE 2026-10-27) — MUHASEBE AKTARIM DOSYASI (genel Excel) ═══
///  MA1 — dönem sunucuda (TR günü): "geçen ay" yalnız o ayın faturaları; iptal edilen dahil DEĞİL
///  MA2 — Faturalar sayfası: matrah = brüt − iskonto, KDV, tevkifat, genel toplam; cari kodu/VKN doğru
///  MA3 — KDV Dökümü: fatura × KDV oranı ayrı satır (beyanname eşlemesi)
///  MA4 — Cariler sayfası yalnız dönemde faturası olan carileri içerir
///  MA5 — Excel dosyası 4 sayfa (Bilgi, Faturalar, KDV Dökümü, Cariler); dışa aktarım yetkisi olmayan alamaz
///  MA6 — web ve masaüstü aynı dönem seçeneklerini gösterir
/// </summary>
public class MuhasebeAktarimTests : IDisposable
{
    private sealed class Saat : IClock { public DateTimeOffset UtcNow => new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero); }

    private readonly string _yol = Path.Combine(Path.GetTempPath(), "dw_mhs_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly InvoiceService _fatura;
    private readonly MuhasebeAktarimService _aktarim;
    private readonly SessionContext _y;
    private readonly string _c1, _c2, _c3;

    private static long Gun(int y, int m, int d) => new DateTimeOffset(y, m, d, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    public MuhasebeAktarimTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('MA','MA',1,1,1,0);";
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(_f).EnsureInitialAdmin("MA", "ma_admin", "Test!2026", RoleKeys.CompanyAdmin);
        _y = new SessionContext(uid, "MA", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var cari = new PartyService(_f);
        _c1 = cari.Create(_y, new NewParty("C-1", "Alfa Ltd", PartyTypes.Supplier, TaxNo: "1111111111"));
        _c2 = cari.Create(_y, new NewParty("C-2", "Beta AŞ", PartyTypes.Customer, TaxNo: "2222222222"));
        _c3 = cari.Create(_y, new NewParty("C-3", "Gama (dönem dışı)", PartyTypes.Supplier));
        _fatura = new InvoiceService(_f, new StockService(_f), new PartyLedgerService(_f));
        _aktarim = new MuhasebeAktarimService(_f, new Saat());

        // Eylül 2026 (= "geçen ay"): alış (2 KDV oranı, iskontolu) + satış + iptal edilen; Ekim'de (dönem dışı) bir fatura.
        _fatura.Create(_y, new NewInvoice(InvoiceDirections.Purchase, _c1, new[]
        {
            new NewInvoiceLine(null, "Malzeme", "Adet", 10, 100, DiscountRate: 10, VatRate: 20),
            new NewInvoiceLine(null, "Kitap", "Adet", 1, 50, VatRate: 10),
        }, "op-1", ExternalNo: "ALFA-77", InvoiceDate: Gun(2026, 9, 5), AffectsStock: false));
        _fatura.Create(_y, new NewInvoice(InvoiceDirections.Sales, _c2, new[] { new NewInvoiceLine(null, "Hizmet", null, 1, 1000, VatRate: 20, WithholdingRate: 50) },
            "op-2", InvoiceDate: Gun(2026, 9, 20), AffectsStock: false));
        var iptal = _fatura.Create(_y, new NewInvoice(InvoiceDirections.Purchase, _c1, new[] { new NewInvoiceLine(null, "İptal", null, 1, 999, VatRate: 20) },
            "op-3", InvoiceDate: Gun(2026, 9, 25), AffectsStock: false));
        _fatura.Cancel(_y, iptal.Id, "yanlış giriş");
        _fatura.Create(_y, new NewInvoice(InvoiceDirections.Purchase, _c3, new[] { new NewInvoiceLine(null, "Ekim", null, 1, 10, VatRate: 20) },
            "op-4", InvoiceDate: Gun(2026, 10, 2), AffectsStock: false));
    }

    public void Dispose() { try { File.Delete(_yol); } catch { } }

    private static object? H(Application.Reports.TableModel t, int satir, string sutun) => t.Rows[satir][t.Headers.ToList().IndexOf(sutun)];

    [Fact]
    public void MA1_Donem_Ve_Iptal()
    {
        var (bas, son, etiket) = _aktarim.Aralik("gecen-ay");
        Assert.Equal((Gun(2026, 9, 1), Gun(2026, 10, 1)), (bas, son));
        Assert.Contains("01.09.2026–30.09.2026", etiket);
        var f = _aktarim.Hazirla(_y, "gecen-ay")[1];
        Assert.Equal(2, f.Rows.Count);                                         // iptal ve Ekim faturası yok
        Assert.Equal(3, _aktarim.Hazirla(_y, "bu-yil")[1].Rows.Count);
    }

    [Fact]
    public void MA2_Faturalar_Tutarlar()
    {
        var f = _aktarim.Hazirla(_y, "gecen-ay")[1];
        Assert.Equal(("05.09.2026", "ALFA-77", "C-1", "1111111111"), ((string)H(f, 0, "Tarih")!, (string)H(f, 0, "Belge (Dış) No")!, (string)H(f, 0, "Cari Kodu")!, (string)H(f, 0, "VKN")!));
        Assert.Equal((1050m, 100m, 950m, 185m, 0m, 1135m),                    // brüt 1000+50, iskonto 100, KDV 900×20%+50×10%
            ((decimal)H(f, 0, "Brüt")!, (decimal)H(f, 0, "İskonto")!, (decimal)H(f, 0, "Matrah")!, (decimal)H(f, 0, "KDV")!, (decimal)H(f, 0, "Tevkifat")!, (decimal)H(f, 0, "Genel Toplam")!));
        Assert.Equal((200m, 100m, 1100m), ((decimal)H(f, 1, "KDV")!, (decimal)H(f, 1, "Tevkifat")!, (decimal)H(f, 1, "Genel Toplam")!));
    }

    [Fact]
    public void MA3_KDV_Dokumu_Oran_Basina()
    {
        var k = _aktarim.Hazirla(_y, "gecen-ay")[2];
        Assert.Equal(3, k.Rows.Count);                                         // alış: %10 + %20, satış: %20
        var alis10 = k.Rows.Single(r => (string)r[1]! != "" && (decimal)r[5]! == 10m);
        Assert.Equal((50m, 5m), ((decimal)alis10[6]!, (decimal)alis10[7]!));
    }

    [Fact]
    public void MA4_Cariler_Yalniz_Donemdekiler()
    {
        var c = _aktarim.Hazirla(_y, "gecen-ay")[3];
        Assert.Equal(new[] { "C-1", "C-2" }, c.Rows.Select(r => (string)r[0]!).ToArray());
    }

    [Fact]
    public void MA5_Excel_Dort_Sayfa_Ve_Yetki()
    {
        var bytes = new ExcelExportService().ExportMany(_aktarim.Hazirla(_y, "gecen-ay"));
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(new[] { "Bilgi", "Faturalar", "KDV Dökümü", "Cariler" }, wb.Worksheets.Select(w => w.Name).ToArray());
        var yetkisiz = new SessionContext(_y.UserId, "MA", new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("invoices", true, false, false, false) }));
        Assert.Throws<ForbiddenException>(() => _aktarim.Hazirla(yetkisiz, "gecen-ay"));   // dışa aktarım yetkisi yok
    }

    [Fact]
    public void MA6_Iki_Ortam_Ayni_Donem_Secenekleri()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        var web = File.ReadAllText(Path.Combine(d!.FullName, "src/DepoWise.Web/Components/Pages/Invoices.razor"));
        foreach (var (k, l) in MuhasebeAktarimService.Donemler) Assert.Contains($"(\"{k}\", \"{l}\")", web);
        Assert.Contains("DesktopServices.MuhasebeAktarim.Hazirla(_session, anahtar)", File.ReadAllText(Path.Combine(d.FullName, "src/DepoWise.Desktop/ViewModels/InvoicesViewModel.cs")));
    }
}
