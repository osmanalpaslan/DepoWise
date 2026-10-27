using System.Globalization;
using System.Text.RegularExpressions;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Accounting;

/// <summary>Mükerrer grubundaki bir fatura (özet).</summary>
public sealed record MukerrerFatura(string Id, string InvoiceNo, string? ExternalNo, long Tarih, decimal Toplam, string Para, long CreatedAt)
{
    public string Metin => $"{InvoiceNo}" + (string.IsNullOrWhiteSpace(ExternalNo) ? "" : $" (belge {ExternalNo})") +
                           $" · {DateTimeOffset.FromUnixTimeMilliseconds(Tarih).UtcDateTime:dd.MM.yyyy} · {Toplam:#,0.00} {Para}";
}

/// <summary>Olası mükerrer grup. <see cref="Kesin"/>: aynı belge (dış) numarası; değilse aynı gün + aynı toplam.</summary>
public sealed record MukerrerGrup(bool Kesin, string Yon, string CariId, string CariUnvan, IReadOnlyList<MukerrerFatura> Faturalar)
{
    public string Baslik => (Kesin ? "Aynı belge no" : "Aynı gün ve tutar") + $" · {InvoiceDirections.Label(Yon)} · {CariUnvan}";
}

/// <summary>
/// ⭐ M-82 (AR-GE 2026-10-27, F-183) — OLASI MÜKERRER FATURA UYARISI.
/// İki bilgisayar aynı faturayı çevrimdışı ayrı girerse (ya da biri iki kez girilirse) iki kayıt oluşur ve hiçbir uyarı yoktu.
/// Kural (yalnız ETKİN faturalar): <b>kesin</b> = aynı cari + aynı yön + aynı belge (dış) numarası (boşluk/büyük-küçük harf önemsiz);
/// <b>olası</b> = aynı cari + aynı yön + aynı gün + aynı genel toplam ve en az birinde belge numarası BOŞ (iki farklı belge no →
/// farklı faturalar sayılır). Kullanıcı yanlış olanı mevcut iptal akışıyla iptal eder ya da "mükerrer değil" der (firma geneli,
/// eşitlenir — Migration124). Grup, içindeki her çift "mükerrer değil" işaretlenmişse listeden düşer.
/// </summary>
public sealed class MukerrerFaturaService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public MukerrerFaturaService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    private static readonly CultureInfo Tr = new("tr-TR");
    private static string Norm(string? s) => Regex.Replace((s ?? "").Trim(), @"\s+", "").ToUpper(Tr);
    private static string Cift(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + ":" + b : b + ":" + a;

    private sealed record Satir(string Id, string No, string? Dis, string Yon, string Cari, long Tarih, decimal Toplam, string Para, long Olusma);

    public IReadOnlyList<MukerrerGrup> Gruplar(SessionContext s)
    {
        AccessControl.Require(s, InvoiceService.Module, PermissionAction.View);
        using var conn = _factory.Create();
        var satirlar = new List<Satir>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, invoice_no, external_no, direction, party_id, invoice_date, grand_total, currency_code, created_at " +
                              "FROM invoices WHERE company_id=@c AND status='active';";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                satirlar.Add(new Satir(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetString(4),
                    r.GetInt64(5), Money.Parse(r.GetString(6)), r.GetString(7), r.GetInt64(8)));
        }
        var izin = BranchAccess.Effective(s);   // şube kapsamı: faturanın şubesi (aşağıda kapsam dışı olanları ele)
        if (izin is not null)
        {
            var subesi = new Dictionary<string, string?>(StringComparer.Ordinal);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, branch_id FROM invoices WHERE company_id=@c AND status='active';";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) subesi[r.GetString(0)] = r.IsDBNull(1) ? null : r.GetString(1);
            satirlar = satirlar.Where(x => subesi[x.Id] is null || izin.Contains(subesi[x.Id]!)).ToList();
        }

        var reddedilen = new HashSet<string>(StringComparer.Ordinal);
        if (DbIntrospect.TableExists(conn, null, "invoice_duplicate_dismissals"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM invoice_duplicate_dismissals WHERE company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) reddedilen.Add(r.GetString(0));
        }

        var cariAdi = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, title FROM parties WHERE company_id=@c;";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) cariAdi[r.GetString(0)] = r.GetString(1);
        }

        var gruplar = new List<MukerrerGrup>();
        var kesindeki = new HashSet<string>(StringComparer.Ordinal);
        bool HepsiReddedildi(IReadOnlyList<Satir> g) =>
            g.SelectMany((a, i) => g.Skip(i + 1).Select(b => Cift(a.Id, b.Id))).All(reddedilen.Contains);
        MukerrerGrup Yap(bool kesin, IReadOnlyList<Satir> g) => new(kesin, g[0].Yon, g[0].Cari,
            cariAdi.TryGetValue(g[0].Cari, out var ad) ? ad : g[0].Cari,
            g.OrderBy(x => x.Olusma).Select(x => new MukerrerFatura(x.Id, x.No, x.Dis, x.Tarih, x.Toplam, x.Para, x.Olusma)).ToList());

        foreach (var g in satirlar.Where(x => Norm(x.Dis).Length > 0).GroupBy(x => (x.Cari, x.Yon, D: Norm(x.Dis))))
        {
            var liste = g.ToList();
            if (liste.Count < 2 || HepsiReddedildi(liste)) continue;
            gruplar.Add(Yap(true, liste));
            foreach (var x in liste) kesindeki.Add(x.Id);
        }
        foreach (var g in satirlar.Where(x => !kesindeki.Contains(x.Id)).GroupBy(x => (x.Cari, x.Yon, Gun: x.Tarih / 86_400_000, x.Toplam)))
        {
            var liste = g.ToList();
            if (liste.Count < 2) continue;
            var dolu = liste.Select(x => Norm(x.Dis)).Where(d => d.Length > 0).Distinct().Count();
            if (dolu > 1 || liste.All(x => Norm(x.Dis).Length > 0)) continue;   // farklı belge numaraları → farklı faturalar
            if (HepsiReddedildi(liste)) continue;
            gruplar.Add(Yap(false, liste));
        }
        return gruplar.OrderByDescending(g => g.Kesin).ThenBy(g => g.CariUnvan, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>Gruptaki faturaların "mükerrer değil" olduğunu işaretler (gruptaki her çift). Firma geneli, eşitlenir.</summary>
    public void MukerrerDegil(SessionContext s, IReadOnlyList<string> faturaIdleri)
    {
        AccessControl.Require(s, InvoiceService.Module, PermissionAction.Edit);
        var idler = (faturaIdleri ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToList();
        if (idler.Count < 2) throw new ArgumentException("En az iki fatura seçin.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        if (!DbIntrospect.TableExists(conn, null, "invoice_duplicate_dismissals"))
            throw new InvalidOperationException("Veritabanı bu işlemi henüz desteklemiyor (güncelleme bekleniyor).");
        using var tx = conn.BeginTransaction();
        foreach (var id in idler)
        {
            using var k = conn.CreateCommand();
            k.Transaction = tx;
            k.CommandText = "SELECT 1 FROM invoices WHERE id=@id AND company_id=@c;";
            k.AddWithValue("@id", id);
            k.AddWithValue("@c", s.CompanyId);
            if (k.ExecuteScalar() is null) throw new ForbiddenException("Fatura bulunamadı veya başka firmaya ait.");
        }
        foreach (var (a, i) in idler.Select((a, i) => (a, i)))
            foreach (var b in idler.Skip(i + 1))
            {
                var anahtar = Cift(a, b);
                var parca = anahtar.Split(':');
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO invoice_duplicate_dismissals(id, company_id, invoice_a, invoice_b, created_by, created_at, updated_at, version, is_deleted) " +
                                  "VALUES(@id, @c, @a, @b, @u, @n, @n, 1, 0) ON CONFLICT(id) DO UPDATE SET is_deleted=0, updated_at=@n, version=invoice_duplicate_dismissals.version+1;";
                cmd.AddWithValue("@id", anahtar);
                cmd.AddWithValue("@c", s.CompanyId);
                cmd.AddWithValue("@a", parca[0]);
                cmd.AddWithValue("@b", parca[1]);
                cmd.AddWithValue("@u", (object?)s.UserId ?? DBNull.Value);
                cmd.AddWithValue("@n", now);
                cmd.ExecuteNonQuery();
            }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "invoice", string.Join(",", idler), AuditActions.Update, s.UserId,
            AfterJson: "{\"mukerrer_degil\":true}"), _clock);
        tx.Commit();
    }
}
