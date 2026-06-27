using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Desktop.Controls;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Malzemeler ekranı — liste + arama + yeni kayıt. MaterialService üzerine (SQLite).</summary>
public sealed partial class MaterialsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<MaterialRow> Items { get; } = new();

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string? _status;

    // Liste durumları (Faz 7a — boş/hata; yükleme senkron olduğundan kalıcı değil)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;

    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    // Yeni kayıt formu görünürlüğü + alanları
    [ObservableProperty] private bool _showAdd;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CodeError))]
    [NotifyPropertyChangedFor(nameof(HasCodeError))]
    private string _newCode = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyPropertyChangedFor(nameof(HasNameError))]
    private string _newName = "";

    [ObservableProperty] private decimal _newUnitPrice;
    [ObservableProperty] private decimal _newMinStock;
    [ObservableProperty] private decimal _newOpeningStock;
    [ObservableProperty] private string _newDescription = "";
    [ObservableProperty] private string? _newType = "Yedek Parça";

    // ── Paylaşılan tanımlar (Tanımlar/LookupService) — eski projeyle aynı bağlantı ──
    public ObservableCollection<string> TypeOptions { get; } = new() { "Yedek Parça", "Sarf Malzeme", "Hammadde", "Lastik", "Diğer" };
    public ObservableCollection<LookupItem> Categories { get; } = new();
    public ObservableCollection<LookupItem> SubCategories { get; } = new();
    public ObservableCollection<LookupItem> Units { get; } = new();
    public ObservableCollection<LookupItem> Brands { get; } = new();
    public ObservableCollection<LookupItem> Suppliers { get; } = new();

    [ObservableProperty] private LookupItem? _selectedCategory;
    [ObservableProperty] private LookupItem? _selectedSubCategory;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitError))]
    [NotifyPropertyChangedFor(nameof(HasUnitError))]
    private LookupItem? _selectedUnit;
    [ObservableProperty] private LookupItem? _selectedBrand;
    [ObservableProperty] private LookupItem? _selectedSupplier;

    private bool _lookupsLoaded;

    partial void OnSelectedCategoryChanged(LookupItem? value)
    {
        SelectedSubCategory = null;
        SubCategories.Clear();
        if (value is null) return;
        try { foreach (var sc in DesktopServices.Lookups.ListCategories(_session, value.Id)) SubCategories.Add(sc); }
        catch { /* alt kategori yoksa sessiz */ }
    }

    // ── Inline "+" yeni tanım ekleme ──
    [ObservableProperty] private bool _isAddingCategory;
    [ObservableProperty] private string _newCategoryName = "";
    [ObservableProperty] private bool _isAddingSubCategory;
    [ObservableProperty] private string _newSubCategoryName = "";
    [ObservableProperty] private bool _isAddingUnit;
    [ObservableProperty] private string _newUnitName = "";
    [ObservableProperty] private bool _isAddingBrand;
    [ObservableProperty] private string _newBrandName = "";
    [ObservableProperty] private bool _isAddingSupplier;
    [ObservableProperty] private string _newSupplierName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CodeError))]
    [NotifyPropertyChangedFor(nameof(HasCodeError))]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyPropertyChangedFor(nameof(HasNameError))]
    [NotifyPropertyChangedFor(nameof(UnitError))]
    [NotifyPropertyChangedFor(nameof(HasUnitError))]
    private bool _triedSave;

    public string? UnitError => TriedSave && SelectedUnit is null ? "Birim seçin." : null;
    public bool HasUnitError => UnitError != null;

    // Alan-bazlı doğrulama (mevcut iş kuralının görsel yansıması: kod+ad zorunlu)
    public string? CodeError => TriedSave && string.IsNullOrWhiteSpace(NewCode) ? "Kod zorunlu." : null;
    public bool HasCodeError => CodeError != null;
    public string? NameError => TriedSave && string.IsNullOrWhiteSpace(NewName) ? "Ad zorunlu." : null;
    public bool HasNameError => NameError != null;

    public bool CanWrite => AccessControl.Can(_session, "materials", PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, "materials", PermissionAction.Edit);
    public bool CanDelete => AccessControl.Can(_session, "materials", PermissionAction.Delete);
    public string? AddButtonText => CanWrite ? "Yeni Malzeme" : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    [NotifyPropertyChangedFor(nameof(FormTitle))]
    private string? _editId;
    public bool IsEditMode => EditId != null;
    public string FormTitle => IsEditMode ? "MALZEME DÜZENLE" : "YENİ MALZEME";
    [ObservableProperty] private bool _confirmDelete;

    public MaterialsViewModel(SessionContext session, bool openAdd = false)
    {
        _session = session;
        Load();
        if (openAdd && CanWrite) { ShowAdd = true; LoadLookups(); }
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            var page = DesktopServices.Materials.List(_session, new PageRequest { Limit = 200 },
                string.IsNullOrWhiteSpace(Search) ? null : Search.Trim());
            foreach (var m in page.Items)
            {
                var stock = DesktopServices.OpeningStock.GetBalance(_session, m.Id);
                Items.Add(new MaterialRow(m.Id, m.Code, m.Name, m.Type, m.UnitPrice, m.Currency, m.MinStock, stock));
            }
            Status = $"{Items.Count} kayıt";
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            Status = "Hata: " + ex.Message;
        }
        NotifyListState();
    }

    [RelayCommand]
    private async Task Add()
    {
        TriedSave = true;
        bool editing = IsEditMode;
        if (editing ? !CanEdit : !CanWrite) { Status = "Yetki yok."; return; }
        if (string.IsNullOrWhiteSpace(NewCode) || string.IsNullOrWhiteSpace(NewName))
        {
            Status = "Kod ve ad zorunlu."; return;
        }
        if (SelectedUnit is null) { Status = "Birim seçin."; return; }

        var confirmed = await ConfirmService.AskAsync(
            editing ? "Malzeme bilgileri güncellensin mi?" : "Yeni malzeme kaydedilsin mi?", "Kaydet");
        if (!confirmed) return;

        // Alt kategori seçiliyse en özgün olanı (alt) kullanılır; yoksa kategori.
        var categoryId = (SelectedSubCategory ?? SelectedCategory)?.Id;
        var typeVal = string.IsNullOrWhiteSpace(NewType) ? null : NewType;
        var descVal = string.IsNullOrWhiteSpace(NewDescription) ? null : NewDescription.Trim();

        if (editing)
        {
            try
            {
                DesktopServices.Materials.Update(_session, EditId!, new UpdateMaterial(
                    Code: NewCode.Trim(), Name: NewName.Trim(), Type: typeVal,
                    CategoryId: categoryId, UnitId: SelectedUnit.Id,
                    BrandId: SelectedBrand?.Id, SupplierId: SelectedSupplier?.Id,
                    MinStock: NewMinStock, UnitPrice: NewUnitPrice, Description: descVal));
                Clear(); Load(); Status = "Malzeme güncellendi.";
            }
            catch (Exception ex) { Status = "Güncellenemedi: " + ex.Message; }
            return;
        }

        try
        {
            var id = DesktopServices.Materials.Create(_session, new NewMaterial(
                Code: NewCode.Trim(), Name: NewName.Trim(),
                Type: string.IsNullOrWhiteSpace(NewType) ? null : NewType,
                CategoryId: categoryId, UnitId: SelectedUnit.Id,
                BrandId: SelectedBrand?.Id, SupplierId: SelectedSupplier?.Id,
                UnitPrice: NewUnitPrice, MinStock: NewMinStock, Currency: "TRY",
                Description: string.IsNullOrWhiteSpace(NewDescription) ? null : NewDescription.Trim()));

            // Açılış stoğu > 0 ise stok hareketi (eski projeyle aynı davranış)
            if (NewOpeningStock > 0)
                DesktopServices.OpeningStock.RecordOpening(_session, id, NewOpeningStock, Guid.NewGuid().ToString("N"));

            // Uyumlu araçlar (seçiliyse)
            var compatIds = VehiclePicks.Where(p => p.IsSelected).Select(p => p.Id).ToList();
            if (compatIds.Count > 0)
                DesktopServices.Materials.SetCompatibleVehicles(_session, id, compatIds);

            // Muadiller
            foreach (var eq in ChosenEquivalents)
                DesktopServices.Materials.AddEquivalent(_session, id, eq.Id);

            Clear();
            Load();
            Status = "Malzeme eklendi.";
        }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }

    /// <summary>Yeni kayıt formunu aç/kapat (sunum durumu). Açılırken paylaşılan tanımları yükler.</summary>
    [RelayCommand]
    private void ToggleAdd()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        ShowAdd = !ShowAdd;
        if (ShowAdd) LoadLookups();
    }

    /// <summary>İptal — form alanlarını temizler ve kapatır (sunum durumu; iş mantığı yok).</summary>
    [RelayCommand]
    private void Clear()
    {
        NewCode = ""; NewName = ""; NewUnitPrice = 0; NewMinStock = 0;
        NewOpeningStock = 0; NewDescription = ""; NewType = "Yedek Parça";
        SelectedCategory = null; SelectedSubCategory = null; SelectedUnit = null;
        SelectedBrand = null; SelectedSupplier = null;
        IsAddingCategory = IsAddingSubCategory = IsAddingUnit = IsAddingBrand = IsAddingSupplier = false;
        foreach (var p in VehiclePicks) p.IsSelected = false;
        ChosenEquivalents.Clear(); EquivalentResults.Clear(); EquivalentSearch = "";
        EditId = null; ConfirmDelete = false;
        TriedSave = false; ShowAdd = false;
    }

    // ── Paylaşılan tanımları yükle (LookupService) ──
    private void LoadLookups()
    {
        if (_lookupsLoaded) return;
        try
        {
            Categories.Clear(); foreach (var c in DesktopServices.Lookups.ListCategories(_session)) Categories.Add(c);
            Units.Clear(); foreach (var u in DesktopServices.Lookups.List(_session, "units")) Units.Add(u);
            Brands.Clear(); foreach (var b in DesktopServices.Lookups.ListBrands(_session, "material")) Brands.Add(b);
            Suppliers.Clear(); foreach (var sp in DesktopServices.Lookups.List(_session, "suppliers")) Suppliers.Add(sp);
            LoadVehiclePicks();
            _lookupsLoaded = true;
        }
        catch (Exception ex) { Status = "Tanımlar yüklenemedi: " + ex.Message; }
    }

    // ── Inline "+" komutları (eklenen tanım hem DB'ye yazılır hem seçili olur) ──
    [RelayCommand] private void StartAddCategory() { IsAddingCategory = true; NewCategoryName = ""; }
    [RelayCommand] private void CancelAddCategory() { IsAddingCategory = false; NewCategoryName = ""; }
    [RelayCommand]
    private void ConfirmAddCategory()
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName)) return;
        try { var id = DesktopServices.Lookups.AddCategory(_session, NewCategoryName.Trim());
            var item = new LookupItem(id, NewCategoryName.Trim()); Categories.Add(item); SelectedCategory = item;
            IsAddingCategory = false; NewCategoryName = ""; }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }

    [RelayCommand] private void StartAddSubCategory() { if (SelectedCategory is null) { Status = "Önce kategori seçin."; return; } IsAddingSubCategory = true; NewSubCategoryName = ""; }
    [RelayCommand] private void CancelAddSubCategory() { IsAddingSubCategory = false; NewSubCategoryName = ""; }
    [RelayCommand]
    private void ConfirmAddSubCategory()
    {
        if (string.IsNullOrWhiteSpace(NewSubCategoryName) || SelectedCategory is null) return;
        try { var id = DesktopServices.Lookups.AddCategory(_session, NewSubCategoryName.Trim(), SelectedCategory.Id);
            var item = new LookupItem(id, NewSubCategoryName.Trim()); SubCategories.Add(item); SelectedSubCategory = item;
            IsAddingSubCategory = false; NewSubCategoryName = ""; }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }

    [RelayCommand] private void StartAddUnit() { IsAddingUnit = true; NewUnitName = ""; }
    [RelayCommand] private void CancelAddUnit() { IsAddingUnit = false; NewUnitName = ""; }
    [RelayCommand]
    private void ConfirmAddUnit()
    {
        if (string.IsNullOrWhiteSpace(NewUnitName)) return;
        try { var id = DesktopServices.Lookups.AddUnit(_session, NewUnitName.Trim());
            var item = new LookupItem(id, NewUnitName.Trim()); Units.Add(item); SelectedUnit = item;
            IsAddingUnit = false; NewUnitName = ""; }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }

    [RelayCommand] private void StartAddBrand() { IsAddingBrand = true; NewBrandName = ""; }
    [RelayCommand] private void CancelAddBrand() { IsAddingBrand = false; NewBrandName = ""; }
    [RelayCommand]
    private void ConfirmAddBrand()
    {
        if (string.IsNullOrWhiteSpace(NewBrandName)) return;
        try { var id = DesktopServices.Lookups.AddBrand(_session, NewBrandName.Trim(), "material");
            var item = new LookupItem(id, NewBrandName.Trim()); Brands.Add(item); SelectedBrand = item;
            IsAddingBrand = false; NewBrandName = ""; }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }

    [RelayCommand] private void StartAddSupplier() { IsAddingSupplier = true; NewSupplierName = ""; }
    [RelayCommand] private void CancelAddSupplier() { IsAddingSupplier = false; NewSupplierName = ""; }
    [RelayCommand]
    private void ConfirmAddSupplier()
    {
        if (string.IsNullOrWhiteSpace(NewSupplierName)) return;
        try { var id = DesktopServices.Lookups.AddSupplier(_session, NewSupplierName.Trim());
            var item = new LookupItem(id, NewSupplierName.Trim()); Suppliers.Add(item); SelectedSupplier = item;
            IsAddingSupplier = false; NewSupplierName = ""; }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }

    // ═══════════ Detay (satır seçince tüm alanlar) ═══════════
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private MaterialRow? _selected;
    [ObservableProperty] private MaterialDetail? _detail;
    public bool HasSelection => Selected != null;

    partial void OnSelectedChanged(MaterialRow? value)
    {
        ConfirmDelete = false;
        if (value is null) { Detail = null; return; }
        try { Detail = DesktopServices.Materials.GetDetail(_session, value.Id); }
        catch (Exception ex) { Status = "Detay yüklenemedi: " + ex.Message; }
    }

    /// <summary>Seçili malzemeyi düzenleme modunda forma yükler (lookup'lar id ile ön-seçilir).</summary>
    [RelayCommand]
    private void BeginEdit()
    {
        if (Detail is null) return;
        if (!CanEdit) { Status = "Yetki yok."; return; }
        LoadLookups();
        var d = Detail;
        EditId = d.Id;
        NewCode = d.Code; NewName = d.Name; NewType = string.IsNullOrWhiteSpace(d.Type) ? "Diğer" : d.Type;
        NewMinStock = d.MinStock; NewUnitPrice = d.UnitPrice; NewOpeningStock = 0;
        NewDescription = d.Description ?? "";
        SelectedCategory = Categories.FirstOrDefault(c => c.Id == d.CategoryId);
        SelectedSubCategory = SubCategories.FirstOrDefault(c => c.Id == d.CategoryId);
        SelectedUnit = Units.FirstOrDefault(u => u.Id == d.UnitId);
        SelectedBrand = Brands.FirstOrDefault(b => b.Id == d.BrandId);
        SelectedSupplier = Suppliers.FirstOrDefault(x => x.Id == d.SupplierId);
        TriedSave = false;
        ShowAdd = true;
    }

    [RelayCommand]
    private async Task RequestDelete()
    {
        if (!CanDelete) { Status = "Yetki yok."; return; }
        if (Detail is null) return;
        var ok = await ConfirmService.AskAsync(
            $"'{Detail.Name}' malzemesi silinsin mi? Kayıt çöp kutusuna alınır.",
            "Malzeme Sil", "Evet, Sil", "Vazgeç", danger: true);
        if (!ok) return;
        try
        {
            DesktopServices.Materials.Delete(_session, Detail.Id);
            Selected = null; Load(); Status = "Malzeme silindi.";
        }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }

    // ═══════════ Uyumlu araçlar (form çoklu seçim) ═══════════
    public ObservableCollection<VehiclePick> VehiclePicks { get; } = new();

    private void LoadVehiclePicks()
    {
        VehiclePicks.Clear();
        try { foreach (var v in DesktopServices.Vehicles.List(_session)) VehiclePicks.Add(new VehiclePick(v.Id, v.InternalCode, v.Plate ?? "")); }
        catch { /* araç yoksa sessiz */ }
    }

    // ═══════════ Muadil malzeme (mevcut kayıtlardan ara+ekle) ═══════════
    [ObservableProperty] private string _equivalentSearch = "";
    public ObservableCollection<MaterialRow> EquivalentResults { get; } = new();
    public ObservableCollection<MaterialRow> ChosenEquivalents { get; } = new();

    partial void OnEquivalentSearchChanged(string value)
    {
        EquivalentResults.Clear();
        var term = value?.Trim();
        if (string.IsNullOrEmpty(term) || term.Length < 2) return;
        try
        {
            var page = DesktopServices.Materials.List(_session, new PageRequest { Limit = 10 }, term);
            foreach (var m in page.Items)
            {
                if (ChosenEquivalents.Any(c => c.Id == m.Id)) continue;
                EquivalentResults.Add(new MaterialRow(m.Id, m.Code, m.Name, m.Type, m.UnitPrice, m.Currency, m.MinStock, 0));
            }
        }
        catch { /* arama hatası sessiz */ }
    }

    [RelayCommand]
    private void AddEquivalentPick(MaterialRow? m)
    {
        if (m is null) return;
        if (!ChosenEquivalents.Any(c => c.Id == m.Id)) ChosenEquivalents.Add(m);
        EquivalentResults.Remove(m);
        EquivalentSearch = "";
    }

    [RelayCommand]
    private void RemoveEquivalentPick(MaterialRow? m)
    {
        if (m is not null) ChosenEquivalents.Remove(m);
    }

    private void NotifyListState()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRows));
    }
}

public sealed partial class VehiclePick : ObservableObject
{
    public string Id { get; }
    public string Code { get; }
    public string Plate { get; }
    [ObservableProperty] private bool _isSelected;
    public string Display => string.IsNullOrWhiteSpace(Plate) ? Code : $"{Code} — {Plate}";
    public VehiclePick(string id, string code, string plate) { Id = id; Code = code; Plate = plate; }
}

public sealed record MaterialRow(string Id, string Code, string Name, string? Type, decimal UnitPrice, string Currency, decimal MinStock, decimal Stock)
{
    // Sunum türevleri (mevcut veriden hesap; iş mantığı değişmez)
    public bool IsLowStock => Stock <= MinStock;
    public string StockText => IsLowStock ? "Düşük" : "Yeterli";
    public BadgeKind StockKind => IsLowStock ? BadgeKind.Warning : BadgeKind.Success;
    public string TypeDisplay => string.IsNullOrWhiteSpace(Type) ? "—" : Type!;
}
