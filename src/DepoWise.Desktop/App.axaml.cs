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

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var boot = DesktopBootstrap.Run();
            ThemeApplier.Apply(this, boot.Theme);   // merkezi tema (sabit renk yok)
            DesktopServices.Initialize(boot);       // servisler + ilk açılış admin seed

            // Önce giriş ekranı; başarılı login → MainWindow (gerçek oturum + yetkiye göre menü)
            var loginVm = new LoginViewModel();
            var login = new LoginWindow { DataContext = loginVm };
            loginVm.OnLoggedIn = session =>
            {
                var main = new MainWindow { DataContext = new ShellViewModel(session) };
                desktop.MainWindow = main;
                main.Show();
                login.Close();
            };
            desktop.MainWindow = login;
        }

        base.OnFrameworkInitializationCompleted();
    }
}