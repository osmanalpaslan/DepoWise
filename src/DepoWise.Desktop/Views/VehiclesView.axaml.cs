using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using DepoWise.Desktop.ViewModels;

namespace DepoWise.Desktop.Views;

public partial class VehiclesView : UserControl
{
    public VehiclesView() => InitializeComponent();

    // Çift tık: ayrı düzenle/kaydet/sil penceresi (kullanıcı isteği 2026-07-19). Tek tık = detay paneli (korunur).
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is VehiclesViewModel vm && vm.QuickEditSelectedCommand.CanExecute(null))
            vm.QuickEditSelectedCommand.Execute(null);
    }

    // İşlem Geçmişi (madde 5, kullanıcı isteği 2026-08-06): çift tık → salt-okunur detay penceresi. Yalnız
    // Günlük Faaliyet kaynaklı satırlarda "Kaydı Görüntüle" (Günlük Faaliyet ekranına gider); sistem olayları
    // zaten bu ekranda görüntülendiği için buton gösterilmez.
    private void OnHistoryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not ListBox lb || lb.SelectedItem is not MovementDisplay row) return;
        var win = new HistoryDetailWindow("İşlem Detayı", row.DateText, row.Kind, row.Description,
            onOpenRecord: row.CanOpenRecord ? () => ShellViewModel.Current?.NavigateTo("daily_activity", null) : null);
        var owner = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d ? d.MainWindow : null;
        if (owner is not null) win.ShowDialog(owner); else win.Show();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
