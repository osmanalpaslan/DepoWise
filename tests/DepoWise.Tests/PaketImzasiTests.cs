using System.Net;
using System.Security.Cryptography;
using DepoWise.Application.Setup;
using DepoWise.Application.Update;
using DepoWise.Infrastructure.Update;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-01 (K-20, 2026-10-27) — GÜNCELLEME PAKETİ İMZASI ═══
///  PI1 — imza sürüm+özete bağlı: doğru imza geçer; sürüm/özet/imza değişince, boşken ya da bilinmeyen anahtarla REDDEDİLİR
///  PI2 — Node imzalayıcısının (yayın betiği) imzası C#'ta doğrulanır (biçim uyumu); üretim açık anahtarı programda
///  PI3 — masaüstü gerçek kurulum kapısı: özet doğru ama imza yok/yanlış → kurulum YOK (RetMetni)
///  PI4 — imzasız yayın "güncelleme yok" sayılır: zorunlu güncellemeye dönüşüp masaüstünü kilitlemez
///  PI5 — kurulum aracı imzayı sunucu yanıtından okur ve doğrular
///  PI6 — sunucu: imzasız / sahte imzalı paket diske DOKUNMADAN reddedilir; imzalı paket kabul edilir ve imza yayınla döner
///  PI7 — sunucu: imza doğru ama yüklenen dosya özetle uyuşmuyor → reddedilir, yayındaki paket EZİLMEZ
///  PI8 — sunucu: kurulum aracı (AlpnexSetup.exe) da yalnız imzalı yüklenir
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class PaketImzasiTests
{
    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b));

    [Fact]
    public void PI1_Imza_Surum_Ve_Ozete_Bagli()
    {
        var sha = new string('B', 64);
        var imza = TestImza.Imzala("1.0.203", sha);
        Assert.True(PaketImzasi.Dogrula("1.0.203", sha, imza));
        Assert.True(PaketImzasi.Dogrula("1.0.203", sha.ToLowerInvariant(), imza));     // özet harf büyüklüğü önemsiz
        Assert.False(PaketImzasi.Dogrula("1.0.204", sha, imza));                       // eski paket yeni sürüm adıyla yayınlanamaz
        Assert.False(PaketImzasi.Dogrula("1.0.203", new string('C', 64), imza));       // başka paket
        Assert.False(PaketImzasi.Dogrula("1.0.203", sha, null));
        Assert.False(PaketImzasi.Dogrula("1.0.203", sha, ""));
        Assert.False(PaketImzasi.Dogrula("1.0.203", sha, "bozuk-imza!!"));
        var bozuk = Convert.FromBase64String(imza); bozuk[^1] ^= 0x01;
        Assert.False(PaketImzasi.Dogrula("1.0.203", sha, Convert.ToBase64String(bozuk)));
        using var yabanci = ECDsa.Create(ECCurve.NamedCurves.nistP256);                 // listede olmayan anahtar
        var yabanciImza = Convert.ToBase64String(yabanci.SignData(System.Text.Encoding.UTF8.GetBytes(PaketImzasi.ImzaMetni("1.0.203", sha)),
            HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        Assert.False(PaketImzasi.Dogrula("1.0.203", sha, yabanciImza));
    }

    [Fact]
    public void PI2_Node_Imzasi_CSharpta_Dogrulanir_Uretim_Anahtari_Programda()
    {
        Assert.True(PaketImzasi.Dogrula("1.2.3", string.Concat(Enumerable.Repeat("ab", 32)), TestImza.NodeImza));
        Assert.False(PaketImzasi.Dogrula("1.2.4", string.Concat(Enumerable.Repeat("ab", 32)), TestImza.NodeImza));
        // Üretim açık anahtarı geçerli bir P-256 anahtarı ve test anahtarı DEĞİL.
        var uretim = Assert.Single(PaketImzasi.Anahtarlar);
        using var ec = ECDsa.Create();
        ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(uretim), out _);
        Assert.Equal(256, ec.KeySize);
        Assert.NotEqual(TestImza.NodeAcikAnahtar, uretim);
        // Node imzalayıcısı C# ile AYNI metni imzalar.
        var js = File.ReadAllText(Path.Combine(Kok(), "scripts", "lib", "yayin_ortak.mjs"));
        Assert.Contains("alpnex-paket-v1\\n${String(surum).trim()}\\n${String(sha256Hex).trim().toUpperCase()}", js);
    }

    [Fact]
    public void PI3_Masaustu_Kurulum_Kapisi_Imzasiz_Paketi_Kurmaz()
    {
        var icerik = new byte[] { 1, 2, 3, 4 };
        var p = TestImza.Paket("1.0.203", icerik);
        UpdateService.RequireSignedPackage(icerik, p.Version, p.ChecksumSha256, p.Signature);   // geçer

        var ex = Assert.Throws<UpdateFailedException>(() => UpdateService.RequireSignedPackage(icerik, p.Version, p.ChecksumSha256, null));
        Assert.Equal(PaketImzasi.RetMetni, ex.Message);
        Assert.Throws<UpdateFailedException>(() => UpdateService.RequireSignedPackage(icerik, "1.0.204", p.ChecksumSha256, p.Signature));
        Assert.Throws<UpdateFailedException>(() => UpdateService.RequireSignedPackage(new byte[] { 9 }, p.Version, p.ChecksumSha256, p.Signature));

        // Gerçek kurulum yolları imza kapısını kullanır (yalnız özet kontrolüne geri dönülmemiş).
        var yukleyici = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Desktop", "UpdateInstaller.cs"));
        Assert.Contains("UpdateService.RequireSignedPackage(zipBytes, version, expectedSha, signature);", yukleyici);
        Assert.DoesNotContain("UpdateService.RequireVerifiedPackage(zipBytes", yukleyici);
        var oto = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Desktop", "AutoUpdateService.cs"));
        Assert.Contains("InstallAndRestart(PendingBytes!, PendingVersion!, PendingChecksum!, PendingSignature)", oto);
    }

    [Fact]
    public void PI4_Imzasiz_Yayin_Zorunlu_Guncelleme_Dogurmaz()
    {
        var imzali = TestImza.Paket("1.0.203", min: "1.0.203");
        Assert.Equal(GuncellemeKarari.Zorunlu, GuncellemePolitikasi.Karar("1.0.202", imzali, null, DateTimeOffset.UtcNow).Karar);
        var imzasiz = imzali with { Signature = null };
        Assert.Equal(GuncellemeKarari.Yok, GuncellemePolitikasi.Karar("1.0.202", imzasiz, null, DateTimeOffset.UtcNow).Karar);
        var sahte = imzali with { ChecksumSha256 = new string('D', 64) };   // imza başka pakete ait
        Assert.Equal(GuncellemeKarari.Yok, GuncellemePolitikasi.Karar("1.0.202", sahte, null, DateTimeOffset.UtcNow).Karar);

        var kok = Path.Combine(Path.GetTempPath(), "dw_imza_" + Guid.NewGuid().ToString("N"));
        try
        {
            var svc = new UpdateService(kok);
            File.WriteAllText(Path.Combine(kok, "current.txt"), "1.0.202");
            var c = svc.Check(imzasiz);
            Assert.False(c.UpdateAvailable);
            Assert.False(c.BelowMinSupported);
            Assert.True(c.SignedWarning);
            Assert.True(svc.Check(imzali).UpdateAvailable);
        }
        finally { try { Directory.Delete(kok, true); } catch { } }
    }

    [Fact]
    public void PI5_Kurulum_Araci_Imzayi_Okur_Ve_Dogrular()
    {
        var m = SetupManifestReader.FromReleasesLatest(
            "{\"version\":\"1.0.203\",\"checksumSha256\":\"" + new string('A', 64) + "\",\"sizeBytes\":5,\"downloadUrl\":\"/api/releases/1.0.203/download\"," +
            "\"minSupportedVersion\":\"0.0.0\",\"signature\":\"SIG==\"}");
        Assert.Equal("SIG==", m.Application.Signature);
        var runner = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Setup", "SetupRunner.cs"));
        Assert.Contains("PaketImzasi.Dogrula(pkg.Version, pkg.Sha256, pkg.Signature)", runner);
        Assert.Contains("PaketImzasi.cs", File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Setup", "DepoWise.Setup.csproj")));
    }

    private static MultipartFormDataContent Form(string surum, byte[] icerik, string? sha = null, string? imza = null)
        => new()
        {
            { new StringContent(surum), "version" },
            { new StringContent(sha ?? Sha(icerik)), "checksum" },
            { new StringContent(icerik.Length.ToString()), "sizeBytes" },
            { new StringContent("0.0.0"), "minSupportedVersion" },
            { new StringContent(imza ?? ""), "signature" },
            { new ByteArrayContent(icerik), "file", $"DepoWise-{surum}.pkg" },
        };

    [Fact]
    public async Task PI6_Sunucu_Imzasiz_Paketi_Diske_Dokunmadan_Reddeder()
    {
        await using var host = new ApiTestHost();
        var admin = await host.LoginSeedAsync();
        var klasor = Path.Combine(host.Services.GetRequiredService<DepoWise.Api.ServerServices>().DataDir, "releases");
        var icerik = RandomNumberGenerator.GetBytes(2048);

        var r1 = await admin.PostAsync("/api/releases", Form("7.0.1", icerik));                       // imzasız
        Assert.Equal(HttpStatusCode.BadRequest, r1.StatusCode);
        Assert.Contains("imzası doğrulanamadı", await r1.Content.ReadAsStringAsync());
        using var yabanci = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sahte = Convert.ToBase64String(yabanci.SignData(System.Text.Encoding.UTF8.GetBytes(PaketImzasi.ImzaMetni("7.0.1", Sha(icerik))),
            HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        var r2 = await admin.PostAsync("/api/releases", Form("7.0.1", icerik, imza: sahte));           // başka anahtar
        Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);
        Assert.False(File.Exists(Path.Combine(klasor, "DepoWise-7.0.1.pkg")));                         // diske yazılmadı

        var imza = TestImza.Imzala("7.0.1", Sha(icerik));
        var r3 = await admin.PostAsync("/api/releases", Form("7.0.1", icerik, imza: imza));
        Assert.Equal(HttpStatusCode.OK, r3.StatusCode);
        var son = await ApiTestHost.JsonAsync(await host.Anonymous().GetAsync("/api/releases/latest"));
        Assert.Equal("7.0.1", son.GetProperty("version").GetString());
        Assert.Equal(imza, son.GetProperty("signature").GetString());
        Assert.True(son.GetProperty("signed").GetBoolean());
        Assert.Equal(icerik, File.ReadAllBytes(Path.Combine(klasor, "DepoWise-7.0.1.pkg")));
    }

    [Fact]
    public async Task PI7_Sunucu_Ozetle_Uyusmayan_Dosyayi_Reddeder_Yayindakini_Ezmez()
    {
        await using var host = new ApiTestHost();
        var admin = await host.LoginSeedAsync();
        var klasor = Path.Combine(host.Services.GetRequiredService<DepoWise.Api.ServerServices>().DataDir, "releases");
        var dogru = RandomNumberGenerator.GetBytes(1024);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/releases", Form("7.0.2", dogru, imza: TestImza.Imzala("7.0.2", Sha(dogru))))).StatusCode);

        // İmza doğru özete ait ama gönderilen dosya BAŞKA (araya girilmiş / bozuk yükleme).
        var baska = RandomNumberGenerator.GetBytes(1024);
        var r = await admin.PostAsync("/api/releases", Form("7.0.2", baska, sha: Sha(dogru), imza: TestImza.Imzala("7.0.2", Sha(dogru))));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(dogru, File.ReadAllBytes(Path.Combine(klasor, "DepoWise-7.0.2.pkg")));            // yayındaki paket duruyor
        Assert.Empty(Directory.GetFiles(klasor, "*.yukleniyor"));                                        // yarım dosya kalmadı
    }

    [Fact]
    public async Task PI8_Kurulum_Araci_Yalniz_Imzali_Yuklenir()
    {
        await using var host = new ApiTestHost();
        var admin = await host.LoginSeedAsync();
        var exe = RandomNumberGenerator.GetBytes(4096);
        MultipartFormDataContent F(string? imza) => new()
        {
            { new StringContent(imza ?? ""), "signature" },
            { new ByteArrayContent(exe), "file", "AlpnexSetup.exe" },
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/setup", F(null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/setup", F(TestImza.Imzala("1.0.0", Sha(exe))))).StatusCode);  // yanlış etiket
        Assert.Equal(HttpStatusCode.NotFound, (await host.Anonymous().GetAsync("/api/setup/download")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/setup", F(TestImza.Imzala(PaketImzasi.KurulumAraciEtiketi, Sha(exe))))).StatusCode);
        Assert.Equal(exe, await (await host.Anonymous().GetAsync("/api/setup/download")).Content.ReadAsByteArrayAsync());
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d!.FullName;
    }
}
