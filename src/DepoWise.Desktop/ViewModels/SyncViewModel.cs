using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Login ↔ uygulama arası "Web ile eşitleniyor" ekranı. Kullanıcı yetkileri giriş anında zaten yerele
/// çekilir (ServerAuthClient.ImportRemoteUser); burada tanımlar da senkronlanır + dairesel 0-100% animasyon
/// gösterilir. Animasyon EN AZ 2 sn sürer (eşitleme 1 sn'nin altında bitse bile), sonra uygulama açılır.
/// </summary>
public sealed partial class SyncViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    [ObservableProperty] private double _progress;      // 0-100 (arc + yüzde metni)
    [ObservableProperty] private string _percentText = "0%";
    [ObservableProperty] private string _status = "Web ile eşitleniyor…";
    [ObservableProperty] private string _displayName = "";

    /// <summary>Eşitleme + animasyon bitince çağrılır → uygulama açılır.</summary>
    public Action? Done { get; set; }

    public SyncViewModel(SessionContext session)
    {
        _session = session;
        try { DisplayName = DesktopServices.DisplayName(session.UserId); } catch { }
    }

    public async Task RunAsync()
    {
        var sw = Stopwatch.StartNew();
        const int animMs = 1800; // ilerlemeyi ~1.8 sn'de doldur
        const int minMs = 2000;  // toplam en az 2 sn ekranda kal

        // Gerçek iş: tanımları arka planda senkronla (yetkiler login'de zaten çekildi).
        var syncTask = Task.Run(async () =>
        {
            try { await LookupSyncService.SyncNowAsync(null); } catch { }
        });

        // Yumuşak 0→100 animasyon
        while (Progress < 100)
        {
            var p = Math.Min(100, sw.ElapsedMilliseconds * 100.0 / animMs);
            SetProgress(p);
            await Task.Delay(16);
        }
        SetProgress(100);
        Status = "Tamamlandı";

        var remaining = minMs - (int)sw.ElapsedMilliseconds;
        if (remaining > 0) await Task.Delay(remaining);
        try { await syncTask; } catch { }

        Done?.Invoke();
    }

    private void SetProgress(double p)
    {
        Progress = p;
        PercentText = $"{(int)Math.Round(p)}%";
    }

    /// <summary>Otomatik güncelleme indirme yüzdesi (eşitleme ekranında; ana pencere açılmadan önce).</summary>
    public void SetDownloadProgress(int p) => SetProgress(Math.Clamp(p, 0, 100));
}
