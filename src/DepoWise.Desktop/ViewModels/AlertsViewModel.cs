using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Uyarılar ekranı — TÜM aktif uyarılar (bakım + muayene/sigorta + düşük stok + yakıt). Uyarılar 4 KATEGORİ
/// altında listelenir; her kategori bir BUTON ve içinde uyarı sayısını gösterir (kullanıcı isteği 2026-07-25).
/// Bir kategoriye tıklanınca yalnız o kategorinin uyarıları listelenir; "Tümü" hepsini gösterir.
/// Ana ekranda "okundu" yapılsa da aktif olduğu sürece burada kalır (read filtresi UYGULANMAZ).
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
    public int ToplamCount => _all.Count;

    /// <summary>Etkin filtre: "material" | "maintenance" | "inspection" | "fuel" | null (= Tümü).</summary>
    [ObservableProperty] private string? _filter;

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string? _loadError;

    public AlertsViewModel(SessionContext session)
    {
        _session = session;
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            _all.Clear();
            foreach (var a in DesktopServices.Dashboard.GetSummary(_session).Alerts) _all.Add(a);
        }
        catch (Exception ex) { LoadError = "Uyarılar yüklenemedi: " + ex.Message; }
        IsLoading = false;
        NotifyCounts();
        ApplyFilter();
    }

    /// <summary>Kategori butonuna tıkla → yalnız o kategoriyi göster (aynı kategoriye tekrar tıklanırsa Tümü).</summary>
    [RelayCommand]
    private void SelectCategory(string? kind)
    {
        Filter = (Filter == kind) ? null : kind;   // toggle → Tümü
    }

    partial void OnFilterChanged(string? value) => ApplyFilter();

    private void ApplyFilter()
    {
        Alerts.Clear();
        // Kategori seçili DEĞİLSE hiçbir uyarı gösterilmez (yalnız butonlar). Seçiliyse yalnız o kategori.
        if (Filter is not null)
        {
            var kind = Filter switch
            {
                "material" => AlertKind.LowStock,
                "maintenance" => AlertKind.Maintenance,
                "inspection" => AlertKind.Inspection,
                "fuel" => AlertKind.Fuel,
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
        foreach (var n in new[] { nameof(MalzemeCount), nameof(BakimCount), nameof(MuayeneCount), nameof(YakitCount), nameof(ToplamCount) })
            OnPropertyChanged(n);
    }

    [RelayCommand]
    private void OpenAlert(DashboardAlert? alert)
    {
        if (alert is null) return;
        ShellViewModel.Current?.NavigateTo(alert.NavigateKey, alert.EntityId);
    }
}
