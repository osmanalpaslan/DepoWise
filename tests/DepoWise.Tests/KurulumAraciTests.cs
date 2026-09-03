using System.Security.Cryptography;
using System.Text;
using DepoWise.Application.Setup;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ KURULUM ARACI (SETUP) — GÜVENLİK + ÇİFT İNDİRME (2026-09-04) ═══
///
/// İki gerçek kusur kapatıldı; bu testler ikisinin de geri dönmesini engeller:
///
///  1. <b>Paket doğrulanmıyordu.</b> Sunucu SHA-256'yı veriyor ve yayında 64 hane hex zorunlu, ama
///     kurulum aracı bu alanı hiç okumuyordu → "indirilen ne ise onu kur". Aynı açık uygulama içi
///     güncelleyicide 2026-08-26'da kapatılmıştı (UPD-01); kurulum kapısı açık kalmıştı.
///  2. <b>Taze kurulumdan sonra aynı ~86 MB tekrar iniyordu.</b> Kurulum aracı sürüm durumunu
///     (<c>current.txt</c>) yazmıyordu → uygulama kendini 0.0.0 sanıyordu.
///
///  KUR1-KUR6  → checksum kapısı (doğru/yanlış/eksik/bozuk biçim/bozuk dosya/büyük dosya)
///  KUR7-KUR10 → indirme adresi kapısı (HTTPS + host)
///  KUR11-KUR13→ çift indirme: sürüm durumu yazımı ve "zaten güncel" davranışı
///  KUR14-KUR16→ manifest okuma + geriye uyumluluk (manifest ucu yokken de çalışmalı)
///  KUR17-KUR20→ sistem ön-koşulları
/// </summary>
public class KurulumAraciTests : IDisposable
{
    private readonly string _dir;

    public KurulumAraciTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "alpnex_setup_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>İçeriği verilen bir dosya üretir ve gerçek SHA-256'sını döndürür.</summary>
    private (string Path, string Sha, long Size) Paket(byte[] icerik, string ad = "paket.zip")
    {
        var p = Path.Combine(_dir, ad);
        File.WriteAllBytes(p, icerik);
        return (p, Convert.ToHexString(SHA256.HashData(icerik)), icerik.Length);
    }

    // ── 1) CHECKSUM KAPISI ─────────────────────────────────────────────────────────────────

    [Fact]
    public void KUR1_Dogru_Checksum_Kuruluma_Izin_Verir()
    {
        var (p, sha, size) = Paket(Encoding.UTF8.GetBytes("alpnex paketi"));
        SetupPackageVerifier.RequireVerifiedPackage(p, sha, size);   // patlamamalı
        Assert.True(File.Exists(p));                                  // dosya KORUNUR
    }

    [Fact]
    public void KUR2_Yanlis_Checksum_KURULUM_YOK_ve_dosya_silinir()
    {
        var (p, _, size) = Paket(Encoding.UTF8.GetBytes("alpnex paketi"));
        var yanlis = new string('a', 64);

        var ex = Assert.Throws<SetupVerificationException>(
            () => SetupPackageVerifier.RequireVerifiedPackage(p, yanlis, size));

        Assert.Equal("CHECKSUM_UYUSMADI", ex.Code);
        Assert.False(File.Exists(p));                                 // bozuk paket diskte BIRAKILMAZ
    }

    [Fact]
    public void KUR3_Checksum_YOKSA_KURULUM_YOK()      // fail-closed (UPD-01 ile aynı kural)
    {
        var (p, _, size) = Paket(Encoding.UTF8.GetBytes("x"));
        foreach (var bos in new string?[] { null, "", "   " })
        {
            var (p2, _, s2) = Paket(Encoding.UTF8.GetBytes("x"), Guid.NewGuid().ToString("N") + ".zip");
            var ex = Assert.Throws<SetupVerificationException>(
                () => SetupPackageVerifier.RequireVerifiedPackage(p2, bos, s2));
            Assert.Equal("CHECKSUM_YOK", ex.Code);
        }
        Assert.True(File.Exists(p));   // bu dosyaya dokunulmadı (kontrol)
    }

