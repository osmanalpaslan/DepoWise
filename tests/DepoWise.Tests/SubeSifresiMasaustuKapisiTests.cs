using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ H6 GÜVENLİK REGRESYONU (kullanıcı bildirimi, 2026-09-06) ═══
///
/// <para><b>Hata:</b> "Excel Merkezi'nde başka şubenin YANLIŞ şifresini girdiğim hâlde dosya
/// yükleme ekranı açıldı; şifre yanlış uyarısı verip durdurmalıydı."</para>
///
/// <para><b>Kök neden:</b> masaüstü, şube şifresini YEREL veritabanına soruyordu. Masaüstündeki şube
/// aynası şifre karmasını (bilinçli olarak) taşımaz — karmaların istemci makinelere kopyalanması
/// çevrimdışı kırma riski doğurur. Karma yerelde boş olunca
/// <see cref="BranchService.VerifyBranchPassword"/> "şifre tanımlı değil → serbest" deyip
/// <c>true</c> dönüyor, yani <b>her yanlış şifre kabul ediliyordu</b>.</para>
///
/// <para>Aşağıdaki iki test bu mekanizmayı GERÇEKTEN kurup gösterir; üçüncüsü de masaüstünün
/// artık yerel doğrulamaya dönmediğini kaynak üzerinden korur.</para>
/// </summary>
public class SubeSifresiMasaustuKapisiTests : IDisposable
{
    private readonly List<string> _dosyalar = new();
    private readonly TestClock _clock = new();
    private const string Firma = "A";

    private SqliteConnectionFactory YeniVeritabani()
    {
        var p = Path.Combine(Path.GetTempPath(), "depowise_h6_" + Guid.NewGuid().ToString("N") + ".db");
        _dosyalar.Add(p);
        var f = new SqliteConnectionFactory(p);
        new MigrationRunner(f).Run();
        return f;
    }

    private SessionContext Yonetici(SqliteConnectionFactory f)
    {
        var users = new UserService(f, _clock);
        var id = users.EnsureInitialAdmin(Firma, "admin", "Test!2026", RoleKeys.CompanyAdmin);
        return new SessionContext(id, Firma, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    /// <summary>SUNUCUDA doğrulama doğru çalışır: doğru şifre geçer, yanlış şifre GEÇMEZ.</summary>
    [Fact]
    public void Sunucuda_YanlisSubeSifresi_Reddedilir()
    {
        var f = YeniVeritabani();
        var s = Yonetici(f);
        var branches = new BranchService(f, _clock);
        var duzce = branches.Create(s, new NewBranch("DÜZCE", Password: "Sube!2026"));

        Assert.True(branches.VerifyBranchPassword(Firma, duzce, "Sube!2026"));
        Assert.False(branches.VerifyBranchPassword(Firma, duzce, "yanlis-sifre"));
        Assert.False(branches.VerifyBranchPassword(Firma, duzce, ""));
        Assert.False(branches.VerifyBranchPassword(Firma, duzce, null));
    }

    /// <summary>
    /// ⭐ HATANIN KENDİSİ: şube masaüstüne AYNALANDIĞINDA şifre karması taşınmaz; bu yüzden
    /// aynı doğrulama YERELDE her şifreyi kabul eder. Bu davranış bilinçlidir (karma istemciye
    /// kopyalanmamalı) — bu yüzden masaüstü ASLA yerelden doğrulamamalıdır.
    /// </summary>
    [Fact]
    public void Aynada_SifreKarmasi_Tasinmaz_VeYerelDogrulama_HerSifreyiKabulEder()
    {
        // Sunucu tarafı: şifreli şube.
        var sunucu = YeniVeritabani();
        var s = Yonetici(sunucu);
        var sunucuBranches = new BranchService(sunucu, _clock);
        var duzce = sunucuBranches.Create(s, new NewBranch("DÜZCE", Password: "Sube!2026"));
        Assert.False(sunucuBranches.VerifyBranchPassword(Firma, duzce, "yanlis-sifre"));   // sunucuda güvenli

        // Masaüstü tarafı: aynı şube AYNALANIR (gerçek ayna kodu ile).
        var masaustu = YeniVeritabani();
        _ = Yonetici(masaustu);   // masaustunde firma/kullanici kaydi da vardir (yabanci anahtar)
        BranchMirrorApply.Run(masaustu, Firma, new[]
        {
            new BranchMirrorApply.Row(duzce, "DÜZCE", null, "branch", null),
        });

        var yerel = new BranchService(masaustu, _clock);
        Assert.True(yerel.VerifyBranchPassword(Firma, duzce, "yanlis-sifre"),
            "Aynada karma taşınmadığı için yerel doğrulama her şifreyi kabul eder — " +
            "masaüstü bu yüzden yerelden DEĞİL sunucudan doğrulamalıdır (H6).");
    }

    /// <summary>
    /// Masaüstü Excel Merkezi artık yerel doğrulamayı KULLANMAZ; sunucu ucunu çağırır ve
    /// sunucuya ulaşılamadığında (yanıt yok) şifreyi KABUL ETMEZ.
    /// (Masaüstü projesi test projesinden referanslanmadığı için kaynak sözleşmesi denetlenir.)
    /// </summary>
    [Fact]
    public void ExcelMerkezi_YerelDogrulamaya_Donmez_CevrimdisindaKabulEtmez()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        Assert.NotNull(d);
        var kaynak = File.ReadAllText(Path.Combine(d!.FullName,
            "src", "DepoWise.Desktop", "ViewModels", "ImportExportViewModel.cs"));

        var kodSatirlari = kaynak.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)
                     && !l.TrimStart().StartsWith("///", StringComparison.Ordinal));
        var kod = string.Join("\n", kodSatirlari);

        Assert.DoesNotContain("VerifyBranchPassword", kod);              // yerel doğrulama YOK
        Assert.Contains("ServerAuthClient.VerifyBranchAsync", kod);      // yetkili kaynak: sunucu

        // Çevrimdışı (bool? = null) dalı kabul ETMEmeli: üç durumun üçü de ele alınmış olmalı.
        Assert.Contains("true => null,", kod);
        Assert.Contains("şube şifresi hatalı", kod);
        Assert.Contains("sunucuya ulaşılamıyor", kod);

        // Sonuç beklenirken buton kapalı kalmalı (fail-closed).
        Assert.Contains("if (SifreDogrulaniyor) return", kod);
    }


    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }
    public void Dispose()
    {
        foreach (var p in _dosyalar)
            try { File.Delete(p); } catch { }
    }
}
