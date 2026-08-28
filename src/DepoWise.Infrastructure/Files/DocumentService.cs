using System.Security.Cryptography;
using DepoWise.Application.Common;
using DepoWise.Application.Files;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Files;

/// <summary>Belge listesi satırı (meta — dosya içeriği ayrı uçtan indirilir).</summary>
public sealed record DocumentRow(string Id, string EntityType, string EntityId, string EntityLabel,
    string Title, string? DocType, long? ValidFrom, long? ValidUntil, string? Description,
    string FileName, string? Mime, long? SizeBytes, long CreatedAt, string? UploadedBy, long Version)
{
    public string EntityTypeDisplay => DocumentService.EntityLabelOf(EntityType);
}

/// <summary>Belge meta bilgisi (yükleme + düzenleme). Başlık dışında her alan opsiyonel.</summary>
public sealed record DocumentMeta(string Title, string? DocType = null, long? ValidFrom = null,
    long? ValidUntil = null, string? Description = null);

/// <summary>
/// ═══ EVR-01 (ADR-165, 2026-08-27) — EVRAK / BELGE YÖNETİMİ ═══
///
/// <b>Aynı altyapı, ikinci sistem YOK:</b> belgeler mevcut <c>file_records</c> tablosunda
/// <c>kind='document'</c> ile durur; fiziksel içerik aynı <see cref="IFileStorageProvider"/>'a yazılır.
/// Fotoğraf akışına (<see cref="FileService"/>, kind='photo') DOKUNULMAZ.
///
/// <b>SUNUCU-OTORİTELİ:</b> belge içeriği (binary) senkron paketinde TAŞINMAZ — bugün fotoğraflar da
/// taşınmıyor (file_records BusinessSync listesinde yok; masaüstü fotoğrafı yalnız kendi diskinde durur).
/// Belgeler "her yerden erişilsin" gereğiyle SUNUCUDA tutulur: masaüstü de web de aynı API'yi çağırır
/// (şubeler/projeler deseni). Masaüstü çevrimdışıyken evrak eklenemez/görüntülenemez — anlaşılır uyarı.
///
/// <b>İKİ KAPILI YETKİ (LOG-01 deseni):</b>
/// <list type="number">
///   <item><c>files</c> modülü (ekran yetkisi: View/Create/Edit/Delete),</item>
///   <item>belgenin BAĞLI OLDUĞU kaydın modülü (merkezi ekran yetki sisteminde yan kapı olmasın:
///     malzemeyi göremeyen malzemenin belgesini de göremez).</item>
/// </list>
/// Şube/proje belgelerinde ek olarak <see cref="BranchAccess"/> kapsamı uygulanır (fail-closed).
/// </summary>
public sealed class DocumentService
{
    public const string Module = "files";
    public const string Kind = "document";

    /// <summary>Bağlanabilir kayıt türleri: tip → (yetki modülü, varlık tablosu, ad kolonu, etiket).
    /// "company" = kayda bağlı olmayan GENEL firma evrakı (yalnız files modülü kapısından geçer).</summary>
    private static readonly IReadOnlyDictionary<string, (string Module, string Table, string NameCol, string Label)> Entities =
        new Dictionary<string, (string, string, string, string)>(StringComparer.Ordinal)
        {
            ["material"] = ("materials", "materials", "name", "Malzeme"),
            ["vehicle"] = ("vehicles", "vehicles", "internal_code", "Araç"),
            ["personnel"] = ("personnel", "personnel", "full_name", "Personel"),
            ["branch"] = ("branches", "branches", "name", "Şube / Şantiye"),
            ["equipment"] = ("equipment", "equipment", "name", "Ekipman"),   // EKP-01
            ["purchase_order"] = ("purchasing", "purchase_orders", "order_no", "Sipariş"),   // STN-01
            ["project"] = ("branches", "projects", "name", "Proje"),
            ["company"] = (Module, "companies", "name", "Genel (Firma)"),
        };

    public static string EntityLabelOf(string entityType)
        => Entities.TryGetValue(entityType, out var e) ? e.Label : entityType;

    public static IReadOnlyList<(string Key, string Label)> EntityTypes
        => Entities.Select(kv => (kv.Key, kv.Value.Label)).ToList();

    private readonly IDbConnectionFactory _factory;
    private readonly IFileStorageProvider _storage;
    private readonly IClock _clock;

    public DocumentService(IDbConnectionFactory factory, IFileStorageProvider storage, IClock? clock = null)
    { _factory = factory; _storage = storage; _clock = clock ?? new SystemClock(); }

