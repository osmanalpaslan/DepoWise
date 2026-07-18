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
    public ObservableCollection<string> Filters { get; } = new() { "Tümü", "Hareket / Transfer", "Bakım", "İlave Yağ/Filtre/Tamir" };
    // "İlave Yağ/İlave Filtre/Tamir" (kullanıcı isteği 2026-07-19): Bakım ile AYNI alanlar, Bakım Tanımı/Alt
    // Bakım YOK (bkz. IsRealMaintenance/IsMaintenanceLike aşağıda).
    public ObservableCollection<string> KindOptions { get; } = new() { "Hareket", "Transfer", "Bakım", "İlave Yağ", "İlave Filtre", "Tamir" };
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
    [NotifyPropertyChangedFor(nameof(IsRealMaintenance))]
    [NotifyPropertyChangedFor(nameof(IsMovement))]
    private string _formKind = "Hareket";
    public bool IsTransfer => FormKind == "Transfer";
    /// <summary>Bakım İLE AYNI alan setini gösterir (Teknisyen/KM/Saat/Malzeme) — Bakım + 3 yeni tür
    /// (kullanıcı isteği 2026-07-19: "bakım ile aynı olacak sadece bakım tanımı ve alt bakım olmayacak").</summary>
    public bool IsMaintenance => FormKind is "Bakım" or "İlave Yağ" or "İlave Filtre" or "Tamir";
    /// <summary>YALNIZ gerçek "Bakım" — Bakım Tanımı/Alt Bakım seçicileri bu türde gösterilir, 3 yenisinde YOK.</summary>
    public bool IsRealMaintenance => FormKind == "Bakım";
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

    /// <summary>"İlave Yağ/Filtre/Tamir" filtresi çoklu tür kapsadığından <see cref="FilterType"/> (tek
    /// değer) yetersiz — liste tarafında bu özel durumu ayrıca eler.</summary>
    private static readonly HashSet<string> ExtraTypes = new(StringComparer.Ordinal) { "extra_oil", "extra_filter", "repair" };

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
            // "İlave Yağ/Filtre/Tamir" 3 türü birden kapsar → sunucu tarafı tek-değer filtresi (FilterType)
            // kullanılmaz, tümü çekilip burada elenir (liste küçük; ADR-089 grid'i gelince madde 15'te düzelir).
            var isExtraFilter = SelectedFilter == "İlave Yağ/Filtre/Tamir";
            foreach (var a in DesktopServices.DailyActivity.List(_session, isExtraFilter ? null : FilterType))
            {
                if (isExtraFilter && !ExtraTypes.Contains(a.ActivityType)) continue;
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

        if (IsRealMaintenance)
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

        // "İlave Yağ / İlave Filtre / Tamir" (kullanıcı isteği 2026-07-19): Bakım ile AYNI, tanım/alt-bakım YOK.
        if (IsMaintenance)
        {
            if (MntLines.Any(l => l.Quantity <= 0)) { FormError = "Malzeme miktarı pozitif olmalı."; return; }
            if (!await ConfirmService.AskAsync($"{FormKind} kaydı eklensin mi? (malzemeler stoktan düşülür)", "Yeni Kayıt")) return;
            try
            {
                var extraType = FormKind switch
                {
                    "İlave Yağ" => ExtraActivityTypes.ExtraOil, "İlave Filtre" => ExtraActivityTypes.ExtraFilter,
                    "Tamir" => ExtraActivityTypes.Repair, _ => throw new InvalidOperationException("Geçersiz kayıt tipi."),
                };
                var materials = MntLines.Select(l => new MaintenanceMaterialLine(l.MaterialId, l.Quantity)).ToList();
                DesktopServices.DailyActivity.SaveExtraActivity(_session, extraType, new NewMaintenance(
                    VehicleId: FormVehicle.Id, DefinitionId: "", SubDefinitionId: null,
                    TechnicianId: MTechnician?.Id,
                    Description: string.IsNullOrWhiteSpace(FormDescription) ? null : FormDescription.Trim(),
                    PerformedKm: MKm > 0 ? MKm : (decimal?)null,
                    PerformedHour: MHour > 0 ? MHour : (decimal?)null,
                    PerformedDate: FormDate?.ToUnixTimeMilliseconds(),
                    Materials: materials), Guid.NewGuid().ToString("N"));
                ShowForm = false; Load();
                Status = $"{FormKind} kaydı eklendi (Günlük Faaliyet + Bakım Takibi).";
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
