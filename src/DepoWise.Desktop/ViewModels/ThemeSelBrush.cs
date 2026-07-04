using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Tema seçim kartı çerçevesi: seçiliyse accent, değilse şeffaf.</summary>
public sealed class ThemeSelBrush : IValueConverter
{
    public static readonly ThemeSelBrush Instance = new();
    private static readonly IBrush Selected = new SolidColorBrush(Color.Parse("#2F6FD5"));
    private static readonly IBrush None = Brushes.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Selected : None;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
