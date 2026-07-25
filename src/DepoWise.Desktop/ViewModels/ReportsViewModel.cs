using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Raporlar — rapor tipi + tarih filtreleri (ortak form bileşenleri) + Sorgula. Salt okuma (ReportService).
/// Rapor, Sorgula tıklanmadan çalışmaz (ReportGate). Grafikler yalnız çalıştırılan raporun GERÇEK verisinden
/// türetilir (ek sorgu/sahte veri yok); tema merkezi palet renklerine bağlı.
/// </summary>
public sealed partial class ReportsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    // Merkezi palet (Palette.axaml ile aynı) — sınırlı, tutarlı seri renkleri
    private static readonly SKColor Accent = SKColor.Parse("2F6FD5");
    private static readonly SKColor Success = SKColor.Parse("2CBF6D");
    private static readonly SKColor Warning = SKColor.Parse("D8A617");
    private static readonly SKColor TextSecondary = SKColor.Parse("AEB7C4");
    private const int MaxBars = 20; // büyük veri: nokta sayısını sınırla

    public ObservableCollection<string> ReportTypes { get; } = new()
        { "Genel Rapor", "Stok Durumu", "Stok Sayım", "Yakıt Tüketim", "Bakım Raporu", "Depo Girişi", "Talep Raporu",
          "Malzeme — Şablonlu", "Malzeme — Şablon Dışı", "Araç — Şablonlu", "Araç — Şablon Dışı", "Durum Rapor" };

    /// <summary>Yönetici raporu mu (Excel yetkisi buna göre: Yönetici Rapor vs Rapor özel butonu).</summary>
    private static bool IsManagerReport(string report) => report is "Malzeme — Şablonlu" or "Malzeme — Şablon Dışı"
        or "Araç — Şablonlu" or "Araç — Şablon Dışı" or "Durum Rapor";
    public ObservableCollection<string> Headers { get; } = new();
    public ObservableCollection<string[]> Rows { get; } = new();

    // Grafik
    public ObservableCollection<ISeries> ChartSeries { get; } = new();
    public ObservableCollection<ISeries> PieSeries { get; } = new();
    public Axis[] XAxes { get; private set; } = Array.Empty<Axis>();
    public Axis[] YAxes { get; private set; } = Array.Empty<Axis>();

    [ObservableProperty] private string _selectedReport = "Stok Durumu";
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

    public ReportsViewModel(SessionContext session) => _session = session;

    /// <summary>Rapor tipi değişince önceki raporun sonucunu TEMİZLE — her rapor kendi Sorgula'sını ister
    /// (alakasız veri başka raporda görünmesin). Web sekme davranışıyla tutarlı.</summary>
    partial void OnSelectedReportChanged(string value)
    {
        HasRun = false;
        LoadError = null;
        Headers.Clear();
        Rows.Clear();
        ShowBar = ShowPie = false;
        Status = null;
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmptyResult));
        OnPropertyChanged(nameof(IsPrompt));
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
            foreach (var row in table.Rows)
            {
                var cells = new string[row.Count];
                for (int i = 0; i < row.Count; i++) cells[i] = Format(row[i]);
                Rows.Add(cells);
            }
            HasRun = true;
            BuildChart(table);
            Status = $"{Rows.Count} satır — {table.Title}";
        }
        catch (Exception ex) { LoadError = ex.Message; ShowBar = ShowPie = false; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmptyResult));
        OnPropertyChanged(nameof(IsPrompt));
        OnPropertyChanged(nameof(ShowChart));
    }

    /// <summary>Seçili rapor tipini TableModel'e çevirir (ekran + Excel export ortak kullanır).</summary>
    private TableModel BuildTable()
    {
        var req = new ReportRequest(
            Executed: true,
            FromDate: FromDate?.ToUnixTimeMilliseconds(),
            ToDate: ToDate?.ToUnixTimeMilliseconds());
        return SelectedReport switch
        {
            "Genel Rapor" => DesktopServices.Reports.General(_session, req),
            "Stok Sayım" => DesktopServices.Reports.StockCount(_session, req),
            "Yakıt Tüketim" => DesktopServices.Reports.FuelConsumption(_session, req),
            "Bakım Raporu" => DesktopServices.Reports.Maintenance(_session, req),
            "Depo Girişi" => DesktopServices.Reports.FuelDepot(_session, req),
            "Talep Raporu" => DesktopServices.Reports.Requests(_session, req),
            "Malzeme — Şablonlu" => DesktopServices.Reports.MaterialsByTemplate(_session, req),
            "Malzeme — Şablon Dışı" => DesktopServices.Reports.MaterialsNonTemplate(_session, req),
            "Araç — Şablonlu" => DesktopServices.Reports.VehiclesByTemplate(_session, req),
            "Araç — Şablon Dışı" => DesktopServices.Reports.VehiclesNonTemplate(_session, req),
            "Durum Rapor" => DesktopServices.Reports.StatusReport(_session, req),
            _ => DesktopServices.Reports.StockStatus(_session, req),
        };
    }

    /// <summary>Seçili raporu Excel'e aktarır. Yetki yoksa (deny-by-default özel buton) "yetkiniz yok"
    /// uyarısı gösterir; API tarafı da fail-closed. Yönetici raporu ↔ Rapor için iki ayrı özel buton.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ExportExcelAsync()
    {
        var button = IsManagerReport(SelectedReport)
            ? SpecialButtons.ExportManagerReports : SpecialButtons.ExportReports;
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
            if (string.IsNullOrEmpty(path)) return;   // kullanıcı iptal etti
            await System.IO.File.WriteAllBytesAsync(path, bytes);
            DepoWise.Desktop.FilePickerService.OpenFile(path);
            Status = $"Excel'e aktarıldı: {table.Title}";
        }
        catch (Exception ex) { Status = "Excel dışa aktarma hatası: " + ex.Message; }
    }

    private static string SafeFileName(string title)
        => System.Text.RegularExpressions.Regex.Replace(title ?? "Rapor", @"[^\p{L}\p{Nd}]+", "_").Trim('_');

    /// <summary>Grafiği çalıştırılan raporun gerçek satırlarından kurar (sahte seri yok).</summary>
    private void BuildChart(TableModel table)
    {
        ChartSeries.Clear();
        PieSeries.Clear();
        ShowBar = ShowPie = false;

        var labelPaint = new SolidColorPaint(TextSecondary) { SKTypeface = SKTypeface.Default };

        if (SelectedReport == "Yakıt Tüketim")
        {
            // Araç başına litre (col0=Araç, col3=Litre). TOPLAM satırı hariç. Büyük veride ilk MaxBars.
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
                EasingFunction = null, // animasyonu azalt (perf)
            });
            XAxes = new[] { new Axis { Labels = labels, LabelsPaint = labelPaint, TextSize = 11, LabelsRotation = 30 } };
            YAxes = new[] { new Axis { Name = "Litre", NamePaint = labelPaint, LabelsPaint = labelPaint, TextSize = 11 } };
            OnPropertyChanged(nameof(XAxes));
            OnPropertyChanged(nameof(YAxes));
            ChartTitle = "Araç Bazında Yakıt (Litre)";
            ShowBar = liters.Length > 0;
        }
        else if (SelectedReport == "Stok Durumu")
        {
            // Stok durum dağılımı: Düşük (stok<=min) vs Yeterli (col2=Stok, col3=Min Stok)
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

    private static string Format(object? v) => v switch
    {
        null => "",
        double d => d.ToString("0.##"),
        decimal m => m.ToString("0.##"),
        _ => v.ToString() ?? "",
    };
}
