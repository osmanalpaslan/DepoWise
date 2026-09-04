using System.Globalization;
using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Materials;

public sealed record NewMaterial(
    string Code, string Name, string? Type = null,
    string? CategoryId = null, string? UnitId = null, string? BrandId = null, string? SupplierId = null,
    decimal MinStock = 0m, decimal UnitPrice = 0m, string Currency = "TRY",
    string? Description = null, string? ExternalEquivalentNote = null,
    string? TemplateId = null);   // yeni kayıt formunda seçilen malzeme şablonu (şablonlu/şablon-dışı rapor ayrımı)

public sealed record MaterialRecord(
    string Id, string CompanyId, string Code, string Name, string? Type,
    decimal MinStock, decimal UnitPrice, string Currency, long CreatedAt);

public sealed record MaterialStock(string MaterialId, string Code, string Name, decimal Quantity);

public sealed record MaterialRefRow(string Id, string Code, string Name)
{
    /// <summary>
    /// Seçim listelerinde gösterilecek metin: <b>KOD — AD</b> (kullanıcı isteği 2026-09-04).
    /// Eskiden yalnız AD gösteriliyordu; kullanıcı kodu yazıp arama yaptığında doğru parçanın
    /// geldiğini <i>doğrulayamıyordu</i>. Kod zaten elde olduğu için tek yerden birleştirilir —
    /// her ekran kendi biçimini kurmasın diye burada durur.
    /// </summary>
    public string Display => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} — {Name}";
}

/// <summary>Malzeme listesi ÖZET ŞERİDİ (web v4, 2026-08-27) — sayfalamadan bağımsız, aktif filtrelerle
/// tutarlı üç sayı. Web'de listenin üstündeki kutuları besler; masaüstü bu alanı okumaz.</summary>
public sealed record MaterialGridSummary(int CriticalCount, int CategoryCount, decimal StockValue);

/// <summary>Malzeme listesi (kolon-bazlı filtre + sayfalama) satırı — <see cref="MaterialListColumns"/>'taki
/// HER kolonun görüntü değerini taşır; ekran hangi kolonları göstereceğine kendi tercihine göre karar verir.</summary>
public sealed record MaterialGridRow(
    string Id, string Code, string Name, string? Type, string? Category, string? Unit, string? Brand,
    string? Supplier, decimal UnitPrice, string Currency, decimal MinStock, decimal Stock, string Status,
    string? Description, string? CompatibleVehicles, string? Equivalents);

/// <summary>Her alan için kullanıcının o kolona yazdığı filtre metni (boş/null = filtre yok). Sıra
/// <see cref="MaterialListColumns.All"/> ile AYNIDIR (birden çok filtre aktifken deterministik öncelik).</summary>
public sealed record MaterialGridFilter(
    string? Code = null, string? Name = null, string? Type = null, string? Category = null, string? Unit = null,
    string? Brand = null, string? Supplier = null, string? UnitPrice = null, string? Currency = null,
    string? MinStock = null, string? Stock = null, string? Status = null, string? Description = null,
    string? CompatibleVehicles = null, string? Equivalents = null);

public sealed record MaterialDetail(
    string Id, string Code, string Name, string? Type,
    string? CategoryId, string? UnitId, string? BrandId, string? SupplierId,
    string? CategoryName, string? UnitName, string? BrandName, string? SupplierName,
    decimal MinStock, decimal UnitPrice, string Currency, string? Description, decimal Stock,
    IReadOnlyList<MaterialRefRow> Equivalents, IReadOnlyList<MaterialRefRow> CompatibleVehicles,
    // DÜZENLEME KİLİDİ: formun açıldığı andaki sürüm. Kaydederken geri gönderilir; kayıt arada
    // değiştiyse sessizce ezilmez (bkz. MaterialService.Update / ConcurrencyException).
    long Version = 0,
    string? TemplateId = null);   // bağlı olduğu malzeme şablonu (düzenlemede korunur/değiştirilebilir)

public sealed record UpdateMaterial(
    string Code, string Name, string? Type = null,
    string? CategoryId = null, string? UnitId = null, string? BrandId = null, string? SupplierId = null,
    decimal MinStock = 0m, decimal UnitPrice = 0m, string? Description = null, string? TemplateId = null);

