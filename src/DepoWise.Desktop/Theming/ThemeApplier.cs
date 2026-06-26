using Avalonia;
using Avalonia.Media;
using DepoWise.Application.Theming;

namespace DepoWise.Desktop.Theming;

/// <summary>
/// Merkezi tema token'larını Application.Resources'a yazar. Ekranlar renkleri SABİT yazmaz;
/// yalnız "Brand.*" DynamicResource anahtarlarını kullanır. Ayar değişince yeniden uygulanır.
/// </summary>
public static class ThemeApplier
{
    public static void Apply(Avalonia.Application app, ThemeTokens t)
    {
        Set(app, "Brand.Primary", t.Primary);
        Set(app, "Brand.OnPrimary", t.OnPrimary);
        Set(app, "Brand.Surface", t.Surface);
        Set(app, "Brand.OnSurface", t.OnSurface);
        Set(app, "Brand.Accent", t.Accent);
        Set(app, "Brand.Danger", t.Danger);
        Set(app, "Brand.Warning", t.Warning);
        Set(app, "Brand.Success", t.Success);

        if (double.TryParse(t.CornerRadius, out var r))
            app.Resources["Brand.CornerRadius"] = new CornerRadius(r);
    }

    private static void Set(Avalonia.Application app, string key, string hex)
    {
        if (Color.TryParse(hex, out var color))
        {
            app.Resources[key] = color;
            app.Resources[key + ".Brush"] = new SolidColorBrush(color);
        }
    }
}
