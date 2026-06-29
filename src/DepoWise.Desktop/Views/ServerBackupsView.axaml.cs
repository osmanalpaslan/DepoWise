using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DepoWise.Desktop.Views;

public partial class ServerBackupsView : UserControl
{
    public ServerBackupsView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