/// <summary>
/// Malzeme kartı — kod benzersiz (tenant), muadil (çift yönlü, döngü güvenli), uyumlu araç (çoklu seçim).
/// Para decimal + currency. Stok bakiyesi BU SERVİSTE değiştirilmez (ledger ayrı servis).
/// </summary>
public sealed class MaterialService
{
    private const string Module = "materials";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public MaterialService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public string Create(SessionContext s, NewMaterial dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(dto.Code)) throw new ArgumentException("Kod zorunlu.");
        if (!Money.IsSupported(dto.Currency)) throw new ArgumentException($"Desteklenmeyen para birimi: {dto.Currency}");
        // ADR-086: min stok / birim fiyat negatif OLAMAZ (yalnız AÇILIŞ stoğu negatif olabilir — o ayrı,
        // RecordOpening'de). Arayüz de engeller; bu sunucu-tarafı kalkanı (savunma derinliği).
        if (dto.MinStock < 0) throw new ArgumentException("Minimum stok negatif olamaz.");
        if (dto.UnitPrice < 0) throw new ArgumentException("Birim fiyat negatif olamaz.");

        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        if (CodeExists(conn, tx, s.CompanyId, dto.Code, excludeId: null))
            throw new InvalidOperationException($"Bu kod zaten kullanılıyor: {dto.Code}");

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO materials(id, company_id, branch_id, code, name, type, category_id, unit_id, brand_id, supplier_id,
    min_stock, unit_price, currency_code, description, external_equivalent_note, template_id,
    created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@br,@code,@name,@type,@cat,@unit,@brand,@sup,@min,@price,@cur,@desc,@eqnote,@tpl,@now,@now,1,0);";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            // ŞUBE-BAZLI: oturumun çalışma şubesi (Tüm Şubeler → NULL, her şubede görünür).
            cmd.AddWithValue("@br", (object?)DepoWise.Application.Security.BranchScope.Active(s) ?? DBNull.Value);
            cmd.AddWithValue("@code", dto.Code.Trim());
            cmd.AddWithValue("@name", dto.Name);
            cmd.AddWithValue("@type", (object?)DepoWise.Application.Ui.MaterialType.Normalize(dto.Type) ?? DBNull.Value);
            cmd.AddWithValue("@cat", (object?)dto.CategoryId ?? DBNull.Value);
            cmd.AddWithValue("@unit", (object?)dto.UnitId ?? DBNull.Value);
            cmd.AddWithValue("@brand", (object?)dto.BrandId ?? DBNull.Value);
            cmd.AddWithValue("@sup", (object?)dto.SupplierId ?? DBNull.Value);
            cmd.AddWithValue("@min", Money.Serialize(dto.MinStock));
            cmd.AddWithValue("@price", Money.Serialize(dto.UnitPrice));
            cmd.AddWithValue("@cur", dto.Currency);
            cmd.AddWithValue("@desc", (object?)dto.Description ?? DBNull.Value);
            cmd.AddWithValue("@eqnote", (object?)dto.ExternalEquivalentNote ?? DBNull.Value);
            cmd.AddWithValue("@tpl", (object?)dto.TemplateId ?? DBNull.Value);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "material", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>
    /// İÇE AKTARIM için firmanın TÜM malzeme kodları → id (SAYFALAMA YOK).
    ///
    /// ⚠️ NEDEN AYRI METOT: <see cref="List"/> PageRequest kullanır ve <c>PageRequest.MaxLimit = 200</c>
    /// ile SINIRLIDIR — Limit=100000 verilse bile 200 satır döner. İçe aktarım mükerrer kontrolünü buna
    /// dayandırmak, 200'den fazla malzemesi olan firmada 201. kayıttan sonrasını "yok" sanıp KOPYA
    /// oluştururdu. Satır başına IsCodeUnique çağırmak da 2600 satırda 2600 sorgu demekti.
    /// </summary>
    public Dictionary<string, string> AllCodeToId(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT code, id FROM materials WHERE company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@c", s.CompanyId);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetString(0).Trim().ToUpperInvariant()] = r.GetString(1);
        return map;
    }

    public bool IsCodeUnique(SessionContext s, string code, string? excludeId = null)
    {
        using var conn = _factory.Create();
        return !CodeExists(conn, null, s.CompanyId, code, excludeId);
    }

