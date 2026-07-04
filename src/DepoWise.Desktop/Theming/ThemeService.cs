using System;
using Avalonia.Media;
using Avalonia.Styling;

namespace DepoWise.Desktop.Theming;

/// <summary>
/// Uygulama tema modu (Koyu / Açık / Sistem). Ayarlar → Tema ekranından seçilir; app_settings'te saklanır
/// (key = theme_mode) ve anında uygulanır. Semantik palet (Palette.axaml) ThemeVariant'a duyarlıdır.
/// </summary>
public static class ThemeService
{
    public const string Key = "theme_mode";
    public const string AccentKey = "theme_accent";
    public const string StyleKey = "theme_style";
    public const string Dark = "Dark";
    public const string Light = "Light";
    public const string System = "System";

    /// <summary>Şu anki mod ("Dark"/"Light"/"System").</summary>
    public static string CurrentMode { get; private set; } = Dark;

    /// <summary>Şu anki renk teması anahtarı.</summary>
    public static string CurrentAccent { get; private set; } = "blue";

    /// <summary>Kullanılabilir renk temaları (ana/accent renk).</summary>
    public static readonly (string Key, string Name, string Hex)[] Accents =
    {
        ("blue",    "Mavi",      "#2F6FD5"),
        ("indigo",  "İndigo",    "#6366F1"),
        ("violet",  "Mor",       "#8B5CF6"),
        ("emerald", "Zümrüt",    "#10B981"),
        ("teal",    "Turkuaz",   "#14B8A6"),
        ("cyan",    "Camgöbeği", "#0EA5C4"),
        ("rose",    "Gül",       "#F43F5E"),
        ("amber",   "Kehribar",  "#E0951B"),
        ("slate",   "Gri",       "#64748B"),
    };

    /// <summary>Şu anki görünüm (stil kütüphanesi) anahtarı.</summary>
    public static string CurrentStyle { get; private set; } = "fluent";
    private static Avalonia.Styling.IStyle? _baseStyle;

    /// <summary>Kullanılabilir GÖRÜNÜM (stil) temaları — tüm kontrolleri baştan biçimlendiren kütüphaneler.</summary>
    public static readonly (string Key, string Name, string Desc)[] Styles =
    {
        ("fluent", "Fluent (Klasik)", "Avalonia varsayılan modern görünüm."),
        ("semi",   "Semi (Modern)",   "Semi Design — yumuşak, çağdaş arayüz."),
    };

    /// <summary>Kayıtlı stil + mod + renk temasını oku ve uygula (açılışta çağrılır).</summary>
    public static void ApplySaved()
    {
        var mode = Dark; var accent = "blue"; var style = "fluent";
        try { mode = DesktopServices.Settings.Get(DesktopServices.DefaultCompanyId, Key) ?? Dark; } catch { }
        try { accent = DesktopServices.Settings.Get(DesktopServices.DefaultCompanyId, AccentKey) ?? "blue"; } catch { }
        try { style = DesktopServices.Settings.Get(DesktopServices.DefaultCompanyId, StyleKey) ?? "fluent"; } catch { }
        ApplyStyle(style, persist: false);
        ApplyAccent(accent, persist: false);
        Apply(mode, persist: false);
    }

    /// <summary>Görünüm (stil kütüphanesi) uygula. Base tema Application.Styles[0]'a yerleştirilir (Fluent/Semi).
    /// Çalışırken değiştirilince eski base kaldırılıp yenisi eklenir.</summary>
    public static void ApplyStyle(string styleKey, bool persist = true)
    {
        if (styleKey != "fluent" && styleKey != "semi") styleKey = "fluent";
        CurrentStyle = styleKey;

        var app = Avalonia.Application.Current;
        if (app is not null)
        {
            try
            {
                Avalonia.Styling.IStyle baseStyle = styleKey == "semi"
                    ? new Semi.Avalonia.SemiTheme()
                    : new Avalonia.Themes.Fluent.FluentTheme();

                if (_baseStyle is not null && app.Styles.Contains(_baseStyle)) app.Styles.Remove(_baseStyle);
                app.Styles.Insert(0, baseStyle);
                _baseStyle = baseStyle;
            }
            catch
            {
                // Stil yüklenemezse (bozuk paket vb.) Fluent'e düş — app KİLİTLENMESİN, seçim de kaydedilmesin.
                styleKey = "fluent";
                CurrentStyle = "fluent";
                if (_baseStyle is null)
                {
                    try { var f = new Avalonia.Themes.Fluent.FluentTheme(); app.Styles.Insert(0, f); _baseStyle = f; } catch { }
                }
                persist = false;
            }
        }

        if (persist)
            try { DesktopServices.Settings.Set(DesktopServices.DefaultCompanyId, StyleKey, styleKey); } catch { }
    }

    /// <summary>Renk temasını uygula (+ opsiyonel kaydet). Accent brush'larını ÜST SEVİYE override eder
    /// (palette theme-dict değerlerini geçersiz kılar; her iki modda geçerli).</summary>
    public static void ApplyAccent(string accentKey, bool persist = true)
    {
        var hex = Array.Find(Accents, a => a.Key == accentKey).Hex;
        if (string.IsNullOrEmpty(hex)) { accentKey = "blue"; hex = "#2F6FD5"; }
        CurrentAccent = accentKey;

        var app = Avalonia.Application.Current;
        if (app is not null && Color.TryParse(hex, out var c))
        {
            var hover = Darken(c, 0.12);
            var soft = Color.FromArgb(0x38, c.R, c.G, c.B);
            app.Resources["AccentBrush"] = new SolidColorBrush(c);
            app.Resources["AccentHoverBrush"] = new SolidColorBrush(hover);
            app.Resources["AccentSoftBrush"] = new SolidColorBrush(soft);
        }

        if (persist)
            try { DesktopServices.Settings.Set(DesktopServices.DefaultCompanyId, AccentKey, accentKey); } catch { }
    }

    private static Color Darken(Color c, double amount)
    {
        byte M(byte ch) => (byte)(ch * (1 - amount));
        return Color.FromRgb(M(c.R), M(c.G), M(c.B));
    }

    /// <summary>Modu uygula (+ opsiyonel kaydet). Anında etki eder.</summary>
    public static void Apply(string mode, bool persist = true)
    {
        if (mode != Dark && mode != Light && mode != System) mode = Dark;
        CurrentMode = mode;

        var app = Avalonia.Application.Current;
        if (app is not null)
            app.RequestedThemeVariant = mode switch
            {
                Light => ThemeVariant.Light,
                Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default, // Sistem: OS'a uyar
            };

        if (persist)
            try { DesktopServices.Settings.Set(DesktopServices.DefaultCompanyId, Key, mode); } catch { }
    }
}