    [Theory]
    [InlineData("abc")]                                   // çok kısa
    [InlineData("zz34567890123456789012345678901234567890123456789012345678901234")]  // hex değil
    [InlineData("00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff0")] // 65 hane
    public void KUR4_Bozuk_Checksum_Bicimi_Reddedilir(string bozuk)
    {
        var (p, _, size) = Paket(Encoding.UTF8.GetBytes("x"));
        Assert.False(SetupPackageVerifier.IsValidChecksumFormat(bozuk));
        var ex = Assert.Throws<SetupVerificationException>(
            () => SetupPackageVerifier.RequireVerifiedPackage(p, bozuk, size));
        Assert.Equal("CHECKSUM_YOK", ex.Code);
    }

    [Fact]
    public void KUR5_Eksik_Indirme_Boyut_Kontrolunde_Yakalanir()
    {
        // Yarım inen dosya: checksum hesaplamaya bile gerek yok, boyut tutmuyor.
        var tam = Encoding.UTF8.GetBytes(new string('A', 5000));
        var beklenenSha = Convert.ToHexString(SHA256.HashData(tam));
        var (p, _, _) = Paket(Encoding.UTF8.GetBytes(new string('A', 2000)));   // yarım

        var ex = Assert.Throws<SetupVerificationException>(
            () => SetupPackageVerifier.RequireVerifiedPackage(p, beklenenSha, tam.Length));

        Assert.Equal("BOYUT_UYUSMADI", ex.Code);
        Assert.False(File.Exists(p));
    }

    [Fact]
    public void KUR6_Buyuk_Dosya_Akisla_Dogrulanir_Bellege_Alinmaz()
    {
        // 8 MB: gerçek paket ~86 MB. Amaç, doğrulamanın akış üzerinden çalıştığını göstermek.
        var buyuk = new byte[8 * 1024 * 1024];
        new Random(1234).NextBytes(buyuk);
        var (p, sha, size) = Paket(buyuk, "buyuk.zip");

        SetupPackageVerifier.RequireVerifiedPackage(p, sha.ToLowerInvariant(), size);  // harf duyarsız
        Assert.True(File.Exists(p));
    }

    // ── 2) İNDİRME ADRESİ KAPISI ───────────────────────────────────────────────────────────

    [Fact]
    public void KUR7_Goreli_Adres_Sunucu_Koküne_Eklenir()
    {
        var u = SetupUrlPolicy.ResolveDownloadUrl("https://depowise-erp.fly.dev", "/api/releases/1.0.171/download");
        Assert.Equal("https://depowise-erp.fly.dev/api/releases/1.0.171/download", u.ToString());
    }

    [Fact]
    public void KUR8_HTTP_Adres_Reddedilir()
    {
        var ex = Assert.Throws<SetupVerificationException>(
            () => SetupUrlPolicy.ResolveDownloadUrl("https://depowise-erp.fly.dev", "http://depowise-erp.fly.dev/x.zip"));
        Assert.Equal("SEMA_GUVENSIZ", ex.Code);
    }

    [Fact]
    public void KUR9_Yabanci_Host_Reddedilir()
    {
        var ex = Assert.Throws<SetupVerificationException>(
            () => SetupUrlPolicy.ResolveDownloadUrl("https://depowise-erp.fly.dev", "https://baska-site.example/x.zip"));
        Assert.Equal("HOST_IZINSIZ", ex.Code);
    }

    [Fact]
    public void KUR10_Bos_Adres_Reddedilir()
    {
        var ex = Assert.Throws<SetupVerificationException>(
            () => SetupUrlPolicy.ResolveDownloadUrl("https://depowise-erp.fly.dev", null));
        Assert.Equal("ADRES_YOK", ex.Code);
    }

