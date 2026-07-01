using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;

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

    public DashboardViewModel(SessionContext session)
    {
        try { CurrentVersion = DesktopServices.Update.CurrentVersion(); } catch { }
        try
        {
            var s = DesktopServices.Dashboard.GetSummary(session);
            // Yalnız ilk kart vurgulu (Primary=true); diğerleri nötr koyu yüzey.
            Cards.Add(new KpiCard(s.VehicleCount.ToString(), "Toplam Araç", "accent", Primary: true, NavKey: "vehicles"));
            Cards.Add(new KpiCard(s.MaterialCount.ToString(), "Malzeme Çeşidi", "neutral", Primary: false, NavKey: "materials"));
            Cards.Add(new KpiCard(s.LowStockCount.ToString(), "Düşük Stok", "warning", Primary: false, NavKey: "materials"));
            Cards.Add(new KpiCard(s.PendingRequestCount.ToString(), "Bekleyen Talep", "neutral", Primary: false, NavKey: "requests:approve"));
            Cards.Add(new KpiCard(s.PersonnelCount.ToString(), "Aktif Personel", "success", Primary: false, NavKey: null));
            foreach (var a in s.Alerts) Alerts.Add(a);
        }
        catch (Exception ex)
        {
            LoadError = "Özet verileri yüklenemedi: " + ex.Message;
        }
        IsLoading = false;
        CheckUpdate(); // açılışta güncelleme var mı otomatik kontrol → uyarı + buton
    }

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

    /// <summary>Güncelleme kontrolü: yayınlanan en son sürümü mevcutla karşılaştırır (Güncelleme sunucusundan sync edilen app_releases).</summary>
    [RelayCommand]
    private void CheckUpdate()
    {
        try
        {
            var latest = DesktopServices.Releases.Latest();
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
            var bytes = await DesktopServices.UpdateDownload.DownloadAsync(
                pkg.DownloadUrl!, p => UpdateProgress = p * 60 / 100);        // indirme: 0–60
            UpdateMessage = "Kuruluyor…";
            DesktopServices.Update.ApplyUpdate(pkg, bytes,
                p => UpdateProgress = 60 + p * 40 / 100);                     // kurulum: 60–100
            CurrentVersion = DesktopServices.Update.CurrentVersion();
            UpdateAvailable = false;
            UpdateProgress = 100;
            UpdateMessage = $"Güncelleme kuruldu (sürüm {CurrentVersion}). Lütfen uygulamayı yeniden başlatın.";
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

/// <summary>Kind: accent|neutral|warning|success|danger (durum tonu). Primary: tek vurgulu kart. NavKey: tıklayınca gidilecek ekran.</summary>
public sealed record KpiCard(string Value, string Label, string Kind, bool Primary, string? NavKey = null);
