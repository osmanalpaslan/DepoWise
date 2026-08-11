using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Rapor seçicide/filtrede kullanılan şube işareti (yetkili kullanıcı çoklu şube seçebilir).</summary>
public sealed partial class BranchPick : ObservableObject
{
    public string Id { get; }
    public string Name { get; }
    [ObservableProperty] private bool _isChecked;
    public BranchPick(string id, string name) { Id = id; Name = name; }
}

/// <summary>
/// Raporlar — ORTAK MİMARİ (kullanıcı isteği 2026-08-07). Rapor listesi + filtre görünürlüğü TEK KATALOĞA
/// (ReportCatalog) göre sürülür; yürütme ortak ReportService.Run (katalog dispatch + Bu Ay tarih varsayılanı +
/// maks-kayıt). Filtreler rapora göre görünür (tarih yalnız UsesDate; şube seçici yalnız UsesBranch VE yetki).
/// Otomatik sorgu YOK — yalnız Sorgula (ReportGate). Hesaplama BU FAZDA değişmez.
/// </summary>
public sealed partial class ReportsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    private static readonly SKColor Accent = SKColor.Parse("2F6FD5");
    private static readonly SKColor Success = SKColor.Parse("2CBF6D");
    private static readonly SKColor Warning = SKColor.Parse("D8A617");
    private static readonly SKColor TextSecondary = SKColor.Parse("AEB7C4");
    private const int MaxBars = 20;

    /// <summary>Katalog-sürümlü rapor listesi (tek doğru kaynak). ComboBox Name gösterir (DataTemplate).</summary>
    public IReadOnlyList<ReportDescriptor> ReportItems { get; } = ReportCatalog.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDate))]
    [NotifyPropertyChangedFor(nameof(ShowBranchSelect))]
    [NotifyPropertyChangedFor(nameof(ShowVehicleSelect))]
    [NotifyPropertyChangedFor(nameof(ShowVehicleType))]
    [NotifyPropertyChangedFor(nameof(ShowMaintenanceDef))]
    [NotifyPropertyChangedFor(nameof(ShowTechnician))]
    [NotifyPropertyChangedFor(nameof(ShowSupplier))]
    [NotifyPropertyChangedFor(nameof(ShowRequester))]
    [NotifyPropertyChangedFor(nameof(ShowStatus))]
    [NotifyPropertyChangedFor(nameof(ShowLocation))]
    private ReportDescriptor _selectedReport = ReportCatalog.ByKey("stock")!;

    /// <summary>Kullanıcı raporda şube SEÇEBİLİR mi (btn-branch-select; admin bypass). Yoksa şube seçici gizli.</summary>
    public bool CanSelectBranches => AccessControl.CanUseButton(_session, SpecialButtons.BranchSelect);
    public bool ShowDate => SelectedReport?.UsesDate == true;
    public bool ShowBranchSelect => SelectedReport?.UsesBranch == true && CanSelectBranches;
    public bool ShowVehicleSelect => SelectedReport?.UsesVehicle == true;
    public bool ShowVehicleType => SelectedReport?.UsesVehicleType == true;
    public bool ShowMaintenanceDef => SelectedReport?.UsesMaintenanceDef == true;
    public bool ShowTechnician => SelectedReport?.UsesTechnician == true;
    public bool ShowSupplier => SelectedReport?.UsesSupplier == true;
    public bool ShowRequester => SelectedReport?.UsesRequester == true;
    public bool ShowStatus => SelectedReport?.UsesStatus == true;
    /// <summary>STK-06 — stok lokasyonu (depo/şantiye) filtresi. Şube seçici özel butonuna BAĞLI DEĞİLDİR:
    /// o filtre "kaydı işleyen şube", bu ise "stoğun fiziksel yeri". Rapor yetkisi zaten kapıda.</summary>
    public bool ShowLocation => SelectedReport?.UsesLocation == true;

    /// <summary>Yetkili kullanıcıya gösterilen şube listesi (çoklu işaret). İşaretsiz = oturum şubesi (non-breaking).</summary>
    public ObservableCollection<BranchPick> Branches { get; } = new();

    /// <summary>STK-06 — stok lokasyonları. Kaynak YEREL SQLite'tır (çevrimdışı rapor için API çağrısı YOK).
    /// Son satır "📦 Atanmamış" (boş kimlik): lokasyonu bilinmeyen geçmiş stok — gerçek bir depo DEĞİLDİR.</summary>
    public ObservableCollection<BranchPick> Locations { get; } = new();

    /// <summary>Araç seçimi (Araç Raporu) — tümü + arama-filtreli; çoklu işaret. Boş = tüm araçlar (sunucu).</summary>
    public ObservableCollection<VehiclePick> VehiclePicks { get; } = new();
    public ObservableCollection<VehiclePick> FilteredVehiclePicks { get; } = new();
    /// <summary>Araç türü filtresi (BranchPick = genel id/ad/işaret öğesi olarak yeniden kullanıldı).</summary>
    public ObservableCollection<BranchPick> VehicleTypes { get; } = new();
    /// <summary>Bakım tanımı filtresi (Bakım Raporu) — çoklu işaret.</summary>
    public ObservableCollection<BranchPick> MaintenanceDefs { get; } = new();
    /// <summary>Teknisyen filtresi (Bakım Raporu) — çoklu işaret.</summary>
    public ObservableCollection<BranchPick> Technicians { get; } = new();
    /// <summary>Tedarikçi filtresi (Depo Girişi) — çoklu işaret.</summary>
    public ObservableCollection<BranchPick> Suppliers { get; } = new();
    /// <summary>Talep eden filtresi (Talep Raporu) — mevcut personel listesinden; Teknisyen'den AYRI işaret durumu.</summary>
    public ObservableCollection<BranchPick> Requesters { get; } = new();
    /// <summary>Durum filtresi (Talep Raporu) — sabit liste (RequestStatusOptions); Id = DB değeri.</summary>
    public ObservableCollection<BranchPick> Statuses { get; } = new();
    [ObservableProperty] private string _vehicleSearch = "";
    partial void OnVehicleSearchChanged(string value) => RebuildFilteredVehicles();

    public ObservableCollection<string> Headers { get; } = new();
    public ObservableCollection<string[]> Rows { get; } = new();

    /// <summary>Ortak tablo bileşeni (Birim 4) — kolon-altı filtre/sıralama/genişlik/gizleme + kişisel tercih.
    /// Rapora özel değil; GridKey ile herhangi bir ekran kullanabilir. Rapor sonucu buraya beslenir (aşağıda).</summary>
    public GridController Grid { get; } = new();
    private string GridKey => $"reports:{SelectedReport.Key}";

    public ObservableCollection<ISeries> ChartSeries { get; } = new();
    public ObservableCollection<ISeries> PieSeries { get; } = new();
    public Axis[] XAxes { get; private set; } = Array.Empty<Axis>();
    public Axis[] YAxes { get; private set; } = Array.Empty<Axis>();

    [ObservableProperty] private DateTimeOffset? _fromDate;
    [ObservableProperty] private DateTimeOffset? _toDate;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _chartTitle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowChart))]
    private bool _showBar;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowChart))]
    private bool _showPie;
    public bool ShowChart => (ShowBar || ShowPie) && HasRows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrompt))]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    private bool _hasRun;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _loadError;

    public bool HasError => LoadError != null;
    public bool IsPrompt => !HasRun && !HasError;
    public bool HasRows => HasRun && !HasError && Rows.Count > 0;
    public bool IsEmptyResult => HasRun && !HasError && Rows.Count == 0;

    public ReportsViewModel(SessionContext session)
    {
        _session = session;
        // Ortak tablonun kişisel tercih kancaları — kalıcılık DesktopServices.ListPrefs (yerel SQLite, kişiye özel).
        // GridKey rapor bazlı: her rapor kendi kolon sırası/genişliği/gizli/sıralamasını hatırlar. Ekran açılışında
        // TEK yükleme (GetAll — tek sorgu); değişince ilgili alan yazılır (performans kuralı).
        Grid.LoadPreferences = () => { try { return DesktopServices.ListPrefs.GetAll(_session, GridKey); } catch { return null; } };
        Grid.PersistColumns = cols => { try { DesktopServices.ListPrefs.SaveColumns(_session, GridKey, cols); } catch { } };
        // Kolon GENİŞLİĞİ kalıcı DEĞİL (kullanıcı isteği 2026-08-08): her açılışta standart; oturum içi resize kaydedilmez.
        Grid.PersistWidths = null;
        Grid.PersistSort = (k, d) => { try { DesktopServices.ListPrefs.SaveSort(_session, GridKey, k, d); } catch { } };
        LoadBranches();
        LoadLocations();   // STK-06: stok lokasyonları (yerel veriden — çevrimdışı çalışır)
        LoadVehiclePicks();
        LoadVehicleTypes();
        LoadMaintenanceDefs();
        LoadTechnicians();
        LoadSuppliers();
        LoadRequesters();
        LoadStatuses();
        ApplyDateDefault();
    }

    private void LoadBranches()
    {
        if (!CanSelectBranches) return;
        try { foreach (var b in DesktopServices.Branches.List(_session)) Branches.Add(new BranchPick(b.Id, b.Name)); }
        catch { }
    }

    /// <summary>STK-06 — lokasyon listesi YEREL veritabanından (internet gerekmez). Sonda ATANMAMIŞ kovası.</summary>
    private void LoadLocations()
    {
        try { foreach (var b in DesktopServices.Branches.List(_session)) Locations.Add(new BranchPick(b.Id, b.Name)); }
        catch { }
        Locations.Add(new BranchPick("", "📦 Atanmamış"));
    }

    private void LoadVehiclePicks()
    {
        try { foreach (var v in DesktopServices.Vehicles.List(_session)) VehiclePicks.Add(new VehiclePick(v.Id, v.InternalCode, v.Plate ?? "")); }
        catch { }
        RebuildFilteredVehicles();
    }

    private void LoadVehicleTypes()
    {
        try { foreach (var t in DesktopServices.Lookups.List(_session, "vehicle_types")) VehicleTypes.Add(new BranchPick(t.Id, t.Name)); }
        catch { }
    }

    private void LoadMaintenanceDefs()
    {
        try { foreach (var d in DesktopServices.MaintenanceDefs.List(_session)) MaintenanceDefs.Add(new BranchPick(d.Id, d.Name)); }
        catch { }
    }

    private void LoadTechnicians()
    {
        try { foreach (var p in DesktopServices.Lookups.ListPersonnel(_session)) Technicians.Add(new BranchPick(p.Id, p.Name)); }
        catch { }
    }

    private void LoadSuppliers()
    {
        try { foreach (var sp in DesktopServices.Lookups.List(_session, "suppliers")) Suppliers.Add(new BranchPick(sp.Id, sp.Name)); }
        catch { }
    }

    /// <summary>Talep eden listesi — Teknisyen ile AYNI personel kaynağı, AYRI öğeler (işaret durumu karışmasın).</summary>
    private void LoadRequesters()
    {
        try { foreach (var p in DesktopServices.Lookups.ListPersonnel(_session)) Requesters.Add(new BranchPick(p.Id, p.Name)); }
        catch { }
    }

    /// <summary>Talep durumları — sabit liste (web ile AYNI kaynak: RequestStatusOptions). Sorgu yok.</summary>
    private void LoadStatuses()
    {
        foreach (var (key, label) in RequestStatusOptions.All) Statuses.Add(new BranchPick(key, label));
    }

    private static readonly System.Globalization.CompareInfo TrCmp = System.Globalization.CultureInfo.GetCultureInfo("tr-TR").CompareInfo;
    private static bool TrContains(string s, string sub) => TrCmp.IndexOf(s ?? "", sub, System.Globalization.CompareOptions.IgnoreCase) >= 0;

    private void RebuildFilteredVehicles()
    {
        FilteredVehiclePicks.Clear();
        var t = VehicleSearch?.Trim();
        foreach (var p in VehiclePicks)
            if (string.IsNullOrEmpty(t) || TrContains(p.Display, t)) FilteredVehiclePicks.Add(p);
    }

    /// <summary>Tümünü Seç: arama sonucundakiler (arama yoksa tümü) işaretlenir (kullanıcı isteği rule 7).</summary>
    [RelayCommand] private void SelectAllVehicles() { foreach (var p in FilteredVehiclePicks) p.IsSelected = true; }
    /// <summary>Tümünü Kaldır: TÜM araç seçimini temizler (yalnız aramadakini değil).</summary>
    [RelayCommand] private void ClearVehicles() { foreach (var p in VehiclePicks) p.IsSelected = false; }

    /// <summary>Rapor tipi değişince önceki sonucu TEMİZLE (otomatik sorgu yok) + RequiresDate ise tarih Bu Ay ön-dolu.</summary>
    partial void OnSelectedReportChanged(ReportDescriptor value)
    {
        HasRun = false;
        LoadError = null;
        Headers.Clear();
        Rows.Clear();
        Grid.Clear();          // ortak tablodaki eski sonucu temizle (yeni rapor için)
        ShowBar = ShowPie = false;
        Status = null;
        ApplyDateDefault();
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmptyResult));
        OnPropertyChanged(nameof(IsPrompt));
    }

    private void ApplyDateDefault()
    {
        if (SelectedReport is { RequiresDate: true } && FromDate is null && ToDate is null)
        {
            var now = DateTimeOffset.Now;
            FromDate = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
            ToDate = now;
        }
    }

    [RelayCommand]
    private void Run()
    {
        try
        {
            LoadError = null;
            var table = BuildTable();

            Headers.Clear();
            foreach (var h in table.Headers) Headers.Add(h);
            Rows.Clear();
            var rows = new List<string[]>(table.Rows.Count);
            foreach (var row in table.Rows)
            {
                var cells = new string[row.Count];
                for (int i = 0; i < row.Count; i++) cells[i] = Format(row[i]);
                rows.Add(cells);
                Rows.Add(cells);
            }
            HasRun = true;
            // Ortak tabloya besle: her hücre HAM değer (Num) + GÖRÜNTÜ (Text) taşır → filtre/sıralama HAM değerde,
            // ekranda görüntü. Toplam satırı (varsa) pinned olarak ayrı verilir (filtre/sıralama dışı).
            var gridRows = table.Rows.Select(rr => (IReadOnlyList<GridCell>)rr.Select(ToGridCell).ToList()).ToList();
            IReadOnlyList<GridCell>? totalRow = table.TotalRow?.Select(ToGridCell).ToList();
            Grid.SetData(BuildColumns(table), gridRows, totalRow);
            BuildChart(table);
            Status = $"{Rows.Count} satır — {table.Title}";
        }
        catch (Exception ex) { LoadError = ex.Message; ShowBar = ShowPie = false; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmptyResult));
        OnPropertyChanged(nameof(IsPrompt));
        OnPropertyChanged(nameof(ShowChart));
    }

    /// <summary>Seçili raporu ortak yürütmeyle (ReportService.Run) TableModel'e çevirir — API ile AYNI dispatch +
    /// tarih varsayılanı + maks-kayıt. Filtreler rapora göre: tarih yalnız UsesDate; şube yalnız yetkili seçim.</summary>
    private TableModel BuildTable()
    {
        var branchIds = ShowBranchSelect
            ? Branches.Where(b => b.IsChecked).Select(b => b.Id).ToList()
            : null;
        var vehicleIds = ShowVehicleSelect
            ? VehiclePicks.Where(p => p.IsSelected).Select(p => p.Id).ToList()
            : null;
        var typeIds = ShowVehicleType
            ? VehicleTypes.Where(t => t.IsChecked).Select(t => t.Id).ToList()
            : null;
        var defIds = ShowMaintenanceDef
            ? MaintenanceDefs.Where(t => t.IsChecked).Select(t => t.Id).ToList()
            : null;
        var techIds = ShowTechnician
            ? Technicians.Where(t => t.IsChecked).Select(t => t.Id).ToList()
            : null;
        var supplierIds = ShowSupplier
            ? Suppliers.Where(t => t.IsChecked).Select(t => t.Id).ToList()
            : null;
        var requesterIds = ShowRequester
            ? Requesters.Where(t => t.IsChecked).Select(t => t.Id).ToList()
            : null;
        var statuses = ShowStatus
            ? Statuses.Where(t => t.IsChecked).Select(t => t.Id).ToList()
            : null;
        // STK-06: seçili stok lokasyonları. Hiçbiri seçili değilse null → "Tüm Şubeler" (firma toplamı).
        var locationIds = ShowLocation
            ? Locations.Where(t => t.IsChecked).Select(t => t.Id).ToList()
            : null;
        var req = new ReportRequest(
            Executed: true,
            FromDate: ShowDate ? FromDate?.ToUnixTimeMilliseconds() : null,
            ToDate: ShowDate ? ToDate?.ToUnixTimeMilliseconds() : null,
            BranchIds: branchIds,
            VehicleIds: vehicleIds,
            VehicleTypeIds: typeIds,
            MaintenanceDefIds: defIds,
            TechnicianIds: techIds,
            SupplierIds: supplierIds,
            RequesterIds: requesterIds,
            Statuses: statuses,
            LocationIds: locationIds is { Count: > 0 } ? locationIds : null);
        var maxRows = ReportLimits.Resolve(k => DesktopServices.Settings.Get(_session.CompanyId, k));
        return DesktopServices.Reports.Run(_session, SelectedReport.Key, req, maxRows);
    }

    /// <summary>Seçili raporu Excel'e aktarır. Yönetici ↔ Standart için iki ayrı özel buton (deny-by-default).</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ExportExcelAsync()
    {
        var button = SelectedReport.IsManager ? SpecialButtons.ExportManagerReports : SpecialButtons.ExportReports;
        if (!AccessControl.CanUseButton(_session, button))
        {
            Status = "Excel dışa aktarma yetkiniz yok. Yetki için firma yöneticinize başvurun.";
            return;
        }
        try
        {
            var table = BuildTable();
            var bytes = DesktopServices.Excel.Export(table);
            var path = await DepoWise.Desktop.FilePickerService.SaveExcelAsync(SafeFileName(table.Title) + ".xlsx");
            if (string.IsNullOrEmpty(path)) return;
            await System.IO.File.WriteAllBytesAsync(path, bytes);
            DepoWise.Desktop.FilePickerService.OpenFile(path);
            Status = $"Excel'e aktarıldı: {table.Title}";
        }
        catch (Exception ex) { Status = "Excel dışa aktarma hatası: " + ex.Message; }
    }

    private static string SafeFileName(string title)
        => System.Text.RegularExpressions.Regex.Replace(title ?? "Rapor", @"[^\p{L}\p{Nd}]+", "_").Trim('_');

    /// <summary>Grafiği çalıştırılan raporun gerçek satırlarından kurar (sahte seri yok). Katalog anahtarına göre.</summary>
    private void BuildChart(TableModel table)
    {
        ChartSeries.Clear();
        PieSeries.Clear();
        ShowBar = ShowPie = false;

        var labelPaint = new SolidColorPaint(TextSecondary) { SKTypeface = SKTypeface.Default };

        if (SelectedReport.Key == "fuel")
        {
            // Kolonu İNDEKS'e değil BAŞLIĞA göre hedefle (yeni yapıda Litre index 8) — kolon sırası değişse de kırılmaz.
            // TotalRow artık ayrı (satırlarda TOPLAM yok). NumCell → HAM değer ToDouble ile okunur.
            int litreCol = HeaderIndex(table, "Litre");
            int labelCol = HeaderIndex(table, "Araç İç Kod");
            var data = table.Rows.Take(MaxBars).ToList();
            var liters = data.Select(r => litreCol >= 0 && litreCol < r.Count ? ToDouble(r[litreCol]) : 0).ToArray();
            var labels = data.Select(r => (labelCol >= 0 && labelCol < r.Count ? r[labelCol]?.ToString() : null) ?? "").ToArray();

            ChartSeries.Add(new ColumnSeries<double>
            {
                Name = "Litre",
                Values = liters,
                Fill = new SolidColorPaint(Accent),
                DataLabelsPaint = new SolidColorPaint(TextSecondary),
                DataLabelsSize = 11,
                DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top,
                EasingFunction = null,
            });
            XAxes = new[] { new Axis { Labels = labels, LabelsPaint = labelPaint, TextSize = 11, LabelsRotation = 30 } };
            YAxes = new[] { new Axis { Name = "Litre", NamePaint = labelPaint, LabelsPaint = labelPaint, TextSize = 11 } };
            OnPropertyChanged(nameof(XAxes));
            OnPropertyChanged(nameof(YAxes));
            ChartTitle = "Araç Bazında Yakıt (Litre)";
            ShowBar = liters.Length > 0;
        }
        else if (SelectedReport.Key == "stock")
        {
            int low = 0, ok = 0;
            foreach (var r in table.Rows)
            {
                var stock = ToDecimal(r[2]);
                var min = ToDecimal(r[3]);
                if (stock <= min) low++; else ok++;
            }
            if (low > 0)
                PieSeries.Add(new PieSeries<double> { Name = "Düşük", Values = new double[] { low },
                    Fill = new SolidColorPaint(Warning), DataLabelsPaint = new SolidColorPaint(TextSecondary),
                    DataLabelsSize = 12, EasingFunction = null,
                    DataLabelsFormatter = p => $"Düşük: {p.Coordinate.PrimaryValue:0}" });
            if (ok > 0)
                PieSeries.Add(new PieSeries<double> { Name = "Yeterli", Values = new double[] { ok },
                    Fill = new SolidColorPaint(Success), DataLabelsPaint = new SolidColorPaint(TextSecondary),
                    DataLabelsSize = 12, EasingFunction = null,
                    DataLabelsFormatter = p => $"Yeterli: {p.Coordinate.PrimaryValue:0}" });
            ChartTitle = "Stok Durum Dağılımı (Düşük / Yeterli)";
            ShowPie = (low + ok) > 0;
        }
    }

    /// <summary>Grafikte kolonu başlığa göre bulur (indeks sabitlenmez → kolon sırası değişse de kırılmaz). Yoksa -1.</summary>
    private static int HeaderIndex(TableModel t, string header)
    {
        for (int i = 0; i < t.Headers.Count; i++) if (t.Headers[i] == header) return i;
        return -1;
    }

    private static double ToDouble(object? v) => v switch
    {
        NumCell n => n.Value,
        double d => d,
        decimal m => (double)m,
        _ => double.TryParse(v?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : 0,
    };

    private static decimal ToDecimal(object? v) => v switch
    {
        decimal m => m,
        double d => (decimal)d,
        _ => decimal.TryParse(v?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : 0,
    };

    /// <summary>Kolon tanımları: sayısal-tip raporun verdiği <see cref="TableModel.Numeric"/> bayraklarından
    /// (varsa) alınır; yoksa hücreler örneklenir. Aynı başlık tekrarsa anahtar benzersizleşir (tercih çakışmasın).</summary>
    private static List<ListColumn> BuildColumns(TableModel table)
    {
        var cols = new List<ListColumn>(table.Headers.Count);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int c = 0; c < table.Headers.Count; c++)
        {
            bool numeric = table.Numeric is { } nf && c < nf.Count ? nf[c] : SampleNumeric(table.Rows, c);
            var label = table.Headers[c];
            var key = label;
            if (seen.TryGetValue(label, out var n)) { key = $"{label}#{n + 1}"; seen[label] = n + 1; }
            else seen[label] = 0;
            cols.Add(new ListColumn(key, label, numeric));
        }
        return cols;
    }

    /// <summary>Rapor Numeric bayrağı vermediğinde kolon tipini örnekler (dolu hücrelerin çoğu sayısalsa numeric).</summary>
    private static bool SampleNumeric(IReadOnlyList<IReadOnlyList<object?>> rows, int c)
    {
        int filled = 0, parsed = 0;
        foreach (var r in rows)
        {
            if (c >= r.Count) continue;
            var cell = ToGridCell(r[c]);
            if (string.IsNullOrWhiteSpace(cell.Text)) continue;
            filled++;
            if (cell.Num is not null) parsed++;
        }
        return filled >= 3 && parsed * 10 >= filled * 6;
    }

    /// <summary>Rapor hücresi (object?) → ortak tablo hücresi: GÖRÜNTÜ (Text) + HAM sayısal değer (Num).
    /// NumCell zaten ikisini taşır; ham sayı/string ise değer ayrıştırılıp korunur (sıralama/filtre bozulmaz).</summary>
    private static GridCell ToGridCell(object? c) => c switch
    {
        null => new GridCell("", null),
        NumCell n => new GridCell(n.Display, n.Value),
        double d => new GridCell(d.ToString("0.##", CultureInfo.InvariantCulture), d),
        decimal m => new GridCell(m.ToString("0.##", CultureInfo.InvariantCulture), (double)m),
        int i => new GridCell(i.ToString(CultureInfo.InvariantCulture), i),
        long l => new GridCell(l.ToString(CultureInfo.InvariantCulture), l),
        string s => new GridCell(s, GridDataView.TryNum(s, out var v) ? (double)v : (double?)null),
        _ => new GridCell(c.ToString() ?? "", null),
    };

    private static string Format(object? v) => v switch
    {
        null => "",
        NumCell n => n.Display,
        double d => d.ToString("0.##"),
        decimal m => m.ToString("0.##"),
        _ => v.ToString() ?? "",
    };
}