    // ---- Muadil (çift yönlü, döngü güvenli) ----
    public void AddEquivalent(SessionContext s, string materialId, string equivalentId)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (materialId == equivalentId) throw new InvalidOperationException("Malzeme kendisine muadil olamaz.");

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureOwned(conn, tx, s.CompanyId, materialId);
        EnsureOwned(conn, tx, s.CompanyId, equivalentId);
        // Simetrik: her iki yön de yazılır (INSERT OR IGNORE → tekrar güvenli)
        InsertPair(conn, tx, materialId, equivalentId);
        InsertPair(conn, tx, equivalentId, materialId);
        tx.Commit();
    }

    /// <summary>Y-1 (2026-08-09): firma sahipliği doğrulanMIYORDU — başka firmanın iki malzemesi
    /// arasındaki muadil ilişkisi silinebiliyordu. Artık <c>AddEquivalent</c> ile AYNI kontrol uygulanır
    /// (asimetri giderildi). API ucu yoktur; bu metot yalnız masaüstünden çağrılır.</summary>
    public void RemoveEquivalent(SessionContext s, string materialId, string equivalentId)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        using var conn = _factory.Create();
        EnsureOwned(conn, null, s.CompanyId, materialId);
        EnsureOwned(conn, null, s.CompanyId, equivalentId);
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "DELETE FROM material_equivalents WHERE (material_id=@a AND equivalent_material_id=@b) " +
            "OR (material_id=@b AND equivalent_material_id=@a);";
        cmd.AddWithValue("@a", materialId);
        cmd.AddWithValue("@b", equivalentId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// G2-03 (2026-08-10) — Malzemenin muadil listesini TEK transaction içinde değiştirir.
    /// <see cref="SetCompatibleVehicles"/>'ın SİMETRİĞİDİR (yeni mekanizma değil, aynı desen):
    /// önce tüm hedefler doğrulanır, sonra eski bağlar silinip yeni liste yazılır, sonra commit.
    /// Bir hedef bile yabancı/geçersizse <b>HİÇBİRİ</b> yazılmaz (yarım liste oluşmaz).
    ///
    /// ⚠️ <b>null ≠ boş liste.</b> "Dokunma" kararı ÇAĞIRANA aittir: <c>null</c> geldiğinde bu metot
    /// hiç çağrılmaz (bkz. PUT /api/materials/{id}). Boş liste = TÜM muadilleri kaldır.
    /// Hızlı düzenleme pencereleri muadil göndermez → mevcut muadiller korunur.
    ///
    /// Çift yönlülük: bu malzemeye dokunan bağlar HER İKİ YÖNDE silinir, yeni çiftler
    /// <see cref="AddEquivalent"/> ile AYNI kuralla iki yönlü yazılır.
    ///
    /// ⚠️ BİLİNEN SINIR (davranış bilerek DEĞİŞTİRİLMEDİ): <see cref="GetEquivalentGroup"/> TRANSİTİF
    /// çalışır, bu metot ise DOĞRUDAN bağları yazar. A↔B ve B↔C varken A'nın listesinden C çıkarılırsa
    /// C, B üzerinden hâlâ grupta görünür. Aynı sınır masaüstü uzlaştırmasında da vardır; ürün kararı
    /// alınana kadar korunuyor (bkz. plan §5 G2-03 notu / §12 teknik borç kaydı).
    /// </summary>
    public void SetEquivalents(SessionContext s, string materialId, IEnumerable<string> equivalentIds)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var ids = equivalentIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)      // aynı id iki kez gelirse tek satır
            .ToList();

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureOwned(conn, tx, s.CompanyId, materialId);

        // ÖNCE hepsi doğrulanır → tek geçersiz id varsa hiçbir şey yazılmadan istisna (atomiklik).
        foreach (var eq in ids)
        {
            if (eq == materialId) throw new InvalidOperationException("Malzeme kendisine muadil olamaz.");
            EnsureOwned(conn, tx, s.CompanyId, eq);
        }

        // Bu malzemeye dokunan TÜM doğrudan bağlar (iki yön) temizlenir...
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM material_equivalents WHERE material_id=@m OR equivalent_material_id=@m;";
            del.AddWithValue("@m", materialId);
            del.ExecuteNonQuery();
        }
        // ...ardından istenen liste simetrik olarak yazılır.
        foreach (var eq in ids)
        {
            InsertPair(conn, tx, materialId, eq);
            InsertPair(conn, tx, eq, materialId);
        }
        tx.Commit();
    }

    /// <summary>Muadil grubunu döngü-güvenli BFS ile çözer (transitive; visited set ile sonsuz döngü yok).</summary>
    /// <param name="companyId">TNT-02 (2026-08-25) — verilirse gruba YALNIZ bu firmanın malzemeleri alınır.
    /// <c>null</c> = eski davranış (filtre yok); mevcut çağıranlar aynen çalışır.</param>
    public IReadOnlyCollection<string> GetEquivalentGroup(string materialId, string? companyId = null)
    {
        using var conn = _factory.Create();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(materialId);
        visited.Add(materialId);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var n in DirectEquivalents(conn, cur, companyId))
                if (visited.Add(n)) queue.Enqueue(n);
        }
        visited.Remove(materialId);
        return visited;
    }

    // ---- Uyumlu araç (çoklu seçim) ----
    public void SetCompatibleVehicles(SessionContext s, string materialId, IEnumerable<string> vehicleIds)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureOwned(conn, tx, s.CompanyId, materialId);
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM material_compatible_vehicles WHERE material_id=@m;";
            del.AddWithValue("@m", materialId);
            del.ExecuteNonQuery();
        }
        foreach (var v in vehicleIds.Distinct())
        {
            // İŞ C (2026-08-09) — FİRMA İZOLASYONU: eskiden YALNIZ malzemenin sahipliği doğrulanıyordu;
            // araç hiç kontrol edilmiyordu. API üç uçta bu listeyi doğrudan geçirdiği için, A firmasının
            // yetkili kullanıcısı B firmasından bir araç id'si biliyorsa ilişkiyi yazabiliyordu.
            // Paket 1'de Y-2 için kullanılan guard'ın AYNISI (yeni mekanizma değil).
            // Tek transaction: bir araç bile yabancıysa TAMAMI geri alınır (yarım yazma yok).
            EnsureVehicleOwned(conn, tx, s.CompanyId, v);

            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO material_compatible_vehicles(material_id, vehicle_id) VALUES(@m,@v) ON CONFLICT DO NOTHING;";
            ins.AddWithValue("@m", materialId);
            ins.AddWithValue("@v", v);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Bir aracın uyumlu malzemelerini GÜNCEL STOĞUYLA döndürür (araç detayı stok gösterimi).</summary>
    public IReadOnlyList<MaterialStock> MaterialsForVehicle(SessionContext s, string vehicleId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        // STK-02: bakiye artık (malzeme + lokasyon) anahtarlı. Doğrudan JOIN malzeme satırlarını
        // ÇOĞALTIRDI (aracın uyumlu malzemesi her depo için tekrar listelenirdi). Amaç FİRMA GENELİ
        // stok olduğu için lokasyonlar toplayan alt sorguda tek satıra indirilir.
        cmd.CommandText = $@"
SELECT m.id, m.code, m.name, COALESCE(b.quantity,'0')
FROM material_compatible_vehicles mcv
JOIN materials m ON m.id = mcv.material_id AND m.company_id = @c AND m.is_deleted = 0
LEFT JOIN {SqlDialect.StockTotalSubquery(conn)} b ON b.material_id = m.id AND b.company_id = m.company_id
WHERE mcv.vehicle_id = @v;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@v", vehicleId);
        var list = new List<MaterialStock>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new MaterialStock(r.GetString(0), r.GetString(1), r.GetString(2), Money.Parse(r.GetString(3))));
        return list;
    }

    /// <summary>Malzeme detayı (salt okuma) — tüm alanlar + lookup adları + muadiller + uyumlu araçlar + stok.</summary>
    public MaterialDetail GetDetail(SessionContext s, string materialId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();

        string? code, name, type, catId, unitId, brandId, supId, catName, unitName, brandName, supName, desc, cur;
        decimal minStock, unitPrice, stock;
        long version;
        string? tplId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT m.code, m.name, m.type, m.category_id, m.unit_id, m.brand_id, m.supplier_id,
       mc.name, u.name, b.name, sup.name,
       m.min_stock, m.unit_price, m.currency_code, m.description, COALESCE(sb.quantity,'0'), m.version, m.template_id
FROM materials m
LEFT JOIN material_categories mc ON mc.id = m.category_id
LEFT JOIN units u   ON u.id = m.unit_id
LEFT JOIN brands b  ON b.id = m.brand_id
LEFT JOIN suppliers sup ON sup.id = m.supplier_id
-- STK-02: bakiye depo bazlı → düz JOIN detay satırını depo sayısı kadar çoğaltırdı (ilk satır okunur,
-- kart YANLIŞ stok gösterirdi). Malzeme kartı firma-geneli olduğu için lokasyonlar toplanır.
LEFT JOIN " + SqlDialect.StockTotalSubquery(conn) + @" sb ON sb.material_id = m.id AND sb.company_id = m.company_id
WHERE m.id=@id AND m.company_id=@c AND m.is_deleted=0;";
            cmd.AddWithValue("@id", materialId);
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new ForbiddenException("Malzeme bulunamadı veya başka firmaya ait.");
            code = r.GetString(0); name = r.GetString(1);
            type = r.IsDBNull(2) ? null : r.GetString(2);
            catId = r.IsDBNull(3) ? null : r.GetString(3);
            unitId = r.IsDBNull(4) ? null : r.GetString(4);
            brandId = r.IsDBNull(5) ? null : r.GetString(5);
            supId = r.IsDBNull(6) ? null : r.GetString(6);
            catName = r.IsDBNull(7) ? null : r.GetString(7);
            unitName = r.IsDBNull(8) ? null : r.GetString(8);
            brandName = r.IsDBNull(9) ? null : r.GetString(9);
            supName = r.IsDBNull(10) ? null : r.GetString(10);
            minStock = Money.Parse(r.GetString(11));
            unitPrice = Money.Parse(r.GetString(12));
            cur = r.GetString(13);
            desc = r.IsDBNull(14) ? null : r.GetString(14);
            stock = Money.Parse(r.GetString(15));
            version = r.GetInt64(16); // düzenleme kilidi
            tplId = r.IsDBNull(17) ? null : r.GetString(17);
        }

        // Muadiller (grup ids → kod/ad)
        // ⭐ TNT-02/TNT-03 (denetim 2026-08-25) — SAVUNMA KATMANI: firma filtresi HEM grubu kurarken
        // HEM de kod/ad okunurken uygulanır. Eskiden ikisi de firmasızdı; veritabanında firma ötesi bir
        // muadil satırı bulunduğunda (eski veri ya da manipüle edilmiş senkron paketi) karşı firmanın
        // malzeme KODU ve ADI bu kartta görünüyordu. Aynı savunma malzeme LİSTESİ sorgusunda zaten vardı
        // (bkz. GridInnerSql "SAVUNMA KATMANI" notu) — kart bu savunmadan yoksundu.
        var equivIds = GetEquivalentGroup(materialId, s.CompanyId);
        var equivalents = new List<MaterialRefRow>();
        foreach (var eid in equivIds)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, code, name FROM materials WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@id", eid);
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            if (r.Read()) equivalents.Add(new MaterialRefRow(r.GetString(0), r.GetString(1), r.GetString(2)));
        }

        // Uyumlu araçlar
        var vehicles = new List<MaterialRefRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT v.id, v.internal_code, COALESCE(v.plate,'')
