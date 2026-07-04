namespace DepoWise.Application.Security;

/// <summary>Yetki işlemleri (modül seviyesi). user_permissions kolonlarıyla eşlenir.</summary>
public enum PermissionAction
{
    View,    // can_view  — menü + liste görünürlüğü
    Create,  // can_create
    Edit,    // can_edit
    Delete,  // can_delete
}

/// <summary>Bilinen rol anahtarları (Migration002 seed). Roller analiz §4.</summary>
public static class RoleKeys
{
    public const string SuperAdmin = "role-super-admin";
    public const string CompanyAdmin = "role-company-admin";
    public const string Manager = "role-manager";       // Yönetici / Onaycı
    public const string Warehouse = "role-warehouse";   // Depo kullanıcısı
    public const string Operation = "role-operation";   // Operasyon kullanıcısı
    public const string ReadOnly = "role-readonly";     // Salt okunur

    public static readonly IReadOnlyList<(string Key, string Name, bool IsSystem)> Seed = new[]
    {
        (SuperAdmin, "Süper Admin", true),
        (CompanyAdmin, "Firma Admini", true),
        (Manager, "Yönetici / Onaycı", true),
        (Warehouse, "Depo Kullanıcısı", true),
        (Operation, "Operasyon Kullanıcısı", true),
        (ReadOnly, "Salt Okunur", true),
    };
}

/// <summary>Modül kataloğu — menü/permission tek doğru kaynağı (web ile eşit anahtarlar).</summary>
public static class AppModules
{
    // Herkese açık (deny-by-default istisnası): Dashboard, Hakkında ve Tema (her kullanıcı tema seçebilir).
    public const string Dashboard = "dashboard";
    public const string About = "about";
    public const string Theme = "theme";

    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        (Dashboard, "Ana Ekran"),
        ("companies", "Firmalar"),
        ("releases", "Güncelleme Yönetimi"),
        ("branches", "Şube / Şantiye"),
        ("users", "Kullanıcılar"),
        ("permissions", "Yetkiler"),
        ("definitions", "Tanımlar"),
        ("settings", "Ayarlar"),
        ("materials", "Malzemeler"),
        ("stock", "Stok İşlemleri"),
        ("vehicles", "Araçlar"),
        ("maintenance", "Bakım"),
        ("inspection", "Muayene / Sigorta"),
        ("fuel", "Yakıt"),
        ("daily_activity", "Günlük Faaliyet"),
        ("requests", "Malzeme Talep"),
        ("personnel", "Personel"),
        ("reports", "Raporlar"),
        ("import_export", "İmport / Export"),
        ("files", "Dosya / Fotoğraf"),
        ("audit", "Sistem Logu / Audit"),
        ("backup", "Yedekleme"),
        ("server_backups", "Sunucu Yedekleri"),
        ("machines", "Makine Yönetimi"),
        ("permission_templates", "Yetki Şablonları"),
        ("sync", "Senkronizasyon"),
    };

    /// <summary>Yetki kontrolünden muaf, herkese görünür modüller.</summary>
    public static bool IsPublic(string moduleKey)
        => moduleKey is Dashboard or About or Theme;

    /// <summary>
    /// Yalnız Süper Admin erişebilir; Firma Admini dahil hiç kimseye ATANAMAZ (admin bypass geçersiz).
    /// Firma Tanım platform sahibinindir; çok-firmalı dağıtımda firma admini başka firmayı yönetemez.
    /// </summary>
    public static bool IsSuperAdminOnly(string moduleKey)
        => moduleKey is "companies" or "releases" or "server_backups" or "machines" or "permission_templates";
}

/// <summary>Modül seviyesi özel buton anahtarları (deny-by-default; açıkça verilmedikçe gizli).</summary>
public static class SpecialButtons
{
    public const string Approve = "btn-approve";          // talep onay
    public const string Reverse = "btn-reverse";          // ters kayıt / iptal
    public const string RestoreTrash = "btn-restore";     // çöp kutusu geri yükle
    public const string ResetDatabase = "btn-reset-db";   // DB sıfırlama
    public const string ChangeCompanyLogo = "btn-logo";   // şirket logosu değiştir
    public const string AddLookup = "btn-add-lookup";     // "+" satır içi tanım ekleme (genel)

    /// <summary>Yetki ağacında gösterilen özel buton kataloğu (tek doğru kaynak; yeni buton eklenince otomatik gelir).</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        (Approve, "Talep Onayla / Reddet"),
        (RestoreTrash, "Çöp Kutusu Geri Yükle"),
        (ResetDatabase, "Veritabanı Sıfırlama"),
        (ChangeCompanyLogo, "Firma Logosu Değiştir"),
        (AddLookup, "\"+\" Satır İçi Ekleme"),
    };
}
