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
    public static ThemeTokens Default => new(
        Primary: "#1F6FEB",
        OnPrimary: "#FFFFFF",
        Surface: "#FFFFFF",
        OnSurface: "#1A1A1A",
        Accent: "#0EA5E9",
        Danger: "#DC2626",
        Warning: "#D97706",
        Success: "#16A34A",
        CornerRadius: "8");
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
