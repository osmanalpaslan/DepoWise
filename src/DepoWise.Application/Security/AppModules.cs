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
    public const string RestrictedSuperAdmin = "role-restricted-super-admin"; // Admin ile Süper Admin arası (Migration036)
    public const string CompanyAdmin = "role-company-admin";
    public const string Staff = "role-staff";           // Personel (2-rol modeli, Migration029)

    // Legacy roller (Migration029 ile Personel'e taşındı; yalnız migration referansı için tutulur).
    public const string Manager = "role-manager";
    public const string Warehouse = "role-warehouse";
    public const string Operation = "role-operation";
    public const string ReadOnly = "role-readonly";

    /// <summary>Eski roller — Migration029 bunları Personel'e taşır + soft-delete eder.</summary>
    public static readonly IReadOnlyList<string> Legacy = new[] { Manager, Warehouse, Operation, ReadOnly };

    /// <summary>Aktif rol modeli: Personel + Admin + Kısıtlı Süper Admin + sistemsel Süper Admin.</summary>
    public static readonly IReadOnlyList<(string Key, string Name, bool IsSystem)> Seed = new[]
    {
        (SuperAdmin, "Süper Admin", true),
        (RestrictedSuperAdmin, "Kısıtlı Süper Admin", true),
        (CompanyAdmin, "Admin", true),
        (Staff, "Personel", true),
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
        ("vehicle_templates", "Araç Genel Tanım"),
        ("quota_monitor", "Kota İzleme"),
        ("companies", "Firmalar"),
        ("releases", "Güncelleme Yönetimi"),
        ("branches", "Şube / Şantiye"),
        ("users", "Kullanıcılar"),
        ("permissions", "Yetkiler"),
        ("definitions", "Tanımlar"),
        ("settings", "Ayarlar"),
        ("materials", "Malzemeler"),
        ("material_templates", "Malzeme Şablonları"),
        ("stock", "Stok İşlemleri"),
        ("vehicles", "Araçlar"),
        ("equipment", "Ekipman"),   // EKP-01 (ADR-166): araçtan AYRI varlık/ekipman modülü
        ("assignments", "Zimmet"),   // ZMT-01 (ADR-167): kimde ne var + teslim/iade/devir/kayıp defteri
        ("maintenance", "Bakım"),
        ("inspection", "Muayene / Sigorta"),
        ("fuel", "Yakıt"),
        ("daily_activity", "Günlük Faaliyet"),
        ("requests", "Talep Formu"),
        ("request_approval", "Talep Onaylama"),
        // Talep Operasyonları (kullanıcı isteği 2026-08-08, Faz 1): ekran yetkisi + iki birim yetkisi.
        // Faz 1'de yalnız ağaca eklenir/atanabilir; operasyon ekranı ve adımları Faz 2+'da bunlara bağlanır.
        ("request_ops", "Talep Operasyonları"),
        ("request_ops_warehouse", "Talep Operasyonları — Ana Depo"),
        ("request_ops_purchase", "Talep Operasyonları — Satın Alma"),
        ("personnel", "Personel"),
        ("reports", "Raporlar"),
        ("import_export", "İçe Aktarım (Import)"),   // 2026-07-26: yalnız İÇE AKTARIM
        ("export", "Dışa Aktarım (Export)"),         // 2026-07-26: ayrı DIŞA AKTARIM (liste Excel butonları dahil)
        ("files", "Dosya / Fotoğraf"),
        ("audit", "Sistem Logu / Audit"),
        ("stock_change_log", "Stok Değişiklik Kaydı"),   // madde 1.5: doğrudan stok değişikliği uyarı logu ekranı
        ("backup", "Yedekleme"),
        ("server_backups", "Sunucu Yedekleri"),
        ("machines", "Makine Yönetimi"),
        ("machine_backups", "Makine Yedekleri"),
        ("permission_templates", "Yetki Şablonları"),
        ("role_permissions", "Rol Yetki Kontrol"),
        ("server_status", "Canlı Sunucu Durumu"),
        ("purge_company", "Kalıcı Silme"),          // ADR-083 — geri alınamaz firma silme (web, özel kod ile)
        // ADR-084 / YET (2026-08-18, kullanıcı isteği): "makinelerin yerel verisini sıfırla" isteği artık
        // YETKİ AĞACINDA bir menü maddesidir. Eskiden Firmalar ekranının içinde gömülü bir düğmeydi ve
        // sunucu sert biçimde yalnız süper admine izin veriyordu → hiç kimseye DEVREDİLEMİYORDU.
        // "Açık-verilir" (IsExplicitOnly) katmanındadır: admin bypass YOK, yalnız açıkça verilirse çalışır.
        ("local_reset", "Yerel Veri Sıfırlama"),
        // G5 (2026-08-12): ekranların hangi platformda (masaüstü/web) açık olacağını yönetir.
        // YETKİ DEĞİL, platform kısıtıdır; yalnız süper admin (IsSuperAdminOnly) — dar tutuldu.
        ("screen_visibility", "Menü / Ekran Yönetimi"),
        // G4-1 (2026-08-12): ÖN MUHASEBE — CARİ. Tek modül + dört aksiyon (View/Create/Edit/Delete).
        // Ayrı "party_view/party_create/..." anahtarları AÇILMADI: modül modeli aksiyonu zaten taşıyor.
        ("parties", "Cari Hesaplar"),
        // G4-2 (2026-08-12): ON MUHASEBE - FATURA. Cariden AYRI modul: fatura kesme yetkisi ile cari
        // karti gorme yetkisi ayri verilebilsin (depo gorevlisi fatura kesmez, cari listesi gorebilir).
        // Delete AKSIYONU KULLANILMAZ: fatura fiziksel silinmez, Edit yetkisiyle IPTAL edilir (CLAUDE.md 4).
        ("invoices", "Faturalar"),
        // G4-3 (2026-08-12): ON MUHASEBE - KASA / BANKA. Tek modul + dort aksiyon.
        // Kasa ve banka AYRI modul DEGIL: ayni defter mantigi, ayni ekran, ayni yetki.
        // Delete AKSIYONU KULLANILMAZ: finansal hareket silinmez, Edit yetkisiyle TERS KAYIT yazilir.
        ("finance", "Kasa / Banka"),
        ("cost_centers", "Maliyet Merkezi"),   // MLY-01 (ADR-168)
        ("purchasing", "Satın Alma"),   // STN-01 (ADR-169): sipariş + mal kabul ekranı (talep durum-geçiş yetkisi request_ops_purchase AYRI kalır)
        ("work_orders", "İş Emirleri"),   // EMR-01 (ADR-170)
        ("calendar", "Takvim"),           // TKV-01 (ADR-171)
        ("announcements", "Duyurular"),   // DYR-01 (ADR-173) — okuma herkese (IsPublicRead), yazma bu modülle
        // G2-B1 DÜZELTMESİ (2026-08-12): "Çöp Kutusu" ekranı bu katalogda YOKTU. Masaüstünde menü grubu ve
        // Navigate kaydı, web'de "@admin" sözde-anahtarı vardı; ama yetki ağacında görünmediği için süper
        // admin bu ekranı belirli bir kullanıcıya DEVREDEMİYOR, Rol Yetki Kontrol ile kısıtlayamıyordu.
        // Yalnız admin bypass'ı sayesinde admin'e açıktı (kazara doğru davranış).
        // ⚠️ Ekleme kimseden yetki ALMAZ: admin bypass ile erişmeye devam eder; personel ise zaten
        // erişemiyordu ve şimdi de yalnız AÇIKÇA verilirse erişir (deny-by-default korunur).
        // "Yönetim düzeyi" sayılır (IsAdminRestricted) — çöp kutusu silinmiş kayıtları geri getirir.
        ("trash", "Çöp Kutusu"),
    };

    /// <summary>Yetki kontrolünden muaf, herkese görünür modüller (Uyarılar ekranı yetkiye göre kendi filtreler).</summary>
    public static bool IsPublic(string moduleKey)
        => moduleKey is Dashboard or About or Theme or "alerts";

    /// <summary>DYR-01 (ADR-173, PK-J1): OKUMASI herkese açık, YAZMASI normal yetki kurallarına tabi
    /// modüller. <see cref="IsPublic"/>'ten farkı: IsPublic yazmayı TÜMÜYLE kapatır (View dışı her şey
    /// false) — duyuruda ise yönetici oluşturabilmeli. Mevcut modüllerin davranışı DEĞİŞMEZ.</summary>
    public static bool IsPublicRead(string moduleKey) => moduleKey == "announcements";

    /// <summary>Kullanıcı REHBERİ (2026-07-25): kullanıcı LİSTESİ tüm oturum sahiplerine görünür (menüde çıkar,
    /// salt-okuma sınırlı liste). Oluşturma/düzenleme/şifre sıfırlama YİNE yalnız admindir (write yolları
    /// IsAdmin ister; menüde create/edit/delete bayrakları admin dışına false gelir). Yetki AĞACINDA yönetimi
    /// değişmez (users hâlâ admin-restricted).</summary>
    public static bool IsUserDirectory(string moduleKey) => moduleKey == "users";

    /// <summary>Modül anahtarı → kullanıcıya dönük Türkçe etiket (bilinmeyen anahtar olduğu gibi döner).
    /// Hata mesajlarında ham anahtar ("stock") yerine ekran adı ("Stok İşlemleri") göstermek için —
    /// <c>RequestStatusOptions.Label</c> ile aynı desen. Eklendi: RPR-15 (2026-08-26).</summary>
    public static string Label(string moduleKey)
    {
        foreach (var (k, l) in All) if (string.Equals(k, moduleKey, StringComparison.Ordinal)) return l;
        return moduleKey;
    }

    /// <summary>
    /// Yalnız Süper Admin erişebilir; Firma Admini dahil hiç kimseye ATANAMAZ (admin bypass geçersiz).
    /// Firma Tanım platform sahibinindir; çok-firmalı dağıtımda firma admini başka firmayı yönetemez.
    /// </summary>
    public static bool IsSuperAdminOnly(string moduleKey)
        => moduleKey is "companies" or "releases" or "server_backups" or "machines" or "permission_templates"
            or "server_status" or "quota_monitor" or "machine_backups" or "role_permissions"
            or "purge_company"    // ADR-083 — geri alınamaz silme; devredilemez, yalnız süper admin
            or "screen_visibility";   // G5 — platform görünürlüğü tüm firmayı etkiler; devredilemez

    /// <summary>
    /// YET (2026-08-18, kullanıcı kuralı) — "AÇIK-VERİLİR" KATMAN. Bugüne kadar yalnız iki uç vardı:
    /// <list type="bullet">
    ///   <item><see cref="IsSuperAdminOnly"/> → HİÇ devredilemez</item>
    ///   <item>normal modül → firma adminine <b>admin bypass</b> ile örtük açık</item>
    /// </list>
    /// Kullanıcının istediği ara katman ikisi de değildi: <b>devredilebilir ama asla örtük verilmeyen</b>.
    ///
    /// Bu modüller için:
    /// <list type="number">
    ///   <item><see cref="Can"/> içindeki admin bypass'ı <b>GEÇERSİZDİR</b> — açıkça verilmedikçe kimse alamaz
    ///     (süper admin ve geliştirici modu muaf).</item>
    ///   <item>Devretme: <b>Süper Admin veya Kısıtlı Süper Admin</b>, ya da yetkiyi kendisi AÇIKÇA almış olan.
    ///     Böylece istenen zincir kurulur: SA/KSA → Admin → Personel; her kademe yalnız kendisinde olanı verir.</item>
    ///   <item>"İlk admin her şeyi verebilir" kestirmesi bu modüllerde <b>UYGULANMAZ</b>.</item>
    /// </list>
    /// Rol Yetki Kontrol matrisine normal modül gibi girer → rol bazlı yasak da konabilir.
    /// </summary>
    public static bool IsExplicitOnly(string moduleKey)
        => moduleKey is "local_reset";

    /// <summary>
    /// #3 (şema Rol Durumları): Bu modüller alt rollere (Personel) VERİLEMEZ — verilmek istenirse kullanıcı
    /// önce Admin'e yükseltilmelidir (web'de uyarı penceresi + otomatik yükseltme). Süper admin bu kuraldan muaf.
    /// </summary>
    public static bool IsAdminRestricted(string moduleKey)
        => moduleKey is "users" or "permissions" or "branches" or "audit" or "backup" or "stock_change_log"
            // G2-B1: Çöp Kutusu silinmiş kayıtları geri getirir → yönetim düzeyi. Bugünkü fiilî davranış
            // (yalnız admin görebiliyordu) böylece KORUNUR; alt role verilmek istenirse önce Admin'e yükseltilir.
            or "trash";
}

