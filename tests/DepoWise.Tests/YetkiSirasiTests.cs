using DepoWise.Application.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 1 (ADR-221, 2026-09-05) — YETKİ ÖNCELİK SIRASI SÖZLEŞMESİ ═══
///
/// <b>Neden bu dosya var.</b> Projede yetki katmanlarının her biri ayrı ayrı test ediliyor
/// (<see cref="RoleGrantTests"/>, <see cref="ExplicitOnlyModuleTests"/>, <see cref="AuthPermissionTests"/> …),
/// ama <b>katmanlar arasındaki SIRA hiçbir yerde yazılı değil</b> — yalnız
/// <c>AccessControl.Can</c> içindeki satır sırasından okunuyor. Bir sonraki fazda bu metodun altına
/// <c>role_permissions</c> katmanı eklenecek; sırayı kazara bozmak <b>canlı kullanıcıların yetkisini
/// SESSİZCE değiştirir</b> — ne hata verir, ne test kırılır, kimse fark etmez.
///
/// Bu testler bugünkü sırayı <b>sözleşme hâline getirir</b>:
///
/// <code>
/// 1. Rol kilidi (role_grant_limits)   → DENY   [süper admin muaf]
/// 2. Yapısal modül sınıfı              → sınıfa özel kural
/// 3. Admin bypass                      → ALLOW
/// 4. Kullanıcının açık izni            → ALLOW/DENY
/// 5. Varsayılan                        → DENY
/// </code>
///
/// <b>Her test bir ÜSTÜNLÜK iddiasıdır</b>, tek bir katmanın çalıştığını değil: "N. katman
/// N+1'i yener". Bu yüzden her senaryoda alt katman BİLEREK "izin verecek" şekilde kurulur —
/// üst katman onu ezemezse test kırılır.
///
/// ⚠️ Bu dosya <b>hiçbir davranış değiştirmez</b>; yalnız bugünkü davranışı mühürler.
/// </summary>
public class YetkiSirasiTests
{
    private const string Co = "YSR";

    // ── Oturum kurucular ────────────────────────────────────────────────────────────────────

    private static SessionContext Oturum(string[] roller, PermissionSet? izinler = null,
        string[]? kapaliModuller = null)
        => new("u-" + string.Join("-", roller), Co, roller, izinler ?? PermissionSet.Empty)
        {
            BlockedModules = new HashSet<string>(kapaliModuller ?? Array.Empty<string>(), StringComparer.Ordinal),
        };

    private static PermissionSet TamIzin(string modul)
        => new(new[] { new ModulePermission(modul, true, true, true, true) });

    private static SessionContext SuperAdmin(string[]? kapali = null)
        => Oturum(new[] { RoleKeys.SuperAdmin }, null, kapali);

    private static SessionContext FirmaAdmini(PermissionSet? izin = null, string[]? kapali = null)
        => Oturum(new[] { RoleKeys.CompanyAdmin }, izin, kapali);

    private static SessionContext Personel(PermissionSet? izin = null, string[]? kapali = null)
        => Oturum(new[] { RoleKeys.Staff }, izin, kapali);

    /// <summary>Sıradan (yapısal sınıfı olmayan) bir modül — katman sırası bunun üzerinde ölçülür.</summary>
    private const string NormalModul = "materials";

    // ══════════════════════ KATMAN 1 — ROL KİLİDİ ══════════════════════

