using System;
using System.Collections.ObjectModel;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Ana Ekran — KPI kartları + aktif uyarılar (DashboardService). Tasarım şemasına göre.</summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    public string Welcome { get; }
    public ObservableCollection<KpiCard> Cards { get; } = new();
    public ObservableCollection<DashboardAlert> Alerts { get; } = new();
    public bool HasAlerts => Alerts.Count > 0;

    public DashboardViewModel(SessionContext session)
    {
        Welcome = $"Hoş geldiniz — {DateTime.Now:dd MMMM yyyy dddd}";
        try
        {
            var s = DesktopServices.Dashboard.GetSummary(session);
            Cards.Add(new KpiCard("🚗", s.VehicleCount.ToString(), "Toplam Araç", "accent"));
            Cards.Add(new KpiCard("📦", s.MaterialCount.ToString(), "Malzeme Çeşidi", "accent"));
            Cards.Add(new KpiCard("⚠️", s.LowStockCount.ToString(), "Düşük Stok", "warning"));
            Cards.Add(new KpiCard("📝", s.PendingRequestCount.ToString(), "Bekleyen Talep", "accent"));
            Cards.Add(new KpiCard("👥", s.PersonnelCount.ToString(), "Aktif Personel", "success"));
            foreach (var a in s.Alerts) Alerts.Add(a);
        }
        catch { /* boş veri → kart 0 */ }
    }
}

/// <summary>kind: accent|warning|success|danger → Brand.* renk anahtarına bağlanır.</summary>
public sealed record KpiCard(string Icon, string Value, string Label, string Kind);
