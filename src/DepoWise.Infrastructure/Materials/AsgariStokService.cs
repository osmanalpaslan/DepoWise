using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Requests;

namespace DepoWise.Infrastructure.Materials;

/// <summary>Bir malzemenin bir depodaki asgari miktarı (malzeme kartındaki düzenleyici).</summary>
public sealed record DepoAsgari(string LocationId, string LocationName, decimal Asgari);

/// <summary>Öneri listesi satırı. <see cref="Kaynak"/>: "depo" (depoya özel asgari) | "genel" (malzeme kartının asgari stoğu).</summary>
public sealed record AsgariOneri(string MaterialId, string Code, string Name, string? Unit, string LocationId, string LocationName,
    decimal Mevcut, decimal Asgari, string Kaynak)
{
    /// <summary>Asgariye çıkmak için gereken miktar (otomatik sipariş DEĞİL — kullanıcı talep oluştururken değiştirebilir).</summary>
    public decimal Onerilen => Math.Max(0m, Asgari - Mevcut);
    public string KaynakMetni => Kaynak == "depo" ? "Depoya özel" : "Malzeme kartı";
}

/// <summary>
/// ⭐ M-31 (AR-GE 2026-10-26, F-114) — DEPO BAZINDA ASGARİ STOK + SATIN ALMA ÖNERİ LİSTESİ.
/// Kural: her depo × malzeme için geçerli asgari = depoya özel değer (varsa) ya da malzeme kartının genel asgari stoğu. Genel değer
/// YALNIZ malzemenin o depoda bakiye kaydı varsa (o depoda tutulan malzeme) uygulanır — aksi hâlde her boş şantiye, firmadaki her
/// malzeme için öneri üretirdi. Depodaki stok asgarinin ALTINDAYSA öneri çıkar. Sistem sipariş VERMEZ; kullanıcı seçtiği önerilerden
/// tek tıkla malzeme TALEBİ oluşturur (mevcut talep/onay akışı). İki ortam aynı servis; kapsam = çalışma şubesi ya da yetkili şubeler.
/// </summary>
public sealed class AsgariStokService
{
    private const string Modul = "materials";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly RequestService _talepler;

    public AsgariStokService(IDbConnectionFactory factory, RequestService talepler, IClock? clock = null)
    {
        _factory = factory;
        _talepler = talepler;
        _clock = clock ?? new SystemClock();
    }

    public static bool Destekli(DbConnection conn) => DbIntrospect.TableExists(conn, null, "material_location_mins");

