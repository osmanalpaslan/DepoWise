using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Security;
using DepoWise.Desktop.Controls;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Araçlar — liste + arama + durum/bakım-muayene uyarı badge'i + yeni araç. VehicleService üzerine.</summary>
public sealed partial class VehiclesViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<VehicleRow> Items { get; } = new();
    public ObservableCollection<string> StatusOptions { get; } = new() { "active", "passive", "maintenance" };
    public ObservableCollection<string> MeterUnits { get; } = new() { "km", "hour" };

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string? _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;

    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    [ObservableProperty] private bool _showAdd;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CodeError))]
    [NotifyPropertyChangedFor(nameof(HasCodeError))]
    private string _newCode = "";

    [ObservableProperty] private string _newPlate = "";
    [ObservableProperty] private int _newYear;
    [ObservableProperty] private string _newStatus = "active";
    [ObservableProperty] private decimal _newMeter;
    [ObservableProperty] private string _newMeterUnit = "km";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CodeError))]
    [NotifyPropertyChangedFor(nameof(HasCodeError))]
    private bool _triedSave;

    public string? CodeError => TriedSave && string.IsNullOrWhiteSpace(NewCode) ? "İç kod zorunlu." : null;
    public bool HasCodeError => CodeError != null;

    public bool CanWrite => AccessControl.Can(_session, "vehicles", PermissionAction.Create);
    public string? AddButtonText => CanWrite ? "Yeni Araç" : null;

    public VehiclesViewModel(SessionContext session)
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

            // Uyarı haritaları (bakım + muayene) — araç başına en kötü seviye
            var maint = SafeMaint();
            var insp = SafeInsp();

            foreach (var v in DesktopServices.Vehicles.List(_session, string.IsNullOrWhiteSpace(Search) ? null : Search.Trim()))
            {
                var (kind, text) = CombineAlert(
                    maint.TryGetValue(v.Id, out var ml) ? ml : (AlertLevel?)null,
                    insp.TryGetValue(v.Id, out var il) ? il : (DateAlertLevel?)null);
                Items.Add(new VehicleRow(v.Id, v.InternalCode, v.Plate, v.Status, v.CurrentMeter, v.MeterUnit, v.ProductionYear, kind, text));
            }
            Status = $"{Items.Count} araç";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRows));
    }

    private Dictionary<string, AlertLevel> SafeMaint()
    {
        try
        {
            return DesktopServices.Maintenance.GetAlerts(_session)
                .GroupBy(a => a.VehicleId)
                .ToDictionary(g => g.Key, g => (AlertLevel)g.Max(x => (int)x.Level));
        }
        catch { return new(); }
    }

    private Dictionary<string, DateAlertLevel> SafeInsp()
    {
        try
        {
            return DesktopServices.Inspection.GetAlerts(_session)
                .GroupBy(a => a.VehicleId)
                .ToDictionary(g => g.Key, g => (DateAlertLevel)g.Max(x => (int)x.Level));
        }
        catch { return new(); }
    }

    private static (BadgeKind, string) CombineAlert(AlertLevel? m, DateAlertLevel? i)
    {
        bool overdue = m == AlertLevel.Overdue || i == DateAlertLevel.Expired;
        bool soon = m is AlertLevel.Critical or AlertLevel.Approaching || i == DateAlertLevel.Approaching;
        if (overdue) return (BadgeKind.Danger, "Gecikti");
        if (soon) return (BadgeKind.Warning, "Yaklaşıyor");
        return (BadgeKind.Success, "Güncel");
    }

    [RelayCommand]
    private void Add()
    {
        TriedSave = true;
        if (!CanWrite) { Status = "Yetki yok."; return; }
        if (string.IsNullOrWhiteSpace(NewCode)) { Status = "İç kod zorunlu."; return; }
        try
        {
            DesktopServices.Vehicles.Create(_session, new NewVehicle(
                InternalCode: NewCode.Trim(),
                Plate: string.IsNullOrWhiteSpace(NewPlate) ? null : NewPlate.Trim(),
                ProductionYear: NewYear > 0 ? NewYear : (int?)null,
                CurrentMeter: NewMeter, MeterUnit: NewMeterUnit, Status: NewStatus));
            Clear();
            Load();
            Status = "Araç eklendi.";
        }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }

    [RelayCommand]
    private void ToggleAdd()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        ShowAdd = !ShowAdd;
    }

    [RelayCommand]
    private void Clear()
    {
        NewCode = ""; NewPlate = ""; NewYear = 0; NewStatus = "active"; NewMeter = 0; NewMeterUnit = "km";
        TriedSave = false; ShowAdd = false;
    }
}

public sealed record VehicleRow(string Id, string Code, string? Plate, string Status, decimal Meter, string MeterUnit,
    int? Year, BadgeKind AlertKind, string AlertText)
{
    public string PlateDisplay => string.IsNullOrWhiteSpace(Plate) ? "—" : Plate!;
    public string MeterDisplay => $"{Meter:0.##} {MeterUnit}";
    public string YearDisplay => Year is > 0 ? Year!.Value.ToString() : "—";

    public string StatusText => Status switch
    {
        "active" => "Aktif",
        "passive" => "Pasif",
        "maintenance" => "Bakımda",
        _ => Status,
    };
    public BadgeKind StatusKind => Status switch
    {
        "active" => BadgeKind.Success,
        "maintenance" => BadgeKind.Warning,
        "passive" => BadgeKind.Neutral,
        _ => BadgeKind.Neutral,
    };
}
