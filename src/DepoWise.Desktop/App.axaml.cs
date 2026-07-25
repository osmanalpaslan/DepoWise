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
            DepoWise.Desktop.Theming.ThemeService.ApplySaved(); // kullanıcının seçtiği tema modu (Koyu/Açık/Sistem)

            // Kullanıcı isteği: uygulama kapatılınca oturum biter → her açılışta LOGIN ekranı gelir.
            // "Beni Hatırla" artık yalnız kullanıcı ADINI ön-doldurur (otomatik giriş YAPILMAZ). Kapanışta
            // (Exit) auto-login token'ı da temizlenir ki geçmiş token ile kimse otomatik girmesin.
            desktop.Exit += (_, _) => { try { RememberMeService.Clear(); } catch { } };
            ShowLogin();
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
            ShowSyncThenMain(session, login); // login → eşitleme animasyonu → uygulama
        };
        _desktop.MainWindow = login;
        login.Show();
        old?.Close();
    }

    /// <summary>Login ile uygulama arasında "Web ile Eşitleniyor" ekranını gösterir; eşitleme + min 2 sn
    /// animasyon bitince MainWindow açılır. Kullanıcı yetkileri giriş anında zaten yerele çekilmiştir.</summary>
    private void ShowSyncThenMain(SessionContext session, Avalonia.Controls.Window? toClose)
    {
        if (_desktop is null) return;
        var vm = new SyncViewModel(session);
        var sync = new SyncWindow { DataContext = vm };
        vm.Done = () => Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            // Otomatik güncelleme AÇIKSA: ana pencere AÇILMADAN önce, eşitleme ekranında en son paketi
            // SESSİZCE indir → "Kur / Ertele" sor. Kur → kurar + yeniden başlatır (uygulama kapanır).
            // Ertele → uygulama açılır; ShellViewModel 10 dk sonra tekrar sorar (paket saklanır, tekrar inmez).
            if (AutoUpdateService.IsEnabled(session.CompanyId))
            {
                try
                {
                    var ready = await AutoUpdateService.CheckAndDownloadAsync(session.CompanyId,
                        s => Avalonia.Threading.Dispatcher.UIThread.Post(() => vm.Status = s),
                        p => Avalonia.Threading.Dispatcher.UIThread.Post(() => vm.SetDownloadProgress(p)));
                    if (ready)
                    {
                        var install = await ConfirmService.AskAsync(
                            $"Yeni sürüm {AutoUpdateService.PendingVersion} hazır.\n\n" +
                            "Şimdi kurulsun mu? Uygulama yeniden başlatılır; veritabanınıza dokunulmaz.",
                            "Güncelleme Hazır", "Kur ve Yeniden Başlat", "Ertele");
                        if (install)
                        {
                            vm.Status = "Güncelleme kuruluyor, yeniden başlatılıyor…";
                            AutoUpdateService.InstallPendingNow();   // uygulama kapanır + yeniden açılır
                            return;
                        }
                        AutoUpdateService.Snooze();   // ertele: main açılır, 10 dk sonra tekrar sorulur
                    }
                }
                catch { /* güncelleme akışı girişi asla engellemez */ }
            }

            var main = new MainWindow { DataContext = new ShellViewModel(session) };
            _desktop.MainWindow = main;
            main.Show();
            sync.Close();
        });
        _desktop.MainWindow = sync;
        sync.Show();
        toClose?.Close();
        _ = vm.RunAsync();
    }

    /// <summary>Çıkış Yap: "Beni Hatırla" token'ını sil, oturumu kapat, giriş ekranına dön.</summary>
    public void Logout()
    {
        RememberMeService.Clear();
        DepoWise.Application.Security.DeveloperMode.IsActive = false; // çıkışta geliştirici modu daima kapanır
        DesktopServices.Session = null;
        ShowLogin();
    }

    public static new App? Current => Avalonia.Application.Current as App;
}