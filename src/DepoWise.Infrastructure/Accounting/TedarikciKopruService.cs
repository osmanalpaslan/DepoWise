using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Accounting;

/// <summary>Eşlenmemiş kayıt (rapor satırı).</summary>
public sealed record EslesmemisKayit(string Tur, string Id, string Ad);

/// <summary>
/// ⭐ M-53 (AR-GE 2026-10-26, F-059) — TEDARİKÇİ TEK YERDE.
/// Tanımlar › Tedarikçi (<c>suppliers</c>) ile Cari (<c>parties</c>) ayrı tutuluyor, aradaki köprü (<c>parties.supplier_id</c>,
/// Migration066) ELLE kuruluyordu → aynı firma iki yerde ayrı açılıyor, yakıt/satın alma (supplier_id) ile cari defteri (party_id)
/// raporları bölünüyordu. Kural artık: her tedarikçi tanımının bir tedarikçi carisi, her tedarikçi/karma carinin bir tedarikçi tanımı
/// VARDIR ve köprü KENDİLİĞİNDEN kurulur — açılırken aynı adlı (boşluk/büyük-küçük harf farkı önemsiz) karşılık varsa ona bağlanır,
/// yoksa oluşturulur. Yeni tablo yok; mevcut köprü kullanılır. Eski veride eşlenmemiş kalanlar yönetici aracıyla toplu eşlenir
/// (canlı veriye kendiliğinden dokunulmaz).
/// </summary>
public sealed class TedarikciKopruService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public TedarikciKopruService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    private static readonly CultureInfo Tr = new("tr-TR");
    /// <summary>Eşleştirme anahtarı: kırpılmış, ardışık boşluk teke inmiş, Türkçe küçük harf.</summary>
    internal static string Anahtar(string? ad) => Regex.Replace((ad ?? "").Trim(), @"\s+", " ").ToLower(Tr);

    /// <summary>Tedarikçi tanımı açıldı/var: carisi yoksa bağlar ya da oluşturur. Aynı işlem bloğunda çağrılır. Cari kimliğini döndürür.</summary>
    internal static string CariBagla(DbConnection conn, DbTransaction tx, string companyId, string supplierId, string ad, long now, string? makineKodu)
    {
        var bagli = Skaler(conn, tx, "SELECT id FROM parties WHERE company_id=@c AND supplier_id=@s AND is_deleted=0", ("@c", companyId), ("@s", supplierId));
        if (bagli is not null) return bagli;

        // Aynı adlı, henüz bağlanmamış tedarikçi/karma cari varsa ona bağla.
        string? aday = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id, title FROM parties WHERE company_id=@c AND is_deleted=0 AND supplier_id IS NULL " +
                              "AND party_type IN ('supplier','both');";
            cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            var hedef = Anahtar(ad);
            while (r.Read()) if (Anahtar(r.GetString(1)) == hedef) { aday = r.GetString(0); break; }
        }
        if (aday is not null)
        {
            Calistir(conn, tx, "UPDATE parties SET supplier_id=@s, updated_at=@n, version=version+1 WHERE id=@id AND company_id=@c",
                ("@s", supplierId), ("@n", now), ("@id", aday), ("@c", companyId));
            return aday;
        }

        var id = Guid.NewGuid().ToString("N");
        var kod = BelgeNumarasi.Sonraki(conn, tx, "parties", "code", companyId, "TDR-", makineKodu);
        Calistir(conn, tx,
            "INSERT INTO parties(id, company_id, code, title, party_type, is_person, currency_code, is_active, supplier_id, created_at, updated_at, version, is_deleted) " +
            "VALUES(@id, @c, @code, @t, 'supplier', 0, 'TRY', 1, @s, @n, @n, 1, 0)",
            ("@id", id), ("@c", companyId), ("@code", kod), ("@t", Regex.Replace(ad.Trim(), @"\s+", " ")), ("@s", supplierId), ("@n", now));
        return id;
    }

    /// <summary>Tedarikçi/karma cari açıldı/var: tedarikçi tanımı yoksa bağlar ya da oluşturur. Tanım kimliğini döndürür (müşteri carisinde null).</summary>
    internal static string? TedarikciBagla(DbConnection conn, DbTransaction tx, string companyId, string partyId, long now)
    {
        string? tur = null, ad = null, mevcut = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT party_type, title, supplier_id FROM parties WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@id", partyId);
            cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            if (r.Read()) { tur = r.GetString(0); ad = r.GetString(1); mevcut = r.IsDBNull(2) ? null : r.GetString(2); }
        }
        if (tur is not ("supplier" or "both") || ad is null) return null;
        if (mevcut is not null) return mevcut;

        // Aynı adlı, başka bir cariye bağlı OLMAYAN tedarikçi tanımı varsa ona bağla; yoksa oluştur.
        // ⚠ Tanım adı firmada TEKİLDİR (silinmiş satır dahil). Aynı adlı tanım başka bir cariye bağlıysa ya da silinmişse YENİ tanım
        // açılmaz ve cari bağsız kalır → "eşlenmemiş" raporunda görünür (aynı adlı iki tedarikçi carisi insan kararı ister).
        // Bu koruma olmadan cari açmak benzersizlik hatasıyla DURUYORDU (2026-10-26 paket koşusu yakaladı).
        string? aday = null;
        var adCakisiyor = false;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT s.id, s.name, s.is_deleted, " +
                              "CASE WHEN EXISTS (SELECT 1 FROM parties p WHERE p.company_id=s.company_id AND p.supplier_id=s.id AND p.is_deleted=0) THEN 1 ELSE 0 END " +
                              "FROM suppliers s WHERE s.company_id=@c;";
            cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            var hedef = Anahtar(ad);
            while (r.Read())
            {
                if (Anahtar(r.GetString(1)) != hedef) continue;
                if (Convert.ToInt64(r.GetValue(2)) == 0 && Convert.ToInt64(r.GetValue(3)) == 0) { aday = r.GetString(0); break; }
                adCakisiyor = true;
            }
        }
        if (aday is null && adCakisiyor) return null;
        if (aday is null)
        {
            aday = Guid.NewGuid().ToString("N");
            Calistir(conn, tx, "INSERT INTO suppliers(id, company_id, name, created_at, updated_at, version, is_deleted) VALUES(@id, @c, @n, @now, @now, 1, 0)",
                ("@id", aday), ("@c", companyId), ("@n", Regex.Replace(ad.Trim(), @"\s+", " ")), ("@now", now));
        }
        Calistir(conn, tx, "UPDATE parties SET supplier_id=@s, updated_at=@n, version=version+1 WHERE id=@id AND company_id=@c",
            ("@s", aday), ("@n", now), ("@id", partyId), ("@c", companyId));
        return aday;
    }

    /// <summary>Eşlenmemiş kayıtlar: carisi olmayan tedarikçi tanımları + tedarikçi tanımı olmayan tedarikçi/karma cariler.</summary>
    public IReadOnlyList<EslesmemisKayit> Eslesmemis(SessionContext s)
    {
        AccessControl.Require(s, PartyService.Module, PermissionAction.View);
        using var conn = _factory.Create();
        var list = new List<EslesmemisKayit>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT s.id, s.name FROM suppliers s WHERE s.company_id=@c AND s.is_deleted=0 " +
                              "AND NOT EXISTS (SELECT 1 FROM parties p WHERE p.company_id=s.company_id AND p.supplier_id=s.id AND p.is_deleted=0) ORDER BY s.name;";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(new EslesmemisKayit("Tedarikçi tanımı (carisi yok)", r.GetString(0), r.GetString(1)));
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, title FROM parties WHERE company_id=@c AND is_deleted=0 AND supplier_id IS NULL " +
                              "AND party_type IN ('supplier','both') ORDER BY title;";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(new EslesmemisKayit("Tedarikçi carisi (tanımı yok)", r.GetString(0), r.GetString(1)));
        }
        return list;
    }

    /// <summary>Tüm eşlenmemişleri eşler (aynı adlı karşılığa bağlar ya da oluşturur). Yalnız yönetici. Tek işlem; eşlenen sayıyı döndürür.</summary>
    public int TopluEsle(SessionContext s)
    {
        if (!AccessControl.IsAdmin(s)) throw new ForbiddenException("Toplu eşleme yalnız yöneticiye açıktır.");
        var eksikler = Eslesmemis(s);
        if (eksikler.Count == 0) return 0;
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        var n = 0;
        // Önce cariler (tanım tarafında aynı adlı serbest tanım varsa ona bağlanır), sonra kalan tanımlar.
        foreach (var k in eksikler.Where(k => k.Tur.StartsWith("Tedarikçi carisi", StringComparison.Ordinal)))
            if (TedarikciBagla(conn, tx, s.CompanyId, k.Id, now) is not null) n++;
        foreach (var k in eksikler.Where(k => k.Tur.StartsWith("Tedarikçi tanımı", StringComparison.Ordinal)))
        {
            if (Skaler(conn, tx, "SELECT id FROM parties WHERE company_id=@c AND supplier_id=@s AND is_deleted=0", ("@c", s.CompanyId), ("@s", k.Id)) is not null)
                continue;   // yukarıdaki adımda bağlandı
            CariBagla(conn, tx, s.CompanyId, k.Id, k.Ad, now, s.MachineCode);
            n++;
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "party", "tedarikci-esleme", AuditActions.Update, s.UserId,
            AfterJson: $"{{\"eslenen\":{n}}}"), _clock);
        tx.Commit();
        return n;
    }

    private static string? Skaler(DbConnection conn, DbTransaction tx, string sql, params (string Ad, object Deger)[] p)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql + ";";
        foreach (var (a, d) in p) cmd.AddWithValue(a, d);
        return cmd.ExecuteScalar() as string;
    }

    private static void Calistir(DbConnection conn, DbTransaction tx, string sql, params (string Ad, object Deger)[] p)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql + ";";
        foreach (var (a, d) in p) cmd.AddWithValue(a, d);
        cmd.ExecuteNonQuery();
    }
}
