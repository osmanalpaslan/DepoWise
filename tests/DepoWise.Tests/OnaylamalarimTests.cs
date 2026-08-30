using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Approvals;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 3 (ADR-189) — "ONAYLAMALARIM" SÖZLEŞMESİ (GERÇEK HTTP) ═══
///
/// Kilitlenenler: liste YALNIZ oturumdaki kullanıcının SIRASI GELMİŞ adımlarını verir · başkasının
/// kuyruğu istenemez (uçta parametre bile yok) · çapraz firma sızıntısı yok · onay/ret sonrası satır
/// listeden düşer · Satın Alma onayı <c>Receive</c> kapısını açar · <b>listede görünmek onaylama
/// yetkisi değildir</b> · aynı adıma iki eşzamanlı karardan yalnız biri geçer.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-geneli ortam değişkeni yazar → seri koşmalı
public class OnaylamalarimTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CoA = "ONYM-A";
    private const string CoB = "ONYM-B";
    private const string Pass = "Test!2026";

    private HttpClient _a = null!, _b = null!, _ust = null!;
    private string _adminA = "", _adminB = "", _ustA = "", _ustB = "";
    private ServerServices _svc = null!;
    private string _subeA = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        _adminA = _svc.Users.EnsureInitialAdmin(CoA, "onym_super", Pass, RoleKeys.SuperAdmin);
        var sa = new SessionContext(_adminA, CoA, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        _subeA = _svc.Branches.Create(sa, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _a = await _host.LoginAsync("onym_super", Pass, CoA, _subeA);

        _adminB = _svc.Users.EnsureInitialAdmin(CoB, "onym_super_b", Pass, RoleKeys.SuperAdmin);
        var sb = new SessionContext(_adminB, CoB, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        var subeB = _svc.Branches.Create(sb, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _b = await _host.LoginAsync("onym_super_b", Pass, CoB, subeB);

        // Gerçek giriş yapabilen ÜST kullanıcı — onay adımlarının sahibi olacak.
        _ustA = _svc.Users.EnsureInitialAdmin(CoA, "onym_ust", Pass, RoleKeys.CompanyAdmin);
        _ust = await _host.LoginAsync("onym_ust", Pass, CoA, _subeA);
        _ustB = _svc.Users.EnsureInitialAdmin(CoA, "onym_ust2", Pass, RoleKeys.CompanyAdmin);

        // Zincir: admin → ust → ust2 (iki adım).
        _svc.Hierarchy.SetManager(sa, _adminA, _ustA);
        _svc.Hierarchy.SetManager(sa, _ustA, _ustB);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private SessionContext Sa => new(_adminA, CoA, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);

    // ══════════════════════ LİSTE ══════════════════════

    /// <summary>OY01 — Liste yalnız SIRASI GELMİŞ adımı ve MEVCUT modelden gelen alanları verir
    /// (uydurma alan yok). Süreci başlatan kişi kendi listesinde bu adımı GÖRMEZ.</summary>
    [Fact]
    public async Task OY01_Liste_Dogru_Kullaniciya_Dogru_Alanlarla_Gelir()
    {
        var talep = TalepAc();
        var docNo = TekMetin($"SELECT doc_no FROM material_requests WHERE id='{talep}';");

        // Süreci başlatan admin: adım sahibi DEĞİL → listesi boş.
        Assert.Empty((await ApiTestHost.JsonAsync(await _a.GetAsync("/api/approvals/mine"))).EnumerateArray());

        var liste = (await ApiTestHost.JsonAsync(await _ust.GetAsync("/api/approvals/mine"))).EnumerateArray().ToList();
        var satir = Assert.Single(liste);
        Assert.Equal(ApprovalEntityTypes.MaterialRequest, satir.GetProperty("entityType").GetString());
        Assert.Equal("Malzeme Talebi", satir.GetProperty("entityLabel").GetString());
        Assert.Equal(docNo, satir.GetProperty("docNo").GetString());
        Assert.Equal(talep, satir.GetProperty("entityId").GetString());
        Assert.Equal(1, satir.GetProperty("stepNo").GetInt64());
        Assert.Equal(2, satir.GetProperty("totalSteps").GetInt64());     // zincir 2 adım
        Assert.Equal("1 / 2", satir.GetProperty("stepLabel").GetString());
        Assert.Equal(_adminA, satir.GetProperty("startedBy").GetString());
        Assert.True(satir.GetProperty("entityDate").GetInt64() > 0);
    }

    /// <summary>OY02 — <b>Başkasının kuyruğu istenemez:</b> uçta kullanıcı parametresi YOKTUR ve
    /// çapraz firma hiçbir satır görmez (tenant + IDOR).</summary>
    [Fact]
    public async Task OY02_Baskasinin_Kuyrugu_Istenemez()
    {
        _ = TalepAc();

        // Sorgu parametresiyle başkasının listesini almaya çalışmak SONUCU DEĞİŞTİRMEZ.
        foreach (var yol in new[]
                 {
                     $"/api/approvals/mine?userId={_ustA}",
                     $"/api/approvals/mine?approverUserId={_ustA}",
                     $"/api/approvals/mine?companyId={CoA}",
                 })
            Assert.Empty((await ApiTestHost.JsonAsync(await _a.GetAsync(yol))).EnumerateArray());

        Assert.Empty((await ApiTestHost.JsonAsync(await _b.GetAsync("/api/approvals/mine"))).EnumerateArray());
        Assert.Single((await ApiTestHost.JsonAsync(await _ust.GetAsync("/api/approvals/mine"))).EnumerateArray());
    }

    /// <summary>OY03 — Sıra: 2. adım sahibi, 1. adım kapanmadan listesinde satır GÖRMEZ; 1. adım
    /// onaylanınca sıra ona geçer ve önceki sahibin listesi boşalır.</summary>
    [Fact]
    public async Task OY03_Sira_Gelmeden_Gorunmez_Onaydan_Sonra_Devreder()
    {
        _ = TalepAc();
        var ust2 = await _host.LoginAsync("onym_ust2", Pass, CoA, _subeA);
        Assert.Empty((await ApiTestHost.JsonAsync(await ust2.GetAsync("/api/approvals/mine"))).EnumerateArray());

        var stepId = (await ApiTestHost.JsonAsync(await _ust.GetAsync("/api/approvals/mine")))
            .EnumerateArray().Single().GetProperty("stepId").GetString()!;
        (await _ust.PostAsJsonAsync($"/api/approvals/steps/{stepId}/approve", new { })).EnsureSuccessStatusCode();

        Assert.Empty((await ApiTestHost.JsonAsync(await _ust.GetAsync("/api/approvals/mine"))).EnumerateArray());
        Assert.Single((await ApiTestHost.JsonAsync(await ust2.GetAsync("/api/approvals/mine"))).EnumerateArray());
    }

    // ══════════════════════ KARAR ══════════════════════

    /// <summary>OY04 — Zincir tamamlanınca talep onaylanır ve satır listeden düşer.</summary>
    [Fact]
    public async Task OY04_Onay_Sonrasi_Listeden_Duser_Talep_Onaylanir()
    {
        var talep = TalepAc();
        var ust2 = await _host.LoginAsync("onym_ust2", Pass, CoA, _subeA);

        await AdimiOnayla(_ust);
        await AdimiOnayla(ust2);

        Assert.Empty((await ApiTestHost.JsonAsync(await _ust.GetAsync("/api/approvals/mine"))).EnumerateArray());
        Assert.Empty((await ApiTestHost.JsonAsync(await ust2.GetAsync("/api/approvals/mine"))).EnumerateArray());
        Assert.Equal("approved", TekMetin($"SELECT status FROM material_requests WHERE id='{talep}';"));
    }

    /// <summary>OY05 — Ret: gerekçe ZORUNLU; ret sonrası satır listeden düşer, talep reddedilir ve
    /// gerekçe kayıtta GÖRÜNÜR kalır (İK-10). Reddedilen talep yeniden gönderilemez (İK-4).</summary>
    [Fact]
    public async Task OY05_Ret_Gerekce_Zorunlu_Ve_Listeden_Duser()
    {
        var talep = TalepAc();
        var stepId = (await ApiTestHost.JsonAsync(await _ust.GetAsync("/api/approvals/mine")))
            .EnumerateArray().Single().GetProperty("stepId").GetString()!;

        Assert.Equal(HttpStatusCode.BadRequest,
            (await _ust.PostAsJsonAsync($"/api/approvals/steps/{stepId}/reject", new { reason = "  " })).StatusCode);

        (await _ust.PostAsJsonAsync($"/api/approvals/steps/{stepId}/reject",
            new { reason = "Bütçe yok" })).EnsureSuccessStatusCode();

        Assert.Empty((await ApiTestHost.JsonAsync(await _ust.GetAsync("/api/approvals/mine"))).EnumerateArray());
        Assert.Equal("rejected", TekMetin($"SELECT status FROM material_requests WHERE id='{talep}';"));
        Assert.Equal("Bütçe yok", TekMetin(
            $"SELECT reason FROM approval_step WHERE id='{stepId}';"));

        // İK-4: rejected uçtur — yeniden onaya gönderilemez.
        Assert.Throws<InvalidOperationException>(() => _svc.Requests.Submit(Sa, talep));
    }

    /// <summary>OY06 — <b>Listede görünmek onaylama yetkisi DEĞİLDİR:</b> ilgili modül yetkisi
    /// olmayan kullanıcı satırı görse bile karar veremez (kapı serviste).</summary>
    [Fact]
    public async Task OY06_Listede_Gorunmek_Yetki_Degildir()
    {
        _ = TalepAc();
        var satir = (await ApiTestHost.JsonAsync(await _ust.GetAsync("/api/approvals/mine")))
            .EnumerateArray().Single();
        var stepId = satir.GetProperty("stepId").GetString()!;

        // Aynı adım sahibi ama request_approval yetkisi OLMAYAN oturum → reddedilir.
        var yetkisiz = new SessionContext(_ustA, CoA, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _svc.Approvals.Approve(yetkisiz, stepId));

        // Adım sahibi OLMAYAN kullanıcı, yetkisi olsa bile karar veremez.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _a.PostAsJsonAsync($"/api/approvals/steps/{stepId}/approve", new { })).StatusCode);
    }

    /// <summary>OY07 — <b>EŞZAMANLILIK:</b> aynı adıma iki karar denemesinden yalnız İLKİ geçer;
    /// ikincisi reddedilir (LWW yok). UI değil, sunucudaki atomik geçiş karar verir.</summary>
    [Fact]
    public async Task OY07_Ayni_Adima_Ikinci_Karar_Reddedilir()
    {
        _ = TalepAc();
        var stepId = (await ApiTestHost.JsonAsync(await _ust.GetAsync("/api/approvals/mine")))
            .EnumerateArray().Single().GetProperty("stepId").GetString()!;

        (await _ust.PostAsJsonAsync($"/api/approvals/steps/{stepId}/approve", new { })).EnsureSuccessStatusCode();
        var ikinci = await _ust.PostAsJsonAsync($"/api/approvals/steps/{stepId}/approve", new { });
        Assert.NotEqual(HttpStatusCode.OK, ikinci.StatusCode);

        var red = await _ust.PostAsJsonAsync($"/api/approvals/steps/{stepId}/reject", new { reason = "geç kaldım" });
        Assert.NotEqual(HttpStatusCode.OK, red.StatusCode);
    }

    // ══════════════════════ SATIN ALMA ══════════════════════

    /// <summary>OY08 — Satın Alma bekleyen onayı listede görünür; zincir tamamlanana kadar
    /// <c>Receive</c> KAPALI, tamamlanınca onay kapısı engellemez (ADR-188 §1).</summary>
    [Fact]
    public async Task OY08_Satin_Alma_Listede_Gorunur_Ve_Receive_Kapisi_Acilir()
    {
        var (orderId, lineId) = Siparis();
        var orderNo = TekMetin($"SELECT order_no FROM purchase_orders WHERE id='{orderId}';");

        var satir = (await ApiTestHost.JsonAsync(await _ust.GetAsync("/api/approvals/mine")))
            .EnumerateArray().Single(e => e.GetProperty("entityType").GetString() == ApprovalEntityTypes.PurchaseOrder);
        Assert.Equal("Satın Alma", satir.GetProperty("entityLabel").GetString());
        Assert.Equal(orderNo, satir.GetProperty("docNo").GetString());

        // Onay tamamlanmadan mal kabul REDDEDİLİR.
        var red = await _a.PostAsJsonAsync($"/api/purchasing/{orderId}/receive",
            new { operationId = "oy-op-1", lines = new[] { new { lineId, quantity = 1m } } });
        Assert.Contains("onay", (await red.Content.ReadAsStringAsync()).ToLowerInvariant());

        // Zinciri tamamla (iki adım).
        await AdimiOnayla(_ust, ApprovalEntityTypes.PurchaseOrder);
        var ust2 = await _host.LoginAsync("onym_ust2", Pass, CoA, _subeA);
        await AdimiOnayla(ust2, ApprovalEntityTypes.PurchaseOrder);

        var sonra = await _a.PostAsJsonAsync($"/api/purchasing/{orderId}/receive",
            new { operationId = "oy-op-2", lines = new[] { new { lineId, quantity = 1m } } });
        Assert.DoesNotContain("onay bekliyor", (await sonra.Content.ReadAsStringAsync()).ToLowerInvariant());
        Assert.Equal("open", TekMetin($"SELECT status FROM purchase_orders WHERE id='{orderId}';"));
    }

    /// <summary>OY09 — PO reddedilirse mal kabul KALICI kapalıdır ve satır listeden düşer.</summary>
    [Fact]
    public async Task OY09_Reddedilen_PO_Receive_Kapali_Kalir()
    {
        var (orderId, lineId) = Siparis();
        var stepId = (await ApiTestHost.JsonAsync(await _ust.GetAsync("/api/approvals/mine")))
            .EnumerateArray().Single(e => e.GetProperty("entityType").GetString() == ApprovalEntityTypes.PurchaseOrder)
            .GetProperty("stepId").GetString()!;

        (await _ust.PostAsJsonAsync($"/api/approvals/steps/{stepId}/reject",
            new { reason = "Fiyat yüksek" })).EnsureSuccessStatusCode();

        Assert.Empty((await ApiTestHost.JsonAsync(await _ust.GetAsync("/api/approvals/mine"))).EnumerateArray());
        var red = await _a.PostAsJsonAsync($"/api/purchasing/{orderId}/receive",
            new { operationId = "oy-op-3", lines = new[] { new { lineId, quantity = 1m } } });
        // ⚠️ Türkçe "İ" harfi ToLowerInvariant ile birleşik noktaya dönüşür ("i̇") — bu yüzden
        // mesaj KÜÇÜLTÜLMEDEN aranır. (Ürün mesajı doğru; küçültme testi yanıltıyordu.)
        Assert.Contains("REDDEDİLDİ", await red.Content.ReadAsStringAsync());
    }

    // ══════════════════════ GERİYE UYUMLULUK / PERFORMANS ══════════════════════

    /// <summary>OY10 — <b>Zincirsiz akış bozulmadı:</b> hiyerarşisi olmayan kullanıcının talebinde
    /// süreç oluşmaz, listede satır çıkmaz ve eski tek-adımlı onay çalışmaya devam eder.</summary>
    [Fact]
    public async Task OY10_Zincirsiz_Eski_Akis_Bozulmaz()
    {
        // ust2'nin üstü yok → onun açtığı talepte zincir OLUŞMAZ.
        var ust2Oturum = new SessionContext(_ustB, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var talep = _svc.Requests.Create(ust2Oturum, new DepoWise.Infrastructure.Requests.NewRequest(
            new[] { new DepoWise.Infrastructure.Requests.RequestItemInput(Malzeme(), 1m) },
            SubmitImmediately: true)).Id;

        using (var conn = _svc.Factory.Create())
            Assert.Null(DepoWise.Infrastructure.Approvals.ApprovalService.OpenInstanceId(
                conn, null, CoA, ApprovalEntityTypes.MaterialRequest, talep));

        var ust2 = await _host.LoginAsync("onym_ust2", Pass, CoA, _subeA);
        Assert.Empty((await ApiTestHost.JsonAsync(await ust2.GetAsync("/api/approvals/mine"))).EnumerateArray());

        _svc.Requests.Approve(ust2Oturum, talep);            // eski tek-adımlı yol açık
        Assert.Equal("approved", TekMetin($"SELECT status FROM material_requests WHERE id='{talep}';"));
    }

    /// <summary>OY11 — <b>N+1 GUARD (§14):</b> liste kaç satır dönerse dönsün TEK sorgu ile üretilir.
    /// Satır sayısı arttıkça sorgu sayısı ARTMAMALIDIR.</summary>
    [Fact]
    public async Task OY11_Liste_Tek_Sorgu_Ile_Uretilir_N1_Yok()
    {
        for (int i = 0; i < 5; i++) _ = TalepAc();
        var oturum = new SessionContext(_ustA, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        // TEK satırlık ve ÇOK satırlık listede komut sayısı AYNI olmalı → satır başına sorgu yok.
        var sayan = new SayanFabrika(_svc.Factory);
        var olcum = new DepoWise.Infrastructure.Approvals.ApprovalService(sayan);

        sayan.Sifirla();
        var liste = olcum.MyPending(oturum);
        var cokSatirKomut = sayan.KomutSayisi;

        Assert.Equal(5, liste.Count);                        // sonuç DOĞRU (sayaç tek başına kanıt değil)
        Assert.True(cokSatirKomut == 1,
            $"Onay listesi {cokSatirKomut} komut çalıştırdı; N+1 var. Beklenen: 1 (satır sayısından bağımsız).");

        // Alanlar da tek sorgudan geliyor: belge no ve toplam adım dolu.
        Assert.All(liste, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.DocNo));
            Assert.Equal(2, r.TotalSteps);
        });
        await Task.CompletedTask;
    }

    // ── yardımcılar ────────────────────────────────────────────────────────────────────────

    private async Task AdimiOnayla(HttpClient c, string entityType = ApprovalEntityTypes.MaterialRequest)
    {
        var stepId = (await ApiTestHost.JsonAsync(await c.GetAsync("/api/approvals/mine")))
            .EnumerateArray().First(e => e.GetProperty("entityType").GetString() == entityType)
            .GetProperty("stepId").GetString()!;
        (await c.PostAsJsonAsync($"/api/approvals/steps/{stepId}/approve", new { })).EnsureSuccessStatusCode();
    }

    private string TalepAc()
        => _svc.Requests.Create(Sa, new DepoWise.Infrastructure.Requests.NewRequest(
            new[] { new DepoWise.Infrastructure.Requests.RequestItemInput(Malzeme(), 1m) },
            SubmitImmediately: true)).Id;

    private (string OrderId, string LineId) Siparis()
    {
        var mat = Malzeme();
        var id = _svc.Purchasing.Create(Sa, new DepoWise.Infrastructure.Purchasing.NewPurchaseOrder(
            OrderNo: "OY-" + Guid.NewGuid().ToString("N")[..6], BranchId: _subeA,
            Lines: new[] { new DepoWise.Infrastructure.Purchasing.NewPurchaseOrderLine(mat, 5m) }));
        return (id, TekMetin($"SELECT id FROM purchase_order_lines WHERE order_id='{id}';"));
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

    private string TekMetin(string sql)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar() as string ?? "";
    }
}