    /// <summary>
    /// ⭐ 1 > 3 — Rol kilidi ADMIN BYPASS'INI yener.
    ///
    /// Firma admini normalde <c>materials</c>'a bypass ile erişir. Süper admin bu ekranı admin ROLÜNE
    /// kapattıysa erişemez. Kilit bypass'tan SONRA uygulansaydı, süper adminin kapattığı ekran
    /// yöneticide açık kalırdı — yani platform sahibinin kararı sessizce hükümsüz olurdu.
    /// </summary>
    [Fact]
    public void P1_RolKilidi_AdminBypassini_Yener()
    {
        var admin = FirmaAdmini(kapali: new[] { NormalModul });

        Assert.False(AccessControl.Can(admin, NormalModul, PermissionAction.View));
        Assert.False(AccessControl.Can(admin, NormalModul, PermissionAction.Create));
        Assert.False(AccessControl.Can(admin, NormalModul, PermissionAction.Edit));
        Assert.False(AccessControl.Can(admin, NormalModul, PermissionAction.Delete));

        // Kontrol: kilit KALKINCA bypass yine çalışıyor (test kilidi ölçüyor, admini değil).
        Assert.True(AccessControl.Can(FirmaAdmini(), NormalModul, PermissionAction.View));
    }

    /// <summary>
    /// ⭐ 1 > 4 — Rol kilidi AÇIK İZNİ de yener.
    ///
    /// Kullanıcıya modül açıkça verilmiş olsa bile rolü kapalıysa erişemez. Aksi hâlde kilidi
    /// delmenin yolu "kullanıcıya tek tek izin vermek" olurdu ve kilit anlamsızlaşırdı.
    /// </summary>
    [Fact]
    public void P2_RolKilidi_AcikIzni_De_Yener()
    {
        var personel = Personel(TamIzin(NormalModul), kapali: new[] { NormalModul });

        Assert.False(AccessControl.Can(personel, NormalModul, PermissionAction.View));
        Assert.False(AccessControl.Can(personel, NormalModul, PermissionAction.Edit));

        // Kontrol: kilit yokken aynı açık izin ÇALIŞIYOR.
        Assert.True(AccessControl.Can(Personel(TamIzin(NormalModul)), NormalModul, PermissionAction.View));
    }

    /// <summary>
    /// ⭐ Rol kilidinin BELGELİ istisnası: süper admin muaftır.
    ///
    /// Gerekçe kodda yazılı: aksi hâlde platform sahibi kendini kilitleyebilirdi. Bu bir açık değil,
    /// bilinçli bir kaçış kapısıdır ve kayıt altına alınıyor.
    /// </summary>
    [Fact]
    public void P3_RolKilidi_SuperAdmini_Etkilemez()
    {
        var sa = SuperAdmin(kapali: new[] { NormalModul, "companies", "users" });

        Assert.True(AccessControl.Can(sa, NormalModul, PermissionAction.Delete));
        Assert.True(AccessControl.Can(sa, "companies", PermissionAction.View));
    }

    // ══════════════════════ KATMAN 2 — YAPISAL MODÜL SINIFI ══════════════════════

    /// <summary>
    /// ⭐ 2 > 3 — <c>IsPublic</c> modülde YAZMA, ADMİNE BİLE kapalıdır.
    ///
    /// Bu şaşırtıcı ama doğrudur: "herkese açık" modüller (Ana Ekran, Hakkında, Tema, Uyarılar)
    /// yalnız OKUMA içindir; yazma kavramı yoktur. Admin bypass'ı bu sınıfın ÜSTÜNDE olsaydı
    /// yönetici, yazma yolu bulunmayan bir modülde "yetkim var" sanırdı.
    /// </summary>
    [Fact]
    public void P4_HerkeseAcik_Modulde_Yazma_Adminde_De_Kapali()
    {
        foreach (var modul in new[] { AppModules.Dashboard, AppModules.About, AppModules.Theme, "alerts" })
        {
            Assert.True(AppModules.IsPublic(modul), $"{modul} artık IsPublic değil — test varsayımı eskimiş.");

            // Okuma: herkese açık (izinsiz personel dahil).
            Assert.True(AccessControl.Can(Personel(), modul, PermissionAction.View));
            Assert.True(AccessControl.Can(FirmaAdmini(), modul, PermissionAction.View));

            // Yazma: HERKESE kapalı — admin ve açık izin dahil.
            Assert.False(AccessControl.Can(FirmaAdmini(), modul, PermissionAction.Create));
            Assert.False(AccessControl.Can(Personel(TamIzin(modul)), modul, PermissionAction.Edit));
            Assert.False(AccessControl.Can(Personel(TamIzin(modul)), modul, PermissionAction.Delete));
        }
    }

