using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Uyarılar ekranı (ayrı menü, şema notu) — TÜM aktif uyarıları (bakım+muayene+stok+yakıt) gösterir.
/// Ana ekranda "okundu" yapılsa da aktif olduğu sürece burada kalır (read filtresi UYGULANMAZ).
/// </summary>
public sealed partial class AlertsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<DashboardAlert> Alerts { get; } = new();
    public bool HasAlerts => Alerts.Count > 0;

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
            Alerts.Clear();
            foreach (var a in DesktopServices.Dashboard.GetSummary(_session).Alerts) Alerts.Add(a);
        }
        catch (Exception ex) { LoadError = "Uyarılar yüklenemedi: " + ex.Message; }
        IsLoading = false;
        OnPropertyChanged(nameof(HasAlerts));
    }

    [RelayCommand]
    private void OpenAlert(DashboardAlert? alert)
    {
        if (alert is null) return;
        ShellViewModel.Current?.NavigateTo(alert.NavigateKey, alert.EntityId);
    }
}
