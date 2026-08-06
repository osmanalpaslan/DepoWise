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
using DepoWise.Application.Maintenance;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Desktop.Controls;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Araç detayı "İşlem Geçmişi" sekmesi satırı (madde 4, kullanıcı isteği 2026-08-06): Günlük Faaliyet
/// modülünden loglanan hareketler + sistem olayları (oluşturma/şube transferi/güncelleme/sayaç) birleşimi.
/// CanOpenRecord=true ise "Kaydı Görüntüle" Günlük Faaliyet ekranına yönlendirir (madde 5); sistem satırları
/// zaten bu ekranda görüntülendiği için buton gösterilmez.</summary>
public sealed record MovementDisplay(long DateRaw, string DateText, string Kind, string Description, bool CanOpenRecord = false);

/// <summary>Araçlar — liste + arama + durum/bakım-muayene uyarı badge'i + yeni araç. VehicleService üzerine.</summary>
public sealed partial class VehiclesViewModel : ViewModelBase, IDeepLinkTarget, IListGridViewModel, IRefreshable
{
    ICommand IListGridViewModel.SortByCommand => SortByCommand;
    /// <summary>Eşitleme yeni veri getirince açık ekranı yenile (kullanıcı isteği 2026-07-19).</summary>
    public void RefreshData() => Load();
    private readonly SessionContext _session;

    public ObservableCollection<VehicleRow> Items { get; } = new();
    /// <summary>Durum seçenekleri ORTAK listeden gelir (DepoWise.Application.Ui.VehicleStatus) — eskiden
    /// burada ham kodlar ("active"/"passive") elle yazılıydı ve kutuda Türkçe değil KOD görünüyordu.</summary>
    public ObservableCollection<StatusPick> StatusOptions { get; } =
        new(DepoWise.Application.Ui.VehicleStatus.All.Select(x => new StatusPick(x.Code, x.Label)));
    public ObservableCollection<string> MeterUnits { get; } = new() { "km", "hour" };

    [ObservableProperty] private string? _status;

    // ── Araç Listesi — kolon bazlı filtre + sayfalama (kullanıcı isteği 2026-07-17) ──
    public IReadOnlyList<int> PageSizes { get; } = new[] { 25, 50, 100, 200 };
    [ObservableProperty] private List<string> _visibleColumns = VehicleListColumns.DefaultVisible.ToList();
    public ObservableCollection<ColumnFilterItem> FilterFields { get; } = new();
    /// <summary>Başlık-altı filtre satırı (madde 4, 2026-08-06) — bkz. MaterialsViewModel aynı yorum.</summary>
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
    public (string? SortColumn, bool SortDesc) SortState => (_sortColumn, _sortDesc);

    [RelayCommand]
    private void SortBy(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (_sortColumn != key) { _sortColumn = key; _sortDesc = false; }
        else if (!_sortDesc) { _sortDesc = true; }
        else { _sortColumn = null; _sortDesc = false; }
        OnPropertyChanged(nameof(SortState));
        Page = 1; Load();
    }

    // ── Kolon genişlikleri — kişiye özel, manuel ayarlanabilir (kullanıcı isteği 2026-07-18, madde 3) ──
    private static readonly Dictionary<string, double> DefaultColWidths = new()
    {
        [VehicleListColumns.InternalCode] = 100, [VehicleListColumns.Plate] = 100, [VehicleListColumns.ProductionYear] = 90,
        [VehicleListColumns.Meter] = 100, [VehicleListColumns.Status] = 100, [VehicleListColumns.StatusNote] = 140,
        [VehicleListColumns.VehicleType] = 110, [VehicleListColumns.Category] = 110, [VehicleListColumns.Brand] = 100,
        [VehicleListColumns.Model] = 110, [VehicleListColumns.Branch] = 130, [VehicleListColumns.Driver] = 130,
        [VehicleListColumns.ChassisNo] = 130, [VehicleListColumns.EngineNo] = 130,
    };

    [ObservableProperty] private Dictionary<string, double> _colWidths = new(DefaultColWidths);

    public void PreviewColumnWidth(string key, double newWidth)
    {
        var w = Math.Max(40, Math.Min(600, newWidth));
        ColWidths = new Dictionary<string, double>(ColWidths) { [key] = w };
    }

    public double GetColumnWidth(string key) => ColWidths.TryGetValue(key, out var w) ? w : 100;

    public void CommitColumnWidth()
    {
        try { DesktopServices.ListPrefs.SaveWidths(_session, "vehicles", ColWidths.ToDictionary(k => k.Key, v => (int)v.Value)); }
        catch { }
    }

    partial void OnVisibleColumnsChanged(List<string> value) => RebuildFilterFields();

