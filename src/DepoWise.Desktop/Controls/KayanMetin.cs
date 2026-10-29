using System;
using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace DepoWise.Desktop.Controls;

/// <summary>
/// ═══ KAYAN METİN ═══ (kullanıcı isteği 2026-10-29: "kayıt tipi üzerinde beklediğinde sola kayar şekilde animasyon olabilir ve
/// görünmeyen kısmı okunabilir")
///
/// <para>Tek satırlık yazı: kutuya sığmıyorsa "…" ile kesilir, kutu ASLA genişlemez (uzun veri alanı büyütmez). Fare üzerinde
/// <see cref="BeklemeMs"/> bekleyince yazı, taşan kısmı kadar yavaşça sola kayar (saniyede <see cref="HizPxSn"/> px); fare çekilince
/// hızla yerine döner ve kesme geri gelir. Sığan yazıda hiçbir şey olmaz. Yalnız RenderTransform değişir → yerleşim yeniden
/// hesaplanmaz, liste kaydırması akıcı kalır.</para>
/// </summary>
public sealed class KayanMetin : Border
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<KayanMetin, string?>(nameof(Text));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Kaymaya başlamadan önce farenin üzerinde beklemesi (ms).</summary>
    public const int BeklemeMs = 500;
    /// <summary>Kayma hızı (px/sn) — okunabilir, yavaş.</summary>
    public const double HizPxSn = 45;

    private readonly TextBlock _yazi;
    private readonly DispatcherTimer _bekle;
    private DispatcherTimer? _kesmeyiGeriAl;

    /// <summary>Şu an kaydırılmış mı (test ve durum için).</summary>
    public bool Kaydirildi { get; private set; }

    public KayanMetin()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;   // boş alanda da fare algılansın
        _yazi = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = TransformOperations.Parse("translateX(0px)"),
        };
        _yazi.Bind(TextBlock.TextProperty, this.GetObservable(TextProperty));
        Child = _yazi;

        _bekle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(BeklemeMs) };
        _bekle.Tick += (_, _) => { _bekle.Stop(); Kaydir(); };
    }

    /// <summary>Yazının kutudan taşan kısmı (px); sığıyorsa 0.</summary>
    public double Tasma
    {
        get
        {
            if (string.IsNullOrEmpty(Text) || Bounds.Width <= 0) return 0;
            var olcu = new TextBlock
            {
                Text = Text, FontSize = _yazi.FontSize, FontFamily = _yazi.FontFamily,
                FontWeight = _yazi.FontWeight, FontStyle = _yazi.FontStyle, LetterSpacing = _yazi.LetterSpacing,
            };
            olcu.Measure(Size.Infinity);
            var alan = Bounds.Width - Padding.Left - Padding.Right;
            return Math.Max(0, Math.Ceiling(olcu.DesiredSize.Width - alan));
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _bekle.Stop();
        _bekle.Start();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Geri();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _bekle.Stop();
        _kesmeyiGeriAl?.Stop();
    }

    /// <summary>Taşan kısmı göstermek için sola kaydırır (sığıyorsa hiçbir şey yapmaz).</summary>
    public void Kaydir()
    {
        var tasma = Tasma;
        if (tasma <= 1) return;
        _kesmeyiGeriAl?.Stop();
        _yazi.TextTrimming = TextTrimming.None;
        var sure = TimeSpan.FromSeconds(Math.Clamp(tasma / HizPxSn, 0.6, 8));
        _yazi.Transitions = new Transitions
        {
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = sure, Easing = new SineEaseInOut() },
        };
        _yazi.RenderTransform = TransformOperations.Parse("translateX(" + (-tasma).ToString("0.#", CultureInfo.InvariantCulture) + "px)");
        Kaydirildi = true;
    }

    /// <summary>Yazıyı hızla başa döndürür; dönüş bitince "…" kesmesi geri gelir.</summary>
    public void Geri()
    {
        _bekle.Stop();
        if (!Kaydirildi) return;
        Kaydirildi = false;
        _yazi.Transitions = new Transitions
        {
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new CubicEaseOut() },
        };
        _yazi.RenderTransform = TransformOperations.Parse("translateX(0px)");
        _kesmeyiGeriAl?.Stop();
        _kesmeyiGeriAl = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
        _kesmeyiGeriAl.Tick += (_, _) =>
        {
            _kesmeyiGeriAl?.Stop();
            if (!Kaydirildi) _yazi.TextTrimming = TextTrimming.CharacterEllipsis;
        };
        _kesmeyiGeriAl.Start();
    }

    /// <summary>Yazı şu an "…" ile kesiliyor mu (durgunken evet; kayarken hayır).</summary>
    public bool KesmeAcik => _yazi.TextTrimming != TextTrimming.None;
    /// <summary>Son kaydırmanın hedefi (px, negatif = sola); başa dönünce 0.</summary>
    public double KaymaHedefi => Kaydirildi ? -Tasma : 0;
}
