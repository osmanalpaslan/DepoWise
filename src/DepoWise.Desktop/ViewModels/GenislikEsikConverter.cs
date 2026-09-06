using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// ⭐ ÜST BAR ÖLÇEKLENMESİ (görsel QA 2026-09-06, kullanıcı isteği "her şey pencere boyutuna göre
/// ölçeklensin").
///
/// <para><b>Sorun.</b> Üst bar 60 px sabit yükseklikte TEK SATIRLIK bir Grid'dir; sarılamaz. İçindeki
/// öğelerin çoğu sabit genişliktedir (global arama 170 px · "Ekran" etiketi · kullanıcı adı 140 px).
/// Pencere daraldığında yıldız sütun (başlık) sıfıra iner, sonrasında sağdaki öğeler pencerenin
/// DIŞINA taşar ve TIKLANAMAZ hâle gelir. UI Automation ölçümü: 1120 px'te kullanıcı düğmesi 7 px,
/// 1060 px'te 67 px, 900 px'te 227 px dışarıda.</para>
///
/// <para><b>Çözüm.</b> Bu dönüştürücü, pencere genişliğini bir EŞİKLE karşılaştırır. Eşiğin altında
/// kalan öğeler gizlenir; böylece üst bar daralan pencereye uyum sağlar. Gizlenen hiçbir şey tek yol
/// değildir: global aramanın yerine menü araması, kullanıcı ADININ yerine baş harf dairesi (menü yine
/// açılır), "Ekran" ETİKETİNİN yerine ikonu kalır. Yani işlev kaybolmaz, yalnız etiket/kutu küçülür.</para>
///
/// <para><b>Kullanım.</b> <c>IsVisible="{Binding $parent[Window].Bounds.Width,
/// Converter={x:Static vm:GenislikEsikConverter.Instance}, ConverterParameter=1300}"</c>
/// → pencere 1300 px ve üzerindeyse görünür.</para>
/// </summary>
public sealed class GenislikEsikConverter : IValueConverter
{
    public static readonly GenislikEsikConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double genislik = value switch
        {
            double d => d,
            int i => i,
            _ => double.TryParse(value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : 0,
        };

        double esik = parameter switch
        {
            double d2 => d2,
            int i2 => i2,
            string s2 when double.TryParse(s2, NumberStyles.Any, CultureInfo.InvariantCulture, out var x2) => x2,
            _ => 0,
        };

        // Eşik verilmemişse (0) gizleme yapma — yanlış yapılandırma yüzünden öğe kaybolmasın.
        if (esik <= 0) return true;

        // Ölçüm henüz yapılmadıysa Bounds.Width 0 gelir. O anda gizlersek açılışta öğeler bir kare
        // kaybolup geri gelir (titreme). Bu yüzden 0 = "bilinmiyor" kabul edilip GÖRÜNÜR dönülür.
        if (genislik <= 0) return true;

        return genislik >= esik;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
