using System;
using System.Globalization;
using Avalonia.Data.Converters;
using DepoWise.Application.Ui;

namespace DepoWise.Desktop.Converters;

/// <summary>
/// Kapsayıcı yüksekliğini (<c>Bounds.Height</c>) alır, forma verilecek üst sınırı döndürür.
/// Karar mantığı YOKTUR — hesabı <see cref="FormListeOrani"/> yapar (orası test edilir);
/// burası yalnız Avalonia'ya bağlayan ince kabuktur.
/// </summary>
public sealed class FormUstSiniriConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var yukseklik = value is double d ? d : 0;
        var oran = FormListeOrani.VarsayilanOran;
        if (parameter is string p && double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var o))
            oran = o;
        return FormListeOrani.FormUstSiniri(yukseklik, oran);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
