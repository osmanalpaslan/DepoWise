using DepoWise.Application.Reports;
using DepoWise.Application.Requests;

namespace DepoWise.Infrastructure.Requests;

/// <summary>
/// ⭐ M-54 (AR-GE 2026-10-24, F-057): talep listesinin Excel/Yazdır tablosu — ekrandaki kolonlarla aynı (iki ortam aynı tablo).
/// </summary>
public static class RequestListeTablosu
{
    /// <summary>Durum etiketi — masaüstü/web ekranlarındaki metinle aynı.</summary>
    public static string DurumMetni(RequestStatus s) => s switch
    {
        RequestStatus.Draft => "Taslak",
        RequestStatus.Pending => "Beklemede",
        RequestStatus.Approved => "Onaylı",
        RequestStatus.Rejected => "Reddedildi",
        RequestStatus.Cancelled => "İptal",
        _ => s.ToString(),
    };

    /// <summary>API ve masaüstü dışa aktarımın ortak "status" çözümlemesi (liste ucuyla aynı anahtarlar; bilinmeyen = hata).</summary>
    public static bool DurumCoz(string? status, out RequestStatus? durum)
    {
        durum = null;
        if (string.IsNullOrWhiteSpace(status)) return true;
        durum = status.Trim().ToLowerInvariant() switch
        {
            "draft" => RequestStatus.Draft,
            "pending" => RequestStatus.Pending,
            "approved" => RequestStatus.Approved,
            "rejected" => RequestStatus.Rejected,
            "cancelled" => RequestStatus.Cancelled,
            _ => null,
        };
        return durum is not null;
    }

    public static TableModel ToTableModel(IReadOnlyList<RequestListRow> rows)
        => new("Talepler", new[] { "Belge No", "Tarih", "Durum", "Öncelik", "Kalem", "Operasyon Durumu", "Açıklama" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[]
            {
                r.DocNo,
                DateTimeOffset.FromUnixTimeMilliseconds(r.RequestDate).LocalDateTime.ToString("dd.MM.yyyy"),
                DurumMetni(r.Status), r.PriorityText, r.ItemCount, r.OperationStatusText, r.Description ?? "",
            }).ToList(),
            new[] { false, false, false, false, true, false, false });
}