FROM material_compatible_vehicles mcv JOIN vehicles v ON v.id = mcv.vehicle_id
WHERE mcv.material_id=@m AND v.company_id=@c AND v.is_deleted=0 ORDER BY v.internal_code;";
            cmd.AddWithValue("@m", materialId);
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) vehicles.Add(new MaterialRefRow(r.GetString(0), r.GetString(1), r.GetString(2)));
        }

        return new MaterialDetail(materialId, code!, name!, type, catId, unitId, brandId, supId,
            catName, unitName, brandName, supName,
            minStock, unitPrice, cur!, desc, stock, equivalents, vehicles, version, tplId);
    }

    /// <summary>Malzeme alanlarını günceller (kod benzersiz; FK alanları). Stok bu serviste değişmez.</summary>
    /// <param name="expectedVersion">DÜZENLEME KİLİDİ: formun açıldığı andaki <c>version</c>. Verilirse ve kayıt
    /// o andan beri değiştiyse <see cref="ConcurrencyException"/> atılır (sessiz üzerine yazma olmaz).
    /// <c>null</c> = kontrol yok (eski davranış; henüz sürüm taşımayan çağrılar için geriye uyumluluk).</param>
    public void Update(SessionContext s, string materialId, UpdateMaterial dto, long? expectedVersion = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(dto.Code)) throw new ArgumentException("Kod zorunlu.");
        if (dto.MinStock < 0) throw new ArgumentException("Minimum stok negatif olamaz.");   // ADR-086 (bkz. Create)
        if (dto.UnitPrice < 0) throw new ArgumentException("Birim fiyat negatif olamaz.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        if (CodeExists(conn, tx, s.CompanyId, dto.Code, excludeId: materialId))
            throw new InvalidOperationException($"Bu kod zaten kullanılıyor: {dto.Code}");

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE materials SET code=@code, name=@name, type=@type, category_id=@cat, unit_id=@unit,
    brand_id=@brand, supplier_id=@sup, min_stock=@min, unit_price=@price, description=@desc, template_id=@tpl,
    version=version+1, updated_at=@now
WHERE id=@id AND company_id=@c AND is_deleted=0" + EditLockGuard.Clause(expectedVersion) + ";";
            EditLockGuard.Bind(cmd, expectedVersion);
            cmd.AddWithValue("@code", dto.Code.Trim());
            cmd.AddWithValue("@name", dto.Name);
            cmd.AddWithValue("@type", (object?)DepoWise.Application.Ui.MaterialType.Normalize(dto.Type) ?? DBNull.Value);
            cmd.AddWithValue("@cat", (object?)dto.CategoryId ?? DBNull.Value);
            cmd.AddWithValue("@unit", (object?)dto.UnitId ?? DBNull.Value);
            cmd.AddWithValue("@brand", (object?)dto.BrandId ?? DBNull.Value);
            cmd.AddWithValue("@sup", (object?)dto.SupplierId ?? DBNull.Value);
            cmd.AddWithValue("@min", Money.Serialize(dto.MinStock));
            cmd.AddWithValue("@price", Money.Serialize(dto.UnitPrice));
            cmd.AddWithValue("@desc", (object?)dto.Description ?? DBNull.Value);
            cmd.AddWithValue("@tpl", (object?)dto.TemplateId ?? DBNull.Value);   // düzenlemede şablona bağla/koru
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", materialId);
            cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0)
            {
                // 0 satır: ya kayıt yok/başka firmaya ait, YA DA sürüm tutmadı (düzenleme kilidi).
                EditLockGuard.ThrowIfStale(conn, tx, "materials", materialId, s.CompanyId, expectedVersion);
                throw new ForbiddenException("Malzeme bulunamadı veya başka firmaya ait.");
            }
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "material", materialId, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Malzeme soft-delete (is_deleted=1).</summary>
    /// <summary>
    /// MLZ-01 — Silme öncesi KULLANIM KORUMASI (2026-08-10).
    ///
    /// Sorun: silme yalnız yetki ve firma kontrolü yapıyordu. Malzeme kataloğu FİRMA-GENELİ ortak liste
    /// olduğu için (bkz. List() yorumu, kullanıcı kararı 2026-07-26), silme yetkisi olan HERHANGİ bir
    /// şubedeki kullanıcı, tüm firmanın kullandığı bir malzemeyi listeden düşürebiliyordu. Kayıt soft-delete
    /// olduğu için geri alınabiliyordu ama operasyon o an duruyordu.
    ///
    /// Kural: malzeme HİÇ kullanılmamışsa silinebilir; stoğu varsa veya operasyonel geçmişi varsa SİLİNEMEZ.
    /// İlişki/tanım bağları (muadil, uyumlu araç, araç şablonu satırı) silmeyi ENGELLEMEZ — bunlar geçmiş
    /// değil, düzenlenebilir bağlardır.
    ///
    /// Bu metot Delete()'in transaction'ı İÇİNDE çalışır: kontrol ile güncelleme arasında araya kayıt girmesi
    /// zorlaşır. Silme zaten soft-delete ve geri alınabilir olduğundan bundan daha ağır bir kilit gerekmez.
    ///
    /// Şube notu: kontroller BİLİNÇLİ olarak firma genelindedir. Şube bazlı stok mimarisi geldiğinde
    /// (plan: STK-01…STK-06) bu kontrol "hiçbir şubede stok yoksa" haline gelecek; bugünkü davranış
    /// zaten o kuralın firma-geneli hâlidir, yani ileride ÇELİŞMEZ.
    /// </summary>
    private static void GuardDeletable(DbConnection conn, DbTransaction tx, string companyId, string materialId)
    {
        // Malzemenin kendisi (mesajda kullanmak + firma doğrulaması için)
        string code = "", name = "";
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT code, name FROM materials WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@id", materialId);
            cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return;   // yok/başka firmaya ait → mevcut UPDATE zaten 0 satır etkileyip hata verecek
            code = r.IsDBNull(0) ? "" : r.GetString(0);
            name = r.IsDBNull(1) ? "" : r.GetString(1);
        }

        var reasons = new List<string>();

        // 1) Stok bakiyesi — sıfırdan farklıysa silinemez (fiziksel olarak elde olan mal silinemez).
        // STK-02: bakiye artık depo bazlı. Eski tek satır okuması yalnız İLK deponun bakiyesini
        // görürdü → başka depoda malı olan malzeme "stoğu yok" sanılıp silinebilirdi. ReadTotal
        // firmanın TÜM depolarını C# decimal ile toplar (SQL SUM float'a düşerdi).
        decimal qty = StockBalanceWriter.ReadTotal(conn, tx, companyId, materialId);
        if (qty != 0m)
            reasons.Add($"stokta {qty.ToString("0.####", TrCulture)} birim var");

        // 2) Operasyonel geçmiş. Tablo yoksa (eski şema) sessizce atlanır — kontrol uygulamayı KIRMAZ.
        var used = new (string Table, string Label)[]
        {
            ("stock_movements",       "stok hareketi"),
            ("maintenance_materials", "bakım kaydı"),
            ("material_request_items","talep kalemi"),
            ("stock_count_lines",     "sayım satırı"),
        };
        foreach (var (table, label) in used)
        {
            var n = CountByMaterial(conn, tx, table, materialId);
            if (n > 0) reasons.Add($"{n} {label}");
        }

        if (reasons.Count == 0) return;   // hiç kullanılmamış → silinebilir

        var title = string.IsNullOrWhiteSpace(code) ? name : $"{code} - {name}";
        // Yalnız stok varsa yapılacak iş nettir → farklı yönlendirme ver.
        var tail = reasons.Count == 1 && qty != 0m
            ? "Önce stok çıkışı yapın."
            : "Geçmiş kayıtların bozulmaması için kullanılmış malzemeler silinemez.";
        throw new InvalidOperationException($"'{title}' silinemez: {string.Join(", ", reasons)}. {tail}");
    }

    private static readonly CultureInfo TrCulture = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Verilen tabloda bu malzemeye ait satır sayısı. Tablo yoksa 0 döner (eski şemada kırılmasın).
    /// material_id genel-benzersiz olduğundan firma süzmesi GEREKMEZ — malzeme zaten firma doğrulamasından geçti.</summary>
    private static long CountByMaterial(DbConnection conn, DbTransaction tx, string table, string materialId)
    {
        if (!DbIntrospect.TableExists(conn, tx, table)) return 0;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE material_id=@m;";
        cmd.AddWithValue("@m", materialId);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    public void Delete(SessionContext s, string materialId)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        // MLZ-01: kullanılmış malzeme silinemez. AYNI transaction içinde kontrol edilir.
        GuardDeletable(conn, tx, s.CompanyId, materialId);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE materials SET is_deleted=1, version=version+1, updated_at=@now WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", materialId);
            cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Malzeme bulunamadı veya başka firmaya ait.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "material", materialId, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    // ---- Liste (arama + keyset) ----
    public PagedResult<MaterialRecord> List(SessionContext s, PageRequest page, string? search = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var limit = page.NormalizedLimit();
        var hasCursor = Cursor.TryDecode(page.Cursor, out var cursor);
        bool hasSearch = !string.IsNullOrWhiteSpace(search);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        // Malzeme listesi FİRMA-GENELİdir (ortak katalog) — tüm şubelerde aynı görünür. Şube ayrımı STOK'tadır,
        // malzeme kaydında değil (kullanıcı kararı 2026-07-26: "ortak liste + şube-bazlı stok").
        cmd.CommandText =
            "SELECT id, company_id, code, name, type, min_stock, unit_price, currency_code, created_at FROM materials " +
            "WHERE company_id = @c AND is_deleted = 0 " +
            (hasSearch ? $"AND ({SqlDialect.LikeTr(conn, "code", "@q")} OR {SqlDialect.LikeTr(conn, "name", "@q")}) " : "") +
            (hasCursor ? "AND " + TenantSql.KeysetAfterPredicate + " " : "") +
            TenantSql.KeysetOrderBy + " LIMIT @limit;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@limit", limit + 1);
        if (hasSearch) cmd.AddWithValue("@q", "%" + search!.Trim() + "%");
        if (hasCursor)
        {
            cmd.AddWithValue("@cursorCreatedAt", cursor.CreatedAt);
            cmd.AddWithValue("@cursorId", cursor.Id);
        }

        var items = new List<MaterialRecord>();
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                items.Add(new MaterialRecord(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4), Money.Parse(r.GetString(5)), Money.Parse(r.GetString(6)),
                    r.GetString(7), r.GetInt64(8)));
        }
        string? next = null;
        if (items.Count > limit)
        {
            var last = items[limit - 1];
            items.RemoveAt(items.Count - 1);
            next = new Cursor(last.CreatedAt, last.Id).Encode();
        }
        return PagedResult<MaterialRecord>.Of(items, next);
    }

    private const string GridInnerSql = @"
