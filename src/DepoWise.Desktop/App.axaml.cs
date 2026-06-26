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
            // Merkezi tema Application.Resources'a uygulanır (ekranlar sabit renk yazmaz).
            ThemeApplier.Apply(this, boot.Theme);

            // NOT: Masaüstü login akışı Faz 05'te. Şu an menü önizlemesi için admin oturumu;
            // yetki mantığı MenuBuilder/AccessControl ile testlerde doğrulanmıştır.
            var previewSession = new SessionContext("preview", "preview",
                new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

            var summary = boot.Health.Ok
                ? $"DB: {boot.Health.DatabasePath} | journal={boot.Health.JournalMode} | DURUM: SAĞLIKLI"
                : $"DB HATA: {boot.Health.Error}";

            desktop.MainWindow = new MainWindow
            {
                DataContext = new ShellViewModel(previewSession, boot.Branding, summary),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}