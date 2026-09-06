using System.Globalization;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Materials;

public sealed record NewMaterialTemplate(
    string Name, string? Code = null, string? Type = null, string? CategoryId = null, string? UnitId = null,
    string? BrandId = null, string? SupplierId = null, decimal MinStock = 0m, decimal UnitPrice = 0m,
    string Currency = "TRY", string? Description = null, string? CompatibleVehicleIds = null);

/// <param name="Version">
/// KLT-01d — DÜZENLEME KİLİDİ jetonu (<c>material_templates.version</c>). Form bu değeri okur,
/// kaydederken geri gönderir; arada başkası kaydettiyse sürüm artmıştır ve kayıt reddedilir.
/// 0 = sürüm bilinmiyor (eski istemci) → kontrol yapılmaz.
/// </param>
public sealed record MaterialTemplateRecord(
    string Id, string Name, string? Code, string? Type, string? CategoryId, string? UnitId,
    string? BrandId, string? SupplierId, decimal MinStock, decimal UnitPrice, string Currency, string? Description,
    string? CompatibleVehicleIds = null, long Version = 0);

/// <summary>Şablon listesi satırı. IsGlobal = admin şablonu (herkese görünür); Mine = aktör oluşturmuş.</summary>
public sealed record MaterialTemplateRow(string Id, string Name, string? Code, string? UnitName, bool IsGlobal, bool Mine)
{
    public string CodeDisplay => string.IsNullOrEmpty(Code) ? "—" : Code!;
    public string ScopeText => IsGlobal ? "Genel (tüm kullanıcılar)" : "Kişisel";
    public override string ToString() => Name;
}

