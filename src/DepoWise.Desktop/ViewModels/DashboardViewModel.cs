using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Genel Özet — KPI kartları + kritik uyarılar (DashboardService). İş verisi/sayımlar değişmez;
/// yalnız sunum. Yalnız ilk (en önemli) kart vurgulu (Primary), diğerleri nötr yüzey.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    public ObservableCollection<KpiCard> Cards { get; } = new();
    public ObservableCollection<DashboardAlert> Alerts { get; } = new();

    public bool HasAlerts => Alerts.Count > 0;

    /// <summary>Yükleme/hata/boş durumları (minimum durum modeli; iş mantığı değiştirilmedi).</summary>
    public bool IsLoading { get; }
    public string? LoadError { get; }
    public bool HasError => LoadError is not null;
    public bool IsLoaded => !IsLoading && !HasError;

    public DashboardViewModel(SessionContext session)
    {
        try
        {
            var s = DesktopServices.Dashboard.GetSummary(session);
            // Yalnız ilk kart vurgulu (Primary=true); diğerleri nötr koyu yüzey.
            Cards.Add(new KpiCard(s.VehicleCount.ToString(), "Toplam Araç", "accent", Primary: true, NavKey: "vehicles"));
            Cards.Add(new KpiCard(s.MaterialCount.ToString(), "Malzeme Çeşidi", "neutral", Primary: false, NavKey: "materials"));
            Cards.Add(new KpiCard(s.LowStockCount.ToString(), "Düşük Stok", "warning", Primary: false, NavKey: "materials"));
            Cards.Add(new KpiCard(s.PendingRequestCount.ToString(), "Bekleyen Talep", "neutral", Primary: false, NavKey: "requests:approve"));
            Cards.Add(new KpiCard(s.PersonnelCount.ToString(), "Aktif Personel", "success", Primary: false, NavKey: null));
            foreach (var a in s.Alerts) Alerts.Add(a);
        }
        catch (Exception ex)
        {
            LoadError = "Özet verileri yüklenemedi: " + ex.Message;
        }
        IsLoading = false;
    }

    /// <summary>KPI kartına tıklayınca ilgili ekrana git (köprü). NavKey boşsa hedef ekran henüz yok → işlem yok.</summary>
    [RelayCommand]
    private void Open(string? navKey)
    {
        if (string.IsNullOrEmpty(navKey)) return;
        ShellViewModel.Current?.NavigateCommand.Execute(navKey);
    }
}

/// <summary>Kind: accent|neutral|warning|success|danger (durum tonu). Primary: tek vurgulu kart. NavKey: tıklayınca gidilecek ekran.</summary>
public sealed record KpiCard(string Value, string Label, string Kind, bool Primary, string? NavKey = null);
