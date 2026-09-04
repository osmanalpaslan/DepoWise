using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Accounting;

/// <summary>Maliyet merkezi tanım satırı.</summary>
public sealed record CostCenterRow(string Id, string? Code, string Name, string Status, string? Description, long Version)
{
    public string StatusDisplay => Status == "passive" ? "Pasif" : "Aktif";
    public string CodeDisplay => string.IsNullOrEmpty(Code) ? "—" : Code!;
}

public sealed record NewCostCenter(string Name, string? Code = null, string? Status = null, string? Description = null);

/// <summary>Merkez bazlı maliyet özeti satırı (para birimi bazında — birimler KARIŞTIRILMAZ, Money kuralı).</summary>
public sealed record CostCenterSummaryRow(string CostCenterId, string CostCenterName, string Category,
    string Currency, decimal Amount, int Count)
{
    public string AmountDisplay => Amount.ToString("N2", System.Globalization.CultureInfo.CurrentCulture) + " " + Currency;
}

/// <summary>
/// ═══ MLY-01 (ADR-168, 2026-08-28) — MALİYET MERKEZİ ═══
///
/// Tanım CRUD (soft delete + Çöp Kutusu + audit + düzenleme kilidi) + kayıt→merkez BAĞI + merkez bazlı
/// maliyet ÖZETİ. Model: tek kayıt = tek merkez (bağ tablosunda UNIQUE ile kilitli); mevcut tablolara ve
/// servis zincirlerine dokunulmadı (bkz. Migration077 açıklaması).
///
/// <b>ÖZET — MEVCUT HESAPLARI DEĞİŞTİRMEZ:</b> yalnız bağlı kayıtların satırlarını OKUR ve
/// C# decimal'de toplar (SQL SUM yok — Money kuralı, StockBalanceWriter/Araç Raporu emsali);
/// para birimleri AYRI satır olarak döner (kur çevrimi İCAT EDİLMEDİ).
/// <b>KAPSAM:</b> kaynak kaydın şubesi kullanıcının BranchAccess kapsamı dışındaysa o satır
/// özete KATILMAZ — merkez raporu yan kapı değildir. Tenant: her sorgu company_id.
/// </summary>
public sealed class CostCenterService
{
    public const string Module = "cost_centers";