    /// <summary>
    /// ⭐ <c>IsPublicRead</c> — okuma herkese, YAZMA normal kurallara tabi.
    ///
    /// <c>IsPublic</c>'ten farkı budur: duyuruyu herkes okur ama yalnız yetkili oluşturur.
    /// İki sınıfın karıştırılması ya duyuruları gizler ya da herkese yazdırır.
    /// </summary>
    [Fact]
    public void P5_OkumasiHerkese_Modulde_Yazma_Normal_Kurallara_Tabi()
    {
        const string modul = "announcements";
        Assert.True(AppModules.IsPublicRead(modul));

        // Okuma: izinsiz personel bile görür.
        Assert.True(AccessControl.Can(Personel(), modul, PermissionAction.View));

        // Yazma: izinsiz personel YAZAMAZ...
        Assert.False(AccessControl.Can(Personel(), modul, PermissionAction.Create));
        // ...açık izinli personel YAZAR...
        Assert.True(AccessControl.Can(Personel(TamIzin(modul)), modul, PermissionAction.Create));
        // ...ve admin bypass ile YAZAR (IsPublic'ten ayrıldığı nokta).
        Assert.True(AccessControl.Can(FirmaAdmini(), modul, PermissionAction.Create));
    }

    /// <summary>
    /// ⭐ 2 > 3 — <c>IsSuperAdminOnly</c> admin bypass'ını yener.
    ///
    /// Çok firmalı dağıtımda firma yöneticisi başka firmayı yönetememelidir. Bypass bu sınıfın
    /// üstünde olsaydı her firma admini "Firma Tanım", "Kalıcı Silme", "Sunucu Yedekleri" gibi
    /// platform ekranlarına erişirdi.
    /// </summary>
    [Fact]
    public void P6_SuperAdminOnly_AdminBypassini_Yener()
    {
        foreach (var modul in new[] { "companies", "purge_company", "server_backups", "role_permissions", "screen_visibility" })
        {
            Assert.True(AppModules.IsSuperAdminOnly(modul), $"{modul} artık süper-admin-only değil.");
            Assert.False(AccessControl.Can(FirmaAdmini(), modul, PermissionAction.View));
            Assert.True(AccessControl.Can(SuperAdmin(), modul, PermissionAction.View));
        }
    }

    /// <summary>
    /// ⭐ <c>IsSuperAdminOnly</c> içinde bile AÇIK İZİN çalışır (B5 kararı, 2026-08-19).
    ///
    /// "Yetki tamamen süper adminin elinde olsun": süper admin bilerek verirse rol fark etmeksizin
    /// erişilir. Deny-by-default bozulmaz — bypass hâlâ geçersizdir, yalnız açık izin geçerlidir.
    /// </summary>
    [Fact]
    public void P7_SuperAdminOnly_Acikca_Verilirse_Erisilir()
    {
        const string modul = "companies";

        Assert.False(AccessControl.Can(Personel(), modul, PermissionAction.View));           // izinsiz → yok
        Assert.True(AccessControl.Can(Personel(TamIzin(modul)), modul, PermissionAction.View)); // açık izin → var
        Assert.False(AccessControl.Can(FirmaAdmini(), modul, PermissionAction.View));         // bypass → yine yok
    }

