namespace DepoWise.Application.Security;

/// <summary>
/// ═══ LOG-01 — EKRAN → LOG VARLIK TİPLERİ ═══ (kullanıcı isteği 2026-08-27)
///
/// Her ekranın kendi "kayıt geçmişi" düğmesi vardır ve YALNIZ o ekrana ait değişiklikleri gösterir.
/// Denetim satırları <c>audit_logs.entity_type</c> ile etiketlenir; bu sınıf hangi ekranın hangi
/// etiketleri kapsadığını söyler.
///
/// <b>Eşleme uydurulmadı:</b> aşağıdaki etiketler kodda gerçekten yazılan <c>AuditEntry</c>
/// değerlerinden çıkarıldı (bkz. <c>AuditWriter</c> çağrı yerleri). Tanımlar ekranı, tanım tablolarını
/// doğrudan tablo adıyla loglar (<c>LookupService.Insert</c> → tablo adı) — bu yüzden orada çoğul
/// tablo adları vardır.
///
/// <b>Bilinmeyen modül:</b> boş liste döner → ekran "bu ekran için kayıt geçmişi tanımlı değil" der.
/// Sessizce TÜM logu göstermek YASAK: bir ekranın düğmesi başka ekranın verisini açamaz.
/// </summary>
public static class ScreenAuditMap
{
    private static readonly IReadOnlyDictionary<string, string[]> ByModule =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["materials"]        = new[] { "material", "material_template" },
            ["vehicles"]         = new[] { "vehicle", "vehicle_template" },
            ["stock"]            = new[] { "stock_document", "stock_movement" },
            ["fuel"]             = new[] { "fuel_depot_entry", "fuel_distribution" },
            ["maintenance"]      = new[] { "vehicle_maintenance", "maintenance_definition" },
            ["inspection"]       = new[] { "vehicle_inspection" },
            ["daily_activity"]   = new[] { "daily_activity" },
            ["requests"]         = new[] { "material_request" },
            ["personnel"]        = new[] { "personnel", "personnel_title" },
            ["branches"]         = new[] { "branch", "project" },   // PRJ-01: Projeler ekranı branches modülündedir (PK-C4)
            ["files"]            = new[] { "file_record" },         // EVR-01: Evrak ekranı (belge + fotoğraf izleri)
            ["equipment"]        = new[] { "equipment" },           // EKP-01
            ["users"]            = new[] { "user", "user_permissions", "user_scopes", "user_view_all_branches", "role_permissions" },
            ["parties"]          = new[] { "party", "party_ledger" },
            ["invoices"]         = new[] { "invoices" },
            ["finance"]          = new[] { "finance_accounts", "finance_transactions" },
            ["companies"]        = new[] { "company", "company_permissions", "company_purge", "company_business_reset", "company_local_reset" },
            ["material_templates"] = new[] { "material_template" },
            ["vehicle_templates"]  = new[] { "vehicle_template" },
            // Tanımlar ekranı: LookupService tablo ADIYLA loglar.
            ["definitions"]      = new[] { "units", "brands", "suppliers", "material_categories",
                                           "vehicle_types", "vehicle_categories", "vehicle_models" },
        };

    /// <summary>Modülün log etiketleri. Tanımlı değilse BOŞ dizi (tüm logu açmak YASAK).</summary>
    public static IReadOnlyList<string> EntityTypes(string? moduleKey)
        => moduleKey is not null && ByModule.TryGetValue(moduleKey, out var v) ? v : Array.Empty<string>();

    /// <summary>Bu modülün ekran logu var mı — düğme buna göre gösterilir.</summary>
    public static bool Has(string? moduleKey) => EntityTypes(moduleKey).Count > 0;

    /// <summary>Kayıt geçmişi tanımlı TÜM modüller (test ve yönetim ekranları için).</summary>
    public static IReadOnlyCollection<string> Modules => (IReadOnlyCollection<string>)ByModule.Keys;
}