SELECT m.id AS id, m.code AS code, m.name AS name, COALESCE(m.type,'') AS type,
       COALESCE(mc.name,'') AS category, COALESCE(u.name,'') AS unit, COALESCE(b.name,'') AS brand,
       COALESCE(sup.name,'') AS supplier,
       m.unit_price AS unit_price_raw, printf('%.2f', CAST(m.unit_price AS REAL)) AS unit_price_text,
       m.currency_code AS currency,
       m.min_stock AS min_stock_raw, printf('%.2f', CAST(m.min_stock AS REAL)) AS min_stock_text,
       COALESCE(sb.quantity,'0') AS stock_raw, printf('%.2f', CAST(COALESCE(sb.quantity,'0') AS REAL)) AS stock_text,
       CASE WHEN CAST(COALESCE(sb.quantity,'0') AS REAL) <= 0 THEN 'Stok Yok'
            WHEN CAST(COALESCE(sb.quantity,'0') AS REAL) <= CAST(m.min_stock AS REAL) THEN 'Düşük Stok'
            ELSE 'Yeterli' END AS status,
       COALESCE(m.description,'') AS description,
       -- İŞ C (2026-08-09) — SAVUNMA KATMANI: alt sorgularda araç/malzeme tarafına da FİRMA filtresi.
       -- Yazma tarafı artık sahipliği doğruluyor (EnsureVehicleOwned / EnsureOwned), ama eski sürümde
       -- yazılmış ya da elle oluşmuş bozuk bir ilişki varsa BAŞKA firmanın araç iç kodu / malzeme kodu
       -- bu kolonlarda görünüyordu. Filtre, dış sorgunun firmasıyla eşleşmeyi zorunlu kılar.
       COALESCE((SELECT GROUP_CONCAT(v.internal_code, ', ') FROM material_compatible_vehicles mcv
                 JOIN vehicles v ON v.id = mcv.vehicle_id AND v.company_id = m.company_id
                 WHERE mcv.material_id = m.id), '') AS compatible_vehicles,
       COALESCE((SELECT GROUP_CONCAT(m2.code, ', ') FROM material_equivalents me
                 JOIN materials m2 ON m2.id = me.equivalent_material_id AND m2.company_id = m.company_id
                 WHERE me.material_id = m.id), '') AS equivalents
