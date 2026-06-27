using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Requests;
using DepoWise.Application.Security;
using DepoWise.Desktop.Controls;
using DepoWise.Infrastructure.Requests;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Talepler — liste + durum filtresi + arama + detay (kalemler/geçmiş) + onay/ret/iptal/gönder.
/// Komutlar RequestService durum makinesine bağlı; iş kuralları (geçiş/yetki) serviste korunur.
/// Not: Şemada "öncelik" alanı yok → öncelik göstergesi eklenmedi (sahte veri yok).
/// </summary>
public sealed partial class RequestsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<RequestRow> Items { get; } = new();
    public ObservableCollection<RequestItemRow> DetailItems { get; } = new();
    public ObservableCollection<string> History { get; } = new();
    public ObservableCollection<string> Filters { get; } = new() { "Tümü", "Taslak", "Beklemede", "Onaylı", "Reddedildi", "İptal" };

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _selectedFilter = "Tümü";
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string _rejectReason = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;

    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyPropertyChangedFor(nameof(CanApprove))]
    [NotifyPropertyChangedFor(nameof(CanReject))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    private RequestRow? _selected;

    public bool HasSelection => Selected != null;
    public bool CanSubmit => Selected?.Status == RequestStatus.Draft;
    public bool CanApprove => Selected?.Status == RequestStatus.Pending;
    public bool CanReject => Selected?.Status == RequestStatus.Pending;
    public bool CanCancel => Selected is { Status: RequestStatus.Draft or RequestStatus.Pending };

    public RequestsViewModel(SessionContext session)
    {
        _session = session;
        Load();
    }

    private RequestStatus? FilterStatus => SelectedFilter switch
    {
        "Taslak" => RequestStatus.Draft,
        "Beklemede" => RequestStatus.Pending,
        "Onaylı" => RequestStatus.Approved,
        "Reddedildi" => RequestStatus.Rejected,
        "İptal" => RequestStatus.Cancelled,
        _ => null,
    };

    partial void OnSelectedFilterChanged(string value) => Load();

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            foreach (var r in DesktopServices.Requests.List(_session, FilterStatus,
                         string.IsNullOrWhiteSpace(Search) ? null : Search.Trim()))
                Items.Add(new RequestRow(r.Id, r.DocNo, r.Status, r.RequestDate, r.ItemCount, r.Description));
            Status = $"{Items.Count} talep";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        Selected = null;
        DetailItems.Clear();
        History.Clear();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRows));
    }

    partial void OnSelectedChanged(RequestRow? value)
    {
        DetailItems.Clear();
        History.Clear();
        if (value is null) return;
        try
        {
            foreach (var it in DesktopServices.Requests.GetItems(_session, value.Id)) DetailItems.Add(it);
            foreach (var (from, to, reason) in DesktopServices.Requests.GetHistory(value.Id))
                History.Add($"{(from is null ? "—" : RequestRow.StatusLabel(from.Value))} → {RequestRow.StatusLabel(to)}"
                            + (string.IsNullOrWhiteSpace(reason) ? "" : $" ({reason})"));
        }
        catch (Exception ex) { Status = "Detay yüklenemedi: " + ex.Message; }
    }

    [RelayCommand]
    private void Submit() => Act(id => DesktopServices.Requests.Submit(_session, id), "Talep gönderildi.");

    [RelayCommand]
    private void Approve() => Act(id => DesktopServices.Requests.Approve(_session, id), "Talep onaylandı.");

    [RelayCommand]
    private void Reject() => Act(id => DesktopServices.Requests.Reject(_session, id, RejectReason), "Talep reddedildi.");

    [RelayCommand]
    private void Cancel() => Act(id => DesktopServices.Requests.Cancel(_session,
        id, string.IsNullOrWhiteSpace(RejectReason) ? null : RejectReason), "Talep iptal edildi.");

    private void Act(Action<string> action, string ok)
    {
        if (Selected is null) { Status = "Talep seçin."; return; }
        try { action(Selected.Id); RejectReason = ""; Load(); Status = ok; }
        catch (Exception ex) { Status = "İşlem başarısız: " + ex.Message; }
    }
}

public sealed record RequestRow(string Id, string DocNo, RequestStatus Status, long RequestDate, int ItemCount, string? Description)
{
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(RequestDate).LocalDateTime.ToString("dd.MM.yyyy");
    public string DescriptionDisplay => string.IsNullOrWhiteSpace(Description) ? "—" : Description!;
    public string StatusText => StatusLabel(Status);
    public BadgeKind StatusKind => Status switch
    {
        RequestStatus.Pending => BadgeKind.Warning,
        RequestStatus.Approved => BadgeKind.Success,
        RequestStatus.Rejected => BadgeKind.Danger,
        _ => BadgeKind.Neutral,
    };

    public static string StatusLabel(RequestStatus s) => s switch
    {
        RequestStatus.Draft => "Taslak",
        RequestStatus.Pending => "Beklemede",
        RequestStatus.Approved => "Onaylı",
        RequestStatus.Rejected => "Reddedildi",
        RequestStatus.Cancelled => "İptal",
        _ => s.ToString(),
    };
}
