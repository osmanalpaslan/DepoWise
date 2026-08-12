using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DepoWise.Desktop.Views;

/// <summary>G4-3 — İş kuralı YOKTUR (MVVM): tüm mantık ilgili ViewModel'dedir.</summary>
public partial class FinanceView : UserControl
{
    public FinanceView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
