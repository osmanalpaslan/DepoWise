using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Application.Theming;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Genel Özet — KPI kartları + kritik uyarılar (DashboardService). İş verisi/sayımlar değişmez;
/// yalnız sunum. Yalnız ilk (en önemli) kart vurgulu (Primary), diğerleri nötr yüzey.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    public ObservableCollection<KpiCard> Cards { get; } = new();
    public ObservableCollection<DashboardAlert> Alerts { get; } = new();

    public bool HasAlerts => Alerts.Count > 0;

    /// <summary>Yükleme/hata/boş durumları (minimum durum modeli; iş mantığı değiştirilmedi).</summary>
    public bool IsLoading { get; }
    public string? LoadError { get; }
    public bool HasError => LoadError is not null;
    public bool IsLoaded => !IsLoading && !HasError;

    // ── Güncelleme (Ana Ekran "Güncelle" + % ilerleme) ──
    [ObservableProperty] private string _currentVersion = "—";
    [ObservableProperty] private string? _updateMessage;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyUpdate))]
    private bool _updateAvailable;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyUpdate))]
    private bool _isUpdating;
    [ObservableProperty] private int _updateProgress;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateError))]
    private string? _updateErrorDetail;
    public bool HasUpdateError => !string.IsNullOrEmpty(UpdateErrorDetail);
    public bool CanApplyUpdate => UpdateAvailable && !IsUpdating;
    private DepoWise.Application.Update.UpdatePackage? _latestPackage;

    /// <summary>Bu makinenin adı — ana ekranda gösterilir.</summary>
    public string MachineName => Environment.MachineName;

    /// <summary>Giriş yapılan şube (login'de seçilen) — ana ekranda gösterilir.</summary>
    public string BranchName => string.IsNullOrEmpty(DesktopServices.CurrentBranchName) ? "Tüm / Belirsiz" : DesktopServices.CurrentBranchName!;
    public bool HasBranch => !string.IsNullOrEmpty(DesktopServices.CurrentBranchName);

    /// <summary>Otomatik güncelleme açık/kapalı (app_settings). Kapalıysa ShellViewModel 10 dk'lık oto-uyarıyı atlar.</summary>
    public const string AutoUpdateKey = "auto_update_enabled";
    [ObservableProperty] private bool _autoUpdateEnabled = true;
    partial void OnAutoUpdateEnabledChanged(bool value)
    {
        try { DesktopServices.Settings.Set(_companyId, AutoUpdateKey, value ? "1" : "0"); } catch { }
    }

    private readonly string _companyId;
    private readonly SessionContext _session;

    public DashboardViewModel(SessionContext session)
    {
        _companyId = session.CompanyId;
        _session = session;
        try { CurrentVersion = DesktopServices.Update.CurrentVersion(); } catch { }
        try { _autoUpdateEnabled = DesktopServices.Settings.Get(_companyId, AutoUpdateKey) != "0"; } catch { }
        try
        {
            var s = DesktopServices.Dashboard.GetSummary(session);
            // Yalnız ilk kart vurgulu (Primary=true); diğerleri nötr koyu yüzey.
            Cards.Add(new KpiCard(s.VehicleCount.ToString(), "Toplam Araç", "accent", Primary: true, NavKey: "vehicles"));
            Cards.Add(new KpiCard(s.MaterialCount.ToString(), "Malzeme Çeşidi", "neutral", Primary: false, NavKey: "materials"));
            Cards.Add(new KpiCard(s.LowStockCount.ToString(), "Düşük Stok", "warning", Primary: false, NavKey: "materials"));
            Cards.Add(new KpiCard(s.PendingRequestCount.ToString(), "Bekleyen Talep", "neutral", Primary: false, NavKey: "requests:approve"));
            Cards.Add(new KpiCard(s.PersonnelCount.ToString(), "Aktif Personel", "success", Primary: false, NavKey: null));
            foreach (var a in s.Alerts) if (!a.Read) Alerts.Add(a); // #18: okunmuşları ana ekranda gösterme
        }
        catch (Exception ex)
        {
            LoadError = "Özet verileri yüklenemedi: " + ex.Message;
        }
        IsLoading = false;
        _ = CheckUpdate(); // açılışta güncelleme var mı otomatik kontrol → uyarı + buton

        // Periyodik otomatik kontrol: sunucuya yeni paket yüklenince (uygulama açıkken) uyarı KENDİLİĞİNDEN çıkar.
        _updateTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _updateTimer.Tick += (_, _) => { if (!IsUpdating) _ = CheckUpdate(); };
        _updateTimer.Start();
    }

    private readonly Avalonia.Threading.DispatcherTimer _updateTimer;

    /// <summary>KPI kartına tıklayınca ilgili ekrana git (köprü). NavKey boşsa hedef ekran henüz yok → işlem yok.</summary>
    [RelayCommand]
    private void Open(string? navKey)
    {
        if (string.IsNullOrEmpty(navKey)) return;
        ShellViewModel.Current?.NavigateCommand.Execute(navKey);
    }

    /// <summary>Uyarıya tıkla → ilgili ekran + ilgili kaydın detayı/işlemi otomatik açılır.</summary>
    [RelayCommand]
    private void OpenAlert(DashboardAlert? alert)
    {
        if (alert is null) return;
        ShellViewModel.Current?.NavigateTo(alert.NavigateKey, alert.EntityId);
    }

    /// <summary>#18 — Uyarıyı okundu işaretle → ana ekrandan kaldır (ilgili modül ekranında kalır).</summary>
    [RelayCommand]
    private void MarkAlertRead(DashboardAlert? alert)
    {
        if (alert is null) return;
        try { DesktopServices.Dashboard.MarkAlertRead(_session, alert.Key, alert.Signature); } catch { }
        Alerts.Remove(alert);
        OnPropertyChanged(nameof(HasAlerts));
    }

    /// <summary>Kurulum aracının uygulama klasörüne yazdığı serverurl.txt (varsa). Bağlantı ayarı otomatik gelsin diye.</summary>
    private static string? ReadInstalledServerUrl()
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "serverurl.txt");
            if (System.IO.File.Exists(path))
            {
                var v = System.IO.File.ReadAllText(path).Trim();
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Güncelleme kontrolü: sunucu (API) tanımlıysa `/api/releases/latest`'ten, değilse yerel app_releases'ten
    /// en son sürümü alıp mevcutla karşılaştırır.</summary>
    [RelayCommand]
    private async Task CheckUpdate()
    {
        try
        {
            // Sunucu adresi: DB ayarı YOKSA kurulum aracının yazdığı serverurl.txt'ten okunur (elle ayar gerekmez).
            var serverUrl = DesktopServices.Settings.Get(_companyId, SettingKeys.UpdateServerUrl) ?? ReadInstalledServerUrl();
            var latest = !string.IsNullOrWhiteSpace(serverUrl)
                ? await DesktopServices.UpdateApi.GetLatestAsync(serverUrl!) ?? DesktopServices.Releases.Latest()
                : DesktopServices.Releases.Latest();
            _latestPackage = latest;
            var res = DesktopServices.Update.Check(latest);
            UpdateAvailable = res.UpdateAvailable;
            UpdateMessage = res.UpdateAvailable
                ? $"Yeni sürüm mevcut: {res.LatestVersion} (mevcut {res.CurrentVersion})"
                  + (res.SignedWarning ? " — UYARI: paket imzasız." : "")
                : $"Uygulama güncel (sürüm {res.CurrentVersion}).";
        }
        catch (Exception ex) { UpdateMessage = "Güncelleme kontrolü başarısız: " + ex.Message; }
    }

    /// <summary>Güncellemeyi indir + kur (yüzde ana ekranda). DB'ye DOKUNULMAZ (yalnız uygulama dizini);
    /// bozuk/checksum'suz paket kurulmaz, hata olursa eski sürüme rollback.</summary>
    [RelayCommand]
    private async Task InstallUpdate()
    {
        if (_latestPackage is null || !UpdateAvailable) { UpdateMessage = "Önce güncelleme kontrol edin."; return; }
        if (string.IsNullOrWhiteSpace(_latestPackage.DownloadUrl))
        { UpdateMessage = "Paket indirme adresi tanımlı değil (sunucuya yüklenmemiş)."; return; }
        if (!await ConfirmService.AskAsync(
                $"Sürüm {_latestPackage.Version} indirilip kurulsun mu?\nVeritabanınıza dokunulmaz; hata olursa eski sürüme dönülür.",
                "Güncellemeyi Yükle")) return;

        IsUpdating = true; UpdateProgress = 0; UpdateErrorDetail = null;
        try
        {
            var pkg = _latestPackage;
            UpdateMessage = "İndiriliyor…";
            int dlPct = 0;
            var bytes = await DesktopServices.UpdateDownload.DownloadAsync(
                pkg.DownloadUrl!,
                p => { dlPct = p; UpdateProgress = p * 60 / 100; },              // indirme: 0–60
                speedBytesPerSec: bps => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    UpdateMessage = bps > 0 ? $"İndiriliyor… %{dlPct} • {FormatSpeed(bps)}" : "Kuruluyor…"));
            UpdateMessage = "Kuruluyor…";
            UpdateProgress = 100;
            // Yeniden başlatma öncesi bilgilendirme — Tamam'a basınca kapanıp yeniden açılır.
            await ConfirmService.AskAsync(
                "Güncelleme indirildi. Uygulamanız yeniden başlatılacaktır, lütfen bekleyiniz…",
                "Yeniden Başlatılıyor", "Tamam", "Tamam");
            UpdateMessage = "Yeniden başlatılıyor…";
            // GERÇEK kurulum: dosyaları kurulum dizinine kopyalar + sürümü yazar + uygulamayı yeniden açar.
            DepoWise.Desktop.UpdateInstaller.InstallAndRestart(bytes, pkg.Version, pkg.ChecksumSha256);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            UpdateMessage = "Güncelleme başarısız: " + ex.Message;
            // Hatanın ne olduğu + detayları (iç hata + güncelleme log kuyruğu) ekranda gösterilir.
            var detail = ex.InnerException is { } inner ? $"{ex.Message}\nAyrıntı: {inner.Message}" : ex.Message;
            string log = "";
            try { log = DesktopServices.Update.ReadLogTail(); } catch { }
            UpdateErrorDetail = string.IsNullOrWhiteSpace(log) ? detail : $"{detail}\n\n— Güncelleme günlüğü —\n{log}";
        }
        finally { IsUpdating = false; }
    }
}

// (yardımcı) İndirme hızını okunur biçime çevirir.
public sealed partial class DashboardViewModel
{
    private static string FormatSpeed(double bytesPerSec)
        => bytesPerSec >= 1024 * 1024
            ? $"{bytesPerSec / 1024 / 1024:0.0} MB/sn"
            : $"{bytesPerSec / 1024:0} KB/sn";
}

/// <summary>Kind: accent|neutral|warning|success|danger (durum tonu). Primary: tek vurgulu kart. NavKey: tıklayınca gidilecek ekran.</summary>
public sealed record KpiCard(string Value, string Label, string Kind, bool Primary, string? NavKey = null);
