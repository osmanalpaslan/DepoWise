using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Sync;

/// <summary>
/// İş verisi SNAPSHOT senkronu (Faz 2 — güvenli "web görünürlüğü" yolu). Masaüstü kendi firmasının iş
/// tablolarını snapshot olarak sunucuya gönderir; sunucu entity-aware generic upsert eder → web adminleri
/// tüm şube verisini (salt-okunur) görür. DepoWise FTS kullanmaz.
/// ⚠️ SNK-11 (2026-08-11): türetilmiş <c>stock_balances</c> ARTIK TAŞINMAZ — otoriter kaynak
/// <c>stock_movements</c> defteridir, sunucu bakiyeyi push sonrası defterden yeniden hesaplar.
/// Generic upsert: satırın verdiği kolonlar ∩ tablo kolonları; company_id sunucuda
/// zorlanır; updated_at varsa LWW (yalnız daha yeni/eşit yazma uygulanır). FK sırası: ebeveyn tablolar önce.
/// </summary>
public sealed class BusinessSyncService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public BusinessSyncService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Ebeveyn → çocuk sırası (FK güvenliği). Sadece bu tablolar snapshot'a girer / uygulanır.
    /// Önce masaüstünde oluşturulabilen lookup/tanım ebeveynleri (materials/vehicles/maintenance FK'leri çözülsün);
    /// sonra iş kayıtları. NOT: branches PUSH'a dahil DEĞİL (web-otoriteli; kod/şifre taşır) — sunucuda zaten var.</summary>
    public static readonly string[] Tables =
    {
        // ebeveyn lookup/tanımlar (LWW: web daha yeni düzenlediyse ezilmez)
        "units",
        "suppliers",
        "brands",
        "material_categories",
        "vehicle_types",
        "vehicle_categories",
        "vehicle_models",
        "maintenance_definitions",
        "personnel_titles",          // unvan sabit tanımları (personel formundaki liste)
        // iş kayıtları
        "personnel",
        "materials",
        // SNK-11 (2026-08-11): `stock_balances` BU LİSTEDEN ÇIKARILDI — senkronda TAŞINMAZ.
        // NEDEN: bakiye TÜRETİLMİŞ veridir; otoriter kaynak `stock_movements` defteridir. Sunucu
        // push sonrası bakiyeyi zaten defterden yeniden hesaplıyor (Program.cs → RecomputeBalances) ve
        // masaüstü pull'u bakiyeyi zaten HARİÇ tutuyordu (BusinessSyncPullService). Yani paketle taşınan
        // değer HİÇBİR ZAMAN kullanılmıyordu → saf yük. STK-07'de kanıtlandı: kasten bozulmuş bir bakiye
        // senkron sonrası defterin değerine dönüyordu.
        // ⚠️ TABLO KALDIRILMADI: yerel SQLite'ta ve sunucuda aynen duruyor; masaüstü çevrimdışı stok
        // işlemleri ve bakiye görüntüleme bundan ETKİLENMEZ (SNK-11 yalnız senkron paketini ilgilendirir).
        "vehicles",
        "vehicle_maintenances",
        "maintenance_materials",
        "fuel_depot_entries",
        "fuel_distributions",
        "daily_activities",
        "stock_movements",
        "stock_documents",
        "material_requests",
        "material_request_items",
        // G4-1c (2026-08-12): ÖN MUHASEBE — CARİ. Masaüstü ÇEVRİMDIŞI cari açabildiği ve elle hareket
        // girebildiği için bunlar senkronda TAŞINMAK ZORUNDA; aksi halde çevrimdışı girilen cari ve
        // bakiyesi sunucuya HİÇ ulaşmaz (web'de görünmez, başka makineye gitmez).
        // SIRA ÖNEMLİ: parties ÖNCE gider — party_ledger.party_id onu referans alır.
        // ⚠️ Bakiye TAŞINMAZ çünkü SAKLANMIYOR (stock_balances ile aynı gerekçe): cari bakiyesi
        // party_ledger'dan Σ(direction × amount) ile hesaplanır → taşınacak türetilmiş alan YOKTUR.
        "parties",
        "party_ledger",
        // G4-2 (2026-08-12): FATURA. Masaustu cevrimdisi fatura kesebildigi icin senkronda TASINIR.
        // SIRA ONEMLI (yabanci anahtar): vat_rates + invoice_series ONCE (fatura seriye bakar),
        // sonra invoices (parties + branches + materials zaten yukarida), en son invoice_lines.
        // Cift kayit riski YOK: invoices.operation_id uzerinde tekil indeks var; ayni fatura ikinci
        // kez uygulanamaz. Stok ve cari etkisi ayrica kendi tablolariyla (stock_movements/party_ledger)
        // tasindigi icin sunucuda YENIDEN URETILMEZ - iki kez borclanma olmaz.
        "vat_rates",
        "invoice_series",
        "invoices",
        "invoice_lines",
        // G4-3 (2026-08-12): KASA / BANKA. Masaustu cevrimdisi tahsilat/odeme yapabildigi icin
        // senkronda TASINIR - G4-1c'de cari icin kapatilan acigin ayni tekrarlanmasin.
        // SIRA ONEMLI (yabanci anahtar): once hesap tanimlari, sonra hareketler, EN SON fatura
        // kapamalari (invoice_allocations hem invoices'a hem finance_transactions'a baglidir).
        // finance_transactions ayrica parties ve party_ledger'a referans verir - ikisi de YUKARIDA.
        // Cift kayit riski YOK: operation_id uzerinde tekil indeks var; ayni tahsilat ikinci kez
        // uygulanamaz, cari ikinci kez etkilenmez, fatura ikinci kez kapanmaz.
        "finance_accounts",
        "finance_transactions",
        "invoice_allocations",
    };

    /// <summary>Her iş tablosunun ait olduğu yetki modülü (business-push yetki kontrolü için).
    /// Kullanıcı bir tabloyu ancak ilgili modülde Create VEYA Edit yetkisi varsa push edebilir.</summary>
    private static readonly IReadOnlyDictionary<string, string> TableModule = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["units"] = "definitions",
        ["suppliers"] = "definitions",
        ["brands"] = "definitions",
        ["material_categories"] = "definitions",
        ["vehicle_types"] = "definitions",
        ["vehicle_categories"] = "definitions",
        ["vehicle_models"] = "definitions",
        ["maintenance_definitions"] = "maintenance",
        ["personnel_titles"] = "personnel",   // unvan tanımları personel modülüne bağlı
        ["personnel"] = "personnel",
        ["materials"] = "materials",
        // SNK-11: `stock_balances` artık senkronda taşınmıyor → yetki eşlemesi de gereksizdi, kaldırıldı.
        ["stock_movements"] = "stock",
        ["stock_documents"] = "stock",
        ["vehicles"] = "vehicles",
        ["vehicle_maintenances"] = "maintenance",
        ["maintenance_materials"] = "maintenance",
        ["fuel_depot_entries"] = "fuel",
        ["fuel_distributions"] = "fuel",
        ["daily_activities"] = "daily_activity",
        ["material_requests"] = "requests",
        ["material_request_items"] = "requests",
        // G4-1c: cari tabloları "parties" modülüne bağlı → kullanıcı ancak cari Create/Edit yetkisi
        // varsa push edebilir (senkron yolu yetki kapısını ATLAMAZ).
        ["parties"] = "parties",
        ["party_ledger"] = "parties",
        // G4-2: fatura tablolari "invoices" modulune bagli - kullanici ancak fatura Create/Edit
        // yetkisi varsa push edebilir. Seri/KDV kataloglari da fatura yetkisine baglidir.
        ["invoices"] = "invoices",
        ["invoice_lines"] = "invoices",
        ["invoice_series"] = "invoices",
        ["vat_rates"] = "invoices",
        // G4-3: kasa/banka tablolari "finance" modulune bagli - kullanici ancak kasa/banka
        // Create/Edit yetkisi varsa push edebilir. Fatura yetkisi TEK BASINA yetmez.
        ["finance_accounts"] = "finance",
        ["finance_transactions"] = "finance",
        ["invoice_allocations"] = "finance",
    };

    /// <summary>Bir iş tablosunun bağlı olduğu yetki modülü (yoksa null → push YASAK).
    /// Testler ve teşhis için açıktır; karar yine <c>TableModule</c> üzerinden verilir.</summary>
    public static string? ModuleOf(string table)
        => TableModule.TryGetValue(table, out var m) ? m : null;

    /// <summary>Negatif olamayacak sayısal alanlar (tablo bazında). Bozuk/kötü niyetli snapshot bunları
    /// eksi değerle gönderirse satır reddedilir (stok/tutar tutarlılığı).
    ///
    /// ⚠️ stock_balances BİLİNÇLİ olarak listede DEĞİL (ADR-086): açılış stoğu negatif olabildiğinden
    /// türetilmiş BAKİYE de negatif olabilir. Ledger kalkanı hareket düzeyinde korunur — stock_movements.quantity
    /// DAİMA pozitiftir (işaret 'direction' ile taşınır), o yüzden aşağıda kalmaya devam eder. Ayrıca sunucu
    /// her push sonrası bakiyeyi hareketlerden yeniden hesaplar (RecomputeBalances) → otoriteli değer korunur.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> NonNegativeFields = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["stock_movements"] = new[] { "quantity", "qty" },
        ["material_request_items"] = new[] { "quantity", "qty" },
        ["fuel_distributions"] = new[] { "liters", "unit_price", "amount", "total" },
        ["fuel_depot_entries"] = new[] { "liters", "unit_price", "amount", "total" },
        ["materials"] = new[] { "unit_price" },
    };

    /// <summary>Yerel DB'den firmanın iş tablolarını JSON snapshot olarak üretir (masaüstü push / sunucu pull).
    /// machineId: bu cihazın adı (çakışma baseline'ı için sunucuda kullanılır).

    // ═══════════════════════════════════════════════════════════════════════════════════════
    //  G4-3c — SENKRONDA ŞUBE İZOLASYONU (GAP-6, kullanıcı isteği 2026-08-12)
    // ═══════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Şube kapsamının UYGULANACAĞI ön muhasebe tabloları ve hangi kolon üzerinden.
    ///
    /// <b>NEDEN YALNIZ BUNLAR:</b> kullanıcı isteği ön muhasebenin şube bazlı olmasıdır. Stok/araç/
    /// personel gibi mevcut çalışan senkron akışlarına DOKUNULMADI — oraya filtre eklemek bugün
    /// çalışan çok-makineli görünürlüğü sessizce daraltırdı (ayrı bir karar konusudur).
    ///
    /// <b>⚠️ parties BU LİSTEDE YOK — BİLİNÇLİ.</b> Cari KARTI firma genelinde tekildir (şubeye
    /// kopyalanmaz); kart süzülseydi, o carinin izinli şubedeki HAREKETİ sahipsiz kalır ve yabancı
    /// anahtar/görünürlük bozulurdu. Şube izolasyonu <c>party_ledger</c> HAREKETLERİNDE yapılır.
    /// Aynı gerekçeyle <c>materials</c>, <c>vat_rates</c>, <c>invoice_series</c> de süzülmez
    /// (ortak tanım/katalog kayıtlarıdır).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> BranchScopedTables =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["party_ledger"] = "branch_id",
            ["invoices"] = "branch_id",
            ["finance_accounts"] = "branch_id",
            ["finance_transactions"] = "branch_id",
        };

    /// <summary>
    /// Kendi <c>branch_id</c>'si OLMAYAN ama ebeveyni üzerinden kapsanan çocuk tablolar.
    /// (tablo → (ebeveyn tablo, çocuktaki yabancı anahtar kolonu))
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string Parent, string Fk)> BranchScopedChildren =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["invoice_lines"] = ("invoices", "invoice_id"),
            ["invoice_allocations"] = ("invoices", "invoice_id"),
        };

    /// <summary>Bu tablo şube kapsamına tabi mi (doğrudan ya da ebeveyni üzerinden)?</summary>
    public static bool IsBranchScoped(string table)
        => BranchScopedTables.ContainsKey(table) || BranchScopedChildren.ContainsKey(table);

    /// <summary>
    /// Snapshot sorgusuna eklenecek şube koşulu. Kapsam sınırsızsa <c>""</c> döner (mevcut davranış).
    /// Doğrudan kolonu olan tabloda <c>branch_id IN (…) OR IS NULL</c>; çocuk tabloda ebeveynin
    /// şubesine bakan <c>EXISTS</c> alt sorgusu.
    /// </summary>
    private static string BranchWhere(SessionContext? session, string table, IReadOnlyList<string>? eff)
    {
        if (session is null || eff is null) return "";     // kapsam yok → filtre yok (geriye dönük davranış)
        var ps = eff.Count == 0 ? null : string.Join(",", Enumerable.Range(0, eff.Count).Select(i => "@bs" + i));

        if (BranchScopedTables.TryGetValue(table, out var col))
            return ps is null ? $"{col} IS NULL" : $"({col} IN ({ps}) OR {col} IS NULL)";

        if (BranchScopedChildren.TryGetValue(table, out var link))
        {
            var cond = ps is null ? "p.branch_id IS NULL" : $"(p.branch_id IN ({ps}) OR p.branch_id IS NULL)";
            return $"EXISTS (SELECT 1 FROM {link.Parent} p WHERE p.id = {table}.{link.Fk} AND {cond})";
        }
        return "";
    }

    private static void BindBranch(DbCommand cmd, IReadOnlyList<string>? eff)
    {
        if (eff is null) return;
        for (int i = 0; i < eff.Count; i++) cmd.AddWithValue("@bs" + i, eff[i]);
    }

    /// <summary>
    /// PUSH kapısı: gelen satırın şubesi kullanıcının kapsamında mı?
    /// Kapsam dışıysa satır UYGULANMAZ (sessizce atlanır ve sayılır) — kısmi/yetkisiz finansal veri
    /// sunucuya yazılamaz. Manipüle edilmiş <c>branch_id</c> ile de geçilemez.
    /// </summary>
    private static bool RowBranchAllowed(SessionContext? session, string table, JsonElement row)
    {
        if (session is null) return true;
        if (!BranchScopedTables.TryGetValue(table, out var col)) return true;   // çocuk tablolar ebeveynle gelir
        if (!row.TryGetProperty(col, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return true;                                                        // şubesiz (firma geneli) kayıt
        var branchId = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
        return BranchAccess.CanAccess(session, branchId);
    }
    ///
    /// DELTA (kullanıcı bulgusu 2026-07-19: 2508 malzemeli firmada tam snapshot 120sn'yi aşıp zaman aşımına
    /// uğruyordu): <paramref name="sinceVersion"/> > 0 ise YALNIZ updated_at &gt; sinceVersion satırlar alınır
    /// (değişenler). 0 ise tam snapshot (ilk kurulum / manuel tam eşitleme). updated_at kolonu olmayan tabloda
    /// (yoksa) filtre uygulanmaz — tümü alınır. Böylece rutin eşitleme küçük ve hızlıdır.</summary>
    public string BuildSnapshot(string companyId, string? machineId = null, long sinceVersion = 0,
        SessionContext? session = null)
    {
        // ⭐ GAP-6: oturum verilirse ön muhasebe tabloları KULLANICININ İZİNLİ ŞUBELERİYLE süzülür.
        // Yetkisiz şubenin finansal verisi cihaza HİÇ İNMEZ (yanıta bile girmez).
        var eff = session is null ? null : BranchAccess.Effective(session);
        using var conn = _factory.Create();
        var tables = new Dictionary<string, List<Dictionary<string, object?>>>();
        foreach (var table in Tables)
        {
            if (!TableExists(conn, table)) continue;
            var cols = ColumnNames(conn, table);
            var hasCompany = cols.Contains("company_id");
            var stamp = StampColumn(cols);
            var rows = new List<Dictionary<string, object?>>();
            using var cmd = conn.CreateCommand();
            var where = new List<string>();
            if (hasCompany) where.Add("company_id=@c");
            if (sinceVersion > 0 && stamp is not null) where.Add($"{stamp} > @since");
            var branchWhere = BranchWhere(session, table, eff);
            if (branchWhere.Length > 0) where.Add(branchWhere);
            cmd.CommandText = $"SELECT * FROM {table}" + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "") + ";";
            if (hasCompany) cmd.AddWithValue("@c", companyId);
            if (sinceVersion > 0 && stamp is not null) cmd.AddWithValue("@since", sinceVersion);
            if (branchWhere.Length > 0) BindBranch(cmd, eff);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (int i = 0; i < r.FieldCount; i++)
                    row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                rows.Add(row);
            }
            tables[table] = rows;
        }
        return JsonSerializer.Serialize(new { companyId, machineId, tables });
    }

    /// <summary>Firmanın iş verisinin "sürümü": tüm iş tablolarındaki EN BÜYÜK updated_at. Ucuz tek sayı —
    /// istemci, tam snapshot çekmeden önce sunucu sürümü değişti mi diye bakar (sık yoklama + yalnız değişince
    /// aktarım). Kullanıcı isteği 2026-07-19: eşitleme 3 dk yerine anlık, ama sabit tam-snapshot bant israfı
    /// olmasın. (Gerçek delta senkron ayrı iş; bu, ucuz değişiklik-tespitiyle "duyarlı" davranış verir.)</summary>
    public long CompanyVersion(string companyId)
    {
        using var conn = _factory.Create();
        long max = 0;
        foreach (var table in Tables)
        {
            if (!TableExists(conn, table)) continue;
            var cols = ColumnNames(conn, table);
            var stamp = StampColumn(cols);
            if (stamp is null) continue;
            using var cmd = conn.CreateCommand();
            var hasCompany = cols.Contains("company_id");
            cmd.CommandText = hasCompany
                ? $"SELECT MAX({stamp}) FROM {table} WHERE company_id=@c;"
                : $"SELECT MAX({stamp}) FROM {table};";
            if (hasCompany) cmd.AddWithValue("@c", companyId);
            var v = cmd.ExecuteScalar();
            if (v is not null and not DBNull) { var l = Convert.ToInt64(v); if (l > max) max = l; }
        }
        return max;
    }

    /// <summary>
    /// Bir tablonun DEĞİŞİM DAMGASI sütunu: normalde <c>updated_at</c>; yoksa <c>created_at</c>.
    ///
    /// ⚠️ QA bulgusu (2026-07-22): <c>stock_movements</c> (stok hareket defteri — değiştirilemez/append-only
    /// olduğu için bilinçli olarak <c>updated_at</c> taşımaz) damgasız sayılıyordu. İki sonucu vardı:
    /// (1) BuildSnapshot'ta delta filtresi HİÇ uygulanmıyor → her eşitlemede TÜM defter aktarılıyordu
    ///     (defter hiç silinmediği için sürekli büyür → zaman aşımı riski),
    /// (2) CompanyVersion o tabloyu atlıyor → YENİ BİR STOK HAREKETİ firma sürümünü yükseltmiyor →
    ///     karşı makine "değişiklik yok" sanıp çekmiyordu (doğruluk hatası).
    /// created_at'e düşmek her ikisini de çözer: defter satırı hiç güncellenmediği için created_at
    /// tam olarak "bu satır ne zaman değişti" demektir.
    /// </summary>
    private static string? StampColumn(ICollection<string> cols)
        => cols.Contains("updated_at") ? "updated_at" : (cols.Contains("created_at") ? "created_at" : null);

    public sealed record ApplyResult(int Upserted, int Skipped, IReadOnlyList<string> Errors);

    public sealed record ConflictRow(string Id, string EntityType, string EntityId, string Winner,
        string? AdminName, long ServerUpdatedAt, long DeviceUpdatedAt, bool PersonnelSeen, long CreatedAt)
    {
        public string WinnerText => Winner == "device" ? "Personel (masaüstü) kazandı" : "Admin (web) kazandı";
        public string EntityLabel => EntityType switch
        {
            "materials" => "Malzeme", "vehicles" => "Araç", "personnel" => "Personel",
            "material_requests" => "Talep", "vehicle_maintenances" => "Bakım", _ => EntityType,
        };
    }

    /// <summary>Firmanın açık çakışmaları (admin ana ekran listesi için).</summary>
    public IReadOnlyList<ConflictRow> ListConflicts(string companyId, bool onlyOpen = true)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, entity_type, entity_id, winner, admin_name, server_updated_at, device_updated_at, personnel_seen, created_at " +
            "FROM data_conflicts WHERE company_id=@c " + (onlyOpen ? "AND status='open' " : "") +
            "ORDER BY created_at DESC LIMIT 200;";
        cmd.AddWithValue("@c", companyId);
        var list = new List<ConflictRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ConflictRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.GetInt64(5), r.GetInt64(6), r.GetInt64(7) == 1, r.GetInt64(8)));
        return list;
    }

    /// <summary>Personelin (masaüstü) HENÜZ görmediği açık çakışmalar — şube kapsamında.</summary>
    public IReadOnlyList<ConflictRow> ListUnseen(string companyId, string? branchId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, entity_type, entity_id, winner, admin_name, server_updated_at, device_updated_at, personnel_seen, created_at " +
            "FROM data_conflicts WHERE company_id=@c AND status='open' AND personnel_seen=0 " +
            (branchId is null ? "" : "AND (branch_id=@b OR branch_id IS NULL) ") +
            "ORDER BY created_at DESC LIMIT 100;";
        cmd.AddWithValue("@c", companyId);
        if (branchId is not null) cmd.AddWithValue("@b", branchId);
        var list = new List<ConflictRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ConflictRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.GetInt64(5), r.GetInt64(6), r.GetInt64(7) == 1, r.GetInt64(8)));
        return list;
    }

    /// <summary>Personel uyarıları gösterildi → görüldü işaretle (şube kapsamında).</summary>
    public int MarkSeen(string companyId, string? branchId)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE data_conflicts SET personnel_seen=1, updated_at=@n WHERE company_id=@c AND status='open' AND personnel_seen=0 " +
            (branchId is null ? "" : "AND (branch_id=@b OR branch_id IS NULL)") + ";";
        cmd.AddWithValue("@n", now);
        cmd.AddWithValue("@c", companyId);
        if (branchId is not null) cmd.AddWithValue("@b", branchId);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>Admin çakışmayı çözümledi (listeden kaldırır).</summary>
    public void ResolveConflict(string companyId, string conflictId)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE data_conflicts SET status='resolved', updated_at=@n WHERE company_id=@c AND id=@id;";
        cmd.AddWithValue("@n", now);
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@id", conflictId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Sunucu: gelen snapshot'ı firmaya uygular. company_id oturumdan zorlanır (tenant güvenliği).
    /// Her satır kendi try/catch'inde (bir satır hatası diğerlerini bozmaz); FK sırası korunur.</summary>
    /// <summary>Çakışma izlenen (admin+personel ikisinin de düzenleyebildiği) kart/kayıt tabloları.
    /// Sadece bunlarda eşzamanlı düzenleme çakışması aranır (append-only hareketlerde gürültü olmasın).</summary>
    private static readonly HashSet<string> ConflictTracked = new(StringComparer.Ordinal)
    {
        "materials", "vehicles", "personnel", "material_requests", "vehicle_maintenances",
    };

    /// <summary>Yetki-farkında uygulama: oturumun yazma (Create/Edit) yetkisi olmayan modüllerin tabloları
    /// UYGULANMAZ (Y3 — en yetkisiz kullanıcının tüm firma verisini ezmesi engellenir). Admin/SüperAdmin tam yetkili.
    ///
    /// SUNUCU (WEB) SİLMEDE OTORİTER — DİRİLTME YASAK: sunucuda silinmiş (is_deleted=1) bir kayıt, cihazın
    /// push'uyla (is_deleted=0, daha yeni updated_at) GERİ GETİRİLEMEZ. Aksi halde masaüstü giriş sırasında
    /// önce push yaptığı için web'de silinen kayıt sunucuda diriliyor, ardından pull ile makinelere geri yayılıyordu.
    /// Kaydı geri getirmenin tek yolu web'den yeniden aktifleştirmektir.</summary>
    public ApplyResult Apply(SessionContext session, JsonElement payload)
    {
        bool CanWrite(string table)
        {
            if (!TableModule.TryGetValue(table, out var moduleKey)) return false; // eşlenmemiş tabloya izin yok
            return AccessControl.Can(session, moduleKey, PermissionAction.Create)
                || AccessControl.Can(session, moduleKey, PermissionAction.Edit);
        }
        // ⭐ GAP-6 PUSH KAPISI: session verilir → ApplyCore her satırın şubesini de denetler.
        // Kapsam dışı satır UYGULANMAZ; manipüle edilmiş branch_id ile de geçilemez.
        return ApplyCore(session.CompanyId, payload, CanWrite, protectServerDeletes: true, session: session);
    }

    public ApplyResult Apply(string companyId, JsonElement payload)
        => ApplyCore(companyId, payload, null, protectServerDeletes: true);

    /// <summary>GERİ-ÇEKME (server → masaüstü): sunucudan gelen firmanın iş verisini YEREL DB'ye uygular (LWW).
    /// Trusted (sunucu) veri olduğundan yazma-yetkisi filtresi yoktur. <paramref name="excludeTables"/> ile
    /// belirli tablolar atlanır — örn. stock_balances (türetilmiş; sunucu-otoriteli hesaplama 2b'de gelecek).
    ///
    /// SİLMEDE SUNUCU (WEB) TAM OTORİTERDİR: gelen satır <c>is_deleted=1</c> ise LWW koşulu ATLANIR ve silme
    /// yerelde koşulsuz uygulanır. Aksi halde makinede daha yeni bir düzenleme, web'de silinmiş kaydı "diriltiyordu".
    /// (Bu yalnız PULL yönünde geçerlidir; push'ta normal LWW korunur.)</summary>
    public ApplyResult ApplyPull(string companyId, JsonElement payload, ISet<string>? excludeTables = null)
        => ApplyCore(companyId, payload, null, excludeTables, serverAuthoritativeDeletes: true);

    private ApplyResult ApplyCore(string companyId, JsonElement payload, Func<string, bool>? canWriteTable,
        ISet<string>? excludeTables = null, bool serverAuthoritativeDeletes = false, bool protectServerDeletes = false,
        SessionContext? session = null)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("tables", out var tablesEl) ||
            tablesEl.ValueKind != JsonValueKind.Object)
            return new ApplyResult(0, 0, new[] { "Geçersiz snapshot (tables yok)." });

        int upserted = 0, skipped = 0;
        var errors = new List<string>();
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        using var conn = _factory.Create();
        bool isPg = !SqlDialect.IsSqlite(conn);   // PG'de satır hatası TÜM transaction'ı abort eder → savepoint gerekir.

        // ⚠️ PERFORMANS (kullanıcı bulgusu 2026-07-19: 2508 kayıtlı push SUNUCUDA zaman aşımına uğruyordu;
        // araçlar hiç ulaşmıyordu): tüm upsert'ler TEK transaction'da → 2508+ ayrı commit yerine 1 commit
        // (SQLite'ta her commit fsync → binlerce satır dakikalarca sürüyordu). Ham BEGIN/COMMIT — tek bağlantı,
        // alt komutlar (UpsertRow/DetectConflict) bu transaction içinde çalışır. Hata olursa ROLLBACK.
        using (var begin = conn.CreateCommand()) { begin.CommandText = "BEGIN;"; begin.ExecuteNonQuery(); }
        try
        {

        // Cihaz baseline'ı: bu cihazın son iş-verisi push zamanı (çakışma penceresi). machineId payload'da.
        var machineId = payload.TryGetProperty("machineId", out var mEl) && mEl.ValueKind == JsonValueKind.String
            ? mEl.GetString() : null;
        var (deviceId, deviceBranchId, lastPush) = ResolveDevice(conn, companyId, machineId);

        foreach (var table in Tables) // FK-güvenli sıra
        {
            if (excludeTables is not null && excludeTables.Contains(table)) continue; // geri-çekmede hariç (ör. stock_balances)
            if (!tablesEl.TryGetProperty(table, out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Array) continue;
            if (!TableExists(conn, table)) continue;
            // Yetki: kullanıcı bu tablonun modülünde yazamıyorsa tüm tablo atlanır (hata değil, sessiz atla).
            if (canWriteTable is not null && !canWriteTable(table))
            {
                int n = 0; foreach (var _ in rowsEl.EnumerateArray()) n++;
                skipped += n;
                if (errors.Count < 20 && n > 0) errors.Add($"{table}: yetki yok (atlandı).");
                continue;
            }
            var cols = ColumnNames(conn, table);
            var pk = PrimaryKey(conn, table);
            if (pk.Count == 0) continue; // PK yoksa güvenli upsert yapılamaz
            bool hasCompany = cols.Contains("company_id");
            bool hasUpdated = cols.Contains("updated_at");
            bool trackConflict = hasUpdated && ConflictTracked.Contains(table) && pk.Count == 1 && pk[0] == "id";

            var (tUp, tSk) = ApplyTableRows(conn, isPg, table, cols, pk, hasCompany, hasUpdated, trackConflict,
                companyId, rowsEl, now, deviceBranchId, lastPush, serverAuthoritativeDeletes, protectServerDeletes, errors, session);
            upserted += tUp; skipped += tSk;
        }

        // Cihazın son push zamanını ilerlet (bir sonraki çakışma penceresinin başlangıcı)
        if (deviceId is not null) SetLastPush(conn, deviceId, now);

        using (var commit = conn.CreateCommand()) { commit.CommandText = "COMMIT;"; commit.ExecuteNonQuery(); }
        }
        catch
        {
            try { using var rb = conn.CreateCommand(); rb.CommandText = "ROLLBACK;"; rb.ExecuteNonQuery(); } catch { }
            throw;
        }

        return new ApplyResult(upserted, skipped, errors);
    }

    /// <summary>Bir tablonun satırlarını uygular. Döner: (upserted, skipped).
    ///
    /// <b>SQLite (masaüstü/mevcut sunucu):</b> DEĞİŞMEDİ — satır başı try/catch. SQLite hatalı bir statement'tan
    /// sonra aynı transaction'da devam edebildiği için ekstra bir şey gerekmez.
    ///
    /// <b>PostgreSQL:</b> tek bir satır hatası TÜM transaction'ı abort eder (25P02) → sonraki her komut da
    /// patlar. Bu yüzden iki kademeli:
    ///   • HIZLI YOL — tüm tablo TEK savepoint içinde denenir (geçerli veride ekstra maliyet ~yok; normal durum).
    ///   • KURTARMA — bir satır patlarsa tablo o savepoint'e geri alınır ve satırlar TEKRAR, her biri kendi
    ///     savepoint'inde uygulanır → yalnız gerçekten hatalı satır(lar) atlanır, gerisi yazılır. Satır-başı
    ///     savepoint maliyeti YALNIZ hata olan (nadir) tabloda ödenir.</summary>
    private (int Up, int Sk) ApplyTableRows(DbConnection conn, bool isPg, string table, HashSet<string> cols,
        List<string> pk, bool hasCompany, bool hasUpdated, bool trackConflict, string companyId,
        JsonElement rowsEl, long now, string? deviceBranchId, long lastPush,
        bool serverAuth, bool protectDeletes, List<string> errors, SessionContext? session = null)
    {
        int up = 0, sk = 0;

        // Bir satırı uygular (validate → conflict → upsert). DB hatasında FIRLATIR (savepoint/try çağırana ait).
        // Döner: null = geçersiz (atla), true = upserted, false = geçerli ama no-op (atla).
        bool? ApplyOne(JsonElement rowEl, List<string> errSink)
        {
            if (rowEl.ValueKind != JsonValueKind.Object) return null;
            // ⭐ GAP-6 PUSH KAPISI: kapsam dışı şubenin satırı UYGULANMAZ. Cihaz manipüle edilmiş bir
            // branch_id gönderse bile yetkisiz şubeye finansal veri yazılamaz.
            if (!RowBranchAllowed(session, table, rowEl))
            {
                if (errSink.Count < 20) errSink.Add($"{table}: şube kapsam dışı (atlandı).");
                return null;
            }
            var (okRow, reason) = ValidateRow(table, rowEl, companyId);
            if (!okRow) { if (errSink.Count < 20) errSink.Add($"{table}: {reason}"); return null; }
            if (trackConflict) DetectConflict(conn, table, companyId, deviceBranchId, lastPush, rowEl, now);
            return UpsertRow(conn, table, cols, pk, hasCompany, hasUpdated, companyId, rowEl, now, serverAuth, protectDeletes);
        }

        if (!isPg)
        {
            // SQLite — mevcut davranış (satır başı try/catch, savepoint yok).
            foreach (var rowEl in rowsEl.EnumerateArray())
            {
                try { var r = ApplyOne(rowEl, errors); if (r == true) up++; else sk++; }
                catch (Exception ex) { sk++; if (errors.Count < 20) errors.Add($"{table}: {ex.Message}"); }
            }
            return (up, sk);
        }

        // PostgreSQL — HIZLI YOL: tüm tablo tek savepoint.
        ExecRaw(conn, "SAVEPOINT dw_tbl;");
        try
        {
            int fUp = 0, fSk = 0; var fErr = new List<string>();
            foreach (var rowEl in rowsEl.EnumerateArray())
            {
                var r = ApplyOne(rowEl, fErr);   // hatalı satır → fırlatır → catch (kurtarma yoluna geç)
                if (r == true) fUp++; else fSk++;
            }
            ExecRaw(conn, "RELEASE SAVEPOINT dw_tbl;");
            foreach (var e in fErr) { if (errors.Count >= 20) break; errors.Add(e); }
            return (fUp, fSk);
        }
        catch
        {
            ExecRaw(conn, "ROLLBACK TO SAVEPOINT dw_tbl;");   // tabloyu geri al → satır başı tekrar dene
        }

        // PostgreSQL — KURTARMA YOLU: satır başı savepoint (yalnız hatalı tabloda).
        up = 0; sk = 0;
        foreach (var rowEl in rowsEl.EnumerateArray())
        {
            ExecRaw(conn, "SAVEPOINT dw_row;");
            try
            {
                var r = ApplyOne(rowEl, errors);
                ExecRaw(conn, "RELEASE SAVEPOINT dw_row;");
                if (r == true) up++; else sk++;
            }
            catch (Exception ex)
            {
                ExecRaw(conn, "ROLLBACK TO SAVEPOINT dw_row;");
                ExecRaw(conn, "RELEASE SAVEPOINT dw_row;");
                sk++;
                if (errors.Count < 20) errors.Add($"{table}: {ex.Message}");
            }
        }
        ExecRaw(conn, "RELEASE SAVEPOINT dw_tbl;");
        return (up, sk);
    }

    private static void ExecRaw(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static (string? DeviceId, string? BranchId, long LastPush) ResolveDevice(DbConnection conn, string companyId, string? machineId)
    {
        if (string.IsNullOrWhiteSpace(machineId)) return (null, null, 0);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, branch_id, COALESCE(last_business_push_at,0) FROM sync_devices WHERE company_id=@c AND device_name=@n LIMIT 1;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@n", machineId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (null, null, 0);
        return (r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetInt64(2));
    }

    private static void SetLastPush(DbConnection conn, string deviceId, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sync_devices SET last_business_push_at=@n WHERE id=@id;";
        cmd.AddWithValue("@n", now);
        cmd.AddWithValue("@id", deviceId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Sunucudaki kayıt VE gelen kayıt SON push'tan sonra değişmiş + içerik farklıysa → çakışma.
    /// LWW kazananı (device/admin) ve admin kimliği (audit_logs'tan) ile data_conflicts'e yazılır (open, tek kayıt).</summary>
    private void DetectConflict(DbConnection conn, string table, string companyId, string? deviceBranchId,
        long lastPush, JsonElement row, long now)
    {
        if (!row.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String) return;
        var id = idEl.GetString()!;
        long incomingUpdated = ReadLong(row, "updated_at");

        // Sunucudaki mevcut kayıt
        long serverUpdated;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT updated_at FROM {table} WHERE id=@id;";
            cmd.AddWithValue("@id", id);
            var v = cmd.ExecuteScalar();
            if (v is null || v is DBNull) return; // sunucuda yok → yeni kayıt, çakışma değil
            serverUpdated = Convert.ToInt64(v);
        }

        // İkisi de son push'tan sonra değiştiyse ve zaman damgaları farklıysa → eşzamanlı düzenleme
        bool serverChanged = serverUpdated > lastPush;
        bool deviceChanged = incomingUpdated > lastPush;
        if (!serverChanged || !deviceChanged || serverUpdated == incomingUpdated) return;

        var winner = incomingUpdated >= serverUpdated ? "device" : "admin";
        var (adminUserId, adminName) = LastServerEditor(conn, companyId, id);

        // Aynı kayıt için açık çakışma varsa güncelle; yoksa ekle (unique index: company+entity WHERE open)
        using var up = conn.CreateCommand();
        up.CommandText = @"
INSERT INTO data_conflicts(id, company_id, branch_id, entity_type, entity_id, winner, admin_user_id, admin_name,
    server_updated_at, device_updated_at, personnel_seen, status, created_at, updated_at)
VALUES(@id,@c,@b,@et,@eid,@w,@au,@an,@su,@du,0,'open',@now,@now)
ON CONFLICT(company_id, entity_id) WHERE status='open' DO UPDATE SET
    winner=excluded.winner, admin_user_id=excluded.admin_user_id, admin_name=excluded.admin_name,
    server_updated_at=excluded.server_updated_at, device_updated_at=excluded.device_updated_at,
    personnel_seen=0, updated_at=excluded.updated_at;";
        up.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        up.AddWithValue("@c", companyId);
        up.AddWithValue("@b", (object?)deviceBranchId ?? DBNull.Value);
        up.AddWithValue("@et", table);
        up.AddWithValue("@eid", id);
        up.AddWithValue("@w", winner);
        up.AddWithValue("@au", (object?)adminUserId ?? DBNull.Value);
        up.AddWithValue("@an", (object?)adminName ?? DBNull.Value);
        up.AddWithValue("@su", serverUpdated);
        up.AddWithValue("@du", incomingUpdated);
        up.AddWithValue("@now", now);
        up.ExecuteNonQuery();
    }

    private static (string? UserId, string? Name) LastServerEditor(DbConnection conn, string companyId, string entityId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT a.user_id, u.full_name, u.username FROM audit_logs a
LEFT JOIN users u ON u.id = a.user_id
WHERE a.company_id=@c AND a.entity_id=@e ORDER BY a.created_at DESC LIMIT 1;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@e", entityId);
        using var r = cmd.ExecuteReader();
        if (!r.Read() || r.IsDBNull(0)) return (null, null);
        var uid = r.GetString(0);
        var name = !r.IsDBNull(1) ? r.GetString(1) : (!r.IsDBNull(2) ? r.GetString(2) : null);
        return (uid, name);
    }

    private static long ReadLong(JsonElement row, string name) // updated_at okuma yardımcı
    {
        if (row.TryGetProperty(name, out var v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l)) return l;
            if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
        }
        return 0;
    }

    private bool UpsertRow(DbConnection conn, string table, HashSet<string> tableCols, List<string> pk, bool hasCompany,
        bool hasUpdated, string companyId, JsonElement row, long now,
        bool serverAuthoritativeDeletes = false, bool protectServerDeletes = false)
    {
        // Satırın verdiği kolonlar ∩ gerçek tablo kolonları
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in row.EnumerateObject())
            if (tableCols.Contains(prop.Name))
                values[prop.Name] = JsonToDb(prop.Value);

        // PK kolonlarının hepsi gelmeli (aksi halde çakışma hedefi belirsiz)
        foreach (var k in pk)
            if (!values.TryGetValue(k, out var v) || v is null) return false;
        if (hasCompany) values["company_id"] = companyId; // tenant zorla

        var colList = values.Keys.ToList();
        var insertCols = string.Join(", ", colList);
        var insertVals = string.Join(", ", colList.Select(c => "@" + c));
        var conflictTarget = string.Join(", ", pk);
        var updateSet = string.Join(", ", colList.Where(c => !pk.Contains(c)).Select(c => $"{c}=excluded.{c}"));

        bool hasDeleted = tableCols.Contains("is_deleted");

        // (A) PULL — SİLME SUNUCU-OTORİTELİ: gelen satır silinmişse LWW koşulu uygulanmaz; silme her zaman kazanır.
        // Aksi halde makinedeki daha yeni bir düzenleme, web'de silinmiş kaydı yerelde "diriltiyordu".
        bool incomingDeleted = serverAuthoritativeDeletes && hasDeleted
            && values.TryGetValue("is_deleted", out var delVal)
            && delVal is not null && Convert.ToInt64(delVal) == 1;

        // LWW: updated_at varsa yalnız gelen >= mevcut ise güncelle (sunucudan gelen silme hariç — yukarı bak)
        var conds = new List<string>();
        if (hasUpdated && !incomingDeleted) conds.Add($"excluded.updated_at >= {table}.updated_at");

        // (B) PUSH — DİRİLTME YASAK: sunucuda silinmiş kayıt, cihazın "silinmemiş" satırıyla geri getirilemez.
        // (Masaüstü girişte önce push yaptığı için web'de silinen kayıt sunucuda diriliyordu.)
        if (protectServerDeletes && hasDeleted)
            conds.Add($"NOT ({table}.is_deleted = 1 AND excluded.is_deleted = 0)");

        var whereLww = conds.Count > 0 ? " WHERE " + string.Join(" AND ", conds) : "";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = updateSet.Length == 0
            ? $"INSERT INTO {table} ({insertCols}) VALUES ({insertVals}) ON CONFLICT({conflictTarget}) DO NOTHING;"
            : $"INSERT INTO {table} ({insertCols}) VALUES ({insertVals}) ON CONFLICT({conflictTarget}) DO UPDATE SET {updateSet}{whereLww};";
        foreach (var kv in values)
            cmd.AddWithValue("@" + kv.Key, kv.Value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        return true;
    }

    // PostgreSQL geçişi: şema sorgulama lehçe-duyarlı ortak yardımcıya taşındı (SQLite PRAGMA ↔ PG information_schema).
    private static List<string> PrimaryKey(DbConnection conn, string table) => DbIntrospect.PrimaryKey(conn, table);

    /// <summary>Satır içerik doğrulaması: tabloya göre negatif olamayacak sayısal alanlar eksi olamaz
    /// (bozuk/kötü niyetli snapshot stok/tutarı eksiye çekemez). Değer sayı VEYA sayısal string olabilir.
    /// Not: company_id UpsertRow'da oturumdan zorlandığı için burada ayrıca kontrol edilmez (tenant güvenli).</summary>
    private static (bool Ok, string? Reason) ValidateRow(string table, JsonElement row, string companyId)
    {
        if (NonNegativeFields.TryGetValue(table, out var fields))
            foreach (var f in fields)
                if (row.TryGetProperty(f, out var fv) && TryReadNumber(fv, out var d) && d < 0)
                    return (false, $"negatif değer: {f}={d}.");

        return (true, null);
    }

    /// <summary>JSON değeri sayı ya da sayısal string ise double'a çevirir (SQLite TEXT affinity toleransı).</summary>
    private static bool TryReadNumber(JsonElement v, out double d)
    {
        d = 0;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetDouble(out d);
        if (v.ValueKind == JsonValueKind.String)
            return double.TryParse(v.GetString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out d);
        return false;
    }

    private static object? JsonToDb(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => v.GetString(),
        JsonValueKind.True => 1L,
        JsonValueKind.False => 0L,
        JsonValueKind.Number => v.TryGetInt64(out var l) ? l : v.GetDouble(),
        _ => v.ToString(),
    };

    private static bool TableExists(DbConnection conn, string table) => DbIntrospect.TableExists(conn, null, table);

    private static HashSet<string> ColumnNames(DbConnection conn, string table) => DbIntrospect.ColumnNames(conn, table);
}
