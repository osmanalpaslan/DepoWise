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

    private IListGridViewModel? _vm;
    private bool _dragging;
    private double _dragStartX;
    private double _dragStartWidth;

    public SortHeader()
    {
        ColumnDefinitions = new ColumnDefinitions("*,6");

        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        stack.Children.Add(_label);
        stack.Children.Add(_arrow);

        _button = new Button
        {
            Content = stack,
            Padding = new Thickness(2, 4),
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

    /// <summary>Kişiye özel kayıtlı genişlik varsa XAML'deki sabit MinWidth'i BÜYÜTÜR (küçültmez) —
    /// böylece kullanıcı önceden ayarladığı genişlik bir sonraki açılışta korunur.</summary>
    private void ApplySavedWidth()
    {
        if (_vm is null || string.IsNullOrEmpty(ColumnKey)) return;
        var saved = _vm.GetColumnWidth(ColumnKey);
        if (saved > 0 && (double.IsNaN(MinWidth) || saved > MinWidth)) MinWidth = saved;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(IListGridViewModel.SortState)) UpdateArrow();
    }

    private void UpdateArrow()
    {
        if (_vm is null || string.IsNullOrEmpty(ColumnKey)) { _arrow.Text = ""; return; }
        var (col, desc) = _vm.SortState;
        _arrow.Text = col == ColumnKey ? (desc ? "▼" : "▲") : "";
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (string.IsNullOrEmpty(ColumnKey)) return;
        _dragging = true;
        _dragStartX = e.GetPosition(this).X;
        _dragStartWidth = double.IsNaN(MinWidth) || MinWidth <= 0 ? Bounds.Width : MinWidth;
        e.Pointer.Capture(_grip);
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || string.IsNullOrEmpty(ColumnKey)) return;
        var x = e.GetPosition(this).X;
        var newWidth = Math.Max(40, Math.Min(600, _dragStartWidth + (x - _dragStartX)));
        MinWidth = newWidth;
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