    /// <summary>
    /// ⭐ 2 > 3 — <c>IsExplicitOnly</c> ("açık-verilir") admin bypass'ını yener.
    ///
    /// Devredilebilir ama asla ÖRTÜK verilmeyen ara katman. Firma admini kendiliğinden almaz.
    /// </summary>
    [Fact]
    public void P8_AcikVerilir_Modul_AdminBypassini_Yener()
    {
        const string modul = "local_reset";
        Assert.True(AppModules.IsExplicitOnly(modul));

        Assert.False(AccessControl.Can(FirmaAdmini(), modul, PermissionAction.View));            // örtük ALMAZ
        Assert.True(AccessControl.Can(FirmaAdmini(TamIzin(modul)), modul, PermissionAction.View)); // açıkça verilirse alır
        Assert.True(AccessControl.Can(SuperAdmin(), modul, PermissionAction.View));               // süper admin daima
        Assert.False(AccessControl.Can(Personel(), modul, PermissionAction.View));                // deny-by-default
    }

    // ══════════════════════ KATMAN 3 — ADMIN BYPASS ══════════════════════

    /// <summary>⭐ 3 > 4/5 — Firma admini NORMAL modüllerde açık izin OLMADAN tam yetkilidir.</summary>
    [Fact]
    public void P9_AdminBypass_AcikIzin_Gerektirmez()
    {
        var admin = FirmaAdmini();   // hiçbir açık izni YOK

        foreach (var action in new[] { PermissionAction.View, PermissionAction.Create,
                                       PermissionAction.Edit, PermissionAction.Delete })
            Assert.True(AccessControl.Can(admin, NormalModul, action), $"{action} adminde kapalı çıktı.");
    }

    // ══════════════════════ KATMAN 4 — AÇIK İZİN ══════════════════════

    /// <summary>
    /// ⭐ 4 > 5 — Açık izin varsayılan reddi yener, ama YALNIZ VERİLEN İŞLEM kadar.
    ///
    /// Bu, yetki ekranının temel vaadidir: "görebilir ama silemez". Dört bayrak bağımsız olmalı.
    /// </summary>
    [Fact]
    public void P10_AcikIzin_Yalnizca_Verilen_Islemi_Acar()
    {
        var yalnizGorme = new PermissionSet(new[] { new ModulePermission(NormalModul, true, false, false, false) });
        var p = Personel(yalnizGorme);

        Assert.True(AccessControl.Can(p, NormalModul, PermissionAction.View));
        Assert.False(AccessControl.Can(p, NormalModul, PermissionAction.Create));
        Assert.False(AccessControl.Can(p, NormalModul, PermissionAction.Edit));
        Assert.False(AccessControl.Can(p, NormalModul, PermissionAction.Delete));

        // Ara kombinasyon da bağımsız: oluşturur + düzenler, ama SİLEMEZ.
        var silmeHaric = new PermissionSet(new[] { new ModulePermission(NormalModul, true, true, true, false) });
        var q = Personel(silmeHaric);
        Assert.True(AccessControl.Can(q, NormalModul, PermissionAction.Create));
        Assert.True(AccessControl.Can(q, NormalModul, PermissionAction.Edit));
        Assert.False(AccessControl.Can(q, NormalModul, PermissionAction.Delete));
    }

    // ══════════════════════ KATMAN 5 — VARSAYILAN REDDET ══════════════════════

    /// <summary>
    /// ⭐ 5 — Hiçbir izni olmayan kullanıcı HİÇBİR normal modüle erişemez.
    ///
    /// Katalogdaki TÜM modüller taranır: yeni bir modül eklenip yanlışlıkla varsayılan açık
    /// bırakılırsa burada yakalanır (tek tek modül testleri bunu göremez).
    /// </summary>
    [Fact]
    public void P11_Izinsiz_Kullanici_Hicbir_Korumali_Module_Erisemez()
    {
        var p = Personel();
        var sizanlar = new List<string>();

        foreach (var (modul, _) in AppModules.All)
        {
            if (AppModules.IsPublic(modul)) continue;                                   // okuma serbest — beklenen
            if (AppModules.IsPublicRead(modul) ) continue;                              // okuma serbest — beklenen
            if (AppModules.IsUserDirectory(modul)) continue;                            // kullanıcı rehberi istisnası

            foreach (var action in new[] { PermissionAction.View, PermissionAction.Create,
                                           PermissionAction.Edit, PermissionAction.Delete })
                if (AccessControl.Can(p, modul, action))
                    sizanlar.Add($"{modul}/{action}");
        }

        Assert.True(sizanlar.Count == 0,
            "İzinsiz kullanıcıya AÇIK modül(ler): " + string.Join(", ", sizanlar));
    }

