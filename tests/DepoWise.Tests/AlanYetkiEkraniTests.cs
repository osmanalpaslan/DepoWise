using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 3b-5 (ADR-223, kullanıcı onayı 2026-09-05) — ALAN YETKİSİ YÖNETİM KATMANI ═══
///
/// 3b-3/3b-4 KARARI kurmuştu; bu faz onu <b>yönetilebilir</b> ve <b>görünür</b> yapar.
/// Testler yönetim ekranının kendisini de yetki sisteminin parçası olarak sınar (kullanıcı şartı §12).
///
/// <b>En önemli kabul kriteri (değişmedi):</b> koruma listesi boşken yetki ağacı ve tüm ekranlar
/// bugünküyle BİREBİR aynı — YK1 bunu doğrudan ölçer.
///
///  YK1  — Koruma yokken yetki ağacında HİÇ alan satırı yok (geriye uyumluluk)
///  YK2  — Alan korumalı yapılınca ağaçta AİT OLDUĞU EKRANIN ardında satır belirir
///  YK3  — Etiket insan-okur: teknik <c>fld_</c> anahtarı kullanıcıya gösterilmez
///  YK4  — Koruma kaldırılınca satır ağaçtan KAYBOLUR
///  YK5  — Alan satırı yalnız Oku/Düzelt taşır (Yaz/Sil bir alanda anlamsız)
///  YK6  — 🔴 EDIT ⇒ VIEW sunucu kapısı: kullanıcı yolunda reddedilir
///  YK7  — 🔴 EDIT ⇒ VIEW sunucu kapısı: ROL yolunda da reddedilir
///  YK8  — DEVRETME TAVANI: kendinde olmayan alan yetkisi başkasına verilemez
///  YK9  — Katalog dışı/ham anahtar yönetim ekranından geçemez (fail-closed)
///  YK10 — Yetkisiz kullanıcı koruma listesini ne okur ne yazar
///  YK11 — Koruma değişince yetki fotoğrafı düşer; ağaç bir sonraki okumada tazedir
///  YK12 — TENANT: A firmasının koruması B firmasının ağacına girmez
///  YK13 — Alan izni verilip koruma açıldığında ETKİN sonuç doğru (uçtan uca senaryo)
///  YK14 — Rol üzerinden verilen alan izni ağaçta ve kararda çalışır
///  YK15 — Katalogdaki her alanın modülü yetki ağacında GERÇEKTEN var (kopuk satır olamaz)
///  YK16 — 🔴 REGRESYON: önekli anahtarlar (rpt_/datype_/fld_) SÜPER ADMIN OLMAYAN aktörde de yazılır
/// </summary>
public class AlanYetkiEkraniTests : IDisposable
{
    private const string Co = "YKR";
    private const string CoB = "YKR-B";
    private const string Pass = "Ykr!2026";

    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly AuthService _auth;
    private readonly PermissionService _perms;
    private readonly FieldProtectionService _koruma;
    private readonly PermissionSnapshotCache _cache = new();
    private readonly string _personelId;

    /// <summary>Test boyunca kullanılan örnek alan: Malzemeler › Birim Fiyat.</summary>
    private static readonly string FiyatAnahtari =
        FieldAccess.Key(FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice);

    public AlanYetkiEkraniTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_ykr_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();

        foreach (var c in new[] { Co, CoB })
            Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{c}','{c}',1,1,1,0);");

        var users = new UserService(_f);
        users.EnsureInitialAdmin(Co, "ykr_admin", Pass, RoleKeys.CompanyAdmin);
        _personelId = users.EnsureInitialAdmin(Co, "ykr_personel", Pass, RoleKeys.Staff);

