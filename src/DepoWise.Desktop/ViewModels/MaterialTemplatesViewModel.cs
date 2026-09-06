using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Malzeme Şablonları (masaüstü yönetim ekranı — kullanıcı isteği 2026-08-08). Şablon altyapısı zaten vardı
/// (material_templates tablosu + MaterialTemplateService + "material_templates" yetkisi + Şablonlu/Şablon-dışı
/// raporları); masaüstünde YALNIZ yönetim ekranı eksikti (web'de vardı) → Araç Genel Tanım ekranının AYNI deseni.
/// Şablon seçimi Malzemeler formundadır; burada şablonlar oluşturulur/düzenlenir/silinir.
///
/// Görünürlük kuralı servistedir: admin şablonu firmada herkese (is_global), diğer kullanıcının şablonu yalnız
/// kendisine görünür. Yetki: material_templates (View/Create/Edit/Delete) — deny-by-default korunur.
/// </summary>
public sealed partial class MaterialTemplatesViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    // FAZ 3c — şablon fiyatı, malzeme birim fiyatının kaynağıdır; aynı karardan (FieldAccess) geçer.
    // Gerçek kapı MaterialTemplateService'tedir; bu bayrak yalnız alanı hiç çizmemek içindir.
    /// <summary>Şablon birim fiyatı bu kullanıcıya açık mı?</summary>
    public bool FiyatGorunur => DepoWise.Infrastructure.Materials.MaterialService.FiyatGorunur(_session);

    public ObservableCollection<MaterialTemplateRow> Items { get; } = new();

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string? _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;

    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private MaterialTemplateRow? _selected;
    public bool HasSelection => Selected != null;

    [ObservableProperty] private bool _showAdd;

    // ── Form alanları (şablonun taşıdığı alanlar) ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyPropertyChangedFor(nameof(HasNameError))]
    private string _newName = "";
    [ObservableProperty] private string _newCode = "";
    [ObservableProperty] private string? _newType;
    [ObservableProperty] private decimal _newMinStock;
    [ObservableProperty] private decimal _newUnitPrice;
    [ObservableProperty] private string _newCurrency = "TRY";
    [ObservableProperty] private string _newDescription = "";

    /// <summary>Malzeme formundaki ile AYNI tür seçenekleri (tutarlılık).</summary>
    public ObservableCollection<string> TypeOptions { get; } = new() { "Yedek Parça", "Sarf Malzeme", "Hammadde", "Lastik", "Diğer" };
    public ObservableCollection<string> CurrencyOptions { get; } = new() { "TRY", "USD", "EUR" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyPropertyChangedFor(nameof(HasNameError))]
    private bool _triedSave;
    public string? NameError => TriedSave && string.IsNullOrWhiteSpace(NewName) ? "Ad zorunlu." : null;
    public bool HasNameError => NameError != null;

    // ── Tanım listeleri (malzeme formundakilerin aynısı) ──
    public ObservableCollection<LookupItem> Categories { get; } = new();
    public ObservableCollection<LookupItem> Units { get; } = new();
    public ObservableCollection<LookupItem> Brands { get; } = new();
    public ObservableCollection<LookupItem> Suppliers { get; } = new();
    [ObservableProperty] private LookupItem? _selCategory;
    [ObservableProperty] private LookupItem? _selUnit;
    [ObservableProperty] private LookupItem? _selBrand;
    [ObservableProperty] private LookupItem? _selSupplier;
    private bool _lookupsLoaded;

    public bool CanWrite => AccessControl.Can(_session, "material_templates", PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, "material_templates", PermissionAction.Edit);
    public bool CanDelete => AccessControl.Can(_session, "material_templates", PermissionAction.Delete);
    public string? AddButtonText => CanWrite ? "Yeni Şablon" : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    [NotifyPropertyChangedFor(nameof(FormTitle))]
    private string? _editId;

    /// <summary>KLT-01d DÜZENLEME KİLİDİ: form açılırken okunan şablon sürümü.
    /// Kaydederken geri gönderilir; arada başka yönetici kaydettiyse ConcurrencyException atılır.
    /// 0 = yeni kayıt / bilinmiyor → kontrol yapılmaz.</summary>
    private long _editVersion;
    public bool IsEditMode => EditId != null;
    public string FormTitle => IsEditMode ? "ŞABLON DÜZENLE" : "YENİ ŞABLON";

    // ══════════ B-4 (PRT-01 Grup 2b, 2026-08-10): UYUMLU ARAÇLAR — artık MASAÜSTÜNDE DE YÖNETİLİYOR ══════════
    // Web'de (MaterialTemplates.razor) zaten yönetilebiliyordu; masaüstünde yalnız KORUNUYORDU
    // (_editCompatibleVehicleIds ile geri gönderiliyordu). Desen: MaterialsViewModel'deki VehiclePick
    // çoklu seçim listesi — arama + tümünü seç/temizle. Yeni DB yapısı, migration veya model değişikliği YOK;
    // aynı virgülle ayrık compatible_vehicle_ids alanı kullanılır.
    // Firma izolasyonu servis tarafında SanitizeVehicleIds ile ayrıca güvenceye alınmıştır.
    public ObservableCollection<VehiclePick> VehiclePicks { get; } = new();
    public ObservableCollection<VehiclePick> FilteredVehiclePicks { get; } = new();
    [ObservableProperty] private string _vehicleSearch = "";

    partial void OnVehicleSearchChanged(string value) => RebuildFilteredVehicles();

    private void LoadVehiclePicks()
    {
        VehiclePicks.Clear();
        try { foreach (var v in DesktopServices.Vehicles.List(_session)) VehiclePicks.Add(new VehiclePick(v.Id, v.InternalCode, v.Plate ?? "")); }
        catch { /* araç yoksa sessiz — şablon yine kaydedilebilir */ }
        RebuildFilteredVehicles();
    }

    private void RebuildFilteredVehicles()
    {
        FilteredVehiclePicks.Clear();
        var t = VehicleSearch?.Trim();
        foreach (var p in VehiclePicks)
            if (string.IsNullOrEmpty(t)
                || p.Code.Contains(t, StringComparison.OrdinalIgnoreCase)
                || p.Plate.Contains(t, StringComparison.OrdinalIgnoreCase))
                FilteredVehiclePicks.Add(p);
    }

    [RelayCommand] private void SelectAllVehicles() { foreach (var p in FilteredVehiclePicks) p.IsSelected = true; }
    [RelayCommand] private void ClearVehicles() { foreach (var p in FilteredVehiclePicks) p.IsSelected = false; }

    /// <summary>Seçili araçlar → virgülle ayrık id listesi (alanın mevcut biçimi; değiştirilmedi).</summary>
    private string? SelectedVehicleIds()
    {
        var ids = VehiclePicks.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        return ids.Count == 0 ? null : string.Join(",", ids);
    }

    /// <summary>Şablonun mevcut bağını seçim kutularına dağıtır (düzenleme).</summary>
    private void ApplyVehicleSelection(string? csv)
    {
        var set = (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var p in VehiclePicks) p.IsSelected = set.Contains(p.Id);
    }

    // ══════════ B-5 (PRT-01 Grup 2b, 2026-08-10): ŞABLON FOTOĞRAFLARI — masaüstüne eklendi ══════════
    // Web'de (MaterialTemplates.razor + /api/templates/material/{id}/photos) zaten vardı; masaüstünde yoktu.
    // MEVCUT altyapı kullanılır: FileService + Storage, varlık adı "material_template" — web ucunun
    // TplEntity("material") ile ürettiği ADIN AYNISI, yani iki platform AYNI kayıtları görür.
    // Yeni fotoğraf sistemi, migration, dependency veya API tasarımı YOK.
    // Desen: MaterialsViewModel'deki Photos/DetailPhotos akışının aynısı.
    public ObservableCollection<PhotoStage> Photos { get; } = new();
    public ObservableCollection<DetailPhoto> DetailPhotos { get; } = new();

    private const string PhotoEntity = "material_template";

    [RelayCommand]
    private async Task AddPhotos()
    {
        var picked = await FilePickerService.PickImagesAsync();
        // Desteklenmeyen biçim (yalnız JPEG/PNG) seçilirse uyarır, yalnız geçerlileri forma ekler.
        var valid = await PhotoPickHelper.ValidateAndWarnAsync(picked);
        foreach (var p in valid) Photos.Add(new PhotoStage(p));
    }

    [RelayCommand] private void RemovePhoto(PhotoStage? p) { if (p is not null) Photos.Remove(p); }
    [RelayCommand] private void OpenPhoto(Avalonia.Media.Imaging.Bitmap? b) => PhotoViewer.Show(b);

    /// <summary>Kayıtlı fotoğrafı sil (onaylı) — yalnız düzenleme modunda anlamlı.</summary>
    [RelayCommand]
    private async Task DeleteDetailPhoto(DetailPhoto? p)
    {
        if (p is null || EditId is null) return;
        if (!CanEdit) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync("Bu fotoğraf silinsin mi?", "Fotoğraf Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try { DesktopServices.Files.DeletePhoto(_session, p.FileId); LoadDetailPhotos(EditId); Status = "Fotoğraf silindi."; }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }

    private void SaveStagedPhotos(string templateId)
    {
        foreach (var ph in Photos)
        {
            try
            {
                var bytes = System.IO.File.ReadAllBytes(ph.LocalPath);
                DesktopServices.Files.SavePhoto(_session, PhotoEntity, templateId, System.IO.Path.GetFileName(ph.LocalPath), null, bytes);
            }
            catch (Exception ex) { Status = "Foto kaydedilemedi: " + ex.Message; }
        }
    }

    private void LoadDetailPhotos(string templateId)
    {
        DetailPhotos.Clear();
        try
        {
            foreach (var f in DesktopServices.Files.GetPhotos(_session, PhotoEntity, templateId))
            {
                var bytes = DesktopServices.Storage.Read(f.StorageKey);
                DetailPhotos.Add(new DetailPhoto(f.Id, new Avalonia.Media.Imaging.Bitmap(new System.IO.MemoryStream(bytes))));
            }
        }
        catch { /* foto yoksa sessiz */ }
    }

    public MaterialTemplatesViewModel(SessionContext session)
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
            foreach (var t in DesktopServices.MaterialTemplates.List(_session, string.IsNullOrWhiteSpace(Search) ? null : Search.Trim()))
                Items.Add(t);
            Status = $"{Items.Count} şablon";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        Selected = null;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRows));
    }

    [RelayCommand]
    private async Task Add()
    {
        TriedSave = true;
        bool editing = IsEditMode;
        if (editing ? !CanEdit : !CanWrite) { Status = "Yetki yok."; return; }
        if (string.IsNullOrWhiteSpace(NewName)) { Status = "Ad zorunlu."; return; }
        if (!await ConfirmService.AskAsync(editing ? "Şablon güncellensin mi?" : "Yeni şablon kaydedilsin mi?", "Kaydet")) return;

        var dto = new NewMaterialTemplate(
            Name: NewName.Trim(),
            Code: string.IsNullOrWhiteSpace(NewCode) ? null : NewCode.Trim(),
            Type: string.IsNullOrWhiteSpace(NewType) ? null : NewType,
            CategoryId: SelCategory?.Id, UnitId: SelUnit?.Id,
            BrandId: SelBrand?.Id, SupplierId: SelSupplier?.Id,
            MinStock: NewMinStock, UnitPrice: NewUnitPrice,
            Currency: string.IsNullOrWhiteSpace(NewCurrency) ? "TRY" : NewCurrency,
            Description: string.IsNullOrWhiteSpace(NewDescription) ? null : NewDescription.Trim(),
            // B-4: artık bu ekranda YÖNETİLİYOR — seçili araçlar gönderilir (eskiden mevcut bağ körlemesine korunurdu).
            CompatibleVehicleIds: SelectedVehicleIds());

        try
        {
            // B-5: bekleyen fotoğraflar kayıt BAŞARILI olduktan sonra, oluşan/güncellenen şablona yüklenir.
            if (editing)
            {
                DesktopServices.MaterialTemplates.Update(_session, EditId!, dto, expectedVersion: _editVersion > 0 ? _editVersion : null);
                SaveStagedPhotos(EditId!);
                Clear(); Load(); Status = "Şablon güncellendi.";
            }
            else
            {
                var newId = DesktopServices.MaterialTemplates.Create(_session, dto);
                SaveStagedPhotos(newId);
                Clear(); Load(); Status = "Şablon eklendi.";
            }
        }
        catch (ConcurrencyException)
        {
            // KLT-01d DÜZENLEME KİLİDİ: şablon, form açıldıktan sonra başkası tarafından değiştirilmiş.
            // Form KAPATILMAZ — kullanıcının 12 alanlık girdisi korunur, kararı kendisi verir.
            Status = "Bu şablon siz formu açtıktan sonra başka bir yönetici tarafından değiştirildi. "
                   + "Değişiklikleriniz kaydedilmedi. Vazgeçip şablonu yeniden açın ve tekrar deneyin.";
        }
        catch (Exception ex) { Status = editing ? "Güncellenemedi: " + ex.Message : "Eklenemedi: " + ex.Message; }
    }

    /// <summary>Seçili şablonu düzenleme modunda forma yükler. Onay sorar.</summary>
    [RelayCommand]
    private async Task BeginEdit()
    {
        if (Selected is null) return;
        if (!CanEdit) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync("Bu şablonu düzenlemek istiyor musunuz?", "Düzenle")) return;

        LoadLookups();
        var t = DesktopServices.MaterialTemplates.Get(_session, Selected.Id);
        if (t is null) { Status = "Şablon bulunamadı."; return; }
        EditId = t.Id;
        _editVersion = t.Version;   // KLT-01d: düzenleme kilidi jetonu
        NewName = t.Name; NewCode = t.Code ?? ""; NewType = t.Type;
        NewMinStock = t.MinStock; NewUnitPrice = t.UnitPrice;
        NewCurrency = string.IsNullOrWhiteSpace(t.Currency) ? "TRY" : t.Currency;
        NewDescription = t.Description ?? "";
        SelCategory = Categories.FirstOrDefault(x => x.Id == t.CategoryId);
        SelUnit = Units.FirstOrDefault(x => x.Id == t.UnitId);
        SelBrand = Brands.FirstOrDefault(x => x.Id == t.BrandId);
        SelSupplier = Suppliers.FirstOrDefault(x => x.Id == t.SupplierId);
        ApplyVehicleSelection(t.CompatibleVehicleIds);   // B-4: mevcut bağı seçim kutularına dağıt
        Photos.Clear(); LoadDetailPhotos(Selected.Id);   // B-5: kayıtlı fotoğrafları göster
        TriedSave = false; ShowAdd = true;
    }

    [RelayCommand]
    private void ToggleAdd()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        ShowAdd = !ShowAdd;
        if (ShowAdd) LoadLookups();
    }

    [RelayCommand]
    private void Clear()
    {
        NewName = ""; NewCode = ""; NewType = null; NewDescription = "";
        NewMinStock = 0m; NewUnitPrice = 0m; NewCurrency = "TRY";
        SelCategory = null; SelUnit = null; SelBrand = null; SelSupplier = null;
        foreach (var p in VehiclePicks) p.IsSelected = false;   // B-4
        VehicleSearch = "";
        Photos.Clear(); DetailPhotos.Clear();   // B-5
        EditId = null; _editVersion = 0; TriedSave = false; ShowAdd = false;
    }

    [RelayCommand]
    private async Task RequestDelete()
    {
        if (!CanDelete) { Status = "Yetki yok."; return; }
        if (Selected is null) return;
        if (!await ConfirmService.AskAsync($"'{Selected.Name}' şablonu silinsin mi?", "Şablon Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try { DesktopServices.MaterialTemplates.Delete(_session, Selected.Id); Selected = null; Load(); Status = "Şablon silindi."; }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }

    private void LoadLookups()
    {
        if (_lookupsLoaded) return;
        try
        {
            Categories.Clear(); foreach (var x in DesktopServices.Lookups.ListCategories(_session)) Categories.Add(x);
            Units.Clear(); foreach (var x in DesktopServices.Lookups.List(_session, "units")) Units.Add(x);
            Brands.Clear(); foreach (var x in DesktopServices.Lookups.ListBrands(_session, "material")) Brands.Add(x);
            Suppliers.Clear(); foreach (var x in DesktopServices.Lookups.List(_session, "suppliers")) Suppliers.Add(x);
            LoadVehiclePicks();   // B-4: uyumlu araç seçim listesi
            _lookupsLoaded = true;
        }
        catch (Exception ex) { Status = "Tanımlar yüklenemedi: " + ex.Message; }
    }
}