/// <summary>Modül seviyesi özel buton anahtarları (deny-by-default; açıkça verilmedikçe gizli).</summary>
public static class SpecialButtons
{
    public const string Approve = "btn-approve";          // LEGACY — Talep Onaylama artık "request_approval" MODÜLÜ (Migration035). Yalnız migration string referansı.
    public const string Reverse = "btn-reverse";          // ters kayıt / iptal
    public const string RestoreTrash = "btn-restore";     // çöp kutusu geri yükle
    public const string ResetDatabase = "btn-reset-db";   // DB sıfırlama
    public const string ChangeCompanyLogo = "btn-logo";   // şirket logosu değiştir
    public const string AddLookup = "btn-add-lookup";     // "+" satır içi tanım ekleme (genel)
    public const string ExportReports = "btn-export-reports";       // Raporlar ekranı (şube bazlı) Excel dışa aktarma
    public const string ExportManagerReports = "btn-export-mgr-reports"; // Yönetici raporları (firma geneli/şablon/durum) Excel dışa aktarma
    /// <summary>GENEL şube seçimi / çok-şubeli görüntüleme (kullanıcı isteği 2026-08-07). Rapor'a özel DEĞİL —
    /// ileride Dashboard/Analiz/Grafik ekranlarında da kullanılacak. Bu yetki olmayan (normal) kullanıcı yalnız
    /// login şubesini görür; şube seçici gizli. Admin/süper admin bypass (CanUseButton). Deny-by-default.</summary>
    public const string BranchSelect = "btn-branch-select";

