using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DepoWise.Desktop.Views;

/// <summary>STK-08 — Atanmamış stok dağıtımı ekranı. Kod-arkası iş kuralı içermez (MVVM).</summary>
public partial class StockDistributeView : UserControl
{
    public StockDistributeView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
