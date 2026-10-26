using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Accounting;

/// <summary>
/// ⭐ M-52 (2/2) (AR-GE 2026-10-27, F-141, K-02=a) — MUHASEBE AKTARIM DOSYASI (programdan bağımsız, standart sütunlu Excel).
/// Kullanıcı kararı: resmî defter programı belli değil → muhasebecinin Logo/Mikro/başka programa kolayca eşleyebileceği GENEL biçim.
/// Üç sayfa: <b>Faturalar</b> (fatura başına tek satır: tarih, no, tür, cari, VKN/TCKN, vergi dairesi, matrah, KDV, tevkifat, toplam),
/// <b>KDV Dökümü</b> (fatura × KDV oranı: matrah, KDV, tevkifat — beyanname eşlemesi için), <b>Cariler</b> (dönemde faturası olan cariler).
/// İptal edilen faturalar dahil EDİLMEZ. Dönem sunucuda hesaplanır (TR iş günü, UTC+3) → iki ortam aynı aralığı kullanır.
/// </summary>
public sealed class MuhasebeAktarimService
{
    /// <summary>Dönem seçenekleri (anahtar, etiket) — iki ortam aynı listeyi gösterir.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Donemler = new[]
    {
        ("bu-ay", "Bu ay"), ("gecen-ay", "Geçen ay"), ("son-3-ay", "Son 3 ay"), ("bu-yil", "Bu yıl"),
    };

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public MuhasebeAktarimService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Dönemin [başlangıç, bitiş) aralığı (UTC ms, TR iş günü başı). Bilinmeyen anahtar → geçen ay.</summary>
    public (long Bas, long Son, string Etiket) Aralik(string? donem)
    {
        var bugun = _clock.UtcNow.ToOffset(TimeSpan.FromHours(3)).Date;
        var buAy = new DateTime(bugun.Year, bugun.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var (bas, son) = donem switch
        {
            "bu-ay" => (buAy, buAy.AddMonths(1)),
            "son-3-ay" => (buAy.AddMonths(-2), buAy.AddMonths(1)),
            "bu-yil" => (new DateTime(bugun.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(bugun.Year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            _ => (buAy.AddMonths(-1), buAy),
        };
        var etiket = Donemler.FirstOrDefault(d => d.Key == donem).Label ?? "Geçen ay";
        return (new DateTimeOffset(bas, TimeSpan.Zero).ToUnixTimeMilliseconds(), new DateTimeOffset(son, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            $"{etiket} ({bas:dd.MM.yyyy}–{son.AddDays(-1):dd.MM.yyyy})");
    }

    private sealed record Fatura(string Id, long Tarih, string No, string? DisNo, string Yon, string CariId, string Para, decimal Kur,
        decimal Brut, decimal Iskonto, decimal Kdv, decimal Tevkifat, decimal Toplam, long? Vade);
    private sealed record Cari(string Kod, string Unvan, string Tur, string? Vkn, string? Tckn, string? Vd, string? Adres, string? Il,
        string? Ilce, string? Tel, string? Eposta);

    /// <summary>Aktarım sayfaları (Faturalar, KDV Dökümü, Cariler). Yetki: fatura görüntüleme + dışa aktarım.</summary>
    public IReadOnlyList<TableModel> Hazirla(SessionContext s, string? donem)
    {
        AccessControl.Require(s, InvoiceService.Module, PermissionAction.View);
        AccessControl.Require(s, "export", PermissionAction.View);
        var (bas, son, etiket) = Aralik(donem);
        var subeler = BranchAccess.Effective(s);
        using var conn = _factory.Create();

        var faturalar = new List<Fatura>();
        using (var cmd = conn.CreateCommand())
        {
            var subeSql = "";
            if (subeler is not null)
            {
                var adlar = subeler.Select((_, i) => "@b" + i).ToList();
                subeSql = adlar.Count == 0 ? " AND i.branch_id IS NULL" : $" AND (i.branch_id IS NULL OR i.branch_id IN ({string.Join(",", adlar)}))";
                for (var i = 0; i < subeler.Count; i++) cmd.AddWithValue("@b" + i, subeler[i]);
            }
            cmd.CommandText = "SELECT i.id, i.invoice_date, i.invoice_no, i.external_no, i.direction, i.party_id, i.currency_code, i.fx_rate, " +
                              "i.subtotal, i.discount_total, i.vat_total, i.withholding_total, i.grand_total, i.due_date FROM invoices i " +
                              "WHERE i.company_id=@c AND i.status='active' AND i.invoice_date >= @bas AND i.invoice_date < @son" + subeSql +
                              " ORDER BY i.invoice_date, i.invoice_no;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@bas", bas);
            cmd.AddWithValue("@son", son);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                faturalar.Add(new Fatura(r.GetString(0), r.GetInt64(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4),
                    r.GetString(5), r.GetString(6), r.IsDBNull(7) ? 1m : Money.Parse(r.GetString(7)), Money.Parse(r.GetString(8)),
                    Money.Parse(r.GetString(9)), Money.Parse(r.GetString(10)), Money.Parse(r.GetString(11)), Money.Parse(r.GetString(12)),
                    r.IsDBNull(13) ? null : r.GetInt64(13)));
        }

        var cariler = new Dictionary<string, Cari>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, code, title, party_type, tax_no, national_id, tax_office, address, city, district, phone, email " +
                              "FROM parties WHERE company_id=@c;";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            string? N(int i) => r.IsDBNull(i) ? null : r.GetString(i);
            while (r.Read())
                cariler[r.GetString(0)] = new Cari(r.GetString(1), r.GetString(2), PartyTypes.Label(r.GetString(3)), N(4), N(5), N(6), N(7), N(8), N(9), N(10), N(11));
        }

        // Fatura × KDV oranı dökümü (satırlardan).
        var dokum = new List<(Fatura F, decimal Oran, decimal Matrah, decimal Kdv, decimal Tevkifat)>();
        if (faturalar.Count > 0)
        {
            var dizin = faturalar.ToDictionary(f => f.Id, StringComparer.Ordinal);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT l.invoice_id, l.vat_rate, l.net_total, l.vat_amount, l.withholding_amount FROM invoice_lines l " +
                              "JOIN invoices i ON i.id = l.invoice_id AND i.company_id = l.company_id " +
                              "WHERE l.company_id=@c AND i.status='active' AND i.invoice_date >= @bas AND i.invoice_date < @son;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@bas", bas);
            cmd.AddWithValue("@son", son);
            var top = new Dictionary<(string Id, decimal Oran), (decimal M, decimal K, decimal T)>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!dizin.ContainsKey(r.GetString(0))) continue;   // şube kapsamı dışı
                var key = (r.GetString(0), Money.Parse(r.GetString(1)));
                var v = top.TryGetValue(key, out var x) ? x : (M: 0m, K: 0m, T: 0m);
                top[key] = (v.M + Money.Parse(r.GetString(2)), v.K + Money.Parse(r.GetString(3)), v.T + Money.Parse(r.GetString(4)));
            }
            dokum = top.Select(kv => (dizin[kv.Key.Id], kv.Key.Oran, kv.Value.M, kv.Value.K, kv.Value.T))
                .OrderBy(x => x.Item1.Tarih).ThenBy(x => x.Item1.No).ThenBy(x => x.Oran).ToList();
        }

        static string Tarih(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("dd.MM.yyyy");
        static string Tur(string yon) => InvoiceDirections.Label(yon);
        Cari C(string id) => cariler.TryGetValue(id, out var c) ? c : new Cari("?", "(bilinmeyen cari)", "", null, null, null, null, null, null, null, null);

        var fSayfa = new TableModel("Faturalar",
            new[] { "Tarih", "Fatura No", "Belge (Dış) No", "Tür", "Cari Kodu", "Cari Ünvan", "VKN", "TCKN", "Vergi Dairesi",
                    "Para Birimi", "Kur", "Brüt", "İskonto", "Matrah", "KDV", "Tevkifat", "Genel Toplam", "Vade" },
            faturalar.Select(f =>
            {
                var c = C(f.CariId);
                return (IReadOnlyList<object?>)new object?[] { Tarih(f.Tarih), f.No, f.DisNo, Tur(f.Yon), c.Kod, c.Unvan, c.Vkn, c.Tckn, c.Vd,
                    f.Para, f.Kur, f.Brut, f.Iskonto, f.Brut - f.Iskonto, f.Kdv, f.Tevkifat, f.Toplam, f.Vade is { } v ? Tarih(v) : null };
            }).ToList(),
            new[] { false, false, false, false, false, false, false, false, false, false, true, true, true, true, true, true, true, false });
        var kSayfa = new TableModel("KDV Dökümü",
            new[] { "Tarih", "Fatura No", "Tür", "Cari Kodu", "VKN / TCKN", "KDV Oranı (%)", "Matrah", "KDV", "Tevkifat" },
            dokum.Select(d =>
            {
                var c = C(d.F.CariId);
                return (IReadOnlyList<object?>)new object?[] { Tarih(d.F.Tarih), d.F.No, Tur(d.F.Yon), c.Kod, c.Vkn ?? c.Tckn, d.Oran, d.Matrah, d.Kdv, d.Tevkifat };
            }).ToList(),
            new[] { false, false, false, false, false, true, true, true, true });
        var kullanilan = faturalar.Select(f => f.CariId).Distinct().Select(C).OrderBy(c => c.Kod, StringComparer.Ordinal).ToList();
        var cSayfa = new TableModel("Cariler",
            new[] { "Cari Kodu", "Ünvan", "Tür", "VKN", "TCKN", "Vergi Dairesi", "Adres", "İl", "İlçe", "Telefon", "E-posta" },
            kullanilan.Select(c => (IReadOnlyList<object?>)new object?[] { c.Kod, c.Unvan, c.Tur, c.Vkn, c.Tckn, c.Vd, c.Adres, c.Il, c.Ilce, c.Tel, c.Eposta }).ToList());
        var bilgi = new TableModel("Bilgi", new[] { "Alan", "Değer" }, new List<IReadOnlyList<object?>>
        {
            new object?[] { "Dönem", etiket },
            new object?[] { "Fatura sayısı", faturalar.Count },
            new object?[] { "Not", "İptal edilen faturalar dahil değildir. Tutarlar faturanın kendi para birimindedir (Kur sütunu)." },
        });
        return new[] { bilgi, fSayfa, kSayfa, cSayfa };
    }
}
