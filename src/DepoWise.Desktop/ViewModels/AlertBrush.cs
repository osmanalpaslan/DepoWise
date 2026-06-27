using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DepoWise.Desktop.ViewModels;

/// <summary>IsCritical → kırmızı (gecikti) / turuncu (yaklaşıyor) uyarı barı rengi.</summary>
public sealed class AlertBrush : IValueConverter
{
    public static readonly AlertBrush Instance = new();
    private static readonly IBrush Critical = new SolidColorBrush(Color.Parse("#DC2626"));
    private static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#F59E0B"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Critical : Warning;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
