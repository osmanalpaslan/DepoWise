using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Purchasing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-24 (K-09 a, F-045) + M-51 (K-13 a, F-053) — ÇİFT STOK UYARISI VE PARA İŞLEMİ ONAYI ═══
///  CSU1 — Aynı tedarikçi fatura no'suyla yapılmış stok girişi → uyarı; başka no / satış faturası → uyarı yok
///  CSU2 — Cari tedarikçiye bağlıysa aynı malzemenin SİPARİŞLE mal kabulü → uyarı (sipariş no); başka malzeme / bağsız cari → yok
///  CSU3 — /api/invoices/check: onay metni (genel toplam), çift stok metni ve "stoğu etkilemesin" seçilince uyarı yok
///  CSU4 — /api/finance/entries/check: tahsilat/ödeme onay metni (tutar + kapatılacak fatura)
///  CSU5 — İki platform kayıttan ÖNCE kontrol eder ve onay ister (kaynak kilidi)
/// </summary>
[Collection("PostgresSchema")]
public class CiftStokUyariTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "CSU-A", User = "csu_admin", Pass = "Test!2026";
    private HttpClient _client = null!;
    private SessionContext _s = null!;
    private string _sube = "", _mat = "", _mat2 = "", _cariBagli = "", _cariBagsiz = "", _tedarikci = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        Exec($"INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) VALUES('{Company}','{Company}',1,1,1,0,5,20,5) ON CONFLICT(id) DO NOTHING;");
        var uid = Svc.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _sube = Svc.Branches.Create(_s, new NewBranch("CSU Depo"));
        _mat = Svc.Materials.Create(_s, new NewMaterial("CSU-M1", "Filtre"));
        _mat2 = Svc.Materials.Create(_s, new NewMaterial("CSU-M2", "Kayış"));
        _tedarikci = "csu-sup-1";
        Exec($"INSERT INTO suppliers(id,company_id,name,created_at,updated_at) VALUES('{_tedarikci}','{Company}','CSU Tedarik',1,1);");
        _cariBagli = Svc.Parties.Create(_s, new NewParty("CSU-C1", "CSU Tedarik A.Ş.", PartyTypes.Supplier, SupplierId: _tedarikci));
        _cariBagsiz = Svc.Parties.Create(_s, new NewParty("CSU-C2", "Bağsız Cari", PartyTypes.Supplier));
        _client = await _host.LoginAsync(User, Pass, Company);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private ServerServices Svc => _host.Services.GetRequiredService<ServerServices>();

    private void Exec(string sql)
    {
        using var c = Svc.Factory.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
    }

    private string Op() => Guid.NewGuid().ToString("N");

    [Fact]
    public void CSU1_Ayni_Fatura_Nosuyla_Stok_Girisi()
    {
        Svc.Stock.ReceiveIn(_s, new[] { new StockLine(_mat, 5m) }, Op(), _sube, invoiceNo: "TED-2026-77");
        var u = Svc.Invoices.CiftStokUyarilari(_s, InvoiceDirections.Purchase, _cariBagsiz, " ted-2026-77 ", new[] { _mat });
        Assert.Single(u);
        Assert.Contains("TED-2026-77", u[0], StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Svc.Invoices.CiftStokUyarilari(_s, InvoiceDirections.Purchase, _cariBagsiz, "BASKA-1", new[] { _mat }));
        Assert.Empty(Svc.Invoices.CiftStokUyarilari(_s, InvoiceDirections.Sales, _cariBagsiz, "TED-2026-77", new[] { _mat }));
    }

    [Fact]
    public void CSU2_Siparisle_Mal_Kabul_Uyarisi()
    {
        var po = Svc.Purchasing.Create(_s, new NewPurchaseOrder("CSU-SP-1", _tedarikci, BranchId: _sube,
            Lines: new[] { new NewPurchaseOrderLine(_mat, 10m, 12m) }));
        var satir = Svc.Purchasing.Lines(_s, po).Single();
        Svc.Purchasing.Receive(_s, po, new[] { new ReceiveLine(satir.Id, 4m) }, Op());

        var u = Svc.Invoices.CiftStokUyarilari(_s, InvoiceDirections.Purchase, _cariBagli, null, new[] { _mat });
        Assert.Single(u);
        Assert.Contains("CSU-SP-1", u[0]);
        Assert.Empty(Svc.Invoices.CiftStokUyarilari(_s, InvoiceDirections.Purchase, _cariBagli, null, new[] { _mat2 }));   // başka malzeme
        Assert.Empty(Svc.Invoices.CiftStokUyarilari(_s, InvoiceDirections.Purchase, _cariBagsiz, null, new[] { _mat }));   // köprüsüz cari
    }

    private object Fatura(bool stok, string? disNo = null) => new
    {
        direction = InvoiceDirections.Purchase, partyId = _cariBagsiz, operationId = Op(), externalNo = disNo,
        branchId = _sube, currency = "TRY", affectsStock = stok,
        lines = new[] { new { materialId = _mat, description = (string?)null, unit = (string?)null, quantity = 2m, unitPrice = 50m,
            discountRate = 0m, vatRate = 20m, withholdingRate = 0m } },
    };

    [Fact]
    public async Task CSU3_Fatura_Kontrol_Ucu()
    {
        Svc.Stock.ReceiveIn(_s, new[] { new StockLine(_mat, 1m) }, Op(), _sube, invoiceNo: "CSU-F-9");
        var r = await _client.PostAsJsonAsync("/api/invoices/check", Fatura(true, "CSU-F-9"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await ApiTestHost.JsonAsync(r);
        Assert.Contains("120.00 TRY", j.GetProperty("onayMetni").GetString());          // 2 × 50 + %20 KDV
        Assert.Contains("İKİNCİ kez", j.GetProperty("ciftStokMetni").GetString());
        Assert.Equal(1, j.GetProperty("uyarilar").GetArrayLength());

        var r2 = await ApiTestHost.JsonAsync(await _client.PostAsJsonAsync("/api/invoices/check", Fatura(false, "CSU-F-9")));
        Assert.Equal(0, r2.GetProperty("uyarilar").GetArrayLength());                   // stoğu etkilemeyecek → risk yok
        Assert.Equal(JsonValueKind.Null, r2.GetProperty("ciftStokMetni").ValueKind);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.CreateClient().PostAsJsonAsync("/api/invoices/check", Fatura(true))).StatusCode);
    }

    [Fact]
    public async Task CSU4_Tahsilat_Odeme_Onay_Metni()
    {
        var r = await _client.PostAsJsonAsync("/api/finance/entries/check", new
        {
            accountId = "x", txnType = FinanceTxnTypes.Payment, amount = 250.5m, operationId = Op(), partyId = _cariBagsiz,
            currency = "TRY", allocations = new[] { new { invoiceId = "f1", amount = 100m }, new { invoiceId = "f2", amount = 0m } },
        });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var m = (await ApiTestHost.JsonAsync(r)).GetProperty("onayMetni").GetString()!;
        Assert.StartsWith("Ödeme kaydedilsin mi?", m);
        Assert.Contains("250.50 TRY", m);
        Assert.Contains("1 fatura bu işlemle kapatılacak", m);
        Assert.Equal(m, FinanceTxnTypes.KayitOnayMetni(FinanceTxnTypes.Payment, 250.5m, "TRY", 1));   // masaüstüyle aynı metin
    }

    [Fact]
    public void CSU5_Iki_Platform_Kayittan_Once_Kontrol_Eder()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !File.Exists(Path.Combine(dir, "DepoWise.sln")); i++) dir = Directory.GetParent(dir)!.FullName;
        string Oku(params string[] p) => File.ReadAllText(Path.Combine(new[] { dir }.Concat(p).ToArray()));

        var vm = Oku("src", "DepoWise.Desktop", "ViewModels", "InvoicesViewModel.cs");
        Assert.True(vm.IndexOf("CiftStokUyarilari(", StringComparison.Ordinal) < vm.IndexOf("DesktopServices.Invoices.Create(", StringComparison.Ordinal));
        Assert.Contains("AffectsStock: stokEtkiler", vm);
        Assert.Contains("InvoiceService.KayitOnayMetni(", vm);
        var web = Oku("src", "DepoWise.Web", "Components", "Pages", "Invoices.razor");
        Assert.True(web.IndexOf("/api/invoices/check", StringComparison.Ordinal) < web.IndexOf("Api.PostAsync(\"/api/invoices\"", StringComparison.Ordinal));
        Assert.Contains("Govde(stokEtkiler)", web);
        var pvm = Oku("src", "DepoWise.Desktop", "ViewModels", "PaymentsViewModel.cs");
        Assert.True(pvm.IndexOf("FinanceTxnTypes.KayitOnayMetni(", StringComparison.Ordinal) < pvm.IndexOf("DesktopServices.Finance.Add(", StringComparison.Ordinal));
        var pweb = Oku("src", "DepoWise.Web", "Components", "Pages", "Payments.razor");
        Assert.True(pweb.IndexOf("/api/finance/entries/check", StringComparison.Ordinal) < pweb.IndexOf("Api.PostAsync(\"/api/finance/entries\"", StringComparison.Ordinal));
    }
}
