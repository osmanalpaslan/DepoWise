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

    // ═══ İNDİRİLEN PAKET DİSKTE SAKLANIR (kullanıcı bildirimi 2026-09-07) ═══════════════════════
    //
    // KULLANICI: "her login oluşumda 'Güncelleme indiriliyor (sürüm X)…' karşıma çıkıyor."
    //
    // SEBEBİ: paket yalnız BELLEKTE tutuluyordu. Kullanıcı "Ertele" derse ya da uygulamayı
    // kapatırsa paket kayboluyor ve BİR SONRAKİ GİRİŞTE 86 MB yeniden iniyordu. Üstelik bu
    // indirme ana pencere açılmadan ÖNCE yapıldığı için kullanıcı her seferinde bekliyordu.
    //
    // Artık paket diske yazılır ve sonraki girişte checksum'ı doğrulanarak yeniden kullanılır.
    // Doğrulama şart: bozuk/yarım dosya kuruluma girmemeli — eşleşmezse dosya silinir ve
    // normal indirme yapılır. Kurulum tamamlanınca dosya zaten silinir.
    private static string OnbellekYolu(string version)
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Alpnex", "staging", version + ".pkg");

    /// <summary>Diskteki paketi doğrulayarak okur; yoksa/bozuksa null döner (ve bozuğu siler).</summary>
    private static byte[]? OnbellektenOku(string version, string? checksum)
    {
        try
        {
            var yol = OnbellekYolu(version);
            if (string.IsNullOrWhiteSpace(checksum) || !File.Exists(yol)) return null;
            var bytes = File.ReadAllBytes(yol);
            if (DepoWise.Infrastructure.Update.UpdateService.VerifyChecksum(bytes, checksum!)) return bytes;
            try { File.Delete(yol); } catch { }   // bozuk dosya saklanmaz
        }
        catch { }
        return null;
    }

    private static void OnbellegeYaz(string version, byte[] bytes)
    {
        try
        {
            var yol = OnbellekYolu(version);
            Directory.CreateDirectory(Path.GetDirectoryName(yol)!);
            // Eski sürümlerin paketleri disk doldurmasın.
            foreach (var eski in Directory.GetFiles(Path.GetDirectoryName(yol)!, "*.pkg"))
                if (!string.Equals(eski, yol, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(eski); } catch { }
            File.WriteAllBytes(yol, bytes);
        }
        catch { /* önbellek yazılamazsa akış bozulmaz: sadece bir dahaki sefere yeniden iner */ }
    }

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
            if (HasPending && PendingVersion == latest.Version) return true;   // bu oturumda indirilmişti

            // Önceki oturumda inmiş ve DİSKTE duruyorsa yeniden indirme (checksum doğrulanır).
            if (OnbellektenOku(latest.Version, latest.ChecksumSha256) is { } saklanan)
            {
                SetPending(saklanan, latest.Version, latest.ChecksumSha256);
                return true;
            }

            status?.Invoke($"Güncelleme indiriliyor (sürüm {latest.Version})…");
            var bytes = await DesktopServices.UpdateDownload.DownloadAsync(
                latest.DownloadUrl!, p => progress?.Invoke(p));
            SetPending(bytes, latest.Version, latest.ChecksumSha256);
            OnbellegeYaz(latest.Version, bytes);
            return true;
        }
        catch { return HasPending; }
    }

    /// <summary>Kurulum denemesi başarısızsa sebebi (arayüzde gösterilir). Başarıda null.</summary>
    public static string? SonKurulumHatasi { get; private set; }

    /// <summary>
    /// Bekleyen paketi kurar + uygulamayı yeniden başlatır. Başarıda bu çağrıdan DÖNÜLMEZ (uygulama kapanır).
    /// <b>false</b> dönerse kurulum yapılamadı ve uygulama çalışmaya devam etmelidir.
    ///
    /// <para>⭐ 2026-09-07: eskiden hata FIRLATIYORDU. Çağıranların ikisi <c>async void</c> olduğu için
    /// (pencere kapanışı ve giriş akışı) fırlayan hata YAKALANAMIYOR ve uygulama sessizce ölüyordu —
    /// kullanıcı "güncelledim, uygulama hiç açılmıyor" durumuyla kalıyordu. Artık hata yutulmaz ama
    /// FIRLATILMAZ da: sebebi <see cref="SonKurulumHatasi"/>'na yazılır, çağıran kullanıcıya söyler
    /// ve uygulama normal şekilde açılmaya devam eder.</para>
    /// </summary>
    public static bool InstallPendingNow()
    {
        SonKurulumHatasi = null;
        if (!HasPending) return false;
        try
        {
            UpdateInstaller.InstallAndRestart(PendingBytes!, PendingVersion!, PendingChecksum!);
        }
        catch (Exception ex)
        {
            SonKurulumHatasi = ex.Message;
            // Bozuk/doğrulanamayan paketi elde tutma: aynı oturumda tekrar tekrar denenip
            // kullanıcıyı kilitlemesin. Bir sonraki girişte yeniden indirilir.
            ClearPending();
            return false;
        }
        Environment.Exit(0);   // uygulama kapanır → harici yardımcı kopyalar + yeniden açar
        return true;
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