        _auth = new AuthService(_f, null, _cache);
        _perms = new PermissionService(_f, null, _cache);
        _koruma = new FieldProtectionService(_f, null, _cache);
    }

    // ── yardımcılar ─────────────────────────────────────────────────────────────────────────

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private long Say(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private SessionContext Oturum(string kullaniciAdi)
    {
        var r = _auth.Login(Co, kullaniciAdi, Pass);
        Assert.True(r.Success, "Giriş başarısız: " + kullaniciAdi);
        return r.Session!;
    }

    private static SessionContext SuperAdmin(string co = Co)
        => new("sa", co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);

    private static ModulePermission Tam(string modul) => new(modul, true, true, true, true);

    private void FiyatiKoru(bool korumali = true)
        => _koruma.Set(SuperAdmin(), FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice, korumali);

    /// <summary>Yetki ağacının düz hâli — (grup, anahtar, etiket).</summary>
    private static List<(string Grup, string Key, string Label)> Agac(IReadOnlySet<string>? korumali = null)
        => AppModules.Grouped(korumali)
            .SelectMany(g => g.Items.Select(i => (g.Title, i.Key, i.Label)))
            .ToList();

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }

    // ══════════════════ YK1 — GERİYE UYUMLULUK ══════════════════

    /// <summary>
    /// ⭐ Yetki ağacı, koruma yokken BUGÜNKÜ ağacın birebir aynısıdır: tek bir <c>fld_</c> satırı
    /// bile yoktur. Bu test kırılırsa yayın günü yöneticinin gördüğü ekran değişmiş demektir.
    /// </summary>
    [Fact]
    public void YK1_Koruma_Yokken_Agacta_Alan_Satiri_Yok()
    {
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM field_protections;"));

        var varsayilan = Agac();                       // parametresiz = eski çağrılar
        var bosKume = Agac(new HashSet<string>());     // boş küme

        Assert.DoesNotContain(varsayilan, x => AppModules.IsFieldItem(x.Key));
        Assert.DoesNotContain(bosKume, x => AppModules.IsFieldItem(x.Key));
        Assert.Equal(varsayilan.Count, bosKume.Count);

        // Oturumun kümesi de boş → gerçek kullanım yolunda da satır yok.
        var s = Oturum("ykr_personel");
        Assert.Empty(s.ProtectedFields);
        Assert.DoesNotContain(Agac(s.ProtectedFields), x => AppModules.IsFieldItem(x.Key));
    }

    // ══════════════════ YK2–YK4 — AĞACIN İÇERİĞİ ══════════════════

    [Fact]
    public void YK2_Korumali_Alan_Ekraninin_Ardinda_Belirir()
    {
        FiyatiKoru();
        var s = Oturum("ykr_admin");
        var agac = Agac(s.ProtectedFields);

        var i = agac.FindIndex(x => x.Key == FiyatAnahtari);
        Assert.True(i > 0, "Korumalı alan yetki ağacında görünmedi.");

        // Ait olduğu ekranın HEMEN ardında ve AYNI grupta.
        Assert.Equal("materials", agac[i - 1].Key);
        Assert.Equal("Malzeme & Stok", agac[i].Grup);
        Assert.Equal(agac[i - 1].Grup, agac[i].Grup);

        // Yalnız korumalı olan geldi; diğer katalog alanları gelmedi.
        Assert.Single(agac.Where(x => AppModules.IsFieldItem(x.Key)));
    }

    [Fact]
    public void YK3_Etiket_Insan_Okur_Teknik_Anahtar_Gostermez()
    {
        FiyatiKoru();
        var agac = Agac(Oturum("ykr_admin").ProtectedFields);
        var satir = agac.Single(x => x.Key == FiyatAnahtari);

        Assert.DoesNotContain("fld_", satir.Label);
        Assert.DoesNotContain("unit_price", satir.Label);
        Assert.Equal("Alan › Malzemeler › Birim Fiyat", satir.Label);

        // Hata mesajı / denetim kaydı da ham anahtar göstermez (koruma durumundan bağımsız).
        Assert.Equal("Alan › Malzemeler › Birim Fiyat", AppModules.Label(FiyatAnahtari));
    }

    [Fact]
    public void YK4_Koruma_Kaldirilinca_Satir_Kaybolur()
    {
        FiyatiKoru();
        Assert.Contains(Agac(Oturum("ykr_admin").ProtectedFields), x => x.Key == FiyatAnahtari);

        FiyatiKoru(false);
        var s = Oturum("ykr_admin");
        Assert.Empty(s.ProtectedFields);
        Assert.DoesNotContain(Agac(s.ProtectedFields), x => AppModules.IsFieldItem(x.Key));
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM field_protections;"));
    }

    [Fact]
    public void YK5_Alan_Satiri_Yalniz_Oku_Ve_Duzelt_Tasir()
    {
        Assert.True(AppModules.IsFieldItem(FiyatAnahtari));
        Assert.True(AppModules.FieldItemUsesViewEditOnly(FiyatAnahtari));

        // Normal ekran modülü etkilenmez — dört kutu sürer.
        Assert.False(AppModules.IsFieldItem("materials"));
        Assert.False(AppModules.FieldItemUsesViewEditOnly("materials"));
    }

    // ══════════════════ YK6–YK7 — EDIT ⇒ VIEW SUNUCU KAPISI ══════════════════

    /// <summary>
    /// 🔴 Arayüz bu kombinasyonu oluşturamıyor; ama arayüz güvenlik değildir. Doğrudan servis
    /// çağrısıyla "görme kapalı, düzenleme açık" gönderildiğinde REDDEDİLMELİ ve hiçbir satır
    /// yazılmamalıdır (kısmi kayıt yok).
    /// </summary>
    [Fact]
    public void YK6_Gorme_Olmadan_Duzenleme_Kullanici_Yolunda_Reddedilir()
    {
        FiyatiKoru();

        var ex = Assert.Throws<ArgumentException>(() =>
            _perms.SaveForUser(SuperAdmin(), _personelId,
                new[] { Tam("materials"), new ModulePermission(FiyatAnahtari, false, false, true, false) },
                Array.Empty<string>()));
        Assert.Contains("görebilmelidir", ex.Message);

        // Hiçbir şey yazılmadı — "materials" izni de gitmedi (işlem bütünüyle reddedildi).
        Assert.Equal(0L, Say($"SELECT COUNT(*) FROM user_permissions WHERE user_id='{_personelId}';"));

        // Geçerli kombinasyon sorunsuz yazılır.
        _perms.SaveForUser(SuperAdmin(), _personelId,
            new[] { Tam("materials"), new ModulePermission(FiyatAnahtari, true, false, true, false) },
            Array.Empty<string>());
        Assert.True(FieldAccess.Duzenlenebilir(Oturum("ykr_personel"),
            FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));
    }

    [Fact]
    public void YK7_Gorme_Olmadan_Duzenleme_Rol_Yolunda_Da_Reddedilir()
    {
        FiyatiKoru();

        var ex = Assert.Throws<ArgumentException>(() =>
            _perms.SaveForRoleKey(SuperAdmin(), RoleKeys.Staff,
                new[] { new ModulePermission(FiyatAnahtari, false, false, true, false) },
                Array.Empty<string>()));
        Assert.Contains("görebilmelidir", ex.Message);
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM role_permissions;"));
    }

    // ══════════════════ YK8 — DEVRETME TAVANI ══════════════════

    /// <summary>
    /// ⭐ Kullanıcı şartı §4: "Bir kullanıcı başkasına kendisinde bulunmayan bir alan yetkisini
    /// verememelidir." Alan kalemleri mevcut <c>GrantCeiling</c> mekanizmasından geçtiği için bu
    /// kural bedava gelir — ama bedava geldiğini KANITLAMAK gerekir.
    /// </summary>
    [Fact]
    public void YK8_Kendinde_Olmayan_Alan_Yetkisi_Verilemez()
    {
        FiyatiKoru();
        var users = new UserService(_f);
        var araciId = users.EnsureInitialAdmin(Co, "ykr_araci", Pass, RoleKeys.Staff);

        // Aracıya yetki yönetimi verilir ama ALAN yetkisi VERİLMEZ.
        _perms.SaveForUser(SuperAdmin(), araciId,
            new[] { Tam("permissions"), Tam("materials") }, Array.Empty<string>());

        var araci = Oturum("ykr_araci");
        Assert.False(AccessControl.GrantCeiling(araci, FiyatAnahtari).CanView);
        Assert.False(FieldAccess.Gorunur(araci, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));

        // Aracı, personele alan yetkisi vermeye çalışır → kırpılır, satır YAZILMAZ.
        _perms.SaveForUser(araci, _personelId,
            new[] { Tam("materials"), new ModulePermission(FiyatAnahtari, true, false, true, false) },
            Array.Empty<string>());

        Assert.Equal(0L, Say(
            $"SELECT COUNT(*) FROM user_permissions WHERE user_id='{_personelId}' AND module_key='{FiyatAnahtari}';"));
        Assert.False(FieldAccess.Gorunur(Oturum("ykr_personel"),
            FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));

        // Aracıya alan yetkisi verilirse artık DEVREDEBİLİR (zincir aşağı doğru işler).
        _perms.SaveForUser(SuperAdmin(), araciId,
            new[] { Tam("permissions"), Tam("materials"), new ModulePermission(FiyatAnahtari, true, false, true, false) },
            Array.Empty<string>());
        _perms.SaveForUser(Oturum("ykr_araci"), _personelId,
            new[] { Tam("materials"), new ModulePermission(FiyatAnahtari, true, false, true, false) },
            Array.Empty<string>());
        Assert.True(FieldAccess.Gorunur(Oturum("ykr_personel"),
            FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));
    }

    // ══════════════════ YK9–YK10 — YÖNETİM EKRANININ KAPILARI ══════════════════

    [Fact]
    public void YK9_Katalog_Disi_Anahtar_Yonetimden_Gecemez()
    {
        Assert.Throws<ArgumentException>(() => _koruma.Set(SuperAdmin(), "materials", "yok_boyle_alan", true));
        Assert.Throws<ArgumentException>(() => _koruma.Set(SuperAdmin(), "yok_boyle_ekran", "unit_price", true));
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM field_protections;"));

        // Liste yalnız katalogdaki alanları döner — uydurma satır üretmez.
        var liste = _koruma.List(SuperAdmin());
        Assert.Equal(FieldProtectionCatalog.All.Count, liste.Count);
        Assert.All(liste, r => Assert.NotNull(FieldProtectionCatalog.Find(r.ScreenKey, r.FieldKey)));
    }

    [Fact]
    public void YK10_Yetkisiz_Kullanici_Koruma_Yonetemez()
    {
        _perms.SaveForUser(SuperAdmin(), _personelId, new[] { Tam("materials") }, Array.Empty<string>());
        var s = Oturum("ykr_personel");

        Assert.Throws<ForbiddenException>(() => _koruma.List(s));
        Assert.Throws<ForbiddenException>(() =>
            _koruma.Set(s, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice, true));
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM field_protections;"));

        // Yalnız OKUMA yetkisi de yazmaya YETMEZ (Edit istenir).
        _perms.SaveForUser(SuperAdmin(), _personelId,
            new[] { new ModulePermission("permissions", true, false, false, false) }, Array.Empty<string>());
        var okur = Oturum("ykr_personel");
        _ = _koruma.List(okur);   // okuyabilir
        Assert.Throws<ForbiddenException>(() =>
            _koruma.Set(okur, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice, true));
    }

    // ══════════════════ YK11 — ÖNBELLEK ══════════════════

    [Fact]
    public void YK11_Koruma_Degisince_Fotograf_Duser_Agac_Tazelenir()
    {
        _perms.SaveForUser(SuperAdmin(), _personelId, new[] { Tam("materials") }, Array.Empty<string>());
        var once = _auth.CreateSessionForUser(Co, _personelId)!;
        Assert.Empty(once.ProtectedFields);
        Assert.True(_cache.Count > 0);

        FiyatiKoru();
        Assert.Equal(0, _cache.Count);   // InvalidateAll çalıştı

        var sonra = _auth.CreateSessionForUser(Co, _personelId)!;
        Assert.Single(sonra.ProtectedFields);
        Assert.Contains(Agac(sonra.ProtectedFields), x => x.Key == FiyatAnahtari);
    }

    // ══════════════════ YK12 — TENANT ══════════════════

    [Fact]
    public void YK12_Baska_Firmanin_Korumasi_Agaca_Girmez()
    {
        FiyatiKoru();   // yalnız Co

        var users = new UserService(_f);
        var bId = users.EnsureInitialAdmin(CoB, "ykr_b", Pass, RoleKeys.CompanyAdmin);
        var rb = _auth.Login(CoB, "ykr_b", Pass);
        Assert.True(rb.Success);

        Assert.Empty(rb.Session!.ProtectedFields);
        Assert.DoesNotContain(Agac(rb.Session!.ProtectedFields), x => AppModules.IsFieldItem(x.Key));

        // A firmasında ise var.
        Assert.Contains(Agac(Oturum("ykr_admin").ProtectedFields), x => x.Key == FiyatAnahtari);
        Assert.Equal(1L, Say($"SELECT COUNT(*) FROM field_protections WHERE company_id='{Co}';"));
        Assert.Equal(0L, Say($"SELECT COUNT(*) FROM field_protections WHERE company_id='{CoB}';"));
        _ = bId;
    }

    // ══════════════════ YK13–YK14 — UÇTAN UCA ══════════════════

    /// <summary>
    /// ⭐ Gerçek yönetici akışı: (1) alanı korumalı yap → (2) ağaçta satırı gör →
    /// (3) kişiye izni ver → (4) kullanıcı alanı görsün. Dört adım da ölçülür.
    /// </summary>
    [Fact]
    public void YK13_Yonetici_Akisi_Uctan_Uca()
    {
        _perms.SaveForUser(SuperAdmin(), _personelId, new[] { Tam("materials") }, Array.Empty<string>());

        // (0) Başlangıç: koruma yok → alan açık (bugünkü davranış)
        Assert.True(FieldAccess.Gorunur(Oturum("ykr_personel"),
            FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));

        // (1) Yönetici alanı korumalı yapar
        FiyatiKoru();

        // (2) Ağaçta satır belirir
        var admin = Oturum("ykr_admin");
        Assert.Contains(Agac(admin.ProtectedFields), x => x.Key == FiyatAnahtari);

        // (3) Personel artık göremez
        Assert.False(FieldAccess.Gorunur(Oturum("ykr_personel"),
            FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));

        // (4) Yönetici izni verir → görür ama DÜZENLEYEMEZ (yalnız Oku verildi)
        _perms.SaveForUser(admin, _personelId,
            new[] { Tam("materials"), new ModulePermission(FiyatAnahtari, true, false, false, false) },
            Array.Empty<string>());

        var s = Oturum("ykr_personel");
        Assert.True(FieldAccess.Gorunur(s, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));
        Assert.False(FieldAccess.Duzenlenebilir(s, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));
    }

    [Fact]
    public void YK14_Rol_Uzerinden_Alan_Izni_Agacta_Ve_Kararda_Calisir()
    {
        _perms.SaveForUser(SuperAdmin(), _personelId, new[] { Tam("materials") }, Array.Empty<string>());
        FiyatiKoru();

        _perms.SaveForRoleKey(SuperAdmin(), RoleKeys.Staff,
            new[] { new ModulePermission(FiyatAnahtari, true, false, true, false) }, Array.Empty<string>());

        var s = Oturum("ykr_personel");
        Assert.True(FieldAccess.Gorunur(s, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));
        Assert.True(FieldAccess.Duzenlenebilir(s, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));

        // Kullanıcının KENDİ satırı yok — izin gerçekten ROLDEN geliyor.
        Assert.Equal(0L, Say(
            $"SELECT COUNT(*) FROM user_permissions WHERE user_id='{_personelId}' AND module_key='{FiyatAnahtari}';"));

        // Rol izni geri alınınca ANINDA kapanır (bayat yetki yok).
        _perms.SaveForRoleKey(SuperAdmin(), RoleKeys.Staff, Array.Empty<ModulePermission>(), Array.Empty<string>());
        Assert.False(FieldAccess.Gorunur(Oturum("ykr_personel"),
            FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));
    }

    // ══════════════════ YK15 — KOPUK SATIR OLAMAZ ══════════════════

    /// <summary>
    /// Katalogdaki her alanın <c>ModuleKey</c>'i yetki ağacında GERÇEKTEN bulunmalıdır. Yanlış
    /// yazılmış bir modül anahtarı, alan satırının sessizce "Diğer" grubuna düşmesine ya da hiç
    /// görünmemesine yol açardı — yönetici alanı bulamaz, koruma fiilen yönetilemez hâle gelirdi.
    /// </summary>
    [Fact]
    public void YK15_Her_Alanin_Modulu_Agacta_Vardir()
    {
        var moduller = AppModules.All.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var f in FieldProtectionCatalog.All)
            Assert.True(moduller.Contains(f.ModuleKey),
                $"«{f.Label}» alanının modülü ağaçta yok: {f.ModuleKey}");

        // Hepsini birden korumalı yapınca her biri ağaçta belirir ve HİÇBİRİ "Diğer"e düşmez.
        foreach (var f in FieldProtectionCatalog.All)
            _koruma.Set(SuperAdmin(), f.ScreenKey, f.FieldKey, true);

        var s = Oturum("ykr_admin");
        var agac = Agac(s.ProtectedFields);
        Assert.Equal(FieldProtectionCatalog.All.Count, agac.Count(x => AppModules.IsFieldItem(x.Key)));
        Assert.DoesNotContain(agac.Where(x => AppModules.IsFieldItem(x.Key)), x => x.Grup == "Diğer");
    }

    // ══════════════════ YK16 — BULUNAN ÜRÜN HATASININ REGRESYONU ══════════════════

    /// <summary>
    /// 🔴 FAZ 3b-5'te BULUNAN GERÇEK ÜRÜN HATASININ regresyon testi. Düzeltme: <c>ClampModule</c>.
    ///
    /// <b>Belirti:</b> süper admin OLMAYAN bir yönetici rapor / kayıt tipi / alan yetkisi verdiğinde
    /// işlem başarılı dönüyor ama izin KAYDOLMUYORDU. Sessiz kusur: hata yok, sonuç da yok.
    ///
    /// <b>Kök neden:</b> devretme tavanı sözlüğü yalnız <c>AppModules.All</c> üzerinde kuruluyordu;
    /// önekli anahtarlar orada bulunmadığı için dört bayrak da siliniyor ve satır atlanıyordu.
    ///
    /// Test hem YENİ alan anahtarını hem de hatadan ETKİLENEN MEVCUT rapor anahtarını ölçer —
    /// düzeltmenin yalnız yeni özelliği değil, eski kusuru da kapattığını kanıtlamak için.
    /// Son bölüm tavanın HÂLÂ çalıştığını doğrular: düzeltme bir kapı açmadı.
    /// </summary>
    [Fact]
    public void YK16_Onekli_Anahtarlar_Super_Admin_Olmayan_Aktorde_De_Yazilir()
    {
        FiyatiKoru();
        var raporAnahtari = AppModules.ReportItems[0].Key;
        Assert.StartsWith(AppModules.ReportItemPrefix, raporAnahtari);

        // AKTÖR: firma admini (süper admin DEĞİL) — hata tam burada ortaya çıkıyordu.
        var admin = Oturum("ykr_admin");
        Assert.False(admin.IsSuperAdmin);

        _perms.SaveForUser(admin, _personelId, new[]
        {
            Tam("materials"),
            Tam("reports"),
            new ModulePermission(FiyatAnahtari, true, false, true, false),
            new ModulePermission(raporAnahtari, true, false, false, false),
        }, Array.Empty<string>());

        // Üç satır da GERÇEKTEN yazıldı.
        Assert.Equal(1L, Say($"SELECT COUNT(*) FROM user_permissions WHERE user_id='{_personelId}' AND module_key='{FiyatAnahtari}';"));
        Assert.Equal(1L, Say($"SELECT COUNT(*) FROM user_permissions WHERE user_id='{_personelId}' AND module_key='{raporAnahtari}';"));

        var s = Oturum("ykr_personel");
        Assert.True(FieldAccess.Gorunur(s, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));
        Assert.True(AccessControl.Can(s, raporAnahtari, PermissionAction.View));

        // ⭐ TAVAN HÂLÂ GEÇERLİ — düzeltme bir kapı AÇMADI. Admin bypass'ı olmayan bir aktör,
        // kendisinde bulunmayan önekli anahtarı devredemez (süper-admin-only ekranlar ayrı ve
        // DAHA ERKEN bir kapıdan zaten reddediliyor; burada ölçülen KIRPMA kapısıdır).
        var users = new UserService(_f);
        var araciId = users.EnsureInitialAdmin(Co, "ykr_araci16", Pass, RoleKeys.Staff);
        _perms.SaveForUser(SuperAdmin(), araciId, new[] { Tam("permissions"), Tam("reports") }, Array.Empty<string>());
        var araci = Oturum("ykr_araci16");
        Assert.False(araci.IsCompanyAdmin);

        var hedefId = users.EnsureInitialAdmin(Co, "ykr_hedef16", Pass, RoleKeys.Staff);
        _perms.SaveForUser(araci, hedefId,
            new[] { Tam("reports"), new ModulePermission(raporAnahtari, true, false, false, false) },
            Array.Empty<string>());
        Assert.Equal(0L, Say($"SELECT COUNT(*) FROM user_permissions WHERE user_id='{hedefId}' AND module_key='{raporAnahtari}';"));
    }
}
