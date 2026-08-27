using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DepoWise.Desktop.ViewModels;

namespace DepoWise.Desktop;

/// <summary>
/// Malzeme/Araç Listesi başlık hücresi (kullanıcı isteği 2026-07-18): tıklayınca sırala (madde 5) + sağ
/// kenarından sürükleyerek genişlet (madde 3, "Excel'de olan hücre/sütun yapısı gibi").
///
/// Grid alt sınıfıdır — TemplatedControl DEĞİL: kendi görsel çocuklarını (Button + sürükleme tutamağı)
/// constructor'da doğrudan ekler. Böylece Themes/ altında yeni bir ControlTheme GEREKMEZ; Button'ın zaten
/// var olan "Ghost" stili kullanılır (bu dosyanın diğer stilleri gibi). Bu tasarım kasıtlı — bu ortamda
/// Avalonia'yı görsel test edemediğimizden, yeni bir şablon dosyası eklemenin "sessizce boş görünme" riski
/// taşıdığı düşünülerek EN GÜVENLİ (kanıtlanmış bileşenlere dayanan) yol seçildi.
///
/// DataContext, IListGridViewModel uygulayan liste ekranı ViewModel'i olmalıdır (MaterialsViewModel/
/// VehiclesViewModel) — genişlik/sıralama durumu ORADAN okunur, buraya iş kuralı YAZILMAZ (MVVM korunur;
/// bu sınıf yalnız pointer olaylarını VM komut/metotlarına iletir).
/// </summary>
public sealed class SortHeader : Grid
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SortHeader, string?>(nameof(Text));
    public static readonly StyledProperty<string?> ColumnKeyProperty =
        AvaloniaProperty.Register<SortHeader, string?>(nameof(ColumnKey));

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? ColumnKey { get => GetValue(ColumnKeyProperty); set => SetValue(ColumnKeyProperty, value); }

    private readonly TextBlock _label = new();
    private readonly TextBlock _arrow = new() { Margin = new Thickness(2, 0, 0, 0) };
    private readonly Button _button;
    private readonly Border _grip;
    private readonly Border _vurgu;   // M7: siralanan kolonun altindaki 2px aksan cizgisi

    private IListGridViewModel? _vm;
    private bool _dragging;
    private double _dragStartX;
    private double _dragStartWidth;

    public SortHeader()
    {
        ColumnDefinitions = new ColumnDefinitions("*,6");

        // M7 — SIRALANAN KOLON ISARETI. Satir 1 yalnizca 2px yuksekliginde bir vurgu seridi tasir;
        // _button ve _grip icin satir belirtilmez (varsayilan Row 0). Siralama/surukleme MANTIGI degismez.
        RowDefinitions = new RowDefinitions("*,2");

        _vurgu = new Border
        {
            Height = 2,
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsHitTestVisible = false,
        };
        _vurgu.Bind(Border.BackgroundProperty, this.GetResourceObservable("AccentBrush"));
        SetRow(_vurgu, 1);
        SetColumnSpan(_vurgu, 2);
        Children.Add(_vurgu);

        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        stack.Children.Add(_label);
        stack.Children.Add(_arrow);

        // ⭐ MAS-04 (kullanıcı bildirimi 2026-08-26) — HİZA: başlık yazısı, ALTINDAKİ veri hücresiyle
        // TAM AYNI noktadan başlamalı. Veri hücresi düz bir metindir (kenarlık/iç boşluk yok); başlık ise
        // "Ghost" butonudur ve tema ona 1 px kenarlık verir. Kenarlık + 2 px iç boşluk, başlık yazısını
        // veriden 3 px sağa kaydırıyordu. Kolon AYIRICISI artık ayrı çiziliyor (ctrl:ColumnRules), bu
        // yüzden her başlığın kendi kutu çizgisine gerek yok: kenarlık 0, sol iç boşluk 0.
        _button = new Button
        {
            Content = stack,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        _button.Classes.Add("Ghost");
        _button.Click += (_, _) => _vm?.SortByCommand.Execute(ColumnKey);
        SetColumn(_button, 0);
        Children.Add(_button);

        _grip = new Border
        {
            Width = 6,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
        };
        _grip.PointerPressed += OnGripPressed;
        _grip.PointerMoved += OnGripMoved;
        _grip.PointerReleased += OnGripReleased;
        SetColumn(_grip, 1);
        Children.Add(_grip);

        DataContextChanged += (_, _) => AttachVm();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == TextProperty) _label.Text = Text;
        else if (e.Property == ColumnKeyProperty) { ApplySavedWidth(); UpdateArrow(); }
    }

    private void AttachVm()
    {
        if (_vm is INotifyPropertyChanged oldNp) oldNp.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as IListGridViewModel;
        if (_vm is INotifyPropertyChanged np) np.PropertyChanged += OnVmPropertyChanged;
        ApplySavedWidth();
        UpdateArrow();
    }

    /// <summary>
    /// Genişliğin TEK KAYNAĞI ViewModel'dir (kullanıcı isteği 2026-08-08 — hizalama hatası düzeltmesi).
    /// Eskiden başlık sürüklerken kendi MinWidth'ini ayrıca değiştiriyordu → başlık, filtre satırı ve gövde
    /// hücreleri FARKLI genişlik kaynağı kullandığı için hizalar kayıyor, ancak liste yeniden kurulunca
    /// (eşitleme sonrası) düzeliyordu. Artık üçü de aynı değeri (VM.ColWidths) okur: Min=Max ile SIKI bağlanır,
    /// böylece SharedSizeGroup üç satırı da aynı ölçüde tutar.
    /// </summary>
    private void ApplySavedWidth()
    {
        if (_vm is null || string.IsNullOrEmpty(ColumnKey)) return;
        var w = _vm.GetColumnWidth(ColumnKey);
        if (w <= 0) return;
        MinWidth = w;
        MaxWidth = w;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(IListGridViewModel.SortState)) UpdateArrow();
        // Genişlik VM'de değişti (sürükleme/varsayılana dönüş) → başlık da AYNI değeri alır (tek kaynak).
        if (e.PropertyName is null or "ColWidths") ApplySavedWidth();
    }

    /// <summary>Ok + M7 vurgusu. ClearValue ile pasif baslik, Components.axaml'deki
    /// <c>Border.TableHeader :is(TextBlock)</c> kuralina (TableHeaderTextBrush) geri doner.</summary>
    private void UpdateArrow()
    {
        if (_vm is null || string.IsNullOrEmpty(ColumnKey))
        {
            _arrow.Text = "";
            _vurgu.IsVisible = false;
            _label.ClearValue(TextBlock.ForegroundProperty);
            _arrow.ClearValue(TextBlock.ForegroundProperty);
            return;
        }
        var (col, desc) = _vm.SortState;
        var aktif = col == ColumnKey;
        _arrow.Text = aktif ? (desc ? "▼" : "▲") : "";
        _vurgu.IsVisible = aktif;
        if (aktif)
        {
            _label.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AccentBrush"));
            _arrow.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AccentBrush"));
        }
        else
        {
            _label.ClearValue(TextBlock.ForegroundProperty);
            _arrow.ClearValue(TextBlock.ForegroundProperty);
        }
    }

    // NOT (2026-08-08): Sürükleme ölçümü PENCEREYE göre yapılır — eskiden `GetPosition(this)` kullanılıyordu;
    // `this` sürükleme sırasında GENİŞLEDİĞİ/DARALDIĞI için ölçüm çerçevesi de kayıyor ve fare hareketi kendini
    // besliyordu (özellikle SOLA çekerken sıçrama/geri dönme). Pencere koordinatı sabittir → hareket birebir.
    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (string.IsNullOrEmpty(ColumnKey)) return;
        _dragging = true;
        _dragStartX = e.GetPosition(null).X;                       // pencereye göre (sabit çerçeve)
        _dragStartWidth = _vm?.GetColumnWidth(ColumnKey) ?? Bounds.Width;   // tek kaynak: VM
        e.Pointer.Capture(_grip);
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || string.IsNullOrEmpty(ColumnKey)) return;
        var x = e.GetPosition(null).X;
        var newWidth = Math.Max(40, Math.Min(600, _dragStartWidth + (x - _dragStartX)));
        // Kendi MinWidth'ini DOĞRUDAN değiştirme: VM'e yaz → ApplySavedWidth ile başlık+filtre+gövde birlikte güncellenir.
        _vm?.PreviewColumnWidth(ColumnKey, newWidth);
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        _vm?.CommitColumnWidth();
    }
}
