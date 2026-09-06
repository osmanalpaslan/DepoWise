using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DepoWise.Desktop.Controls;

/// <summary>
/// ═══ İ1 — ALT BARDA TAŞAN SEKME PANELİ (kullanıcı tasarımı 2026-09-06) ═══
///
/// <para>Kullanıcı: <i>"sığdığı kadar sekme → hemen ardından «Diğer Sayfalar ∨» → onun sağında
/// en sağda sabit «Sohbet». Pencere boyutu değişince taşan sekmeler otomatik olarak panele
/// aktarılır."</i></para>
///
/// <para><b>Ne yapar:</b> çocukları soldan sağa dizer; verilen genişliğe SIĞMAYAN çocukları
/// çizmez ve onları "taştı" olarak işaretler. Böylece sekme şeridi asla sohbet düğmesini
/// itmez ve kaydırma çubuğuna da gerek kalmaz.</para>
///
/// <para><b>Neden ViewModel'de hesaplanmıyor:</b> bir sekmenin genişliği yazı tipine, etiket
/// uzunluğuna, ikona ve temaya bağlıdır. Bunu tahmin etmek kırılgan olurdu; gerçek ölçüm
/// yalnız yerleşim (layout) sırasında bilinir. Bu yüzden ölçüm burada yapılır ve sonuç
/// <see cref="TasmaDegisti"/> ile dışarı bildirilir — <b>iş kuralı içermez</b>, saf yerleşimdir.</para>
///
/// <para><b>Sonsuz döngü koruması:</b> taşma bilgisi çocukların <c>IsVisible</c> değeri
/// DEĞİŞTİRİLEREK verilmez (bu yeni bir ölçüm turu tetikler ve titremeye yol açardı);
/// sığmayan çocuklar sıfır boyutlu bir alana yerleştirilir ve panel kırpılır.</para>
/// </summary>
public sealed class TasanSekmePaneli : Panel
{
    /// <summary>Sekmeler arası boşluk (XAML'deki eski StackPanel Spacing="2" ile aynı).</summary>
    public static readonly StyledProperty<double> AraBoslukProperty =
        AvaloniaProperty.Register<TasanSekmePaneli, double>(nameof(AraBosluk), 2d);

    public double AraBosluk
    {
        get => GetValue(AraBoslukProperty);
        set => SetValue(AraBoslukProperty, value);
    }

    /// <summary>
    /// Taşan sekmelerin <c>DataContext</c> listesi DEĞİŞTİĞİNDE çalıştırılan komut (sıra korunur).
    /// Boş liste = her şey sığdı. ViewModel bu listeden "Diğer Sayfalar" menüsünü kurar.
    ///
    /// <para>Komut olarak tasarlandı ki panel ViewModel tipini TANIMASIN: bağ XAML'de kurulur,
    /// kod-arkası (code-behind) gerekmez.</para>
    /// </summary>
    public static readonly StyledProperty<System.Windows.Input.ICommand?> TasmaKomutuProperty =
        AvaloniaProperty.Register<TasanSekmePaneli, System.Windows.Input.ICommand?>(nameof(TasmaKomutu));

    public System.Windows.Input.ICommand? TasmaKomutu
    {
        get => GetValue(TasmaKomutuProperty);
        set => SetValue(TasmaKomutuProperty, value);
    }

    /// <summary>
    /// AKTİF sekmenin sıra numarası (ViewModel'den bağlanır; -1 = yok).
    ///
    /// <para><b>Neden gerekli (görsel doğrulamada bulundu, 2026-09-06):</b> yeni açılan ekranın sekmesi
    /// listenin SONUNA eklenir. Bar doluysa sekme doğrudan taşmaya düşüyor, kullanıcı ekranı açtığı
    /// hâlde sekmesini göremiyor ve şeritte hiçbir sekme vurgulu kalmıyordu. Panel, aktif sekmeye
    /// <b>her zaman</b> yer açar; gerekirse ondan önceki son sekmeyi menüye gönderir.</para>
    ///
    /// <para>Karar burada verilir çünkü "kaç sekme sığıyor" bilgisi yalnız yerleşim sırasında bilinir;
    /// ViewModel'de listeyi yeniden sıralayarak çözmek yerleşimle yarışır ve titremeye yol açardı.</para>
    /// </summary>
    public static readonly StyledProperty<int> AktifSiraProperty =
        AvaloniaProperty.Register<TasanSekmePaneli, int>(nameof(AktifSira), -1);

