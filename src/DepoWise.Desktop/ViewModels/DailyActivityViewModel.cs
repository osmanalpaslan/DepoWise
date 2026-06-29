using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
/// Günlük Faaliyet — bir günde yapılan TÜM işler tek ekranda. Tek "Yeni Kayıt Oluştur" + Kayıt Tipi
/// (Hareket / Transfer / Bakım) forma göre alanları değiştirir. Transfer → araç otomatik pasife. Bakım →
/// tek bakım kaydı + tek stok düşümü (Bakım Takibi'nde de görünür). Gün filtresi + günlük özet.
/// </summary>
public sealed partial class DailyActivityViewModel : ViewModelBase
{
    private readonly SessionContext _session;
    private bool _pickersLoaded;

    public bool CanWrite => AccessControl.Can(_session, "daily_activity", PermissionAction.Create);
    public bool CanDelete => AccessControl.Can(_session, "daily_activity", PermissionAction.Delete);

    public ObservableCollection<DailyActivityListRow> Items { get; } = new();
    public ObservableCollection<string> Filters { get; } = new() { "Tümü", "Hareket / Transfer", "Bakım" };
    public ObservableCollection<string> KindOptions { get; } = new() { "Hareket", "Transfer", "Bakım" };
    public ObservableCollection<VehicleListRow> Vehicles { get; } = new();
    public ObservableCollection<BranchRow> Branches { get; } = new();
    public ObservableCollection<LookupItem> Personnel { get; } = new();
    public ObservableCollection<MaintenanceDefinitionRow> MaintDefs { get; } = new();
    public ObservableCollection<MaintenanceDefinitionRow> MaintSubDefs { get; } = new();
    public ObservableCollection<MntMaterialLine> MntLines { get; } = new();
    public ObservableCollection<MaterialRefRow> MntMaterialResults { get; } = new();

    [ObservableProperty] private string _selectedFilter = "Tümü";
    [ObservableProperty] private DateTimeOffset? _filterDate;
    [ObservableProperty] private string? _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool HasRows => Items.Count > 0;
    public bool IsEmpty => !HasError && Items.Count == 0;

