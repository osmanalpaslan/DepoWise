using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DepoWise.Desktop;

/// <summary>XAML'de kullanılan küçük dönüştürücüler (yalnız görsel; iş kuralı içermez).</summary>
public static class Conv
{
    /// <summary>bool → opaklık (true=1, false=0). Menü alt-listesinin açılışta yumuşak belirmesi için
    /// (ItemsControl.Opacity buna bağlanır + Opacity geçişi tanımlıdır).</summary>
    public static readonly IValueConverter BoolToOpacity =
        new FuncValueConverter<bool, double>(b => b ? 1d : 0d);

    /// <summary>Malzeme/Araç Listesi kolon seçimi (kullanıcı isteği 2026-07-17): value = görünür kolon
    /// anahtarları listesi, ConverterParameter = bu hücrenin kolon anahtarı → görünür mü? Sabit XAML
    /// kolonlarının IsVisible'ı buna bağlanır; Auto genişlikli SharedSizeGroup kolonu görünmeyince 0'a çöker.</summary>
    public static readonly IValueConverter ColumnVisible = new ColumnVisibleConverter();

    private sealed class ColumnVisibleConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not IEnumerable cols || parameter is not string key) return false;
            foreach (var c in cols) if (c is string s && s == key) return true;
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Malzeme/Araç Listesi kolon genişliği (kullanıcı isteği 2026-07-18: "kolon genişliklerini manuel
    /// ayarlanabilmeli"). value = VM'in ColWidths sözlüğü, ConverterParameter = kolon anahtarı → o kolonun
    /// piksel genişliği (double). Başlık hücresinin MinWidth'ine bağlanır: SharedSizeGroup zaten header+satır
    /// genişliğini senkron tutuyordu (bkz. Conv.ColumnVisible yorumu); burada yalnız o ölçümün ALT SINIRINI
    /// kullanıcı sürükleyerek büyütebiliyor — mevcut senkron mekanizması BOZULMAZ.</summary>
    public static readonly IValueConverter ColWidth = new ColWidthConverter();

    private sealed class ColWidthConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is IReadOnlyDictionary<string, double> map && parameter is string key && map.TryGetValue(key, out var w))
                return w;
            return 100d;   // ilk açılış / bilinmeyen kolon → makul varsayılan
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Başlığa tıklayınca sıralama oku (kullanıcı isteği 2026-07-18): value = VM'in SortState'i
    /// (SortColumn, SortDesc), ConverterParameter = bu başlığın kolon anahtarı → "▲"/"▼"/"".</summary>
    public static readonly IValueConverter SortArrow = new SortArrowConverter();

    private sealed class SortArrowConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ValueTuple<string?, bool> t && parameter is string key && t.Item1 == key)
                return t.Item2 ? " ▼" : " ▲";
            return "";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