    public int AktifSira
    {
        get => GetValue(AktifSiraProperty);
        set => SetValue(AktifSiraProperty, value);
    }

    static TasanSekmePaneli()
    {
        AffectsMeasure<TasanSekmePaneli>(AraBoslukProperty, AktifSiraProperty);
    }

    private List<object> _sonTasanlar = new();
    /// <summary>Bu ölçümde ÇİZİLECEK çocukların sırası (Arrange aynı kararı kullanır).</summary>
    private readonly HashSet<int> _gorunurler = new();

    protected override Size MeasureOverride(Size availableSize)
    {
        var bosluk = AraBosluk;
        var sinir = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
        var genislikler = new double[Children.Count];
        double yukseklik = 0;

        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(double.PositiveInfinity, availableSize.Height));
            genislikler[i] = Children[i].DesiredSize.Width;
            yukseklik = Math.Max(yukseklik, Children[i].DesiredSize.Height);
        }

        // 1) Soldan sağa sığdığı kadarını al.
        _gorunurler.Clear();
        double toplam = 0;
        for (int i = 0; i < genislikler.Length; i++)
        {
            var eklenecek = _gorunurler.Count == 0 ? genislikler[i] : genislikler[i] + bosluk;
            if (toplam + eklenecek > sinir) break;   // biri sığmadıysa sonrakiler de menüye gider (sıra korunur)
            toplam += eklenecek;
            _gorunurler.Add(i);
        }

        // 2) Aktif sekme dışarıda kaldıysa ona YER AÇ: sondaki görünenleri sırayla menüye gönder.
        var aktif = AktifSira;
        if (aktif >= 0 && aktif < genislikler.Length && !_gorunurler.Contains(aktif))
        {
            var gereken = _gorunurler.Count == 0 ? genislikler[aktif] : genislikler[aktif] + bosluk;
            while (_gorunurler.Count > 0 && toplam + gereken > sinir)
            {
                var son = _gorunurler.Max();
                _gorunurler.Remove(son);
                toplam -= _gorunurler.Count == 0 ? genislikler[son] : genislikler[son] + bosluk;
                gereken = _gorunurler.Count == 0 ? genislikler[aktif] : genislikler[aktif] + bosluk;
            }
            if (toplam + gereken <= sinir) { _gorunurler.Add(aktif); toplam += gereken; }
        }

        var tasanlar = new List<object>();
        for (int i = 0; i < Children.Count; i++)
            if (!_gorunurler.Contains(i) && Children[i].DataContext is { } dc) tasanlar.Add(dc);

        Bildir(tasanlar);
        return new Size(double.IsInfinity(availableSize.Width) ? toplam : Math.Min(toplam, sinir),
                        double.IsInfinity(availableSize.Height) ? yukseklik : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var bosluk = AraBosluk;
        double x = 0;
        var ilk = true;

        for (int i = 0; i < Children.Count; i++)
        {
            var c = Children[i];
            if (!_gorunurler.Contains(i))
            {
                // Sığmayan sekme çizilmez. IsVisible'a DOKUNULMAZ (yeni ölçüm turu → titreme olurdu).
                c.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }
            var g = c.DesiredSize.Width;
            var baslangic = ilk ? x : x + bosluk;
            c.Arrange(new Rect(baslangic, 0, g, finalSize.Height));
            x = baslangic + g;
            ilk = false;
        }
        return finalSize;
    }

    /// <summary>Taşma listesi gerçekten değiştiyse haber verir (her ölçümde gereksiz bildirim yok).</summary>
    private void Bildir(List<object> tasanlar)
    {
        if (tasanlar.Count == _sonTasanlar.Count && tasanlar.SequenceEqual(_sonTasanlar)) return;
        _sonTasanlar = tasanlar;
        // Ölçüm sırasında dışarıya haber vermek yeni bir yerleşim isteği doğurabilir; bir sonraki
        // tura bırakılır ki aynı tur içinde yeniden ölçüm zinciri kurulmasın.
        var kopya = _sonTasanlar;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var k = TasmaKomutu;
            if (k is not null && k.CanExecute(kopya)) k.Execute(kopya);
        }, Avalonia.Threading.DispatcherPriority.Background);
    }
}
