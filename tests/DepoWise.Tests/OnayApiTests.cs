using DepoWise.Infrastructure.Database;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 2 — HİYERARŞİ + ONAY API SÖZLEŞMESİ (GERÇEK HTTP) ═══
///
/// Kilitlenenler: firma gövdeden ALINMAZ · başka firmanın hiyerarşisine/adımına erişilemez (IDOR) ·
/// derinlik/döngü kapıları API üzerinden de zorlanır · onay adımı yalnız sahibince işlenir ·
/// <b>onaysız mal kabul API'dan da engellenir</b> (ADR-188 §1) · ayna hiyerarşiyi doğru adlarla taşır.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-geneli ortam değişkeni yazar → seri koşmalı
public class OnayApiTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CoA = "ONAPI-A";
    private const string CoB = "ONAPI-B";
    private const string Pass = "Test!2026";

    private HttpClient _a = null!, _b = null!;
    private string _adminA = "", _adminB = "", _ustA = "";
    private ServerServices _svc = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        _adminA = _svc.Users.EnsureInitialAdmin(CoA, "on_super", Pass, RoleKeys.SuperAdmin);
        var sa = new SessionContext(_adminA, CoA, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        var subeA = _svc.Branches.Create(sa, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _a = await _host.LoginAsync("on_super", Pass, CoA, subeA);

        _adminB = _svc.Users.EnsureInitialAdmin(CoB, "on_super_b", Pass, RoleKeys.SuperAdmin);
        var sb = new SessionContext(_adminB, CoB, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        var subeB = _svc.Branches.Create(sb, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _b = await _host.LoginAsync("on_super_b", Pass, CoB, subeB);

        _ustA = Kullanici(CoA, "on_ust");
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private string Kullanici(string co, string username)
    {
        var id = Guid.NewGuid().ToString("N");
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO users(id,company_id,username,password_hash,is_active,created_at,updated_at,version,is_deleted) " +
            "VALUES(@i,@c,@u,'x',1,1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.AddWithValue("@c", co);
        cmd.AddWithValue("@u", username);
        cmd.ExecuteNonQuery();
        return id;
    }

    /// <summary>OA01 — Hiyerarşi CRUD API'dan çalışır; zincir ucu doğru sırayı verir.</summary>
    [Fact]
    public async Task OA01_Hiyerarsi_Api_Calisir()
    {
        var r = await _a.PutAsJsonAsync($"/api/hierarchy/{_adminA}", new { managerUserId = _ustA });
        r.EnsureSuccessStatusCode();

        var liste = await ApiTestHost.JsonAsync(await _a.GetAsync("/api/hierarchy"));
        Assert.Contains(liste.EnumerateArray(), e => e.GetProperty("userId").GetString() == _adminA);

        var zincir = await ApiTestHost.JsonAsync(await _a.GetAsync($"/api/hierarchy/{_adminA}/chain"));
        Assert.Equal(_ustA, zincir.EnumerateArray().Single().GetString());

        (await _a.DeleteAsync($"/api/hierarchy/{_adminA}")).EnsureSuccessStatusCode();
        Assert.Empty((await ApiTestHost.JsonAsync(await _a.GetAsync($"/api/hierarchy/{_adminA}/chain"))).EnumerateArray());
    }

    /// <summary>OA02 — Self-reference ve çapraz firma API'dan da reddedilir; B, A'nın hiyerarşisini
    /// göremez (tenant/IDOR).</summary>
    [Fact]
    public async Task OA02_Api_Kapilari()
    {
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _a.PutAsJsonAsync($"/api/hierarchy/{_adminA}", new { managerUserId = _adminA })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _a.PutAsJsonAsync($"/api/hierarchy/{_adminA}", new { managerUserId = _adminB })).StatusCode);

        (await _a.PutAsJsonAsync($"/api/hierarchy/{_adminA}", new { managerUserId = _ustA })).EnsureSuccessStatusCode();
        var listeB = await ApiTestHost.JsonAsync(await _b.GetAsync("/api/hierarchy"));
        Assert.Empty(listeB.EnumerateArray());
    }

    /// <summary>OA03 — <b>Onay adımı API'dan yalnız SAHİBİNCE işlenir</b>; uydurma adım kimliği ve
    /// başka firmanın adımı reddedilir.</summary>
    [Fact]
    public async Task OA03_Onay_Adimi_Sahiplik_Ve_IDOR()
    {
        (await _a.PutAsJsonAsync($"/api/hierarchy/{_adminA}", new { managerUserId = _ustA })).EnsureSuccessStatusCode();
        var talep = TalepAc();

        // A (süreci başlatan) adım sahibi DEĞİL → kendi listesinde adım yok.
        Assert.Empty((await ApiTestHost.JsonAsync(await _a.GetAsync("/api/approvals/mine"))).EnumerateArray());

        using var conn = _svc.Factory.Create();
        var inst = DepoWise.Infrastructure.Approvals.ApprovalService.OpenInstanceId(
            conn, null, CoA, DepoWise.Application.Approvals.ApprovalEntityTypes.MaterialRequest, talep)!;
        var stepId = (await ApiTestHost.JsonAsync(await _a.GetAsync($"/api/approvals/{inst}/steps")))
            .EnumerateArray().First().GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.Forbidden,
            (await _a.PostAsJsonAsync($"/api/approvals/steps/{stepId}/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _b.PostAsJsonAsync($"/api/approvals/steps/{stepId}/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _a.PostAsJsonAsync("/api/approvals/steps/uydurma/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _b.GetAsync($"/api/approvals/{inst}/steps")).StatusCode);

        // Zincirli talep ESKİ uçtan da onaylanamaz (bypass kapısı API'da da geçerli).
        // Ortak hata modeli: iş kuralı ihlali → 400 (409 yalnız düzenleme kilidi içindir).
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _a.PostAsJsonAsync($"/api/requests/{talep}/approve", new { })).StatusCode);
    }

    /// <summary>OA04 — <b>ADR-188 §1:</b> onay tamamlanmadan mal kabul API'dan da REDDEDİLİR;
    /// onaydan sonra onay kapısı engellemez ve <c>purchase_orders.status</c> DEĞİŞMEZ (§2).</summary>
    [Fact]
    public async Task OA04_Onaysiz_Mal_Kabul_Api_Engeli()
    {
        (await _a.PutAsJsonAsync($"/api/hierarchy/{_adminA}", new { managerUserId = _ustA })).EnsureSuccessStatusCode();
        var (orderId, lineId) = Siparis();

        var red = await _a.PostAsJsonAsync($"/api/purchasing/{orderId}/receive",
            new { operationId = "api-op-1", lines = new[] { new { lineId, quantity = 1m } } });
        Assert.NotEqual(HttpStatusCode.OK, red.StatusCode);
        Assert.Contains("onay", (await red.Content.ReadAsStringAsync()).ToLowerInvariant());
        Assert.Equal("open", SiparisDurumu(orderId));

        // Onay: adım sahibi üst kullanıcı.
        using var conn = _svc.Factory.Create();
        var inst = DepoWise.Infrastructure.Approvals.ApprovalService.OpenInstanceId(
            conn, null, CoA, DepoWise.Application.Approvals.ApprovalEntityTypes.PurchaseOrder, orderId)!;
        var ustOturum = new SessionContext(_ustA, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var stepId = _svc.Approvals.Steps(ustOturum, inst).Single().Id;
        _svc.Approvals.Approve(ustOturum, stepId);

        var sonra = await _a.PostAsJsonAsync($"/api/purchasing/{orderId}/receive",
            new { operationId = "api-op-2", lines = new[] { new { lineId, quantity = 1m } } });
        var govde = (await sonra.Content.ReadAsStringAsync()).ToLowerInvariant();
        Assert.DoesNotContain("onay bekliyor", govde);
        Assert.NotEqual("cancelled", SiparisDurumu(orderId));
    }

    /// <summary>OA05 — <b>AYNA:</b> <c>/api/lookups/sync</c> hiyerarşiyi masaüstünün OKUDUĞU adlarla
    /// taşır; onay tabloları aynada YOKTUR (İK-9: onay yalnız çevrimiçi).</summary>
    [Fact]
    public async Task OA05_Lookup_Aynasi_Hiyerarsiyi_Tasir_Onayi_Tasimaz()
    {
        (await _a.PutAsJsonAsync($"/api/hierarchy/{_adminA}", new { managerUserId = _ustA })).EnsureSuccessStatusCode();

        var kok = await ApiTestHost.JsonAsync(await _a.GetAsync("/api/lookups/sync"));
        var satir = kok.GetProperty("userHierarchy").EnumerateArray()
            .Single(e => e.GetProperty("user_id").GetString() == _adminA);
        foreach (var alan in new[] { "id", "user_id", "manager_user_id" })
            Assert.True(satir.EnumerateObject().Any(p => p.Name == alan),
                $"userHierarchy: '{alan}' alanı yanıtta yok. Gelen: " +
                string.Join(", ", satir.EnumerateObject().Select(p => p.Name)));
        Assert.Equal(_ustA, satir.GetProperty("manager_user_id").GetString());

        // Onay yapıları AYNADA YOK.
        foreach (var yasak in new[] { "approvalInstance", "approvalStep", "approvals" })
            Assert.False(kok.EnumerateObject().Any(p => p.Name == yasak),
                $"Onay verisi lookup aynasında olmamalı: {yasak}");

        // Ayna tenant süzgeçli.
        var kokB = await ApiTestHost.JsonAsync(await _b.GetAsync("/api/lookups/sync"));
        Assert.Empty(kokB.GetProperty("userHierarchy").EnumerateArray());
    }

    // ── yardımcılar ────────────────────────────────────────────────────────────────────────

    private string TalepAc()
    {
        var sa = new SessionContext(_adminA, CoA, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        return _svc.Requests.Create(sa, new DepoWise.Infrastructure.Requests.NewRequest(
            new[] { new DepoWise.Infrastructure.Requests.RequestItemInput(Malzeme(), 1m) },
            SubmitImmediately: true)).Id;
    }

    private (string OrderId, string LineId) Siparis()
    {
        var sa = new SessionContext(_adminA, CoA, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        var mat = Malzeme();
        var sube = _svc.Branches.List(sa, CoA).First().Id;
        var id = _svc.Purchasing.Create(sa, new DepoWise.Infrastructure.Purchasing.NewPurchaseOrder(
            OrderNo: "OA-" + Guid.NewGuid().ToString("N")[..6], BranchId: sube,
            Lines: new[] { new DepoWise.Infrastructure.Purchasing.NewPurchaseOrderLine(mat, 5m) }));

        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM purchase_order_lines WHERE order_id=@o AND is_deleted=0;";
        cmd.AddWithValue("@o", id);
        return (id, (string)cmd.ExecuteScalar()!);
    }

    private string Malzeme()
    {
        var id = Guid.NewGuid().ToString("N");
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO materials(id,company_id,code,name,created_at,updated_at,version,is_deleted) VALUES(@i,@c,@k,@k,1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.AddWithValue("@c", CoA);
        cmd.AddWithValue("@k", "M" + id[..6]);
        cmd.ExecuteNonQuery();
        return id;
    }

    private string SiparisDurumu(string orderId)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM purchase_orders WHERE id=@i;";
        cmd.AddWithValue("@i", orderId);
        return (string)cmd.ExecuteScalar()!;
    }
}
