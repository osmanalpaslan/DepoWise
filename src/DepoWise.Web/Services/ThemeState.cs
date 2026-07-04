using System;

namespace DepoWise.Web.Services;

/// <summary>
/// Web tema durumu (per-circuit scoped). Mod: light / dark / system. MainLayout MudThemeProvider'ı buna
/// göre günceller (system için MudThemeProvider.GetSystemPreference). Tema ekranı Mode'u değiştirip Changed
/// tetikler; MainLayout uygular + localStorage'a yazar.
/// </summary>
public sealed class ThemeState
{
    public const string Dark = "dark";
    public const string Light = "light";
    public const string System = "system";

    /// <summary>Seçili mod (light/dark/system).</summary>
    public string Mode { get; set; } = Dark;

    /// <summary>MudThemeProvider'a verilen efektif değer (system çözümlendikten sonra).</summary>
    public bool IsDark { get; set; } = true;

    /// <summary>Seçili renk teması (Palettes anahtarı).</summary>
    public string Color { get; set; } = "blue";

    /// <summary>Seçili görünüm stili (Styles anahtarı) — köşe, gölge, boşluk, menü/üstbar dokusu.</summary>
    public string Style { get; set; } = "classic";

    /// <summary>Kullanılabilir modern görünüm stilleri (ücretsiz, MudBlazor+CSS ile). Tema ekranında listelenir.</summary>
    public static readonly (string Key, string Name, string Desc)[] Styles =
    {
        ("classic", "Klasik",  "Sade ve dengeli varsayılan görünüm."),
        ("soft",    "Yumuşak", "Yuvarlak köşeler, yumuşak gölgeler, ferah boşluklar."),
        ("gradient","Canlı",   "Gradient üst bar + belirgin vurgular, modern SaaS havası."),
        ("compact", "Kompakt", "Sık yerleşim, ince kenarlar, bilgi yoğun ekranlar."),
    };

    /// <summary>Kullanılabilir renk temaları (ana renk). Tema ekranında swatch olarak listelenir.</summary>
    public static readonly (string Key, string Name, string Hex)[] Palettes =
    {
        ("blue",    "Mavi",     "#2563eb"),
        ("indigo",  "İndigo",   "#6366f1"),
        ("violet",  "Mor",      "#8b5cf6"),
        ("emerald", "Zümrüt",   "#10b981"),
        ("teal",    "Turkuaz",  "#14b8a6"),
        ("cyan",    "Camgöbeği","#06b6d4"),
        ("rose",    "Gül",      "#f43f5e"),
        ("amber",   "Kehribar", "#f59e0b"),
        ("slate",   "Gri",      "#64748b"),
    };

    public string CurrentHex => Array.Find(Palettes, p => p.Key == Color).Hex is { Length: > 0 } h ? h : "#2563eb";

    /// <summary>Mod/renk değişince MainLayout dinler (uygular + saklar).</summary>
    public event Func<Task>? Changed;

    public async Task SetModeAsync(string mode)
    {
        Mode = mode is Light or Dark or System ? mode : Dark;
        if (Changed is not null) await Changed.Invoke();
    }

    public async Task SetColorAsync(string color)
    {
        if (Array.Exists(Palettes, p => p.Key == color)) Color = color;
        if (Changed is not null) await Changed.Invoke();
    }

    public async Task SetStyleAsync(string style)
    {
        if (Array.Exists(Styles, s => s.Key == style)) Style = style;
        if (Changed is not null) await Changed.Invoke();
    }
}
