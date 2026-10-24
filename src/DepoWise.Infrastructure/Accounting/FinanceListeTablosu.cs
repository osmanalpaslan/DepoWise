using DepoWise.Application.Reports;

namespace DepoWise.Infrastructure.Accounting;

/// <summary>
/// ⭐ M-54 (AR-GE 2026-10-24, F-057): kasa/banka hesap listesinin Excel/Yazdır tablosu — ekrandaki kolonlarla aynı. Bakiye
/// kullanıcıya GİZLİYSE (FieldAccess) giriş/çıkış/bakiye kolonları HİÇ konmaz (API yanıtındaki kural ile aynı).
/// </summary>
public static class FinanceListeTablosu
{
    public static TableModel ToTableModel(IReadOnlyList<FinanceAccountRow> rows, bool bakiyeGorunur)
    {
        var basliklar = new List<string> { "Kod", "Hesap", "Tür", "Şube", "Banka", "IBAN", "Para Birimi" };
        if (bakiyeGorunur) { basliklar.Add("Giriş"); basliklar.Add("Çıkış"); basliklar.Add("Bakiye"); }
        basliklar.Add("Durum");
        var sayisal = basliklar.Select(b => b is "Giriş" or "Çıkış" or "Bakiye").ToList();
        return new TableModel("Kasa ve Banka Hesapları", basliklar, rows.Select(r =>
        {
            var a = r.Account;
            var hucre = new List<object?> { a.Code, a.Name, a.KindText, a.BranchName ?? "", a.BankName ?? "", a.Iban ?? "", a.Currency };
            if (bakiyeGorunur) { hucre.Add(r.Inflow); hucre.Add(r.Outflow); hucre.Add(r.Balance); }
            hucre.Add(a.StatusText);
            return (IReadOnlyList<object?>)hucre;
        }).ToList(), sayisal);
    }
}
