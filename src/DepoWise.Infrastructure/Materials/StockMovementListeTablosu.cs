using DepoWise.Application.Reports;

namespace DepoWise.Infrastructure.Materials;

/// <summary>
/// ⭐ M-54 (AR-GE 2026-10-24, F-057): stok hareketleri listesinin Excel/Yazdır tablosu — ekrandaki kolonlarla aynı (fiyat kolonu
/// ekranda olmadığı için burada da yok → fiyat gizleme kuralı etkilenmez). Miktar yönlü (çıkış eksi) yazılır: Excel'de toplanabilir.
/// </summary>
public static class StockMovementListeTablosu
{
    public static TableModel ToTableModel(IReadOnlyList<StockMovementRow> rows)
        => new("Stok Hareketleri", new[] { "Tarih", "Tip", "Kod", "Malzeme", "Miktar", "Birim", "Depo / Şantiye", "Not", "Belge No", "Durum" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[]
            {
                r.DateText, r.TypeText, r.Code, r.Name, r.Direction * r.Quantity, r.Unit, r.LocationFlowText,
                r.Aciklama ?? "", r.InvoiceNo ?? r.OrderSlipNo ?? r.CreditSlipNo ?? "", r.StatusText,
            }).ToList(),
            new[] { false, false, false, false, true, false, false, false, false, false });
}
