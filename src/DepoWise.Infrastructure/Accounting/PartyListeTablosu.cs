using DepoWise.Application.Reports;

namespace DepoWise.Infrastructure.Accounting;

/// <summary>
/// ⭐ M-54 (AR-GE 2026-10-24, F-057): cari listesinin Excel/Yazdır tablosu — ekrandaki kolonlarla aynı. Bakiye kullanıcıya
/// GİZLİYSE (FieldAccess) borç/alacak/bakiye kolonları HİÇ konmaz (API yanıtındaki kural ile aynı).
/// </summary>
public static class PartyListeTablosu
{
    public static TableModel ToTableModel(IReadOnlyList<PartyListRow> rows, bool bakiyeGorunur)
    {
        var basliklar = new List<string> { "Kod", "Ünvan", "Tip", "Vergi / TCKN", "Telefon", "E-posta", "İl" };
        if (bakiyeGorunur) { basliklar.Add("Borç"); basliklar.Add("Alacak"); basliklar.Add("Bakiye"); }
        basliklar.Add("Durum");
        var sayisal = basliklar.Select(b => b is "Borç" or "Alacak" or "Bakiye").ToList();
        return new TableModel("Cariler", basliklar, rows.Select(r =>
        {
            var p = r.Party;
            var hucre = new List<object?> { p.Code, p.Title, p.TypeText, p.TaxIdText, p.Phone ?? "", p.Email ?? "", p.City ?? "" };
            if (bakiyeGorunur) { hucre.Add(r.Debit); hucre.Add(r.Credit); hucre.Add(r.Balance); }
            hucre.Add(p.StatusText);
            return (IReadOnlyList<object?>)hucre;
        }).ToList(), sayisal);
    }
}
