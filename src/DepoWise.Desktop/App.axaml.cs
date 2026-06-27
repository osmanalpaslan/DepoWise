using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using DepoWise.Application.Security;
using DepoWise.Desktop.Theming;
using DepoWise.Desktop.ViewModels;
using DepoWise.Desktop.Views;

namespace DepoWise.Desktop;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private IClassicDesktopStyleApplicationLifetime? _desktop;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            var boot = DesktopBootstrap.Run();
            ThemeApplier.Apply(this, boot.Theme);   // merkezi tema (sabit renk yok)
            DesktopServices.Initialize(boot);       // servisler + ilk açılış admin seed

            // "Beni Hatırla": geçerli token varsa giriş ekranını atla
            var remembered = RememberMeService.TryAutoLogin();
            if (remembered is not null)
            {
                DesktopServices.Session = remembered;
                desktop.MainWindow = new MainWindow { DataContext = new ShellViewModel(remembered) };
            }
            else
            {
                ShowLogin();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Giriş penceresini gösterir; başarılı girişte MainWindow açılır. Açılışta ve çıkış sonrasında kullanılır.</summary>
    public void ShowLogin()
    {
        if (_desktop is null) return;
        var old = _desktop.MainWindow;
        var loginVm = new LoginViewModel();
        var login = new LoginWindow { DataContext = loginVm };
        loginVm.OnLoggedIn = session =>
        {
            DesktopServices.Session = session;
            var main = new MainWindow { DataContext = new ShellViewModel(session) };
            _desktop.MainWindow = main;
            main.Show();
            login.Close();
        };
        _desktop.MainWindow = login;
        login.Show();
        old?.Close();
    }

    /// <summary>Çıkış Yap: "Beni Hatırla" token'ını sil, oturumu kapat, giriş ekranına dön.</summary>
    public void Logout()
    {
        RememberMeService.Clear();
        DesktopServices.Session = null;
        ShowLogin();
    }

    public static new App? Current => Avalonia.Application.Current as App;
}