using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using DepoWise.Desktop.ViewModels;

namespace DepoWise.Desktop.Views;

public partial class PersonnelView : UserControl
{
    public PersonnelView() => InitializeComponent();

    /// <summary>Çift tık → düzenleme formu (İş #4, 2026-08-09). Malzemeler/Araçlar ekranlarındaki AYNI desen:
    /// tek tık seçim, çift tık düzenleme. Yetki, firma izolasyonu ve düzenleme kilidi kontrolleri
    /// <c>BeginEdit</c> komutunun içindedir — bu kısayol hiçbir kontrolü atlamaz.</summary>
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is PersonnelViewModel vm && vm.BeginEditCommand.CanExecute(null))
            vm.BeginEditCommand.Execute(null);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
