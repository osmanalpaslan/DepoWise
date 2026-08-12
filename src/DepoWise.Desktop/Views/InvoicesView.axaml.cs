using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DepoWise.Desktop.Views;

/// <summary>G4-2 — Faturalar ekranı. İş kuralı YOKTUR (MVVM): tüm mantık InvoicesViewModel'dedir.</summary>
public partial class InvoicesView : UserControl
{
    public InvoicesView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
