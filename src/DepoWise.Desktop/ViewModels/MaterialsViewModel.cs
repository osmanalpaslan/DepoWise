using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
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
public sealed partial class MaterialsViewModel : ViewModelBase, IDeepLinkTarget, IListGridViewModel, IRefreshable
{
    ICommand IListGridViewModel.SortByCommand => SortByCommand;
    /// <summary>
    /// Eşitleme yeni veri getirince açık ekranı yenile (2026-07-19) — ANCAK kullanıcı ekranda çalışıyorsa
    /// DOKUNMA (kullanıcı isteği 2026-08-08): detay paneli ya da ekleme/düzenleme formu açıkken liste yeniden
    /// kurulmaz; yenileme BEKLETİLİR, panel/form kapanınca sessizce uygulanır.
    /// </summary>
    public void RefreshData()
    {
        if (IsUserBusy) { _pendingRefresh = true; return; }
        Load();
    }

    /// <summary>Kullanıcı ekranda bir işin ortasında mı (detay açık / form açık)?</summary>
    private bool IsUserBusy => ShowAdd || Selected is not null;
    private bool _pendingRefresh;

    /// <summary>Detay/form kapanınca bekleyen eşitleme yenilemesini uygular (sessiz).</summary>
    private void FlushPendingRefresh()
    {
        if (!_pendingRefresh || IsUserBusy) return;
        _pendingRefresh = false;
        Load();
    }
    private readonly SessionContext _session;

    public ObservableCollection<MaterialRow> Items { get; } = new();

    [ObservableProperty] private string? _status;

