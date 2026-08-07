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
    private ReportDescriptor _selectedReport = ReportCatalog.ByKey("stock")!;

    /// <summary>Kullanıcı raporda şube SEÇEBİLİR mi (btn-branch-select; admin bypass). Yoksa şube seçici gizli.</summary>
    public bool CanSelectBranches => AccessControl.CanUseButton(_session, SpecialButtons.BranchSelect);
    public bool ShowDate => SelectedReport?.UsesDate == true;
    public bool ShowBranchSelect => SelectedReport?.UsesBranch == true && CanSelectBranches;

    /// <summary>Yetkili kullanıcıya gösterilen şube listesi (çoklu işaret). İşaretsiz = oturum şubesi (non-breaking).</summary>
    public ObservableCollection<BranchPick> Branches { get; } = new();

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
        Grid.PersistWidths = w => { try { DesktopServices.ListPrefs.SaveWidths(_session, GridKey, w); } catch { } };
        Grid.PersistSort = (k, d) => { try { DesktopServices.ListPrefs.SaveSort(_session, GridKey, k, d); } catch { } };
        LoadBranches();
        ApplyDateDefault();
    }

    private void LoadBranches()
    {
        if (!CanSelectBranches) return;
        try { foreach (var b in DesktopServices.Branches.List(_session)) Branches.Add(new BranchPick(b.Id, b.Name)); }
        catch { }
    }

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
            // Ortak tabloya besle (kolonlar sayısal-tespitli; satırlar snapshot). Filtre/sıralama istemcide.
            Grid.SetData(BuildColumns(table.Headers, rows), rows);
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
        var req = new ReportRequest(
            Executed: true,
            FromDate: ShowDate ? FromDate?.ToUnixTimeMilliseconds() : null,
            ToDate: ShowDate ? ToDate?.ToUnixTimeMilliseconds() : null,
            BranchIds: branchIds);
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
            var data = table.Rows.Where(r => (r[0]?.ToString() ?? "") != "TOPLAM").Take(MaxBars).ToList();
            var liters = data.Select(r => ToDouble(r[3])).ToArray();
            var labels = data.Select(r => r[0]?.ToString() ?? "").ToArray();

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

    private static double ToDouble(object? v) => v switch
    {
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

    /// <summary>Rapor başlıklarından ortak tablonun kolon tanımlarını üretir (web ile AYNI kural): başlık =
    /// anahtar+etiket; sayısal tespit → filtre kutusu tam/karşılaştırma/aralık kabul eder. Aynı başlık tekrarsa
    /// anahtar benzersizleşir (tercih JSON'u çakışmasın).</summary>
    private static List<ListColumn> BuildColumns(IReadOnlyList<string> headers, List<string[]> rows)
    {
        var cols = new List<ListColumn>(headers.Count);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int c = 0; c < headers.Count; c++)
        {
            int filled = 0, parsed = 0;
            foreach (var r in rows)
            {
                if (c >= r.Length || string.IsNullOrWhiteSpace(r[c])) continue;
                filled++;
                if (TryNum(r[c], out _)) parsed++;
            }
            bool numeric = filled >= 3 && parsed * 10 >= filled * 6;
            var label = headers[c];
            var key = label;
            if (seen.TryGetValue(label, out var n)) { key = $"{label}#{n + 1}"; seen[label] = n + 1; }
            else seen[label] = 0;
            cols.Add(new ListColumn(key, label, numeric));
        }
        return cols;
    }

    private static bool TryNum(string s, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim().Replace(" ", "").Replace(" ", "");
        if (decimal.TryParse(t, NumberStyles.Number, CultureInfo.GetCultureInfo("tr-TR"), out value)) return true;
        if (decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out value)) return true;
        return false;
    }

    private static string Format(object? v) => v switch
    {
        null => "",
        double d => d.ToString("0.##"),
        decimal m => m.ToString("0.##"),
        _ => v.ToString() ?? "",
    };
}
