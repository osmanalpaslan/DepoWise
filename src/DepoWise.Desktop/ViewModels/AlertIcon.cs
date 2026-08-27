using System;
using System.Globalization;
using Avalonia.Data.Converters;
using DepoWise.Application.Reports;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// M6 — ana ekran uyarı satırının ikonunu uyarı TİPİNDEN üretir.
///
/// <b>Kapatılan eksik:</b> uyarı satırlarının hepsi tek bir sabit üçgen (⚠) çiziyordu; bakım,
/// muayene/sigorta, düşük stok ve yakıt uyarıları listede birbirinden ayırt edilemiyordu — oysa
/// üstteki kategori düğmeleri zaten dört ayrı ikon kullanıyor. Artık ikisi aynı dili konuşur.
///
/// Geometri <c>Themes/Icons.axaml</c>'den okunur; kaynak bulunamazsa <c>null</c> döner ve
/// <c>PathIcon</c> boş çizilir — akış bozulmaz (bkz. <see cref="DesktopIcons"/>).
/// </summary>
public sealed class AlertIcon : IValueConverter
{
    public static readonly AlertIcon Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is AlertKind kind ? DesktopIcons.ForAlert(kind) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
