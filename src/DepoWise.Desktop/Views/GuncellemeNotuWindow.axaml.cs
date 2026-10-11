using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.Views;

public partial class GuncellemeNotuWindow : Window
{
    public GuncellemeNotuWindow() => AvaloniaXamlLoader.Load(this);

    public GuncellemeNotuWindow(GuncellemeNotu not) : this()
    {
        this.FindControl<SelectableTextBlock>("UstText")!.Text =
            $"Uygulamanız güncellendi · {not.TarihMetni}" + (not.MasaustuSurumu is null ? "" : $" · Sürüm {not.MasaustuSurumu}");
        this.FindControl<SelectableTextBlock>("BaslikText")!.Text = "Yenilikler: " + not.Baslik;
        this.FindControl<ItemsControl>("Liste")!.ItemsSource = not.MaddelerIcin(web: false).Select(m => m.Metin).ToList();
        this.FindControl<Button>("TamamBtn")!.Click += (_, _) => Close();
    }
}
