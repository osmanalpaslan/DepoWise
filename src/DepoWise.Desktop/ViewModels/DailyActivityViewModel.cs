using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Günlük Faaliyet — bir günde yapılan TÜM işler tek ekranda. Tek "Yeni Kayıt Oluştur" + Kayıt Tipi
/// (Hareket / Transfer / Bakım) forma göre alanları değiştirir. Transfer → araç otomatik pasife. Bakım →
/// tek bakım kaydı + tek stok düşümü (Bakım Takibi'nde de görünür). Liste kolon bazlı filtre + sayfalama +
/// sıralama + Excel'e aktar kullanır (kullanıcı isteği 2026-07-19, ADR-087/088/089 deseninin AYNISI).
/// </summary>
public sealed partial class DailyActivityViewModel : ViewModelBase, IListGridViewModel, IRefreshable
{
    ICommand IListGridViewModel.SortByCommand => SortByCommand;
    /// <summary>Eşitleme yeni veri getirince açık ekranı yenile (kullanıcı isteği 2026-07-19).</summary>
    public void RefreshData() => Load();
    private readonly SessionContext _session;
    private bool _pickersLoaded;

    public bool CanWrite => AccessControl.Can(_session, "daily_activity", PermissionAction.Create);
    public bool CanDelete => AccessControl.Can(_session, "daily_activity", PermissionAction.Delete);

    public ObservableCollection<DailyActivityGridRow> Items { get; } = new();
    // "İlave Yağ/İlave Filtre/Tamir" (kullanıcı isteği 2026-07-19): Bakım ile AYNI alanlar, Bakım Tanımı/Alt
    // Bakım YOK (bkz. IsRealMaintenance/IsMaintenanceLike aşağıda).
    public ObservableCollection<string> KindOptions { get; } = new() { "Hareket", "Transfer", "Bakım", "İlave Yağ", "İlave Filtre", "Tamir", "Depo Çıkışı" };
    // Depo Çıkışı (kullanıcı isteği 2026-08-07): Giriş-Çıkış'takiyle AYNI ortak servis (StockService.IssueOut/
    // Transfer). Şube İçi = çıkış, Şube Dışı = transfer. Araç faaliyet "Transfer"inden (mevcut) BAĞIMSIZ.
    public ObservableCollection<string> ExitScopeOptions { get; } = new() { "Şube İçi", "Şube Dışı" };
    public ObservableCollection<VehicleListRow> Vehicles { get; } = new();
    public ObservableCollection<BranchRow> Branches { get; } = new();
    public ObservableCollection<LookupItem> Personnel { get; } = new();
    public Func<string, CancellationToken, Task<IEnumerable<object>>> VehiclePopulator => SearchPopulator.For(() => Vehicles, v => v.Display);
    public Func<string, CancellationToken, Task<IEnumerable<object>>> BranchPopulator => SearchPopulator.For(() => Branches, b => b.Name);
    public Func<string, CancellationToken, Task<IEnumerable<object>>> PersonnelPopulator => SearchPopulator.For(() => Personnel, p => p.Name);
    public ObservableCollection<MaintenanceDefinitionRow> MaintDefs { get; } = new();
    public ObservableCollection<MaintenanceDefinitionRow> MaintSubDefs { get; } = new();
    public ObservableCollection<MntMaterialLine> MntLines { get; } = new();
    public ObservableCollection<MaterialRefRow> MntMaterialResults { get; } = new();

    [ObservableProperty] private string? _status;

