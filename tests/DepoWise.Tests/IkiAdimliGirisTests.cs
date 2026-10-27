using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-01 (K-20, 2026-10-27) — SÜPER ADMİN İKİ ADIMLI GİRİŞ (Google Authenticator / TOTP) ═══
///  MF1 — kod üretimi RFC 6238 resmi örnekleriyle birebir; Base32 gidiş-dönüş
///  MF2 — yalnız süper admin kurar; kurulum ilk doğru koda kadar ETKİN DEĞİL; yanlış kod etkinleştirmez; 8 kurtarma kodu
///  MF3 — aynı kod ikinci kez kabul edilmez (tekrar saldırısı); saat kayması ±30 sn tolere edilir, fazlası reddedilir
///  MF4 — kurtarma kodu tek kullanımlık
///  MF5 — gizli bilgi veritabanında ŞİFRELİ; başka anahtarla / anahtarsız sunucuda kodla giriş YOK (fail-closed)
///  MF6 — kapatma geçerli kod ister
///  MA1 — API: kod açık süper admin kodsuz giremez (web girişi + masaüstü senkron girişi); doğru kodla girer; paket "mfa" taşır
///  MA2 — API: kodsuz kullanıcılar etkilenmez; personel kurulum yapamaz
///  MA3 — API: kod tahmini kullanıcı başına sınırlı (429, mfaRequired)
///  MI1 — iki ortam: web 6/7/8. adımlar, masaüstü kod kutusu + çevrimdışı giriş kapısı + 429 "çevrimdışı" sayılmaz
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class IkiAdimliGirisTests
{
    private sealed class Saat : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000); }

    private static readonly byte[] Anahtar = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private static (SqliteConnectionFactory F, string Yol) Db()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_mfa_" + Guid.NewGuid().ToString("N") + ".db");
        var f = new SqliteConnectionFactory(yol);
        new MigrationRunner(f).Run();
        return (f, yol);
    }

    private static void Sil(string yol) { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { File.Delete(yol); } catch { } }

    private static SessionContext Super(string uid = "u-super") => new(uid, "CO-MFA", new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);

    private static string Kod(string gizliB32, DateTimeOffset an, int kayma = 0)
        => Totp.Uret(Totp.Base32Coz(gizliB32), Totp.Adim(an) + kayma);

    [Fact]
    public void MF1_Rfc6238_Ornekleri_Ve_Base32()
    {
        var gizli = System.Text.Encoding.ASCII.GetBytes("12345678901234567890");
        Assert.Equal("287082", Totp.Uret(gizli, Totp.Adim(DateTimeOffset.FromUnixTimeSeconds(59))));
        Assert.Equal("081804", Totp.Uret(gizli, Totp.Adim(DateTimeOffset.FromUnixTimeSeconds(1111111109))));
        Assert.Equal("005924", Totp.Uret(gizli, Totp.Adim(DateTimeOffset.FromUnixTimeSeconds(1234567890))));
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Totp.Base32(gizli));
        Assert.Equal(gizli, Totp.Base32Coz("gezd gnbv-gy3tqojqgezdgnbvgy3tqojq"));
        Assert.StartsWith("otpauth://totp/Alpnex:superadmin?secret=ABC&issuer=Alpnex", Totp.OtpAuthAdresi("Alpnex", "superadmin", "ABC"));
    }

    [Fact]
    public void MF2_Kurulum_Yalniz_Super_Admin_Ilk_Dogru_Kodla_Etkinlesir()
    {
        var (f, yol) = Db();
        try
        {
            var saat = new Saat();
            var svc = new IkiAdimliGirisService(f, Anahtar, saat);
            var personel = new SessionContext("u-p", "CO-MFA", new[] { RoleKeys.Staff }, PermissionSet.Empty);
            Assert.Throws<ForbiddenException>(() => svc.KurulumBaslat(personel, "p"));

            var k = svc.KurulumBaslat(Super(), "superadmin");
            Assert.False(svc.Etkin("u-super"));                                   // yarım kurulum kimseyi kilitlemez
            Assert.False(svc.GirisDogrula("u-super", Kod(k.GizliBase32, saat.UtcNow)));
            Assert.NotEmpty(Convert.FromBase64String(k.QrPngBase64));
            Assert.Contains("secret=" + k.GizliBase32, k.OtpAuthAdresi);

            Assert.Throws<ArgumentException>(() => svc.KurulumOnayla(Super(), "000000"));
            Assert.False(svc.Etkin("u-super"));
            var kurtarma = svc.KurulumOnayla(Super(), Kod(k.GizliBase32, saat.UtcNow));
            Assert.True(svc.Etkin("u-super"));
            Assert.Equal(IkiAdimliGirisService.KurtarmaKoduSayisi, kurtarma.Distinct().Count());
            Assert.Equal(new IkiAdimliGirisService.Durum(true, 8), svc.DurumOku("u-super"));
            Assert.Throws<InvalidOperationException>(() => svc.KurulumBaslat(Super(), "superadmin"));   // açıkken yeniden kurulmaz
        }
        finally { Sil(yol); }
    }

    [Fact]
    public void MF3_Ayni_Kod_Iki_Kez_Kullanilamaz_Saat_Kaymasi_Siniri()
    {
        var (f, yol) = Db();
        try
        {
            var saat = new Saat();
            var svc = new IkiAdimliGirisService(f, Anahtar, saat);
            var k = svc.KurulumBaslat(Super(), "superadmin");
            svc.KurulumOnayla(Super(), Kod(k.GizliBase32, saat.UtcNow, -1));      // onay bir önceki adımla

            var kod = Kod(k.GizliBase32, saat.UtcNow);
            Assert.True(svc.GirisDogrula("u-super", kod));
            Assert.False(svc.GirisDogrula("u-super", kod));                        // ⭐ tekrar saldırısı
            Assert.False(svc.GirisDogrula("u-super", Kod(k.GizliBase32, saat.UtcNow, -1)));   // eski adım da geçmez
            Assert.True(svc.GirisDogrula("u-super", Kod(k.GizliBase32, saat.UtcNow, +1)));    // +30 sn kayma kabul
            saat.UtcNow = saat.UtcNow.AddMinutes(10);
            Assert.False(svc.GirisDogrula("u-super", Kod(k.GizliBase32, saat.UtcNow, +2)));   // ±1'den fazlası red
            Assert.False(svc.GirisDogrula("u-super", "12345"));
            Assert.False(svc.GirisDogrula("u-super", null));
        }
        finally { Sil(yol); }
    }

    [Fact]
    public void MF4_Kurtarma_Kodu_Tek_Kullanimlik()
    {
        var (f, yol) = Db();
        try
        {
            var saat = new Saat();
            var svc = new IkiAdimliGirisService(f, Anahtar, saat);
            var k = svc.KurulumBaslat(Super(), "superadmin");
            var kodlar = svc.KurulumOnayla(Super(), Kod(k.GizliBase32, saat.UtcNow));
            Assert.True(svc.GirisDogrula("u-super", kodlar[0].ToLowerInvariant().Replace("-", " ")));   // biçim affedici
            Assert.False(svc.GirisDogrula("u-super", kodlar[0]));                                         // ikinci kez YOK
            Assert.Equal(7, svc.DurumOku("u-super").KalanKurtarmaKodu);
            Assert.False(svc.GirisDogrula("u-super", "AAAAA-BBBBB"));
        }
        finally { Sil(yol); }
    }

    [Fact]
    public void MF5_Gizli_Bilgi_Sifreli_Anahtarsiz_Giris_Yok()
    {
        var (f, yol) = Db();
        try
        {
            var saat = new Saat();
            var svc = new IkiAdimliGirisService(f, Anahtar, saat);
            var k = svc.KurulumBaslat(Super(), "superadmin");
            svc.KurulumOnayla(Super(), Kod(k.GizliBase32, saat.UtcNow, -1));
            using (var c = f.Create())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT secret_enc FROM user_mfa WHERE user_id='u-super';";
                var saklanan = (string)cmd.ExecuteScalar()!;
                Assert.DoesNotContain(k.GizliBase32, saklanan);
                Assert.DoesNotContain(Convert.ToBase64String(Totp.Base32Coz(k.GizliBase32)), saklanan);
            }
            var baskaAnahtar = new IkiAdimliGirisService(f, Enumerable.Repeat((byte)7, 32).ToArray(), saat);
            Assert.False(baskaAnahtar.GirisDogrula("u-super", Kod(k.GizliBase32, saat.UtcNow)));
            var anahtarsiz = new IkiAdimliGirisService(f, null, saat);
            Assert.True(anahtarsiz.Etkin("u-super"));                             // kod açık sayılır …
            Assert.False(anahtarsiz.GirisDogrula("u-super", Kod(k.GizliBase32, saat.UtcNow)));   // … ama doğrulanamaz → giriş YOK
            Assert.False(anahtarsiz.AnahtarVar);
            Assert.Throws<InvalidOperationException>(() => anahtarsiz.KurulumBaslat(Super("u-2"), "x"));
            Assert.Null(IkiAdimliGirisService.AnahtarCoz("kisa"));
            Assert.Throws<ArgumentException>(() => new IkiAdimliGirisService(f, new byte[16]));
        }
        finally { Sil(yol); }
    }

    [Fact]
    public void MF6_Kapatma_Gecerli_Kod_Ister()
    {
        var (f, yol) = Db();
        try
        {
            var saat = new Saat();
            var svc = new IkiAdimliGirisService(f, Anahtar, saat);
            var k = svc.KurulumBaslat(Super(), "superadmin");
            svc.KurulumOnayla(Super(), Kod(k.GizliBase32, saat.UtcNow, -1));
            Assert.Throws<ArgumentException>(() => svc.Kapat(Super(), "111111"));
            Assert.True(svc.Etkin("u-super"));
            svc.Kapat(Super(), Kod(k.GizliBase32, saat.UtcNow));
            Assert.False(svc.Etkin("u-super"));
        }
        finally { Sil(yol); }
    }

    // ── API ────────────────────────────────────────────────────────────────────────────────────────

    private const string Co = "MFA-CO";
    private const string Pass = "Mfa!2026x";

    private static async Task<(ApiTestHost Host, ServerServices Svc, string Sube)> Kur()
    {
        var host = new ApiTestHost();
        _ = host.CreateClient();
        var svc = host.Services.GetRequiredService<ServerServices>();
        var uid = svc.Users.EnsureInitialAdmin(Co, "mfa_super", Pass, RoleKeys.SuperAdmin);
        svc.Users.EnsureInitialAdmin(Co, "mfa_personel", Pass, RoleKeys.Staff);
        var sube = svc.Branches.Create(new SessionContext(uid, Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty),
            new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        return (host, svc, sube);
    }

    private static Task<HttpResponseMessage> Giris(ApiTestHost h, string kullanici, string sube, string? kod, string uc = "/api/auth/login")
        => h.Anonymous().PostAsJsonAsync(uc, new { companyId = Co, username = kullanici, password = Pass, branchId = sube, totpCode = kod });

    [Fact]
    public async Task MA1_Kod_Acik_Super_Admin_Kodsuz_Giremez()
    {
        var (host, _, sube) = await Kur();
        await using var _h = host;
        var super = await host.LoginAsync("mfa_super", Pass, Co, sube);
        var kurulum = await ApiTestHost.JsonAsync(await super.PostAsJsonAsync("/api/auth/mfa/setup", new { }));
        var gizli = kurulum.GetProperty("secret").GetString()!;
        var onay = await super.PostAsJsonAsync("/api/auth/mfa/confirm", new { code = Kod(gizli, DateTimeOffset.UtcNow) });
        Assert.Equal(HttpStatusCode.OK, onay.StatusCode);
        var kurtarma = (await ApiTestHost.JsonAsync(onay)).GetProperty("recoveryCodes").EnumerateArray().Select(x => x.GetString()!).ToList();
        var durum = await ApiTestHost.JsonAsync(await super.GetAsync("/api/auth/mfa/status"));
        Assert.True(durum.GetProperty("enabled").GetBoolean());

        // Web girişi: kodsuz → 401 + mfaRequired, jeton YOK.
        var r1 = await Giris(host, "mfa_super", sube, null);
        Assert.Equal(HttpStatusCode.Unauthorized, r1.StatusCode);
        var g1 = await ApiTestHost.JsonAsync(r1);
        Assert.True(g1.GetProperty("mfaRequired").GetBoolean());
        Assert.False(g1.TryGetProperty("token", out _));
        // Doğru (sonraki adım) kodla → jeton.
        var r2 = await Giris(host, "mfa_super", sube, Kod(gizli, DateTimeOffset.UtcNow, +1));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.False(string.IsNullOrEmpty((await ApiTestHost.JsonAsync(r2)).GetProperty("token").GetString()));

        // Masaüstü senkron girişi: kodsuz → yerel parola özeti dahil paket VERİLMEZ.
        var s1 = await Giris(host, "mfa_super", sube, null, "/api/auth/sync-login");
        Assert.Equal(HttpStatusCode.Unauthorized, s1.StatusCode);
        var sg1 = await s1.Content.ReadAsStringAsync();
        Assert.Contains("mfaRequired", sg1);
        Assert.DoesNotContain("passwordHash", sg1, StringComparison.OrdinalIgnoreCase);
        // Kurtarma koduyla → paket + jeton + "mfa": true (masaüstü çevrimdışı girişi kapatsın).
        var s2 = await Giris(host, "mfa_super", sube, kurtarma[0], "/api/auth/sync-login");
        Assert.Equal(HttpStatusCode.OK, s2.StatusCode);
        var sg2 = await ApiTestHost.JsonAsync(s2);
        Assert.True(sg2.GetProperty("mfa").GetBoolean());
        Assert.False(string.IsNullOrEmpty(sg2.GetProperty("token").GetString()));
        // Aynı kurtarma kodu ikinci kez → red.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(host, "mfa_super", sube, kurtarma[0], "/api/auth/sync-login")).StatusCode);
    }

    [Fact]
    public async Task MA2_Kodsuz_Kullanicilar_Etkilenmez_Personel_Kuramaz()
    {
        var (host, _, sube) = await Kur();
        await using var _h = host;
        var r = await Giris(host, "mfa_personel", sube, null);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var s = await Giris(host, "mfa_personel", sube, null, "/api/auth/sync-login");
        Assert.Equal(HttpStatusCode.OK, s.StatusCode);
        Assert.False((await ApiTestHost.JsonAsync(s)).GetProperty("mfa").GetBoolean());

        var personel = await host.LoginAsync("mfa_personel", Pass, Co, sube);
        Assert.Equal(HttpStatusCode.Forbidden, (await personel.PostAsJsonAsync("/api/auth/mfa/setup", new { })).StatusCode);
        var d = await ApiTestHost.JsonAsync(await personel.GetAsync("/api/auth/mfa/status"));
        Assert.False(d.GetProperty("canSetup").GetBoolean());
        Assert.True(d.GetProperty("serverReady").GetBoolean());
        // Kodu kurulmamış süper admin de kodsuz girer (kurulum web'de istenir; kimse kilitlenmez).
        Assert.Equal(HttpStatusCode.OK, (await Giris(host, "mfa_super", sube, null)).StatusCode);
    }

    [Fact]
    public async Task MA3_Kod_Tahmini_Kullanici_Basina_Sinirli()
    {
        var (host, _, sube) = await Kur();
        await using var _h = host;
        var super = await host.LoginAsync("mfa_super", Pass, Co, sube);
        var gizli = (await ApiTestHost.JsonAsync(await super.PostAsJsonAsync("/api/auth/mfa/setup", new { }))).GetProperty("secret").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await super.PostAsJsonAsync("/api/auth/mfa/confirm", new { code = Kod(gizli, DateTimeOffset.UtcNow) })).StatusCode);

        HttpStatusCode? son = null;
        for (int i = 0; i < 6; i++) son = (await Giris(host, "mfa_super", sube, (100000 + i).ToString())).StatusCode;
        Assert.Equal(HttpStatusCode.TooManyRequests, son);
        // Sınır dolunca DOĞRU kod da bekletilir (tahmin penceresi kapanır).
        var dogru = await Giris(host, "mfa_super", sube, Kod(gizli, DateTimeOffset.UtcNow, +1));
        Assert.Equal(HttpStatusCode.TooManyRequests, dogru.StatusCode);
        Assert.True((await ApiTestHost.JsonAsync(dogru)).GetProperty("mfaRequired").GetBoolean());
    }

    [Fact]
    public void MI1_Iki_Ortam_Arayuz_Sozlesmesi()
    {
        string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));
        var web = Oku("src/DepoWise.Web/Components/Pages/Login.razor");
        Assert.Contains("else if (_step == 6)", web);
        Assert.Contains("else if (_step == 7)", web);
        Assert.Contains("else if (_step == 8)", web);
        Assert.Contains("if (mfa) { _totp = \"\"; _step = 6; return; }", web);
        Assert.Contains("/api/auth/mfa/setup", web);
        Assert.Contains("/api/auth/mfa/confirm", web);
        Assert.Contains("totpCode", Oku("src/DepoWise.Web/Services/ApiClient.cs"));

        var vm = Oku("src/DepoWise.Desktop/ViewModels/LoginViewModel.cs");
        Assert.Contains("ServerAuthClient.IkiAdimAcik(result.Session.UserId)", vm);
        Assert.Contains("srv.State == ServerAuthClient.AuthState.MfaRequired", vm);
        Assert.Contains("{Binding MfaKodu}", Oku("src/DepoWise.Desktop/Views/LoginWindow.axaml"));
        var istemci = Oku("src/DepoWise.Desktop/ServerAuthClient.cs");
        Assert.Contains("System.Net.HttpStatusCode.TooManyRequests", istemci);   // 429 kod adımında kalır, yerel girişe düşmez
        Assert.Contains("IkiAdimBilgisiniYaz(bundle.UserId, YanittaIkiAdim(body));", istemci);
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d!.FullName;
    }
}
