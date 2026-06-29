using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Günlük Faaliyet — araç hareket/transfer kayıtları + (bakımdan gelen) faaliyet listesi. Yeni kayıt:
/// Hareket/Transfer (araç + kaynak/hedef şube + operatör + süre + açıklama). Transfer → araç otomatik pasife alınır.
/// Bakım faaliyetleri Bakım ekranından oluşturulur; burada listede görünür.
/// </summary>
public sealed partial class DailyActivityViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, "daily_activity", PermissionAction.Create);
    public bool CanDelete => AccessControl.Can(_session, "daily_activity", PermissionAction.Delete);

    public ObservableCollection<DailyActivityListRow> Items { get; } = new();
    public ObservableCollection<string> Filters { get; } = new() { "Tümü", "Hareket / Transfer", "Bakım" };
    public ObservableCollection<string> KindOptions { get; } = new() { "Hareket", "Transfer" };
    public ObservableCollection<VehicleListRow> Vehicles { get; } = new();
    public ObservableCollection<BranchRow> Branches { get; } = new();
    public ObservableCollection<LookupItem> Personnel { get; } = new();

    [ObservableProperty] private string _selectedFilter = "Tümü";
    [ObservableProperty] private DateTimeOffset? _filterDate;
    [ObservableProperty] private string? _status;

    partial void OnFilterDateChanged(DateTimeOffset? value) => Load();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool HasRows => Items.Count > 0;
    public bool IsEmpty => !HasError && Items.Count == 0;

    // Form
    [ObservableProperty] private bool _showAdd;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTransfer))]
    private string _formKind = "Hareket";
    public bool IsTransfer => FormKind == "Transfer";
    [ObservableProperty] private VehicleListRow? _formVehicle;
    [ObservableProperty] private BranchRow? _formFrom;
    [ObservableProperty] private BranchRow? _formTo;
    [ObservableProperty] private LookupItem? _formOperator;
    [ObservableProperty] private decimal _formDuration;
    [ObservableProperty] private string _formDescription = "";
    [ObservableProperty] private DateTimeOffset? _formDate = DateTimeOffset.Now;
    [ObservableProperty] private string? _formError;

    public DailyActivityViewModel(SessionContext session)
    {
        _session = session;
        Load();
    }

    private string? FilterType => SelectedFilter switch
    {
        "Hareket / Transfer" => "movement",
        "Bakım" => "maintenance",
        _ => null
    };

    partial void OnSelectedFilterChanged(string value) => Load();

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            var day = FilterDate?.LocalDateTime.Date;
            foreach (var a in DesktopServices.DailyActivity.List(_session, FilterType))
            {
                if (day is not null &&
                    DateTimeOffset.FromUnixTimeMilliseconds(a.ActivityDate).LocalDateTime.Date != day) continue;
                Items.Add(a);
            }
            var bakim = Items.Count(x => x.ActivityType == "maintenance");
            var hareket = Items.Count - bakim;
            Status = $"{Items.Count} faaliyet — {bakim} bakım, {hareket} hareket/transfer"
                     + (day is null ? "" : $" ({day:dd.MM.yyyy})");
            if (Vehicles.Count == 0)
                try { foreach (var v in DesktopServices.Vehicles.List(_session)) Vehicles.Add(v); } catch { }
            if (Branches.Count == 0)
                try { foreach (var b in DesktopServices.Branches.List(_session)) Branches.Add(b); } catch { }
            if (Personnel.Count == 0)
                try { foreach (var p in DesktopServices.Lookups.ListPersonnel(_session)) Personnel.Add(p); } catch { }
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }

    // ════════════════════ BAKIM EKLE ════════════════════
    public ObservableCollection<MaintenanceDefinitionRow> MaintDefs { get; } = new();
    public ObservableCollection<MaintenanceDefinitionRow> MaintSubDefs { get; } = new();
    public ObservableCollection<MntMaterialLine> MntLines { get; } = new();
    public ObservableCollection<MaterialRefRow> MntMaterialResults { get; } = new();
    private bool _maintPickersLoaded;

    [ObservableProperty] private bool _showMaintAdd;
    [ObservableProperty] private VehicleListRow? _mVehicle;
    [ObservableProperty] private MaintenanceDefinitionRow? _mDef;
    [ObservableProperty] private MaintenanceDefinitionRow? _mSubDef;
    [ObservableProperty] private LookupItem? _mTechnician;
    [ObservableProperty] private decimal _mKm;
    [ObservableProperty] private decimal _mHour;
    [ObservableProperty] private DateTimeOffset? _mDate = DateTimeOffset.Now;
    [ObservableProperty] private string _mDescription = "";
    [ObservableProperty] private string _mntMaterialSearch = "";
    [ObservableProperty] private string? _maintError;

    partial void OnMDefChanged(MaintenanceDefinitionRow? value)
    {
        MSubDef = null; MaintSubDefs.Clear();
        if (value is null) return;
        try { foreach (var sub in DesktopServices.MaintenanceDefs.List(_session, value.Id)) MaintSubDefs.Add(sub); } catch { }
    }

    partial void OnMntMaterialSearchChanged(string value) => RefreshMntMaterials();

    private void RefreshMntMaterials()
    {
        MntMaterialResults.Clear();
        var term = MntMaterialSearch?.Trim();
        try
        {
            var page = DesktopServices.Materials.List(_session, new PageRequest { Limit = 30 },
                string.IsNullOrEmpty(term) ? null : term);
            foreach (var m in page.Items)
            {
                if (MntLines.Any(l => l.MaterialId == m.Id)) continue;
                MntMaterialResults.Add(new MaterialRefRow(m.Id, m.Code, m.Name));
            }
        }
        catch { }
    }

    [RelayCommand]
    private void NewMaintenance()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        ShowAdd = false;
        if (!_maintPickersLoaded)
        {
            try { foreach (var d in DesktopServices.MaintenanceDefs.List(_session)) MaintDefs.Add(d); } catch { }
            if (Vehicles.Count == 0) try { foreach (var v in DesktopServices.Vehicles.List(_session)) Vehicles.Add(v); } catch { }
            if (Personnel.Count == 0) try { foreach (var p in DesktopServices.Lookups.ListPersonnel(_session)) Personnel.Add(p); } catch { }
            _maintPickersLoaded = true;
        }
        MVehicle = null; MDef = null; MSubDef = null; MTechnician = null; MKm = 0; MHour = 0;
        MDate = DateTimeOffset.Now; MDescription = ""; MntMaterialSearch = ""; MaintError = null;
        MntLines.Clear(); RefreshMntMaterials();
        ShowMaintAdd = true;
    }

    [RelayCommand] private void CancelMaint() => ShowMaintAdd = false;

    [RelayCommand] private void AddMntMaterial(MaterialRefRow? m)
    {
        if (m is null) return;
        if (!MntLines.Any(l => l.MaterialId == m.Id)) MntLines.Add(new MntMaterialLine(m.Id, m.Code, m.Name));
        RefreshMntMaterials();
    }

    [RelayCommand] private void RemoveMntLine(MntMaterialLine? l)
    {
        if (l is not null) MntLines.Remove(l);
        RefreshMntMaterials();
    }

    [RelayCommand]
    private async Task SaveMaint()
    {
        MaintError = null;
        if (!CanWrite) { MaintError = "Yetki yok."; return; }
        if (MVehicle is null) { MaintError = "Araç seçin."; return; }
        if (MDef is null) { MaintError = "Bakım tanımı seçin."; return; }
        if (MntLines.Any(l => l.Quantity <= 0)) { MaintError = "Malzeme miktarı pozitif olmalı."; return; }
        if (!await ConfirmService.AskAsync("Bakım kaydı eklensin mi? (malzemeler stoktan düşülür)", "Bakım Ekle")) return;
        try
        {
            var materials = MntLines.Select(l => new MaintenanceMaterialLine(l.MaterialId, l.Quantity)).ToList();
            DesktopServices.DailyActivity.SaveMaintenanceActivity(_session, new NewMaintenance(
                VehicleId: MVehicle.Id, DefinitionId: MDef.Id, SubDefinitionId: MSubDef?.Id,
                TechnicianId: MTechnician?.Id,
                Description: string.IsNullOrWhiteSpace(MDescription) ? null : MDescription.Trim(),
                PerformedKm: MKm > 0 ? MKm : (decimal?)null,
                PerformedHour: MHour > 0 ? MHour : (decimal?)null,
                PerformedDate: MDate?.ToUnixTimeMilliseconds(),
                Materials: materials), Guid.NewGuid().ToString("N"));
            ShowMaintAdd = false;
            Load();
            Status = "Bakım kaydı eklendi (Günlük Faaliyet + Bakım Takibi).";
        }
        catch (Exception ex) { MaintError = "Kaydedilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private void NewActivity()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        ShowMaintAdd = false;
        FormKind = "Hareket"; FormVehicle = null; FormFrom = null; FormTo = null; FormOperator = null;
        FormDuration = 0; FormDescription = ""; FormDate = DateTimeOffset.Now; FormError = null;
        ShowAdd = true;
    }

    [RelayCommand]
    private void CancelAdd() => ShowAdd = false;

    [RelayCommand]
    private async Task DeleteActivity(DailyActivityListRow? row)
    {
        if (row is null) return;
        if (!CanDelete) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync(
                $"{row.TypeText} kaydı silinsin mi?" + (row.MaintenanceId != null ? "\n(Bağlı bakım kaydı Bakım ekranında kalır.)" : ""),
                "Faaliyet Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try
        {
            DesktopServices.DailyActivity.Delete(_session, row.Id);
            Load();
            Status = "Faaliyet silindi.";
        }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (!CanWrite) { FormError = "Yetki yok."; return; }
        if (FormVehicle is null) { FormError = "Araç seçin."; return; }
        var confirm = IsTransfer
            ? "Transfer kaydedilsin mi? (araç otomatik PASİF'e alınır)"
            : "Hareket kaydı oluşturulsun mu?";
        if (!await ConfirmService.AskAsync(confirm, "Günlük Faaliyet")) return;
        try
        {
            DesktopServices.DailyActivity.SaveMovement(_session, new NewMovementActivity(
                MovementKind: IsTransfer ? "transfer" : "movement",
                VehicleId: FormVehicle.Id,
                FromLocationId: FormFrom?.Id,
                ToLocationId: FormTo?.Id,
                OperatorId: FormOperator?.Id,
                DurationDays: FormDuration > 0 ? (int)FormDuration : null,
                Description: string.IsNullOrWhiteSpace(FormDescription) ? null : FormDescription.Trim(),
                ActivityDate: FormDate?.ToUnixTimeMilliseconds()), Guid.NewGuid().ToString("N"));
            ShowAdd = false;
            Load();
            Status = IsTransfer ? "Transfer kaydedildi (araç pasife alındı)." : "Hareket kaydedildi.";
        }
        catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
    }
}
