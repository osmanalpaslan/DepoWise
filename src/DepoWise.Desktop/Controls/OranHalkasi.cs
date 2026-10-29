using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DepoWise.Desktop.Controls;

/// <summary>
/// ⭐ Ana ekran B düzeni (2026-10-29, kullanıcı: "Filo durumu kartını B seçeneğindeki gibi … pasta dilimi grafik şeklinde") — ORAN
/// HALKASI: dört değeri (Çalışıyor / Bakımda / Arızalı / Pasif) halka üzerinde, toplam içindeki paylarıyla, aralarında küçük boşluklu
/// renkli dilimler olarak çizer (saat 12'den saat yönünde). Ortadaki yazı (toplam) XAML'de üstüne konur. Sıfır değer çizilmez; hepsi
/// sıfırsa yalnız boş iz görünür. <see cref="OranCubugu"/> ile aynı özellik adları → bağlamalar birebir taşınır.
/// </summary>
public sealed class OranHalkasi : Control
{
    public static readonly StyledProperty<double> Deger1Property = AvaloniaProperty.Register<OranHalkasi, double>(nameof(Deger1));
    public static readonly StyledProperty<double> Deger2Property = AvaloniaProperty.Register<OranHalkasi, double>(nameof(Deger2));
    public static readonly StyledProperty<double> Deger3Property = AvaloniaProperty.Register<OranHalkasi, double>(nameof(Deger3));
    public static readonly StyledProperty<double> Deger4Property = AvaloniaProperty.Register<OranHalkasi, double>(nameof(Deger4));
    public static readonly StyledProperty<IBrush?> Renk1Property = AvaloniaProperty.Register<OranHalkasi, IBrush?>(nameof(Renk1));
    public static readonly StyledProperty<IBrush?> Renk2Property = AvaloniaProperty.Register<OranHalkasi, IBrush?>(nameof(Renk2));
    public static readonly StyledProperty<IBrush?> Renk3Property = AvaloniaProperty.Register<OranHalkasi, IBrush?>(nameof(Renk3));
    public static readonly StyledProperty<IBrush?> Renk4Property = AvaloniaProperty.Register<OranHalkasi, IBrush?>(nameof(Renk4));
    public static readonly StyledProperty<IBrush?> IzRengiProperty = AvaloniaProperty.Register<OranHalkasi, IBrush?>(nameof(IzRengi));
    public static readonly StyledProperty<double> KalinlikProperty = AvaloniaProperty.Register<OranHalkasi, double>(nameof(Kalinlik), 16);

    public double Deger1 { get => GetValue(Deger1Property); set => SetValue(Deger1Property, value); }
    public double Deger2 { get => GetValue(Deger2Property); set => SetValue(Deger2Property, value); }
    public double Deger3 { get => GetValue(Deger3Property); set => SetValue(Deger3Property, value); }
    public double Deger4 { get => GetValue(Deger4Property); set => SetValue(Deger4Property, value); }
    public IBrush? Renk1 { get => GetValue(Renk1Property); set => SetValue(Renk1Property, value); }
    public IBrush? Renk2 { get => GetValue(Renk2Property); set => SetValue(Renk2Property, value); }
    public IBrush? Renk3 { get => GetValue(Renk3Property); set => SetValue(Renk3Property, value); }
    public IBrush? Renk4 { get => GetValue(Renk4Property); set => SetValue(Renk4Property, value); }
    /// <summary>Dilimlerin altındaki boş halka (toplam 0 iken de görünür).</summary>
    public IBrush? IzRengi { get => GetValue(IzRengiProperty); set => SetValue(IzRengiProperty, value); }
    /// <summary>Halka kalınlığı (px).</summary>
    public double Kalinlik { get => GetValue(KalinlikProperty); set => SetValue(KalinlikProperty, value); }

    /// <summary>Dilimler arası boşluk (derece).</summary>
    private const double BoslukDerece = 3;

    static OranHalkasi()
    {
        AffectsRender<OranHalkasi>(Deger1Property, Deger2Property, Deger3Property, Deger4Property,
            Renk1Property, Renk2Property, Renk3Property, Renk4Property, IzRengiProperty, KalinlikProperty);
    }

    /// <summary>Çizilecek dilimler: (başlangıç açısı, süpürme açısı, renk) — derece, saat 12 = −90°. Test de bunu okur.</summary>
    public (double Bas, double Supurme, IBrush Renk)[] Dilimler()
    {
        var parcalar = new[] { (Deger1, Renk1), (Deger2, Renk2), (Deger3, Renk3), (Deger4, Renk4) }
            .Where(p => p.Item1 > 0 && p.Item2 is not null).Select(p => (Deger: p.Item1, Renk: p.Item2!)).ToList();
        var toplam = parcalar.Sum(p => p.Deger);
        if (toplam <= 0) return Array.Empty<(double, double, IBrush)>();
        var bosluk = parcalar.Count > 1 ? BoslukDerece : 0;
        var bas = -90.0;
        var sonuc = new (double, double, IBrush)[parcalar.Count];
        for (int i = 0; i < parcalar.Count; i++)
        {
            var pay = 360.0 * parcalar[i].Deger / toplam;
            var supurme = Math.Max(1.5, pay - bosluk);   // çok küçük pay da görünür kalsın
            sonuc[i] = (bas, supurme, parcalar[i].Renk);
            bas += pay;
        }
        return sonuc;
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width; var h = Bounds.Height;
        var kalinlik = Math.Max(1, Kalinlik);
        var cap = Math.Min(w, h);
        if (cap <= kalinlik) return;
        var merkez = new Point(w / 2, h / 2);
        var r = (cap - kalinlik) / 2;
        if (IzRengi is { } iz) context.DrawEllipse(null, new Pen(iz, kalinlik), merkez, r, r);

        var dilimler = Dilimler();
        if (dilimler.Length == 1 && dilimler[0].Supurme >= 359)
        {
            context.DrawEllipse(null, new Pen(dilimler[0].Renk, kalinlik), merkez, r, r);
            return;
        }
        foreach (var (bas, supurme, renk) in dilimler)
        {
            Point Nokta(double derece)
            {
                var a = derece * Math.PI / 180;
                return new Point(merkez.X + r * Math.Cos(a), merkez.Y + r * Math.Sin(a));
            }
            var geo = new StreamGeometry();
            using (var c = geo.Open())
            {
                c.BeginFigure(Nokta(bas), false);
                c.ArcTo(Nokta(bas + supurme), new Size(r, r), 0, supurme > 180, SweepDirection.Clockwise);
                c.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(renk, kalinlik), geo);
        }
    }
}
