using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Sync;

/// <summary>Temizlik sonucu. <c>TamCekimGerekli</c>: kapsam GENİŞLEDİ → çekme imleci sıfırlanmalı (önceden inmemiş satırlar gelsin).
/// <c>Ertelendi</c>: gönderim sayacı henüz bilinmiyor → hiçbir şey yapılmadı, sonraki girişte tekrar denenir.</summary>
public sealed record YerelKapsamSonucu(int Silinen, int BekleyenKorunan, bool TamCekimGerekli, bool Ertelendi, string YeniImza);

/// <summary>
/// ⭐ M-09 (AR-GE 2026-10-18, 05 F-040, A3b R4/P9) — ŞUBE YETKİSİ DARALINCA YEREL VERİ TEMİZLİĞİ.
///
/// <b>Sorun.</b> Çekme yalnız kullanıcının şube kapsamındaki satırları indirir, ama daha önce (daha geniş yetkiyle ya da aynı
/// bilgisayarı kullanan başka bir kullanıcıyla) inmiş kapsam dışı satırlar SİLİNMİYORDU → yetkisi alınan kişi eski şubenin
/// verisini bilgisayarında taşımaya devam ediyordu (gizlilik). Tersi de vardı: kapsam genişleyince imleç ileride olduğu için
/// diğer şubenin eski satırları hiç inmiyordu.
///
/// <b>Kural.</b> Girişte kullanıcının etkin kapsamı (sınırsız = <c>*</c>) bu bilgisayardaki yerel verinin kapsamıyla karşılaştırılır:
/// • Daraldıysa: şube kapsamlı tablolarda (çekmeyle AYNI liste) kapsam dışı ve şubesi dolu satırlar silinir; ebeveyni silinen
///   çocuk satırlar da. <b>Gönderilmemiş yerel kayıt ASLA silinmez</b> (yerel sayaç &gt; gönderilmiş sayaç; gönderim sayacı
///   bilinmiyorsa hiçbir şey yapılmaz). Bekleyen çocuğu olan ebeveyn de korunur. Sert silme: sunucuya silme olarak YAYILMAZ.
/// • Genişlediyse: silme yok; çekme imleci sıfırlanır (tam çekim).
/// Bilinmeyen eski kapsam (ilk kez) güvenli tarafta "sınırsız" sayılır.
/// </summary>
public static class YerelKapsamTemizligi
{
    public const string Sinirsiz = "*";

