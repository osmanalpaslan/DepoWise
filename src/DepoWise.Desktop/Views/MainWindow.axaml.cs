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

    /// <summary>Uygulama kapatılırken onay iste (kazara kapatmayı engeller) + kapanmadan ÖNCE bekleyen veriyi
    /// sunucuya gönder (kullanıcı isteği 2026-07-19: "Eşitle"ye basmadan da veri gitsin). Push en fazla 10 sn
    /// bekletir — küçük değişiklikler hızlıdır, çevrimdışıysa anında döner; kapanışı kilitlemez.</summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_confirmedClose) { base.OnClosing(e); return; }
        e.Cancel = true;
        var ok = await ConfirmService.AskAsync(
            "Uygulamayı kapatmak istediğinize emin misiniz?", "Uygulamadan Çık",
            "Evet, Kapat", "Vazgeç");
        if (ok)
        {
            _confirmedClose = true;
            // Bekleyen veriyi son bir kez sunucuya gönder (sınırlı bekleme ile).
            // Z1: başka bir eşitleme sürüyorsa push'u ATLA (o zaten gönderiyor); KAPANIŞ her hâlükârda olur.
            if (SyncGate.TryEnter())
            {
                try { await Task.WhenAny(BusinessSyncPushService.PushAsync(), Task.Delay(10000)); } catch { }
                finally { SyncGate.Exit(); }
            }
            Close();
        }
    }
}
