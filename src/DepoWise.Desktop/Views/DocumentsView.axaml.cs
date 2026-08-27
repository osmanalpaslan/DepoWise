using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DepoWise.Desktop.Views;

public partial class DocumentsView : UserControl
{
    public DocumentsView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
