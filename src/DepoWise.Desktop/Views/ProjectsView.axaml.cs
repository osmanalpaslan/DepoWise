using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DepoWise.Desktop.Views;

public partial class ProjectsView : UserControl
{
    public ProjectsView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
