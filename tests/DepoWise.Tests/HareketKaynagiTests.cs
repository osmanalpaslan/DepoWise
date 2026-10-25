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
/// ═══ M-56 2/2 (AR-GE 2026-10-25, F-090) — STOK HAREKETİNDEN KAYNAĞA GİTME ═══
///  HK1 — Kaynak türleri: siparişli mal kabul → sipariş · talebe bağlı belge → talep · iş emri bağı → iş emri ·
///        stoklu fatura → fatura · bakım tüketimi (not = bakım) → araç / ekipman bakımı; bağsız belge → kaynak yok
///  HK2 — Firma ayrımı: başka firmanın belgesi/bakımı HİÇ bulunmaz (etiket bile sızmaz)
///  HK3 — Yetki: stok görüntüleme yoksa reddedilir; hedef ekranı göremeyen kullanıcıya etiket döner ama gitme kapalı
///  HK4 — API ucu: masaüstüyle aynı sonuç (web yolu, erişim), oturumsuz istek reddedilir
///  HK5 — İki platform kaynak kilidi (satır düğmesi, derin bağlantı hedefleri)
/// </summary>
[Collection("PostgresSchema")]
public class HareketKaynagiTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "HKY-A", Other = "HKY-B", Admin = "hky_admin", Pass = "Test!2026";
    private HttpClient _client = null!;
    private SessionContext _s = null!, _b = null!;
    private string _sube = "", _mat = "", _cari = "", _tedarikci = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        Firma(Company); Firma(Other);
        var uid = Svc.Users.EnsureInitialAdmin(Company, Admin, Pass, RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var uidB = Svc.Users.EnsureInitialAdmin(Other, "hky_b", Pass, RoleKeys.CompanyAdmin);
        _b = new SessionContext(uidB, Other, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _sube = Svc.Branches.Create(_s, new NewBranch("HKY Depo"));
        _mat = Svc.Materials.Create(_s, new NewMaterial("HKY-M1", "Filtre"));
        _tedarikci = "hky-sup-1";
        Exec($"INSERT INTO suppliers(id,company_id,name,created_at,updated_at) VALUES('{_tedarikci}','{Company}','HKY Tedarik',1,1);");
        _cari = Svc.Parties.Create(_s, new NewParty("HKY-C1", "HKY Tedarik A.Ş.", PartyTypes.Supplier, SupplierId: _tedarikci));
        _client = await _host.LoginAsync(Admin, Pass, Company);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();
    private ServerServices Svc => _host.Services.GetRequiredService<ServerServices>();
    private static string Op() => Guid.NewGuid().ToString("N");

    private void Firma(string id) => Exec(
        "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
        $"VALUES('{id}','{id}',1,1,1,0,5,20,5) ON CONFLICT(id) DO NOTHING;");

    private void Exec(string sql)
    {
        using var c = Svc.Factory.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
    }

    private string Tek(string sql)
    {
        using var c = Svc.Factory.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql;
        return (string)cmd.ExecuteScalar()!;
    }

    private string Giris(SessionContext s, string mat) => Svc.Stock.ReceiveIn(s, new[] { new StockLine(mat, 5m) }, Op()).DocumentId;

    /// <summary>Araç + bakım tanımı + bakım kaydı (bakım tüketimi hareketinin notu bu kimliği taşır).</summary>
    private (string Bakim, string Arac) AracBakimi(string firma, string kod)
    {
        string arac = Op(), tanim = Op(), bakim = Op();
        Exec($"INSERT INTO vehicles(id, company_id, internal_code, created_at, updated_at) VALUES('{arac}','{firma}','{kod}',1,1);");
        Exec($"INSERT INTO maintenance_definitions(id, company_id, name, created_at, updated_at) VALUES('{tanim}','{firma}','Yağ değişimi',1,1);");
        Exec("INSERT INTO vehicle_maintenances(id, company_id, vehicle_id, maintenance_def_id, operation_id, created_at, updated_at) " +
             $"VALUES('{bakim}','{firma}','{arac}','{tanim}','{Op()}',1,1);");
        return (bakim, arac);
    }

    private (string Bakim, string Ekipman) EkipmanBakimi(string firma, string kod)
    {
        string ekipman = Op(), tanim = Op(), bakim = Op();
        Exec($"INSERT INTO equipment(id, company_id, code, name, created_at, updated_at) VALUES('{ekipman}','{firma}','{kod}','Jeneratör',1,1);");
        Exec($"INSERT INTO maintenance_definitions(id, company_id, name, created_at, updated_at) VALUES('{tanim}','{firma}','Filtre bakımı',1,1);");
        Exec("INSERT INTO equipment_maintenances(id, company_id, equipment_id, maintenance_def_id, operation_id, created_at, updated_at) " +
             $"VALUES('{bakim}','{firma}','{ekipman}','{tanim}','{Op()}',1,1);");
        return (bakim, ekipman);
    }

    [Fact]
    public void HK1_Kaynak_Turleri()
    {
        // Sipariş → mal kabul
        var po = Svc.Purchasing.Create(_s, new NewPurchaseOrder("HKY-SP-1", _tedarikci, BranchId: _sube,
            Lines: new[] { new NewPurchaseOrderLine(_mat, 10m, 12m) }));
        Svc.Purchasing.Receive(_s, po, new[] { new ReceiveLine(Svc.Purchasing.Lines(_s, po).Single().Id, 4m) }, Op());
        var kabul = Tek($"SELECT id FROM stock_documents WHERE company_id='{Company}' AND purchase_order_id='{po}';");
        var k = Svc.Stock.KaynakBul(_s, kabul, "in", null)!;
        Assert.Equal(("siparis", "purchasing", po, "/purchasing?ac=" + po, true), (k.Tur, k.EkranKey, k.HedefId, k.WebYolu, k.Erisim));
        Assert.Contains("HKY-SP-1", k.Etiket);

        // Talep
        var talep = Svc.Requests.Create(_s, new DepoWise.Infrastructure.Requests.NewRequest(
            new[] { new DepoWise.Infrastructure.Requests.RequestItemInput(_mat, 1m) }));
        var tb = Giris(_s, _mat);
        Exec($"UPDATE stock_documents SET request_id='{talep.Id}' WHERE id='{tb}';");
        k = Svc.Stock.KaynakBul(_s, tb, "transfer", null)!;
        Assert.Equal(("talep", "requests:form", talep.Id, "/requests?ac=" + talep.Id), (k.Tur, k.EkranKey, k.HedefId, k.WebYolu));
        Assert.Contains(talep.DocNo, k.Etiket);

        // İş emri
        var wo = Svc.WorkOrders.Create(_s, new DepoWise.Infrastructure.WorkOrders.NewWorkOrder("HKY-IE-1", "Bakım işi"));
        var wb = Giris(_s, _mat);
        Exec("INSERT INTO work_order_links(id, work_order_id, company_id, entity_type, entity_id, created_at, updated_at) " +
             $"VALUES('{Op()}','{wo}','{Company}','stock_document','{wb}',1,1);");
        k = Svc.Stock.KaynakBul(_s, wb, "out", null)!;
        Assert.Equal(("is_emri", "work_orders", wo, "/work-orders?ac=" + wo), (k.Tur, k.EkranKey, k.HedefId, k.WebYolu));
        Assert.Contains("HKY-IE-1", k.Etiket);

        // Stoklu fatura (fatura kendi stok belgesini üretir)
        var fa = Svc.Invoices.Create(_s, new NewInvoice(InvoiceDirections.Purchase, _cari,
            new[] { new NewInvoiceLine(_mat, null, null, 2m, 10m) }, Op(), BranchId: _sube));
        Assert.NotNull(fa.StockDocumentId);
        k = Svc.Stock.KaynakBul(_s, fa.StockDocumentId, "in", null)!;
        Assert.Equal(("fatura", "invoices", "fatura:" + fa.Id, "/invoices?ac=" + fa.Id), (k.Tur, k.EkranKey, k.HedefId, k.WebYolu));
        Assert.Contains(fa.InvoiceNo, k.Etiket);

        // Bakım tüketimi: belgesiz, not = bakım kimliği
        var (bakim, arac) = AracBakimi(Company, "HKY-ARAC");
        k = Svc.Stock.KaynakBul(_s, null, "usage", bakim)!;
        Assert.Equal(("arac_bakimi", "maintenance:records", $"bakim:{bakim}:{arac}", "/maintenance?view=" + bakim + "&arac=" + arac), (k.Tur, k.EkranKey, k.HedefId, k.WebYolu));
        Assert.Contains("HKY-ARAC", k.Etiket);
        Assert.Equal("arac_bakimi", Svc.Stock.KaynakBul(_s, null, "usage_reverse", bakim)!.Tur);
        var (ebakim, ekipman) = EkipmanBakimi(Company, "HKY-EKP");
        k = Svc.Stock.KaynakBul(_s, null, "usage", ebakim)!;
        Assert.Equal(("ekipman_bakimi", $"ekbakim:{ebakim}:{ekipman}", "/maintenance?ekview=" + ebakim), (k.Tur, k.HedefId, k.WebYolu));

        // Bağsız belge / bakım dışı türde not → kaynak yok
        Assert.Null(Svc.Stock.KaynakBul(_s, Giris(_s, _mat), "in", null));
        Assert.Null(Svc.Stock.KaynakBul(_s, null, "in", bakim));   // not yalnız bakım tüketiminde bakım kimliğidir
        Assert.Null(Svc.Stock.KaynakBul(_s, null, null, null));
    }

    [Fact]
    public void HK2_Firma_Ayrimi()
    {
        var matB = Svc.Materials.Create(_b, new NewMaterial("HKB-M1", "B malzemesi"));
        var wo = Svc.WorkOrders.Create(_b, new DepoWise.Infrastructure.WorkOrders.NewWorkOrder("HKB-IE-1", "B işi"));
        var wb = Giris(_b, matB);
        Exec("INSERT INTO work_order_links(id, work_order_id, company_id, entity_type, entity_id, created_at, updated_at) " +
             $"VALUES('{Op()}','{wo}','{Other}','stock_document','{wb}',1,1);");
        Assert.Equal("is_emri", Svc.Stock.KaynakBul(_b, wb, "out", null)!.Tur);   // kendi firmasında bulunur
        Assert.Null(Svc.Stock.KaynakBul(_s, wb, "out", null));                     // başka firmadan HİÇ bulunmaz

        var (bakim, _) = AracBakimi(Other, "HKB-ARAC");
        var (ebakim, _) = EkipmanBakimi(Other, "HKB-EKP");
        Assert.NotNull(Svc.Stock.KaynakBul(_b, null, "usage", bakim));
        Assert.Null(Svc.Stock.KaynakBul(_s, null, "usage", bakim));
        Assert.Null(Svc.Stock.KaynakBul(_s, null, "usage", ebakim));
    }

    private SessionContext Kisitli(params ModulePermission[] p) => new("hky-staff", Company, new[] { RoleKeys.Staff }, new PermissionSet(p));

    [Fact]
    public void HK3_Yetki()
    {
        var wo = Svc.WorkOrders.Create(_s, new DepoWise.Infrastructure.WorkOrders.NewWorkOrder("HKY-IE-9", "Yetki işi"));
        var wb = Giris(_s, _mat);
        Exec("INSERT INTO work_order_links(id, work_order_id, company_id, entity_type, entity_id, created_at, updated_at) " +
             $"VALUES('{Op()}','{wo}','{Company}','stock_document','{wb}',1,1);");
        Assert.ThrowsAny<Exception>(() => Svc.Stock.KaynakBul(Kisitli(), wb, "out", null));   // stok görüntüleme yok

        var yalnizStok = Svc.Stock.KaynakBul(Kisitli(new ModulePermission("stock", true, false, false, false)), wb, "out", null)!;
        Assert.False(yalnizStok.Erisim);                                                        // iş emri ekranı kapalı
        Assert.Contains("HKY-IE-9", yalnizStok.Etiket);
        var ikisi = Svc.Stock.KaynakBul(Kisitli(new ModulePermission("stock", true, false, false, false),
            new ModulePermission("work_orders", true, false, false, false)), wb, "out", null)!;
        Assert.True(ikisi.Erisim);
    }

    [Fact]
    public async Task HK4_Api()
    {
        var (bakim, arac) = AracBakimi(Company, "HKY-API");
        var j = await _client.GetFromJsonAsync<JsonElement>($"/api/stock/movements/source?type=usage&note={bakim}");
        Assert.True(j.GetProperty("found").GetBoolean());
        Assert.Equal("arac_bakimi", j.GetProperty("tur").GetString());
        Assert.Equal("/maintenance?view=" + bakim + "&arac=" + arac, j.GetProperty("webYolu").GetString());
        Assert.True(j.GetProperty("erisim").GetBoolean());

        j = await _client.GetFromJsonAsync<JsonElement>("/api/stock/movements/source?documentId=" + Giris(_s, _mat) + "&type=in");
        Assert.False(j.GetProperty("found").GetBoolean());

        var anonim = _host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.GetAsync($"/api/stock/movements/source?type=usage&note={bakim}")).StatusCode);
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void HK5_Iki_Platform()
    {
        var vm = Oku("src/DepoWise.Desktop/ViewModels/StockMovementsViewModel.cs");
        Assert.Contains("DesktopServices.Stock.KaynakBul(_session, row.DocumentId, row.MovementType, row.Note);", vm);
        Assert.Contains("if (!k.Erisim) {", vm);
        Assert.Contains("ShellViewModel.Current?.NavigateTo(k.EkranKey, k.HedefId);", vm);
        Assert.Contains("Command=\"{Binding $parent[UserControl].((vm:StockMovementsViewModel)DataContext).KaynagaGitCommand}\"",
            Oku("src/DepoWise.Desktop/Views/StockMovementsView.axaml"));
        // Masaüstü hedefleri derin bağlantıyı karşılar
        foreach (var f in new[] { "PurchasingViewModel", "WorkOrdersViewModel", "RequestsViewModel" })
            Assert.Contains("IDeepLinkTarget", Oku($"src/DepoWise.Desktop/ViewModels/{f}.cs"));
        Assert.Contains("entityId.StartsWith(\"fatura:\", StringComparison.Ordinal)", Oku("src/DepoWise.Desktop/ViewModels/InvoicesViewModel.cs"));
        var bakim = Oku("src/DepoWise.Desktop/ViewModels/MaintenanceViewModel.cs");
        Assert.Contains("p[0] == \"bakim\"", bakim);
        Assert.Contains("p[0] == \"ekbakim\"", bakim);

        var web = Oku("src/DepoWise.Web/Components/Pages/StockMovements.razor");
        Assert.Contains("\"/api/stock/movements/source?documentId=\"", web);
        Assert.Contains("if (!Bool(k, \"erisim\"))", web);
        Assert.Contains("Nav.NavigateTo(Str(k, \"webYolu\"));", web);
        foreach (var p in new[] { "Invoices", "Purchasing", "WorkOrders", "Requests" })
            Assert.Contains("[SupplyParameterFromQuery(Name = \"ac\")]", Oku($"src/DepoWise.Web/Components/Pages/{p}.razor"));
        Assert.Contains("[SupplyParameterFromQuery(Name = \"ekview\")]", Oku("src/DepoWise.Web/Components/Pages/Maintenance.razor"));
        Assert.Contains("svc.Stock.KaynakBul(s, documentId, type, note)", Oku("src/DepoWise.Api/Program.cs"));
    }
}