    /// <summary>⭐ Rapor kalemleri de deny-by-default: <c>rpt_*</c> anahtarları açıkça verilmedikçe kapalı.</summary>
    [Fact]
    public void P12_Rapor_Kalemleri_De_Deny_By_Default()
    {
        var p = Personel();
        foreach (var (anahtar, _) in AppModules.ReportItems)
            Assert.False(AccessControl.Can(p, anahtar, PermissionAction.View), anahtar + " izinsiz açık.");

        // Açıkça verilen TEK rapor kalemi açılır, diğerleri kapalı kalır.
        var ilk = AppModules.ReportItems[0].Key;
        var q = Personel(TamIzin(ilk));
        Assert.True(AccessControl.Can(q, ilk, PermissionAction.View));
        if (AppModules.ReportItems.Count > 1)
            Assert.False(AccessControl.Can(q, AppModules.ReportItems[1].Key, PermissionAction.View));
    }

    // ══════════════════════ MENÜ GÖRÜNÜRLÜĞÜ ══════════════════════

    /// <summary>
    /// ⭐ Menü görünürlüğü = okuma yetkisi (tek istisna: kullanıcı REHBERİ).
    ///
    /// Menü ile erişim aynı kaynaktan beslenmezse, kullanıcı göremediği ekranı menüde görür
    /// (ya da tersi) — ikisi ayrışırsa yetki ekranı yalan söylemeye başlar.
    /// </summary>
    [Fact]
    public void P13_Menu_Gorunurlugu_Okuma_Yetkisiyle_Ayni()
    {
        var p = Personel(TamIzin(NormalModul));

        foreach (var (modul, _) in AppModules.All)
        {
            if (AppModules.IsUserDirectory(modul)) continue;   // belgeli istisna, ayrı test
            Assert.Equal(AccessControl.Can(p, modul, PermissionAction.View),
                         AccessControl.CanSeeMenu(p, modul));
        }
    }

    /// <summary>Kullanıcı REHBERİ istisnası: liste herkese görünür, yönetimi yine admindir.</summary>
    [Fact]
    public void P14_Kullanici_Rehberi_Istisnasi_Belgelenir()
    {
        var p = Personel();
        Assert.True(AccessControl.CanSeeMenu(p, "users"));                          // menüde görünür
        Assert.False(AccessControl.Can(p, "users", PermissionAction.Create));       // ama yönetemez
        Assert.False(AccessControl.Can(p, "users", PermissionAction.Delete));
    }

    // ══════════════════════ BUTON YETKİLERİ ══════════════════════

    /// <summary>⭐ Özel buton: admin bypass + açık izin; aksi hâlde kapalı (deny-by-default).</summary>
    [Fact]
    public void P15_Buton_Yetkisi_Admin_Bypass_Ve_Acik_Izin()
    {
        foreach (var (buton, _) in SpecialButtons.All)
        {
            Assert.False(AccessControl.CanUseButton(Personel(), buton), buton + " izinsiz açık.");
            Assert.True(AccessControl.CanUseButton(FirmaAdmini(), buton), buton + " adminde kapalı.");
            Assert.True(AccessControl.CanUseButton(SuperAdmin(), buton));

            var acik = new PermissionSet(Array.Empty<ModulePermission>(), new[] { buton });
            Assert.True(AccessControl.CanUseButton(Personel(acik), buton));
        }
    }