    /// <summary>Malzemenin depo bazında asgari değerleri (yalnız tanımlı olanlar, kullanıcının kapsamındaki depolar).</summary>
    public IReadOnlyList<DepoAsgari> Liste(SessionContext s, string materialId)
    {
        AccessControl.Require(s, Modul, PermissionAction.View);
        using var conn = _factory.Create();
        if (!Destekli(conn)) return Array.Empty<DepoAsgari>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT m.location_id, COALESCE(b.name, m.location_id), m.min_qty
FROM material_location_mins m LEFT JOIN branches b ON b.id = m.location_id AND b.company_id = m.company_id
WHERE m.company_id=@c AND m.material_id=@m AND m.is_deleted=0 ORDER BY 2;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@m", materialId);
        var list = new List<DepoAsgari>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var konum = r.GetString(0);
            if (!BranchAccess.CanAccess(s, konum)) continue;
            list.Add(new DepoAsgari(konum, r.GetString(1), Money.Parse(r.GetString(2))));
        }
        return list;
    }

    /// <summary>Depoya özel asgari miktarı yazar; <paramref name="asgari"/> null ya da 0 → depoya özel değer kaldırılır (genel geçerli olur).</summary>
    public void Ayarla(SessionContext s, string materialId, string locationId, decimal? asgari)
    {
        AccessControl.Require(s, Modul, PermissionAction.Edit);
        if (asgari is < 0) throw new ArgumentException("Asgari miktar eksi olamaz.");
        if (string.IsNullOrWhiteSpace(locationId)) throw new ArgumentException("Depo / şantiye seçin.");
        BranchAccess.Require(s, locationId, "asgari stok tanımı");
        using var conn = _factory.Create();
        if (!Destekli(conn)) throw new InvalidOperationException("Veritabanı depo bazında asgari stoğu henüz desteklemiyor (güncelleme bekleniyor).");
        using var tx = conn.BeginTransaction();
        if (!Var(conn, tx, "SELECT 1 FROM materials WHERE id=@x AND company_id=@c AND is_deleted=0", materialId, s.CompanyId))
            throw new ArgumentException("Malzeme bulunamadı.");
        if (!Var(conn, tx, "SELECT 1 FROM branches WHERE id=@x AND company_id=@c AND is_deleted=0", locationId, s.CompanyId))
            throw new ForbiddenException("Şube kapsam dışı: bu depo firmanıza ait değil.");
        var id = materialId + ":" + locationId;
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var sil = asgari is null or 0;
        using (var up = conn.CreateCommand())
        {
            up.Transaction = tx;
            up.CommandText = "UPDATE material_location_mins SET min_qty=@q, is_deleted=@d, updated_at=@n, version=version+1 " +
                             "WHERE id=@id AND company_id=@c;";
            up.AddWithValue("@q", Money.Serialize(asgari ?? 0m));
            up.AddWithValue("@d", sil ? 1 : 0);
            up.AddWithValue("@n", now);
            up.AddWithValue("@id", id);
            up.AddWithValue("@c", s.CompanyId);
            if (up.ExecuteNonQuery() == 0 && !sil)
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO material_location_mins(id, company_id, material_id, location_id, min_qty, created_at, updated_at, version, is_deleted) " +
                                  "VALUES(@id, @c, @m, @l, @q, @n, @n, 1, 0);";
                ins.AddWithValue("@id", id);
                ins.AddWithValue("@c", s.CompanyId);
                ins.AddWithValue("@m", materialId);
                ins.AddWithValue("@l", locationId);
                ins.AddWithValue("@q", Money.Serialize(asgari!.Value));
                ins.AddWithValue("@n", now);
                ins.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    /// <summary>Öneri listesi. <paramref name="locationId"/> verilirse yalnız o depo (kapsam denetlenir); verilmezse kapsamdaki tüm depolar.</summary>
    public IReadOnlyList<AsgariOneri> Oneriler(SessionContext s, string? locationId = null)
    {
        AccessControl.Require(s, Modul, PermissionAction.View);
        if (!string.IsNullOrEmpty(locationId)) BranchAccess.Require(s, locationId, "asgari stok önerisi");
        var kapsam = !string.IsNullOrEmpty(locationId) ? new[] { locationId! } : StokKapsami.Konumlar(s);
        using var conn = _factory.Create();
        var destek = Destekli(conn);

        // Depolar (ad), malzemeler (kod/ad/birim/genel asgari), bakiyeler (depo × malzeme), depoya özel asgariler.
        var depolar = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name FROM branches WHERE company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) depolar[r.GetString(0)] = r.IsDBNull(1) ? r.GetString(0) : r.GetString(1);
        }
        bool KapsamIci(string konum) => depolar.ContainsKey(konum) && (kapsam is null || kapsam.Contains(konum));

        var malzemeler = new Dictionary<string, (string Kod, string Ad, string? Birim, decimal Genel)>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT m.id, m.code, m.name, u.name, m.min_stock FROM materials m
LEFT JOIN units u ON u.id = m.unit_id AND u.company_id = m.company_id
WHERE m.company_id=@c AND m.is_deleted=0;";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                malzemeler[r.GetString(0)] = (r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? 0m : Money.Parse(r.GetString(4)));
        }

        var bakiye = new Dictionary<(string M, string L), decimal>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT material_id, location_id, quantity FROM stock_balances WHERE company_id=@c AND location_id <> '';";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var key = (r.GetString(0), r.GetString(1));
                bakiye[key] = (bakiye.TryGetValue(key, out var v) ? v : 0m) + Money.Parse(r.GetString(2));
            }
        }

        var ozel = new Dictionary<(string M, string L), decimal>();
        if (destek)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT material_id, location_id, min_qty FROM material_location_mins WHERE company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) ozel[(r.GetString(0), r.GetString(1))] = Money.Parse(r.GetString(2));
        }

        var sonuc = new List<AsgariOneri>();
        void Ekle(string m, string l, decimal asgari, string kaynak)
        {
            if (!malzemeler.TryGetValue(m, out var mz) || !KapsamIci(l) || asgari <= 0) return;
            var mevcut = bakiye.TryGetValue((m, l), out var b) ? b : 0m;
            if (mevcut < asgari) sonuc.Add(new AsgariOneri(m, mz.Kod, mz.Ad, mz.Birim, l, depolar[l], mevcut, asgari, kaynak));
        }
        foreach (var ((m, l), q) in ozel) Ekle(m, l, q, "depo");
        foreach (var (m, l) in bakiye.Keys)                     // genel asgari: yalnız o depoda tutulan malzemede, depoya özel yoksa
            if (!ozel.ContainsKey((m, l)) && malzemeler.TryGetValue(m, out var mz)) Ekle(m, l, mz.Genel, "genel");

        return sonuc.OrderBy(x => x.LocationName, StringComparer.CurrentCulture).ThenBy(x => x.Code, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>Seçilen önerilerden TEK talep (aynı depo). Miktarlar kullanıcının verdiğidir (öneri yalnız başlangıç değeri).</summary>
    public RequestHeader TalepOlustur(SessionContext s, string locationId, IReadOnlyList<RequestItemInput> kalemler, string? aciklama = null)
    {
        if (kalemler is null || kalemler.Count == 0) throw new ArgumentException("Talep için en az bir malzeme seçin.");
        if (kalemler.Any(k => k.Quantity <= 0)) throw new ArgumentException("Talep miktarı sıfırdan büyük olmalı.");
        BranchAccess.Require(s, locationId, "asgari stok talebi");
        return _talepler.Create(s, new NewRequest(kalemler, BranchId: locationId,
            Description: string.IsNullOrWhiteSpace(aciklama) ? "Asgari stok önerisinden oluşturuldu" : aciklama!.Trim()));
    }

    /// <summary>Öneri listesinin Excel/PDF tablosu (ekrandaki sütunlarla aynı).</summary>
    public static Application.Reports.TableModel ToTableModel(IReadOnlyList<AsgariOneri> rows) => new(
        "Asgari Stok Önerileri",
        new[] { "Depo / Şantiye", "Kod", "Malzeme", "Birim", "Mevcut", "Asgari", "Önerilen", "Asgari Kaynağı" },
        rows.Select(r => (IReadOnlyList<object?>)new object?[]
            { r.LocationName, r.Code, r.Name, r.Unit, r.Mevcut, r.Asgari, r.Onerilen, r.KaynakMetni }).ToList(),
        new[] { false, false, false, false, true, true, true, false });

    private static bool Var(DbConnection conn, DbTransaction tx, string sql, string x, string c)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.AddWithValue("@x", x);
        cmd.AddWithValue("@c", c);
        return cmd.ExecuteScalar() is not null;
    }
}
