using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Security;
using DepoWise.Desktop.Controls;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Bakım Takibi — periyodik bakım uyarı listesi (MaintenanceService.GetAlerts). Gecikmiş=Danger,
/// yaklaşan=Warning, güncel=Success (metin + badge). Salt okuma; iş kuralları servis katmanında.
/// </summary>
public sealed partial class MaintenanceViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<MaintenanceAlertRow> Items { get; } = new();

    [ObservableProperty] private string? _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;

    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    public MaintenanceViewModel(SessionContext session)
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
            Items.Clear();

            // Araç id → iç kod (görsel ad için)
            var codes = new Dictionary<string, string>();
            try
            {
                foreach (var v in DesktopServices.Vehicles.List(_session))
                    codes[v.Id] = v.InternalCode;
            }
            catch { /* ad çözümleme başarısızsa id gösterilir */ }

            foreach (var a in DesktopServices.Maintenance.GetAlerts(_session)
                         .OrderByDescending(x => (int)x.Level).ThenByDescending(x => x.Progress))
            {
                var code = codes.TryGetValue(a.VehicleId, out var c) ? c : a.VehicleId;
                Items.Add(new MaintenanceAlertRow(code, a.DefinitionName, a.Level, a.Progress, a.Consumed, a.Interval));
            }
            Status = $"{Items.Count} kayıt";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRows));
    }
}

public sealed record MaintenanceAlertRow(string VehicleCode, string Definition, AlertLevel Level,
    double Progress, decimal Consumed, decimal Interval)
{
    public string ProgressText => $"%{Progress * 100:0}";
    public string ConsumedText => $"{Consumed:0.##} / {Interval:0.##}";

    public string LevelText => Level switch
    {
        AlertLevel.Overdue => "Gecikti",
        AlertLevel.Critical => "Kritik",
        AlertLevel.Approaching => "Yaklaşıyor",
        _ => "Güncel",
    };
    public BadgeKind LevelKind => Level switch
    {
        AlertLevel.Overdue => BadgeKind.Danger,
        AlertLevel.Critical => BadgeKind.Warning,
        AlertLevel.Approaching => BadgeKind.Warning,
        _ => BadgeKind.Success,
    };
}
