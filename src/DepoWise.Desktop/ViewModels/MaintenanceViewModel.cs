using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Security;
using DepoWise.Desktop.Controls;
using DepoWise.Infrastructure.Maintenance;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Bakım Takibi — sekmeli: (1) Bakım Tanımları (tanım CRUD + ilişkili araç + alt bakım), (2) Uyarılar (GetAlerts).
/// "Araç Bakımları" sekmesi sonraki fazda. İş kuralları servis katmanında.
/// </summary>
public sealed partial class MaintenanceViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    [ObservableProperty] private string? _status;

    public MaintenanceViewModel(SessionContext session)
    {
        _session = session;
        LoadDefs();
        LoadAlerts();
    }

    public bool CanWrite => AccessControl.Can(_session, "maintenance", PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, "maintenance", PermissionAction.Edit);
    public bool CanDelete => AccessControl.Can(_session, "maintenance", PermissionAction.Delete);

    // ════════════════════ TAB 1 — BAKIM TANIMLARI ════════════════════
    public ObservableCollection<MaintenanceDefinitionRow> Defs { get; } = new();
    public ObservableCollection<MaintenanceDefinitionRow> SubDefs { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDefSelection))]
    private MaintenanceDefinitionRow? _selectedDef;
    public bool HasDefSelection => SelectedDef != null;

    [ObservableProperty] private string? _defsError;
    public bool HasDefsError => DefsError != null;
    public bool DefsEmpty => !HasDefsError && Defs.Count == 0;
    public bool HasDefs => Defs.Count > 0;

    // Yeni/düzenle tanım formu
    [ObservableProperty] private bool _showDefAdd;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefIsEditMode))]
    [NotifyPropertyChangedFor(nameof(DefFormTitle))]
    private string? _defEditId;
    public bool DefIsEditMode => DefEditId != null;
    public string DefFormTitle => DefIsEditMode ? "BAKIM TANIMI DÜZENLE" : "YENİ BAKIM TANIMI";
    public string? AddDefButtonText => CanWrite ? "Yeni Tanım" : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefNameError))]
    [NotifyPropertyChangedFor(nameof(HasDefNameError))]
    private string _defName = "";
    [ObservableProperty] private string _defDescription = "";
    [ObservableProperty] private decimal _defIntervalValue;
    [ObservableProperty] private string _defUnitDisplay = "km";
    public ObservableCollection<string> UnitOptions { get; } = new() { "km", "saat", "gün" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefNameError))]
    [NotifyPropertyChangedFor(nameof(HasDefNameError))]
    private bool _triedDefSave;
    public string? DefNameError => TriedDefSave && string.IsNullOrWhiteSpace(DefName) ? "Tanım adı zorunlu." : null;
    public bool HasDefNameError => DefNameError != null;

    // İlişkili araçlar (periyodik takip)
    public ObservableCollection<VehiclePick> VehiclePicks { get; } = new();
    public ObservableCollection<VehiclePick> FilteredVehicles { get; } = new();
    [ObservableProperty] private string _vehicleSearch = "";
    private bool _vehiclesLoaded;

    partial void OnVehicleSearchChanged(string value) => RebuildFilteredVehicles();

    // Alt bakım ekleme
    [ObservableProperty] private string _newSubDefName = "";

    private static string UnitCode(string display) => display switch { "saat" => "hour", "gün" => "day", _ => "km" };
    private static string UnitDisplay(string code) => code switch { "hour" => "saat", "day" => "gün", _ => "km" };

    [RelayCommand]
    private void LoadDefs()
    {
        try
        {
            DefsError = null;
            Defs.Clear();
            foreach (var d in DesktopServices.MaintenanceDefs.List(_session)) Defs.Add(d);
            Status = $"{Defs.Count} bakım tanımı";
        }
        catch (Exception ex) { DefsError = ex.Message; Status = "Hata: " + ex.Message; }
        SelectedDef = null;
        OnPropertyChanged(nameof(DefsEmpty));
        OnPropertyChanged(nameof(HasDefs));
        OnPropertyChanged(nameof(HasDefsError));
    }

    partial void OnSelectedDefChanged(MaintenanceDefinitionRow? value)
    {
        SubDefs.Clear();
        if (value is null) return;
        try { foreach (var sub in DesktopServices.MaintenanceDefs.List(_session, value.Id)) SubDefs.Add(sub); }
        catch { }
    }

    [RelayCommand]
    private void ToggleDefAdd()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        ShowDefAdd = !ShowDefAdd;
        if (ShowDefAdd) LoadVehiclePicks();
    }

    [RelayCommand]
    private void ClearDef()
    {
        DefName = ""; DefDescription = ""; DefIntervalValue = 0; DefUnitDisplay = "km";
        foreach (var p in VehiclePicks) p.IsSelected = false;
        VehicleSearch = ""; DefEditId = null; TriedDefSave = false; ShowDefAdd = false;
    }

    [RelayCommand]
    private async Task AddDef()
    {
        TriedDefSave = true;
        bool editing = DefIsEditMode;
        if (editing ? !CanEdit : !CanWrite) { Status = "Yetki yok."; return; }
        if (string.IsNullOrWhiteSpace(DefName)) { Status = "Tanım adı zorunlu."; return; }
        if (!await ConfirmService.AskAsync(editing ? "Bakım tanımı güncellensin mi?" : "Yeni bakım tanımı kaydedilsin mi?", "Kaydet")) return;

        var dto = new NewMaintenanceDefinition(
            Name: DefName.Trim(), IntervalValue: DefIntervalValue, IntervalUnit: UnitCode(DefUnitDisplay),
            Description: string.IsNullOrWhiteSpace(DefDescription) ? null : DefDescription.Trim());
        var vehIds = VehiclePicks.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        try
        {
            if (editing)
            {
                DesktopServices.MaintenanceDefs.Update(_session, DefEditId!, dto);
                DesktopServices.MaintenanceDefs.SetVehicles(_session, DefEditId!, vehIds);
                Status = "Bakım tanımı güncellendi.";
            }
            else
            {
                DesktopServices.MaintenanceDefs.Create(_session, dto, vehIds);
                Status = "Bakım tanımı eklendi.";
            }
            ClearDef(); LoadDefs();
        }
        catch (Exception ex) { Status = "Kaydedilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task BeginEditDef()
    {
        if (SelectedDef is null) return;
        if (!CanEdit) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync("Bu bakım tanımını düzenlemek istiyor musunuz?", "Düzenle")) return;
        LoadVehiclePicks();
        var d = SelectedDef;
        DefEditId = d.Id;
        DefName = d.Name; DefDescription = d.Description ?? "";
        DefIntervalValue = d.IntervalValue; DefUnitDisplay = UnitDisplay(d.IntervalUnit);
        var ids = DesktopServices.MaintenanceDefs.GetVehicleIds(_session, d.Id).ToHashSet();
        foreach (var p in VehiclePicks) p.IsSelected = ids.Contains(p.Id);
        RebuildFilteredVehicles();
        TriedDefSave = false; ShowDefAdd = true;
    }

    [RelayCommand]
    private async Task RequestDeleteDef()
    {
        if (SelectedDef is null) return;
        if (!CanDelete) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync($"'{SelectedDef.Name}' bakım tanımı silinsin mi?", "Tanım Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try { DesktopServices.MaintenanceDefs.Delete(_session, SelectedDef.Id); LoadDefs(); Status = "Tanım silindi."; }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }

    // Alt bakım tanımı (parent = SelectedDef)
    [RelayCommand]
    private void AddSubDef()
    {
        if (SelectedDef is null) { Status = "Önce ana tanım seçin."; return; }
        if (!CanWrite) { Status = "Yetki yok."; return; }
        if (string.IsNullOrWhiteSpace(NewSubDefName)) return;
        try
        {
            DesktopServices.MaintenanceDefs.Create(_session, new NewMaintenanceDefinition(
                Name: NewSubDefName.Trim(), IntervalValue: 0, IntervalUnit: UnitCode(DefUnitDisplay),
                ParentDefId: SelectedDef.Id));
            NewSubDefName = "";
            OnSelectedDefChanged(SelectedDef); // alt listeyi yenile
            Status = "Alt bakım eklendi.";
        }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteSubDef(MaintenanceDefinitionRow? sub)
    {
        if (sub is null || !CanDelete) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync($"'{sub.Name}' alt bakımı silinsin mi?", "Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try { DesktopServices.MaintenanceDefs.Delete(_session, sub.Id); OnSelectedDefChanged(SelectedDef); Status = "Alt bakım silindi."; }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }

    private void LoadVehiclePicks()
    {
        if (_vehiclesLoaded) { RebuildFilteredVehicles(); return; }
        VehiclePicks.Clear();
        try { foreach (var v in DesktopServices.Vehicles.List(_session)) VehiclePicks.Add(new VehiclePick(v.Id, v.InternalCode, v.Plate ?? "")); }
        catch { }
        _vehiclesLoaded = true;
        RebuildFilteredVehicles();
    }

    private void RebuildFilteredVehicles()
    {
        FilteredVehicles.Clear();
        var t = VehicleSearch?.Trim();
        foreach (var p in VehiclePicks)
            if (string.IsNullOrEmpty(t) || p.Code.Contains(t, StringComparison.OrdinalIgnoreCase) || p.Plate.Contains(t, StringComparison.OrdinalIgnoreCase))
                FilteredVehicles.Add(p);
    }

    [RelayCommand] private void SelectAllVehicles() { foreach (var p in FilteredVehicles) p.IsSelected = true; }
    [RelayCommand] private void ClearVehicles() { foreach (var p in FilteredVehicles) p.IsSelected = false; }

    // ════════════════════ TAB 2 — UYARILAR ════════════════════
    public ObservableCollection<MaintenanceAlertRow> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    [RelayCommand]
    private void LoadAlerts()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            var codes = new Dictionary<string, string>();
            try { foreach (var v in DesktopServices.Vehicles.List(_session)) codes[v.Id] = v.InternalCode; }
            catch { }
            foreach (var a in DesktopServices.Maintenance.GetAlerts(_session)
                         .OrderByDescending(x => (int)x.Level).ThenByDescending(x => x.Progress))
            {
                var code = codes.TryGetValue(a.VehicleId, out var c) ? c : a.VehicleId;
                Items.Add(new MaintenanceAlertRow(code, a.DefinitionName, a.Level, a.Progress, a.Consumed, a.Interval));
            }
        }
        catch (Exception ex) { LoadError = ex.Message; }
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
