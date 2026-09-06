using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Stok Değişiklik Kaydı (madde 1.4/1.5) — malzeme kartından Giriş/Çıkış KULLANILMADAN yapılan
/// doğrudan stok değişikliklerinin uyarı logu. Salt-okuma. Tarih Aralığı + kayıt sayısı filtreleri (Sistem
/// Logu ile AYNI desen; filtreleme servis katmanında). Yetki: module stock_change_log (Admin-restricted).</summary>
public sealed partial class StockChangeLogViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<StockChangeLogRow> Items { get; } = new();
    public ObservableCollection<int> LimitOptions { get; } = new() { 100, 300, 500, 1000, 2000, 5000 };

    [ObservableProperty] private DateTimeOffset? _fromDate;
    [ObservableProperty] private DateTimeOffset? _toDate;
    [ObservableProperty] private int _limit = 300;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _loadError;
    public bool HasError => LoadError != null;
    public bool HasRows => Items.Count > 0;
    public bool IsEmpty => !HasError && Items.Count == 0;

    public StockChangeLogViewModel(SessionContext session)
    {
        _session = session;
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            long? from = FromDate is { } f ? new DateTimeOffset(f.Date, TimeSpan.Zero).ToUnixTimeMilliseconds() : null;
            long? to = ToDate is { } t ? new DateTimeOffset(t.Date.AddDays(1).AddMilliseconds(-1), TimeSpan.Zero).ToUnixTimeMilliseconds() : null;
            foreach (var a in DesktopServices.StockChangeLog.List(_session, from, to, Limit)) Items.Add(a);

            // ⭐ LST-01 (2026-09-07) — SESSİZ KESME KALDIRILDI.
            // Bu ekran en fazla `Limit` satır okur ve eskiden OKUDUĞU satır sayısını yazıyordu:
            // 10.000 kaydı olan firmada "300 kayıt" görünüyor, geri kalanı kullanıcıdan SESSİZCE
            // gizleniyordu. Artık gerçek toplam ayrıca sorulur ve tavana takıldığı açıkça söylenir.
            var toplam = DesktopServices.StockChangeLog.Sayim(_session, from, to);
            Status = toplam == 0 ? "Seçilen ölçütlerde kayıt yok."
                : toplam > Items.Count
                    ? $"{toplam} kayıt — en yenisinden {Items.Count} tanesi gösteriliyor. Daraltmak için tarih aralığı kullanın. (salt okunur)"
                    : $"{toplam} kayıt (salt okunur)";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }

    [RelayCommand]
    private void Clear()
    {
        FromDate = null; ToDate = null; Limit = 300;
        Load();
    }
}
