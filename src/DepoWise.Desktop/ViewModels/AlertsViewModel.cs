using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Uyarılar ekranı — TÜM aktif uyarılar (bakım + muayene/sigorta + düşük stok + yakıt
/// + BLD-01: evrak geçerlilik + geciken iş emri + bekleyen talep). Uyarılar KATEGORİ butonları
/// altında listelenir; bir kategoriye tıklanınca yalnız o kategori, "Tümü" hepsini gösterir.
/// Ana ekranda/çanda "okundu" yapılsa da aktif olduğu sürece burada kalır (okundu SOLUK görünür).
/// BLD-01: okundu işaretleri CİHAZ-YERELDİR (PK-I4); evrak bildirimleri sunucu-otoritelidir —
/// çevrimdışıyken üretilmez (nota düşülür). Bildirimler TÜRETİLMİŞTİR: fiziksel kayıt yok.
/// </summary>
public sealed partial class AlertsViewModel : ViewModelBase
{
    private readonly SessionContext _session;
    private readonly List<DashboardAlert> _all = new();

    public ObservableCollection<DashboardAlert> Alerts { get; } = new();
    public bool HasAlerts => Alerts.Count > 0;
    // Görünürlük: ilk açılışta (kategori seçilmeden) hiçbir uyarı listelenmez — yalnız butonlar+sayılar (kullanıcı isteği 2026-07-26).
    public bool CategorySelected => Filter is not null;
    public bool ShowSelectPrompt => Filter is null;                    // "Bir kategori seçin" ipucu
    public bool ShowEmptyCategory => Filter is not null && Alerts.Count == 0; // seçili kategori boş

    // Kategori sayıları (buton etiketlerinde gösterilir).
    public int MalzemeCount => _all.Count(a => a.Kind == AlertKind.LowStock);
    public int BakimCount => _all.Count(a => a.Kind == AlertKind.Maintenance);
    public int MuayeneCount => _all.Count(a => a.Kind == AlertKind.Inspection);
    public int YakitCount => _all.Count(a => a.Kind == AlertKind.Fuel);
    public int EvrakCount => _all.Count(a => a.Kind == AlertKind.Document);       // BLD-01
    public int IsEmriCount => _all.Count(a => a.Kind == AlertKind.WorkOrder);     // BLD-01
    public int TalepCount => _all.Count(a => a.Kind == AlertKind.Request);        // BLD-01
    public int DuyuruCount => _all.Count(a => a.Kind == AlertKind.Announcement);  // DYR-01
    public int ToplamCount => _all.Count;
    public bool HasUnread => _all.Any(a => !a.Read);                              // BLD-01: "Tümünü Okundu Yap" görünürlüğü

    /// <summary>Etkin filtre: "material"|"maintenance"|"inspection"|"fuel"|"document"|"work_order"|"request"
    /// | "all" (= Tümü, BLD-01) | null (= henüz seçilmedi).</summary>
    [ObservableProperty] private string? _filter;

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string? _loadError;

    /// <summary>BLD-01: evrak bildirimleri sunucu-otoriteli — çevrimdışıyken gösterilen not (çevrimiçiyse null).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRemoteNote))]
    private string? _remoteNote;
    public bool HasRemoteNote => RemoteNote != null;

    public AlertsViewModel(SessionContext session)
    {
        _session = session;
        _ = Load();
    }

    [RelayCommand]
    private async Task Load()
    {
        try
        {
            LoadError = null;
            _all.Clear();
            var (all, remoteOffline) = await AlertFeed.GetAsync(_session);
            _all.AddRange(all);
            RemoteNote = remoteOffline
                ? "Evrak geçerlilik bildirimleri çevrimiçi bağlantı gerektirir — şu an gösterilemiyor." : null;
        }
        catch (Exception ex) { LoadError = "Uyarılar yüklenemedi: " + ex.Message; }
        IsLoading = false;
        NotifyCounts();
        ApplyFilter();
        ShellViewModel.Current?.RefreshAlertBadge();   // çan sayacı bu ekranla senkron kalsın
    }

    /// <summary>Kategori butonuna tıkla → yalnız o kategoriyi göster (aynı kategoriye tekrar tıklanırsa seçim kalkar).</summary>
    [RelayCommand]
    private void SelectCategory(string? kind)
    {
        Filter = (Filter == kind) ? null : kind;
    }

    partial void OnFilterChanged(string? value) => ApplyFilter();

    private void ApplyFilter()
    {
        Alerts.Clear();
        // Kategori seçili DEĞİLSE hiçbir uyarı gösterilmez (yalnız butonlar). "all" → Tümü (BLD-01).
        if (Filter == "all")
        {
            foreach (var a in _all) Alerts.Add(a);
        }
        else if (Filter is not null)
        {
            var kind = Filter switch
            {
                "material" => AlertKind.LowStock,
                "maintenance" => AlertKind.Maintenance,
                "inspection" => AlertKind.Inspection,
                "fuel" => AlertKind.Fuel,
                "document" => AlertKind.Document,     // BLD-01
                "work_order" => AlertKind.WorkOrder,  // BLD-01
                "request" => AlertKind.Request,       // BLD-01
                "announcement" => AlertKind.Announcement,   // DYR-01
                _ => (AlertKind?)null,
            };
            if (kind is { } k)
                foreach (var a in _all.Where(a => a.Kind == k)) Alerts.Add(a);
        }
        OnPropertyChanged(nameof(HasAlerts));
        OnPropertyChanged(nameof(CategorySelected));
        OnPropertyChanged(nameof(ShowSelectPrompt));
        OnPropertyChanged(nameof(ShowEmptyCategory));
    }

    private void NotifyCounts()
    {
        foreach (var n in new[] { nameof(MalzemeCount), nameof(BakimCount), nameof(MuayeneCount), nameof(YakitCount),
                     nameof(EvrakCount), nameof(IsEmriCount), nameof(TalepCount), nameof(DuyuruCount), nameof(ToplamCount), nameof(HasUnread) })
            OnPropertyChanged(n);
    }

    [RelayCommand]
    private void OpenAlert(DashboardAlert? alert)
    {
        if (alert is null) return;
        ShellViewModel.Current?.NavigateTo(alert.NavigateKey, alert.EntityId);
    }

    // ═══ BLD-01 (ADR-172) — okundu işaretleme (cihaz-yerel upsert; kopya satır üretmez) ═══

    [RelayCommand]
    private async Task MarkRead(DashboardAlert? alert)
    {
        if (alert is null || alert.Read) return;
        try { DesktopServices.Dashboard.MarkAlertRead(_session, alert.Key, alert.Signature); } catch { }
        await Load();
    }

    /// <summary>Tümünü okundu yap — yerel kaynaklar + (çevrimiçiyse) evrak bildirimleri; yalnız BU cihazda.</summary>
    [RelayCommand]
    private async Task MarkAllRead()
    {
        try
        {
            DesktopServices.Dashboard.MarkAllAlertsRead(_session,
                _all.Where(a => a.Kind == AlertKind.Document));
        }
        catch { }
        await Load();
    }
}
