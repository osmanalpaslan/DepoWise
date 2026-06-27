using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;

namespace DepoWise.Desktop.Views;

/// <summary>Fotoğrafı orijinal boyutta gösteren pencere; fare tekerleği ile yakınlaştır/uzaklaştır.</summary>
public partial class PhotoViewerWindow : Window
{
    private Bitmap? _bitmap;
    private double _scale = 1.0;
    private const double MinScale = 0.05;
    private const double MaxScale = 16.0;

    public PhotoViewerWindow() => InitializeComponent();

    public PhotoViewerWindow(Bitmap bitmap) : this()
    {
        _bitmap = bitmap;
        var img = this.FindControl<Image>("Img")!;
        img.Source = bitmap;
        _scale = 1.0;             // orijinal boyutta aç
        ApplyScale();

        img.PointerWheelChanged += OnWheel;
        img.DoubleTapped += (_, _) => { _scale = 1.0; ApplyScale(); };
        var scroller = this.FindControl<ScrollViewer>("Scroller")!;
        scroller.PointerWheelChanged += OnWheel;
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        _scale *= e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        if (_scale < MinScale) _scale = MinScale;
        if (_scale > MaxScale) _scale = MaxScale;
        ApplyScale();
        e.Handled = true;
    }

    private void ApplyScale()
    {
        if (_bitmap is null) return;
        var img = this.FindControl<Image>("Img")!;
        img.Width = _bitmap.Size.Width * _scale;
        img.Height = _bitmap.Size.Height * _scale;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
