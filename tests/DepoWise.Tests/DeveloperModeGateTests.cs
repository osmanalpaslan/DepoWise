using DepoWise.Application.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ SEC-03 · GELİŞTİRİCİ MODU YALNIZ SÜPER ADMİNE AÇIK ═══
///
/// <b>Sorun (denetim 2026-08-25):</b> masaüstünde <i>Ayarlar › Geliştirici Modu</i> ekranını açabilen
/// <b>herhangi bir kullanıcı</b> (yalnız <c>settings</c> görüntüleme yetkisi yeterliydi) kaynak kodda
/// SABİT yazan kodu girerek <c>DeveloperMode.IsActive</c>'i açıyordu. Bu bayrak
/// <see cref="AccessControl"/>'ün HER kararında süper admin gibi davranıyor → o oturumda tüm ekranlar,
/// tüm işlemler ve tüm özel butonlar açılıyordu. Kodu doğrulayan yerde <b>rol kontrolü hiç yoktu</b>
/// ve depo herkese açık olduğu için kod da herkese açıktı.
///
/// <b>Kapı neden <c>AccessControl.IsAdmin</c> OLAMAZ:</b> o metot <c>DeveloperMode.IsActive</c>'i de
/// sayar → mod bir kez açıldığında kapı kendi kendini açık tutardı (döngüsel yetki). Kapı, oturumun
/// HAM rol bilgisine (<see cref="SessionContext.IsSuperAdmin"/>) bakmak zorundadır.
///
/// Bu testler <b>karar fonksiyonunu</b> (saf, yan etkisiz) ve gerçek etkinleştirme yolunu doğrular.
/// </summary>
public class DeveloperModeGateTests : IDisposable
{
    private static SessionContext S(params string[] roles)
        => new("u1", "CO", roles, PermissionSet.Empty);

    private static SessionContext Personel() => S(RoleKeys.Staff);
    private static SessionContext FirmaAdmin() => S(RoleKeys.CompanyAdmin);
    private static SessionContext KisitliSuper() => S(RoleKeys.RestrictedSuperAdmin);
    private static SessionContext SuperAdmin() => S(RoleKeys.SuperAdmin);

    // ═════════════════════════════════════════════════════════════════════════════════════════
    //  KARAR FONKSİYONU — yan etkisiz, paralel koşuda güvenli
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>⭐ SEC-03a — DEPO/PERSONEL kullanıcısı geliştirici modunu AÇAMAZ.</summary>
    [Fact]
    public void SEC03a_Personel_Acamaz() => Assert.False(DeveloperMode.CanActivate(Personel()));

    /// <summary>⭐ SEC-03b — FİRMA ADMİNİ de açamaz (süper admin değildir).</summary>
    [Fact]
    public void SEC03b_Firma_Admini_Acamaz() => Assert.False(DeveloperMode.CanActivate(FirmaAdmin()));

    /// <summary>SEC-03c — KISITLI süper admin de açamaz (devredilemez yetkidir).</summary>
    [Fact]
    public void SEC03c_Kisitli_Super_Admin_Acamaz() => Assert.False(DeveloperMode.CanActivate(KisitliSuper()));

    /// <summary>SEC-03d — GERÇEK süper admin açabilir (mevcut davranış korunur).</summary>
    [Fact]
    public void SEC03d_Super_Admin_Acabilir() => Assert.True(DeveloperMode.CanActivate(SuperAdmin()));

    // ═════════════════════════════════════════════════════════════════════════════════════════
    //  ETKİNLEŞTİRME YOLU
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ SEC-03e — <b>KODU BİLSE BİLE</b> yetkisiz kullanıcı etkinleştiremez ve küresel bayrak
    /// KİRLENMEZ. (Kod depoda açık olduğu için "kimse bilmez" bir varsayım değildir.)
    /// </summary>
    [Fact]
    public void SEC03e_Kodu_Bilse_Bile_Yetkisiz_Etkinlestiremez()
    {
        Assert.False(DeveloperMode.TryActivate(Personel(), DeveloperMode.Code));
        Assert.False(DeveloperMode.TryActivate(FirmaAdmin(), DeveloperMode.Code));
        Assert.False(DeveloperMode.IsActive);     // başarısız deneme bayrağa DOKUNMAZ
    }

    /// <summary>SEC-03f — süper admin YANLIŞ kodla etkinleştiremez (kod kontrolü korunur).</summary>
    [Fact]
    public void SEC03f_Super_Admin_Yanlis_Kodla_Etkinlestiremez()
    {
        Assert.False(DeveloperMode.TryActivate(SuperAdmin(), "000000"));
        Assert.False(DeveloperMode.TryActivate(SuperAdmin(), null));
        Assert.False(DeveloperMode.IsActive);
    }