    // ── Faaliyet Listesi — kolon bazlı filtre + sayfalama (kullanıcı isteği 2026-07-19, ADR-087/088/089
    // deseninin AYNISI — bkz. VehiclesViewModel). "Tarih" filtre kutusu ALMAZ (yalnız sıralanır). ──
    public IReadOnlyList<int> PageSizes { get; } = new[] { 25, 50, 100, 200 };
    [ObservableProperty] private List<string> _visibleColumns = DailyActivityListColumns.DefaultVisible.ToList();
    public ObservableCollection<ColumnFilterItem> FilterFields { get; } = new();
    /// <summary>Başlık-altı filtre satırı (madde 4, 2026-08-06) — bkz. MaterialsViewModel aynı yorum.</summary>
    [ObservableProperty] private IReadOnlyDictionary<string, ColumnFilterItem> _filterFieldsByKey = new Dictionary<string, ColumnFilterItem>();
    public ObservableCollection<int> PageNumbers { get; } = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoPrev))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private int _page = 1;
    [ObservableProperty] private int _pageSize = 25;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private int _totalPages = 1;
    private bool _suppressPageSizeReload;

    public bool CanGoPrev => Page > 1;
    public bool CanGoNext => Page < TotalPages;

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

    private static readonly Dictionary<string, double> DefaultColWidths = new()
    {
        [DailyActivityListColumns.Date] = 100, [DailyActivityListColumns.Type] = 100, [DailyActivityListColumns.Vehicle] = 150,
        [DailyActivityListColumns.Route] = 170, [DailyActivityListColumns.Operator] = 130, [DailyActivityListColumns.Duration] = 80,
        [DailyActivityListColumns.Description] = 160,
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
        try { DesktopServices.ListPrefs.SaveWidths(_session, "daily_activity", ColWidths.ToDictionary(k => k.Key, v => (int)v.Value)); }
        catch { }
    }

    partial void OnVisibleColumnsChanged(List<string> value) => RebuildFilterFields();

    partial void OnPageSizeChanged(int value)
    {
        if (_suppressPageSizeReload) return;
        try { DesktopServices.ListPrefs.SavePageSize(_session, "daily_activity", value); } catch { }
        Page = 1; Load();
    }

    private void RebuildFilterFields()
    {
        var old = FilterFields.ToDictionary(f => f.Key, f => f.Value);
        FilterFields.Clear();
        foreach (var key in VisibleColumns.Where(k => k != DailyActivityListColumns.Date))
        {
            var col = DailyActivityListColumns.All.FirstOrDefault(c => c.Key == key);
            FilterFields.Add(new ColumnFilterItem(key, col?.Label ?? key, false)
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

    /// <summary>"Excel'e Aktar" (kullanıcı isteği 2026-07-19) — bkz. VehiclesViewModel.ExportExcel (aynı desen).</summary>
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
            var rows = DesktopServices.DailyActivity.SearchGridAll(_session, BuildFilter(), _sortColumn, _sortDesc);
            var path = await FilePickerService.SaveExcelAsync("GunlukFaaliyet.xlsx");
            if (path is null) return;
            var bytes = DesktopServices.Excel.Export(DailyActivityService.ToTableModel(rows));
            await System.IO.File.WriteAllBytesAsync(path, bytes);
        }
        catch (Exception ex) { Status = "Excel'e aktarılamadı: " + ex.Message; }
        finally { IsExporting = false; }
    }

    private DailyActivityGridFilter BuildFilter()
    {
        string? V(string key)
        {
            var f = FilterFields.FirstOrDefault(x => x.Key == key);
            return string.IsNullOrWhiteSpace(f?.Value) ? null : f!.Value.Trim();
        }
        return new DailyActivityGridFilter(
            V(DailyActivityListColumns.Type), V(DailyActivityListColumns.Vehicle), V(DailyActivityListColumns.Route),
            V(DailyActivityListColumns.Operator), V(DailyActivityListColumns.Duration), V(DailyActivityListColumns.Description));
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
        var available = DailyActivityListColumns.All.Select(c => (c.Key, c.Label)).ToList();
        var chosen = await ColumnPickerService.PickAsync(available, VisibleColumns);
        if (chosen is null) return;
        VisibleColumns = chosen;
        DesktopServices.ListPrefs.SaveColumns(_session, "daily_activity", chosen);
        Load();
    }

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
    [NotifyPropertyChangedFor(nameof(IsStockExit))]
    [NotifyPropertyChangedFor(nameof(IsInBranchExit))]
    [NotifyPropertyChangedFor(nameof(IsOutBranchExit))]
    [NotifyPropertyChangedFor(nameof(ShowExitTargetBranch))]
    [NotifyPropertyChangedFor(nameof(ShowExitPersonnel))]
    [NotifyPropertyChangedFor(nameof(DescriptionLabel))]
    private string _formKind = "Hareket";
    public bool IsTransfer => FormKind == "Transfer";
    /// <summary>Depo Çıkışı (MALZEME) — mevcut araç "Transfer"inden ayrı; StockService ile stok düşer.</summary>
    public bool IsStockExit => FormKind == "Depo Çıkışı";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInBranchExit))]
    [NotifyPropertyChangedFor(nameof(IsOutBranchExit))]
    [NotifyPropertyChangedFor(nameof(ShowExitTargetBranch))]
    [NotifyPropertyChangedFor(nameof(ShowExitPersonnel))]
    private string _exitScope = "Şube İçi";
    public bool IsInBranchExit => IsStockExit && ExitScope == "Şube İçi";
    public bool IsOutBranchExit => IsStockExit && ExitScope == "Şube Dışı";
    public bool ShowExitTargetBranch => IsOutBranchExit;
    public bool ShowExitPersonnel => IsInBranchExit;   // Şube Dışı'nda personel gizli (madde 6)

    // Depo Çıkışı alanları — tek malzeme + miktar (Giriş-Çıkış deseni). Personel/Araç: Şube İçi'de görünür.
    public ObservableCollection<MaterialRefRow> ExitMaterialResults { get; } = new();
    [ObservableProperty] private string _exitMaterialSearch = "";
    [ObservableProperty] private MaterialRefRow? _exitMaterial;
    [ObservableProperty] private string _exitBalanceText = "";
    [ObservableProperty] private decimal _exitQuantity;
    [ObservableProperty] private BranchRow? _exitToBranch;
    [ObservableProperty] private LookupItem? _exitPersonnel;
    /// <summary>Açıklama alanı etiketi: arıza-onarım türlerinde (İlave Yağ/Filtre/Tamir) "Arıza Açıklaması",
    /// diğerlerinde "Açıklama" (kullanıcı isteği 2026-07-19). Aynı alana (description) yazılır; şema değişmez.</summary>
    public string DescriptionLabel => FormKind is "İlave Yağ" or "İlave Filtre" or "Tamir" ? "Arıza Açıklaması" : "Açıklama";
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
        var saved = DesktopServices.ListPrefs.GetColumns(session, "daily_activity");
        VisibleColumns = saved is { Count: > 0 } ? saved.ToList() : DailyActivityListColumns.DefaultVisible.ToList();
        _suppressPageSizeReload = true;
        try { PageSize = DesktopServices.ListPrefs.GetPageSize(session, "daily_activity") ?? 25; }
        finally { _suppressPageSizeReload = false; }
        var savedWidths = DesktopServices.ListPrefs.GetWidths(session, "daily_activity");
        if (savedWidths is { Count: > 0 })
        {
            var merged = new Dictionary<string, double>(DefaultColWidths);
            foreach (var (k, v) in savedWidths) merged[k] = v;
            ColWidths = merged;
        }
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            var grid = DesktopServices.DailyActivity.SearchGrid(_session, BuildFilter(), Page, PageSize, _sortColumn, _sortDesc);
            foreach (var a in grid.Items) Items.Add(a);
            TotalCount = grid.TotalCount; TotalPages = grid.TotalPages;
            Page = grid.Page;
            _suppressPageSizeReload = true;
            try { PageSize = grid.PageSize; } finally { _suppressPageSizeReload = false; }
            RebuildPageNumbers();
            Status = $"{TotalCount} faaliyet — sayfa {Page} / {TotalPages}";
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
        ExitScope = "Şube İçi"; ExitMaterial = null; ExitMaterialSearch = ""; ExitBalanceText = "";
        ExitQuantity = 0; ExitToBranch = null; ExitPersonnel = null; RefreshExitMaterials();
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

    // ── Depo Çıkışı: tek malzeme seçici (Giriş-Çıkış deseni) ──
    partial void OnExitMaterialSearchChanged(string value) => RefreshExitMaterials();
    private void RefreshExitMaterials()
    {
        ExitMaterialResults.Clear();
        var term = ExitMaterialSearch?.Trim();
        try
        {
            var page = DesktopServices.Materials.List(_session, new PageRequest { Limit = 30 },
                string.IsNullOrEmpty(term) ? null : term);
            foreach (var m in page.Items) ExitMaterialResults.Add(new MaterialRefRow(m.Id, m.Code, m.Name));
        }
        catch { }
    }
    [RelayCommand] private void PickExitMaterial(MaterialRefRow? m)
    {
        if (m is null) return;
        ExitMaterial = m;
        ExitMaterialSearch = $"{m.Code} - {m.Name}";
        ExitMaterialResults.Clear();
        try { ExitBalanceText = $"Mevcut stok: {DesktopServices.Stock.GetBalance(m.Id):0.##}"; }
        catch { ExitBalanceText = ""; }
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

        // Depo Çıkışı (kullanıcı isteği 2026-08-07): Giriş-Çıkış ile AYNI ortak servis. Araç OPSİYONEL → araç
        // zorunluluk kontrolünün ÖNÜNDE ele alınır. Şube İçi = IssueOut, Şube Dışı = Transfer.
        if (IsStockExit)
        {
            if (!await BranchGuard.RequireBranchAsync(_session, "Günlük Faaliyet — Depo Çıkışı")) return;
            if (ExitMaterial is null) { FormError = "Malzeme seçin."; return; }
            if (ExitQuantity <= 0) { FormError = "Miktar sıfırdan büyük olmalı."; return; }
            if (ShowExitPersonnel && ExitPersonnel is null) { FormError = "Personel (teslim alan) zorunludur."; return; }
            var opx = Guid.NewGuid().ToString("N");
            var notex = string.IsNullOrWhiteSpace(FormDescription) ? null : FormDescription.Trim();
            try
            {
                if (IsInBranchExit)   // Şube İçi = merkez depodan düşer (IssueOut)
                {
                    if (!await ConfirmService.AskAsync("Şube içi çıkış kaydedilsin mi? (stok AZALIR)", "Depo Çıkışı — Şube İçi")) return;
                    DesktopServices.Stock.IssueOut(_session, new[] { new StockLine(ExitMaterial.Id, ExitQuantity) }, opx,
                        branchId: _session.OperatingBranchId, personnelId: ExitPersonnel?.Id, vehicleId: FormVehicle?.Id, note: notex);
                    Status = "Şube içi çıkış kaydedildi (Stok Hareketleri'nde görünür).";
                }
                else   // Şube Dışı = Transfer
                {
                    var fromx = _session.OperatingBranchId;
                    if (string.IsNullOrEmpty(fromx)) { FormError = "Şubeniz belirlenemedi."; return; }
                    if (ExitToBranch is null) { FormError = "Hedef şube seçin."; return; }
                    if (ExitToBranch.Id == fromx) { FormError = "Hedef şube, kendi şubenizden farklı olmalı."; return; }
                    if (!await ConfirmService.AskAsync("Şube dışı çıkış (transfer) kaydedilsin mi?", "Depo Çıkışı — Şube Dışı")) return;
                    DesktopServices.Stock.Transfer(_session, ExitMaterial.Id, ExitQuantity, fromx, ExitToBranch.Id, opx, notex,
                        personnelId: null, vehicleId: FormVehicle?.Id);
                    Status = "Şube dışı çıkış (transfer) kaydedildi (Stok Hareketleri'nde görünür).";
                }
                ShowForm = false; Load();
            }
            catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
            return;
        }

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
    private async Task DeleteActivity(DailyActivityGridRow? row)
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
