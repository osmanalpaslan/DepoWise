using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Stok Hareketleri — ayrı ekran (kullanıcı isteği 2026-08-05): tüm giriş/çıkış/transfer/sayım hareketleri,
/// tarih aralığı + metin araması. Salt-okunur (iptal Giriş-Çıkış ekranında kalır). Şube kapsamı ve yetki API/servis
/// katmanında (StockService.SearchMovements). Kod-arkası yok, MVVM.
/// </summary>
public sealed partial class StockMovementsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<StockMovementRow> Movements { get; } = new();

    [ObservableProperty] private DateTimeOffset? _fromDate;
    [ObservableProperty] private DateTimeOffset? _toDate;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _status = "";

    public bool HasRows => Movements.Count > 0;

    public StockMovementsViewModel(SessionContext session)
    {
        _session = session;
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        Movements.Clear();
        long? from = FromDate is { } f ? new DateTimeOffset(f.Date, TimeSpan.Zero).ToUnixTimeMilliseconds() : null;
        long? to = ToDate is { } t ? new DateTimeOffset(t.Date.AddDays(1).AddMilliseconds(-1), TimeSpan.Zero).ToUnixTimeMilliseconds() : null;
        try
        {
            foreach (var m in DesktopServices.Stock.SearchMovements(
                         _session, from, to, string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(), 1000))
                Movements.Add(m);
            Status = Movements.Count == 0 ? "Seçilen ölçütlerde hareket yok." : $"{Movements.Count} hareket";
        }
        catch (Exception ex) { Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
    }

    [RelayCommand]
    private void Clear()
    {
        FromDate = null; ToDate = null; Search = "";
        Load();
    }
}