    /// <summary>Kapsam imzası: null (sınırsız) → "*"; aksi hâlde sıralı, tekil şube kimlikleri (boş liste = yalnız şubesizler).</summary>
    public static string Imza(IReadOnlyList<string>? kapsam)
        => kapsam is null ? Sinirsiz : "[" + string.Join(",", kapsam.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)) + "]";

    private static HashSet<string>? Coz(string imza)
    {
        if (string.IsNullOrWhiteSpace(imza) || imza == Sinirsiz) return null;
        var ic = imza.Trim('[', ']');
        return new HashSet<string>(ic.Length == 0 ? Array.Empty<string>() : ic.Split(','), StringComparer.Ordinal);
    }

    /// <summary><paramref name="genis"/> kapsamı <paramref name="dar"/>'ı içeriyor mu?</summary>
    public static bool Kapsar(string genis, string dar)
    {
        var g = Coz(genis); var d = Coz(dar);
        if (g is null) return true;
        if (d is null) return false;
        return d.IsSubsetOf(g);
    }

    /// <param name="oncekiImza">Bu bilgisayardaki yerel verinin kapsamı (yoksa null/boş → "*" sayılır).</param>
    /// <param name="gonderilenSeq">Sunucunun kabul ettiği son yerel sayaç (<c>sync_push_seq</c>); &lt; 0 = bilinmiyor.</param>
    public static YerelKapsamSonucu Uygula(IDbConnectionFactory factory, string companyId, string? oncekiImza,
        IReadOnlyList<string>? yeniKapsam, long gonderilenSeq)
    {
        var onceki = string.IsNullOrWhiteSpace(oncekiImza) ? Sinirsiz : oncekiImza!;
        var yeni = Imza(yeniKapsam);
        if (onceki == yeni) return new YerelKapsamSonucu(0, 0, false, false, yeni);

        var tamCekim = !Kapsar(onceki, yeni);          // yeni kapsamda eskisinin görmediği şube var → tam çekim
        var daraldi = !Kapsar(yeni, onceki);           // eski kapsamda yenisinin görmediği şube var → temizlik
        if (!daraldi) return new YerelKapsamSonucu(0, 0, tamCekim, false, yeni);
        if (gonderilenSeq < 0) return new YerelKapsamSonucu(0, 0, false, true, onceki);   // bekleyeni ayırt edemeyiz → ertele

        var izinli = Coz(yeni)!.ToList();
        using var conn = factory.Create();
        int silinen = 0, korunan = 0;
        using (var off = conn.CreateCommand()) { off.CommandText = "PRAGMA foreign_keys=OFF;"; off.ExecuteNonQuery(); }
        try
        {
            using var tx = conn.BeginTransaction();
            var ps = string.Join(",", izinli.Select((_, i) => "@k" + i));
            string Disarida(string alias, string kolon)
                => izinli.Count == 0 ? $"{alias}.{kolon} IS NOT NULL" : $"{alias}.{kolon} IS NOT NULL AND {alias}.{kolon} NOT IN ({ps})";
            string Bekleyen(string tablo, string alias) => SayacVar(conn, tx, tablo)
                ? $"({alias}.local_change_seq IS NOT NULL AND {alias}.local_change_seq > @gs)" : "0";

            int Calistir(string sql)
            {
                using var c = conn.CreateCommand();
                c.Transaction = tx;
                c.CommandText = sql;
                c.AddWithValue("@c", companyId);
                c.AddWithValue("@gs", gonderilenSeq);
                for (int i = 0; i < izinli.Count; i++) c.AddWithValue("@k" + i, izinli[i]);
                return c.ExecuteNonQuery();
            }
            long Say(string sql)
            {
                using var c = conn.CreateCommand();
                c.Transaction = tx;
                c.CommandText = sql;
                c.AddWithValue("@c", companyId);
                c.AddWithValue("@gs", gonderilenSeq);
                for (int i = 0; i < izinli.Count; i++) c.AddWithValue("@k" + i, izinli[i]);
                return Convert.ToInt64(c.ExecuteScalar());
            }

            // 1) Çocuklar: ebeveyni kapsam dışı olan, kendisi bekleyen OLMAYAN satırlar.
            foreach (var (cocuk, (ebeveyn, fk)) in BusinessSyncService.SubeKapsamliCocuklar)
            {
                if (!DbIntrospect.TableExists(conn, tx, cocuk) || !DbIntrospect.TableExists(conn, tx, ebeveyn)) continue;
                var kol = BusinessSyncService.SubeKapsamliTablolar.TryGetValue(ebeveyn, out var k) ? k : "branch_id";
                var ebeveynDisarida = $"SELECT p.id FROM {ebeveyn} p WHERE p.company_id=@c AND {Disarida("p", kol)}";
                korunan += (int)Say($"SELECT COUNT(*) FROM {cocuk} x WHERE x.{fk} IN ({ebeveynDisarida}) AND {Bekleyen(cocuk, "x")};");
                silinen += Calistir($"DELETE FROM {cocuk} AS x WHERE x.{fk} IN ({ebeveynDisarida}) AND NOT {Bekleyen(cocuk, "x")};");
            }
            // 2) Doğrudan kapsamlı tablolar: kapsam dışı, bekleyen olmayan ve bekleyen çocuğu kalmamış satırlar.
            foreach (var (tablo, kol) in BusinessSyncService.SubeKapsamliTablolar)
            {
                if (!DbIntrospect.TableExists(conn, tx, tablo) || !DbIntrospect.ColumnExists(conn, tx, tablo, kol)) continue;
                var cocuguKalan = string.Concat(BusinessSyncService.SubeKapsamliCocuklar
                    .Where(c => c.Value.Parent == tablo && DbIntrospect.TableExists(conn, tx, c.Key))
                    .Select(c => $" AND NOT EXISTS (SELECT 1 FROM {c.Key} y WHERE y.{c.Value.Fk} = t.id)"));
                korunan += (int)Say($"SELECT COUNT(*) FROM {tablo} t WHERE t.company_id=@c AND {Disarida("t", kol)} AND {Bekleyen(tablo, "t")};");
                silinen += Calistir($"DELETE FROM {tablo} AS t WHERE t.company_id=@c AND {Disarida("t", kol)} AND NOT {Bekleyen(tablo, "t")}{cocuguKalan};");
            }
            tx.Commit();
        }
        finally
        {
            using var on = conn.CreateCommand(); on.CommandText = "PRAGMA foreign_keys=ON;"; on.ExecuteNonQuery();
        }
        return new YerelKapsamSonucu(silinen, korunan, tamCekim, false, yeni);
    }

    private static bool SayacVar(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, string tablo)
        => DbIntrospect.ColumnExists(conn, tx, tablo, "local_change_seq");
}
