using DepoWise.Application.Reports;

namespace DepoWise.Infrastructure.Accounting;

/// <summary>
/// ⭐ M-54 (AR-GE 2026-10-24, F-057): tahsilat/ödeme (kasa-banka hareketi) listesinin Excel/Yazdır tablosu — ekrandaki kolonlarla
/// aynı. Tutar kullanıcıya GİZLİYSE (FieldAccess Finance/Amount) giriş/çıkış kolonları HİÇ konmaz (API yanıtındaki kural ile aynı).
/// </summary>
public static class FinanceIslemTablosu
{
    public static TableModel ToTableModel(IReadOnlyList<FinanceTxnRow> rows, bool tutarGorunur)
    {
        var basliklar = new List<string> { "Tarih", "Tür", "Cari", "Hesap", "Yöntem", "Belge No", "Açıklama" };
        if (tutarGorunur) { basliklar.Add("Giriş"); basliklar.Add("Çıkış"); basliklar.Add("Para Birimi"); }
        basliklar.Add("Durum");
        var sayisal = basliklar.Select(b => b is "Giriş" or "Çıkış").ToList();
        return new TableModel("Tahsilat ve Ödemeler", basliklar, rows.Select(r =>
        {
            var hucre = new List<object?> { r.DateText, r.TypeText, r.PartyTitle ?? "", r.AccountName, r.PaymentMethod ?? "", r.DocNo ?? "", r.Description ?? "" };
            if (tutarGorunur) { hucre.Add(r.In); hucre.Add(r.Out); hucre.Add(r.Currency); }
            hucre.Add(r.IsReversed ? "Ters kayıtla iptal" : r.IsReversalEntry ? "Ters kayıt" : "Geçerli");
            return (IReadOnlyList<object?>)hucre;
        }).ToList(), sayisal);
    }
}
