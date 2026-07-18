using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using DepoWise.Desktop.ViewModels;

namespace DepoWise.Desktop.Views;

public partial class MaterialsView : UserControl
{
    public MaterialsView()
    {
        InitializeComponent();
    }

    // Çift tık: ayrı düzenle/kaydet/sil penceresi (kullanıcı isteği 2026-07-19). Tek tık = detay paneli (korunur).
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MaterialsViewModel vm && vm.QuickEditSelectedCommand.CanExecute(null))
            vm.QuickEditSelectedCommand.Execute(null);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
