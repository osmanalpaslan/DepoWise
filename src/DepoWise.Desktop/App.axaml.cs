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
            // Tarih alanlarının boş hâlindeki İngilizce yer tutucularını ("day/month/year") Türkçeleştirir.
            // Sınıf düzeyinde tek kurulum; hiçbir görünüm dosyası değişmez (bkz. TarihYerTutucu).
            DepoWise.Desktop.Theming.TarihYerTutucu.Kur();
            // ⭐ SES (2026-09-07): bildirim sesleri gömülü kaynaktan belleğe alınır. Sonuç açılış
            // günlüğüne yazılır — bir ses yüklenemiyorsa kullanıcı fark etmeden ÖNCE görülür.
            SesServisi.Onyukle();

            // Kullanıcı isteği: uygulama kapatılınca oturum biter → her açılışta LOGIN ekranı gelir.
            // "Beni Hatırla" artık yalnız kullanıcı ADINI ön-doldurur (otomatik giriş YAPILMAZ). Kapanışta
            // (Exit) auto-login token'ı da temizlenir ki geçmiş token ile kimse otomatik girmesin.
            desktop.Exit += (_, _) => { try { RememberMeService.Clear(); } catch { } };
            // 2026-10-11: masaüstü kısayolu arka planlı simgeyi göstersin (exe simgesi artık şeffaf) — bir kerelik, sessiz.
            KisayolSimgesi.ArkaPlandaDuzelt();
            // ⭐ 2026-10-11 (kullanıcı isteği): AÇILIŞ ANİMASYONU — giriş penceresinden önce bir kez; tıklayınca geçer.
            // Yüklenemezse null döner ve doğrudan giriş açılır (açılış asla engellenmez).
            var acilis = AcilisAnimasyonu.Goster(() => Avalonia.Threading.Dispatcher.UIThread.Post(ShowLogin));
            if (acilis is not null) desktop.MainWindow = acilis;
            else ShowLogin();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Giriş penceresini gösterir; başarılı girişte MainWindow açılır. Açılışta ve çıkış sonrasında kullanılır.</summary>
    public void ShowLogin()
    {
        if (_desktop is null) return;
        var old = _desktop.MainWindow;
        // ⭐ MAS-01 (2026-08-26): eski kabuk statik olaylara abone ve zamanlayıcıları çalışır hâlde
        // kalıyordu → her çıkış→giriş bir kabuk daha biriktiriyordu (dakikada N güncelleme kontrolü,
        // mükerrer güncelleme penceresi, kapanmış pencerelerin işleyicileri, artan bellek).
        if (old?.DataContext is ViewModels.ShellViewModel eskiKabuk) eskiKabuk.Release();
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
            // ⭐ M-12 (K-17 a): zorunlu güncelleme (en düşük sürümün altı ya da 7 günü geçen erteleme) otomatik güncelleme
            // KAPALI olsa bile uygulanır; "Ertele" seçeneği sunulmaz.
            var karar = await AutoUpdateService.KararAsync(session.CompanyId);
            var zorunlu = karar.Karar == DepoWise.Application.Update.GuncellemeKarari.Zorunlu;
            if (AutoUpdateService.IsEnabled(session.CompanyId) || zorunlu)
            {
                try
                {
                    var ready = await AutoUpdateService.CheckAndDownloadAsync(session.CompanyId,
                        s => Avalonia.Threading.Dispatcher.UIThread.Post(() => vm.Status = s),
                        p => Avalonia.Threading.Dispatcher.UIThread.Post(() => vm.SetDownloadProgress(p)));
                    if (ready)
                    {
                        if (zorunlu)
                            await ConfirmService.InfoAsync(AutoUpdateService.ZorunluMetin(karar, AutoUpdateService.PendingVersion ?? "", AutoUpdateService.PendingNotes),
                                "Zorunlu Güncelleme", "Kur ve Yeniden Başlat");
                        var install = zorunlu || await ConfirmService.AskAsync(
                            $"Yeni sürüm {AutoUpdateService.PendingVersion} hazır.\n\n" +
                            "Şimdi kurulsun mu? Uygulama yeniden başlatılır; veritabanınıza dokunulmaz." +
                            AutoUpdateService.ZorunlulukNotu(karar) +
                            AutoUpdateService.NotBolumu(AutoUpdateService.PendingNotes),   // 2026-10-11: güncelleme ÖNCESİ notlar
                            "Güncelleme Hazır", "Kur ve Yeniden Başlat", "Ertele");
                        if (install)
                        {
                            vm.Status = "Güncelleme kuruluyor, yeniden başlatılıyor…";
                            if (AutoUpdateService.InstallPendingNow()) return;   // başarıda uygulama kapanır
                            // Kurulum yapılamadı: kullanıcı KİLİTLENMEZ, sebebi söylenir ve uygulama açılır.
                            await ConfirmService.InfoAsync(
                                "Güncelleme kurulamadı, bu sürümle devam ediliyor.\n\nSebep: " +
                                (AutoUpdateService.SonKurulumHatasi ?? "bilinmiyor"),
                                "Güncelleme Kurulamadı", "Tamam");
                        }
                        AutoUpdateService.Snooze();   // ertele: main açılır, 10 dk sonra tekrar sorulur
                    }
                }
                catch { /* güncelleme akışı girişi asla engellemez */ }
            }

            var main = new MainWindow { DataContext = new ShellViewModel(session) };
            // ⭐ FAZ 4.11 (kullanıcı isteği 2026-09-06): giriş sonrası ilk açılış TAM EKRAN.
            // Kullanıcı pencereyi küçültürse Width/Height değerleri (1180x720) devreye girer.
            main.WindowState = Avalonia.Controls.WindowState.Maximized;
            _desktop.MainWindow = main;
            main.Show();
            // ⭐ 2026-10-11: yeni sürümle İLK açılışta "Yenilikler" penceresi bir kez (güncelleme SONRASI notlar).
            _ = GuncellemeNotuGosterici.GosterGerekirseAsync(main);
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