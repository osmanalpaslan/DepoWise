using System.Threading.Tasks;
using Avalonia.Controls;

namespace DepoWise.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _confirmedClose;

    public MainWindow()
    {
        InitializeComponent();
    }

    private static bool KomutCalistir(System.Windows.Input.ICommand k)
    {
        if (!k.CanExecute(null)) return false;
        k.Execute(null);
        return true;
    }

    // ⭐ M-62 (2/4) (F-095): "kaydedildi" bildirimi — sağ altta, 4 sn, en fazla 3 tane. Abonelik pencere açıkken.
    private Avalonia.Controls.Notifications.WindowNotificationManager? _bildirim;

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        _bildirim = new Avalonia.Controls.Notifications.WindowNotificationManager(this)
        {
            Position = Avalonia.Controls.Notifications.NotificationPosition.BottomRight,
            MaxItems = 3,
        };
        Bildirimler.BasariGoster += BildirimGoster;
    }

    protected override void OnClosed(System.EventArgs e)
    {
        Bildirimler.BasariGoster -= BildirimGoster;
        base.OnClosed(e);
    }

    private void BildirimGoster(string mesaj)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() => _bildirim?.Show(new Avalonia.Controls.Notifications.Notification(
            "İşlem tamam", mesaj, Avalonia.Controls.Notifications.NotificationType.Success, System.TimeSpan.FromSeconds(4))));

    /// <summary>BAR-01 (ADR-177): Ctrl+K → global arama kutusuna odak + tümünü seç. USB barkod/QR
    /// okuyucuyla taramadan önce tek tuşla kutuya gelinir; seçim sayesinde yeni tarama eskisini ezer.
    /// Yalnız kutu görünürken (giriş sonrası üst bar) çalışır; başka kısayolla çakışmaz (mevcut tek
    /// pencere-kısayolu buydu). Odaktaki kontrol Ctrl+K'yı kendisi işlerse ona dokunulmaz.</summary>
    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        // ⭐ iOS tasarım (2026-10-28): kutu üst çubukta gizli → Ctrl+K önce görünür yapar (giriş sonrası kabuk varken).
        if (e.Key == Avalonia.Input.Key.K
            && e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)
            && DataContext is DepoWise.Desktop.ViewModels.ShellViewModel aramaKabugu)
        {
            aramaKabugu.AramaGorunur = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { GlobalSearchBox.Focus(); GlobalSearchBox.SelectAll(); },
                Avalonia.Threading.DispatcherPriority.Input);
            e.Handled = true;
            return;
        }
        // ⭐ M-62 (3/4) (F-093): pencere düzeyi kısayollar — ayrıntı Kisayollar.cs. Karşılığı olmayan ekranda tuş serbest kalır.
        if (DataContext is DepoWise.Desktop.ViewModels.ShellViewModel kabuk)
        {
            var ctrl = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
            var shift = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift);
            var islendi = (e.Key, ctrl) switch
            {
                (Avalonia.Input.Key.F5, false) => Kisayollar.Yenile(kabuk.CurrentPage),
                (Avalonia.Input.Key.N, true) => Kisayollar.AnaEylem(SayfaAlani),
                (Avalonia.Input.Key.F, true) => Kisayollar.AramayaOdaklan(SayfaAlani),
                (Avalonia.Input.Key.W, true) => Kisayollar.SekmeKapat(kabuk),
                (Avalonia.Input.Key.Tab, true) => Kisayollar.SekmeGec(kabuk, shift ? -1 : +1),
                (Avalonia.Input.Key.F1, false) => KomutCalistir(kabuk.ShowScreenInfoCommand),
                _ => false,
            };
            if (islendi) { e.Handled = true; return; }
        }
        base.OnKeyDown(e);
    }

    /// <summary>Uygulama kapatılırken onay iste (kazara kapatmayı engeller) + kapanmadan ÖNCE bekleyen veriyi
    /// sunucuya gönder (kullanıcı isteği 2026-07-19: "Eşitle"ye basmadan da veri gitsin). Push en fazla 10 sn
    /// bekletir — küçük değişiklikler hızlıdır, çevrimdışıysa anında döner; kapanışı kilitlemez.</summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_confirmedClose) { base.OnClosing(e); return; }
        e.Cancel = true;

        // Onaylanmamış (ertelenmiş) bir güncelleme varsa: kullanıcı kapatarak güncellemeyi ATLAYAMAZ.
        // Kapatma yerine güncelleme ZORLA kurulur ve uygulama yeniden başlatılır (kullanıcı isteği 2026-07-25).
        if (AutoUpdateService.HasPending)
        {
            await ConfirmService.InfoAsync(
                $"Bekleyen bir güncelleme var (sürüm {AutoUpdateService.PendingVersion}).\n\n" +
                "Uygulama kapatılmadan güncelleme kurulacak ve yeniden başlatılacaktır.",
                "Güncelleme Kuruluyor", "Tamam");
            if (AutoUpdateService.InstallPendingNow()) return;   // başarıda kapanır + yeniden başlar
            // Kurulum yapılamadı (2026-09-07): kullanıcı kapatamadan kilitlenmemeli — sebebi söylenir
            // ve normal kapanış akışına devam edilir.
            await ConfirmService.InfoAsync(
                "Güncelleme kurulamadı, uygulama normal şekilde kapatılacak.\n\nSebep: " +
                (AutoUpdateService.SonKurulumHatasi ?? "bilinmiyor"),
                "Güncelleme Kurulamadı", "Tamam");
        }

        var ok = await ConfirmService.AskAsync(
            "Uygulamayı kapatmak istediğinize emin misiniz?", "Uygulamadan Çık",
            "Evet, Kapat", "Vazgeç");
        if (ok)
        {
            _confirmedClose = true;
            // Bekleyen veriyi son bir kez sunucuya gönder (KISA bekleme — çıkış hızlı olmalı, kullanıcı isteği
            // 2026-07-25). Ulaşamazsa hemen kapat: gönderilmemiş veri bir sonraki girişte zaten push edilir
            // (watermark korunur, kayıp yok). Z1: başka eşitleme sürüyorsa push'u ATLA; kapanış her hâlükârda olur.
            if (SyncGate.TryEnter())
            {
                try { await Task.WhenAny(BusinessSyncPushService.PushAsync(), Task.Delay(2000)); } catch { }
                finally { SyncGate.Exit(); }
            }
            Close();
        }
    }

}