    /// <summary>⭐ Buton izni MODÜL izninden bağımsızdır — biri diğerini açmaz.</summary>
    [Fact]
    public void P16_Buton_Ve_Modul_Izinleri_Bagimsiz()
    {
        var modulVarButonYok = Personel(TamIzin(NormalModul));
        Assert.True(AccessControl.Can(modulVarButonYok, NormalModul, PermissionAction.Delete));
        Assert.False(AccessControl.CanUseButton(modulVarButonYok, SpecialButtons.Reverse));

        var butonVarModulYok = Personel(new PermissionSet(Array.Empty<ModulePermission>(),
            new[] { SpecialButtons.Reverse }));
        Assert.True(AccessControl.CanUseButton(butonVarModulYok, SpecialButtons.Reverse));
        Assert.False(AccessControl.Can(butonVarModulYok, NormalModul, PermissionAction.View));
    }

    // ══════════════════════ FAIL-CLOSED KAPI ══════════════════════

    /// <summary>
    /// ⭐ <c>Require</c> ve <c>RequireButton</c> servis sınırında İSTİSNA atar.
    ///
    /// <c>Can</c>'in false dönmesi yetmez: servis katmanı bunu HATAYA çevirmelidir, aksi hâlde
    /// çağıran kod dönüş değerini kontrol etmeyi unutabilir ve yetkisiz iş sessizce yürür.
    /// </summary>
    [Fact]
    public void P17_Require_Fail_Closed_Istisna_Atar()
    {
        var p = Personel();

        Assert.Throws<ForbiddenException>(() => AccessControl.Require(p, NormalModul, PermissionAction.View));
        Assert.Throws<ForbiddenException>(() => AccessControl.RequireButton(p, SpecialButtons.Reverse));

        // Yetkili çağrı istisna ATMAZ.
        AccessControl.Require(Personel(TamIzin(NormalModul)), NormalModul, PermissionAction.View);
        AccessControl.Require(FirmaAdmini(), NormalModul, PermissionAction.Delete);
    }

    // ══════════════════════ TENANT ══════════════════════

    /// <summary>
    /// ⭐ Tenant kapısı yetkiden BAĞIMSIZ ve ondan önce gelir: tam yetkili bir firma admini bile
    /// başka firmanın kaydına dokunamaz. Yetki "ne yapabilir", tenant "hangi veride" sorusudur.
    /// </summary>
    [Fact]
    public void P18_Tenant_Kapisi_Yetkiden_Bagimsizdir()
    {
        var admin = FirmaAdmini();

        // Payload'daki yabancı firma REDDEDİLİR; kendi firması sorunsuz.
        Assert.Throws<ForbiddenException>(() => TenantAccessGuard.ResolveCompanyId(admin, "BASKA-FIRMA"));
        Assert.Equal(Co, TenantAccessGuard.ResolveCompanyId(admin, Co));
        Assert.Equal(Co, TenantAccessGuard.ResolveCompanyId(admin, null));

        // Yabancı kaydın sahipliği REDDEDİLİR.
        Assert.Throws<ForbiddenException>(() => TenantAccessGuard.EnsureOwnership(admin, "BASKA-FIRMA"));
        TenantAccessGuard.EnsureOwnership(admin, Co);

        // Süper admin firma değiştirebilir (belgeli istisna).
        Assert.Equal("BASKA-FIRMA", TenantAccessGuard.ResolveCompanyId(SuperAdmin(), "BASKA-FIRMA"));
    }

    // ══════════════════════ ŞUBE KAPSAMI (VERİ KAPSAMI) ══════════════════════

    /// <summary>
    /// ⭐ Açık şube kapsamı ADMİN BYPASS'INI da bağlar — yetki "ne yapabilir", kapsam "hangi
    /// veride" sorusudur ve ikincisi admin için de geçerlidir.
    /// </summary>
    [Fact]
    public void P19_Acik_Sube_Kapsami_Admini_De_Baglar()
    {
        var admin = FirmaAdmini();
        admin.ScopeBranchIds = new[] { "sube-A" };

        var izinli = BranchAccess.Allowed(admin);
        Assert.NotNull(izinli);
        Assert.Equal(new[] { "sube-A" }, izinli!);

        // Kapsam YOKKEN admin sınırsızdır (bugünkü davranış).
        Assert.Null(BranchAccess.Allowed(FirmaAdmini()));
    }

