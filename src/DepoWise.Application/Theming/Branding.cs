namespace DepoWise.Application.Theming;

/// <summary>
/// Merkezi tema token'ları. Renkler ekranlara SABİT yazılmaz; buradan (ayarlardan) gelir.
/// Avalonia bir ResourceDictionary'ye, web CSS değişkenlerine bu değerleri bağlar.
/// </summary>
public sealed record ThemeTokens(
    string Primary,
    string OnPrimary,
    string Surface,
    string OnSurface,
    string Accent,
    string Danger,
    string Warning,
    string Success,
    string CornerRadius)
{
    // Koyu sol menü temalı modern palet (assets-incoming/design şemasına göre).
    public static ThemeTokens Default => new(
        Primary: "#1E1E24",     // koyu sidebar/header
        OnPrimary: "#FFFFFF",
        Surface: "#F4F5F9",     // açık içerik zemini
        OnSurface: "#1F2430",
        Accent: "#3B82F6",      // mavi vurgu (Eşitle, linkler, KPI)
        Danger: "#E5484D",
        Warning: "#E08C00",
        Success: "#2E9E5B",
        CornerRadius: "10");
}

/// <summary>
/// Marka metinleri/logoları. Ekranlara sabit yazılmaz; ayarlardan yüklenir, firma başına özelleşir.
/// </summary>
public sealed record BrandingSettings(
    string AppName,
    string CompanyName,
    string? LogoPath,
    string? Contact,
    string? Website,
    string? Copyright)
{
    public static BrandingSettings Default => new(
        AppName: "DepoWise",
        CompanyName: "DepoWise",
        LogoPath: null,
        Contact: null,
        Website: null,
        Copyright: null);
}

/// <summary>app_settings anahtarları (tek doğru kaynak).</summary>
public static class SettingKeys
{
    public const string ThemePrimary = "theme.primary";
    public const string ThemeOnPrimary = "theme.on_primary";
    public const string ThemeSurface = "theme.surface";
    public const string ThemeOnSurface = "theme.on_surface";
    public const string ThemeAccent = "theme.accent";
    public const string ThemeDanger = "theme.danger";
    public const string ThemeWarning = "theme.warning";
    public const string ThemeSuccess = "theme.success";
    public const string ThemeCornerRadius = "theme.corner_radius";

    public const string BrandAppName = "brand.app_name";
    public const string BrandCompanyName = "brand.company_name";
    public const string BrandLogoPath = "brand.logo_path";
    public const string BrandContact = "brand.contact";
    public const string BrandWebsite = "brand.website";
    public const string BrandCopyright = "brand.copyright";
}
