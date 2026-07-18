using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Desktop.Controls;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Malzemeler ekranı — liste + kolon bazlı filtre + sayfalama + yeni kayıt. MaterialService üzerine (SQLite).</summary>
public sealed partial class MaterialsViewModel : ViewModelBase, IDeepLinkTarget, IListGridViewModel
{
    ICommand IListGridViewModel.SortByCommand => SortByCommand;
    private readonly SessionContext _session;

    public ObservableCollection<MaterialRow> Items { get; } = new();

    [ObservableProperty] private string? _status;

    // ── Malzeme Listesi — kolon bazlı filtre + sayfalama (kullanıcı isteği 2026-07-17) ──
    public IReadOnlyList<int> PageSizes { get; } = new[] { 25, 50, 100, 200 };
    [ObservableProperty] private List<string> _visibleColumns = MaterialListColumns.DefaultVisible.ToList();
    public ObservableCollection<ColumnFilterItem> FilterFields { get; } = new();
    public ObservableCollection<int> PageNumbers { get; } = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoPrev))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private int _page = 1;
    [ObservableProperty] private int _pageSize = 25;   // varsayılan 25 (kullanıcı isteği 2026-07-18)
    [ObservableProperty] private int _totalCount;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private int _totalPages = 1;
    private bool _suppressPageSizeReload;

    public bool CanGoPrev => Page > 1;
    public bool CanGoNext => Page < TotalPages;

    // ── Başlığa tıklayınca sıralama (kullanıcı isteği 2026-07-18, madde 5) ──
    private string? _sortColumn; private bool _sortDesc;
    /// <summary>(SortColumn, SortDesc) — başlık okunun XAML converter'ı buna bağlanır; sıralama değişince
    /// bildirilir ve tüm başlıklardaki ok yeniden hesaplanır.</summary>
    public (string? SortColumn, bool SortDesc) SortState => (_sortColumn, _sortDesc);

    [RelayCommand]
    private void SortBy(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (_sortColumn != key) { _sortColumn = key; _sortDesc = false; }
        else if (!_sortDesc) { _sortDesc = true; }
        else { _sortColumn = null; _sortDesc = false; }   // 3. tık: sıralama kapanır (doğal sıra)
        OnPropertyChanged(nameof(SortState));
        Page = 1; Load();
    }

    // ── Kolon genişlikleri — kişiye özel, manuel ayarlanabilir (kullanıcı isteği 2026-07-18, madde 3) ──
    private static readonly Dictionary<string, double> DefaultColWidths = new()
    {
        [MaterialListColumns.Code] = 90, [MaterialListColumns.Name] = 160, [MaterialListColumns.Type] = 90,
        [MaterialListColumns.Category] = 110, [MaterialListColumns.Unit] = 80, [MaterialListColumns.Brand] = 100,
        [MaterialListColumns.Supplier] = 110, [MaterialListColumns.UnitPrice] = 100, [MaterialListColumns.Currency] = 64,
        [MaterialListColumns.MinStock] = 90, [MaterialListColumns.Stock] = 80, [MaterialListColumns.Status] = 110,
        [MaterialListColumns.Description] = 160, [MaterialListColumns.CompatibleVehicles] = 160, [MaterialListColumns.Equivalents] = 160,
    };

    [ObservableProperty] private Dictionary<string, double> _colWidths = new(DefaultColWidths);

    /// <summary>Sürükleme sırasında ANLIK genişlik (her piksel hareketinde çağrılır — henüz KAYDETMEZ).</summary>
    public void PreviewColumnWidth(string key, double newWidth)
    {
        var w = Math.Max(40, Math.Min(600, newWidth));
        ColWidths = new Dictionary<string, double>(ColWidths) { [key] = w };   // yeni referans → converter'lar yeniden çalışır
    }

    public double GetColumnWidth(string key) => ColWidths.TryGetValue(key, out var w) ? w : 100;

    /// <summary>Sürükleme bittiğinde (parmak/mouse kalkınca) ÇAĞRILIR — bu ANDA kişiye özel kalıcı hâle gelir.</summary>
    public void CommitColumnWidth()
    {
        try { DesktopServices.ListPrefs.SaveWidths(_session, "materials", ColWidths.ToDictionary(k => k.Key, v => (int)v.Value)); }
        catch { }
    }

    partial void OnVisibleColumnsChanged(List<string> value) => RebuildFilterFields();

    partial void OnPageSizeChanged(int value)
    {
        if (_suppressPageSizeReload) return;
        try { DesktopServices.ListPrefs.SavePageSize(_session, "materials", value); } catch { }   // kişiye özel hatırla
        Page = 1; Load();
    }

    private void RebuildFilterFields()
    {
        var old = FilterFields.ToDictionary(f => f.Key, f => f.Value);
        FilterFields.Clear();
        foreach (var key in VisibleColumns)
        {
            var col = MaterialListColumns.All.FirstOrDefault(c => c.Key == key);
            FilterFields.Add(new ColumnFilterItem(key, col?.Label ?? key, col?.IsNumeric ?? false)
            { Value = old.TryGetValue(key, out var v) ? v : "" });
        }
    }

    private void RebuildPageNumbers()
    {
        PageNumbers.Clear();
        var start = Math.Max(1, Page - 3);
        var end = Math.Min(TotalPages, Page + 3);
        for (var p = start; p <= end; p++) PageNumbers.Add(p);
    }

    [ObservableProperty] private bool _isExporting;

    /// <summary>"Excel'e Aktar" (kullanıcı isteği 2026-07-19): AKTİF filtrelerle eşleşen TÜM sonuçları
    /// (sayfalama sınırı olmadan) indirir — o an ekrandaki sayfayı değil. Yerel (ağ gerekmez).</summary>
    [RelayCommand]
    private async Task ExportExcel()
    {
        if (IsExporting) return;
        IsExporting = true;
        try
        {
            var rows = DesktopServices.Materials.SearchGridAll(_session, BuildFilter(), _sortColumn, _sortDesc);
            var path = await FilePickerService.SaveExcelAsync("Malzemeler.xlsx");
            if (path is null) return;
            var bytes = DesktopServices.Excel.Export(MaterialService.ToTableModel(rows));
            await File.WriteAllBytesAsync(path, bytes);
        }
        catch (Exception ex) { Status = "Excel'e aktarılamadı: " + ex.Message; }
        finally { IsExporting = false; }
    }

    private MaterialGridFilter BuildFilter()
    {
        string? V(string key)
        {
            var f = FilterFields.FirstOrDefault(x => x.Key == key);
            return string.IsNullOrWhiteSpace(f?.Value) ? null : f!.Value.Trim();
        }
        return new MaterialGridFilter(
            V(MaterialListColumns.Code), V(MaterialListColumns.Name), V(MaterialListColumns.Type),
            V(MaterialListColumns.Category), V(MaterialListColumns.Unit), V(MaterialListColumns.Brand),
            V(MaterialListColumns.Supplier), V(MaterialListColumns.UnitPrice), V(MaterialListColumns.Currency),
            V(MaterialListColumns.MinStock), V(MaterialListColumns.Stock), V(MaterialListColumns.Status),
            V(MaterialListColumns.Description), V(MaterialListColumns.CompatibleVehicles), V(MaterialListColumns.Equivalents));
    }

    [RelayCommand]
    private void ApplyFilters() { Page = 1; Load(); }

    [RelayCommand]
    private void ClearFilters()
    {
        foreach (var f in FilterFields) f.Value = "";
        Page = 1; Load();
    }

    [RelayCommand]
    private void GoToPage(int page) { Page = page; Load(); }

    [RelayCommand]
    private void PrevPage() { if (CanGoPrev) { Page--; Load(); } }

    [RelayCommand]
    private void NextPage() { if (CanGoNext) { Page++; Load(); } }

    [RelayCommand]
    private async Task OpenColumnPicker()
    {
        var available = MaterialListColumns.All.Select(c => (c.Key, c.Label)).ToList();
        var chosen = await ColumnPickerService.PickAsync(available, VisibleColumns);
        if (chosen is null) return;
        VisibleColumns = chosen;
        DesktopServices.ListPrefs.SaveColumns(_session, "materials", chosen);
        Load();
    }

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

    // Malzeme şablonu (örnek kayıt) — seçilince form önden dolar; seçilmezse kayıtta uyarı çıkar.
    public ObservableCollection<DepoWise.Infrastructure.Materials.MaterialTemplateRow> MaterialTemplates { get; } = new();
    [ObservableProperty] private DepoWise.Infrastructure.Materials.MaterialTemplateRow? _selectedMaterialTemplate;

    partial void OnSelectedMaterialTemplateChanged(DepoWise.Infrastructure.Materials.MaterialTemplateRow? value)
    {
        if (value is null) return;
        try
        {
            var t = DesktopServices.MaterialTemplates.Get(_session, value.Id);
            if (t is null) return;
            if (!string.IsNullOrWhiteSpace(t.Code)) NewCode = t.Code!;
            if (!string.IsNullOrWhiteSpace(t.Name)) NewName = t.Name;
            if (!string.IsNullOrWhiteSpace(t.Type)) NewType = t.Type!;
            NewMinStock = t.MinStock; NewUnitPrice = t.UnitPrice;
            if (!string.IsNullOrWhiteSpace(t.Description)) NewDescription = t.Description!;
            SelectedCategory = Categories.FirstOrDefault(c => c.Id == t.CategoryId);
            SelectedUnit = Units.FirstOrDefault(u => u.Id == t.UnitId);
            SelectedBrand = Brands.FirstOrDefault(b => b.Id == t.BrandId);
            SelectedSupplier = Suppliers.FirstOrDefault(x => x.Id == t.SupplierId);
        }
        catch { }
    }

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
        var saved = DesktopServices.ListPrefs.GetColumns(session, "materials");
        VisibleColumns = saved is { Count: > 0 } ? saved.ToList() : MaterialListColumns.DefaultVisible.ToList();
        _suppressPageSizeReload = true;
        try { PageSize = DesktopServices.ListPrefs.GetPageSize(session, "materials") ?? 25; }   // değiştirmediyse 25
        finally { _suppressPageSizeReload = false; }
        var savedWidths = DesktopServices.ListPrefs.GetWidths(session, "materials");
        if (savedWidths is { Count: > 0 })
        {
            var merged = new Dictionary<string, double>(DefaultColWidths);
            foreach (var (k, v) in savedWidths) merged[k] = v;
            ColWidths = merged;
        }
        Load();
        if (openAdd && CanWrite) { ShowAdd = true; LoadLookups(); RefreshEquivalentResults(); }
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            var grid = DesktopServices.Materials.SearchGrid(_session, BuildFilter(), Page, PageSize, _sortColumn, _sortDesc);
            foreach (var m in grid.Items)
                Items.Add(new MaterialRow(m.Id, m.Code, m.Name, m.Type, m.UnitPrice, m.Currency, m.MinStock, m.Stock,
                    m.Category ?? "", m.Unit ?? "", m.Brand ?? "", m.Supplier ?? "", m.Status,
                    m.Description ?? "", m.CompatibleVehicles ?? "", m.Equivalents ?? ""));
            TotalCount = grid.TotalCount; TotalPages = grid.TotalPages;
            Page = grid.Page;
            _suppressPageSizeReload = true;
            try { PageSize = grid.PageSize; } finally { _suppressPageSizeReload = false; }
            RebuildPageNumbers();
            Status = $"{TotalCount} kayıt — sayfa {Page} / {TotalPages}";
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
        // "Tüm Şubeler" modunda yazma yok: açılış stoğu hareketi şubesiz düşerdi.
        if (!await BranchGuard.RequireBranchAsync(_session, "Malzemeler")) return;
        if (string.IsNullOrWhiteSpace(NewCode) || string.IsNullOrWhiteSpace(NewName))
        {
            Status = "Kod ve ad zorunlu."; return;
        }
        if (SelectedUnit is null) { Status = "Birim seçin."; return; }
        if (!editing && SelectedMaterialTemplate is null)
        {
            // Şablon dışı kayıt uyarısı (tek tip kayıt için).
            if (!await ConfirmService.AskAsync("Ana Yetkiliye Bilgi verilmelidir! Şablon dışı kayıt girmektesiniz!\n\nYine de devam edilsin mi?",
                    "Şablon Dışı Kayıt", "Evet, Devam Et", "Vazgeç", danger: true)) return;
        }

        if (!editing && SelectedMaterialTemplate is not null && !await ConfirmService.AskAsync("Yeni malzeme kaydedilsin mi?", "Kaydet")) return;

        // Alt kategori seçiliyse en özgün olanı (alt) kullanılır; yoksa kategori.
        var categoryId = (SelectedSubCategory ?? SelectedCategory)?.Id;
        var typeVal = string.IsNullOrWhiteSpace(NewType) ? null : NewType;
        var descVal = string.IsNullOrWhiteSpace(NewDescription) ? null : NewDescription.Trim();

        if (editing)
        {
            if (Detail is not null)
            {
                var origCat = (Categories.FirstOrDefault(c => c.Id == Detail.CategoryId)
                               ?? SubCategories.FirstOrDefault(c => c.Id == Detail.CategoryId))?.Name;
                var sum = new ChangeSummary();
                sum.Add("Kod", Detail.Code, NewCode.Trim());
                sum.Add("Ad", Detail.Name, NewName.Trim());
                sum.Add("Tür", Detail.Type, typeVal);
                sum.Add("Kategori", origCat, (SelectedSubCategory ?? SelectedCategory)?.Name);
                sum.Add("Birim", Units.FirstOrDefault(u => u.Id == Detail.UnitId)?.Name, SelectedUnit?.Name);
                sum.Add("Marka", Brands.FirstOrDefault(b => b.Id == Detail.BrandId)?.Name, SelectedBrand?.Name);
                sum.Add("Tedarikçi", Suppliers.FirstOrDefault(x => x.Id == Detail.SupplierId)?.Name, SelectedSupplier?.Name);
                sum.Add("Min. Stok", Detail.MinStock, NewMinStock);
                sum.Add("Birim Fiyat", Detail.UnitPrice, NewUnitPrice);
                sum.Add("Açıklama", Detail.Description, descVal);
                if (!await ConfirmService.AskAsync(sum.Build("Malzeme bilgileri güncellensin mi?"), "Kaydet")) return;
            }
            try
            {
                DesktopServices.Materials.Update(_session, EditId!, new UpdateMaterial(
                    Code: NewCode.Trim(), Name: NewName.Trim(), Type: typeVal,
                    CategoryId: categoryId, UnitId: SelectedUnit!.Id,
                    BrandId: SelectedBrand?.Id, SupplierId: SelectedSupplier?.Id,
                    MinStock: NewMinStock, UnitPrice: NewUnitPrice, Description: descVal));

                // Uyumlu araçlar (tam değiştir)
                DesktopServices.Materials.SetCompatibleVehicles(_session, EditId!,
                    VehiclePicks.Where(p => p.IsSelected).Select(p => p.Id));

                // Muadiller (ekle/çıkar uzlaştırma)
                var chosenIds = ChosenEquivalents.Select(e => e.Id).ToHashSet();
                foreach (var addId in chosenIds.Where(x => !_origEquivIds.Contains(x)))
                    DesktopServices.Materials.AddEquivalent(_session, EditId!, addId);
                foreach (var remId in _origEquivIds.Where(x => !chosenIds.Contains(x)))
                    DesktopServices.Materials.RemoveEquivalent(_session, EditId!, remId);

                SaveStagedPhotos(EditId!);
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

            // Açılış stoğu 0 dışında ise stok hareketi. ADR-086: negatif de olur (devralınan eksik stok).
            if (NewOpeningStock != 0)
                DesktopServices.OpeningStock.RecordOpening(_session, id, NewOpeningStock, Guid.NewGuid().ToString("N"));

            // Uyumlu araçlar (seçiliyse)
            var compatIds = VehiclePicks.Where(p => p.IsSelected).Select(p => p.Id).ToList();
            if (compatIds.Count > 0)
                DesktopServices.Materials.SetCompatibleVehicles(_session, id, compatIds);

            // Muadiller
            foreach (var eq in ChosenEquivalents)
                DesktopServices.Materials.AddEquivalent(_session, id, eq.Id);

            SaveStagedPhotos(id);
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
        if (ShowAdd) { LoadLookups(); RefreshEquivalentResults(); }
    }

    /// <summary>İptal — form alanlarını temizler ve kapatır (sunum durumu; iş mantığı yok).</summary>
    [RelayCommand]
    private void Clear()
    {
        NewCode = ""; NewName = ""; NewUnitPrice = 0; NewMinStock = 0;
        NewOpeningStock = 0; NewDescription = ""; NewType = "Yedek Parça";
        SelectedCategory = null; SelectedSubCategory = null; SelectedUnit = null;
        SelectedBrand = null; SelectedSupplier = null; SelectedMaterialTemplate = null;
        IsAddingCategory = IsAddingSubCategory = IsAddingUnit = IsAddingBrand = IsAddingSupplier = false;
        foreach (var p in VehiclePicks) p.IsSelected = false;
        VehicleSearch = "";
        ChosenEquivalents.Clear(); EquivalentResults.Clear(); EquivalentSearch = "";
        _origEquivIds = new();
        Photos.Clear();
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
            MaterialTemplates.Clear();
            try { foreach (var t in DesktopServices.MaterialTemplates.List(_session)) MaterialTemplates.Add(t); } catch { }
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

    /// <summary>Köprü: malzemeyi seçip detayını açar (Ana Ekran düşük stok uyarısı; salt görüntü).</summary>
    public void OpenEntity(string entityId)
    {
        var row = Items.FirstOrDefault(r => r.Id == entityId);
        if (row is not null) Selected = row;
    }

    partial void OnSelectedChanged(MaterialRow? value)
    {
        ConfirmDelete = false;
        if (value is null) { Detail = null; DetailPhotos.Clear(); return; }
        try { Detail = DesktopServices.Materials.GetDetail(_session, value.Id); LoadDetailPhotos(value.Id); }
        catch (Exception ex) { Status = "Detay yüklenemedi: " + ex.Message; }
    }

    /// <summary>Çift tık: ayrı pencerede Düzelt/Kaydet/Sil (kullanıcı isteği 2026-07-19). Tek tık mevcut detay
    /// panelini açar (korunur). Kaydedilir/silinirse liste yenilenir.</summary>
    [RelayCommand]
    private async Task QuickEditSelected()
    {
        if (Selected is null) return;
        var res = await QuickEditService.ShowMaterialAsync(_session, Selected.Id);
        if (res is "saved" or "deleted")
        {
            if (res == "deleted") Selected = null;
            Load();
        }
    }

    private HashSet<string> _origEquivIds = new();

    /// <summary>Seçili malzemeyi düzenleme modunda forma yükler (tüm alanlar + ilişkiler). Onay sorar.</summary>
    [RelayCommand]
    private async Task BeginEdit()
    {
        if (Detail is null) return;
        if (!CanEdit) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync("Bu malzemeyi düzenlemek istiyor musunuz?", "Düzenle")) return;

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

        // İlişkiler — mevcut seçimleri yükle (düzenlemede de değiştirilebilir)
        foreach (var p in VehiclePicks) p.IsSelected = d.CompatibleVehicles.Any(c => c.Id == p.Id);
        ChosenEquivalents.Clear();
        foreach (var e in d.Equivalents)
            ChosenEquivalents.Add(new MaterialRow(e.Id, e.Code, e.Name, null, 0, "TRY", 0, 0));
        _origEquivIds = d.Equivalents.Select(e => e.Id).ToHashSet();
        RefreshEquivalentResults();

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

    // ═══════════ Uyumlu araçlar (form çoklu seçim + arama + tümünü seç) ═══════════
    public ObservableCollection<VehiclePick> VehiclePicks { get; } = new();
    public ObservableCollection<VehiclePick> FilteredVehiclePicks { get; } = new();
    [ObservableProperty] private string _vehicleSearch = "";

    partial void OnVehicleSearchChanged(string value) => RebuildFilteredVehicles();

    private void LoadVehiclePicks()
    {
        VehiclePicks.Clear();
        try { foreach (var v in DesktopServices.Vehicles.List(_session)) VehiclePicks.Add(new VehiclePick(v.Id, v.InternalCode, v.Plate ?? "")); }
        catch { /* araç yoksa sessiz */ }
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

    /// <summary>Aramadaki tüm araçları (arama yoksa hepsini) seç.</summary>
    [RelayCommand]
    private void SelectAllVehicles() { foreach (var p in FilteredVehiclePicks) p.IsSelected = true; }

    [RelayCommand]
    private void ClearVehicles() { foreach (var p in FilteredVehiclePicks) p.IsSelected = false; }

    // ═══════════ Muadil malzeme (mevcut kayıtlardan ara+ekle) ═══════════
    [ObservableProperty] private string _equivalentSearch = "";
    public ObservableCollection<MaterialRow> EquivalentResults { get; } = new();
    public ObservableCollection<MaterialRow> ChosenEquivalents { get; } = new();

    partial void OnEquivalentSearchChanged(string value) => RefreshEquivalentResults();

    /// <summary>Arama boşsa tümü (max 200), doluysa eşleşenler; seçilenler ve kendisi hariç.</summary>
    private void RefreshEquivalentResults()
    {
        EquivalentResults.Clear();
        var term = EquivalentSearch?.Trim();
        try
        {
            var page = DesktopServices.Materials.List(_session, new PageRequest { Limit = 200 },
                string.IsNullOrEmpty(term) ? null : term);
            foreach (var m in page.Items)
            {
                if (m.Id == EditId) continue;
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
        RefreshEquivalentResults();
    }

    [RelayCommand]
    private void RemoveEquivalentPick(MaterialRow? m)
    {
        if (m is not null) ChosenEquivalents.Remove(m);
        RefreshEquivalentResults();
    }

    /// <summary>Aramadaki tüm sonuçları (arama yoksa hepsini) muadil olarak ekle.</summary>
    [RelayCommand]
    private void SelectAllEquivalents()
    {
        foreach (var m in EquivalentResults.ToList())
            if (!ChosenEquivalents.Any(c => c.Id == m.Id)) ChosenEquivalents.Add(m);
        RefreshEquivalentResults();
    }

    // ═══════════ Fotoğraflar ═══════════
    public ObservableCollection<PhotoStage> Photos { get; } = new();
    public ObservableCollection<DetailPhoto> DetailPhotos { get; } = new();

    [RelayCommand]
    private async Task AddPhotos()
    {
        foreach (var p in await FilePickerService.PickImagesAsync()) Photos.Add(new PhotoStage(p));
    }

    [RelayCommand]
    private void RemovePhoto(PhotoStage? p) { if (p is not null) Photos.Remove(p); }

    [RelayCommand]
    private void OpenPhoto(Bitmap? b) => PhotoViewer.Show(b);

    /// <summary>Detaydaki kayıtlı fotoğrafı sil (onaylı).</summary>
    [RelayCommand]
    private async Task DeleteDetailPhoto(DetailPhoto? p)
    {
        if (p is null || Selected is null) return;
        if (!CanEdit) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync("Bu fotoğraf silinsin mi?", "Fotoğraf Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try { DesktopServices.Files.DeletePhoto(_session, p.FileId); LoadDetailPhotos(Selected.Id); Status = "Fotoğraf silindi."; }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }

    /// <summary>Uyumlu araç satırına tıkla → Araçlar ekranında o aracın detayını aç.</summary>
    [RelayCommand]
    private void OpenVehicle(MaterialRefRow? r)
    {
        if (r is not null) ShellViewModel.Current?.GoToVehicle(r.Id);
    }

    private void SaveStagedPhotos(string materialId)
    {
        foreach (var ph in Photos)
        {
            try
            {
                var bytes = File.ReadAllBytes(ph.LocalPath);
                DesktopServices.Files.SavePhoto(_session, "material", materialId, Path.GetFileName(ph.LocalPath), null, bytes);
            }
            catch (Exception ex) { Status = "Foto kaydedilemedi: " + ex.Message; }
        }
    }

    private void LoadDetailPhotos(string materialId)
    {
        DetailPhotos.Clear();
        try
        {
            foreach (var f in DesktopServices.Files.GetPhotos(_session, "material", materialId))
            {
                var bytes = DesktopServices.Storage.Read(f.StorageKey);
                DetailPhotos.Add(new DetailPhoto(f.Id, new Bitmap(new MemoryStream(bytes))));
            }
        }
        catch { /* foto yoksa sessiz */ }
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

/// <summary>Malzeme Listesi satırı. Son 8 alan ADR 2026-07-17 (kolon bazlı liste) ile eklendi — varsayılan
/// değerleri sayesinde ESKİ 8-parametreli çağrılar (Muadil Malzeme arama/seçim listeleri) DEĞİŞMEDEN çalışır.</summary>
public sealed record MaterialRow(string Id, string Code, string Name, string? Type, decimal UnitPrice, string Currency, decimal MinStock, decimal Stock,
    string Category = "", string Unit = "", string Brand = "", string Supplier = "", string StatusText = "",
    string Description = "", string CompatibleVehicles = "", string Equivalents = "")
{
    // Sunum türevleri (mevcut veriden hesap; iş mantığı değişmez)
    public bool IsLowStock => Stock <= MinStock;
    public string StockDisplay => string.IsNullOrEmpty(StatusText) ? (IsLowStock ? "Düşük" : "Yeterli") : StatusText;
    public BadgeKind StockKind => StatusText switch
    {
        "Stok Yok" => BadgeKind.Danger,
        "Düşük Stok" => BadgeKind.Warning,
        "Yeterli" => BadgeKind.Success,
        _ => IsLowStock ? BadgeKind.Warning : BadgeKind.Success,
    };
    public string TypeDisplay => string.IsNullOrWhiteSpace(Type) ? "—" : Type!;
}