    /// <summary>
    /// ⭐ Kapsam belirleme sırası — bugünkü davranış aynen mühürlenir.
    ///
    /// 🔴 4. madde (şubesiz kullanıcı → SINIRSIZ) bilinçli bir GEVŞEKLİKTİR ve ADR-221'de
    /// sıkılaştırma adayı olarak işaretlenmiştir (K3 = HAYIR: bu turda değiştirilmiyor).
    /// Test bunu "doğru" olduğu için değil, <b>bugünkü gerçek</b> olduğu için kilitler —
    /// ileride bilerek değiştirilirse burada görünür.
    /// </summary>
    [Fact]
    public void P20_Sube_Kapsami_Belirleme_Sirasi()
    {
        // 1) Açık kapsam her şeyin üstünde.
        var s1 = Personel();
        s1.ScopeBranchIds = new[] { "A" };
        s1.HomeBranchId = "B";
        Assert.Equal(new[] { "A" }, BranchAccess.Allowed(s1)!);

        // 2) Tüm şube yetkisi → sınırsız.
        var s2 = new SessionContext("u", Co, new[] { RoleKeys.Staff }, PermissionSet.Empty, canViewAllBranches: true);
        Assert.Null(BranchAccess.Allowed(s2));

        // 3) Kendi şubesi → yalnız o şube.
        var s3 = Personel();
        s3.HomeBranchId = "B";
        Assert.Equal(new[] { "B" }, BranchAccess.Allowed(s3)!);

        // 4) Şubesiz → SINIRSIZ (bilinçli gevşeklik; ADR-221 R4).
        Assert.Null(BranchAccess.Allowed(Personel()));
    }

    // ══════════════════════ RAPOR GÖRÜNÜRLÜĞÜ (OR MANTIĞI) ══════════════════════

    /// <summary>
    /// ⭐ Rapor görünürlüğü = kategori izni <b>VEYA</b> rapor kalemi izni.
    ///
    /// OR bilinçlidir (ADR-221 K4 = korunacak): kategori bazlı eski atamalar çalışmaya devam eder.
    /// AND'e çevrilseydi bugün rapor gören kullanıcıların bir kısmı sessizce kör olurdu.
    /// </summary>
    [Fact]
    public void P21_Rapor_Gorunurlugu_Kategori_VEYA_Kalem()
    {
        var rapor = DepoWise.Application.Reports.ReportCatalog.All[0];
        var kalemAnahtari = AppModules.ReportItemKey(rapor.Key);

        // İzinsiz → görünmez.
        Assert.False(DepoWise.Application.Reports.ReportCatalog.CanSee(Personel(), rapor));

        // YALNIZ rapor kalemi verilmiş → görünür.
        Assert.True(DepoWise.Application.Reports.ReportCatalog.CanSee(Personel(TamIzin(kalemAnahtari)), rapor));

        // Admin bypass ile → görünür (kategori modülü üzerinden).
        Assert.True(DepoWise.Application.Reports.ReportCatalog.CanSee(FirmaAdmini(), rapor));
    }

    // ══════════════════════ DEVRETME TAVANI ══════════════════════

