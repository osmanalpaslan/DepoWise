using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Security;
using DepoWise.Desktop.Controls;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewMaintenance))]
    private string _newStatus = "active";
    [ObservableProperty] private decimal _newMeter;
    [ObservableProperty] private string _newMeterUnit = "km";
    [ObservableProperty] private string _newChassisNo = "";
    [ObservableProperty] private string _newEngineNo = "";
    [ObservableProperty] private string _newStatusNote = "";

    public bool IsNewMaintenance => NewStatus == "maintenance";

    // ── Paylaşılan araç tanımları (LookupService) ──
    public ObservableCollection<LookupItem> VehicleTypes { get; } = new();
    public ObservableCollection<LookupItem> VehicleCategories { get; } = new();
    public ObservableCollection<LookupItem> VehicleBrands { get; } = new();
    public ObservableCollection<LookupItem> VehicleModels { get; } = new();
    public ObservableCollection<LookupItem> Branches { get; } = new();
    public ObservableCollection<LookupItem> Drivers { get; } = new();

    [ObservableProperty] private LookupItem? _selVehicleType;
    [ObservableProperty] private LookupItem? _selCategory;
    [ObservableProperty] private LookupItem? _selBrand;
    [ObservableProperty] private LookupItem? _selModel;
    [ObservableProperty] private LookupItem? _selBranch;
    [ObservableProperty] private LookupItem? _selDriver;
    private bool _vehLookupsLoaded;

    partial void OnSelBrandChanged(LookupItem? value)
    {
        SelModel = null;
        VehicleModels.Clear();
        if (value is null) return;
        try { foreach (var m in DesktopServices.Lookups.ListVehicleModels(_session, value.Id)) VehicleModels.Add(m); }
        catch { /* model yoksa sessiz */ }
    }

    // ── Inline "+" yeni tanım ──
    [ObservableProperty] private bool _isAddingType; [ObservableProperty] private string _newTypeName = "";
    [ObservableProperty] private bool _isAddingCat; [ObservableProperty] private string _newCatName = "";
    [ObservableProperty] private bool _isAddingBrand; [ObservableProperty] private string _newBrandName = "";
    [ObservableProperty] private bool _isAddingModel; [ObservableProperty] private string _newModelName = "";
    [ObservableProperty] private bool _isAddingBranch; [ObservableProperty] private string _newBranchName = "";
    [ObservableProperty] private bool _isAddingDriver; [ObservableProperty] private string _newDriverName = "";

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
    private async Task Add()
    {
        TriedSave = true;
        if (!CanWrite) { Status = "Yetki yok."; return; }
        if (string.IsNullOrWhiteSpace(NewCode)) { Status = "İç kod zorunlu."; return; }
        if (!await ConfirmService.AskAsync("Yeni araç kaydedilsin mi?", "Kaydet")) return;
        try
        {
            DesktopServices.Vehicles.Create(_session, new NewVehicle(
                InternalCode: NewCode.Trim(),
                Plate: string.IsNullOrWhiteSpace(NewPlate) ? null : NewPlate.Trim(),
                ProductionYear: NewYear > 0 ? NewYear : (int?)null,
                CurrentMeter: NewMeter, MeterUnit: NewMeterUnit,
                BranchId: SelBranch?.Id, DriverPersonnelId: SelDriver?.Id,
                ChassisNo: string.IsNullOrWhiteSpace(NewChassisNo) ? null : NewChassisNo.Trim(),
                EngineNo: string.IsNullOrWhiteSpace(NewEngineNo) ? null : NewEngineNo.Trim(),
                Status: NewStatus,
                StatusNote: IsNewMaintenance && !string.IsNullOrWhiteSpace(NewStatusNote) ? NewStatusNote.Trim() : null,
                VehicleTypeId: SelVehicleType?.Id, CategoryId: SelCategory?.Id,
                BrandId: SelBrand?.Id, VehicleModelId: SelModel?.Id));
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
        if (ShowAdd) LoadVehLookups();
    }

    [RelayCommand]
    private void Clear()
    {
        NewCode = ""; NewPlate = ""; NewYear = 0; NewStatus = "active"; NewMeter = 0; NewMeterUnit = "km";
        NewChassisNo = ""; NewEngineNo = ""; NewStatusNote = "";
        SelVehicleType = null; SelCategory = null; SelBrand = null; SelModel = null; SelBranch = null; SelDriver = null;
        IsAddingType = IsAddingCat = IsAddingBrand = IsAddingModel = IsAddingBranch = IsAddingDriver = false;
        TriedSave = false; ShowAdd = false;
    }

    private void LoadVehLookups()
    {
        if (_vehLookupsLoaded) return;
        try
        {
            VehicleTypes.Clear(); foreach (var x in DesktopServices.Lookups.List(_session, "vehicle_types")) VehicleTypes.Add(x);
            VehicleCategories.Clear(); foreach (var x in DesktopServices.Lookups.List(_session, "vehicle_categories")) VehicleCategories.Add(x);
            VehicleBrands.Clear(); foreach (var x in DesktopServices.Lookups.ListBrands(_session, "vehicle")) VehicleBrands.Add(x);
            Branches.Clear(); foreach (var x in DesktopServices.Lookups.List(_session, "branches")) Branches.Add(x);
            Drivers.Clear(); foreach (var x in DesktopServices.Lookups.ListPersonnel(_session)) Drivers.Add(x);
            _vehLookupsLoaded = true;
        }
        catch (Exception ex) { Status = "Tanımlar yüklenemedi: " + ex.Message; }
    }

    // ── Inline "+" komutları ──
    [RelayCommand] private void StartAddType() { IsAddingType = true; NewTypeName = ""; }
    [RelayCommand] private void CancelAddType() { IsAddingType = false; NewTypeName = ""; }
    [RelayCommand] private void ConfirmAddType() => AddLookup(NewTypeName, () => DesktopServices.Lookups.AddVehicleType(_session, NewTypeName.Trim()), VehicleTypes, x => SelVehicleType = x, () => { IsAddingType = false; NewTypeName = ""; });

    [RelayCommand] private void StartAddCat() { IsAddingCat = true; NewCatName = ""; }
    [RelayCommand] private void CancelAddCat() { IsAddingCat = false; NewCatName = ""; }
    [RelayCommand] private void ConfirmAddCat() => AddLookup(NewCatName, () => DesktopServices.Lookups.AddVehicleCategory(_session, NewCatName.Trim()), VehicleCategories, x => SelCategory = x, () => { IsAddingCat = false; NewCatName = ""; });

    [RelayCommand] private void StartAddBrand() { IsAddingBrand = true; NewBrandName = ""; }
    [RelayCommand] private void CancelAddBrand() { IsAddingBrand = false; NewBrandName = ""; }
    [RelayCommand] private void ConfirmAddBrand() => AddLookup(NewBrandName, () => DesktopServices.Lookups.AddVehicleBrand(_session, NewBrandName.Trim()), VehicleBrands, x => SelBrand = x, () => { IsAddingBrand = false; NewBrandName = ""; });

    [RelayCommand] private void StartAddModel() { if (SelBrand is null) { Status = "Önce marka seçin."; return; } IsAddingModel = true; NewModelName = ""; }
    [RelayCommand] private void CancelAddModel() { IsAddingModel = false; NewModelName = ""; }
    [RelayCommand] private void ConfirmAddModel() { if (SelBrand is null) return; AddLookup(NewModelName, () => DesktopServices.Lookups.AddVehicleModel(_session, SelBrand!.Id, NewModelName.Trim()), VehicleModels, x => SelModel = x, () => { IsAddingModel = false; NewModelName = ""; }); }

    [RelayCommand] private void StartAddBranch() { IsAddingBranch = true; NewBranchName = ""; }
    [RelayCommand] private void CancelAddBranch() { IsAddingBranch = false; NewBranchName = ""; }
    [RelayCommand] private void ConfirmAddBranch() => AddLookup(NewBranchName, () => DesktopServices.Lookups.AddBranch(_session, NewBranchName.Trim()), Branches, x => SelBranch = x, () => { IsAddingBranch = false; NewBranchName = ""; });

    [RelayCommand] private void StartAddDriver() { IsAddingDriver = true; NewDriverName = ""; }
    [RelayCommand] private void CancelAddDriver() { IsAddingDriver = false; NewDriverName = ""; }
    [RelayCommand] private void ConfirmAddDriver() => AddLookup(NewDriverName, () => DesktopServices.Lookups.AddPersonnel(_session, NewDriverName.Trim(), "Şoför"), Drivers, x => SelDriver = x, () => { IsAddingDriver = false; NewDriverName = ""; });

    private void AddLookup(string name, Func<string> add, ObservableCollection<LookupItem> coll, Action<LookupItem> select, Action done)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        try { var id = add(); var item = new LookupItem(id, name.Trim()); coll.Add(item); select(item); done(); }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }

    // ===== Detay / Düzenle / Sil =====
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private VehicleRow? _selected;

    private decimal _loadedMeter;

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _confirmDelete;
    [ObservableProperty] private string _editPlate = "";
    [ObservableProperty] private int _editYear;
    [ObservableProperty] private decimal _editMeter;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMaintenanceStatus))]
    private string _editStatus = "active";
    [ObservableProperty] private string _editStatusNote = "";

    public bool HasSelection => Selected != null;
    public bool IsMaintenanceStatus => EditStatus == "maintenance";
    public bool CanEdit => AccessControl.Can(_session, "vehicles", PermissionAction.Edit);
    public bool CanDelete => AccessControl.Can(_session, "vehicles", PermissionAction.Delete);

    partial void OnSelectedChanged(VehicleRow? value)
    {
        IsEditing = false; ConfirmDelete = false;
        if (value is null) return;
        try
        {
            var d = DesktopServices.Vehicles.Get(_session, value.Id);
            EditPlate = d.Plate ?? "";
            EditYear = d.ProductionYear ?? 0;
            EditStatus = d.Status;
            EditStatusNote = d.StatusNote ?? "";
            EditMeter = d.CurrentMeter;
            _loadedMeter = d.CurrentMeter;
        }
        catch (Exception ex) { Status = "Detay yüklenemedi: " + ex.Message; }
    }

    [RelayCommand]
    private void BeginEdit()
    {
        if (!CanEdit) { Status = "Yetki yok."; return; }
        if (Selected is null) return;
        IsEditing = true; ConfirmDelete = false;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        OnSelectedChanged(Selected); // alanları yeniden yükle
    }

    [RelayCommand]
    private async Task SaveEdit()
    {
        if (Selected is null || !CanEdit) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync("Araç bilgileri güncellensin mi?", "Kaydet")) return;
        try
        {
            DesktopServices.Vehicles.Update(_session, Selected.Id, new UpdateVehicle(
                Plate: string.IsNullOrWhiteSpace(EditPlate) ? null : EditPlate.Trim(),
                ProductionYear: EditYear > 0 ? EditYear : (int?)null,
                Status: EditStatus,
                StatusNote: string.IsNullOrWhiteSpace(EditStatusNote) ? null : EditStatusNote.Trim()));

            // Sayaç yalnız ileri (servis geriye gitmeyi reddeder)
            if (EditMeter != _loadedMeter)
            {
                try { DesktopServices.Vehicles.SetMeter(_session, Selected.Id, EditMeter, "vehicle_form"); }
                catch (MeterBackwardException) { Status = "Araç güncellendi (sayaç geriye alınamaz, değişmedi)."; IsEditing = false; Load(); return; }
            }
            IsEditing = false;
            Load();
            Status = "Araç güncellendi.";
        }
        catch (Exception ex) { Status = "Güncellenemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task RequestDelete()
    {
        if (!CanDelete) { Status = "Yetki yok."; return; }
        if (Selected is null) return;
        var ok = await ConfirmService.AskAsync(
            $"'{Selected.Code}' aracı silinsin mi? Kayıt çöp kutusuna alınır.",
            "Araç Sil", "Evet, Sil", "Vazgeç", danger: true);
        if (!ok) return;
        try
        {
            DesktopServices.Vehicles.Delete(_session, Selected.Id);
            Selected = null;
            Load();
            Status = "Araç silindi.";
        }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
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