    /// <summary>
    /// Merkezi belge listesi. Tek meta sorgusu + tek etiket sorgusu grubu (satır başına ek sorgu YOK).
    /// GÖRÜNÜRLÜK: kullanıcının View yetkisi OLMAYAN modülün belgeleri sonuçtan ÇIKARILIR (sessiz filtre —
    /// merkezi ekran yan kapı olmaz); şube/proje belgeleri ayrıca BranchAccess kapsamından geçer.
    /// </summary>
    public IReadOnlyList<DocumentRow> List(SessionContext s, string? entityType = null, string? entityId = null, string? search = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();

        var rows = new List<DocumentRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, entity_type, entity_id, title, doc_type, valid_from, valid_until, " +
                "description, storage_key, mime, size_bytes, created_at, uploaded_by, version FROM file_records " +
                "WHERE company_id=@c AND kind=@k AND is_deleted=0" +
                (string.IsNullOrWhiteSpace(entityType) ? "" : " AND entity_type=@et") +
                (string.IsNullOrWhiteSpace(entityId) ? "" : " AND entity_id=@eid") +
                " ORDER BY created_at DESC;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@k", Kind);
            if (!string.IsNullOrWhiteSpace(entityType)) cmd.AddWithValue("@et", entityType);
            if (!string.IsNullOrWhiteSpace(entityId)) cmd.AddWithValue("@eid", entityId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                rows.Add(new DocumentRow(r.GetString(0), r.GetString(1), r.GetString(2), "",
                    r.IsDBNull(3) ? DosyaAdi(r.GetString(8)) : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetInt64(5), r.IsDBNull(6) ? null : r.GetInt64(6),
                    r.IsDBNull(7) ? null : r.GetString(7),
                    DosyaAdi(r.GetString(8)), r.IsDBNull(9) ? null : r.GetString(9),
                    r.IsDBNull(10) ? null : r.GetInt64(10), r.GetInt64(11),
                    r.IsDBNull(12) ? null : r.GetString(12), r.GetInt64(13)));
        }

        // Yetki filtresi: bağlı kaydın modülünde View yoksa satır GÖRÜNMEZ (istisna atılmaz — merkezi liste).
        rows = rows.Where(d => Entities.TryGetValue(d.EntityType, out var e)
                            && AccessControl.Can(s, e.Module, PermissionAction.View)).ToList();

        // Şube/proje kapsamı (BranchAccess): kapsam dışı şubenin/projenin belgesi görünmez.
        rows = KapsamFiltresi(s, conn, rows);

        // Bağlı kayıt etiketleri: tip başına TEK sorgu (IN listesi) — N+1 yok.
        var etiketler = Etiketler(conn, s.CompanyId, rows);
        rows = rows.Select(d => d with
        {
            EntityLabel = d.EntityType == "company" ? "—"
                : etiketler.TryGetValue((d.EntityType, d.EntityId), out var ad) ? ad : "—",
        }).ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            rows = rows.Where(d =>
                d.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || d.FileName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (d.DocType?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || d.EntityLabel.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (d.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }
        return rows;
    }

    /// <summary>Belge yükleme. İki kapı: files.Create + bağlı kaydın modülünde Edit; şube/proje için
    /// BranchAccess.Require. Bağlı kaydın bu firmaya ait ve silinmemiş olduğu doğrulanır.</summary>
    public DocumentRow Save(SessionContext s, string entityType, string? entityId, DocumentMeta meta,
        string? fileName, string? declaredMime, byte[] content)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(meta.Title)) throw new ArgumentException("Belge başlığı zorunlu.");
        if (meta.ValidFrom is { } vf && meta.ValidUntil is { } vu && vu < vf)
            throw new ArgumentException("Geçerlilik bitişi başlangıçtan önce olamaz.");
        var e = Entity(entityType);
        var eid = entityType == "company" ? s.CompanyId : entityId;
        if (string.IsNullOrWhiteSpace(eid)) throw new ArgumentException("Bağlı kayıt seçilmedi.");
        if (entityType != "company") AccessControl.Require(s, e.Module, PermissionAction.Edit);

        var v = DocumentValidation.Validate(fileName, declaredMime, content);
        if (!v.Ok) throw new InvalidOperationException(v.Error);

        using var conn = _factory.Create();
        EnsureEntityOwned(conn, s, entityType, eid!);
        KapsamGerekli(s, conn, entityType, eid!);

