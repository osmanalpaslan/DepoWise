using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DepoWise.Desktop.Views;

// GELİŞTİRME amaçlı bileşen galerisi — üretim navigasyonunda kullanılmaz.
public partial class ComponentGalleryView : UserControl
{
    public ComponentGalleryView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