    partial void OnPageSizeChanged(int value)
    {
        if (_suppressPageSizeReload) return;
        try { DesktopServices.ListPrefs.SavePageSize(_session, "vehicles", value); } catch { }   // kişiye özel hatırla
        Page = 1; Load();
    }

    private void RebuildFilterFields()
    {
        var old = FilterFields.ToDictionary(f => f.Key, f => f.Value);
        FilterFields.Clear();
        foreach (var key in VisibleColumns)
        {
            var col = VehicleListColumns.All.FirstOrDefault(c => c.Key == key);
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

    /// <summary>"Excel'e Aktar" (kullanıcı isteği 2026-07-19) — bkz. MaterialsViewModel.ExportExcel (aynı desen).</summary>
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
            var rows = DesktopServices.Vehicles.SearchGridAll(_session, BuildFilter(), _sortColumn, _sortDesc);
            var path = await FilePickerService.SaveExcelAsync("Araclar.xlsx");
            if (path is null) return;
            var bytes = DesktopServices.Excel.Export(VehicleService.ToTableModel(rows));
            await System.IO.File.WriteAllBytesAsync(path, bytes);
        }
        catch (Exception ex) { Status = "Excel'e aktarılamadı: " + ex.Message; }
        finally { IsExporting = false; }
    }

