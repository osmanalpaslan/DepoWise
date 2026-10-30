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

        // 2026-10-29 (kullanıcı kararı): sohbet yetkiye BAĞLI DEĞİL — hiçbir kullanıcıya özel yetki verilmez.

        _ali = await _host.LoginAsync("ali", Pass, CoA, sube);
        _veli = await _host.LoginAsync("veli", Pass, CoA, sube);
        _yetkisiz = await _host.LoginAsync("yetkisiz", Pass, CoA, sube);

        // Başka firma — tenant izolasyonu için.
        var adminB = _svc.Users.EnsureInitialAdmin(CoB, "sohbet_admin_b", Pass, RoleKeys.CompanyAdmin);
        var oturumB = new SessionContext(adminB, CoB, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var subeB = new BranchService(_svc.Factory).Create(oturumB, new NewBranch("SOHBET-B-Merkez"));
        _digerFirmaId = _svc.Users.EnsureInitialAdmin(CoB, "yabanci", Pass, RoleKeys.Staff);
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

    /// <summary>2026-10-29 (kullanıcı kararı: "sohbet yetkiye bağlı olmamalı, login olan her kullanıcıda etkin"): HİÇBİR yetkisi
    /// olmayan personel de kişileri görür, yazar ve okur. Oturumsuz istek ise reddedilir.</summary>
    [Fact]
    public async Task Yetkisi_Olmayan_Personel_De_Sohbet_Eder()
    {
        var kisiler = await _yetkisiz.GetAsync("/api/chat/users");
        Assert.Equal(HttpStatusCode.OK, kisiler.StatusCode);
        var gonder = await Gonder(_yetkisiz, _aliId, "merhaba ali");
        Assert.Equal(HttpStatusCode.OK, gonder.StatusCode);
        var aliKutusu = await Dizi(_ali, $"/api/chat/messages?withUserId={_yetkisizId}");
        Assert.Single(aliKutusu);

        var oturumsuz = await _host.CreateClient().GetAsync("/api/chat/users");
        Assert.True(ApiTestHost.IsDenied(oturumsuz), $"oturumsuz istek reddedilmeli, gelen: {oturumsuz.StatusCode}");
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

    /// <summary>2026-10-29 (kullanıcı isteği "mesaj görüldüğünde görüldü yazısı mesajın altında"): kişi listesi, karşı tarafın BENİM
    /// mesajlarımı en son okuduğu anı taşır. Okumadan önce boş → "İletildi"; okuduktan sonra dolu → "Görüldü HH:mm". Karşı tarafın
    /// bana yazdığı mesajları BENİM okumam bu alanı DEĞİŞTİRMEZ (yön karışmaz); başka firmanın kişisi listede hiç yok.</summary>
    [Fact]
    public async Task Goruldu_Bilgisi_KarsiTaraf_Okuyunca_Gelir()
    {
        Assert.Equal(HttpStatusCode.OK, (await Gonder(_ali, _veliId, "lastikler geldi mi?")).StatusCode);

        JsonElement VeliSatiri(List<JsonElement> l) => l.Single(k => k.GetProperty("userId").GetString() == _veliId);
        var once = VeliSatiri(await Dizi(_ali, "/api/chat/users"));
        Assert.True(once.GetProperty("sonMesajBenim").GetBoolean());
        Assert.Equal(JsonValueKind.Null, once.GetProperty("karsiOkuduAt").ValueKind);
        Assert.Equal("İletildi", DepoWise.Infrastructure.Chat.ChatKisi.GorulduDurumu(
            once.GetProperty("sonMesajAt").GetInt64(), true, null));

        // Ali, Veli'nin ona yazdığını okusa bile (ters yön) Ali'nin mesajı "görülmüş" sayılmaz.
        Assert.Equal(HttpStatusCode.OK, (await _ali.PostAsJsonAsync("/api/chat/seen", new { withUserId = _veliId })).StatusCode);
        Assert.Equal(JsonValueKind.Null, VeliSatiri(await Dizi(_ali, "/api/chat/users")).GetProperty("karsiOkuduAt").ValueKind);

        Assert.Equal(HttpStatusCode.OK, (await _veli.PostAsJsonAsync("/api/chat/seen", new { withUserId = _aliId })).StatusCode);
        var sonra = VeliSatiri(await Dizi(_ali, "/api/chat/users"));
        var okuma = sonra.GetProperty("karsiOkuduAt").GetInt64();
        Assert.True(okuma >= sonra.GetProperty("sonMesajAt").GetInt64());
        Assert.StartsWith("Görüldü ", DepoWise.Infrastructure.Chat.ChatKisi.GorulduDurumu(sonra.GetProperty("sonMesajAt").GetInt64(), true, okuma));
        // Son mesaj karşı tarafınsa durum yazısı yok.
        Assert.Equal("", DepoWise.Infrastructure.Chat.ChatKisi.GorulduDurumu(sonra.GetProperty("sonMesajAt").GetInt64(), false, okuma));

        // Başka firmanın kullanıcısı Ali'nin kişi listesinde hiç yok (alan firma süzgecinin içinde okunur).
        Assert.DoesNotContain(await Dizi(_ali, "/api/chat/users"), k => k.GetProperty("userId").GetString() == _digerFirmaId);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    //  ⭐ 2026-10-30 — ANLIK SOHBET (/api/chat/wait, kullanıcı isteği "sohbet anlık olsun")
    // ─────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<(long Surum, bool Degisti)> Bekle(HttpClient c, long since, int sure)
    {
        var j = await Json(await c.GetAsync($"/api/chat/wait?since={since}&sure={sure}"));
        return (j.GetProperty("version").GetInt64(), j.GetProperty("changed").GetBoolean());
    }

    /// <summary>Alıcı beklerken mesaj gelince bekleme HEMEN döner (25 sn'yi beklemez) ve "değişti" der.</summary>
    [Fact]
    public async Task Anlik_Mesaj_Gelince_Bekleyen_Alici_Hemen_Uyanir()
    {
        var (v0, _) = await Bekle(_veli, 0, 1);
        var bekleme = Bekle(_veli, v0, 25);
        await Task.Delay(300);
        var sayac = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(HttpStatusCode.OK, (await Gonder(_ali, _veliId, "anlık mı?")).StatusCode);
        var (v1, degisti) = await bekleme;
        Assert.True(degisti);
        Assert.True(v1 > v0);
        Assert.True(sayac.Elapsed < TimeSpan.FromSeconds(5), "bekleme hemen dönmeliydi: " + sayac.Elapsed);
        // Elindeki sürüm güncelken yeni olay yoksa "değişmedi" der (süre sonunda).
        Assert.False((await Bekle(_veli, v1, 1)).Degisti);
    }

    /// <summary>Başka kişiye giden mesaj beni UYANDIRMAZ; başka firmanın kullanıcısı da hiçbir olayla uyanmaz (işaret firma + kullanıcı).</summary>
    [Fact]
    public async Task Anlik_Baskasinin_Mesaji_Ve_Baska_Firma_Uyandirmaz()
    {
        var (vVeli, _) = await Bekle(_veli, 0, 1);
        var (vYabanci, _) = await Bekle(_digerFirma, 0, 1);
        var veliBekleme = Bekle(_veli, vVeli, 2);
        var yabanciBekleme = Bekle(_digerFirma, vYabanci, 2);
        await Task.Delay(200);
        Assert.Equal(HttpStatusCode.OK, (await Gonder(_ali, _yetkisizId, "veli'ye değil")).StatusCode);
        Assert.False((await veliBekleme).Degisti);
        Assert.False((await yabanciBekleme).Degisti);
    }

    /// <summary>Mesajım okununca BEN uyanırım ("Görüldü" anında); okunacak mesaj yoksa uyandırma olmaz.</summary>
    [Fact]
    public async Task Anlik_Okundu_Gondereni_Uyandirir()
    {
        Assert.Equal(HttpStatusCode.OK, (await Gonder(_ali, _veliId, "okuyunca haber ver")).StatusCode);
        var (vAli, _) = await Bekle(_ali, 0, 1);
        var bekleme = Bekle(_ali, vAli, 25);
        await Task.Delay(200);
        Assert.Equal(HttpStatusCode.OK, (await _veli.PostAsJsonAsync("/api/chat/seen", new { withUserId = _aliId })).StatusCode);
        Assert.True((await bekleme).Degisti);

        var (vAli2, _) = await Bekle(_ali, 0, 1);
        var ikinci = Bekle(_ali, vAli2, 2);
        Assert.Equal(HttpStatusCode.OK, (await _veli.PostAsJsonAsync("/api/chat/seen", new { withUserId = _aliId })).StatusCode);   // okunacak yok
        Assert.False((await ikinci).Degisti);
    }

    /// <summary>Oturumsuz bekleme reddedilir.</summary>
    [Fact]
    public async Task Anlik_Oturumsuz_Bekleme_Reddedilir()
        => Assert.True(ApiTestHost.IsDenied(await _host.CreateClient().GetAsync("/api/chat/wait?since=0&sure=1")));

    /// <summary>Sürüm geriye gitmez ve sunucu yeniden başlasa da (sayaç sıfır) ilk yeni olay eski sürümü geçer (şimdiki zaman, ms).</summary>
    [Fact]
    public void Anlik_Surum_Artar_Ve_Zamani_Gecer()
    {
        var kullanici = "anlik-" + Guid.NewGuid().ToString("N");
        var once = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        DepoWise.Api.ChatNotifier.Bildir("FIRMA-X", kullanici);
        var v1 = DepoWise.Api.ChatNotifier.Surum("FIRMA-X", kullanici);
        DepoWise.Api.ChatNotifier.Bildir("FIRMA-X", kullanici);
        var v2 = DepoWise.Api.ChatNotifier.Surum("FIRMA-X", kullanici);
        Assert.True(v1 >= once);
        Assert.True(v2 > v1);
        Assert.Equal(0, DepoWise.Api.ChatNotifier.Surum("FIRMA-Y", kullanici));   // aynı kişi kimliği başka firmada AYRI
    }

    /// <summary>Masaüstü ve web istemcisi anlık dinleyiciyi taşır; aralıklı yoklama YEDEK olarak seyrekleşti.</summary>
    [Fact]
    public void Anlik_Istemciler_Dinleyiciyi_Tasir()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        var masa = File.ReadAllText(Path.Combine(kok!.FullName, "src", "DepoWise.Desktop", "ViewModels", "ChatViewModel.cs"));
        var istemci = File.ReadAllText(Path.Combine(kok.FullName, "src", "DepoWise.Desktop", "OrgServerClient.cs"));
        var web = File.ReadAllText(Path.Combine(kok.FullName, "src", "DepoWise.Web", "Components", "Layout", "ChatDock.razor"));
        Assert.Contains("if (Kullanilabilir) _ = Task.Run(AnlikDinle);", masa);
        Assert.Contains("if (r.Value.Degisti) await Yokla();", masa);
        Assert.Contains("_bekleyenYoklama = true; return;", masa);                 // tur sürerken gelen işaret kaybolmaz
        Assert.Contains("\"/api/chat/wait?since=\"", istemci);
        Assert.Contains("/api/chat/wait?since={_anlikSurum}&sure={AnlikBekleSaniye}", web);
        Assert.Contains("_bekleyenYoklama = true; return;", web);
        Assert.Contains("public const int AcikYoklamaSaniye = 15;", masa);
        Assert.Contains("private const int AcikSaniye = 15;", web);
    }
}
