using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using DepoWise.Desktop.ViewModels;
using DepoWise.Infrastructure.Materials;

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

    // İşlem Geçmişi (madde 5, kullanıcı isteği 2026-08-06): çift tık → salt-okunur detay penceresi +
    // "Kaydı Görüntüle" (Stok Hareketleri ekranına, malzeme koduyla arama yaparak gider).
    private void OnHistoryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not ListBox lb || lb.SelectedItem is not MaterialMovementRow row) return;
        if (DataContext is not MaterialsViewModel vm || vm.Detail is null) return;
        var code = vm.Detail.Code;
        var win = new HistoryDetailWindow("İşlem Detayı", row.DateText, row.Label,
            string.IsNullOrEmpty(row.Reference) ? row.QtyText : $"{row.QtyText} · {row.Reference}",
            onOpenRecord: () => ShellViewModel.Current?.NavigateTo("stock:movements", code));
        var owner = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d ? d.MainWindow : null;
        if (owner is not null) win.ShowDialog(owner); else win.Show();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