    private VehicleGridFilter BuildFilter()
    {
        string? V(string key)
        {
            var f = FilterFields.FirstOrDefault(x => x.Key == key);
            return string.IsNullOrWhiteSpace(f?.Value) ? null : f!.Value.Trim();
        }
        return new VehicleGridFilter(
            V(VehicleListColumns.InternalCode), V(VehicleListColumns.Plate), V(VehicleListColumns.ProductionYear),
            V(VehicleListColumns.Meter), V(VehicleListColumns.Status), V(VehicleListColumns.StatusNote),
            V(VehicleListColumns.VehicleType), V(VehicleListColumns.Category), V(VehicleListColumns.Brand),
            V(VehicleListColumns.Model), V(VehicleListColumns.Branch), V(VehicleListColumns.Driver),
            V(VehicleListColumns.ChassisNo), V(VehicleListColumns.EngineNo));
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
        var available = VehicleListColumns.All.Select(c => (c.Key, c.Label)).ToList();
        var chosen = await ColumnPickerService.PickAsync(available, VisibleColumns);
        if (chosen is null) return;
        VisibleColumns = chosen;
        DesktopServices.ListPrefs.SaveColumns(_session, "vehicles", chosen);
        Load();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;

    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    [ObservableProperty] private bool _showAdd;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CodeError))]
    [NotifyPropertyChangedFor(nameof(HasCodeError))]
    private string _newCode = "";

    [ObservableProperty] private string _newPlate = "";
    [ObservableProperty] private int _newYear;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewMaintenance))]
    private StatusPick? _newStatusPick;

    /// <summary>Seçili durumun KODU (servise bu gider). Seçim yoksa varsayılan "aktif".</summary>
    private string NewStatus => NewStatusPick?.Code ?? DepoWise.Application.Ui.VehicleStatus.Active;

    /// <summary>Koddan seçim nesnesini bulur (düzenlemeye girerken / formu temizlerken).</summary>
    private StatusPick PickStatus(string? code)
        => StatusOptions.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase))
           ?? StatusOptions.First();
    [ObservableProperty] private decimal _newMeter;
    [ObservableProperty] private string _newMeterUnit = "km";
    [ObservableProperty] private string _newChassisNo = "";
    [ObservableProperty] private string _newEngineNo = "";
    [ObservableProperty] private string _newStatusNote = "";

    /// <summary>"Durum Açıklaması" alanı görünsün mü? Bakımda VE Arızalı durumlarında anlamlıdır.</summary>
    public bool IsNewMaintenance => DepoWise.Application.Ui.VehicleStatus.NeedsNote(NewStatus);

    // ── Paylaşılan araç tanımları (LookupService) ──
    public ObservableCollection<LookupItem> VehicleTypes { get; } = new();
    public ObservableCollection<LookupItem> VehicleCategories { get; } = new();
    public ObservableCollection<LookupItem> VehicleBrands { get; } = new();
    public ObservableCollection<LookupItem> VehicleModels { get; } = new();
    public ObservableCollection<LookupItem> Branches { get; } = new();
    public ObservableCollection<LookupItem> Drivers { get; } = new();

    [ObservableProperty] private LookupItem? _selVehicleType;
    [ObservableProperty] private LookupItem? _selCategory;
    [ObservableProperty] private LookupItem? _selBrand;
    [ObservableProperty] private LookupItem? _selModel;
    [ObservableProperty] private LookupItem? _selBranch;
    [ObservableProperty] private LookupItem? _selDriver;
    private bool _vehLookupsLoaded;

    partial void OnSelBrandChanged(LookupItem? value)
    {
        SelModel = null;
        VehicleModels.Clear();
        if (value is null) return;
        try { foreach (var m in DesktopServices.Lookups.ListVehicleModels(_session, value.Id)) VehicleModels.Add(m); }
        catch { /* model yoksa sessiz */ }
    }

    // ── Şablon seç → formu otomatik doldur (yalnız yeni kayıt) ──
    public ObservableCollection<VehicleTemplateRow> Templates { get; } = new();
    [ObservableProperty] private VehicleTemplateRow? _selectedTemplate;
    private string? _templateId;

    partial void OnSelectedTemplateChanged(VehicleTemplateRow? value)
    {
        if (value is null) { _templateId = null; return; }
        _templateId = value.Id;
        if (IsEditMode) return;   // DÜZENLEME: yalnız BAĞLA (_templateId ayarlandı); alanları/kodu EZME
        SelVehicleType = VehicleTypes.FirstOrDefault(x => x.Id == value.VehicleTypeId);
        SelCategory = VehicleCategories.FirstOrDefault(x => x.Id == value.CategoryId);
        SelBrand = VehicleBrands.FirstOrDefault(x => x.Id == value.BrandId); // modelleri yükler
        SelModel = VehicleModels.FirstOrDefault(x => x.Id == value.VehicleModelId);
        if (value.ProductionYear is > 0) NewYear = value.ProductionYear!.Value;
        // İç kod: kullanıcı boş bıraktıysa örnek koddan sonrakini üret
        if (string.IsNullOrWhiteSpace(NewCode) && !string.IsNullOrWhiteSpace(value.InternalCode))
        {
            try { NewCode = DesktopServices.VehicleTemplates.GenerateNextInternalCode(_session, value.InternalCode!); }
            catch { }
        }
    }

    // ── Inline "+" yeni tanım ──
    [ObservableProperty] private bool _isAddingType; [ObservableProperty] private string _newTypeName = "";
    [ObservableProperty] private bool _isAddingCat; [ObservableProperty] private string _newCatName = "";
    [ObservableProperty] private bool _isAddingBrand; [ObservableProperty] private string _newBrandName = "";
    [ObservableProperty] private bool _isAddingModel; [ObservableProperty] private string _newModelName = "";
    [ObservableProperty] private bool _isAddingBranch; [ObservableProperty] private string _newBranchName = "";
    [ObservableProperty] private bool _isAddingDriver; [ObservableProperty] private string _newDriverName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CodeError))]
    [NotifyPropertyChangedFor(nameof(HasCodeError))]
    private bool _triedSave;

    public string? CodeError => TriedSave && string.IsNullOrWhiteSpace(NewCode) ? "İç kod zorunlu." : null;
    public bool HasCodeError => CodeError != null;

    public bool CanWrite => AccessControl.Can(_session, "vehicles", PermissionAction.Create);
    public string? AddButtonText => CanWrite ? "Yeni Araç" : null;

    public VehiclesViewModel(SessionContext session)
    {
        _session = session;
        var saved = DesktopServices.ListPrefs.GetColumns(session, "vehicles");
        VisibleColumns = saved is { Count: > 0 } ? saved.ToList() : VehicleListColumns.DefaultVisible.ToList();
        _suppressPageSizeReload = true;
        try { PageSize = DesktopServices.ListPrefs.GetPageSize(session, "vehicles") ?? 25; }
        finally { _suppressPageSizeReload = false; }
        var savedWidths = DesktopServices.ListPrefs.GetWidths(session, "vehicles");
        if (savedWidths is { Count: > 0 })
        {
            var merged = new Dictionary<string, double>(DefaultColWidths);
            foreach (var (k, v) in savedWidths) merged[k] = v;
            ColWidths = merged;
        }
        Load();
    }

    /// <summary>Listeden id ile araç seç (çapraz navigasyon: malzeme detayından gelinince).</summary>
    public void SelectById(string vehicleId)
    {
        var row = Items.FirstOrDefault(r => r.Id == vehicleId);
        if (row is not null) Selected = row;
    }

    /// <summary>Köprü: araç kaydını seçip detayını açar (Ana Ekran uyarısından gelince).</summary>
    public void OpenEntity(string entityId) => SelectById(entityId);

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

            // Uyarı haritaları (bakım + muayene) — araç başına en kötü seviye
            var maint = SafeMaint();
            var insp = SafeInsp();

            var grid = DesktopServices.Vehicles.SearchGrid(_session, BuildFilter(), Page, PageSize, _sortColumn, _sortDesc);
            foreach (var v in grid.Items)
            {
                var (kind, text) = CombineAlert(
                    maint.TryGetValue(v.Id, out var ml) ? ml : (AlertLevel?)null,
                    insp.TryGetValue(v.Id, out var il) ? il : (DateAlertLevel?)null);
                Items.Add(new VehicleRow(v.Id, v.InternalCode, v.Plate, v.Status, v.Meter, v.MeterUnit, v.ProductionYear, kind, text,
                    v.StatusNote ?? "", v.VehicleType ?? "", v.Category ?? "", v.Brand ?? "", v.Model ?? "",
                    v.Branch ?? "", v.Driver ?? "", v.ChassisNo ?? "", v.EngineNo ?? ""));
            }
            TotalCount = grid.TotalCount; TotalPages = grid.TotalPages;
            Page = grid.Page;
            _suppressPageSizeReload = true;
            try { PageSize = grid.PageSize; } finally { _suppressPageSizeReload = false; }
            RebuildPageNumbers();
            Status = $"{TotalCount} araç — sayfa {Page} / {TotalPages}";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        if (selectedId is not null)
            Selected = Items.FirstOrDefault(x => x.Id == selectedId);   // bulunamazsa (silindi) panel doğal olarak kapanır
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRows));
    }

    private Dictionary<string, AlertLevel> SafeMaint()
    {
        try
        {
            return DesktopServices.Maintenance.GetAlerts(_session)
                .GroupBy(a => a.VehicleId)
                .ToDictionary(g => g.Key, g => (AlertLevel)g.Max(x => (int)x.Level));
        }
        catch { return new(); }
    }

    private Dictionary<string, DateAlertLevel> SafeInsp()
    {
        try
        {
            return DesktopServices.Inspection.GetAlerts(_session)
                .GroupBy(a => a.VehicleId)
                .ToDictionary(g => g.Key, g => (DateAlertLevel)g.Max(x => (int)x.Level));
        }
        catch { return new(); }
    }

    private static (BadgeKind, string) CombineAlert(AlertLevel? m, DateAlertLevel? i)
    {
        bool overdue = m == AlertLevel.Overdue || i == DateAlertLevel.Expired;
        bool soon = m is AlertLevel.Critical or AlertLevel.Approaching || i == DateAlertLevel.Approaching;
        if (overdue) return (BadgeKind.Danger, "Gecikti");
        if (soon) return (BadgeKind.Warning, "Yaklaşıyor");
        return (BadgeKind.Success, "Güncel");
    }

    [RelayCommand]
    private async Task Add()
    {
        TriedSave = true;
        bool editing = IsEditMode;
        if (editing ? !CanEdit : !CanWrite) { Status = "Yetki yok."; return; }
        if (!await BranchGuard.RequireBranchAsync(_session, "Araçlar")) return;   // "Tüm Şubeler" modunda işlem yok
        if (string.IsNullOrWhiteSpace(NewCode)) { Status = "İç kod zorunlu."; return; }
        // Zorunlu: şantiye/şube (madde 8) + makul üretim yılı (madde 1).
        if (SelBranch is null) { Status = "Araç için şantiye/şube seçimi zorunludur."; return; }
        if (!DepoWise.Application.Ui.FieldChecks.YearInRange(NewYear > 0 ? NewYear : (int?)null))
        { Status = $"Üretim yılı {DepoWise.Application.Ui.FieldChecks.MinVehicleYear}–{DepoWise.Application.Ui.FieldChecks.MaxVehicleYear} aralığında olmalı."; return; }
        // Yumuşak uyarılar (kullanıcı yine de geçebilir): plaka biçimi (madde 2) + çok büyük sayaç (madde 7).
        if (!DepoWise.Application.Ui.FieldChecks.PlateLooksTurkish(NewPlate)
            && !await ConfirmService.AskAsync("Plaka standart Türk plaka biçimine (34 ABC 123) uymuyor. İş makinesi/plakasız araç ise geçebilirsiniz.\n\nYine de kaydedilsin mi?", "Plaka Uyarısı", "Evet, Kaydet")) return;
        if (DepoWise.Application.Ui.FieldChecks.IsSuspiciouslyLarge(NewMeter)
            && !await ConfirmService.AskAsync($"Sayaç değeri çok büyük görünüyor ({NewMeter:0.##}). Emin misiniz?", "Sayaç Uyarısı", "Evet, Doğru")) return;
        if (!editing)
        {
            // Şablon dışı kayıt uyarısı (tek tip kayıt için).
            if (SelectedTemplate is null)
            {
                if (!await ConfirmService.AskAsync("Ana Yetkiliye Bilgi verilmelidir! Şablon dışı kayıt girmektesiniz!\n\nYine de devam edilsin mi?",
                        "Şablon Dışı Kayıt", "Evet, Devam Et", "Vazgeç", danger: true)) return;
            }
            else if (!await ConfirmService.AskAsync("Yeni araç kaydedilsin mi?", "Kaydet")) return;
        }

        if (editing)
        {
            if (Detail is not null)
            {
                var sum = new ChangeSummary();
                sum.Add("İç Kod", Detail.InternalCode, NewCode.Trim());
                sum.Add("Plaka", Detail.Plate, string.IsNullOrWhiteSpace(NewPlate) ? null : NewPlate.Trim());
                sum.Add("Yıl", Detail.ProductionYear, NewYear > 0 ? NewYear : (int?)null);
                sum.Add("Durum", Detail.Status, NewStatus);
                sum.Add("Sayaç", Detail.CurrentMeter, NewMeter);
                sum.Add("Şasi No", Detail.ChassisNo, string.IsNullOrWhiteSpace(NewChassisNo) ? null : NewChassisNo.Trim());
                sum.Add("Motor No", Detail.EngineNo, string.IsNullOrWhiteSpace(NewEngineNo) ? null : NewEngineNo.Trim());
                sum.Add("Makine Tipi", Detail.VehicleTypeName, SelVehicleType?.Name);
                sum.Add("Kategori", Detail.CategoryName, SelCategory?.Name);
                sum.Add("Marka", Detail.BrandName, SelBrand?.Name);
                sum.Add("Model", Detail.VehicleModelName, SelModel?.Name);
                sum.Add("Şantiye", Detail.BranchName, SelBranch?.Name);
                sum.Add("Sürücü", Detail.DriverName, SelDriver?.Name);
                if (!await ConfirmService.AskAsync(sum.Build("Araç bilgileri güncellensin mi?"), "Kaydet")) return;
            }
            try
            {
                DesktopServices.Vehicles.Update(_session, EditId!, new UpdateVehicle(
                    Plate: string.IsNullOrWhiteSpace(NewPlate) ? null : NewPlate.Trim(),
                    ProductionYear: NewYear > 0 ? NewYear : (int?)null,
                    Status: NewStatus,
                    StatusNote: IsNewMaintenance && !string.IsNullOrWhiteSpace(NewStatusNote) ? NewStatusNote.Trim() : null,
                    ChassisNo: string.IsNullOrWhiteSpace(NewChassisNo) ? null : NewChassisNo.Trim(),
                    EngineNo: string.IsNullOrWhiteSpace(NewEngineNo) ? null : NewEngineNo.Trim(),
                    VehicleTypeId: SelVehicleType?.Id, CategoryId: SelCategory?.Id,
                    BrandId: SelBrand?.Id, VehicleModelId: SelModel?.Id,
                    BranchId: SelBranch?.Id, DriverPersonnelId: SelDriver?.Id,
                    TemplateId: _templateId),   // düzenlemede şablona bağla/koru (yüklenen mevcut bağ)
                    // DÜZENLEME KİLİDİ: formu açtığımız andaki sürüm — kayıt arada değiştiyse sessizce ezme.
                    expectedVersion: Detail?.Version);

                SaveStagedPhotos(EditId!);

                if (NewMeter != _loadedMeter)
                {
                    try { DesktopServices.Vehicles.SetMeter(_session, EditId!, NewMeter, "vehicle_form"); }
                    catch (MeterBackwardException) { Status = "Araç güncellendi (sayaç geriye alınamaz, değişmedi)."; Clear(); Load(); return; }
                }
                Clear(); Load(); Status = "Araç güncellendi.";
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
            var id = DesktopServices.Vehicles.Create(_session, new NewVehicle(
                InternalCode: NewCode.Trim(),
                Plate: string.IsNullOrWhiteSpace(NewPlate) ? null : NewPlate.Trim(),
                ProductionYear: NewYear > 0 ? NewYear : (int?)null,
                CurrentMeter: NewMeter, MeterUnit: NewMeterUnit,
                BranchId: SelBranch?.Id, DriverPersonnelId: SelDriver?.Id,
                ChassisNo: string.IsNullOrWhiteSpace(NewChassisNo) ? null : NewChassisNo.Trim(),
                EngineNo: string.IsNullOrWhiteSpace(NewEngineNo) ? null : NewEngineNo.Trim(),
                Status: NewStatus,
                StatusNote: IsNewMaintenance && !string.IsNullOrWhiteSpace(NewStatusNote) ? NewStatusNote.Trim() : null,
                VehicleTypeId: SelVehicleType?.Id, CategoryId: SelCategory?.Id,
                BrandId: SelBrand?.Id, VehicleModelId: SelModel?.Id,
                TemplateId: _templateId));
            SaveStagedPhotos(id);
            Clear();
            Load();
            Status = "Araç eklendi.";
        }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }

    [RelayCommand]
    private void ToggleAdd()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        ShowAdd = !ShowAdd;
        if (ShowAdd) LoadVehLookups();
    }

    [RelayCommand]
    private void Clear()
    {
        NewCode = ""; NewPlate = ""; NewYear = 0; NewStatusPick = PickStatus(DepoWise.Application.Ui.VehicleStatus.Active); NewMeter = 0; NewMeterUnit = "km";
        NewChassisNo = ""; NewEngineNo = ""; NewStatusNote = "";
        SelVehicleType = null; SelCategory = null; SelBrand = null; SelModel = null; SelBranch = null; SelDriver = null;
        IsAddingType = IsAddingCat = IsAddingBrand = IsAddingModel = IsAddingBranch = IsAddingDriver = false;
        Photos.Clear();
        SelectedTemplate = null; _templateId = null;
        EditId = null;
        TriedSave = false; ShowAdd = false;
    }

    private void LoadVehLookups()
    {
        if (_vehLookupsLoaded) return;
        try
        {
            VehicleTypes.Clear(); foreach (var x in DesktopServices.Lookups.List(_session, "vehicle_types")) VehicleTypes.Add(x);
            VehicleCategories.Clear(); foreach (var x in DesktopServices.Lookups.List(_session, "vehicle_categories")) VehicleCategories.Add(x);
            VehicleBrands.Clear(); foreach (var x in DesktopServices.Lookups.ListBrands(_session, "vehicle")) VehicleBrands.Add(x);
            Branches.Clear(); foreach (var x in DesktopServices.Lookups.List(_session, "branches")) Branches.Add(x);
            Drivers.Clear(); foreach (var x in DesktopServices.Lookups.ListPersonnel(_session)) Drivers.Add(x);
            Templates.Clear(); foreach (var t in DesktopServices.VehicleTemplates.List(_session)) Templates.Add(t);
            _vehLookupsLoaded = true;
        }
        catch (Exception ex) { Status = "Tanımlar yüklenemedi: " + ex.Message; }
    }

    // ── Inline "+" komutları ──
    [RelayCommand] private void StartAddType() { IsAddingType = true; NewTypeName = ""; }
    [RelayCommand] private void CancelAddType() { IsAddingType = false; NewTypeName = ""; }
    [RelayCommand] private void ConfirmAddType() => AddLookup(NewTypeName, () => DesktopServices.Lookups.AddVehicleType(_session, NewTypeName.Trim()), VehicleTypes, x => SelVehicleType = x, () => { IsAddingType = false; NewTypeName = ""; });

    [RelayCommand] private void StartAddCat() { IsAddingCat = true; NewCatName = ""; }
    [RelayCommand] private void CancelAddCat() { IsAddingCat = false; NewCatName = ""; }
    [RelayCommand] private void ConfirmAddCat() => AddLookup(NewCatName, () => DesktopServices.Lookups.AddVehicleCategory(_session, NewCatName.Trim()), VehicleCategories, x => SelCategory = x, () => { IsAddingCat = false; NewCatName = ""; });

    [RelayCommand] private void StartAddBrand() { IsAddingBrand = true; NewBrandName = ""; }
    [RelayCommand] private void CancelAddBrand() { IsAddingBrand = false; NewBrandName = ""; }
    [RelayCommand] private void ConfirmAddBrand() => AddLookup(NewBrandName, () => DesktopServices.Lookups.AddVehicleBrand(_session, NewBrandName.Trim()), VehicleBrands, x => SelBrand = x, () => { IsAddingBrand = false; NewBrandName = ""; });

    [RelayCommand] private void StartAddModel() { if (SelBrand is null) { Status = "Önce marka seçin."; return; } IsAddingModel = true; NewModelName = ""; }
    [RelayCommand] private void CancelAddModel() { IsAddingModel = false; NewModelName = ""; }
    [RelayCommand] private void ConfirmAddModel() { if (SelBrand is null) return; AddLookup(NewModelName, () => DesktopServices.Lookups.AddVehicleModel(_session, SelBrand!.Id, NewModelName.Trim()), VehicleModels, x => SelModel = x, () => { IsAddingModel = false; NewModelName = ""; }); }

    [RelayCommand] private void StartAddBranch() { IsAddingBranch = true; NewBranchName = ""; }
    [RelayCommand] private void CancelAddBranch() { IsAddingBranch = false; NewBranchName = ""; }
    [RelayCommand] private void ConfirmAddBranch() => AddLookup(NewBranchName, () => DesktopServices.Lookups.AddBranch(_session, NewBranchName.Trim()), Branches, x => SelBranch = x, () => { IsAddingBranch = false; NewBranchName = ""; });

    [RelayCommand] private void StartAddDriver() { IsAddingDriver = true; NewDriverName = ""; }
    [RelayCommand] private void CancelAddDriver() { IsAddingDriver = false; NewDriverName = ""; }
    [RelayCommand] private void ConfirmAddDriver() => AddLookup(NewDriverName, () => DesktopServices.Lookups.AddPersonnel(_session, NewDriverName.Trim(), "Şoför"), Drivers, x => SelDriver = x, () => { IsAddingDriver = false; NewDriverName = ""; });

    private void AddLookup(string name, Func<string> add, ObservableCollection<LookupItem> coll, Action<LookupItem> select, Action done)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        try { var id = add(); var item = new LookupItem(id, name.Trim()); coll.Add(item); select(item); done(); }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }

    // ===== Detay (salt okuma) / Düzenle (form) / Sil =====
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private VehicleRow? _selected;

    [ObservableProperty] private VehicleDetail? _detail;
    private decimal _loadedMeter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    [NotifyPropertyChangedFor(nameof(FormTitle))]
    private string? _editId;
    public bool IsEditMode => EditId != null;
    public string FormTitle => IsEditMode ? "ARAÇ DÜZENLE" : "YENİ ARAÇ";

    public bool HasSelection => Selected != null;
    public bool CanEdit => AccessControl.Can(_session, "vehicles", PermissionAction.Edit);
    public bool CanDelete => AccessControl.Can(_session, "vehicles", PermissionAction.Delete);

    partial void OnSelectedChanged(VehicleRow? value)
    {
        if (value is null) { Detail = null; DetailPhotos.Clear(); ClearVehicleTabs(); return; }
        try { Detail = DesktopServices.Vehicles.Get(_session, value.Id); LoadDetailPhotos(value.Id); LoadVehicleTabs(value.Id, value.Code); }
        catch (Exception ex) { Status = "Detay yüklenemedi: " + ex.Message; }
    }

    /// <summary>Çift tık: ayrı pencerede Düzelt/Kaydet/Sil (kullanıcı isteği 2026-07-19). Tek tık mevcut detay
    /// panelini açar (korunur). Kaydedilir/silinirse liste yenilenir.</summary>
    [RelayCommand]
    private async Task QuickEditSelected()
    {
        if (Selected is null) return;
        var res = await QuickEditService.ShowVehicleAsync(_session, Selected.Id);
        // "stale" = düzenleme kilidi: kayıt biz açıkken değişti, kullanıcı "kapat ve yenile" dedi.
        if (res is "saved" or "deleted" or "stale")
        {
            if (res == "deleted") Selected = null;
            Load();
        }
    }

    // ── Araç detay sekmeleri (webteki gibi): Uyumlu Malzemeler / Muayene-Sigorta / Bakım / Hareketler ──
    public ObservableCollection<MaterialStock> VehicleMaterials { get; } = new();
    public ObservableCollection<InspectionRow> VehicleInspections { get; } = new();
    public ObservableCollection<MaintenanceRow> VehicleMaintenances { get; } = new();
    public ObservableCollection<MovementDisplay> VehicleMovements { get; } = new();

    private void ClearVehicleTabs()
    {
        VehicleMaterials.Clear(); VehicleInspections.Clear(); VehicleMaintenances.Clear(); VehicleMovements.Clear();
    }

    private void LoadVehicleTabs(string vehicleId, string code)
    {
        ClearVehicleTabs();
        try { foreach (var m in DesktopServices.Materials.MaterialsForVehicle(_session, vehicleId)) VehicleMaterials.Add(m); } catch { }
        try { foreach (var i in DesktopServices.Inspection.List(_session).Where(x => x.VehicleCode == code)) VehicleInspections.Add(i); } catch { }
        try { foreach (var mt in DesktopServices.Maintenance.ListMaintenances(_session, vehicleId)) VehicleMaintenances.Add(mt); } catch { }

        // İşlem Geçmişi (madde 4): Günlük Faaliyet hareket kayıtları + sistem olayları (oluşturma/transfer/
        // güncelleme/sayaç) TEK kronolojik listede birleşir.
        var merged = new List<MovementDisplay>();
        try
        {
            foreach (var mv in DesktopServices.DailyActivity.GetForVehicle(_session, vehicleId, "movement"))
                merged.Add(new MovementDisplay(mv.ActivityDate,
                    DateTimeOffset.FromUnixTimeMilliseconds(mv.ActivityDate).LocalDateTime.ToString("dd.MM.yyyy"),
                    mv.MovementKind == "transfer" ? "Transfer" : "Hareket",
                    mv.Description ?? "", CanOpenRecord: true));
        }
        catch { }
        try
        {
            foreach (var h in DesktopServices.Vehicles.RecentHistory(_session, vehicleId, 100))
                merged.Add(new MovementDisplay(h.Date, h.DateText, "Sistem",
                    h.Detail is null ? h.Label : $"{h.Label} ({h.Detail})"));
        }
        catch { }
        foreach (var row in merged.OrderByDescending(x => x.DateRaw)) VehicleMovements.Add(row);
    }

    /// <summary>Seçili aracı düzenleme modunda forma yükler (tüm alanlar + lookup ön-seçim). Onay sorar.</summary>
    [RelayCommand]
    private async Task BeginEdit()
    {
        if (Detail is null) return;
        if (!CanEdit) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync("Bu aracı düzenlemek istiyor musunuz?", "Düzenle")) return;

        LoadVehLookups();
        var d = Detail;
        EditId = d.Id;
        NewCode = d.InternalCode;
        NewPlate = d.Plate ?? "";
        NewYear = d.ProductionYear ?? 0;
        NewStatusPick = PickStatus(d.Status);
        NewStatusNote = d.StatusNote ?? "";
        NewMeter = d.CurrentMeter; _loadedMeter = d.CurrentMeter;
        NewMeterUnit = d.MeterUnit;
        NewChassisNo = d.ChassisNo ?? "";
        NewEngineNo = d.EngineNo ?? "";
        SelVehicleType = VehicleTypes.FirstOrDefault(x => x.Id == d.VehicleTypeId);
        SelCategory = VehicleCategories.FirstOrDefault(x => x.Id == d.CategoryId);
        SelBranch = Branches.FirstOrDefault(x => x.Id == d.BranchId);
        SelDriver = Drivers.FirstOrDefault(x => x.Id == d.DriverPersonnelId);
        SelBrand = VehicleBrands.FirstOrDefault(x => x.Id == d.BrandId); // markaya bağlı modeller yüklenir
        SelModel = VehicleModels.FirstOrDefault(x => x.Id == d.VehicleModelId);
        // Mevcut şablon bağı (EditId set edildiği için changed-handler prefill YAPMAZ; yalnız bağ yüklenir).
        SelectedTemplate = Templates.FirstOrDefault(x => x.Id == d.TemplateId);
        TriedSave = false;
        ShowAdd = true;
    }

    [RelayCommand]
    private async Task RequestDelete()
    {
        if (!CanDelete) { Status = "Yetki yok."; return; }
        if (Selected is null) return;
        var ok = await ConfirmService.AskAsync(
            $"'{Selected.Code}' aracı silinsin mi? Kayıt çöp kutusuna alınır.",
            "Araç Sil", "Evet, Sil", "Vazgeç", danger: true);
        if (!ok) return;
        try
        {
            DesktopServices.Vehicles.Delete(_session, Selected.Id);
            Selected = null;
            Load();
            Status = "Araç silindi.";
        }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
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

    private void SaveStagedPhotos(string vehicleId)
    {
        foreach (var ph in Photos)
        {
            try
            {
                var bytes = File.ReadAllBytes(ph.LocalPath);
                DesktopServices.Files.SavePhoto(_session, "vehicle", vehicleId, Path.GetFileName(ph.LocalPath), null, bytes);
            }
            catch (Exception ex) { Status = "Foto kaydedilemedi: " + ex.Message; }
        }
    }

    private void LoadDetailPhotos(string vehicleId)
    {
        DetailPhotos.Clear();
        try
        {
            foreach (var f in DesktopServices.Files.GetPhotos(_session, "vehicle", vehicleId))
            {
                var bytes = DesktopServices.Storage.Read(f.StorageKey);
                DetailPhotos.Add(new DetailPhoto(f.Id, new Bitmap(new MemoryStream(bytes))));
            }
        }
        catch { /* foto yoksa sessiz */ }
    }
}