        var safeName = FileValidation.SafeFileName(fileName, v.DetectedExt!);
        var storageKey = _storage.Save(s.CompanyId, entityType, eid!, safeName, content);
        var sha = Convert.ToHexString(SHA256.HashData(content));
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");

        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO file_records(id, company_id, entity_type, entity_id, kind, storage_provider, storage_key,
    mime, size_bytes, sha256, title, doc_type, valid_from, valid_until, description, uploaded_by,
    created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@et,@eid,@k,@prov,@key,@mime,@size,@sha,@t,@dt,@vf,@vu,@d,@u,@now,@now,1,0);";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@et", entityType);
            cmd.AddWithValue("@eid", eid!);
            cmd.AddWithValue("@k", Kind);
            cmd.AddWithValue("@prov", _storage.ProviderName);
            cmd.AddWithValue("@key", storageKey);
            cmd.AddWithValue("@mime", v.DetectedMime!);
            cmd.AddWithValue("@size", content.Length);
            cmd.AddWithValue("@sha", sha);
            MetaParam(cmd, meta);
            cmd.AddWithValue("@u", s.UserId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "file_record", id, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"kind\":\"document\",\"entity\":\"{entityType}\",\"mime\":\"{v.DetectedMime}\"}}"), _clock);
        tx.Commit();
        return new DocumentRow(id, entityType, eid!, "", meta.Title.Trim(), meta.DocType, meta.ValidFrom,
            meta.ValidUntil, meta.Description, safeName, v.DetectedMime, content.Length, now, s.UserId, 1);
    }

    /// <summary>Belge içeriğini okur (indirme). İki kapı + tenant + kapsam.</summary>
    public (byte[] Bytes, string FileName, string Mime) Download(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        var d = Bul(conn, s, id);
        var e = Entity(d.EntityType);
        if (d.EntityType != "company") AccessControl.Require(s, e.Module, PermissionAction.View);
        KapsamGerekli(s, conn, d.EntityType, d.EntityId);
        return (_storage.Read(d.StorageKey), d.FileName, d.Mime ?? "application/octet-stream");
    }

    /// <summary>Yalnız META günceller (başlık/tür/tarih/açıklama) — dosya içeriği DEĞİŞMEZ
    /// (yeni içerik = yeni belge yüklenir; sürümleme İCAT EDİLMEDİ — ürün kararı olarak açık).</summary>
    public void UpdateMeta(SessionContext s, string id, DocumentMeta meta)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(meta.Title)) throw new ArgumentException("Belge başlığı zorunlu.");
        if (meta.ValidFrom is { } vf && meta.ValidUntil is { } vu && vu < vf)
            throw new ArgumentException("Geçerlilik bitişi başlangıçtan önce olamaz.");
        using var conn = _factory.Create();
        var d = Bul(conn, s, id);
        if (d.EntityType != "company") AccessControl.Require(s, Entity(d.EntityType).Module, PermissionAction.Edit);
        KapsamGerekli(s, conn, d.EntityType, d.EntityId);

        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE file_records SET title=@t, doc_type=@dt, valid_from=@vf, valid_until=@vu, " +
                "description=@d, updated_at=@now, version=version+1 WHERE id=@id AND company_id=@c;";
            MetaParam(cmd, meta);
            cmd.AddWithValue("@now", _clock.UtcNow.ToUnixTimeMilliseconds());
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "file_record", id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Soft delete (fotoğraf silmeyle aynı desen) + audit. Fiziksel dosya DİSKTE KALIR.</summary>
    public void Delete(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        using var conn = _factory.Create();
        var d = Bul(conn, s, id);
        if (d.EntityType != "company") AccessControl.Require(s, Entity(d.EntityType).Module, PermissionAction.Delete);
        KapsamGerekli(s, conn, d.EntityType, d.EntityId);

        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE file_records SET is_deleted=1, version=version+1, updated_at=@now WHERE id=@id AND company_id=@c;";
            cmd.AddWithValue("@now", _clock.UtcNow.ToUnixTimeMilliseconds());
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "file_record", id, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    // ── yardımcılar ──────────────────────────────────────────────────────────────────────────────

    private sealed record BulunanBelge(string EntityType, string EntityId, string StorageKey, string FileName, string? Mime);

    /// <summary>Belgeyi TENANT kontrolüyle getirir (yalnız kind='document'; fotoğraflar bu yoldan İNDİRİLEMEZ).</summary>
    private static BulunanBelge Bul(System.Data.Common.DbConnection conn, SessionContext s, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT entity_type, entity_id, storage_key, company_id, mime FROM file_records " +
                          "WHERE id=@id AND kind='document' AND is_deleted=0;";
        cmd.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Belge bulunamadı.");
        if (r.GetString(3) != s.CompanyId) throw new ForbiddenException("Belge başka firmaya ait.");
        var key = r.GetString(2);
        return new BulunanBelge(r.GetString(0), r.GetString(1), key, DosyaAdi(key), r.IsDBNull(4) ? null : r.GetString(4));
    }

    private static (string Module, string Table, string NameCol, string Label) Entity(string entityType)
        => Entities.TryGetValue(entityType, out var e) ? e
            : throw new ArgumentException($"Bilinmeyen kayıt türü: {entityType}");

    /// <summary>Bağlı kayıt bu firmaya ait ve silinmemiş olmalı (başka firmanın kaydına belge asılamaz).</summary>
    private static void EnsureEntityOwned(System.Data.Common.DbConnection conn, SessionContext s, string entityType, string entityId)
    {
        var e = Entity(entityType);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = entityType == "company"
            ? "SELECT COUNT(*) FROM companies WHERE id=@id AND is_deleted=0;"
            : $"SELECT COUNT(*) FROM {e.Table} WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", entityId);
        if (entityType != "company") cmd.AddWithValue("@c", s.CompanyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ArgumentException("Bağlı kayıt bulunamadı veya bu firmaya ait değil.");
    }

    /// <summary>Şube belgesi → şube kapsamı; proje belgesi → projenin şantiye bağlarından en az biri kapsamda
    /// (ProjectService.RequireExistingScope ile aynı kural). Diğer tipler modül yetkisiyle yetinir
    /// (fotoğraf akışıyla aynı seviye — BranchAccess yeniden TASARLANMADI).</summary>
    private static void KapsamGerekli(SessionContext s, System.Data.Common.DbConnection conn, string entityType, string entityId)
    {
        if (entityType == "branch") { BranchAccess.Require(s, entityId, "belge"); return; }
        if (entityType != "project") return;
        var izinli = BranchAccess.Allowed(s);
        if (izinli is null) return;
        var baglar = ProjeSubeleri(conn, s.CompanyId, entityId);
        if (baglar.Count == 0) return;   // şantiyesiz proje serbest (şubesiz kayıt ilkesi)
        var set = izinli.ToHashSet(StringComparer.Ordinal);
        if (!baglar.Any(set.Contains))
            throw new ForbiddenException("Bu belge, erişim kapsamınız dışındaki bir projeye bağlı.");
    }

    /// <summary>Liste görünürlüğü için kapsam filtresi (istisna atmaz, satırı çıkarır).</summary>
    private static List<DocumentRow> KapsamFiltresi(SessionContext s, System.Data.Common.DbConnection conn, List<DocumentRow> rows)
    {
        var izinli = BranchAccess.Allowed(s);
        if (izinli is null) return rows;
        var set = izinli.ToHashSet(StringComparer.Ordinal);
        return rows.Where(d => d.EntityType switch
        {
            "branch" => set.Contains(d.EntityId),
            "project" => ProjeSubeleri(conn, s.CompanyId, d.EntityId) is { Count: > 0 } b ? b.Any(set.Contains) : true,
            _ => true,
        }).ToList();
    }

    private static List<string> ProjeSubeleri(System.Data.Common.DbConnection conn, string companyId, string projectId)
    {
        var list = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT branch_id FROM project_branches WHERE project_id=@p AND company_id=@c;";
        cmd.AddWithValue("@p", projectId);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    /// <summary>Bağlı kayıt etiketleri — tip başına TEK IN sorgusu (N+1 yok).</summary>
    private static Dictionary<(string, string), string> Etiketler(System.Data.Common.DbConnection conn, string companyId, List<DocumentRow> rows)
    {
        var sonuc = new Dictionary<(string, string), string>();
        foreach (var grup in rows.Where(d => d.EntityType != "company").GroupBy(d => d.EntityType))
        {
            if (!Entities.TryGetValue(grup.Key, out var e)) continue;
            var ids = grup.Select(d => d.EntityId).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) continue;
            using var cmd = conn.CreateCommand();
            var ps = string.Join(",", ids.Select((_, i) => "@p" + i));
            cmd.CommandText = $"SELECT id, {e.NameCol} FROM {e.Table} WHERE company_id=@c AND id IN ({ps});";
            cmd.AddWithValue("@c", companyId);
            for (int i = 0; i < ids.Count; i++) cmd.AddWithValue("@p" + i, ids[i]);
            using var r = cmd.ExecuteReader();
            while (r.Read()) sonuc[(grup.Key, r.GetString(0))] = r.GetString(1);
        }
        return sonuc;
    }

    private static void MetaParam(System.Data.Common.DbCommand cmd, DocumentMeta meta)
    {
        cmd.AddWithValue("@t", meta.Title.Trim());
        cmd.AddWithValue("@dt", string.IsNullOrWhiteSpace(meta.DocType) ? DBNull.Value : meta.DocType!.Trim());
        cmd.AddWithValue("@vf", (object?)meta.ValidFrom ?? DBNull.Value);
        cmd.AddWithValue("@vu", (object?)meta.ValidUntil ?? DBNull.Value);
        cmd.AddWithValue("@d", string.IsNullOrWhiteSpace(meta.Description) ? DBNull.Value : meta.Description!.Trim());
    }

    /// <summary>storage_key = "firma/tip/id_dosyaadi.ext" → görünen dosya adı ("id_" öneki atılır).</summary>
    private static string DosyaAdi(string storageKey)
    {
        var son = storageKey.Split('/')[^1];
        var i = son.IndexOf('_');
        return i > 0 && i < son.Length - 1 ? son[(i + 1)..] : son;
    }
}
