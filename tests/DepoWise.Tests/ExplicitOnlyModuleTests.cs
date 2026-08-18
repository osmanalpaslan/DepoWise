using DepoWise.Application.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// YET (2026-08-18, kullanıcı kuralı) — "AÇIK-VERİLİR" MODÜL KATMANI.
///
/// <b>İstenen:</b> "Yerel Veri Sıfırlama" yetki ağacında bir menü maddesi olsun; <b>Süper Admin veya
/// Kısıtlı Süper Admin</b> bunu bir role/kullanıcıya verebilsin, <b>yetkiyi alan da kendi altına
/// devredebilsin</b>; ama <b>hiç kimse örtük olarak ALMASIN</b>.
///
/// <b>Neden yeni bir katman gerekti:</b> sistemde yalnız iki uç vardı —
/// <see cref="AppModules.IsSuperAdminOnly"/> (hiç devredilemez) ve normal modül (firma adminine
/// <b>admin bypass</b> ile örtük açık). İstenen ikisi de değildi.
///
/// Bu testler hem ERİŞİMİ (<see cref="AccessControl.Can"/>) hem DEVRİ
/// (<see cref="AccessControl.CanGrantModule"/> / <see cref="AccessControl.GrantCeiling"/>) kilitler.
/// </summary>
public class ExplicitOnlyModuleTests
{
    private const string M = "local_reset";
    private const string Co = "DEPOWISE";

    private static SessionContext Oturum(string rol, PermissionSet? izinler = null)
        => new("u-" + rol, Co, new[] { rol }, izinler ?? PermissionSet.Empty);

    /// <summary>Bu modülde TAM yetki verilmiş kullanıcı.</summary>
    private static PermissionSet Verilmis()
        => new(new[] { new ModulePermission(M, true, true, true, true) }, Array.Empty<string>());

    // ── Katalog ──────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Modul_Katalogda_Ve_AcikVerilir()
    {
        Assert.Contains(AppModules.All, m => m.Key == M);
        Assert.True(AppModules.IsExplicitOnly(M));
        Assert.False(AppModules.IsSuperAdminOnly(M));   // devredilebilir olmalı
        Assert.False(AppModules.IsPublic(M));
    }

    /// <summary>Ekran menü kataloğunda ve WEB'de tanımlı olmalı (kullanıcı: "menü ağacına eklenmeli").</summary>
    [Fact]
    public void Ekran_Menude_Tanimli()
    {
        var ekran = AppScreens.All.SingleOrDefault(e => e.ModuleKey == M);
        Assert.NotNull(ekran);
        Assert.Equal("local-reset", ekran!.WebRoute);
        Assert.True(ekran.Platforms.HasFlag(ScreenPlatform.Web));
        Assert.Null(ekran.WebPermOverride);   // yetki normal modül kapısından geçer, "@super" kestirmesi YOK
    }

    // ── Erişim: örtük ALINMAZ ────────────────────────────────────────────────────────────────────
    [Fact]
    public void FirmaAdmini_Ortuk_ALAMAZ()
    {
        var admin = Oturum(RoleKeys.CompanyAdmin);

        Assert.False(AccessControl.Can(admin, M, PermissionAction.View));
        Assert.False(AccessControl.Can(admin, M, PermissionAction.Create));
        // Karşılaştırma: normal bir modülde admin bypass'ı AYNEN çalışmaya devam eder (regresyon yok).
        Assert.True(AccessControl.Can(admin, "materials", PermissionAction.Create));
    }

    [Fact]
    public void SuperAdmin_Daima_Erisir()
    {
        var su = Oturum(RoleKeys.SuperAdmin);
        Assert.True(AccessControl.Can(su, M, PermissionAction.Create));
    }

    [Fact]
    public void Acikca_Verilen_Erisir()
    {
        var admin = Oturum(RoleKeys.CompanyAdmin, Verilmis());
        var personel = Oturum(RoleKeys.Staff, Verilmis());

        Assert.True(AccessControl.Can(admin, M, PermissionAction.Create));
        Assert.True(AccessControl.Can(personel, M, PermissionAction.Create));
    }

    [Fact]
    public void Yetkisiz_Personel_Erisemez()
    {
        var personel = Oturum(RoleKeys.Staff);
        Assert.False(AccessControl.Can(personel, M, PermissionAction.View));
    }

    // ── Devir zinciri ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void SuperAdmin_Ve_KisitliSuperAdmin_Verebilir()
    {
        Assert.True(AccessControl.CanGrantModule(Oturum(RoleKeys.SuperAdmin), M));
        Assert.True(AccessControl.CanGrantModule(Oturum(RoleKeys.RestrictedSuperAdmin), M));
    }

    /// <summary>Zincirin özü: yetkiyi ALAN, aşağıya devredebilir.</summary>
    [Fact]
    public void Yetkiyi_Alan_Asagiya_Verebilir()
    {
        var adminVerilmis = Oturum(RoleKeys.CompanyAdmin, Verilmis());

        Assert.True(AccessControl.CanGrantModule(adminVerilmis, M));

        var tavan = AccessControl.GrantCeiling(adminVerilmis, M);
        Assert.True(tavan.CanView);
        Assert.True(tavan.CanCreate);
    }

    /// <summary>"İlk admin her şeyi verebilir" kestirmesi bu modülde UYGULANMAZ.</summary>
    [Fact]
    public void Acik_Izni_Olmayan_FirmaAdmini_Veremez()
    {
        var admin = Oturum(RoleKeys.CompanyAdmin);   // hiç açık izin satırı yok → "ilk admin"

        Assert.False(AccessControl.CanGrantModule(admin, M));
        var tavan = AccessControl.GrantCeiling(admin, M);
        Assert.False(tavan.CanView);
        Assert.False(tavan.CanCreate);

        // Regresyon: normal modülde ilk admin YİNE devredebilir (davranış korunur).
        Assert.True(AccessControl.CanGrantModule(admin, "materials"));
    }

    [Fact]
    public void Yetkisiz_Personel_Veremez()
    {
        Assert.False(AccessControl.CanGrantModule(Oturum(RoleKeys.Staff), M));
    }

    /// <summary>Devredilen yetki, devredenin kendi yetkisini AŞAMAZ (yalnız okuma verilmişse yalnız okuma verilir).</summary>
    [Fact]
    public void Devir_Tavani_Kendi_Yetkisini_Asamaz()
    {
        var yalnizOkuma = new PermissionSet(
            new[] { new ModulePermission(M, true, false, false, false) }, Array.Empty<string>());
        var admin = Oturum(RoleKeys.CompanyAdmin, yalnizOkuma);

        var tavan = AccessControl.GrantCeiling(admin, M);

        Assert.True(tavan.CanView);
        Assert.False(tavan.CanCreate);   // kendisinde yok → veremez
    }

    /// <summary>Rol Yetki Kontrol bu modülü de kapatabilmeli (kullanıcı: "rol bazlı yönetim").</summary>
    [Fact]
    public void Rol_Yetki_Kontrol_Kapatabilir()
    {
        var admin = Oturum(RoleKeys.CompanyAdmin, Verilmis());
        admin.BlockedModules = new HashSet<string>(StringComparer.Ordinal) { M };

        Assert.False(AccessControl.Can(admin, M, PermissionAction.Create));
        Assert.False(AccessControl.GrantCeiling(admin, M).CanCreate);
        // Yapısal kilit YOK: her role verilebilir olmalı (süper admin kararına bırakılır).
        Assert.False(DepoWise.Infrastructure.Organization.RoleGrantService.IsHardBlocked(M, RoleKeys.Staff));
    }
}