public sealed record VehicleRow(string Id, string Code, string? Plate, string Status, decimal Meter, string MeterUnit,
    int? Year, BadgeKind AlertKind, string AlertText,
    string StatusNote = "", string VehicleType = "", string Category = "", string Brand = "", string Model = "",
    string Branch = "", string Driver = "", string ChassisNo = "", string EngineNo = "")
{
    public string PlateDisplay => string.IsNullOrWhiteSpace(Plate) ? "—" : Plate!;
    public string MeterDisplay => $"{Meter:0.##} {MeterUnit}";
    public string YearDisplay => Year is > 0 ? Year!.Value.ToString() : "—";

    /// <summary>Durum metni ORTAK listeden (VehicleStatus) — yeni durum eklenince burası kendiliğinden doğrudur.</summary>
    public string StatusText => DepoWise.Application.Ui.VehicleStatus.Label(Status);
    public BadgeKind StatusKind => Status switch
    {
        "active" => BadgeKind.Success,
        "maintenance" => BadgeKind.Warning,
        "faulty" => BadgeKind.Danger,      // Arızalı: bakımdan daha acil → kırmızı
        "passive" => BadgeKind.Neutral,
        _ => BadgeKind.Neutral,
    };
}

/// <summary>Araç durumu seçim satırı — kutuda Türkçe ad görünür, servise KOD gider.</summary>
public sealed record StatusPick(string Code, string Label);
