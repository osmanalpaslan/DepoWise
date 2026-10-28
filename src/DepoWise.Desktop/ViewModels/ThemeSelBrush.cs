using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Tema seçim kartı çerçevesi: seçiliyse accent, değilse şeffaf.</summary>
public sealed class ThemeSelBrush : IValueConverter
{
    public static readonly ThemeSelBrush Instance = new();
    // ⭐ iOS tasarım (2026-10-29): seçili çerçeve sabit maviydi (#2F6FD5) → kullanıcının seçtiği VURGU rengi (AccentBrush; renk
    // teması değişince birlikte değişir). Kaynak bulunamazsa (ör. tasarım zamanı) eski mavi.
    private static readonly IBrush Yedek = new SolidColorBrush(Color.Parse("#2F6FD5"));
    private static readonly IBrush None = Brushes.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Vurgu() : None;

    private static IBrush Vurgu()
        => Avalonia.Application.Current is { } app && Avalonia.Controls.ResourceNodeExtensions.TryFindResource(app, "AccentBrush", app.ActualThemeVariant, out var r) && r is IBrush b ? b : Yedek;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
