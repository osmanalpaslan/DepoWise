using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Requests;
using DepoWise.Application.Security;
using DepoWise.Desktop.Controls;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Talepler — 2 alt sekme: <b>Talep Formu</b> (yeni talep kayıt + tüm talepler listesi/detay) ve
/// <b>Talep Onaylama</b> (bekleyen talepler + onayla/reddet). Eski projeyle parite:
/// belge no otomatik (TLP-YYYY-NNNN), kaydedince talep "iletilir" (Beklemede) ve dondurulur —
/// kaydedildikten sonra düzenleme/silme YOK; yalnız onay/ret akışı kalır. Onay/ret RequestService
/// durum makinesi + buton yetkisiyle (master/yetkili) korunur.
/// </summary>
public sealed partial class RequestsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, "requests", PermissionAction.Create);
    public bool CanApproveButton => AccessControl.Can(_session, "requests", PermissionAction.Edit);

    public ObservableCollection<RequestRow> Items { get; } = new();
    public ObservableCollection<RequestRow> PendingItems { get; } = new();
    public ObservableCollection<RequestItemRow> DetailItems { get; } = new();
    public ObservableCollection<string> History { get; } = new();
    public ObservableCollection<string> Filters { get; } = new() { "Tümü", "Taslak", "Beklemede", "Onaylı", "Reddedildi", "İptal" };

    // Lookup'lar (form)
    public ObservableCollection<LookupItem> Sites { get; } = new();
    public ObservableCollection<LookupItem> Personnel { get; } = new();
    public ObservableCollection<VehicleListRow> Vehicles { get; } = new();
    private bool _lookupsLoaded;

    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _selectedFilter = "Tümü";
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string _rejectReason = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;

    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;
    public bool PendingEmpty => PendingItems.Count == 0;
    public bool HasPending => PendingItems.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyPropertyChangedFor(nameof(CanApprove))]
    [NotifyPropertyChangedFor(nameof(CanReject))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    private RequestRow? _selected;

    public bool HasSelection => Selected != null;
    public bool CanSubmit => Selected?.Status == RequestStatus.Draft;
    public bool CanApprove => Selected?.Status == RequestStatus.Pending && CanApproveButton;
    public bool CanReject => Selected?.Status == RequestStatus.Pending && CanApproveButton;
    public bool CanCancel => Selected is { Status: RequestStatus.Draft or RequestStatus.Pending };

    public RequestsViewModel(SessionContext session, int initialTab = 0)
    {
        _session = session;
        SelectedTab = initialTab;
        Load();
    }

    private RequestStatus? FilterStatus => SelectedFilter switch
    {
        "Taslak" => RequestStatus.Draft,
        "Beklemede" => RequestStatus.Pending,
        "Onaylı" => RequestStatus.Approved,
        "Reddedildi" => RequestStatus.Rejected,
        "İptal" => RequestStatus.Cancelled,
        _ => null,
    };

    partial void OnSearchChanged(string value) => Load();
    partial void OnSelectedFilterChanged(string value) => Load();

    [RelayCommand]
    private void Load()
    {
        EnsureLookups();
        try
        {
            LoadError = null;
            Items.Clear();
            foreach (var r in DesktopServices.Requests.List(_session, FilterStatus,
                         string.IsNullOrWhiteSpace(Search) ? null : Search.Trim()))
                Items.Add(new RequestRow(r.Id, r.DocNo, r.Status, r.RequestDate, r.ItemCount, r.Description));

            PendingItems.Clear();
            foreach (var r in DesktopServices.Requests.List(_session, RequestStatus.Pending))
                PendingItems.Add(new RequestRow(r.Id, r.DocNo, r.Status, r.RequestDate, r.ItemCount, r.Description));

            Status = $"{Items.Count} talep · {PendingItems.Count} bekleyen";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        Selected = null;
        DetailItems.Clear();
        History.Clear();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(PendingEmpty));
        OnPropertyChanged(nameof(HasPending));
    }

    private void EnsureLookups()
    {
        if (_lookupsLoaded) return;
        try
        {
            foreach (var s in DesktopServices.Lookups.List(_session, "branches")) Sites.Add(s);
            foreach (var p in DesktopServices.Lookups.ListPersonnel(_session)) Personnel.Add(p);
            foreach (var v in DesktopServices.Vehicles.List(_session)) Vehicles.Add(v);
        }
        catch { }
        _lookupsLoaded = true;
    }

    partial void OnSelectedChanged(RequestRow? value)
    {
        DetailItems.Clear();
        History.Clear();
        if (value is null) return;
        try
        {
            foreach (var it in DesktopServices.Requests.GetItems(_session, value.Id)) DetailItems.Add(it);
            foreach (var (from, to, reason) in DesktopServices.Requests.GetHistory(value.Id))
                History.Add($"{(from is null ? "—" : RequestRow.StatusLabel(from.Value))} → {RequestRow.StatusLabel(to)}"
                            + (string.IsNullOrWhiteSpace(reason) ? "" : $" ({reason})"));
        }
        catch (Exception ex) { Status = "Detay yüklenemedi: " + ex.Message; }
    }

    // ════════════════════ YENİ TALEP FORMU ════════════════════
    [ObservableProperty] private bool _showForm;
    [ObservableProperty] private LookupItem? _formSite;
    [ObservableProperty] private LookupItem? _formRequester;
    [ObservableProperty] private LookupItem? _formWarehouse;   // Depo Sorumlusu
    [ObservableProperty] private LookupItem? _formApprover;    // Onay Veren
    [ObservableProperty] private DateTimeOffset? _formDate = DateTimeOffset.Now;
    [ObservableProperty] private string _formDescription = "";
    [ObservableProperty] private string? _formError;

    public ObservableCollection<ReqItemLine> FormItems { get; } = new();

    // Kalem ekleme
    public ObservableCollection<MaterialRefRow> MaterialResults { get; } = new();
    [ObservableProperty] private string _materialSearch = "";
    [ObservableProperty] private MaterialRefRow? _pickedMaterial;
    [ObservableProperty] private decimal _newItemQty = 1;
    [ObservableProperty] private VehicleListRow? _newItemVehicle;
    [ObservableProperty] private string? _itemError;

    // Inline personel ekleme (3 alan ortak) + şantiye ekleme
    [ObservableProperty] private bool _isAddingPersonnel;
    [ObservableProperty] private string _newPersonnelName = "";
    private string _personnelTarget = "requester";
    [ObservableProperty] private bool _isAddingSite;
    [ObservableProperty] private string _newSiteName = "";

    partial void OnMaterialSearchChanged(string value) => RefreshMaterials();

    private void RefreshMaterials()
    {
        MaterialResults.Clear();
        var term = MaterialSearch?.Trim();
        try
        {
            var page = DesktopServices.Materials.List(_session, new PageRequest { Limit = 30 },
                string.IsNullOrEmpty(term) ? null : term);
            foreach (var m in page.Items)
            {
                if (FormItems.Any(l => l.MaterialId == m.Id)) continue;
                MaterialResults.Add(new MaterialRefRow(m.Id, m.Code, m.Name));
            }
        }
        catch { }
    }

    [RelayCommand]
    private void NewRequest()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        FormSite = null; FormRequester = null; FormWarehouse = null; FormApprover = null;
        FormDate = DateTimeOffset.Now; FormDescription = ""; FormError = null;
        FormItems.Clear();
        MaterialSearch = ""; PickedMaterial = null; NewItemQty = 1; NewItemVehicle = null; ItemError = null;
        IsAddingPersonnel = false; IsAddingSite = false;
        RefreshMaterials();
        ShowForm = true;
    }

    [RelayCommand]
    private void CancelForm() => ShowForm = false;

    [RelayCommand]
    private void PickMaterial(MaterialRefRow? m)
    {
        if (m is null) return;
        PickedMaterial = m;
        MaterialSearch = $"{m.Code} - {m.Name}";
        MaterialResults.Clear();
        NewItemQty = 1;
    }

    [RelayCommand]
    private void AddItem()
    {
        ItemError = null;
        if (PickedMaterial is null) { ItemError = "Önce bir malzeme seçin."; return; }
        if (NewItemQty <= 0) { ItemError = "Geçerli bir miktar girin."; return; }
        FormItems.Add(new ReqItemLine(PickedMaterial.Id, PickedMaterial.Code, PickedMaterial.Name,
            NewItemQty, NewItemVehicle?.Id, NewItemVehicle?.Display));
        PickedMaterial = null; MaterialSearch = ""; NewItemQty = 1; NewItemVehicle = null;
        RefreshMaterials();
    }

    [RelayCommand]
    private void RemoveItem(ReqItemLine? line)
    {
        if (line is not null) FormItems.Remove(line);
        RefreshMaterials();
    }

    [RelayCommand] private void StartAddRequester() { _personnelTarget = "requester"; NewPersonnelName = ""; IsAddingPersonnel = true; }
    [RelayCommand] private void StartAddWarehouse() { _personnelTarget = "warehouse"; NewPersonnelName = ""; IsAddingPersonnel = true; }
    [RelayCommand] private void StartAddApprover() { _personnelTarget = "approver"; NewPersonnelName = ""; IsAddingPersonnel = true; }
    [RelayCommand] private void CancelAddPersonnel() { IsAddingPersonnel = false; NewPersonnelName = ""; }

    [RelayCommand]
    private void ConfirmAddPersonnel()
    {
        if (string.IsNullOrWhiteSpace(NewPersonnelName)) return;
        try
        {
            var id = DesktopServices.Lookups.AddPersonnel(_session, NewPersonnelName.Trim(), "Personel");
            var item = new LookupItem(id, NewPersonnelName.Trim());
            Personnel.Add(item);
            switch (_personnelTarget)
            {
                case "requester": FormRequester = item; break;
                case "warehouse": FormWarehouse = item; break;
                case "approver": FormApprover = item; break;
            }
            IsAddingPersonnel = false; NewPersonnelName = "";
        }
        catch (Exception ex) { FormError = "Personel eklenemedi: " + ex.Message; }
    }

    [RelayCommand] private void StartAddSite() { NewSiteName = ""; IsAddingSite = true; }
    [RelayCommand] private void CancelAddSite() { IsAddingSite = false; NewSiteName = ""; }

    [RelayCommand]
    private void ConfirmAddSite()
    {
        if (string.IsNullOrWhiteSpace(NewSiteName)) return;
        try
        {
            var id = DesktopServices.Lookups.AddBranch(_session, NewSiteName.Trim());
            var item = new LookupItem(id, NewSiteName.Trim());
            Sites.Add(item); FormSite = item;
            IsAddingSite = false; NewSiteName = "";
        }
        catch (Exception ex) { FormError = "Şantiye eklenemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task SaveRequest()
    {
        FormError = null;
        if (!CanWrite) { FormError = "Yetki yok."; return; }
        if (FormSite is null) { FormError = "Şantiye seçilmesi zorunludur."; return; }
        if (FormItems.Count == 0) { FormError = "En az bir talep kalemi eklenmelidir."; return; }
        if (!await ConfirmService.AskAsync(
                "Talep oluşturulup yöneticiye iletilecek (kaydedildikten sonra düzenlenemez). Onaylıyor musunuz?", "Talep Oluştur"))
            return;
        try
        {
            var items = FormItems.Select(l => new RequestItemInput(l.MaterialId, l.Quantity, l.VehicleId)).ToList();
            DesktopServices.Requests.Create(_session, new NewRequest(
                Items: items,
                BranchId: FormSite.Id,
                RequesterId: FormRequester?.Id,
                WarehouseId: FormWarehouse?.Id,
                ApproverId: FormApprover?.Id,
                Description: string.IsNullOrWhiteSpace(FormDescription) ? null : FormDescription.Trim(),
                RequestDate: FormDate?.ToUnixTimeMilliseconds(),
                SubmitImmediately: true));
            ShowForm = false;
            Load();
            Status = "Talep oluşturuldu ve iletildi.";
        }
        catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
    }

    // ════════════════════ ONAY / DURUM ════════════════════
    [RelayCommand]
    private async System.Threading.Tasks.Task Submit() => await Act(id => DesktopServices.Requests.Submit(_session, id), "Talep gönderildi.", null);

    [RelayCommand]
    private async System.Threading.Tasks.Task Approve()
        => await Act(id => DesktopServices.Requests.Approve(_session, id), "Talep onaylandı.",
            $"\"{Selected?.DocNo}\" talebini ONAYLAMAK istiyor musunuz?");

    [RelayCommand]
    private async System.Threading.Tasks.Task Reject()
    {
        if (Selected is null) { Status = "Talep seçin."; return; }
        if (string.IsNullOrWhiteSpace(RejectReason)) { Status = "Ret gerekçesi zorunlu."; return; }
        await Act(id => DesktopServices.Requests.Reject(_session, id, RejectReason.Trim()), "Talep reddedildi.",
            $"\"{Selected.DocNo}\" talebini REDDETMEK istiyor musunuz?");
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task Cancel()
        => await Act(id => DesktopServices.Requests.Cancel(_session,
            id, string.IsNullOrWhiteSpace(RejectReason) ? null : RejectReason.Trim()), "Talep iptal edildi.",
            "Bu talep iptal edilsin mi?");

    private async System.Threading.Tasks.Task Act(Action<string> action, string ok, string? confirm)
    {
        if (Selected is null) { Status = "Talep seçin."; return; }
        if (confirm is not null && !await ConfirmService.AskAsync(confirm, "Onay")) return;
        var id = Selected.Id;
        try { action(id); RejectReason = ""; Load(); Status = ok; }
        catch (Exception ex) { Status = "İşlem başarısız: " + ex.Message; }
    }
}

/// <summary>Yeni talep formundaki kalem (miktar düzenlenebilir).</summary>
public sealed partial class ReqItemLine : ObservableObject
{
    public string MaterialId { get; }
    public string Code { get; }
    public string Name { get; }
    [ObservableProperty] private decimal _quantity;
    public string? VehicleId { get; }
    public string? VehicleDisplay { get; }

    public ReqItemLine(string materialId, string code, string name, decimal quantity, string? vehicleId, string? vehicleDisplay)
    {
        MaterialId = materialId; Code = code; Name = name; _quantity = quantity;
        VehicleId = vehicleId; VehicleDisplay = vehicleDisplay;
    }

    public string DisplayName => $"{Code} - {Name}";
    public string VehicleText => string.IsNullOrEmpty(VehicleDisplay) ? "—" : VehicleDisplay!;
}

public sealed record RequestRow(string Id, string DocNo, RequestStatus Status, long RequestDate, int ItemCount, string? Description)
{
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(RequestDate).LocalDateTime.ToString("dd.MM.yyyy");
    public string DescriptionDisplay => string.IsNullOrWhiteSpace(Description) ? "—" : Description!;
    public string StatusText => StatusLabel(Status);
    public BadgeKind StatusKind => Status switch
    {
        RequestStatus.Pending => BadgeKind.Warning,
        RequestStatus.Approved => BadgeKind.Success,
        RequestStatus.Rejected => BadgeKind.Danger,
        _ => BadgeKind.Neutral,
    };

    public static string StatusLabel(RequestStatus s) => s switch
    {
        RequestStatus.Draft => "Taslak",
        RequestStatus.Pending => "Beklemede",
        RequestStatus.Approved => "Onaylı",
        RequestStatus.Rejected => "Reddedildi",
        RequestStatus.Cancelled => "İptal",
        _ => s.ToString(),
    };
}
