using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DepoWise.Desktop.Controls;

/// <summary>
/// ⭐ iOS tasarım (2026-10-28, kullanıcı onayı) — yatay ORAN ÇUBUĞU: dört değeri (ör. araç durumları Aktif / Bakımda / Arızalı /
/// Pasif) toplam içindeki paylarıyla, aralarında küçük boşluklu yuvarlak parçalar olarak çizer. Sıfır değerli parça çizilmez;
/// hepsi sıfırsa yalnız boş iz görünür. Renkler dışarıdan verilir (tema token'ı ya da sabit durum rengi).
/// Izgara sütun oranına bağlama Avalonia'da güvenilir olmadığı için çizim kontrolün kendisindedir.
/// </summary>
public sealed class OranCubugu : Control
{
    public static readonly StyledProperty<double> Deger1Property = AvaloniaProperty.Register<OranCubugu, double>(nameof(Deger1));
    public static readonly StyledProperty<double> Deger2Property = AvaloniaProperty.Register<OranCubugu, double>(nameof(Deger2));
    public static readonly StyledProperty<double> Deger3Property = AvaloniaProperty.Register<OranCubugu, double>(nameof(Deger3));
    public static readonly StyledProperty<double> Deger4Property = AvaloniaProperty.Register<OranCubugu, double>(nameof(Deger4));
    public static readonly StyledProperty<IBrush?> Renk1Property = AvaloniaProperty.Register<OranCubugu, IBrush?>(nameof(Renk1));
    public static readonly StyledProperty<IBrush?> Renk2Property = AvaloniaProperty.Register<OranCubugu, IBrush?>(nameof(Renk2));
    public static readonly StyledProperty<IBrush?> Renk3Property = AvaloniaProperty.Register<OranCubugu, IBrush?>(nameof(Renk3));
    public static readonly StyledProperty<IBrush?> Renk4Property = AvaloniaProperty.Register<OranCubugu, IBrush?>(nameof(Renk4));
    public static readonly StyledProperty<IBrush?> IzRengiProperty = AvaloniaProperty.Register<OranCubugu, IBrush?>(nameof(IzRengi));

    public double Deger1 { get => GetValue(Deger1Property); set => SetValue(Deger1Property, value); }
    public double Deger2 { get => GetValue(Deger2Property); set => SetValue(Deger2Property, value); }
    public double Deger3 { get => GetValue(Deger3Property); set => SetValue(Deger3Property, value); }
    public double Deger4 { get => GetValue(Deger4Property); set => SetValue(Deger4Property, value); }
    public IBrush? Renk1 { get => GetValue(Renk1Property); set => SetValue(Renk1Property, value); }
    public IBrush? Renk2 { get => GetValue(Renk2Property); set => SetValue(Renk2Property, value); }
    public IBrush? Renk3 { get => GetValue(Renk3Property); set => SetValue(Renk3Property, value); }
    public IBrush? Renk4 { get => GetValue(Renk4Property); set => SetValue(Renk4Property, value); }
    /// <summary>Parçaların altındaki boş iz (toplam 0 iken de görünür).</summary>
    public IBrush? IzRengi { get => GetValue(IzRengiProperty); set => SetValue(IzRengiProperty, value); }

    private const double Bosluk = 3;

    static OranCubugu()
    {
        AffectsRender<OranCubugu>(Deger1Property, Deger2Property, Deger3Property, Deger4Property,
            Renk1Property, Renk2Property, Renk3Property, Renk4Property, IzRengiProperty);
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 8);

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width; var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var r = h / 2;
        if (IzRengi is { } iz) context.DrawRectangle(iz, null, new Rect(0, 0, w, h), r, r);

        var parcalar = new[] { (Deger1, Renk1), (Deger2, Renk2), (Deger3, Renk3), (Deger4, Renk4) }
            .Where(p => p.Item1 > 0 && p.Item2 is not null).ToList();
        var toplam = parcalar.Sum(p => p.Item1);
        if (toplam <= 0) return;
        var kullanilabilir = w - Bosluk * (parcalar.Count - 1);
        double x = 0;
        foreach (var (deger, renk) in parcalar)
        {
            var g = Math.Max(h, kullanilabilir * deger / toplam);   // çok küçük pay da en az bir nokta kadar görünür
            g = Math.Min(g, w - x);
            context.DrawRectangle(renk, null, new Rect(x, 0, g, h), r, r);
            x += g + Bosluk;
            if (x >= w) break;
        }
    }
}
