using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using DepoWise.Desktop.ViewModels;

namespace DepoWise.Desktop.Views;

public partial class RequestsView : UserControl
{
    public RequestsView() => InitializeComponent();

    /// <summary>Çift tık → talebi düzenleme formuna yükler (İş #4, 2026-08-09). Malzemeler/Araçlar/Personel
    /// ekranlarındaki AYNI desen. Yetki, firma izolasyonu ve "onaylanmış talep düzenlenemez" kuralı
    /// <c>BeginEditRequest</c> komutunun içindedir — bu kısayol hiçbir kontrolü atlamaz.
    /// Hem talep listesine hem "onay bekleyenler" listesine bağlıdır (ikisi de aynı seçime yazar).</summary>
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is RequestsViewModel vm && vm.BeginEditRequestCommand.CanExecute(null))
            vm.BeginEditRequestCommand.Execute(null);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
