using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
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
    [ObservableProperty] private string? _status;
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
            foreach (var a in DesktopServices.DailyActivity.List(_session, FilterType)) Items.Add(a);
            if (Vehicles.Count == 0)
                try { foreach (var v in DesktopServices.Vehicles.List(_session)) Vehicles.Add(v); } catch { }
            if (Branches.Count == 0)
                try { foreach (var b in DesktopServices.Branches.List(_session)) Branches.Add(b); } catch { }
            if (Personnel.Count == 0)
                try { foreach (var p in DesktopServices.Lookups.ListPersonnel(_session)) Personnel.Add(p); } catch { }
            Status = $"{Items.Count} faaliyet";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }

    [RelayCommand]
    private void NewActivity()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
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