    // ── 3) ÇİFT İNDİRME ────────────────────────────────────────────────────────────────────

    [Fact]
    public void KUR11_Kurulum_Surum_Durumunu_Yazar()
    {
        var kok = Path.Combine(_dir, "update");
        SetupInstallState.WriteInstalledVersion(kok, "1.0.171");

        var dosya = SetupInstallState.CurrentVersionFile(kok);
        Assert.True(File.Exists(dosya));
        Assert.Equal("1.0.171", File.ReadAllText(dosya));      // satır sonu YOK
        Assert.Equal("1.0.171", SetupInstallState.ReadInstalledVersion(kok));
    }

    [Fact]
    public void KUR12_TAZE_KURULUM_SONRASI_TEKRAR_INDIRME_YOK()
    {
        // Bu test asıl kusuru kanıtlar: kurulum aracı sürümü yazarsa uygulama "zaten güncel" der.
        var kok = Path.Combine(_dir, "update");
        SetupInstallState.WriteInstalledVersion(kok, "1.0.171");

        var kurulu = SetupInstallState.ReadInstalledVersion(kok);

        // Uygulamanın güncelleme karşılaştırması: kurulu == sunucudaki  → indirme YOK
        Assert.Equal("1.0.171", kurulu);
        Assert.NotEqual("0.0.0", kurulu);   // ESKİ HATALI DAVRANIŞ buydu → 86 MB tekrar inerdi
    }

    [Fact]
    public void KUR13_Surum_Yazilmamissa_Okuma_Null_Doner()
    {
        var kok = Path.Combine(_dir, "bos_update");
        Assert.Null(SetupInstallState.ReadInstalledVersion(kok));   // eski davranış: 0.0.0 sanılırdı
    }

    // ── 4) MANIFEST + GERİYE UYUMLULUK ────────────────────────────────────────────────────

    [Fact]
    public void KUR14_Mevcut_Releases_Latest_Yanitindan_Manifest_Uretilir()
    {
        // Sunucunun BUGÜN döndürdüğü biçim (manifest ucu henüz yok → geri düşüş yolu)
        var json = """
        {"version":"1.0.171","checksum":"C7B2C59B0DD8AA1FCD0B0AA1B10EB6D8FF363813E7BDAA18F263B35F95B3AA73",
         "sizeBytes":90547562,"minSupportedVersion":"0.0.0","signed":false,
         "downloadUrl":"/api/releases/1.0.171/download"}
        """;

        var m = SetupManifestReader.FromReleasesLatest(json);

        Assert.Equal(0, m.ManifestVersion);                 // 0 = geri düşüşle üretildi
        Assert.Equal("1.0.171", m.Application.Version);
        Assert.Equal(90547562, m.Application.SizeBytes);
        Assert.True(SetupPackageVerifier.IsValidChecksumFormat(m.Application.Sha256));
        Assert.Empty(m.Dependencies);                        // BUGÜN kurulacak bağımlılık YOK
        Assert.NotEmpty(m.Requirements);                     // ama ön-koşullar var
    }

    [Fact]
    public void KUR15_Sunucuda_Surum_Yoksa_Anlasilir_Hata()
    {
        foreach (var bos in new[] { "null", "", "   " })
        {
            var ex = Assert.Throws<SetupVerificationException>(() => SetupManifestReader.FromReleasesLatest(bos));
            Assert.Equal("SURUM_YOK", ex.Code);
        }
    }

