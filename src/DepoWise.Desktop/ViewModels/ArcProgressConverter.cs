using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Yüzde (0-100) değerini dairesel ilerleme YAYINA (arc StreamGeometry) çevirir. Tepeden başlar, saat
/// yönünde dolar; %100'de tam tur. 160px daire, 12px kalınlık (yay çizgisi Path.Stroke ile boyanır).
/// </summary>
public sealed class ArcProgressConverter : IValueConverter
{
    public static readonly ArcProgressConverter Instance = new();

    // Varsayılan: senkron penceresindeki büyük halka. ⭐ FAZ 4.12 (2026-09-06): üst bardaki küçük
    // halka için ConverterParameter ile boyut verilebilir (ör. 24) — kalınlık oranla ölçeklenir.
    private const double VarsayilanBoyut = 160;
    private const double VarsayilanKalinlik = 12;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double pct = value switch
        {
            double d => d,
            int i => i,
            _ => double.TryParse(value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : 0,
        };
        pct = Math.Max(0, Math.Min(100, pct));

        // ConverterParameter = halka çapı (px). Verilmezse büyük halka ölçüsü kullanılır.
        double Size = parameter switch
        {
            double d2 => d2,
            int i2 => i2,
            string s2 when double.TryParse(s2, NumberStyles.Any, CultureInfo.InvariantCulture, out var x2) => x2,
            _ => VarsayilanBoyut,
        };
        if (Size < 8) Size = VarsayilanBoyut;
        double Thickness = Size * (VarsayilanKalinlik / VarsayilanBoyut);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            if (pct > 0.01)
            {
                double sweep = Math.Min(pct, 99.999) / 100.0 * 360.0; // 360 tam yay tek segmentte çizilemez
                double r = (Size - Thickness) / 2.0;
                double c = Size / 2.0;
                var start = new Avalonia.Point(c, c - r); // tepe
                double endRad = (-90 + sweep) * Math.PI / 180.0;
                var end = new Avalonia.Point(c + r * Math.Cos(endRad), c + r * Math.Sin(endRad));
                ctx.BeginFigure(start, false);
                ctx.ArcTo(end, new Avalonia.Size(r, r), 0, sweep > 180, SweepDirection.Clockwise);
                ctx.EndFigure(false);
            }
        }
        return geo;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
