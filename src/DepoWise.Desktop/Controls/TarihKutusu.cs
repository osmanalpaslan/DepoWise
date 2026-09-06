using System;
using System.Globalization;
using Calendar = Avalonia.Controls.Calendar;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

using DepoWise.Application.Ui;

namespace DepoWise.Desktop.Controls;

/// <summary>
/// ═══ TARİH KUTUSU — Avalonia <see cref="DatePicker"/>'ın yerine geçen KOMPAKT alan ═══
/// (kullanıcı isteği 2026-09-06: "tarih alanları çok çirkin ve büyük; daha modern ve daha az yer
/// kaplayan bir alan istiyorum".)
///
/// <para><b>Neden değiştirildi.</b> Avalonia'nın kutusu tarihi ÜÇ AYRI BÖLMEYE böler
/// (gün | ay | yıl) ve her bölme kendi payını ister. İki ölçülmüş sonucu vardı:
/// (1) alan 280 px'in altına indiğinde <b>yıl bölmesini sessizce düşürüyordu</b> — kullanıcı
/// süzgecin hangi yıla ait olduğunu göremiyordu; (2) 280 px'lik alanlar formları şişiriyordu.
/// Bu denetim tek kutudur: <c>GG.AA.YYYY</c>. Yaklaşık <b>150 px</b> yer kaplar (yarısından az)
/// ve yıl daima görünür.</para>
///
/// <para><b>Sözleşme DatePicker ile AYNI.</b> <see cref="SelectedDate"/> yine
/// <c>DateTimeOffset?</c> ve çift yönlüdür; görünüm dosyalarında yalnız etiket değişir
/// (<c>&lt;DatePicker SelectedDate="{Binding X}"/&gt;</c> → <c>&lt;ctrl:TarihKutusu SelectedDate="{Binding X}"/&gt;</c>).
/// Görünüm modellerinde <b>hiçbir değişiklik gerekmez</b>.</para>
///
/// <para><b>Gerçek takvim doğrulaması</b> (CLAUDE.md §5). 31.02.2026 gibi var olmayan bir tarih
/// KABUL EDİLMEZ; alan eski geçerli değerine döner. Boş metin = tarih yok (<c>null</c>) — süzgeç
/// alanları bilerek boş bırakılabilir. Kullanıcı yalnız rakam yazar, noktalar KENDİLİĞİNDEN eklenir.</para>
///
/// <para><b>Takvim.</b> Sağdaki küçük ikon, aylık takvimi açılır pencerede gösterir; bir güne
/// tıklanınca kutu dolar ve pencere kapanır. Klavye ile de kullanılabilir (Enter onaylar,
/// Esc açık takvimi kapatır).</para>
///
/// <para>Denetim <b>tamamen kodda</b> kurulur (<see cref="LookupBox"/> ile aynı desen): yeni bir
/// ControlTheme dosyası gerekmez, mevcut "Field" ve "Ghost" stilleri kullanılır.</para>
/// </summary>
public class TarihKutusu : UserControl
{
    /// <summary>Seçili tarih. <see cref="DatePicker.SelectedDate"/> ile aynı tip ve aynı çift yönlü davranış.</summary>
    public static readonly StyledProperty<DateTimeOffset?> SelectedDateProperty =
        AvaloniaProperty.Register<TarihKutusu, DateTimeOffset?>(
            nameof(SelectedDate), defaultBindingMode: BindingMode.TwoWay, enableDataValidation: true);

    /// <summary>Kutu boşken görünen ipucu. Varsayılan "GG.AA.YYYY".</summary>
    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<TarihKutusu, string?>(nameof(Watermark), "GG.AA.YYYY");