FROM materials m
LEFT JOIN material_categories mc ON mc.id = m.category_id
LEFT JOIN units u ON u.id = m.unit_id
LEFT JOIN brands b ON b.id = m.brand_id
LEFT JOIN suppliers sup ON sup.id = m.supplier_id
LEFT JOIN {STOCK_TOTALS} sb ON sb.material_id = m.id AND sb.company_id = m.company_id
WHERE m.company_id = @c AND m.is_deleted = 0";

    /// <summary>
    /// ⭐ RPR-W1 (web v4 özet şeridi, 2026-08-27) — GRID SORGUSUNUN ORTAK KURULUMU.
    ///
    /// <b>Neden ayrı metot:</b> liste, Excel dışa aktarımı ve YENİ özet şeridi <b>aynı satır kümesini</b>
    /// görmek zorundadır. Filtre eşlemesi ikinci bir yere kopyalanırsa üçü zamanla ayrışır ve kullanıcı
    /// listede gördüğü kayıt sayısıyla özetteki sayının tutmadığını yaşar. Gövde <see cref="SearchGrid"/>
    /// içinden AYNEN taşındı; davranış değişmedi.
    /// </summary>
    private (string Inner, string Where, string Order, List<(string Name, object Value)> Ps) GridSorgusu(
        System.Data.Common.DbConnection conn, MaterialGridFilter filter, string? sortColumn, bool sortDesc, bool criticalOnly)
    {
        // Kolon anahtarı → (alias/tür/ham). Hem filtre hem sıralama BUNDAN çözülür (madde 5 sıralama).
        var byKey = new (string Key, GridQuery.ColumnFilter Col)[]
        {
            (MaterialListColumns.Code, new GridQuery.ColumnFilter("t.code", filter.Code)),
            (MaterialListColumns.Name, new GridQuery.ColumnFilter("t.name", filter.Name)),
            (MaterialListColumns.Type, new GridQuery.ColumnFilter("t.type", filter.Type)),
            (MaterialListColumns.Category, new GridQuery.ColumnFilter("t.category", filter.Category)),
            (MaterialListColumns.Unit, new GridQuery.ColumnFilter("t.unit", filter.Unit)),
            (MaterialListColumns.Brand, new GridQuery.ColumnFilter("t.brand", filter.Brand)),
            (MaterialListColumns.Supplier, new GridQuery.ColumnFilter("t.supplier", filter.Supplier)),
            (MaterialListColumns.UnitPrice, new GridQuery.ColumnFilter("t.unit_price_text", filter.UnitPrice, GridQuery.ColumnKind.Numeric, "t.unit_price_raw")),
            (MaterialListColumns.Currency, new GridQuery.ColumnFilter("t.currency", filter.Currency)),
            (MaterialListColumns.MinStock, new GridQuery.ColumnFilter("t.min_stock_text", filter.MinStock, GridQuery.ColumnKind.Numeric, "t.min_stock_raw")),
            (MaterialListColumns.Stock, new GridQuery.ColumnFilter("t.stock_text", filter.Stock, GridQuery.ColumnKind.Numeric, "t.stock_raw")),
            (MaterialListColumns.Status, new GridQuery.ColumnFilter("t.status", filter.Status)),
            (MaterialListColumns.Description, new GridQuery.ColumnFilter("t.description", filter.Description)),
            (MaterialListColumns.CompatibleVehicles, new GridQuery.ColumnFilter("t.compatible_vehicles", filter.CompatibleVehicles)),
            (MaterialListColumns.Equivalents, new GridQuery.ColumnFilter("t.equivalents", filter.Equivalents)),
        };
        var cols = System.Array.ConvertAll(byKey, x => x.Col);
        GridQuery.ColumnFilter? sort = null;
        if (sortColumn is not null)
            foreach (var x in byKey) if (x.Key == sortColumn) { sort = x.Col; break; }
        // (bağlantı çağırandan gelir — SearchGrid ve özet aynı bağlantıyı paylaşır)
        var (whereSql, orderSql, ps) = GridQuery.Build(cols, "t.code", sort, sortDesc, SqlDialect.IsSqlite(conn));
        // Malzeme listesi FİRMA-GENELİdir (ortak katalog) — şubeye göre filtrelenmez. Ayrım STOK'tadır
        // (kullanıcı kararı 2026-07-26: "ortak liste + şube-bazlı stok").
        var inner = SqlDialect.PortableSql(conn, GridInnerSql);
        // A1 (Aurora): "Yalnız kritik" — stok <= min stok (min stok > 0). Düşük-stok tanımıyla birebir
        // (bkz. DashboardService.LowStock). Varsayılan false → davranış eskisi gibi. Inner WHERE'e eklenir
        // (hem sayım hem liste hem export SearchGridAll aynı metottan geçtiği için tek yer).
        if (criticalOnly)
            inner += " AND CAST(COALESCE(sb.quantity,'0') AS REAL) <= CAST(m.min_stock AS REAL) AND CAST(m.min_stock AS REAL) > 0";
        return (inner, whereSql, orderSql, ps);
    }
    /// <summary>Kolon bazlı filtre + numaralı sayfalama (kullanıcı isteği 2026-07-17). Her filtre alanı
    /// "içerir" araması yapar; birden çok filtre aktifken sıralama, doldurulan alanların
    /// <see cref="MaterialListColumns.All"/>'daki SIRASINA göre "başlangıca göre" önceliklidir (GridQuery).</summary>
    public GridResult<MaterialGridRow> SearchGrid(SessionContext s, MaterialGridFilter filter, int page, int pageSize,
        string? sortColumn = null, bool sortDesc = false, bool criticalOnly = false)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 1 : (pageSize > 500 ? 500 : pageSize);

        using var conn = _factory.Create();
        var (inner, whereSql, orderSql, ps) = GridSorgusu(conn, filter, sortColumn, sortDesc, criticalOnly);

        int total;
        using (var cnt = conn.CreateCommand())
        {
            cnt.CommandText = $"SELECT COUNT(*) FROM ({inner}) t {whereSql};";
            cnt.AddWithValue("@c", s.CompanyId);
            GridQuery.AddParams(cnt, ps);
            total = Convert.ToInt32(cnt.ExecuteScalar());
        }

        var items = new List<MaterialGridRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT * FROM ({inner}) t {whereSql}{orderSql}LIMIT @lim OFFSET @off;";
            cmd.AddWithValue("@c", s.CompanyId);
            GridQuery.AddParams(cmd, ps);
            cmd.AddWithValue("@lim", pageSize);
            cmd.AddWithValue("@off", (page - 1) * pageSize);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                items.Add(new MaterialGridRow(
                    r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                    r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7),
                    Money.Parse(r.GetString(8)), r.GetString(10),
                    Money.Parse(r.GetString(11)), Money.Parse(r.GetString(13)),
                    r.GetString(15), r.GetString(16), r.GetString(17), r.GetString(18)));
        }
        return new GridResult<MaterialGridRow>(items, total, page, pageSize);
    }

    /// <summary>
    /// ⭐ RPR-W1 (web v4, 2026-08-27) — MALZEME LİSTESİ ÖZET ŞERİDİ.
    ///
    /// Listenin üstündeki dört kutunun verisi: kritik seviye altındaki kalem sayısı, ayrık kategori
    /// sayısı ve stok değeri. <b>Sayfalamadan bağımsız, AKTİF FİLTRELERLE tutarlıdır</b> — aynı
    /// <see cref="GridSorgusu"/> kurulumunu kullandığı için listede görünen küme ile birebir aynıdır.
    ///
    /// <b>Toplama neden SQL'de değil:</b> tutarlar TEXT içinde decimal saklanır; <c>SUM(CAST(... AS REAL))</c>
    /// kayan nokta hatası üretir ve projenin para kuralı bunu yasaklar (bkz. <c>StockBalanceWriter</c>,
    /// <c>StockService.RecomputeBalances</c> — aynı desen). Tek sorgu, üç kolon, toplama C#'ta decimal ile.
    ///
    /// Yetki: <see cref="GridSorgusu"/> çağrısından ÖNCE listedeki ile aynı kapı uygulanır; ek yetki YOK
    /// (gridi açabilen özeti de görür). Eski masaüstü istemcisi bu metodu HİÇ çağırmaz.
    /// </summary>
    public MaterialGridSummary SearchGridSummary(SessionContext s, MaterialGridFilter filter, bool criticalOnly = false)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        var (inner, whereSql, _, ps) = GridSorgusu(conn, filter, null, false, criticalOnly);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT t.category, t.stock_raw, t.min_stock_raw, t.unit_price_raw FROM ({inner}) t {whereSql};";
        cmd.AddWithValue("@c", s.CompanyId);
        GridQuery.AddParams(cmd, ps);

        int kritik = 0;
        decimal deger = 0m;
        var kategoriler = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var kategori = r.IsDBNull(0) ? "" : r.GetString(0);
                if (kategori.Length > 0) kategoriler.Add(kategori);

                var stok = Money.Parse(r.IsDBNull(1) ? null : r.GetString(1));
                var min = Money.Parse(r.IsDBNull(2) ? null : r.GetString(2));
                var fiyat = Money.Parse(r.IsDBNull(3) ? null : r.GetString(3));

                // "Kritik" tanımı ekrandaki "Yalnız kritik" filtresiyle ve DashboardService.LowStock ile AYNI:
                // min stok TANIMLI (>0) ve stok onun altına inmiş. Yeni eşik uydurulmadı.
                if (min > 0m && stok <= min) kritik++;
                deger += stok * fiyat;
            }

        return new MaterialGridSummary(kritik, kategoriler.Count, deger);
    }

    /// <summary>Filtrelenmiş/sıralanmış TÜM sonuçları (sayfalama sınırı YOK) döner — "Excel'e Aktar" butonu
    /// için (kullanıcı isteği 2026-07-19: sayfadaki değil, filtrelenmiş TÜM sonuç kümesi indirilmeli).
    /// SearchGrid'i 500'lük sayfalarla iç döngüde gezer (o metodun 500 sınırı BOZULMAZ).</summary>
    public IReadOnlyList<MaterialGridRow> SearchGridAll(SessionContext s, MaterialGridFilter filter, string? sortColumn = null, bool sortDesc = false, bool criticalOnly = false)
    {
        var all = new List<MaterialGridRow>();
        int page = 1;
        while (true)
        {
            var res = SearchGrid(s, filter, page, 500, sortColumn, sortDesc, criticalOnly);
            all.AddRange(res.Items);
            if (page >= res.TotalPages || res.Items.Count == 0) break;
            page++;
        }
        return all;
    }

    /// <summary>Grid satırlarını Excel tablosuna çevirir — kolon sırası <see cref="MaterialListColumns.All"/>
    /// ile AYNIDIR. Görünür kolon seçimi (kişiye özel) buraya YANSITILMAZ — dışa aktarım DAİMA tam alan seti
    /// verir (kullanıcının o an ekranda gizlediği bir kolon export'ta eksik kalmasın).</summary>
    public static Application.Reports.TableModel ToTableModel(IReadOnlyList<MaterialGridRow> rows)
    {
        var headers = MaterialListColumns.All.Select(c => c.Label).ToList();
        var body = rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.Code, r.Name, r.Type, r.Category, r.Unit, r.Brand, r.Supplier,
            r.UnitPrice, r.Currency, r.MinStock, r.Stock, r.Status, r.Description,
            r.CompatibleVehicles, r.Equivalents,
        }).ToList();
        return new Application.Reports.TableModel("Malzemeler", headers, body);
    }

    private static bool CodeExists(DbConnection conn, DbTransaction? tx, string companyId, string code, string? excludeId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM materials WHERE company_id=@c AND code=@code" +
                          (excludeId is null ? ";" : " AND id<>@ex;");
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@code", code.Trim());
        if (excludeId is not null) cmd.AddWithValue("@ex", excludeId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>Malzeme oturumun firmasına mı ait? <paramref name="tx"/> null olabilir (transaction
    /// dışında da çağrılır — bkz. RemoveEquivalent).</summary>
    private static void EnsureOwned(DbConnection conn, DbTransaction? tx, string companyId, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM materials WHERE id=@id AND company_id=@c;";
        cmd.AddWithValue("@id", materialId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Malzeme bulunamadı veya başka firmaya ait.");
    }

    /// <summary>Araç oturumun firmasına mı ait? (İş C, 2026-08-09 — uyumlu araç ilişkisi için.)
    /// <see cref="Maintenance.MaintenanceDefinitionService"/> içindeki Y-2 guard'ıyla AYNI davranış:
    /// silinmiş araç da reddedilir.</summary>
    private static void EnsureVehicleOwned(DbConnection conn, DbTransaction? tx, string companyId, string vehicleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM vehicles WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", vehicleId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
    }

    private static void InsertPair(DbConnection conn, DbTransaction tx, string a, string b)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO material_equivalents(material_id, equivalent_material_id) VALUES(@a,@b) ON CONFLICT DO NOTHING;";
        cmd.AddWithValue("@a", a);
        cmd.AddWithValue("@b", b);
        cmd.ExecuteNonQuery();
    }

    /// <param name="companyId">TNT-02 — doluysa firma ötesi bağlar grubun DIŞINDA bırakılır (savunma katmanı).</param>
    private static List<string> DirectEquivalents(DbConnection conn, string materialId, string? companyId = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = companyId is null
            ? "SELECT equivalent_material_id FROM material_equivalents WHERE material_id=@m;"
            : "SELECT me.equivalent_material_id FROM material_equivalents me " +
              "JOIN materials m2 ON m2.id = me.equivalent_material_id AND m2.company_id=@c " +
              "WHERE me.material_id=@m;";
        if (companyId is not null) cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@m", materialId);
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }
}
