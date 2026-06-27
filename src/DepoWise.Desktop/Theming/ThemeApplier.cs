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

        // Türetilen: koyu içerik üzerinde "kart/panel" (Primary'den biraz açık) + ince kenarlık + aktif vurgu.
        if (Color.TryParse(t.Primary, out var p))
        {
            var panel = Lighten(p, 0.06);
            app.Resources["Brand.Panel"] = panel;
            app.Resources["Brand.Panel.Brush"] = new SolidColorBrush(panel);
            app.Resources["Brand.Border.Brush"] = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
            app.Resources["Brand.Hover.Brush"] = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255));
        }
    }

    /// <summary>Rengi beyaza doğru harmanlar (koyu temada yüzey yükseltme).</summary>
    private static Color Lighten(Color c, double amount)
    {
        byte Mix(byte ch) => (byte)(ch + (255 - ch) * amount);
        return Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B));
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
