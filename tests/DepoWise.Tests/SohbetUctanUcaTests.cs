using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Organization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ SOHBET — GERÇEK UÇTAN UCA TESTLER (kullanıcı eleştirisi 2026-09-07) ═══
///
/// <para><b>Kullanıcı:</b> "chat için testlerin yetersiz. süreç hatalı ama sürekli test sonuçların
/// olumlu, anlam veremiyorum."</para>
///
/// <para><b>Haklıydı.</b> Sohbetin mevcut testleri ağırlıkla <i>kaynak metnini</i> denetliyordu
/// ("şu satır dosyada var mı"). Böyle bir test, özellik tamamen bozukken bile YEŞİL kalır.
/// Buradaki testler bunun tersini yapar: iki gerçek kullanıcı yaratılır, gerçek HTTP uçları
/// çağrılır ve <b>karşı tarafın mesajı gerçekten görüp görmediği</b> doğrulanır.</para>
///
/// <para>Kapsam: gönder/al · iki yönlü · okundu bilgisi · okunmamış sayacı · artımlı yoklama
/// (since) · yetki kapısı · firma (tenant) izolasyonu · doğrulama (boş/çok uzun mesaj) ·
/// olmayan kullanıcı.</para>
/// </summary>
[Collection("PostgresSchema")]
public class SohbetUctanUcaTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CoA = "SOHBET-A";
    private const string CoB = "SOHBET-B";
    private const string Pass = "Test!2026";

    private ServerServices _svc = null!;
    private HttpClient _ali = null!, _veli = null!, _yetkisiz = null!, _digerFirma = null!;
    private string _aliId = "", _veliId = "", _yetkisizId = "", _digerFirmaId = "";

    private static ModulePermission Tam(string m) => new(m, true, true, true, true);
    private SessionContext SuperAdmin() => new("sa", CoA, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        var adminId = _svc.Users.EnsureInitialAdmin(CoA, "sohbet_admin", Pass, RoleKeys.CompanyAdmin);
        var adminOturum = new SessionContext(adminId, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var sube = new BranchService(_svc.Factory).Create(adminOturum, new NewBranch("SOHBET-Merkez"));

        _aliId = _svc.Users.EnsureInitialAdmin(CoA, "ali", Pass, RoleKeys.Staff);
        _veliId = _svc.Users.EnsureInitialAdmin(CoA, "veli", Pass, RoleKeys.Staff);
        _yetkisizId = _svc.Users.EnsureInitialAdmin(CoA, "yetkisiz", Pass, RoleKeys.Staff);

        // Sohbet DENY-BY-DEFAULT: iki kullanıcıya AÇIKÇA verilir, üçüncüsüne VERİLMEZ.
        _svc.Permissions.SaveForUser(SuperAdmin(), _aliId, new[] { Tam("chat") }, Array.Empty<string>());
        _svc.Permissions.SaveForUser(SuperAdmin(), _veliId, new[] { Tam("chat") }, Array.Empty<string>());

        _ali = await _host.LoginAsync("ali", Pass, CoA, sube);
        _veli = await _host.LoginAsync("veli", Pass, CoA, sube);
        _yetkisiz = await _host.LoginAsync("yetkisiz", Pass, CoA, sube);

        // Başka firma — tenant izolasyonu için.
        var adminB = _svc.Users.EnsureInitialAdmin(CoB, "sohbet_admin_b", Pass, RoleKeys.CompanyAdmin);
        var oturumB = new SessionContext(adminB, CoB, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var subeB = new BranchService(_svc.Factory).Create(oturumB, new NewBranch("SOHBET-B-Merkez"));
        _digerFirmaId = _svc.Users.EnsureInitialAdmin(CoB, "yabanci", Pass, RoleKeys.Staff);
        _svc.Permissions.SaveForUser(
            new SessionContext("sa", CoB, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty),
            _digerFirmaId, new[] { Tam("chat") }, Array.Empty<string>());
        _digerFirma = await _host.LoginAsync("yabanci", Pass, CoB, subeB);
    }

    public Task DisposeAsync() { _host.Dispose(); return Task.CompletedTask; }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<List<JsonElement>> Dizi(HttpClient c, string yol)
        => (await Json(await c.GetAsync(yol))).EnumerateArray().ToList();

    private Task<HttpResponseMessage> Gonder(HttpClient c, string kime, string govde)
        => c.PostAsJsonAsync("/api/chat/messages", new { toUserId = kime, body = govde });

    // ─────────────────────────────────────────────────────────────────────────────────────────
    //  ⭐ ASIL TEST: karşı taraf mesajı GERÇEKTEN görüyor mu?
    // ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Gonderilen_Mesaji_KarsiTaraf_Gorur_VeCevaplayabilir()
    {
        var g = await Gonder(_ali, _veliId, "merhaba veli");
        Assert.Equal(HttpStatusCode.OK, g.StatusCode);

        // Velinin gözünden: mesaj var, "benim" değil, gövdesi doğru.
        var veliGorunum = await Dizi(_veli, $"/api/chat/messages?withUserId={_aliId}");
        Assert.Single(veliGorunum);
        Assert.Equal("merhaba veli", veliGorunum[0].GetProperty("body").GetString());
        Assert.False(veliGorunum[0].GetProperty("mine").GetBoolean());

        // Veli cevap verir; ALİ ikisini de sırayla görür.
        (await Gonder(_veli, _aliId, "merhaba ali")).EnsureSuccessStatusCode();
        var aliGorunum = await Dizi(_ali, $"/api/chat/messages?withUserId={_veliId}");
        Assert.Equal(2, aliGorunum.Count);
        Assert.Equal("merhaba veli", aliGorunum[0].GetProperty("body").GetString());
        Assert.True(aliGorunum[0].GetProperty("mine").GetBoolean());
        Assert.Equal("merhaba ali", aliGorunum[1].GetProperty("body").GetString());
        Assert.False(aliGorunum[1].GetProperty("mine").GetBoolean());
    }

    /// <summary>Okunmamış sayacı: mesaj gelince artar, okundu işaretlenince sıfırlanır.</summary>
    [Fact]
    public async Task OkunmamisSayaci_Artar_VeOkununca_Sifirlanir()
    {
        (await Gonder(_ali, _veliId, "bir")).EnsureSuccessStatusCode();
        (await Gonder(_ali, _veliId, "iki")).EnsureSuccessStatusCode();

        var kisiler = await Dizi(_veli, "/api/chat/users");
        var ali = kisiler.First(k => k.GetProperty("userId").GetString() == _aliId);
        Assert.Equal(2, ali.GetProperty("unread").GetInt32());

        var ok = await _veli.PostAsJsonAsync("/api/chat/seen", new { withUserId = _aliId });
        Assert.Equal(2, (await Json(ok)).GetProperty("count").GetInt32());

        var sonra = await Dizi(_veli, "/api/chat/users");
        Assert.Equal(0, sonra.First(k => k.GetProperty("userId").GetString() == _aliId)
                            .GetProperty("unread").GetInt32());
    }

    /// <summary>
    /// Artımlı yoklama: arayüz her 3 saniyede since ile sorar. Bu yol bozulursa
    /// "gelen mesajlar görünmüyor" olur — ekran ilk yüklemede doğru, sonrası ölü kalır.
    /// </summary>
    [Fact]
    public async Task SinceIle_Yoklama_YalnizYeniMesajlari_Dondurur()
    {
        (await Gonder(_ali, _veliId, "eski")).EnsureSuccessStatusCode();
        var ilk = await Dizi(_veli, $"/api/chat/messages?withUserId={_aliId}");
        var sonZaman = ilk[^1].GetProperty("createdAt").GetInt64();

        // Aynı andan sonrası: henüz yeni mesaj yok.
        var bos = await Dizi(_veli, $"/api/chat/messages?withUserId={_aliId}&since={sonZaman}");
        Assert.Empty(bos);

        (await Gonder(_ali, _veliId, "yeni")).EnsureSuccessStatusCode();
        var yeni = await Dizi(_veli, $"/api/chat/messages?withUserId={_aliId}&since={sonZaman}");
        Assert.Single(yeni);
        Assert.Equal("yeni", yeni[0].GetProperty("body").GetString());
    }

    /// <summary>Kişi listesi: aynı firmadaki DİĞER kullanıcılar gelir, kendisi ve başka firma gelmez.</summary>
    [Fact]
    public async Task KisiListesi_AyniFirmadakileri_Verir_KendisiniVermez()
    {
        var kisiler = await Dizi(_ali, "/api/chat/users");
        var idler = kisiler.Select(k => k.GetProperty("userId").GetString()).ToList();
        Assert.Contains(_veliId, idler);
        Assert.DoesNotContain(_aliId, idler);
        Assert.DoesNotContain(_digerFirmaId, idler);   // başka firma ASLA görünmez
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    //  Güvenlik kapıları
    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Sohbet yetkisi olmayan kullanıcı hiçbir ucu kullanamaz (deny-by-default).</summary>
    [Fact]
    public async Task Yetkisiz_Kullanici_HicbirUcu_Kullanamaz()
    {
        var kisiler = await _yetkisiz.GetAsync("/api/chat/users");
        Assert.True(ApiTestHost.IsDenied(kisiler), $"kişi listesi reddedilmeli, gelen: {kisiler.StatusCode}");

        var mesajlar = await _yetkisiz.GetAsync($"/api/chat/messages?withUserId={_aliId}");
        Assert.True(ApiTestHost.IsDenied(mesajlar), $"mesajlar reddedilmeli, gelen: {mesajlar.StatusCode}");

        var gonder = await Gonder(_yetkisiz, _aliId, "sizmamali");
        Assert.True(ApiTestHost.IsDenied(gonder), $"gönderim reddedilmeli, gelen: {gonder.StatusCode}");

        // Ve gerçekten hiçbir şey yazılmamış olmalı.
        var aliKutusu = await Dizi(_ali, $"/api/chat/messages?withUserId={_yetkisizId}");
        Assert.Empty(aliKutusu);
    }

    /// <summary>BAŞKA FİRMADAKİ kullanıcıya mesaj gönderilemez (tenant izolasyonu).</summary>
    [Fact]
    public async Task BaskaFirmaya_MesajGonderilemez()
    {
        var r = await Gonder(_ali, _digerFirmaId, "firma disina sizinti");
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, r.StatusCode);   // 500 = kabul edilemez

        // Yabancı da bizim kullanıcımızın kutusunu okuyamaz.
        var okuma = await _digerFirma.GetAsync($"/api/chat/messages?withUserId={_aliId}");
        if (okuma.IsSuccessStatusCode)
            Assert.Empty((await Json(okuma)).EnumerateArray());
    }

    /// <summary>Boş mesaj kabul edilmez; çok uzun mesaj 500 ile değil, anlaşılır hatayla reddedilir.</summary>
    [Fact]
    public async Task BosVeCokUzunMesaj_AnlasilirSekilde_Reddedilir()
    {
        foreach (var bos in new[] { "", "   " })
        {
            var r = await Gonder(_ali, _veliId, bos);
            Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
            Assert.NotEqual(HttpStatusCode.InternalServerError, r.StatusCode);
        }

        var uzun = await Gonder(_ali, _veliId, new string('x', 5000));
        Assert.NotEqual(HttpStatusCode.OK, uzun.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, uzun.StatusCode);

        Assert.Empty(await Dizi(_ali, $"/api/chat/messages?withUserId={_veliId}"));
    }

    /// <summary>Var olmayan kullanıcıya gönderim 500 vermez (ekranda ham hata görünmemeli).</summary>
    [Fact]
    public async Task OlmayanKullaniciya_Gonderim_500_Vermez()
    {
        var r = await Gonder(_ali, "yok-boyle-bir-kullanici", "merhaba");
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, r.StatusCode);
    }
}
