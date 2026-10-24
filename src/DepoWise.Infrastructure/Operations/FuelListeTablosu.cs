using DepoWise.Application.Reports;

namespace DepoWise.Infrastructure.Operations;

/// <summary>
/// ⭐ M-54 (AR-GE 2026-10-24, F-057): yakıt dağıtım listesinin Excel/Yazdır tablosu — ekrandaki kolonlarla aynı (sayaç farkı ve
/// ortalama tüketim ekranla aynı formülden: FuelMath). İptal edilen kayıt "İptal edildi" durumuyla yer alır (ekranda gösteriliyorsa).
/// </summary>
public static class FuelListeTablosu
{
    public static TableModel ToTableModel(IReadOnlyList<FuelDistributionRow> rows)
        => new("Yakıt Dağıtımları",
            new[] { "Araç", "Önceki Sayaç", "Güncel Sayaç", "Litre", "Sayaç Farkı", "Ort. Tüketim", "Birim Fiyat", "Toplam", "Para Birimi", "Tarih", "İrsaliye / Fiş No", "Durum" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[]
            {
                r.VehicleCode ?? r.VehicleId, r.PrevMeter, r.CurrentMeter, r.Liters, r.MeterDiffText, r.ConsumptionText,
                r.UnitPrice, r.Liters * r.UnitPrice, r.Currency,
                DateTimeOffset.FromUnixTimeMilliseconds(r.DistributionDate).UtcDateTime.ToString("dd.MM.yyyy"),
                r.InvoiceDisplay, r.StatusText,
            }).ToList(),
            new[] { false, true, true, true, false, false, true, true, false, false, false, false });
}
