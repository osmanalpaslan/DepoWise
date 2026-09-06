using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Sohbet balonunun hizası: <b>benim mesajım sağda</b>, karşı tarafınki solda. Bu, mesajlaşma
/// arayüzlerinin evrensel dilidir; kim ne yazdı sorusunu okumadan cevaplar.
/// </summary>
public sealed class MesajHizaConverter : IValueConverter
{
    public static readonly MesajHizaConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Sohbet balonunun zemini. Renk TEK BAŞINA anlam taşımaz — hiza da aynı bilgiyi verir
/// (renk körlüğü/koyu-açık tema için ikinci ipucu). Kaynak sözlükten okunur ki tema değişince uyum bozulmasın.
/// </summary>
public sealed class MesajZeminConverter : IValueConverter
{
    public static readonly MesajZeminConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var anahtar = value is true ? "OverlaySelectedBrush" : "SurfaceElevatedBrush";
        if (Avalonia.Application.Current?.Resources.TryGetResource(anahtar, null, out var kaynak) == true && kaynak is IBrush f)
            return f;
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
