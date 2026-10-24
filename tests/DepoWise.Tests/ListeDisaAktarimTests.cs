using System.Net;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-54 (AR-GE 2026-10-24, 05 F-057) — LİSTE STANDARDI: Excel'e Aktar / Yazdır FİLTRELENMİŞ TÜM SONUÇ ═══
///  LD1 — Sayfa birleştirici: servisin kendi sayfa sınırıyla (200/500) tüm kümeyi toplar; boş/son sayfada durur; üst sınır
///  LD2 — Fatura tablosu: ekran kolonları; tutar GİZLİYSE "Tutar" kolonu ve değeri hiç yok (0 yazılmaz)
///  LD3 — API /api/invoices/export: liste ucuyla aynı süzgeç → satır sayısı liste toplamına eşit (sayfa sınırını aşan küme dahil);
///         PDF aynı uçtan; yetkisiz (export yok) 403
///  LD4 — İki platform kaynak kilidi (web ortak FiltreQuery, masaüstü komutlar)
/// </summary>
[Collection("PostgresSchema")]
public class ListeDisaAktarimTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "LDA-A", Admin = "lda_admin", Pass = "Test!2026";
    private HttpClient _client = null!;
    private SessionContext _s = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        using (var conn = Svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                              "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", Company);
            cmd.ExecuteNonQuery();
        }
        var uid = Svc.Users.EnsureInitialAdmin(Company, Admin, Pass, RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _client = await _host.LoginAsync(Admin, Pass, Company);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();
    private ServerServices Svc => _host.Services.GetRequiredService<ServerServices>();

    [Fact]
    public void LD1_Sayfa_Birlestirici()
    {
        GridResult<int> Sayfa(int toplam, int sinir, int p, int n)
        {
            var boyut = Math.Min(n, sinir);
            var items = Enumerable.Range((p - 1) * boyut, boyut).Where(i => i < toplam).ToList();
            return new GridResult<int>(items, toplam, p, boyut);
        }
        Assert.Equal(1234, ListeDisaAktarim.TumSayfalar((p, n) => Sayfa(1234, 200, p, n)).Count);   // servis 200'e kısıyor
        Assert.Equal(1234, ListeDisaAktarim.TumSayfalar((p, n) => Sayfa(1234, 500, p, n)).Count);
        Assert.Equal(Enumerable.Range(0, 1000), ListeDisaAktarim.TumSayfalar((p, n) => Sayfa(1000, 500, p, n)));   // tam katında da durur, sıra korunur
        Assert.Empty(ListeDisaAktarim.TumSayfalar((p, n) => Sayfa(0, 500, p, n)));
        var cagri = 0;
        Assert.Equal(ListeDisaAktarim.UstSinir, ListeDisaAktarim.TumSayfalar((p, n) => { cagri++; return Sayfa(int.MaxValue, 500, p, n); }).Count);
        Assert.Equal(ListeDisaAktarim.UstSinir / 500, cagri);
    }

    private string Fatura(string partyId, string yon, decimal fiyat)
        => Svc.Invoices.Create(_s, new NewInvoice(yon, partyId, new[] { new NewInvoiceLine(null, "Hizmet", null, 1m, fiyat) },
            Guid.NewGuid().ToString("N"), AffectsStock: false)).Id;

    [Fact]
    public void LD2_Fatura_Tablosu_Tutar_Gizliyse_Kolon_Yok()
    {
        var pid = Svc.Parties.Create(_s, new NewParty("LD2", "LD2 Cari", PartyTypes.Customer));
        Fatura(pid, InvoiceDirections.Sales, 1234.5m);
        var rows = Svc.InvoiceQueries.List(_s).Items;
        var acik = InvoiceListeTablosu.ToTableModel(rows, tutarGorunur: true);
        Assert.Contains("Tutar", acik.Headers);
        Assert.Equal(1234.5m, acik.Rows.Single()[acik.Headers.ToList().IndexOf("Tutar")]);
        Assert.True(acik.Numeric![acik.Headers.ToList().IndexOf("Tutar")]);
        var gizli = InvoiceListeTablosu.ToTableModel(rows, tutarGorunur: false);
        Assert.DoesNotContain("Tutar", gizli.Headers);
        Assert.DoesNotContain("Para Birimi", gizli.Headers);
        Assert.All(gizli.Rows, r => Assert.DoesNotContain(r, h => h is decimal));
        Assert.Equal(gizli.Headers.Count, gizli.Rows.Single().Count);
    }

    [Fact]
    public async Task LD3_Api_Export_Liste_Ile_Ayni_Kume()
    {
        var pid = Svc.Parties.Create(_s, new NewParty("LD3", "LD3 Cari", PartyTypes.Customer));
        for (var i = 0; i < 7; i++) Fatura(pid, InvoiceDirections.Sales, 100m + i);
        for (var i = 0; i < 3; i++) Fatura(pid, InvoiceDirections.Purchase, 50m);

        async Task<int> ExcelSatir(string q)
        {
            var r = await _client.GetAsync("/api/invoices/export?" + q);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return Svc.Excel.ReadRows(await r.Content.ReadAsByteArrayAsync()).Count;
        }
        Assert.Equal(Svc.InvoiceQueries.List(_s).TotalCount, await ExcelSatir(""));
        Assert.Equal(7, await ExcelSatir("direction=" + InvoiceDirections.Sales));
        Assert.Equal(3, await ExcelSatir("direction=" + InvoiceDirections.Purchase));
        // Sayfa sınırını aşan küme: servis tek sayfada en çok 500 verir; 1 satırlık sayfalarla bile birleştirici tümünü toplar
        Assert.Equal(10, ListeDisaAktarim.TumSayfalar((p, n) => Svc.InvoiceQueries.List(_s, page: p, pageSize: 1)).Count);

        var pdf = await _client.GetAsync("/api/invoices/export?direction=" + InvoiceDirections.Sales + "&format=pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);

        // export yetkisi olmayan kullanıcı → 403 (fatura görme yetkisi olsa bile)
        var sube = Svc.Branches.Create(_s, new DepoWise.Infrastructure.Organization.NewBranch("LD3 Depo"));
        Svc.Users.CreateUser(_s, new NewUser("lda_gor", "Gor!2026xx", "Gören", new[] { RoleKeys.Staff },
            Permissions: new[] { new ModulePermission("invoices", true, false, false, false) }, BranchId: sube));
        var gor = await _host.LoginAsync("lda_gor", "Gor!2026xx", Company, sube);
        Assert.Equal(HttpStatusCode.OK, (await gor.GetAsync("/api/invoices")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gor.GetAsync("/api/invoices/export")).StatusCode);
    }

    private async Task<int> ExcelSatir(string yol)
    {
        var r = await _client.GetAsync(yol);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return Svc.Excel.ReadRows(await r.Content.ReadAsByteArrayAsync()).Count;
    }

    [Fact]
    public async Task LD5_Cari_Export_Suzgec_Ve_Bakiye_Gizleme()
    {
        for (var i = 0; i < 3; i++) Svc.Parties.Create(_s, new NewParty("LD5M" + i, "Müşteri " + i, PartyTypes.Customer));
        for (var i = 0; i < 2; i++) Svc.Parties.Create(_s, new NewParty("LD5T" + i, "Tedarikçi " + i, PartyTypes.Supplier));
        Assert.Equal(Svc.Parties.List(_s, null, PartyTypes.Customer, null).TotalCount, await ExcelSatir("/api/parties/export?type=" + PartyTypes.Customer));
        Assert.Equal(Svc.Parties.List(_s, null, PartyTypes.Supplier, null).TotalCount, await ExcelSatir("/api/parties/export?type=" + PartyTypes.Supplier));
        Assert.Equal(1, await ExcelSatir("/api/parties/export?search=LD5T1"));
        Assert.Equal("application/pdf", (await _client.GetAsync("/api/parties/export?format=pdf")).Content.Headers.ContentType?.MediaType);
        var rows = Svc.Parties.List(_s, null, null, null).Items;
        Assert.Contains("Bakiye", PartyListeTablosu.ToTableModel(rows, true).Headers);
        var gizli = PartyListeTablosu.ToTableModel(rows, false);
        Assert.DoesNotContain("Bakiye", gizli.Headers);
        Assert.DoesNotContain("Borç", gizli.Headers);
        Assert.All(gizli.Rows, r => Assert.Equal(gizli.Headers.Count, r.Count));
        var web = Oku("src/DepoWise.Web/Components/Pages/Parties.razor");
        Assert.Contains("var url = $\"/api/parties?page={_page}&pageSize=50\" + FiltreQuery;", web);
        Assert.Contains("\"/api/parties/export?\" + FiltreQuery.TrimStart('&')", web);
        Assert.Contains("PartyListeTablosu.ToTableModel(", Oku("src/DepoWise.Desktop/ViewModels/PartiesViewModel.cs"));
        Assert.Contains("YazdirCommand", Oku("src/DepoWise.Desktop/Views/PartiesView.axaml"));
    }

    [Fact]
    public async Task LD6_Finans_Hesaplari_Export_Ve_Bakiye_Gizleme()
    {
        Svc.Finance.CreateAccount(_s, new NewFinanceAccount("LD6K1", "LD6 Kasa 1", FinanceAccountKinds.Cash));
        Svc.Finance.CreateAccount(_s, new NewFinanceAccount("LD6K2", "LD6 Kasa 2", FinanceAccountKinds.Cash));
        Svc.Finance.CreateAccount(_s, new NewFinanceAccount("LD6B1", "LD6 Banka", FinanceAccountKinds.Bank));
        Assert.Equal(Svc.FinanceQueries.Accounts(_s, FinanceAccountKinds.Cash).Count, await ExcelSatir("/api/finance/accounts/export?kind=" + FinanceAccountKinds.Cash));
        Assert.Equal(Svc.FinanceQueries.Accounts(_s, FinanceAccountKinds.Bank).Count, await ExcelSatir("/api/finance/accounts/export?kind=" + FinanceAccountKinds.Bank));
        Assert.Equal(1, await ExcelSatir("/api/finance/accounts/export?search=LD6B1"));
        Assert.Equal("application/pdf", (await _client.GetAsync("/api/finance/accounts/export?format=pdf")).Content.Headers.ContentType?.MediaType);
        var rows = Svc.FinanceQueries.Accounts(_s);
        Assert.Contains("Bakiye", FinanceListeTablosu.ToTableModel(rows, true).Headers);
        var gizli = FinanceListeTablosu.ToTableModel(rows, false);
        Assert.DoesNotContain("Bakiye", gizli.Headers);
        Assert.DoesNotContain("Giriş", gizli.Headers);
        Assert.All(gizli.Rows, r => Assert.Equal(gizli.Headers.Count, r.Count));
        var web = Oku("src/DepoWise.Web/Components/Pages/Finance.razor");
        Assert.Contains("var url = \"/api/finance/accounts?\" + FiltreQuery.TrimStart('&');", web);
        Assert.Contains("\"/api/finance/accounts/export?\" + FiltreQuery.TrimStart('&')", web);
        Assert.Contains("FinanceListeTablosu.ToTableModel(", Oku("src/DepoWise.Desktop/ViewModels/FinanceViewModel.cs"));
        Assert.Contains("YazdirCommand", Oku("src/DepoWise.Desktop/Views/FinanceView.axaml"));
    }

    [Fact]
    public async Task LD7_Tahsilat_Odeme_Export_Ve_Tutar_Gizleme()
    {
        var kasa = Svc.Finance.CreateAccount(_s, new NewFinanceAccount("LD7K", "LD7 Kasa", FinanceAccountKinds.Cash));
        var cari = Svc.Parties.Create(_s, new NewParty("LD7C", "LD7 Cari", PartyTypes.Both));
        for (var i = 0; i < 4; i++) Svc.Finance.Add(_s, new NewFinanceEntry(kasa, FinanceTxnTypes.Receipt, 100m + i, Guid.NewGuid().ToString("N"), PartyId: cari));
        for (var i = 0; i < 2; i++) Svc.Finance.Add(_s, new NewFinanceEntry(kasa, FinanceTxnTypes.Payment, 10m, Guid.NewGuid().ToString("N"), PartyId: cari));
        Assert.Equal(Svc.FinanceQueries.Transactions(_s, txnType: FinanceTxnTypes.Receipt).TotalCount,
            await ExcelSatir("/api/finance/transactions/export?txnType=" + FinanceTxnTypes.Receipt));
        Assert.Equal(Svc.FinanceQueries.Transactions(_s, txnType: FinanceTxnTypes.Payment).TotalCount,
            await ExcelSatir("/api/finance/transactions/export?txnType=" + FinanceTxnTypes.Payment));
        Assert.Equal("application/pdf", (await _client.GetAsync("/api/finance/transactions/export?format=pdf")).Content.Headers.ContentType?.MediaType);
        var rows = Svc.FinanceQueries.Transactions(_s).Items;
        var acik = FinanceIslemTablosu.ToTableModel(rows, true);
        Assert.Contains("Giriş", acik.Headers);
        Assert.Contains(103m, acik.Rows.Select(r => r[acik.Headers.ToList().IndexOf("Giriş")]));
        var gizli = FinanceIslemTablosu.ToTableModel(rows, false);
        Assert.DoesNotContain("Giriş", gizli.Headers);
        Assert.DoesNotContain("Çıkış", gizli.Headers);
        Assert.All(gizli.Rows, r => Assert.DoesNotContain(r, h => h is decimal));
        var web = Oku("src/DepoWise.Web/Components/Pages/Payments.razor");
        Assert.Contains("var url = $\"/api/finance/transactions?page={_page}&pageSize=50\" + FiltreQuery;", web);
        Assert.Contains("\"/api/finance/transactions/export?\" + FiltreQuery.TrimStart('&')", web);
        Assert.Contains("FinanceIslemTablosu.ToTableModel(", Oku("src/DepoWise.Desktop/ViewModels/PaymentsViewModel.cs"));
        Assert.Contains("YazdirCommand", Oku("src/DepoWise.Desktop/Views/PaymentsView.axaml"));
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void LD4_Iki_Platform_Kaynak()
    {
        var api = Oku("src/DepoWise.Api/Program.cs");
        Assert.Contains("app.MapGet(\"/api/invoices/export\"", api);
        var web = Oku("src/DepoWise.Web/Components/Pages/Invoices.razor");
        Assert.Contains("var url = $\"/api/invoices?page={_page}&pageSize=50\" + FiltreQuery;", web);   // liste ve dışa aktarım TEK süzgeç
        Assert.Contains("\"/api/invoices/export?\" + FiltreQuery.TrimStart('&')", web);
        Assert.Contains("OnClick=\"@(() => Disari(true))\"", web);
        var vm = Oku("src/DepoWise.Desktop/ViewModels/InvoicesViewModel.cs");
        Assert.Contains("ListeDisaAktarim.TumSayfalar(", vm);
        Assert.Contains("InvoiceListeTablosu.ToTableModel(", vm);
        var v = Oku("src/DepoWise.Desktop/Views/InvoicesView.axaml");
        Assert.Contains("ExportExcelCommand", v);
        Assert.Contains("YazdirCommand", v);
    }
}
