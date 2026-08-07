using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Sistem Logu — audit_logs salt-okuma. Loglar SİLİNEMEZ (yalnız görüntüleme). Tarih Aralığı + kayıt
/// sayısı filtreleri (madde 4, kullanıcı isteği 2026-08-06) — StockMovementsViewModel ile AYNI desen; filtreleme
/// SUNUCU/SERVİS tarafında (AuditLogService.List) yapılır, performans için kayıt sayısı 5000'de sınırlanır.</summary>
public sealed partial class AuditLogViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<AuditLogRow> Items { get; } = new();
    public ObservableCollection<int> LimitOptions { get; } = new() { 100, 300, 500, 1000, 2000, 5000 };

    [ObservableProperty] private DateTimeOffset? _fromDate;
    [ObservableProperty] private DateTimeOffset? _toDate;
    [ObservableProperty] private int _limit = 300;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _loadError;
    public bool HasError => LoadError != null;
    public bool HasRows => Items.Count > 0;
    public bool IsEmpty => !HasError && Items.Count == 0;

    public AuditLogViewModel(SessionContext session)
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
            foreach (var a in DesktopServices.Audit.List(_session, from, to, Limit)) Items.Add(a);
            Status = Items.Count == 0 ? "Seçilen ölçütlerde kayıt yok." : $"{Items.Count} kayıt (loglar silinemez)";
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