    // ── Malzeme Listesi — kolon bazlı filtre + sayfalama (kullanıcı isteği 2026-07-17) ──
    public IReadOnlyList<int> PageSizes { get; } = new[] { 25, 50, 100, 200 };
    [ObservableProperty] private List<string> _visibleColumns = MaterialListColumns.DefaultVisible.ToList();
    public ObservableCollection<ColumnFilterItem> FilterFields { get; } = new();
    /// <summary>Başlık-altı filtre satırı (madde 4, 2026-08-06): anahtara göre HIZLI erişim — filtre kutusu
    /// sabit Grid.Column konumunda, tablo başlığıyla AYNI SharedSizeGroup'ta durur (bkz. XAML filtre satırı).</summary>
    [ObservableProperty] private IReadOnlyDictionary<string, ColumnFilterItem> _filterFieldsByKey = new Dictionary<string, ColumnFilterItem>();
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
        // Aynı piksel ise DOKUNMA: sürüklerken her fare hareketinde tüm hücrelerin yeniden
        // hesaplanmasını önler (akıcılık — kullanıcı isteği 2026-08-08).
        if (ColWidths.TryGetValue(key, out var cur) && System.Math.Abs(cur - w) < 1) return;
        ColWidths = new Dictionary<string, double>(ColWidths) { [key] = w };   // yeni referans → converter'lar yeniden çalışır
    }

    public double GetColumnWidth(string key) => ColWidths.TryGetValue(key, out var w) ? w : 100;

    /// <summary>Sürükleme bittiğinde çağrılır. KALICILIK KALDIRILDI (kullanıcı isteği 2026-08-08): kolon
    /// genişliği ARTIK KAYDEDİLMEZ → her login'de standart (DefaultColWidths) gelir; oturum içindeki değişiklik
    /// PreviewColumnWidth ile zaten uygulanmıştır. DB yazımı yok → sync ile çakışma/donma/hatalı tepki biter.</summary>
    public void CommitColumnWidth() { }

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
        FilterFieldsByKey = FilterFields.ToDictionary(f => f.Key, f => f);
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
        // Deny-by-default: dışa aktarım ayrı yetki (2026-07-26).
        if (!DepoWise.Application.Security.AccessControl.Can(_session, "export", DepoWise.Application.Security.PermissionAction.View))
        { Status = "Dışa aktarım (export) yetkiniz yok."; return; }
        if (IsExporting) return;
        IsExporting = true;
        try
        {
            // G2-05: dışa aktarım ekrandaki filtreyle AYNI kümeyi verir → "Yalnız kritik" açıkken
            // Excel de yalnız kritik satırları içerir (web'deki davranışın aynısı).
            var rows = DesktopServices.Materials.SearchGridAll(_session, BuildFilter(), _sortColumn, _sortDesc, CriticalOnly);
            var path = await FilePickerService.SaveExcelAsync("Malzemeler.xlsx");
            if (path is null) return;
            var bytes = DesktopServices.Excel.Export(MaterialService.ToTableModel(rows));
            await File.WriteAllBytesAsync(path, bytes);
        }
        catch (Exception ex) { Status = "Excel'e aktarılamadı: " + ex.Message; }
        finally { IsExporting = false; }
    }

    /// <summary>BAR-01 (ADR-177): seçili malzemenin KODUNU içeren yazdırılabilir QR etiketi (PNG).
    /// SALT-OKUNUR — kayda/DB'ye yazmaz; QR'a yalnız kod girer. Yerel üretim → çevrimdışı da çalışır.</summary>
    [RelayCommand]
    private async Task QrLabel()
    {
        if (Selected is null) { Status = "Önce listeden bir malzeme seçin."; return; }
        try
        {
            var bytes = DepoWise.Infrastructure.Reporting.QrLabelService.Png(Selected.Code);
            var path = await FilePickerService.SavePngAsync(DepoWise.Infrastructure.Reporting.QrLabelService.FileName(Selected.Code));
            if (path is null) return;
            await File.WriteAllBytesAsync(path, bytes);
            FilePickerService.OpenFile(path);
            Status = "QR etiketi kaydedildi: " + path;
        }
        catch (Exception ex) { Status = "QR üretilemedi: " + ex.Message; }
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

    // ── G2-05 (PRT-01 Grup 2a, 2026-08-10): "Yalnız kritik" — WEB'DE VARDI, MASAÜSTÜNDE YOKTU ──
    // Servis desteği ZATEN HAZIRDI (MaterialService.SearchGrid/SearchGridAll içindeki criticalOnly
    // parametresi + MaterialGridTests.YalnizKritik_StokMinAltinda_Olanlari_Getirir testi); masaüstü
    // yalnız bu parametreyi hiç geçirmiyordu. Yeni servis/sorgu YAZILMADI.
    // Tanım web ile BİREBİR aynı: stok <= min stok (min stok > 0) — bkz. Materials.razor "Yalnız kritik".
    // Liste ile Excel AYNI durumu paylaşır (web'de de öyle).
    // Kontrol biçimi: araç çubuğunda ONAY KUTUSU — projenin KENDİ deseni (bkz. DailyActivityView
    // "İptal edilenleri göster" + DailyActivityViewModel.OnShowCancelledChanged). Yeni bir toggle
    // mekanizması ya da converter GEREKMEZ.
    [ObservableProperty] private bool _criticalOnly;

    /// <summary>Filtre değişince ilk sayfaya dönülür (web'de de öyle: _page = 1; Load()).</summary>
    partial void OnCriticalOnlyChanged(bool value) { Page = 1; Load(); }

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
    public Func<string, CancellationToken, Task<IEnumerable<object>>> CategoryPopulator => SearchPopulator.For(() => Categories, c => c.Name);
    public Func<string, CancellationToken, Task<IEnumerable<object>>> SubCategoryPopulator => SearchPopulator.For(() => SubCategories, c => c.Name);
    public Func<string, CancellationToken, Task<IEnumerable<object>>> UnitPopulator => SearchPopulator.For(() => Units, u => u.Name);
    public Func<string, CancellationToken, Task<IEnumerable<object>>> BrandPopulator => SearchPopulator.For(() => Brands, b => b.Name);
    public Func<string, CancellationToken, Task<IEnumerable<object>>> SupplierPopulator => SearchPopulator.For(() => Suppliers, s => s.Name);

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
        if (IsEditMode) return;   // DÜZENLEME: yalnız BAĞLA (SelectedMaterialTemplate = bağ); alanları EZME
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

    /// <summary>Şablon bağını kaldırır — form alanlarına DOKUNMAZ (kullanıcı doldurduğunu kaybetmesin).</summary>
    [RelayCommand] private void ClearMaterialTemplate() => SelectedMaterialTemplate = null;

    private bool _lookupsLoaded;

    partial void OnSelectedCategoryChanged(LookupItem? value)
    {
        SelectedSubCategory = null;
        SubCategories.Clear();
        if (value is null) return;
        try { foreach (var sc in DesktopServices.Lookups.ListCategories(_session, value.Id)) SubCategories.Add(sc); }
        catch { /* alt kategori yoksa sessiz */ }
    }

    /// <summary>Düzenlemede kategori/alt kategori kutularını doğru doldur (madde 1.7, kullanıcı isteği 2026-08-06).
    /// Malzemenin <c>category_id</c>'si YAPRAK'tır: alt kategori seçildiyse onun id'si, yoksa üst kategorinin.
    /// Üst-seviye mi alt kategoride mi olduğunu çöz — üst-seviye ise doğrudan seç; alt kategoriyse ÖNCE ebeveyn
    /// üst kategoriyi bul+seç (bu OnSelectedCategoryChanged'i tetikleyip alt listeyi yükler), SONRA alt kategoriyi
    /// seç. Eskiden naif <c>FirstOrDefault</c> ile aranıyordu → alt-kategorili malzemede her iki kutu da boş
    /// açılıyordu. QuickEdit penceresi ve web ana form zaten böyle çözüyor; masaüstü ana form da hizalandı.</summary>
    private void ResolveEditCategory(string? categoryId)
    {
        SelectedCategory = null; SelectedSubCategory = null;
        if (string.IsNullOrEmpty(categoryId)) return;

        var top = Categories.FirstOrDefault(c => c.Id == categoryId);
        if (top is not null) { SelectedCategory = top; return; }   // üst-seviye kategori (alt kategori yok)

        foreach (var t in Categories)
        {
            List<LookupItem> subs;
            try { subs = DesktopServices.Lookups.ListCategories(_session, t.Id).ToList(); }
            catch { continue; }
            if (subs.Any(x => x.Id == categoryId))
            {
                SelectedCategory = t;   // handler SubCategories'i t'nin altlarıyla doldurur
                SelectedSubCategory = SubCategories.FirstOrDefault(s => s.Id == categoryId);
                return;
            }
        }
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
    [NotifyPropertyChangedFor(nameof(CanDeletePhoto))]   // ADR-182 (PK-F3): silme yalnız düzenleme modunda
    private string? _editId;
    public bool IsEditMode => EditId != null;
    public string FormTitle => IsEditMode ? "MALZEME DÜZENLE" : "YENİ MALZEME";
    [ObservableProperty] private bool _confirmDelete;

    public MaterialsViewModel(SessionContext session, bool openAdd = false)
    {
        _session = session;
        var saved = DesktopServices.ListPrefs.GetColumns(session, "materials");
        // İş #10: kaydedilmiş tercih KATALOĞA göre süzülür (hayalet kolon çizilmesin) — bkz. ListColumns.Sanitize.
        VisibleColumns = MaterialListColumns.Sanitize(saved).ToList();
        _suppressPageSizeReload = true;
        try { PageSize = DesktopServices.ListPrefs.GetPageSize(session, "materials") ?? 25; }   // değiştirmediyse 25
        finally { _suppressPageSizeReload = false; }
        // Kolon genişliği KALICI DEĞİL (kullanıcı isteği 2026-08-08): her login STANDART (DefaultColWidths);
        // oturum içinde serbestçe genişletilip daraltılır ama kaydedilmez → sync ile çakışma/donma olmaz.
        Load();
        if (openAdd && CanWrite) { ShowAdd = true; LoadLookups(); RefreshEquivalentResults(); }
    }

    [RelayCommand]
    private void Load()
    {
        // Periyodik eşitleme yenilemesinde (RefreshData) detay paneli KAPANMAMALI (kullanıcı isteği 2026-07-25):
        // Items.Clear() seçili satırı sıfırlar → OnSelectedChanged(null) paneli kapatırdı. Seçili kaydın kimliğini
        // saklayıp yeniden kurulan listede TEKRAR seçerek panel açık kalır (yalnız kayıt gerçekten kalktıysa kapanır).
        var selectedId = Selected?.Id;
        try
        {
            LoadError = null;
            Items.Clear();
            var grid = DesktopServices.Materials.SearchGrid(_session, BuildFilter(), Page, PageSize, _sortColumn, _sortDesc, CriticalOnly);   // G2-05
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
        if (selectedId is not null)
            Selected = Items.FirstOrDefault(x => x.Id == selectedId);   // bulunamazsa (silindi) panel doğal olarak kapanır
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
        // Malzeme şablonu kaldırıldı (kullanıcı isteği 2026-08-05) — şablon dışı uyarısı yok; yeni kayıtta düz onay.
        if (!editing && !await ConfirmService.AskAsync("Yeni malzeme kaydedilsin mi?", "Kaydet")) return;

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
                    MinStock: NewMinStock, UnitPrice: NewUnitPrice, Description: descVal,
                    TemplateId: SelectedMaterialTemplate?.Id),   // düzenlemede şablona bağla/koru (yüklenen mevcut bağ)
                    // DÜZENLEME KİLİDİ: formu açtığımız andaki sürüm. Kayıt arada başkası (ya da eşitlemeyle
                    // gelen başka makine) tarafından değiştiyse sessizce ezmek yerine ConcurrencyException.
                    expectedVersion: Detail?.Version);

                // Uyumlu araçlar (tam değiştir)
                DesktopServices.Materials.SetCompatibleVehicles(_session, EditId!,
                    VehiclePicks.Where(p => p.IsSelected).Select(p => p.Id));

                // Muadiller (ekle/çıkar uzlaştırma)
                var chosenIds = ChosenEquivalents.Select(e => e.Id).ToHashSet();
                foreach (var addId in chosenIds.Where(x => !_origEquivIds.Contains(x)))
                    DesktopServices.Materials.AddEquivalent(_session, EditId!, addId);
                foreach (var remId in _origEquivIds.Where(x => !chosenIds.Contains(x)))
                    DesktopServices.Materials.RemoveEquivalent(_session, EditId!, remId);

                await SaveStagedPhotosAsync(EditId!);
                Clear(); Load(); Status = "Malzeme güncellendi.";
            }
            catch (DepoWise.Application.Security.ConcurrencyException ex)
            {
                // Kayıt biz düzenlerken değişti. Yazdıklarını KAYBETME: karar kullanıcının.
                Status = ex.Message;
                if (await ConfirmService.AskAsync(
                        ex.Message + "\n\nKaydın güncel hâlini yüklemek ister misiniz? " +
                        "(\"Formda kal\" derseniz yazdıklarınız durur, kopyalayıp tekrar uygulayabilirsiniz.)",
                        "Kayıt değişti", okText: "Kaydı yenile", cancelText: "Formda kal"))
                {
                    Clear(); Load();
                }
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
                Description: string.IsNullOrWhiteSpace(NewDescription) ? null : NewDescription.Trim(),
                TemplateId: SelectedMaterialTemplate?.Id));   // seçilen şablon → şablonlu/şablon-dışı rapor ayrımı

            // Açılış stoğu 0 dışında ise stok hareketi. ADR-086: negatif de olur (devralınan eksik stok).
            if (NewOpeningStock != 0)
                // 🔴 STK-05 (D-3): açılış stoğu eskiden DEPOSUZ yazılıyordu → hepsi ATANMAMIŞ kovasına
                // düşüyordu (canlıdaki 663 lokasyonsuz açılışın sebebi). Artık oturumun deposuna yazılır.
                DesktopServices.OpeningStock.RecordOpening(_session, id, NewOpeningStock, Guid.NewGuid().ToString("N"),
                    branchId: _session.OperatingBranchId);

            // Uyumlu araçlar (seçiliyse)
            var compatIds = VehiclePicks.Where(p => p.IsSelected).Select(p => p.Id).ToList();
            if (compatIds.Count > 0)
                DesktopServices.Materials.SetCompatibleVehicles(_session, id, compatIds);

            // Muadiller
            foreach (var eq in ChosenEquivalents)
                DesktopServices.Materials.AddEquivalent(_session, id, eq.Id);

            await SaveStagedPhotosAsync(id);
            var fotoUyarisi = Status;   // yükleme uyarısı varsa korunur (Clear/Load ezmesin)
            Clear();
            Load();
            Status = fotoUyarisi is not null && fotoUyarisi.StartsWith("Kayıt tamam", StringComparison.Ordinal)
                ? fotoUyarisi : "Malzeme eklendi.";
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

    /// <summary>STK-05 — malzeme kartındaki DEPO KIRILIMI (hangi depoda ne kadar). Kart açılınca TEK sorgu
    /// (liste satırlarında çağrılmaz → "100 malzeme × 5 depo = 500 sorgu" yapısı oluşmaz).
    /// ⚠️ "Atanmamış" burada gerçek bir depo gibi DEĞİL, geçmişin bilinmezliği olarak ve EN SONDA listelenir.
    /// Toplam (<c>Detail.Stock</c>) FİRMA GENELİdir ve bu satırların toplamına eşittir — ikisi asla kopmaz.</summary>
    public ObservableCollection<StockLocationBalance> LocationBalances { get; } = new();
    public bool HasLocationBalances => LocationBalances.Count > 0;
    public bool HasSelection => Selected != null;

    /// <summary>Köprü: malzemeyi seçip detayını açar (Ana Ekran düşük stok uyarısı; salt görüntü).</summary>
    public void OpenEntity(string entityId)
    {
        var row = Items.FirstOrDefault(r => r.Id == entityId);
        if (row is not null) Selected = row;
    }

    /// <summary>İşlem Geçmişi (madde 4, 2026-08-06): seçili malzemenin TÜM stok hareketleri (giriş/çıkış/transfer/
    /// sayım/iptal), kronolojik. Malzeme bilgi panelinin bir bölümü — salt-okunur.</summary>
    public ObservableCollection<MaterialMovementRow> MaterialHistory { get; } = new();

    partial void OnShowAddChanged(bool value) { if (!value) FlushPendingRefresh(); }

    partial void OnSelectedChanged(MaterialRow? value)
    {
        ConfirmDelete = false;
        if (value is null)
        {
            Detail = null; DetailPhotos.Clear(); MaterialHistory.Clear(); LocationBalances.Clear();
            OnPropertyChanged(nameof(HasLocationBalances));
            FlushPendingRefresh();   // detay kapandı → bekletilen eşitleme yenilemesi şimdi uygulanır
            return;
        }
        try { Detail = DesktopServices.Materials.GetDetail(_session, value.Id); _ = LoadDetailPhotosAsync(value.Id); }
        catch (Exception ex) { Status = "Detay yüklenemedi: " + ex.Message; }
        MaterialHistory.Clear();
        try { foreach (var m in DesktopServices.Stock.RecentForMaterial(_session, value.Id, 100)) MaterialHistory.Add(m); } catch { }

        // STK-05: DEPO KIRILIMI — kart açılınca TEK sorgu. Liste satırlarında çağrılmaz.
        LocationBalances.Clear();
        try { foreach (var l in DesktopServices.Stock.GetLocationBalances(_session, value.Id)) LocationBalances.Add(l); }
        catch { }
        OnPropertyChanged(nameof(HasLocationBalances));
    }

    /// <summary>Çift tık: ayrı pencerede Düzelt/Kaydet/Sil (kullanıcı isteği 2026-07-19). Tek tık mevcut detay
    /// panelini açar (korunur). Kaydedilir/silinirse liste yenilenir.</summary>
    [RelayCommand]
    private async Task QuickEditSelected()
    {
        if (Selected is null) return;
        var res = await QuickEditService.ShowMaterialAsync(_session, Selected.Id);
        // "stale" = düzenleme kilidi: kayıt biz açıkken değişti, kullanıcı "kapat ve yenile" dedi → listeyi tazele.
        if (res is "saved" or "deleted" or "stale")
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
        ResolveEditCategory(d.CategoryId);   // madde 1.7: category_id yaprak → üst/alt kutulara doğru dağıt
        SelectedUnit = Units.FirstOrDefault(u => u.Id == d.UnitId);
        SelectedBrand = Brands.FirstOrDefault(b => b.Id == d.BrandId);
        SelectedSupplier = Suppliers.FirstOrDefault(x => x.Id == d.SupplierId);
        // Mevcut şablon bağı (EditId set edildiği için changed-handler prefill YAPMAZ; yalnız bağ yüklenir).
        SelectedMaterialTemplate = MaterialTemplates.FirstOrDefault(t => t.Id == d.TemplateId);

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
        var picked = await FilePickerService.PickImagesAsync();
        // Desteklenmeyen biçim (webp/bmp/… — yalnız JPEG/PNG kabul edilir) seçilirse uyarı gösterir,
        // yalnız geçerli dosyaları forma ekler (kullanıcı isteği 2026-07-25).
        var valid = await PhotoPickHelper.ValidateAndWarnAsync(picked);
        foreach (var p in valid) Photos.Add(new PhotoStage(p));
    }

    [RelayCommand]
    private void RemovePhoto(PhotoStage? p) { if (p is not null) Photos.Remove(p); }

    [RelayCommand]
    private void OpenPhoto(Bitmap? b) => PhotoViewer.Show(b);

    /// <summary>
    /// Kayıtlı fotoğrafı siler — ⭐ ADR-182 (PK-F3): YALNIZ düzenleme modunda ve SİLME yetkisiyle.
    ///
    /// 🔴 Kapatılan iki hata: (1) silme düğmesi salt-okunur bilgi panelindeydi, yani kullanıcı Düzenle'ye
    /// geçmeden fotoğrafı silebiliyordu; (2) düğme <c>CanEdit</c> ile gösteriliyor ama sunucu <c>Delete</c>
    /// yetkisi istiyordu → düzenleme yetkisi olup silme yetkisi olmayan kullanıcı düğmeyi görüp hata alıyordu.
    /// </summary>
    [RelayCommand]
    private async Task DeleteDetailPhoto(DetailPhoto? p)
    {
        if (p is null) return;
        var kayitId = EditId ?? Selected?.Id;
        if (kayitId is null) return;
        if (!IsEditMode) { Status = "Fotoğraf silmek için önce Düzenle'ye geçin."; return; }
        if (!CanDelete) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync("Bu fotoğraf silinsin mi?", "Fotoğraf Sil", "Evet, Sil", "Vazgeç", danger: true)) return;

        var r = await DesktopPhotos.SilAsync("material", kayitId, p.FileId);
        if (r.Offline) { Status = "Fotoğraf silme çevrimiçi olmayı gerektirir."; return; }
        if (!r.Ok) { Status = "Silinemedi: " + (r.Error ?? "bilinmeyen hata"); return; }
        await LoadDetailPhotosAsync(kayitId);
        Status = "Fotoğraf silindi.";
    }

    /// <summary>Uyumlu araç satırına tıkla → Araçlar ekranında o aracın detayını aç.</summary>
    [RelayCommand]
    private void OpenVehicle(MaterialRefRow? r)
    {
        if (r is not null) ShellViewModel.Current?.GoToVehicle(r.Id);
    }

    /// <summary>Muadil malzeme satırına tıkla → o malzemeyi seçip detayını aç (kullanıcı isteği 2026-07-25).
    /// Listede varsa o satır seçilir (vurgulanır); yoksa ref'ten geçici satır seçilir → OnSelectedChanged detayı
    /// yine sunucudan/yerelden yükler (başka sayfada/şubede olsa bile detay görüntülenir).</summary>
    [RelayCommand]
    private void OpenMaterial(MaterialRefRow? r)
    {
        if (r is null) return;
        Selected = Items.FirstOrDefault(x => x.Id == r.Id)
            ?? new MaterialRow(r.Id, r.Code, r.Name, null, 0, "TRY", 0, 0);
    }

    /// <summary>⭐ ADR-182 (PK-F1=A): fotoğraflar artık SUNUCUYA yüklenir → başka makine/kullanıcı da görür.
    /// Çevrimdışıysa yerele YAZILMAZ (PK-F4=A): kullanıcı "yüklendi" sanmasın diye NET uyarı verilir.</summary>
    private async Task SaveStagedPhotosAsync(string materialId)
    {
        if (Photos.Count == 0) return;
        var sonuc = await DesktopPhotos.KaydetAsync("material", materialId, Photos.Select(p => p.LocalPath));
        if (sonuc.Cevrimdisi)
            Status = "Kayıt tamam; fotoğraflar YÜKLENEMEDİ (çevrimdışı). Çevrimiçi olduğunuzda fotoğrafı yeniden ekleyin.";
        else if (sonuc.Hata is not null)
            Status = "Kayıt tamam; fotoğraf yüklenemedi: " + sonuc.Hata;
    }

    /// <summary>Kayıtlı fotoğrafları SUNUCUDAN yükler; çevrimdışıysa bu makinedeki eski kopyalara düşer
    /// ve kullanıcıya durumu söyler (sessizce boş göstermez).</summary>
    private async Task LoadDetailPhotosAsync(string materialId)
    {
        DetailPhotos.Clear();
        var (fotograflar, cevrimdisi) = await DesktopPhotos.YukleAsync(_session, "material", materialId);
        foreach (var f in fotograflar)
        {
            try { DetailPhotos.Add(new DetailPhoto(f.FileId, new Bitmap(new MemoryStream(f.Bytes)))); }
            catch { /* bozuk görsel atlanır */ }
        }
        PhotosOffline = cevrimdisi;
    }

    /// <summary>Çevrimdışı görüntüleme uyarısı — ekranda küçük bir not olarak gösterilir.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PhotosOfflineNote))]
    private bool _photosOffline;
    public string? PhotosOfflineNote => PhotosOffline
        ? "Çevrimdışı: yalnız bu bilgisayardaki fotoğraflar gösteriliyor."
        : null;

    /// <summary>PK-F3 — fotoğraf silme düğmesi YALNIZ düzenleme modunda ve SİLME yetkisiyle görünür.</summary>
    public bool CanDeletePhoto => CanDelete && IsEditMode;

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
    /// <summary>Muadil arama/seçim listelerinde gösterim (kullanıcı isteği 2026-08-08): "KOD — Ad".
    /// Arama zaten hem kod hem ad üzerinden yapılıyordu; kod görünmediği için "kodla aramıyor" gibi duruyordu.</summary>
    public string CodeNameDisplay => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} — {Name}";
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