/// <summary>
/// Malzeme yeni-kayıt şablonu (Araç Genel Tanım benzeri). Görünürlük OLUŞTURAN bazlı: admin şablonu (is_global=1)
/// firmada herkese; diğer kullanıcının şablonu yalnız kendisine görünür. Tenant + "material_templates" yetkisi.
/// </summary>
public sealed class MaterialTemplateService
{
    private const string Module = "material_templates";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public MaterialTemplateService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    private static string D(decimal v) => v.ToString(CultureInfo.InvariantCulture);
    private static decimal P(string? v) => decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    public string Create(SessionContext s, NewMaterialTemplate dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Şablon adı zorunlu.");
        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        bool isGlobal = AccessControl.IsAdmin(s); // admin şablonu herkese görünür; diğerininki kişisel
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO material_templates(id, company_id, name, code, type, category_id, unit_id, brand_id, supplier_id,
    min_stock, unit_price, currency, description, compatible_vehicle_ids, created_by, is_global, created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@n,@code,@t,@cat,@u,@br,@sup,@min,@up,@cur,@desc,@cv,@by,@g,@now,@now,1,0);";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@n", dto.Name.Trim());
            cmd.AddWithValue("@code", (object?)Norm(dto.Code) ?? DBNull.Value);
            cmd.AddWithValue("@t", (object?)Norm(dto.Type) ?? DBNull.Value);
            cmd.AddWithValue("@cat", (object?)dto.CategoryId ?? DBNull.Value);
            cmd.AddWithValue("@u", (object?)dto.UnitId ?? DBNull.Value);
            cmd.AddWithValue("@br", (object?)dto.BrandId ?? DBNull.Value);
            cmd.AddWithValue("@sup", (object?)dto.SupplierId ?? DBNull.Value);
            cmd.AddWithValue("@min", D(dto.MinStock));
            cmd.AddWithValue("@up", D(dto.UnitPrice));
            cmd.AddWithValue("@cur", dto.Currency);
            cmd.AddWithValue("@desc", (object?)Norm(dto.Description) ?? DBNull.Value);
            // B-4: yabancı/silinmiş araç id'leri süzülür (firma izolasyonu) — bkz. SanitizeVehicleIds.
            cmd.AddWithValue("@cv", (object?)SanitizeVehicleIds(conn, tx, s.CompanyId, dto.CompatibleVehicleIds) ?? DBNull.Value);
            cmd.AddWithValue("@by", s.UserId);
            cmd.AddWithValue("@g", isGlobal ? 1 : 0);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "material_template", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>Görünür şablonlar: admin şablonları (is_global) + aktörün kendi şablonları. Ad araması.</summary>
    public IReadOnlyList<MaterialTemplateRow> List(SessionContext s, string? search = null, int limit = 200)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT t.id, t.name, t.code, u.name, t.is_global, t.created_by
FROM material_templates t
LEFT JOIN units u ON u.id = t.unit_id
WHERE t.company_id=@c AND t.is_deleted=0
  AND (t.is_global=1 OR t.created_by=@me)
  AND (CAST(@s AS TEXT) IS NULL OR {SqlDialect.LikeTr(conn, "t.name", "@like")} OR {SqlDialect.LikeTr(conn, "COALESCE(t.code,'')", "@like")})
ORDER BY t.is_global DESC, t.name LIMIT @lim;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@me", s.UserId);
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        cmd.AddWithValue("@s", (object?)term ?? DBNull.Value);
        cmd.AddWithValue("@like", term is null ? "%" : "%" + term + "%");
        cmd.AddWithValue("@lim", limit);
        var list = new List<MaterialTemplateRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new MaterialTemplateRow(r.GetString(0), r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                r.GetInt64(4) == 1, !r.IsDBNull(5) && r.GetString(5) == s.UserId));
        return list;
    }

    /// <summary>Şablon içeriği (yeni malzeme formunu doldurmak için). Görünürlük: global veya aktörün kendi şablonu.</summary>
    public MaterialTemplateRecord? Get(SessionContext s, string templateId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, name, code, type, category_id, unit_id, brand_id, supplier_id, min_stock, unit_price, currency, description, compatible_vehicle_ids,
       version
FROM material_templates
WHERE id=@id AND company_id=@c AND is_deleted=0 AND (is_global=1 OR created_by=@me);";
        cmd.AddWithValue("@id", templateId);
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@me", s.UserId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        // ⭐ FAZ 3c — KAÇAK KANALI KAPATILDI. Şablon birim fiyatı, malzemenin birim fiyatının
        // KAYNAĞIDIR (şablondan malzeme türetilir). "fld_materials_unit_price" korumalıyken bu
        // alan açık kalsaydı, kullanıcı aynı bilgiyi şablon ekranından okurdu.
        // Değer 0'lanır; arayüz alanı hiç oluşturmaz (C# kaydından alan çıkarılamaz — ADR-223 · D5).
        var sablonFiyati = MaterialService.FiyatGorunur(s) ? P(r.GetString(9)) : 0m;
        return new MaterialTemplateRecord(r.GetString(0), r.GetString(1), S(2), S(3), S(4), S(5), S(6), S(7),
            P(r.GetString(8)), sablonFiyati, r.GetString(10), S(11), S(12),
            r.IsDBNull(13) ? 0L : r.GetInt64(13));   // KLT-01d: düzenleme kilidi jetonu
    }

    /// <param name="expectedVersion">
    /// KLT-01d — DÜZENLEME KİLİDİ. <see cref="Get"/>'in döndürdüğü <c>Version</c> geri gönderilir.
    ///
    /// Neden gerekli: bu metot <b>12 alanı körlemesine</b> yazıyordu (mevcut değerlerle karşılaştırma yok).
    /// Aynı GENEL şablonu iki firma yöneticisi eşzamanlı düzenlerse ikincisi birincinin tüm
    /// değişikliklerini SESSİZCE eziyordu. (Kişisel şablonda çakışma imkânsızdır:
    /// <see cref="EnsureManageable"/> yalnız <c>created_by</c> sahibine izin verir.)
    ///
    /// Senkron notu: <c>material_templates</c> <c>BusinessSyncService.Tables</c> listesinde YOKTUR →
    /// senkron katmanının LWW politikası bu tabloya uygulanmaz, dolayısıyla burada iyimser kilit
    /// eklemek "iki farklı çakışma politikası" çelişkisi yaratmaz.
    ///
    /// <c>null</c> → kontrol yok (geriye uyumlu: sürüm taşımayan eski çağrılar bozulmaz).
    /// </param>
    /// <summary>Şablonda duran birim fiyat — yazma kararı için AYNI transaction içinde okunur.</summary>
    private static decimal MevcutFiyat(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx,
        string templateId, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT unit_price FROM material_templates WHERE id=@id AND company_id=@c;";
        cmd.AddWithValue("@id", templateId);
        cmd.AddWithValue("@c", companyId);
        return P(cmd.ExecuteScalar() as string ?? "0");
    }

    public void Update(SessionContext s, string templateId, NewMaterialTemplate dto, long? expectedVersion = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureManageable(conn, tx, s, templateId);

        // ⭐ FAZ 3c — YAZMA KAPISI (ADR-223 kanonik kuralı). Fiyatı GÖREMEYEN kullanıcı şablonu
        // güncellediğinde gönderdiği değer YOK SAYILIR ve kayıttaki fiyat KORUNUR; aksi hâlde
        // form 0 gösterdiği için fiyat sessizce sıfırlanırdı (veri kaybı).
        var etkinFiyat = DepoWise.Application.Security.FieldAccess.YazmaDegeri(
            s, DepoWise.Application.Security.FieldProtectionCatalog.Materials,
            DepoWise.Application.Security.FieldProtectionCatalog.UnitPrice,
            dto.UnitPrice, MevcutFiyat(conn, tx, templateId, s.CompanyId), "Birim Fiyat");

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE material_templates SET name=@n, code=@code, type=@t, category_id=@cat, unit_id=@u, brand_id=@br,
    supplier_id=@sup, min_stock=@min, unit_price=@up, currency=@cur, description=@desc, compatible_vehicle_ids=@cv,
    version=version+1, updated_at=@now
WHERE id=@id AND company_id=@c AND is_deleted=0" + EditLockGuard.Clause(expectedVersion) + ";";
            cmd.AddWithValue("@n", dto.Name.Trim());
            cmd.AddWithValue("@code", (object?)Norm(dto.Code) ?? DBNull.Value);
            cmd.AddWithValue("@t", (object?)Norm(dto.Type) ?? DBNull.Value);
            cmd.AddWithValue("@cat", (object?)dto.CategoryId ?? DBNull.Value);
            cmd.AddWithValue("@u", (object?)dto.UnitId ?? DBNull.Value);
            cmd.AddWithValue("@br", (object?)dto.BrandId ?? DBNull.Value);
            cmd.AddWithValue("@sup", (object?)dto.SupplierId ?? DBNull.Value);
            cmd.AddWithValue("@min", D(dto.MinStock));
            cmd.AddWithValue("@up", D(etkinFiyat));
            cmd.AddWithValue("@cur", dto.Currency);
            cmd.AddWithValue("@desc", (object?)Norm(dto.Description) ?? DBNull.Value);
            // B-4: yabancı/silinmiş araç id'leri süzülür (firma izolasyonu) — bkz. SanitizeVehicleIds.
            cmd.AddWithValue("@cv", (object?)SanitizeVehicleIds(conn, tx, s.CompanyId, dto.CompatibleVehicleIds) ?? DBNull.Value);
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", templateId);
            cmd.AddWithValue("@c", s.CompanyId);
            EditLockGuard.Bind(cmd, expectedVersion);
            if (cmd.ExecuteNonQuery() == 0)
            {
                // Kayıt duruyorsa sebep sürüm uyuşmazlığıdır → ConcurrencyException (409).
                // tx.Commit() ÇAĞRILMAZ → 12 alanın hiçbiri ve AUDIT KAYDI yazılmaz.
                EditLockGuard.ThrowIfStale(conn, tx, "material_templates", templateId, s.CompanyId, expectedVersion);
                throw new ForbiddenException("Şablon bulunamadı veya başka firmaya ait.");
            }
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "material_template", templateId, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    public void Delete(SessionContext s, string templateId)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureManageable(conn, tx, s, templateId);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE material_templates SET is_deleted=1, version=version+1, updated_at=@now WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", templateId);
            cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Şablon bulunamadı veya başka firmaya ait.");
        }

        // B-3 (PRT-01 Grup 2b, 2026-08-10) — ŞABLON SİLİNİNCE BAĞLI MALZEMELERİN BAĞI DA TEMİZLENİR.
        //
        // Eskiden yalnız şablon is_deleted=1 yapılıyordu; malzemelerin materials.template_id'si kalıyordu.
        // Sonuç (koddan doğrulandı): ReportService.MaterialsByTemplate sorgusunda t.is_deleted FİLTRESİ YOK
        // → SİLİNMİŞ şablon, bağlı malzemeleriyle birlikte raporda görünmeye DEVAM ediyordu. Aynı malzemeler
        // MaterialsNonTemplate'e de (template_id IS NULL) giremediği için "şablonsuz" sayılmıyordu.
        //
        // Bağı temizleyince: INNER JOIN eşleşme bulamaz → silinen şablon rapordan düşer; malzemeler
        // "şablon-dışı" raporuna geri döner. ReportService'e DOKUNULMADI (rapor mantığı değişmiyor).
        //
        // version+updated_at ARTIRILIR: materials senkron kapsamındadır (BusinessSyncService), değişikliğin
        // diğer makinelere gitmesi gerekir. company_id süzmesi tenant izolasyonunu korur.
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE materials SET template_id=NULL, version=version+1, updated_at=@now " +
                              "WHERE template_id=@id AND company_id=@c;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", templateId);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();   // 0 satır normaldir (şablona bağlı malzeme olmayabilir)
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "material_template", templateId, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Düzenleme/silme: global şablonu yalnız admin; kişisel şablonu yalnız sahibi (veya admin) yönetir.</summary>
    private static void EnsureManageable(DbConnection conn, DbTransaction tx, SessionContext s, string templateId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT created_by, is_global FROM material_templates WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", templateId);
        cmd.AddWithValue("@c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Şablon bulunamadı veya başka firmaya ait.");
        var createdBy = r.IsDBNull(0) ? null : r.GetString(0);
        bool isGlobal = r.GetInt64(1) == 1;
        if (AccessControl.IsAdmin(s)) return;                       // admin tümünü yönetir
        if (isGlobal) throw new ForbiddenException("Genel şablonu yalnız admin düzenleyebilir.");
        if (createdBy != s.UserId) throw new ForbiddenException("Yalnız kendi şablonunuzu düzenleyebilirsiniz.");
    }

    private static string? Norm(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>
    /// B-4 (PRT-01 Grup 2b, 2026-08-10) — FİRMA İZOLASYONU: <c>compatible_vehicle_ids</c> virgülle ayrık
    /// SERBEST METİNDİR; eskiden istemciden geleni hiçbir doğrulama yapmadan yazıyorduk → bir firmanın
    /// kullanıcısı başka firmanın araç id'sini kendi şablonuna yazabilirdi.
    ///
    /// Malzeme tarafındaki emsal <see cref="MaterialService"/>.<c>EnsureVehicleOwned</c> yabancı id'de
    /// İSTİSNA atar. Burada bilerek <b>SÜZME</b> tercih edildi: bu kolon serbest metindir, FK'si yoktur ve
    /// eski kayıtlarda silinmiş araçların id'leri kalmış olabilir. İstisna atmak, eski bir şablonu
    /// düzenlemeyi tamamen ENGELLERDİ (işlevsel gerileme). Süzme aynı garantiyi verir — yabancı id ASLA
    /// yazılamaz — ve eski veriyi kendiliğinden temizler.
    ///
    /// Sıra korunur, tekrarlar atılır. Firma dışı / silinmiş / var olmayan id'ler DÜŞÜRÜLÜR.
    /// </summary>
    private static string? SanitizeVehicleIds(DbConnection conn, DbTransaction tx, string companyId, string? raw)
    {
        var ids = (raw ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return null;

        var kept = new List<string>();
        foreach (var id in ids)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT COUNT(*) FROM vehicles WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", companyId);
            if (Convert.ToInt64(cmd.ExecuteScalar()) > 0) kept.Add(id);
        }
        return kept.Count == 0 ? null : string.Join(",", kept);
    }
}
