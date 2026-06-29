using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DepoWise.Desktop.ViewModels;

/// <summary>bool → "Aktif" / "Pasif".</summary>
public sealed class ActiveTextConverter : IValueConverter
{
    public static readonly ActiveTextConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Aktif" : "Pasif";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
