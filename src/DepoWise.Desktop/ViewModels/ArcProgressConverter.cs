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

    private const double Size = 160;
    private const double Thickness = 12;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double pct = value switch
        {
            double d => d,
            int i => i,
            _ => double.TryParse(value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : 0,
        };
        pct = Math.Max(0, Math.Min(100, pct));

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
