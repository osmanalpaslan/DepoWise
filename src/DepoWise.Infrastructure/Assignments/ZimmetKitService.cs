using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Assignments;

/// <summary>⭐ M-50: kit kalemi (etiketiyle). <see cref="Mevcut"/>=false → kayıt silinmiş, teslim listesine yüklenmez.</summary>
public sealed record KitKalemi(string AssetType, string AssetId, string Label, decimal Quantity, bool Mevcut = true);

/// <summary>⭐ M-50: zimmet kiti (set şablonu).</summary>
public sealed record ZimmetKiti(string Id, string Ad, IReadOnlyList<KitKalemi> Kalemler)
{
    public string Ozet => $"{Ad} ({Kalemler.Count} kalem)";
    public override string ToString() => Ozet;
}

/// <summary>
/// ═══ M-50 (AR-GE 2026-10-19, 05 F-083) — ZİMMET KİT (SET) ŞABLONU ═══
/// Toplu teslim listesi ad verilerek kit olarak saklanır; teslimde kit seçilince kalemler listeye yüklenir ve teslim mevcut
/// tek işlemli toplu teslimden (<see cref="AssignmentService.IssueMany"/>) geçer — kit kendisi hiçbir hareket YAZMAZ.
/// Kurallar toplu teslimle aynı: tür malzeme/ekipman, miktar &gt; 0, ekipman miktarı 1 ve tekrar edemez, en çok
/// <see cref="AssignmentService.MaxTopluKalem"/> kalem; kalemler firmaya ait olmalı. Firma filtresi oturumdan.
/// </summary>
public sealed class ZimmetKitService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public ZimmetKitService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    private static bool Destekli(System.Data.Common.DbConnection conn) => DbIntrospect.TableExists(conn, null, "assignment_kits");

    private sealed record Satir(string t, string a, decimal q);

    /// <summary>Kiti kaydeder (<paramref name="kitId"/> null → yeni). Kit kimliğini döndürür.</summary>
    public string Kaydet(SessionContext s, string? kitId, string ad, IReadOnlyList<AssignmentLine> kalemler)
    {
        AccessControl.Require(s, AssignmentService.Module, PermissionAction.Create);
        ad = (ad ?? "").Trim();
        if (ad.Length == 0) throw new ArgumentException("Kit adı zorunlu.");
        if (ad.Length > 100) throw new ArgumentException("Kit adı en fazla 100 karakter olabilir.");
        if (kalemler is null || kalemler.Count == 0) throw new ArgumentException("Kitte en az bir kalem olmalı.");
        if (kalemler.Count > AssignmentService.MaxTopluKalem)
            throw new ArgumentException($"Bir kitte en fazla {AssignmentService.MaxTopluKalem} kalem olabilir.");
        foreach (var k in kalemler)
        {
            if (k.AssetType is not ("material" or "equipment")) throw new ArgumentException("Geçersiz varlık türü.");
            if (k.Quantity <= 0) throw new ArgumentException("Miktar sıfırdan büyük olmalı.");
            if (k.AssetType == "equipment" && k.Quantity != 1m) throw new ArgumentException("Ekipman kalemi miktarı 1 olmalı.");
        }
        var ekp = kalemler.Where(k => k.AssetType == "equipment").Select(k => k.AssetId).ToList();
        if (ekp.Count != ekp.Distinct(StringComparer.Ordinal).Count()) throw new ArgumentException("Aynı ekipman kitte iki kez olamaz.");
        // Aynı malzeme birden çok satırdaysa tek satırda toplanır.
        var satirlar = kalemler.GroupBy(k => (k.AssetType, k.AssetId))
            .Select(g => new Satir(g.Key.AssetType, g.Key.AssetId, g.Sum(x => x.Quantity))).ToList();
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        if (!Destekli(conn)) throw new InvalidOperationException("Veritabanı zimmet kitini henüz desteklemiyor (güncelleme bekleniyor).");
        using var tx = conn.BeginImmediate();
        if (!string.IsNullOrWhiteSpace(kitId))   // güncellemede önce kitin bu firmaya ait olduğu doğrulanır
        {
            using var own = conn.CreateCommand();
            own.Transaction = tx;
            own.CommandText = "SELECT COUNT(*) FROM assignment_kits WHERE id=@id AND company_id=@c AND is_deleted=0;";
            own.AddWithValue("@id", kitId); own.AddWithValue("@c", s.CompanyId);
            if (Convert.ToInt64(own.ExecuteScalar()) == 0) throw new ForbiddenException("Kit bulunamadı veya başka firmaya ait.");
        }
        foreach (var k in satirlar)
        {
            using var chk = conn.CreateCommand();
            chk.Transaction = tx;
            chk.CommandText = (k.t == "material" ? "SELECT COUNT(*) FROM materials" : "SELECT COUNT(*) FROM equipment") +
                              " WHERE id=@id AND company_id=@c AND is_deleted=0;";
            chk.AddWithValue("@id", k.a); chk.AddWithValue("@c", s.CompanyId);
            if (Convert.ToInt64(chk.ExecuteScalar()) == 0) throw new ArgumentException("Kitteki bir kalem bulunamadı veya bu firmaya ait değil.");
        }
        var json = JsonSerializer.Serialize(satirlar);
        var id = kitId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            if (string.IsNullOrWhiteSpace(id))
            {
                id = Guid.NewGuid().ToString("N");
                cmd.CommandText = "INSERT INTO assignment_kits(id, company_id, name, lines_json, created_by, created_at, updated_at, is_deleted) " +
                                  "VALUES(@id, @c, @n, @j, @u, @t, @t, 0);";
                cmd.AddWithValue("@u", s.UserId);
            }
            else
                cmd.CommandText = "UPDATE assignment_kits SET name=@n, lines_json=@j, updated_at=@t WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@id", id); cmd.AddWithValue("@c", s.CompanyId); cmd.AddWithValue("@n", ad);
            cmd.AddWithValue("@j", json); cmd.AddWithValue("@t", now);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Kit bulunamadı veya başka firmaya ait.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "assignment_kit", id!, string.IsNullOrWhiteSpace(kitId) ? AuditActions.Create : AuditActions.Update,
            s.UserId, AfterJson: JsonSerializer.Serialize(new { ad, kalem = satirlar.Count })), _clock);
        tx.Commit();
        return id!;
    }

    /// <summary>Kiti siler (geri alınabilir işaret; geçmiş hareketlere dokunmaz).</summary>
    public void Sil(SessionContext s, string kitId)
    {
        AccessControl.Require(s, AssignmentService.Module, PermissionAction.Create);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        if (!Destekli(conn)) throw new InvalidOperationException("Veritabanı zimmet kitini henüz desteklemiyor (güncelleme bekleniyor).");
        using var tx = conn.BeginImmediate();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE assignment_kits SET is_deleted=1, updated_at=@t WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@id", kitId); cmd.AddWithValue("@c", s.CompanyId); cmd.AddWithValue("@t", now);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Kit bulunamadı veya başka firmaya ait.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "assignment_kit", kitId, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Firmanın kitleri (ada göre), kalemleri güncel etiketleriyle. Eski şemada boş liste.</summary>
    public IReadOnlyList<ZimmetKiti> Liste(SessionContext s)
    {
        AccessControl.Require(s, AssignmentService.Module, PermissionAction.View);
        using var conn = _factory.Create();
        if (!Destekli(conn)) return Array.Empty<ZimmetKiti>();
        var ham = new List<(string Id, string Ad, List<Satir> Satirlar)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name, lines_json FROM assignment_kits WHERE company_id=@c AND is_deleted=0 ORDER BY name;";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                ham.Add((r.GetString(0), r.GetString(1), JsonSerializer.Deserialize<List<Satir>>(r.GetString(2)) ?? new()));
        }
        var etiket = new Dictionary<(string, string), string>();
        void Etiketler(string tablo, string tur)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT id, code, name FROM {tablo} WHERE company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) etiket[(tur, r.GetString(0))] = $"{r.GetString(1)} — {r.GetString(2)}";
        }
        if (ham.Count > 0) { Etiketler("materials", "material"); Etiketler("equipment", "equipment"); }
        return ham.Select(k => new ZimmetKiti(k.Id, k.Ad, k.Satirlar.Select(x =>
            etiket.TryGetValue((x.t, x.a), out var e) ? new KitKalemi(x.t, x.a, e, x.q)
                                                      : new KitKalemi(x.t, x.a, "(silinmiş kayıt)", x.q, Mevcut: false)).ToList())).ToList();
    }
}