    /// <summary>
    /// ⭐ TRH-01 (kullanıcı isteği 2026-08-27) — GERİ/İLERİ TARİHLİ İŞLEM.
    ///
    /// İşlem tarihi (iş günü) alanını BUGÜNDEN farklı bir güne ayarlayabilme yetkisi. Bu yetki YOKSA
    /// alan görünür ama BUGÜNE kilitlidir — kullanıcı yanlışlıkla geçmiş/gelecek tarihe kayıt açamaz.
    /// Kayıt anı (<c>created_at</c>) bu yetkiden BAĞIMSIZDIR ve daima gerçek saattir; yani geçmişe kayıt
    /// girilse bile logda ne zaman girildiği görünür.
    /// </summary>
    public const string BackDate = "btn-backdate";

    /// <summary>
    /// ⭐ LOG-01 (kullanıcı isteği 2026-08-27) — EKRAN KAYIT GEÇMİŞİ (log).
    ///
    /// Her ekranın kendi işlem geçmişini (kim, ne zaman, ne yaptı) görüntüleme yetkisi. Salt okunur;
    /// veri değiştirmez. Deny-by-default: açıkça verilmedikçe buton görünmez.
    /// </summary>
    public const string ScreenLog = "btn-screen-log";

    /// <summary>Yetki ağacında gösterilen özel buton kataloğu (tek doğru kaynak; yeni buton eklenince otomatik gelir).</summary>
    /// <summary>
    /// Yetki AĞACINDA görünen (yani devredilebilen) özel butonlar.
    ///
    /// ⭐ YET-02 (denetim 2026-08-26) — <see cref="Reverse"/> BU LİSTEDE YOKTU, ama üç gerçek işlemin
    /// kapısıydı: stok belgesi ters kaydı, yakıt depo girişi iptali ve yakıt dağıtımı iptali
    /// (<c>AccessControl.RequireButton(s, SpecialButtons.Reverse)</c>). Listede olmadığı için yalnızca
    /// ADMİN bypass'ıyla geçilebiliyordu: firma yöneticisi bu yetkiyi kimseye VEREMİYOR, depo kullanıcısı
    /// da "Yetki yok: buton btn-reverse" hatasında kilitleniyordu — yöneticinin çözemeyeceği bir çıkmaz.
    ///
    /// Listeye eklenmek kimseye yetki VERMEZ (deny-by-default sürer); yalnızca yöneticinin bilinçli
    /// olarak verebilmesini sağlar. Admin davranışı değişmez.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        (RestoreTrash, "Çöp Kutusu Geri Yükle"),
        (Reverse, "İptal / Ters Kayıt"),
        (ResetDatabase, "Veritabanı Sıfırlama"),
        (ChangeCompanyLogo, "Firma Logosu Değiştir"),
        (AddLookup, "\"+\" Satır İçi Ekleme"),
        (ExportReports, "Rapor Excel Dışa Aktarma"),
        (ExportManagerReports, "Yönetici Rapor Excel Dışa Aktarma"),
        (BranchSelect, "Şube Seçimi (Çok Şubeli Görüntüleme)"),
        (BackDate, "Geri / İleri Tarihli İşlem"),
        (ScreenLog, "Ekran Kayıt Geçmişi (Log)"),
    };
}
