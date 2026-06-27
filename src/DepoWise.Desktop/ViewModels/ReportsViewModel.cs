using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Raporlar — rapor tipi + tarih filtreleri (ortak form bileşenleri) + Sorgula. Salt okuma (ReportService).
/// Rapor, Sorgula tıklanmadan çalışmaz (ReportGate). Grafik alanı LiveCharts2'ye hazır boş container (paket eklenmedi).
/// </summary>
public sealed partial class ReportsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<string> ReportTypes { get; } = new() { "Stok Durumu", "Yakıt Tüketim" };
    public ObservableCollection<string> Headers { get; } = new();
    public ObservableCollection<string[]> Rows { get; } = new();

    [ObservableProperty] private string _selectedReport = "Stok Durumu";
    [ObservableProperty] private DateTimeOffset? _fromDate;
    [ObservableProperty] private DateTimeOffset? _toDate;
    [ObservableProperty] private string? _status;

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

    [RelayCommand]
    private void Run()
    {
        try
        {
            LoadError = null;
            var req = new ReportRequest(
                Executed: true,
                FromDate: FromDate?.ToUnixTimeMilliseconds(),
                ToDate: ToDate?.ToUnixTimeMilliseconds());

            var table = SelectedReport == "Yakıt Tüketim"
                ? DesktopServices.Reports.FuelConsumption(_session, req)
                : DesktopServices.Reports.StockStatus(_session, req);

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
            Status = $"{Rows.Count} satır — {table.Title}";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmptyResult));
        OnPropertyChanged(nameof(IsPrompt));
    }

    private static string Format(object? v) => v switch
    {
        null => "",
        double d => d.ToString("0.##"),
        decimal m => m.ToString("0.##"),
        _ => v.ToString() ?? "",
    };
}