    // ── Tek form, Kayıt Tipi'ne göre değişir ──
    [ObservableProperty] private bool _showForm;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTransfer))]
    [NotifyPropertyChangedFor(nameof(IsMaintenance))]
    [NotifyPropertyChangedFor(nameof(IsMovement))]
    private string _formKind = "Hareket";
    public bool IsTransfer => FormKind == "Transfer";
    public bool IsMaintenance => FormKind == "Bakım";
    public bool IsMovement => FormKind is "Hareket" or "Transfer";

    // Ortak
    [ObservableProperty] private VehicleListRow? _formVehicle;
    [ObservableProperty] private DateTimeOffset? _formDate = DateTimeOffset.Now;
    [ObservableProperty] private string _formDescription = "";
    [ObservableProperty] private string? _formError;

    // Hareket / Transfer alanları
    [ObservableProperty] private BranchRow? _formFrom;
    [ObservableProperty] private BranchRow? _formTo;
    [ObservableProperty] private LookupItem? _formOperator;
    [ObservableProperty] private decimal _formDuration;

    // Bakım alanları
    [ObservableProperty] private MaintenanceDefinitionRow? _mDef;
    [ObservableProperty] private MaintenanceDefinitionRow? _mSubDef;
    [ObservableProperty] private LookupItem? _mTechnician;
    [ObservableProperty] private decimal _mKm;
    [ObservableProperty] private decimal _mHour;
    [ObservableProperty] private string _mntMaterialSearch = "";
    [ObservableProperty] private bool _isAddingSub;
    [ObservableProperty] private string _newSubName = "";

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
    partial void OnFilterDateChanged(DateTimeOffset? value) => Load();

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
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }

    private void EnsurePickers()
    {
        if (_pickersLoaded) return;
        try { foreach (var v in DesktopServices.Vehicles.List(_session)) Vehicles.Add(v); } catch { }
        try { foreach (var b in DesktopServices.Branches.List(_session)) Branches.Add(b); } catch { }
        try { foreach (var p in DesktopServices.Lookups.ListPersonnel(_session)) Personnel.Add(p); } catch { }
        try { foreach (var d in DesktopServices.MaintenanceDefs.List(_session)) MaintDefs.Add(d); } catch { }
        _pickersLoaded = true;
    }

    [RelayCommand]
    private void NewRecord()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        EnsurePickers();
        FormKind = "Hareket";
        FormVehicle = null; FormDate = DateTimeOffset.Now; FormDescription = ""; FormError = null;
        FormFrom = null; FormTo = null; FormOperator = null; FormDuration = 0;
        MDef = null; MSubDef = null; MTechnician = null; MKm = 0; MHour = 0;
        MntMaterialSearch = ""; IsAddingSub = false; NewSubName = "";
        MntLines.Clear(); RefreshMntMaterials();
        ShowForm = true;
    }

    [RelayCommand]
    private void CancelForm() => ShowForm = false;

    // ── Bakım: alt tanım + malzeme ──
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

    [RelayCommand] private void StartAddSub() { if (MDef is null) { Status = "Önce bakım tanımı seçin."; return; } IsAddingSub = true; NewSubName = ""; }
    [RelayCommand] private void CancelAddSub() { IsAddingSub = false; NewSubName = ""; }
    [RelayCommand]
    private void ConfirmAddSub()
    {
        if (MDef is null || string.IsNullOrWhiteSpace(NewSubName)) return;
        try
        {
            var id = DesktopServices.MaintenanceDefs.Create(_session, new NewMaintenanceDefinition(
                NewSubName.Trim(), 0m, "km", ParentDefId: MDef.Id));
            var row = new MaintenanceDefinitionRow(id, NewSubName.Trim(), 0m, "km", null, MDef.Id);
            MaintSubDefs.Add(row); MSubDef = row;
            IsAddingSub = false; NewSubName = "";
        }
        catch (Exception ex) { FormError = "Eklenemedi: " + ex.Message; }
    }

    // ── Kaydet (Kayıt Tipi'ne göre) ──
    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (!CanWrite) { FormError = "Yetki yok."; return; }
        if (FormVehicle is null) { FormError = "Araç seçin."; return; }

        if (IsMaintenance)
        {
            if (MDef is null) { FormError = "Bakım tanımı seçin."; return; }
            if (MntLines.Any(l => l.Quantity <= 0)) { FormError = "Malzeme miktarı pozitif olmalı."; return; }
            if (!await ConfirmService.AskAsync("Bakım kaydı eklensin mi? (malzemeler stoktan düşülür)", "Yeni Kayıt")) return;
            try
            {
                var materials = MntLines.Select(l => new MaintenanceMaterialLine(l.MaterialId, l.Quantity)).ToList();
                DesktopServices.DailyActivity.SaveMaintenanceActivity(_session, new NewMaintenance(
                    VehicleId: FormVehicle.Id, DefinitionId: MDef.Id, SubDefinitionId: MSubDef?.Id,
                    TechnicianId: MTechnician?.Id,
                    Description: string.IsNullOrWhiteSpace(FormDescription) ? null : FormDescription.Trim(),
                    PerformedKm: MKm > 0 ? MKm : (decimal?)null,
                    PerformedHour: MHour > 0 ? MHour : (decimal?)null,
                    PerformedDate: FormDate?.ToUnixTimeMilliseconds(),
                    Materials: materials), Guid.NewGuid().ToString("N"));
                ShowForm = false; Load();
                Status = "Bakım kaydı eklendi (Günlük Faaliyet + Bakım Takibi).";
            }
            catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
            return;
        }

        // Hareket / Transfer
        var confirm = IsTransfer ? "Transfer kaydedilsin mi? (araç otomatik PASİF'e alınır)" : "Hareket kaydı oluşturulsun mu?";
        if (!await ConfirmService.AskAsync(confirm, "Yeni Kayıt")) return;
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
            ShowForm = false; Load();
            Status = IsTransfer ? "Transfer kaydedildi (araç pasife alındı)." : "Hareket kaydedildi.";
        }
        catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
    }

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
}