    [Fact]
    public void KUR16_Yeni_Manifest_Okunur_ve_Bagimlilik_Eklenebilir()
    {
        // Gelecekte bir bağımlılık gelirse KOD DEĞİL, yalnız manifest değişecek — bunu kanıtlar.
        var json = """
        {"manifestVersion":1,
         "application":{"version":"1.0.172","downloadUrl":"/api/releases/1.0.172/download",
                        "sha256":"00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF",
                        "sizeBytes":123,"minSupportedVersion":"1.0.0"},
         "requirements":[{"id":"os_build","label":"Windows 10","value":14393}],
         "dependencies":[{"id":"webview2","name":"Microsoft Edge WebView2","required":true,"order":10,
                          "officialUrl":"https://example.invalid/w.exe","installerType":"exe",
                          "silentArgs":"/silent /install","requiresAdministrator":false}]}
        """;

        var m = SetupManifestReader.Parse(json);

        Assert.Equal(1, m.ManifestVersion);
        Assert.Equal("1.0.172", m.Application.Version);
        var d = Assert.Single(m.Dependencies);
        Assert.Equal("webview2", d.Id);
        Assert.True(d.Required);
        Assert.Equal("/silent /install", d.SilentArgs);
    }

    // ── 5) SİSTEM ÖN-KOŞULLARI ────────────────────────────────────────────────────────────

    private sealed class SahteProbe : ISystemProbe
    {
        public int OsBuild { get; init; } = 26200;
        public string Architecture { get; init; } = "X64";
        public long Free { get; init; } = 10L * 1024 * 1024 * 1024;
        public bool Writable { get; init; } = true;
        public bool NetworkAvailable { get; init; } = true;
        public long AvailableFreeBytes(string path) => Free;
        public bool CanWrite(string path) => Writable;
    }

    [Fact]
    public void KUR17_Uygun_Sistemde_Tum_On_Kosullar_Gecer()
    {
        var r = SetupPrerequisites.Check(new SahteProbe(), @"C:\x", SetupManifestReader.DefaultRequirements);
        Assert.True(SetupPrerequisites.AllOk(r));
        Assert.Null(SetupPrerequisites.FirstBlocker(r));
    }

    [Fact]
    public void KUR18_Eski_Windows_Engellenir()
    {
        var r = SetupPrerequisites.Check(new SahteProbe { OsBuild = 9600 },   // Windows 8.1
            @"C:\x", SetupManifestReader.DefaultRequirements);
        Assert.Equal("os", SetupPrerequisites.FirstBlocker(r)!.Id);
    }

    [Fact]
    public void KUR19_32bit_ve_Yetersiz_Disk_Engellenir()
    {
        var mimari = SetupPrerequisites.Check(new SahteProbe { Architecture = "X86" },
            @"C:\x", SetupManifestReader.DefaultRequirements);
        Assert.Equal("arch", SetupPrerequisites.FirstBlocker(mimari)!.Id);

        var disk = SetupPrerequisites.Check(new SahteProbe { Free = 50L * 1024 * 1024 },
            @"C:\x", SetupManifestReader.DefaultRequirements);
        Assert.Equal("disk", SetupPrerequisites.FirstBlocker(disk)!.Id);
    }

    [Fact]
    public void KUR20_Ag_Yoksa_ve_Yazma_Izni_Yoksa_Engellenir()
    {
        var ag = SetupPrerequisites.Check(new SahteProbe { NetworkAvailable = false },
            @"C:\x", SetupManifestReader.DefaultRequirements);
        Assert.Equal("network", SetupPrerequisites.FirstBlocker(ag)!.Id);

        var yazma = SetupPrerequisites.Check(new SahteProbe { Writable = false },
            @"C:\x", SetupManifestReader.DefaultRequirements);
        Assert.Equal("write", SetupPrerequisites.FirstBlocker(yazma)!.Id);
    }

    [Fact]
    public void KUR21_Disk_Olculemezse_Engellenmez()
    {
        // Ölçülemeyen disk yanlış negatif üretip kurulumu bloke ETMEMELİ.
        var r = SetupPrerequisites.Check(new SahteProbe { Free = -1 }, @"C:\x",
            SetupManifestReader.DefaultRequirements);
        Assert.True(SetupPrerequisites.AllOk(r));
    }
}
