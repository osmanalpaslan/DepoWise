using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 1 (ADR-187) — EKİP API SÖZLEŞMESİ (GERÇEK HTTP) ═══
///
/// Kilitlenenler:
///  • CRUD ve üyelik uçları gerçek HTTP hattı üzerinden çalışır.
///  • <b>Firma istemciden ALINMAZ:</b> gövdeye <c>companyId</c> konsa bile yok sayılır — kayıt daima
///    oturumun firmasına yazılır. Başka firmanın kaynağına erişim <b>403</b> ile kapanır (IDOR).
///  • <b>Ayna sözleşmesi:</b> <c>/api/lookups/sync</c> yanıtı <c>teams</c> ve <c>teamMembers</c>
///    dizilerini masaüstünün OKUDUĞU ADLARLA taşır (TSN dersi: anahtar adı yanlışsa alan sessizce
///    null okunur ve yerelde veri kaybolur).
///  • Yetkisiz kullanıcı ekip uçlarını kullanamaz.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-geneli ortam değişkeni yazar → seri koşmalı
public class EkipApiTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CoA = "EKAPI-A";
    private const string CoB = "EKAPI-B";
    private const string Pass = "Test!2026";

    private HttpClient _a = null!, _b = null!;
    private string _userA = "", _userB = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        var svc = _host.Services.GetRequiredService<ServerServices>();

        _userA = svc.Users.EnsureInitialAdmin(CoA, "ek_super", Pass, RoleKeys.SuperAdmin);
        var sa = new SessionContext(_userA, CoA, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        var subeA = svc.Branches.Create(sa, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _a = await _host.LoginAsync("ek_super", Pass, CoA, subeA);

        _userB = svc.Users.EnsureInitialAdmin(CoB, "ek_super_b", Pass, RoleKeys.SuperAdmin);
        var sb = new SessionContext(_userB, CoB, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        var subeB = svc.Branches.Create(sb, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _b = await _host.LoginAsync("ek_super_b", Pass, CoB, subeB);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private static async Task<string> IdAl(HttpResponseMessage r)
    {
        r.EnsureSuccessStatusCode();
        return (await ApiTestHost.JsonAsync(r)).GetProperty("id").GetString()!;
    }

    /// <summary>EKA01 — Ekip CRUD + üyelik uçları uçtan uca çalışır.</summary>
    [Fact]
    public async Task EKA01_Ekip_Crud_Ve_Uyelik_Calisir()
    {
        var id = await IdAl(await _a.PostAsJsonAsync("/api/teams", new { name = "API Ekip" }));

        var liste = await ApiTestHost.JsonAsync(await _a.GetAsync("/api/teams"));
        Assert.Contains(liste.EnumerateArray(), e => e.GetProperty("id").GetString() == id);

        // Üye ekle → lider ata (lider ancak ÜYE olduktan sonra atanabilir).
        var uye = await _a.PostAsJsonAsync($"/api/teams/{id}/members", new { userId = _userA });
        uye.EnsureSuccessStatusCode();

        var atama = await _a.PutAsJsonAsync($"/api/teams/{id}",
            new { name = "API Ekip", leadUserId = _userA, isActive = true });
        atama.EnsureSuccessStatusCode();

        var tek = await ApiTestHost.JsonAsync(await _a.GetAsync($"/api/teams/{id}"));
        Assert.Equal(_userA, tek.GetProperty("leadUserId").GetString());

        var uyeler = await ApiTestHost.JsonAsync(await _a.GetAsync($"/api/teams/{id}/members"));
        Assert.Single(uyeler.EnumerateArray());
        Assert.True(uyeler.EnumerateArray().First().GetProperty("isLead").GetBoolean());

        // Kullanıcının ekipleri (İK-1 gereği birden fazla olabilir).
        var ekiplerim = await ApiTestHost.JsonAsync(await _a.GetAsync($"/api/users/{_userA}/teams"));
        Assert.Single(ekiplerim.EnumerateArray());

        // Üye çıkar → liderlik de temizlenir.
        (await _a.DeleteAsync($"/api/teams/{id}/members/{_userA}")).EnsureSuccessStatusCode();
        tek = await ApiTestHost.JsonAsync(await _a.GetAsync($"/api/teams/{id}"));
        Assert.Equal(JsonValueKind.Null, tek.GetProperty("leadUserId").ValueKind);

        (await _a.DeleteAsync($"/api/teams/{id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await _a.GetAsync($"/api/teams/{id}")).StatusCode);
    }

    /// <summary>EKA02 — <b>Firma istemciden alınmaz:</b> gövdedeki <c>companyId</c> yok sayılır ve
    /// kayıt oturumun firmasına yazılır. B firması A'nın ekibini GÖREMEZ ve DEĞİŞTİREMEZ (IDOR).</summary>
    [Fact]
    public async Task EKA02_Firma_Govdeden_Alinmaz_Ve_IDOR_Kapali()
    {
        // Gövdeye BAŞKA firmanın kimliği konuyor — sunucu bunu yok saymalı.
        var id = await IdAl(await _a.PostAsJsonAsync("/api/teams",
            new { name = "Sızıntı Denemesi", companyId = CoB }));

        // A'da görünüyor…
        var listeA = await ApiTestHost.JsonAsync(await _a.GetAsync("/api/teams"));
        Assert.Contains(listeA.EnumerateArray(), e => e.GetProperty("id").GetString() == id);

        // …B'de GÖRÜNMÜYOR (gövdedeki companyId hiçbir etki yapmadı).
        var listeB = await ApiTestHost.JsonAsync(await _b.GetAsync("/api/teams"));
        Assert.DoesNotContain(listeB.EnumerateArray(), e => e.GetProperty("id").GetString() == id);

        // B, A'nın ekibine hiçbir yoldan erişemez.
        Assert.Equal(HttpStatusCode.NotFound, (await _b.GetAsync($"/api/teams/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _b.PutAsJsonAsync($"/api/teams/{id}", new { name = "Çalındı", isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _b.DeleteAsync($"/api/teams/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _b.PostAsJsonAsync($"/api/teams/{id}/members", new { userId = _userB })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _b.GetAsync($"/api/teams/{id}/members")).StatusCode);

        // A'nın verisi bozulmadı.
        var tek = await ApiTestHost.JsonAsync(await _a.GetAsync($"/api/teams/{id}"));
        Assert.Equal("Sızıntı Denemesi", tek.GetProperty("name").GetString());
    }

    /// <summary>EKA03 — Başka firmanın KULLANICISI üye yapılamaz (Migration084 kullanıcıya FK vermez;
    /// bütünlük kapısı sunucudadır).</summary>
    [Fact]
    public async Task EKA03_Baska_Firmanin_Kullanicisi_Uye_Yapilamaz()
    {
        var id = await IdAl(await _a.PostAsJsonAsync("/api/teams", new { name = "Ekip" }));
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _a.PostAsJsonAsync($"/api/teams/{id}/members", new { userId = _userB })).StatusCode);
    }

    /// <summary>EKA04 — <b>AYNA SÖZLEŞMESİ:</b> <c>/api/lookups/sync</c> ekip verisini masaüstünün
    /// OKUDUĞU alan adlarıyla taşır. Ad yanlış olsaydı masaüstü alanı null okur ve yerelde veri
    /// kaybolurdu (TSN dersi).</summary>
    [Fact]
    public async Task EKA04_Lookup_Aynasi_Ekipleri_Dogru_Adlarla_Tasir()
    {
        var id = await IdAl(await _a.PostAsJsonAsync("/api/teams", new { name = "Ayna Ekibi" }));
        (await _a.PostAsJsonAsync($"/api/teams/{id}/members", new { userId = _userA })).EnsureSuccessStatusCode();
        (await _a.PutAsJsonAsync($"/api/teams/{id}",
            new { name = "Ayna Ekibi", leadUserId = _userA, isActive = true })).EnsureSuccessStatusCode();

        var kok = await ApiTestHost.JsonAsync(await _a.GetAsync("/api/lookups/sync"));

        var ekip = kok.GetProperty("teams").EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == id);
        foreach (var alan in new[] { "id", "name", "lead_user_id", "is_active" })
            Assert.True(ekip.EnumerateObject().Any(p => p.Name == alan),
                $"teams: '{alan}' alanı yanıtta yok. Gelen anahtarlar: " +
                string.Join(", ", ekip.EnumerateObject().Select(p => p.Name)));
        Assert.Equal(_userA, ekip.GetProperty("lead_user_id").GetString());

        var uye = kok.GetProperty("teamMembers").EnumerateArray()
            .Single(e => e.GetProperty("team_id").GetString() == id);
        foreach (var alan in new[] { "id", "team_id", "user_id", "is_lead" })
            Assert.True(uye.EnumerateObject().Any(p => p.Name == alan),
                $"teamMembers: '{alan}' alanı yanıtta yok. Gelen anahtarlar: " +
                string.Join(", ", uye.EnumerateObject().Select(p => p.Name)));
        Assert.Equal(_userA, uye.GetProperty("user_id").GetString());

        // Ayna TENANT süzgeçlidir: B'nin senkronunda A'nın ekibi YOKTUR.
        var kokB = await ApiTestHost.JsonAsync(await _b.GetAsync("/api/lookups/sync"));
        Assert.DoesNotContain(kokB.GetProperty("teams").EnumerateArray(),
            e => e.GetProperty("id").GetString() == id);
    }

    /// <summary>EKA05 — Yumuşak silinen ekip ve üyelikleri AYNADA GÖRÜNMEZ (sunucuda silinen,
    /// masaüstünde de düşer).</summary>
    [Fact]
    public async Task EKA05_Silinen_Ekip_Aynada_Gorunmez()
    {
        var id = await IdAl(await _a.PostAsJsonAsync("/api/teams", new { name = "Silinecek" }));
        (await _a.PostAsJsonAsync($"/api/teams/{id}/members", new { userId = _userA })).EnsureSuccessStatusCode();
        (await _a.DeleteAsync($"/api/teams/{id}")).EnsureSuccessStatusCode();

        var kok = await ApiTestHost.JsonAsync(await _a.GetAsync("/api/lookups/sync"));
        Assert.DoesNotContain(kok.GetProperty("teams").EnumerateArray(),
            e => e.GetProperty("id").GetString() == id);
        Assert.DoesNotContain(kok.GetProperty("teamMembers").EnumerateArray(),
            e => e.GetProperty("team_id").GetString() == id);
    }
}
