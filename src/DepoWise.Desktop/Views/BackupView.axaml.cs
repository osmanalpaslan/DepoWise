using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DepoWise.Desktop.Views;

public partial class BackupView : UserControl
{
    public BackupView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
