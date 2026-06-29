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
    // Tam koyu modern tema (assets-incoming/design "Örnek arayüz tasarımı").
    public static ThemeTokens Default => new(
        Primary: "#1E232C",     // sidebar / üst bar
        OnPrimary: "#FFFFFF",
        Surface: "#161A21",     // koyu içerik zemini
        OnSurface: "#E6E8EC",   // koyu üstünde açık metin
        Accent: "#2563EB",      // mavi (KPI kartları, Eşitle, aktif menü)
        Danger: "#DC2626",
        Warning: "#F59E0B",
        Success: "#16A34A",
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

    // Sunucu yedek (bulut API). Yapılandırılırsa her yerel yedek sunucuya yüklenir; sunucu hiç silmez.
    public const string BackupServerUrl = "backup.server_url";
    public const string BackupServerToken = "backup.server_token";
    public const string BackupMachineId = "backup.machine_id"; // makine başına sabit GUID (ilk kullanımda üretilir)
}
