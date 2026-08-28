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

    /// <summary>BAR-01 (ADR-177): Ctrl+K → global arama kutusuna odak + tümünü seç. USB barkod/QR
    /// okuyucuyla taramadan önce tek tuşla kutuya gelinir; seçim sayesinde yeni tarama eskisini ezer.
    /// Yalnız kutu görünürken (giriş sonrası üst bar) çalışır; başka kısayolla çakışmaz (mevcut tek
    /// pencere-kısayolu buydu). Odaktaki kontrol Ctrl+K'yı kendisi işlerse ona dokunulmaz.</summary>
    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.K
            && e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)
            && GlobalSearchBox.IsEffectivelyVisible)
        {
            GlobalSearchBox.Focus();
            GlobalSearchBox.SelectAll();
            e.Handled = true;
            return;
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
            await ConfirmService.AskAsync(
                $"Bekleyen bir güncelleme var (sürüm {AutoUpdateService.PendingVersion}).\n\n" +
                "Uygulama kapatılmadan güncelleme kurulacak ve yeniden başlatılacaktır.",
                "Güncelleme Kuruluyor", "Tamam", "Tamam");
            AutoUpdateService.InstallPendingNow();   // kapanır + yeniden başlar
            return;
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
