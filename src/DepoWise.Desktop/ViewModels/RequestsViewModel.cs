using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public bool CanApproveButton => AccessControl.Can(_session, "request_approval", PermissionAction.Edit); // Talep Onaylama ayrı yetki

    public ObservableCollection<RequestRow> Items { get; } = new();
    public ObservableCollection<RequestRow> PendingItems { get; } = new();
    public ObservableCollection<RequestItemRow> DetailItems { get; } = new();
    public ObservableCollection<string> History { get; } = new();
    public ObservableCollection<string> Filters { get; } = new() { "Tümü", "Taslak", "Beklemede", "Onaylı", "Reddedildi", "İptal" };

    // Lookup'lar (form)
    public ObservableCollection<LookupItem> Sites { get; } = new();
    public ObservableCollection<LookupItem> Personnel { get; } = new();
    public ObservableCollection<VehicleListRow> Vehicles { get; } = new();
    public Func<string, CancellationToken, Task<IEnumerable<object>>> PersonnelPopulator => SearchPopulator.For(() => Personnel, p => p.Name);
    public Func<string, CancellationToken, Task<IEnumerable<object>>> VehiclePopulator => SearchPopulator.For(() => Vehicles, v => v.Display);
    private bool _lookupsLoaded;

    private const string LogoKey = "requests.company_logo";
    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private string? _companyLogoPath;
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
    [NotifyPropertyChangedFor(nameof(CanEditSelected))]
    private RequestRow? _selected;

    public bool HasSelection => Selected != null;
    /// <summary>Onaylı talep düzenlenemez (kullanıcı kuralı). Diğer durumlar düzenlenebilir.</summary>
    public bool CanEditSelected => Selected is not null && Selected.Status != RequestStatus.Approved && CanWrite;
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
                Items.Add(new RequestRow(r.Id, r.DocNo, r.Status, r.RequestDate, r.ItemCount, r.Description, r.OperationStatusDb, r.PriorityDb));

            PendingItems.Clear();
            foreach (var r in DesktopServices.Requests.List(_session, RequestStatus.Pending))
                PendingItems.Add(new RequestRow(r.Id, r.DocNo, r.Status, r.RequestDate, r.ItemCount, r.Description, r.OperationStatusDb, r.PriorityDb));

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
            CompanyLogoPath = DesktopServices.Settings.Get(_session.CompanyId, LogoKey);
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
            foreach (var (from, to, reason) in DesktopServices.Requests.GetHistory(_session, value.Id))
                History.Add($"{(from is null ? "—" : RequestRow.StatusLabel(from.Value))} → {RequestRow.StatusLabel(to)}"
                            + (string.IsNullOrWhiteSpace(reason) ? "" : $" ({reason})"));
        }
        catch (Exception ex) { Status = "Detay yüklenemedi: " + ex.Message; }
    }

    // ════════════════════ YENİ / DÜZENLE TALEP FORMU ════════════════════
    [ObservableProperty] private bool _showForm;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle))]
    private string? _editId;
    public string FormTitle => EditId is null ? "YENİ TALEP" : "TALEP DÜZENLE";
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
        EditId = null; _editVersion = null;   // düzenleme kilidi: yeni kayıtta sürüm yok
        FormSite = null; FormRequester = null; FormWarehouse = null; FormApprover = null;
        FormDate = DateTimeOffset.Now; FormDescription = ""; FormError = null;
        FormItems.Clear();
        MaterialSearch = ""; PickedMaterial = null; NewItemQty = 1; NewItemVehicle = null; ItemError = null;
        IsAddingPersonnel = false; IsAddingSite = false;
        RefreshMaterials();
        ShowForm = true;
    }

    [RelayCommand]
    private void CancelForm() { ShowForm = false; EditId = null; _editVersion = null; }

    /// <summary>DÜZENLEME KİLİDİ: formun açıldığı andaki talep sürümü (bkz. <see cref="BeginEditRequest"/>).
    /// Kaydederken geri gönderilir; talep arada değiştiyse sessizce ezmek yerine uyarı verilir.</summary>
    private long? _editVersion;

    /// <summary>Seçili talebi forma yükler (onaylı değilse). Belge no/durum korunur, kalemler tam değiştirilir.</summary>
    [RelayCommand]
    private void BeginEditRequest()
    {
        if (Selected is null) { Status = "Talep seçin."; return; }
        if (Selected.Status == RequestStatus.Approved) { Status = "Onaylanmış talep düzenlenemez."; return; }
        if (!CanWrite) { Status = "Yetki yok."; return; }
        try
        {
            var d = DesktopServices.Requests.GetForEdit(_session, Selected.Id);
            EditId = Selected.Id; _editVersion = d.Version;   // düzenleme kilidi
            FormSite = Sites.FirstOrDefault(x => x.Id == d.BranchId);
            FormRequester = Personnel.FirstOrDefault(x => x.Id == d.RequesterId);
            FormWarehouse = Personnel.FirstOrDefault(x => x.Id == d.WarehouseId);
            FormApprover = Personnel.FirstOrDefault(x => x.Id == d.ApproverId);
            FormDescription = d.Description ?? "";
            FormDate = DateTimeOffset.FromUnixTimeMilliseconds(d.RequestDate);
            FormItems.Clear();
            foreach (var it in d.Items)
            {
                var disp = it.VehicleCode is null ? null
                    : it.VehiclePlate is null ? it.VehicleCode : $"{it.VehicleCode} - {it.VehiclePlate}";
                FormItems.Add(new ReqItemLine(it.MaterialId, it.Code, it.Name, it.Quantity, it.VehicleId, disp));
            }
            MaterialSearch = ""; PickedMaterial = null; NewItemQty = 1; NewItemVehicle = null; ItemError = null; FormError = null;
            IsAddingPersonnel = false; IsAddingSite = false;
            RefreshMaterials();
            ShowForm = true;
        }
        catch (Exception ex) { Status = "Düzenlenemedi: " + ex.Message; }
    }

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
    // NOT (2026-08-09): "Yeni şantiye ekle" komutu KALDIRILDI. Şube/Şantiye tanımları admin-kısıtlı
    // "branches" modülüne aittir; yalnız Şube / Şantiye Tanımları ekranından oluşturulur. Kilit ayrıca
    // servis katmanındadır (LookupService.EnsureWritableTable) → arayüz atlansa bile oluşturma olmaz.

    [RelayCommand]
    private async System.Threading.Tasks.Task SaveRequest()
    {
        FormError = null;
        if (!CanWrite) { FormError = "Yetki yok."; return; }
        if (FormSite is null) { FormError = "Şantiye seçilmesi zorunludur."; return; }
        if (FormItems.Count == 0) { FormError = "En az bir talep kalemi eklenmelidir."; return; }
        var editing = EditId is not null;
        if (!await ConfirmService.AskAsync(
                editing ? "Talep güncellensin mi? (kalemler tam değiştirilir)"
                        : "Talep oluşturulup yöneticiye iletilecek. Onaylıyor musunuz?",
                editing ? "Talebi Güncelle" : "Talep Oluştur"))
            return;
        try
        {
            var items = FormItems.Select(l => new RequestItemInput(l.MaterialId, l.Quantity, l.VehicleId)).ToList();
            var dto = new NewRequest(
                Items: items,
                BranchId: FormSite.Id,
                RequesterId: FormRequester?.Id,
                WarehouseId: FormWarehouse?.Id,
                ApproverId: FormApprover?.Id,
                Description: string.IsNullOrWhiteSpace(FormDescription) ? null : FormDescription.Trim(),
                RequestDate: FormDate?.ToUnixTimeMilliseconds(),
                SubmitImmediately: true);

            if (editing) DesktopServices.Requests.Update(_session, EditId!, dto, _editVersion);
            else DesktopServices.Requests.Create(_session, dto);

            ShowForm = false; EditId = null; _editVersion = null;
            Load();
            Status = editing ? "Talep güncellendi." : "Talep oluşturuldu ve iletildi.";
        }
        catch (DepoWise.Application.Security.ConcurrencyException ex)
        {
            // Talep biz düzenlerken değişti. Yazdıklarını KAYBETME: karar kullanıcının.
            FormError = ex.Message;
            if (await ConfirmService.AskAsync(
                    ex.Message + "\n\nTalebin güncel hâlini yüklemek ister misiniz? " +
                    "(\"Formda kal\" derseniz yazdıklarınız durur, kopyalayıp tekrar uygulayabilirsiniz.)",
                    "Kayıt değişti", okText: "Kaydı yenile", cancelText: "Formda kal"))
            {
                ShowForm = false; EditId = null; _editVersion = null; Load();
            }
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

    // ════════════════════ FİRMA LOGOSU ════════════════════
    /// <summary>Firma logosu seçtirir, app klasörüne kopyalar ve ayarda kalıcı saklar (değişmedikçe seçili kalır).</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task PickLogo()
    {
        var picked = await FilePickerService.PickImagesAsync(false);
        var src = picked.FirstOrDefault();
        if (string.IsNullOrEmpty(src)) return;

        // Yükleme anında kontrol: çözülebilir mi + saydam arka plan var mı?
        var (readable, opaque, err) = InspectLogo(src);
        if (!readable)
        {
            await ConfirmService.AskAsync(
                $"Logo okunamadı ({err}).\n\nÖnerilen: arka planı SAYDAM (transparan) PNG, yaklaşık 300×120 px.",
                "Logo Hatası", "Tamam", "Tamam");
            return;
        }
        if (opaque)
        {
            var keep = await ConfirmService.AskAsync(
                "Seçtiğiniz logonun ŞEFFAF (saydam) arka planı yok; PDF başlığındaki lacivert bantta logonun etrafı BEYAZ/dolu görünür.\n\n" +
                "Olması gereken:\n• Arka planı saydam (transparan) PNG\n• Yaklaşık 300×120 px (yatay)\n• Kenarlarda beyaz dolgu olmamalı\n\n" +
                "Yine de bu logo kullanılsın mı?",
                "Logo Uyarısı", "Yine de Kullan", "Vazgeç");
            if (!keep) return;
        }
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alpnex", "branding");
            System.IO.Directory.CreateDirectory(dir);
            var dest = System.IO.Path.Combine(dir, "company-logo" + System.IO.Path.GetExtension(src));
            System.IO.File.Copy(src, dest, overwrite: true);
            DesktopServices.Settings.Set(_session.CompanyId, LogoKey, dest, _session.UserId);
            CompanyLogoPath = dest;
            Status = "Firma logosu güncellendi.";
        }
        catch (Exception ex) { Status = "Logo eklenemedi: " + ex.Message; }
    }

    /// <summary>Logoyu çözer ve saydam arka planı olup olmadığını örnekleyerek kontrol eder.</summary>
    private static (bool Readable, bool Opaque, string? Error) InspectLogo(string path)
    {
        try
        {
            using var bmp = SkiaSharp.SKBitmap.Decode(path);
            if (bmp is null || bmp.Width == 0) return (false, false, "görsel çözülemedi");
            bool hasAlpha = false;
            int stepX = Math.Max(1, bmp.Width / 80);
            int stepY = Math.Max(1, bmp.Height / 80);
            for (int y = 0; y < bmp.Height && !hasAlpha; y += stepY)
                for (int x = 0; x < bmp.Width; x += stepX)
                    if (bmp.GetPixel(x, y).Alpha < 250) { hasAlpha = true; break; }
            return (true, !hasAlpha, null);
        }
        catch (Exception ex) { return (false, false, ex.Message); }
    }

    // ════════════════════ PDF ÇIKTI ════════════════════
    [RelayCommand]
    private async System.Threading.Tasks.Task ExportPdf() => await ExportPdfCore(economic: false);

    [RelayCommand]
    private async System.Threading.Tasks.Task ExportEconomicPdf() => await ExportPdfCore(economic: true);

    private async System.Threading.Tasks.Task ExportPdfCore(bool economic)
    {
        if (Selected is null) { Status = "Talep seçin."; return; }
        try
        {
            var d = DesktopServices.Requests.GetPdfData(_session, Selected.Id);
            var model = new RequestPdfModel(
                CompanyName: DesktopServices.Branding.CompanyName,
                DocNo: d.DocNo,
                RequestDate: DateTimeOffset.FromUnixTimeMilliseconds(d.RequestDate).LocalDateTime.ToString("dd.MM.yyyy"),
                Status: RequestRow.StatusLabel(d.Status),
                BranchName: d.BranchName, RequesterName: d.RequesterName, WarehouseName: d.WarehouseName,
                ApproverName: d.ApproverName, Description: d.Description,
                Items: d.Items.Select(i => new RequestPdfItem(i.Code, i.Name, i.Unit, i.Quantity, i.VehicleCode, i.VehicleChassis)).ToList(),
                LogoPath: CompanyLogoPath);

            byte[] bytes;
            try { bytes = DesktopServices.RequestPdf.Generate(model, economic); }
            catch when (!string.IsNullOrEmpty(model.LogoPath))
            {
                // Logo okunamadı/desteklenmiyor → logosuz üret (PDF yine de çıksın)
                bytes = DesktopServices.RequestPdf.Generate(model with { LogoPath = null }, economic);
                Status = "Uyarı: firma logosu okunamadı, logosuz PDF üretildi.";
            }
            var path = await FilePickerService.SavePdfAsync(d.DocNo + (economic ? "_ekonomik" : ""));
            if (string.IsNullOrEmpty(path)) return;
            await System.IO.File.WriteAllBytesAsync(path, bytes);
            FilePickerService.OpenFile(path);
            Status = "PDF kaydedildi: " + path;
        }
        catch (Exception ex) { Status = "PDF oluşturulamadı: " + ex.Message; }
    }

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

/// <summary><paramref name="OperationStatusDb"/> = OPERASYON durumu (onay durumundan AYRI; null → "—").
/// Sona eklendi → mevcut çağrılar bozulmaz (Faz 1, kullanıcı isteği 2026-08-08).</summary>
public sealed record RequestRow(string Id, string DocNo, RequestStatus Status, long RequestDate, int ItemCount, string? Description,
    string? OperationStatusDb = null, string PriorityDb = "normal")
{
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(RequestDate).LocalDateTime.ToString("dd.MM.yyyy");
    public string DescriptionDisplay => string.IsNullOrWhiteSpace(Description) ? "—" : Description!;

    // ── Operasyon Durumu (şartname madde 15) + renk (madde 16). Ortak kaynak: RequestOperationStatusInfo. ──
    public string OperationStatusText => RequestOperationStatusInfo.LabelOrDash(OperationStatusDb);
    public BadgeKind OperationStatusKind => ToBadge(RequestOperationStatusInfo.ColorOrNeutral(OperationStatusDb));
    public string PriorityText => RequestPriorityInfo.LabelOf(PriorityDb);
    public BadgeKind PriorityKind => ToBadge(RequestPriorityInfo.ColorOf(PriorityDb));

    /// <summary>Ortak renk anahtarı → masaüstü rozet türü (web kendi tarafında MudBlazor rengine eşler).</summary>
    private static BadgeKind ToBadge(string color) => color switch
    {
        "success" => BadgeKind.Success,
        "warning" => BadgeKind.Warning,
        "danger" => BadgeKind.Danger,
        "info" or "primary" => BadgeKind.Info,
        _ => BadgeKind.Neutral,
    };
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