    public DateTimeOffset? SelectedDate
    {
        get => GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    private const string Bicim = "dd.MM.yyyy";
    private static readonly CultureInfo Kultur = CultureInfo.GetCultureInfo("tr-TR");

    private readonly TextBox _kutu;
    private readonly Button _takvimDugmesi;
    private readonly Popup _acilir;
    private readonly Calendar _takvim;

    /// <summary>Metni programlı yazarken kullanıcı yazıyormuş gibi davranmamak için.</summary>
    private bool _icerdenYaziyor;

    public TarihKutusu()
    {
        _kutu = new TextBox
        {
            MaxLength = 10,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _kutu.Classes.Add("Field");
        _kutu.Bind(TextBox.WatermarkProperty, this.GetObservable(WatermarkProperty));

        _takvimDugmesi = new Button
        {
            Width = 34,
            Padding = new Thickness(0),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new PathIcon { Width = 15, Height = 15 },
        };
        _takvimDugmesi.Classes.Add("Ghost");
        ToolTip.SetTip(_takvimDugmesi, "Takvimden seç");
        if (_takvimDugmesi.Content is PathIcon ikon &&
            Avalonia.Application.Current?.Resources.TryGetResource("IconCalendar", null, out var geo) == true &&
            geo is Geometry g)
        {
            ikon.Data = g;
        }

        _takvim = new Calendar { SelectionMode = CalendarSelectionMode.SingleDate };
        _takvim.SelectedDatesChanged += TakvimdenSecildi;

        _acilir = new Popup
        {
            PlacementTarget = _kutu,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
            Child = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(4),
                Child = _takvim,
            },
        };
        if (_acilir.Child is Border kenar)
        {
            kenar.Bind(Border.BackgroundProperty, new DynamicResourceExtension("SurfaceBrush"));
            kenar.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("BorderSubtleBrush"));
        }

        var izgara = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        izgara.Children.Add(_kutu);
        Grid.SetColumn(_takvimDugmesi, 1);
        izgara.Children.Add(_takvimDugmesi);
        izgara.Children.Add(_acilir);

        Content = izgara;

        _kutu.TextChanged += MetinDegisti;
        _kutu.LostFocus += (_, _) => MetniUygula();
        _kutu.KeyDown += KutuTusa;
        _takvimDugmesi.Click += (_, _) => TakvimiAcKapa();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedDateProperty) MetniTazele();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        MetniTazele();
    }

    /// <summary>Bağlanan değeri kutuya yazar. Kullanıcı o an yazıyorsa araya girmez.</summary>
    private void MetniTazele()
    {
        if (_kutu.IsFocused) return;
        _icerdenYaziyor = true;
        _kutu.Text = TarihMetni.Bicimle(SelectedDate);
        _icerdenYaziyor = false;
    }

    /// <summary>
    /// Kullanıcı yalnız RAKAM yazar; noktalar kendiliğinden eklenir (2 ve 4. rakamdan sonra).
    /// Böylece "06092026" yazmak "06.09.2026" üretir — nokta tuşuna basmak gerekmez.
    /// </summary>
    private void MetinDegisti(object? gonderen, TextChangedEventArgs e)
    {
        if (_icerdenYaziyor) return;
        var ham = _kutu.Text ?? "";
        var s = TarihMetni.Maskele(ham);

        if (s == ham) return;
        _icerdenYaziyor = true;
        var imlecSonda = _kutu.CaretIndex >= ham.Length;
        _kutu.Text = s;
        _kutu.CaretIndex = imlecSonda ? s.Length : Math.Min(_kutu.CaretIndex, s.Length);
        _icerdenYaziyor = false;
    }

    private void KutuTusa(object? gonderen, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { MetniUygula(); e.Handled = true; }
        else if (e.Key == Key.Escape && _acilir.IsOpen) { _acilir.IsOpen = false; e.Handled = true; }
    }

    /// <summary>
    /// Kutudaki metni tarihe çevirir. Boş → <c>null</c>. Geçersiz (ör. 31.02.2026) → DEĞER
    /// DEĞİŞMEZ, kutu son geçerli değere döner. Böylece ekranda hiçbir zaman var olmayan bir tarih
    /// durmaz ve geçerli bir değer sessizce silinmez.
    /// </summary>
    private void MetniUygula()
    {
        if (TarihMetni.Coz(_kutu.Text, out var cozulen))
        {
            if (SelectedDate?.Date != cozulen?.Date) SelectedDate = cozulen;
        }
        // Geçersizse değer DEĞİŞMEZ; her iki durumda da kutu bağlı değerle yeniden yazılır.
        MetniTazele();
    }

    private void TakvimiAcKapa()
    {
        if (_acilir.IsOpen) { _acilir.IsOpen = false; return; }
        _takvim.SelectedDate = SelectedDate?.DateTime.Date ?? DateTime.Today;
        _takvim.DisplayDate = _takvim.SelectedDate ?? DateTime.Today;
        _acilir.IsOpen = true;
    }

    private void TakvimdenSecildi(object? gonderen, SelectionChangedEventArgs e)
    {
        if (_takvim.SelectedDate is not { } d) return;
        SelectedDate = new DateTimeOffset(d.Date, TimeSpan.Zero);
        MetniTazele();
        _acilir.IsOpen = false;
    }
}
