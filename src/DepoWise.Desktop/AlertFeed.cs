using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;

namespace DepoWise.Desktop;

/// <summary>
/// ═══ BLD-01 (ADR-172) — masaüstü bildirim akışı (tek kaynak: Shell çan sayacı + Uyarılar ekranı) ═══
///
/// YEREL kaynaklar (bakım · muayene · stok · yakıt · geciken iş emri · bekleyen talep) çevrimdışı da
/// üretilir (DesktopServices.Dashboard — türetilmiş, fiziksel kayıt yok). EVRAK bildirimleri
/// sunucu-otoriteli: yalnız ÇEVRİMİÇİYKEN sunucudan alınır (Takvim/Projeler emsali), çevrimdışıysa
/// RemoteOffline=true döner ve ekran "çevrimiçi gerekli" notu gösterir.
/// Okundu işaretleri CİHAZ-YERELDİR (PK-I4): uzak evrak bildirimlerine de BU cihazın işaretleri uygulanır.
/// </summary>
public static class AlertFeed
{
    public static async Task<(List<DashboardAlert> All, bool RemoteOffline)> GetAsync(SessionContext s)
    {
        var list = new List<DashboardAlert>();
        var remoteOffline = false;
        try { list.AddRange(DesktopServices.Dashboard.GetSummary(s).Alerts); } catch { }
        try
        {
            var uzak = await OrgServerClient.ListDocumentAlertsAsync();
            if (uzak is null) remoteOffline = true;
            else
            {
                var donusen = uzak.Select(u => new DashboardAlert(
                    AlertKind.Document, u.Title, u.Detail, "documents", u.IsCritical, u.EntityId));
                list.AddRange(DesktopServices.Dashboard.ApplyReads(s, donusen));
            }
        }
        catch { remoteOffline = true; }
        return (list, remoteOffline);
    }

    /// <summary>Çan sayacı: aktif VE okunmamış bildirim sayısı.</summary>
    public static async Task<int> UnreadCountAsync(SessionContext s)
    {
        var (all, _) = await GetAsync(s);
        return all.Count(a => !a.Read);
    }
}