    /// <summary>Bağlanabilir kayıt türleri (maliyet taşıyanlar) — tip → (kaynak tablo, şube kolonu).</summary>
    private static readonly IReadOnlyDictionary<string, (string Table, string BranchCol)> Entities =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            // Boş ("") kapsam kolonu = bu tipte şube denetimi UYGULANMAZ; "şubesiz kayıt gizlenmez"
            // ilkesi geçerlidir (mevcut sistemle tutarlı).
            // ⚠️ Düzeltme (2026-09-04): eski açıklama "yakıt/bakım tablolarında şemada branch yok"
            // diyordu — bu YANLIŞ; Migration027 bu tabloların hepsine `op_branch_id` ekledi. Kolon
            // VAR, burada bilinçli olarak KULLANILMIYOR. Davranış değişmedi, yalnız gerekçe düzeldi.
            ["stock_document"] = ("stock_documents", "from_branch_id"),
            ["fuel_depot_entry"] = ("fuel_depot_entries", ""),
            ["fuel_distribution"] = ("fuel_distributions", ""),
            ["vehicle_maintenance"] = ("vehicle_maintenances", ""),
            // ⭐ MUH-01a (2026-09-04): EKİPMAN BAKIMI. 7b (ADR-191) ekipman bakım hattını araç
            // bakımının birebir karşılığı olarak açtı ve API ucu (`POST /api/equipment-maintenance`)
            // maliyet merkezi bağını YAZMAYA ÇALIŞIYORDU — ama tip bu sözlükte yoktu.
            // 🔴 Sonuç: merkez seçilerek kaydedilseydi bakım YAZILIR, sonra Link() ArgumentException
            // atar ve uç HATA dönerdi → kullanıcı "kaydedilmedi" sanıp tekrar dener, MÜKERRER bakım
            // kaydı oluşurdu. Bugüne kadar tetiklenmedi çünkü hiçbir arayüz bu alanı göndermiyordu;
            // yani yaşayan bir hata değil, ilk kullanan arayüzde patlayacak bir TUZAKTI.
            // Kapsam kolonu KARDEŞİYLE AYNI bırakıldı (""). `equipment_maintenances.op_branch_id`
            // şemada VAR, ama `vehicle_maintenance` da onu kullanmıyor; burada kullanmak ekipman
            // bakımını araç bakımından daha katı yapardı — MUH-01a'nın amacı davranış değiştirmek
            // değil, eksik tipi kapsama almaktı. Kapsam denetimi ayrı bir karardır.
            ["equipment_maintenance"] = ("equipment_maintenances", ""),
        };

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public CostCenterService(IDbConnectionFactory factory, IClock? clock = null)
    { _factory = factory; _clock = clock ?? new SystemClock(); }

    // ══════════════ TANIM CRUD ══════════════

    public IReadOnlyList<CostCenterRow> List(SessionContext s, string? search = null, bool includePassive = true)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        var list = new List<CostCenterRow>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, code, name, status, description, version FROM cost_centers " +
                          "WHERE company_id=@c AND is_deleted=0" + (includePassive ? "" : " AND status='active'") +
                          " ORDER BY name;";
        cmd.AddWithValue("@c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CostCenterRow(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2),
                r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetInt64(5)));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            list = list.Where(x => x.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (x.Code?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (x.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }
        return list;
    }

    public string Create(SessionContext s, NewCostCenter dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Maliyet merkezi adı zorunlu.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO cost_centers(id, company_id, code, name, status, description, " +
                "created_at, updated_at, version, is_deleted) VALUES(@id,@c,@code,@n,@st,@d,@now,@now,1,0);";
            Fields(cmd, dto);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "cost_center", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    public void Update(SessionContext s, string id, NewCostCenter dto, long? expectedVersion = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Maliyet merkezi adı zorunlu.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureOwned(conn, tx, s.CompanyId, id, expectedVersion);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE cost_centers SET code=@code, name=@n, status=@st, description=@d, " +
                "updated_at=@now, version=version+1 WHERE id=@id AND company_id=@c;";
            Fields(cmd, dto);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "cost_center", id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    public void Delete(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureOwned(conn, tx, s.CompanyId, id, null);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE cost_centers SET is_deleted=1, updated_at=@now, version=version+1 WHERE id=@id AND company_id=@c;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "cost_center", id, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    // ══════════════ BAĞ (tek kayıt = tek merkez) ══════════════

    /// <summary>Kaydı merkeze bağlar. Aynı kayda ikinci merkez seçilirse bağ GÜNCELLENİR (tek-merkez kuralı);
    /// costCenterId boş/null → bağ kaldırılır (soft). Kaynak kaydın şubesi kapsam dışındaysa reddedilir.</summary>
    public void Link(SessionContext s, string entityType, string entityId, string? costCenterId)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (!Entities.TryGetValue(entityType, out var e))
            throw new ArgumentException($"Maliyet merkezi bu kayıt türüne bağlanamaz: {entityType}");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // Kaynak kayıt bu firmanın olmalı; şubesi varsa kullanıcının kapsamında olmalı (yan kapı yok).
        string? branchId = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = e.BranchCol.Length == 0
                ? $"SELECT NULL FROM {e.Table} WHERE id=@id AND company_id=@c;"
                : $"SELECT {e.BranchCol} FROM {e.Table} WHERE id=@id AND company_id=@c;";
            cmd.AddWithValue("@id", entityId);
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new ArgumentException("Kayıt bulunamadı veya bu firmaya ait değil.");
            branchId = r.IsDBNull(0) ? null : r.GetString(0);
        }
        if (branchId is not null) BranchAccess.Require(s, branchId, "maliyet merkezi bağı");

        if (!string.IsNullOrWhiteSpace(costCenterId))
        {
            using var cc = conn.CreateCommand();
            cc.Transaction = tx;
            cc.CommandText = "SELECT COUNT(*) FROM cost_centers WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cc.AddWithValue("@id", costCenterId!);
            cc.AddWithValue("@c", s.CompanyId);
            if (Convert.ToInt64(cc.ExecuteScalar()) == 0)
                throw new ArgumentException("Maliyet merkezi bulunamadı veya bu firmaya ait değil.");
        }

        // Tek-merkez upsert: mevcut bağ varsa güncellenir; yoksa eklenir; boş merkez → soft kaldır.
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id FROM cost_center_links WHERE company_id=@c AND entity_type=@t AND entity_id=@e;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@t", entityType);
            cmd.AddWithValue("@e", entityId);
            var mevcut = cmd.ExecuteScalar() as string;
            using var w = conn.CreateCommand();
            w.Transaction = tx;
            if (mevcut is null)
            {
                if (string.IsNullOrWhiteSpace(costCenterId)) { tx.Commit(); return; }   // bağ yok, istenmiyor da
                w.CommandText = "INSERT INTO cost_center_links(id, company_id, cost_center_id, entity_type, entity_id, " +
                    "created_at, updated_at, version, is_deleted) VALUES(@id,@c,@cc,@t,@e,@now,@now,1,0);";
                w.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            }
            else
            {
                w.CommandText = string.IsNullOrWhiteSpace(costCenterId)
                    ? "UPDATE cost_center_links SET is_deleted=1, updated_at=@now, version=version+1 WHERE id=@id;"
                    : "UPDATE cost_center_links SET cost_center_id=@cc, is_deleted=0, updated_at=@now, version=version+1 WHERE id=@id;";
                w.AddWithValue("@id", mevcut);
            }
            if (!string.IsNullOrWhiteSpace(costCenterId)) w.AddWithValue("@cc", costCenterId!);
            if (mevcut is null)
            {
                w.AddWithValue("@c", s.CompanyId);
                w.AddWithValue("@t", entityType);
                w.AddWithValue("@e", entityId);
            }
            w.AddWithValue("@now", now);
            w.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "cost_center_link", entityId, AuditActions.Update, s.UserId,
            AfterJson: $"{{\"entity\":\"{entityType}\",\"costCenter\":\"{costCenterId ?? ""}\"}}"), _clock);
        tx.Commit();
    }

    /// <summary>Aktif merkez seçenekleri (işlem formlarındaki açılır liste).</summary>
    public IReadOnlyList<(string Id, string Name)> Options(SessionContext s)
        => List(s, includePassive: false).Select(x => (x.Id, x.Name)).ToList();

    // ══════════════ ÖZET (mevcut hesapları DEĞİŞTİRMEZ — yalnız okur) ══════════════

    /// <summary>
    /// Merkez bazlı maliyet özeti (iş günü aralığı). Kategoriler: Malzeme Çıkışı (out belgeleri satır
    /// qty×unit_price) · Malzeme Girişi · Yakıt Depo Girişi (litre×fiyat) · Yakıt Dağıtımı · Bakım Malzemesi.
    /// Para birimi bazında AYRI toplanır (kur çevrimi yok); toplama C# decimal'de.
    /// BranchAccess: kapsam dışı şubenin kaydı özete KATILMAZ.
    /// </summary>
    public IReadOnlyList<CostCenterSummaryRow> Summary(SessionContext s, long fromMs, long toMs)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        var izinli = BranchAccess.Allowed(s);
        var set = izinli?.ToHashSet(StringComparer.Ordinal);
        bool Kapsam(string? branchId) => branchId is null || set is null || set.Contains(branchId);

        var toplam = new Dictionary<(string CcId, string CcName, string Cat, string Cur), (decimal Amt, int N)>();
        void Ekle(string ccId, string ccName, string cat, string cur, decimal amt)
        {
            var k = (ccId, ccName, cat, string.IsNullOrEmpty(cur) ? "TRY" : cur);
            toplam[k] = toplam.TryGetValue(k, out var v) ? (v.Amt + amt, v.N + 1) : (amt, 1);
        }
        static decimal D(DbDataReader r, int i) => r.IsDBNull(i) ? 0m
            : decimal.Parse(r.GetString(i), System.Globalization.CultureInfo.InvariantCulture);

        // 1) MALZEME: bağlı stok belgelerinin hareket satırları (belge tipiyle Giriş/Çıkış ayrılır).
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT l.cost_center_id, cc.name, d.doc_type, d.from_branch_id, d.to_branch_id,
       m.quantity, COALESCE(m.unit_price, mat.unit_price, '0'), COALESCE(m.currency_code, mat.currency_code, 'TRY')
FROM cost_center_links l
JOIN cost_centers cc ON cc.id = l.cost_center_id AND cc.is_deleted=0
JOIN stock_documents d ON d.id = l.entity_id AND d.status='active' AND d.is_deleted=0
JOIN stock_movements m ON m.document_id = d.id
JOIN materials mat ON mat.id = m.material_id
WHERE l.company_id=@c AND l.entity_type='stock_document' AND l.is_deleted=0
  AND d.doc_date>=@f AND d.doc_date<=@t;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@f", fromMs);
            cmd.AddWithValue("@t", toMs);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var branch = r.IsDBNull(3) ? (r.IsDBNull(4) ? null : r.GetString(4)) : r.GetString(3);
                if (!Kapsam(branch)) continue;
                var cat = r.GetString(2) == "in" ? "Malzeme Girişi" : "Malzeme Çıkışı";
                Ekle(r.GetString(0), r.GetString(1), cat, r.GetString(7), D(r, 5) * D(r, 6));
            }
        }

        // 2) YAKIT: depo girişleri + dağıtımlar (litre × birim fiyat).
        // Yakıt tablolarında şube/is_deleted kolonu YOKTUR (mevcut şema) — koşullar şemaya uygundur;
        // iptal mekanizması FuelService'in kendi yolundadır ve burada yeniden yorumlanmaz.
        foreach (var (tip, tablo, tarihKolon, kat) in new[]
        {
            ("fuel_depot_entry", "fuel_depot_entries", "entry_date", "Yakıt Depo Girişi"),
            ("fuel_distribution", "fuel_distributions", "distribution_date", "Yakıt Dağıtımı"),
        })
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT l.cost_center_id, cc.name, x.liters, x.unit_price, COALESCE(x.currency_code,'TRY')
FROM cost_center_links l
JOIN cost_centers cc ON cc.id = l.cost_center_id AND cc.is_deleted=0
JOIN {tablo} x ON x.id = l.entity_id AND x.is_deleted=0
WHERE l.company_id=@c AND l.entity_type='{tip}' AND l.is_deleted=0
  AND x.{tarihKolon}>=@f AND x.{tarihKolon}<=@t;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@f", fromMs);
            cmd.AddWithValue("@t", toMs);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                Ekle(r.GetString(0), r.GetString(1), kat, r.GetString(4), D(r, 2) * D(r, 3));
        }

        // 3) BAKIM: bağlı bakım kayıtlarının malzeme satırları (qty × unit_price — Araç Raporu ile aynı kaynak).
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT l.cost_center_id, cc.name, mm.quantity, mm.unit_price
FROM cost_center_links l
JOIN cost_centers cc ON cc.id = l.cost_center_id AND cc.is_deleted=0
JOIN vehicle_maintenances vm ON vm.id = l.entity_id AND vm.is_cancelled=0 AND vm.is_deleted=0
JOIN maintenance_materials mm ON mm.maintenance_id = vm.id
WHERE l.company_id=@c AND l.entity_type='vehicle_maintenance' AND l.is_deleted=0
  AND COALESCE(vm.performed_date,0)>=@f AND COALESCE(vm.performed_date,0)<=@t;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@f", fromMs);
            cmd.AddWithValue("@t", toMs);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                Ekle(r.GetString(0), r.GetString(1), "Bakım Malzemesi", "TRY", D(r, 2) * D(r, 3));
        }

        // 3b) ⭐ MUH-01a: EKİPMAN BAKIMI — araç bakımının birebir karşılığı (7b / ADR-191).
        // Bağı yazmak yetmez: rapora düşmezse merkez seçilir ama maliyet hiçbir yerde görünmez,
        // yani kullanıcı "maliyet merkezine yazdım" sanır. Aynı kategori adı kullanılır ki
        // araç ve ekipman bakım malzemesi tek satırda toplansın.
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT l.cost_center_id, cc.name, mm.quantity, mm.unit_price
FROM cost_center_links l
JOIN cost_centers cc ON cc.id = l.cost_center_id AND cc.is_deleted=0
JOIN equipment_maintenances em ON em.id = l.entity_id AND em.is_cancelled=0 AND em.is_deleted=0
JOIN equipment_maintenance_materials mm ON mm.maintenance_id = em.id
WHERE l.company_id=@c AND l.entity_type='equipment_maintenance' AND l.is_deleted=0
  AND COALESCE(em.performed_date,0)>=@f AND COALESCE(em.performed_date,0)<=@t;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@f", fromMs);
            cmd.AddWithValue("@t", toMs);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                Ekle(r.GetString(0), r.GetString(1), "Bakım Malzemesi", "TRY", D(r, 2) * D(r, 3));
        }

        return toplam
            .Select(kv => new CostCenterSummaryRow(kv.Key.CcId, kv.Key.CcName, kv.Key.Cat, kv.Key.Cur, kv.Value.Amt, kv.Value.N))
            .OrderBy(x => x.CostCenterName, StringComparer.CurrentCulture).ThenBy(x => x.Category).ToList();
    }

    /// <summary>Excel (liste kuralı 2): özet tablosu.</summary>
    public static Application.Reports.TableModel SummaryTable(IReadOnlyList<CostCenterSummaryRow> rows)
        => new("Maliyet Merkezi Özeti",
            new[] { "Maliyet Merkezi", "Kalem", "Tutar", "Para Birimi", "Kayıt Sayısı" },
            rows.Select(x => (IReadOnlyList<object?>)new object?[]
                { x.CostCenterName, x.Category, x.Amount, x.Currency, x.Count }).ToList());

    // ── yardımcılar ──

    private static void Fields(DbCommand cmd, NewCostCenter dto)
    {
        cmd.AddWithValue("@code", string.IsNullOrWhiteSpace(dto.Code) ? DBNull.Value : dto.Code!.Trim());
        cmd.AddWithValue("@n", dto.Name.Trim());
        cmd.AddWithValue("@st", dto.Status == "passive" ? "passive" : "active");
        cmd.AddWithValue("@d", string.IsNullOrWhiteSpace(dto.Description) ? DBNull.Value : dto.Description!.Trim());
    }

    private static void EnsureOwned(DbConnection conn, DbTransaction tx, string companyId, string id, long? expectedVersion)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT version FROM cost_centers WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        var v = cmd.ExecuteScalar();
        if (v is null || v is DBNull) throw new ArgumentException("Maliyet merkezi bulunamadı.");
        if (expectedVersion is { } ev && Convert.ToInt64(v) != ev)
            throw new ConcurrencyException(ev, Convert.ToInt64(v));
    }
}
