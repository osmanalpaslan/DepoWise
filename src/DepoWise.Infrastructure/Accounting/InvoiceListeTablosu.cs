using DepoWise.Application.Reports;

namespace DepoWise.Infrastructure.Accounting;

/// <summary>
/// ⭐ M-54 (AR-GE 2026-10-24, F-057): fatura listesinin Excel/Yazdır tablosu — ekrandaki kolonlarla aynı. Tutar alanı kullanıcıya
/// GİZLİYSE (FieldAccess) kolon HİÇ konmaz: "0 ₺" yazmak yanlış bilgi olurdu (API yanıtındaki kural ile aynı).
/// </summary>
public static class InvoiceListeTablosu
{
    public static TableModel ToTableModel(IReadOnlyList<InvoiceListRow> rows, bool tutarGorunur)
    {
        var basliklar = new List<string> { "Fatura No", "Tür", "Cari", "Karşı Belge No", "Tarih", "Vade" };
        if (tutarGorunur) { basliklar.Add("Tutar"); basliklar.Add("Para Birimi"); }
        basliklar.Add("Durum");
        var sayisal = basliklar.Select(b => b == "Tutar").ToList();
        return new TableModel("Faturalar", basliklar, rows.Select(r =>
        {
            var hucre = new List<object?> { r.InvoiceNo, r.DirectionText, r.PartyTitle, r.ExternalNo ?? "", r.DateText, r.DueText };
            if (tutarGorunur) { hucre.Add(r.GrandTotal); hucre.Add(r.Currency); }
            hucre.Add(r.StatusText);
            return (IReadOnlyList<object?>)hucre;
        }).ToList(), sayisal);
    }
}
