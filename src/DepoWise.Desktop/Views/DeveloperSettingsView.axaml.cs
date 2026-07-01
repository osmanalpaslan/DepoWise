using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DepoWise.Desktop.Views;

public partial class DeveloperSettingsView : UserControl
{
    public DeveloperSettingsView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
