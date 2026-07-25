using System;
using System.IO;
using System.Threading.Tasks;

namespace DepoWise.Desktop;

/// <summary>
/// Otomatik güncelleme — ORTAK DURUM + akış (2026-07-25, kullanıcı isteği). "Otomatik Güncelleme" AÇIKKEN:
///  • Login sonrası EŞİTLEME ekranında (ana pencere açılmadan) en son paket SESSİZCE indirilir → "Kur / Ertele".
///  • Ertele → uygulama açılır; ShellViewModel zamanlayıcısı 10 dk sonra tekrar sorar (indirilen paket saklanır,
///    tekrar inmez).
///  • Onay vermeden kapatmaya çalışırsa (MainWindow) güncelleme ZORLA kurulur.
///  • Yarım kalan kurulum: sürüm hâlâ eskiyse bir sonraki girişte akış yeniden indirir + kurar; InstallAndRestart
///    staging'i her seferinde sıfırdan açar (backup + rollback) → baştan sağlam kurulum.
/// KAPALIYKEN eski davranış (Dashboard'da manuel "Güncellemeyi Yükle" butonu) geçerlidir; bu servis pasif kalır.
///
/// İndirilen paket (bytes/sürüm/checksum) ve erteleme zamanı burada TEK yerde tutulur → eşitleme ekranı,
/// ShellViewModel zamanlayıcısı ve MainWindow kapatma-kilidi aynı durumu paylaşır (mükerrer indirme olmaz).
/// </summary>
public static class AutoUpdateService
{
    public const string AutoUpdateKey = "auto_update_enabled";
    public const int SnoozeMinutes = 10;

    /// <summary>İndirilmiş, kurulmayı bekleyen paket. Null ise bekleyen güncelleme yok.</summary>
    public static byte[]? PendingBytes { get; private set; }
    public static string? PendingVersion { get; private set; }
    public static string? PendingChecksum { get; private set; }
    /// <summary>Ertelenen güncellemenin tekrar sorulacağı zaman (UTC).</summary>
    public static DateTime SnoozeUntilUtc { get; set; } = DateTime.MinValue;

    public static bool HasPending => PendingBytes is not null && PendingVersion is not null;

    /// <summary>Otomatik güncelleme açık mı (app_settings; varsayılan AÇIK).</summary>
    public static bool IsEnabled(string companyId)
    {
        try { return DesktopServices.Settings.Get(companyId, AutoUpdateKey) != "0"; }
        catch { return true; }
    }

    public static void SetPending(byte[] bytes, string version, string checksum)
    { PendingBytes = bytes; PendingVersion = version; PendingChecksum = checksum; }

    public static void ClearPending()
    { PendingBytes = null; PendingVersion = null; PendingChecksum = null; }

    public static void Snooze() => SnoozeUntilUtc = DateTime.UtcNow.AddMinutes(SnoozeMinutes);

    /// <summary>Güncelleme var mı bakar; varsa indirir ve <see cref="PendingBytes"/>'i doldurur (true döner).
    /// Zaten indirilmiş ve sürüm aynıysa yeniden indirmez. Güncelleme yoksa bekleyeni temizler (false).
    /// Hata olursa mevcut bekleyen durumu korur ve girişi ASLA engellemez (sessiz).</summary>
    public static async Task<bool> CheckAndDownloadAsync(string companyId, Action<string>? status = null, Action<int>? progress = null)
    {
        try
        {
            var url = ResolveServerUrl(companyId);
            if (string.IsNullOrWhiteSpace(url)) return HasPending;
            var latest = await DesktopServices.UpdateApi.GetLatestAsync(url!);
            if (latest is null || string.IsNullOrWhiteSpace(latest.DownloadUrl)) return HasPending;
            var res = DesktopServices.Update.Check(latest);
            if (!res.UpdateAvailable) { ClearPending(); return false; }
            if (HasPending && PendingVersion == latest.Version) return true;   // önceden indirildi (tekrar inme)

            status?.Invoke($"Güncelleme indiriliyor (sürüm {latest.Version})…");
            var bytes = await DesktopServices.UpdateDownload.DownloadAsync(
                latest.DownloadUrl!, p => progress?.Invoke(p));
            SetPending(bytes, latest.Version, latest.ChecksumSha256);
            return true;
        }
        catch { return HasPending; }
    }

    /// <summary>Bekleyen paketi kurar + uygulamayı yeniden başlatır. Bu çağrıdan sonra uygulama KAPANIR.</summary>
    public static void InstallPendingNow()
    {
        if (!HasPending) return;
        UpdateInstaller.InstallAndRestart(PendingBytes!, PendingVersion!, PendingChecksum!);
        Environment.Exit(0);   // uygulama kapanır → harici yardımcı kopyalar + yeniden açar
    }

    /// <summary>Güncelleme sunucusu: DB ayarı yoksa kurulum aracının yazdığı serverurl.txt.</summary>
    private static string? ResolveServerUrl(string companyId)
    {
        try
        {
            var s = DesktopServices.Settings.Get(companyId, DepoWise.Application.Theming.SettingKeys.UpdateServerUrl);
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        catch { }
        try
        {
            var p = Path.Combine(AppContext.BaseDirectory, "serverurl.txt");
            if (File.Exists(p)) { var v = File.ReadAllText(p).Trim(); return string.IsNullOrWhiteSpace(v) ? null : v; }
        }
        catch { }
        return null;
    }
}
