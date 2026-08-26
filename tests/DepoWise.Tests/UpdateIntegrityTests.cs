using DepoWise.Application.Update;
using DepoWise.Infrastructure.Update;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ UPD-01 · GÜNCELLEME PAKETİ BÜTÜNLÜĞÜ ═══ (denetim 2026-08-26)
///
/// <b>Bulunan açık:</b> masaüstü kurulumcusu şunu yazıyordu —
/// <c>if (!string.IsNullOrWhiteSpace(expectedSha) &amp;&amp; !VerifyChecksum(...)) throw;</c>
/// Yani sunucudan <b>BOŞ</b> checksum gelirse doğrulama TAMAMEN atlanıyor, indirilen zip olduğu gibi
/// açılıp uygulamanın kurulum dizinine kopyalanıyor ve uygulama yeniden başlatılıyordu. Bu, güncelleme
/// yolunu "sunucudan ne gelirse onu çalıştır"a çeviren bir <b>kod çalıştırma</b> yoludur: bozuk/yarım
/// indirme, hatalı sürüm kaydı ya da araya giren bir aktör aynı kapıdan geçerdi.
///
/// Sunucu tarafı yayında 64 hane hex zaten zorunlu (<c>ReleaseService.Publish</c>) — ama istemci
/// sunucudan gelen cevaba KOŞULSUZ güveniyordu. Kapı artık fail-closed: checksum yoksa kurulum yok.
/// </summary>
public class UpdateIntegrityTests
{
    private static readonly byte[] Paket = System.Text.Encoding.UTF8.GetBytes("DEPOWISE-TEST-PAKET");
    private static string DogruSha => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Paket));

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "DepoWise.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("DepoWise.sln bulunamadı.");
    }

    // ── 1) Kapının kendisi ────────────────────────────────────────────────────────────────────
    [Fact]
    public void UPD01_Bos_Checksum_Kurulumu_Engeller()
    {
        var ex = Assert.Throws<UpdateFailedException>(() => UpdateService.RequireVerifiedPackage(Paket, ""));
        Assert.Contains("checksum", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UPD01_Null_Checksum_Kurulumu_Engeller()
        => Assert.Throws<UpdateFailedException>(() => UpdateService.RequireVerifiedPackage(Paket, null));

    [Fact]
    public void UPD01_Bosluk_Checksum_Kurulumu_Engeller()
        => Assert.Throws<UpdateFailedException>(() => UpdateService.RequireVerifiedPackage(Paket, "   "));

    [Fact]
    public void UPD01_Yanlis_Checksum_Kurulumu_Engeller()
        => Assert.Throws<UpdateFailedException>(() =>
               UpdateService.RequireVerifiedPackage(Paket, new string('A', 64)));

    /// <summary>Yarım inen paket: baytlar eksik → checksum tutmaz → kurulum yok.</summary>
    [Fact]
    public void UPD01_Yarim_Inen_Paket_Kurulmaz()
        => Assert.Throws<UpdateFailedException>(() =>
               UpdateService.RequireVerifiedPackage(Paket[..(Paket.Length - 3)], DogruSha));

    /// <summary>KİLİT: doğru checksum'da davranış DEĞİŞMEZ (mevcut 1.0.149 akışı bozulmasın).</summary>
    [Fact]
    public void UPD01_Dogru_Checksum_Gecer()
    {
        UpdateService.RequireVerifiedPackage(Paket, DogruSha);
        UpdateService.RequireVerifiedPackage(Paket, DogruSha.ToLowerInvariant());   // hex büyük/küçük fark etmez
    }

    // ── 2) Masaüstü kurulumcusu gerçekten bu kapıdan geçiyor mu (kaynak kilidi) ────────────────
    /// <summary>
    /// Kural yalnız <c>UpdateService</c>'te dururken kurulumcu eski satırı kullanmaya devam ederse
    /// açık kapanmış olmaz. Masaüstü projesi test projesinden referanslanmadığı için (Windows GUI)
    /// bu kilit kaynak üzerinden kurulur.
    /// </summary>
    [Fact]
    public void UPD01_Masaustu_Kurulumcusu_Kapiyi_Kullanir()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "src", "DepoWise.Desktop", "UpdateInstaller.cs"));

        Assert.Contains("UpdateService.RequireVerifiedPackage(zipBytes, expectedSha)", src);
        // Eski "boşsa atla" kalıbı geri gelmemeli.
        Assert.DoesNotContain("IsNullOrWhiteSpace(expectedSha)", src);
    }

    // ── 3) Sürüm karşılaştırma (mevcut davranışın kilidi) ─────────────────────────────────────
    private static UpdatePackage Pkt(string v, string min = "0.0.0")
        => new(v, DogruSha, Paket.Length, min, null, false);

    [Fact]
    public void UPD01_Surum_Karsilastirmasi_Dogru()
    {
        var kok = Path.Combine(Path.GetTempPath(), "dw_upd_" + Guid.NewGuid().ToString("N"));
        try
        {
            var svc = new UpdateService(kok);
            File.WriteAllText(Path.Combine(kok, "current.txt"), "1.0.149");

            Assert.True(svc.Check(Pkt("1.0.150")).UpdateAvailable);    // ileri sürüm → var
            Assert.False(svc.Check(Pkt("1.0.149")).UpdateAvailable);   // aynı sürüm → yok
            Assert.False(svc.Check(Pkt("1.0.148")).UpdateAvailable);   // geri sürüm → yok (downgrade)
            Assert.False(svc.Check(null).UpdateAvailable);             // kayıt yok → yok
            Assert.False(svc.Check(Pkt("bozuk-surum")).UpdateAvailable); // bozuk kayıt → yok (çökme yok)

            // Minimum desteklenen sürüm: mevcut sürüm altındaysa işaretlenir.
            Assert.True(svc.Check(Pkt("1.0.150", min: "1.0.150")).BelowMinSupported);
            Assert.False(svc.Check(Pkt("1.0.150", min: "1.0.0")).BelowMinSupported);
        }
        finally { try { Directory.Delete(kok, true); } catch { } }
    }
}