    /// <summary>
    /// ⭐ "Kimse kendinde olmayanı veremez" — devretme tavanı <c>Can</c> ile AYNI kuralları kullanır.
    ///
    /// Rolüne kapatılmış bir modülü aktör kendisi kullanamaz; başkasına da VEREMEZ. Bu iki kural
    /// ayrı kaynaklardan hesaplansaydı, kapalı modül devredilerek kilit delinirdi (geçmişte olan hata).
    /// </summary>
    [Fact]
    public void P22_Devretme_Tavani_Kendi_Yetkisini_Asamaz()
    {
        // Rolüne kapatılmış modül: ne kullanır ne devreder.
        var kisitliAdmin = FirmaAdmini(kapali: new[] { NormalModul });
        var tavan = AccessControl.GrantCeiling(kisitliAdmin, NormalModul);
        Assert.False(tavan.CanView || tavan.CanCreate || tavan.CanEdit || tavan.CanDelete);
        Assert.False(AccessControl.Can(kisitliAdmin, NormalModul, PermissionAction.View));

        // Normal modülde admin tam devreder.
        var acikTavan = AccessControl.GrantCeiling(FirmaAdmini(), NormalModul);
        Assert.True(acikTavan.CanView && acikTavan.CanCreate && acikTavan.CanEdit && acikTavan.CanDelete);

        // Personel yalnız KENDİNDE OLANI devreder.
        var yalnizGorme = new PermissionSet(new[] { new ModulePermission(NormalModul, true, false, false, false) });
        var personelTavan = AccessControl.GrantCeiling(Personel(yalnizGorme), NormalModul);
        Assert.True(personelTavan.CanView);
        Assert.False(personelTavan.CanDelete);
    }

    // ══════════════════════ SIRA SÖZLEŞMESİNİN ÖZETİ ══════════════════════

    /// <summary>
    /// ⭐ SIRA TABLOSU — tek bakışta sözleşme. Her satır bir üstünlük iddiasıdır.
    ///
    /// Bu test yukarıdakileri tekrar etmiyor; hepsini <b>tek tabloda</b> toplayarak sıranın
    /// bütününü kilitliyor. Bir sonraki fazda <c>role_permissions</c> katmanı 4. ile 5. arasına
    /// girecek; bu tablo o eklemeden SONRA da aynen geçmelidir.
    /// </summary>
    [Fact]
    public void P23_Oncelik_Sirasi_Tablosu()
    {
        var beklenen = new (string Ad, bool Sonuc, Func<bool> Olc)[]
        {
            ("1>3  rol kilidi admin bypass'ını yener",
                false, () => AccessControl.Can(FirmaAdmini(kapali: new[] { NormalModul }), NormalModul, PermissionAction.View)),

            ("1>4  rol kilidi açık izni yener",
                false, () => AccessControl.Can(Personel(TamIzin(NormalModul), new[] { NormalModul }), NormalModul, PermissionAction.View)),

            ("1—   süper admin rol kilidinden muaf",
                true,  () => AccessControl.Can(SuperAdmin(new[] { NormalModul }), NormalModul, PermissionAction.View)),

            ("2>3  süper-admin-only admin bypass'ını yener",
                false, () => AccessControl.Can(FirmaAdmini(), "companies", PermissionAction.View)),

            ("2>3  açık-verilir admin bypass'ını yener",
                false, () => AccessControl.Can(FirmaAdmini(), "local_reset", PermissionAction.View)),

            ("2>3  herkese-açık modülde yazma adminde de kapalı",
                false, () => AccessControl.Can(FirmaAdmini(), AppModules.Dashboard, PermissionAction.Create)),

            ("3>4  admin bypass açık izin gerektirmez",
                true,  () => AccessControl.Can(FirmaAdmini(), NormalModul, PermissionAction.Delete)),

            ("4>5  açık izin varsayılan reddi yener",
                true,  () => AccessControl.Can(Personel(TamIzin(NormalModul)), NormalModul, PermissionAction.Edit)),

            ("5    varsayılan reddet",
                false, () => AccessControl.Can(Personel(), NormalModul, PermissionAction.View)),
        };

        var bozulanlar = beklenen.Where(x => x.Olc() != x.Sonuc).Select(x => x.Ad).ToList();

        Assert.True(bozulanlar.Count == 0,
            "ÖNCELİK SIRASI DEĞİŞMİŞ — canlı kullanıcıların yetkisi sessizce değişmiş olabilir:\n  " +
            string.Join("\n  ", bozulanlar));
    }
}