    /// <summary>
    /// SEC-03g — süper admin DOĞRU kodla etkinleştirir ve kapatabilir (mevcut davranış birebir korunur).
    ///
    /// ⚠️ <c>IsActive</c> KÜRESEL bir bayraktır; bu test onu kısa süreliğine açar ve <c>finally</c> ile
    /// mutlaka kapatır. Bayrağı açan TEK test budur — paralel koşuda yan etkiyi en aza indirmek için
    /// diğer tüm senaryolar yan etkisiz <see cref="DeveloperMode.CanActivate"/> ile yazılmıştır.
    /// </summary>
    [Fact]
    public void SEC03g_Super_Admin_Acar_Ve_Kapatir()
    {
        try
        {
            Assert.True(DeveloperMode.TryActivate(SuperAdmin(), DeveloperMode.Code));
            Assert.True(DeveloperMode.IsActive);
        }
        finally { DeveloperMode.IsActive = false; }

        Assert.False(DeveloperMode.IsActive);
    }

    /// <summary>
    /// ⭐ SEC-03h — <b>DÖNGÜSEL YETKİ OLMAMALI:</b> mod açıkken bile yetkisiz kullanıcı için karar
    /// hâlâ HAYIR olmalı. Kapı <c>AccessControl.IsAdmin</c> ile yazılsaydı (o, IsActive'i sayar)
    /// mod bir kez açıldığında herkes açabilir hâle gelirdi.
    /// </summary>
    [Fact]
    public void SEC03h_Mod_Acikken_Bile_Yetkisiz_Icin_Karar_Degismez()
    {
        try
        {
            DeveloperMode.IsActive = true;
            Assert.False(DeveloperMode.CanActivate(Personel()));
            Assert.False(DeveloperMode.CanActivate(FirmaAdmin()));
        }
        finally { DeveloperMode.IsActive = false; }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    //  KAYNAK KİLİDİ — kapı gerçekten BAĞLANDI mı? (UI gizleme tek başına güvenlik değildir)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    private static string Repo()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "DepoWise.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("DepoWise.sln bulunamadı.");
    }

    private static string Oku(params string[] parcalar) => File.ReadAllText(Path.Combine(Repo(), Path.Combine(parcalar)));

    /// <summary>SEC-03i — MASAÜSTÜ: etkinleştirme ekranı kapıyı çağırmalı (kendi kuralını yazmamalı).</summary>
    [Fact]
    public void SEC03i_Masaustu_Ekrani_Kapiyi_Cagirir()
    {
        var src = Oku("src", "DepoWise.Desktop", "ViewModels", "DeveloperSettingsViewModel.cs");
        Assert.Contains("DeveloperMode.TryActivate", src);
        // Kodu ekranda ELLE karşılaştırıp bayrağı doğrudan set etmek YASAK (kapı atlanmış olur).
        Assert.DoesNotContain("DeveloperMode.IsActive = true", src);
    }

    /// <summary>SEC-03j — MASAÜSTÜ: ekrana GEZİNME de kapılı olmalı (menüden gizlemek yetmez).</summary>
    [Fact]
    public void SEC03j_Masaustu_Gezinme_Kapili()
    {
        var src = Oku("src", "DepoWise.Desktop", "ViewModels", "ShellViewModel.cs");
        var i = src.IndexOf("case \"settings:developer\":", StringComparison.Ordinal);
        Assert.True(i > 0, "settings:developer gezinme kaydı bulunamadı");
        var blok = src.Substring(i, Math.Min(400, src.Length - i));
        Assert.Contains("DeveloperMode.CanActivate", blok);
    }

    /// <summary>SEC-03k — SUNUCU: geliştirici modu ucu süper admin istemeli (eskiden admin yetiyordu).</summary>
    [Fact]
    public void SEC03k_Sunucu_Ucu_Super_Admin_Ister()
    {
        var src = Oku("src", "DepoWise.Api", "Program.cs");
        var i = src.IndexOf("app.MapPost(\"/api/settings/developer\"", StringComparison.Ordinal);
        Assert.True(i > 0, "/api/settings/developer POST ucu bulunamadı");
        var blok = src.Substring(i, Math.Min(700, src.Length - i));
        Assert.Contains("IsSuperAdmin", blok);
        // AccessControl.IsAdmin döngüsel kapıdır (DeveloperMode.IsActive'i sayar) → kullanılmamalı.
        Assert.DoesNotContain("AccessControl.IsAdmin(s)", blok);
    }

    /// <summary>SEC-03l — WEB: sayfa ve menü kaydı süper admine kilitli olmalı.</summary>
    [Fact]
    public void SEC03l_Web_Sayfasi_Ve_Menu_Super_Admine_Kilitli()
    {
        var sayfa = Oku("src", "DepoWise.Web", "Components", "Pages", "Developer.razor");
        Assert.Contains("Auth.IsSuperAdmin", sayfa);
        Assert.DoesNotContain("if (!Auth.IsAdmin)", sayfa);

        var katalog = Oku("src", "DepoWise.Application", "Security", "AppScreens.cs");
        var i = katalog.IndexOf("\"settings.developer\"", StringComparison.Ordinal);
        Assert.True(i > 0, "settings.developer ekran kaydı bulunamadı");
        var satir = katalog.Substring(i, Math.Min(220, katalog.Length - i));
        Assert.Contains("@super", satir);
    }

    public void Dispose() => DeveloperMode.IsActive = false;   // her ihtimale karşı temiz bırak
}
