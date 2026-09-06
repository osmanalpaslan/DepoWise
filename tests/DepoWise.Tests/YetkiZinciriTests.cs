using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 1 (ADR-221, 2026-09-05) — YETKİ ZİNCİRİNİN TAMAMI ═══
///
/// <b>Kullanıcının şartı:</b> <i>"Testler sadece 'buton görünmüyor' seviyesinde olmasın. Bir kullanıcı
/// bir özelliğe yetkili değilse UI + API + servis katmanı + veri erişimi zincirinin tamamında
/// engellendiği doğrulanmalı. UI'daki butonun gizlenmesi tek başına güvenlik kabul edilmeyecek."</i>
///
/// Bu dosya her senaryoyu <b>dört halkada birden</b> ölçer:
/// <list type="number">
///   <item><b>UI kararı</b> — arayüzün menüyü/butonu göstermesini belirleyen çağrı,</item>
///   <item><b>API</b> — GERÇEK HTTP isteği (JWT ile), durum kodu,</item>
///   <item><b>Servis</b> — servis metodunun doğrudan çağrısı (masaüstünün ÇEVRİMDIŞI yolu),</item>
///   <item><b>Veri</b> — yanıt gövdesinde gizli içeriğin GEÇMEDİĞİ.</item>
/// </list>
///
/// 3. halka özellikle önemlidir: masaüstü servisleri API'siz de çağırır. Yalnız API'de kontrol
/// olsaydı çevrimdışı yol korumasız kalırdı.
///
/// 🔒 <see cref="ApiTestHost"/> bellek içinde çalışır ve <c>DEPOWISE_PG_URL</c> temizlenir —
/// <b>canlı veritabanına hiçbir istek gitmez.</b>
///
///  ZN1 — Yetkisiz kullanıcı: dört halkanın DÖRDÜNDE de engelli
///  ZN2 — "Görür ama silemez": okuma açık, silme dört halkada da kapalı
///  ZN3 — Doğrudan uç çağrısı (menüyü atlayarak) reddedilir
///  ZN4 — Yetkisiz istek veri SIZDIRMAZ (gövde kontrolü)
///  ZN5 — Buton yetkisi: UI kararı ile servis kapısı AYNI sonucu verir
///  ZN6 — Tenant: başka firmanın kaydına yetkili kullanıcı bile erişemez
///  ZN7 — Kimlik doğrulamasız istek reddedilir
/// </summary>
[Collection("PostgresSchema")]
public class YetkiZinciriTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CoA = "ZNC-A";
    private const string CoB = "ZNC-B";
    private const string Pass = "Znc!2026";
    private const string GizliMalzeme = "B-FIRMASININ-GIZLI-MALZEMESI";

    private ServerServices _svc = null!;
    private HttpClient _yetkisiz = null!;      // hiçbir izni olmayan personel
    private HttpClient _yalnizOkur = null!;    // yalnız View izni olan personel
    private HttpClient _admin = null!;
    private SessionContext _yetkisizOturum = null!, _yalnizOkurOturum = null!;
    private string _malzemeA = "", _malzemeB = "", _subeA = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        foreach (var (id, ad) in new[] { (CoA, "A Firmasi"), (CoB, "B Firmasi") })
            Calistir("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) " +
                     "VALUES(@c,@n,1,1,1,0,5,20,5) ON CONFLICT(id) DO NOTHING;", ("@c", id), ("@n", ad));

        // ── A firması: admin + iki kısıtlı personel ──────────────────────────────────────
        var adminId = _svc.Users.EnsureInitialAdmin(CoA, "znc_admin", Pass, RoleKeys.CompanyAdmin);
        _admin = await _host.LoginAsync("znc_admin", Pass, CoA);

        var adminOturum = new SessionContext(adminId, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _malzemeA = new DepoWise.Infrastructure.Materials.MaterialService(_svc.Factory)
            .Create(adminOturum, new DepoWise.Infrastructure.Materials.NewMaterial("A-KOD", "A Malzeme", UnitPrice: 10m));

        // ⚠️ Personel "Tüm Şubeler" (__all__) ile GİRİŞ YAPAMAZ — bu doğru çalışan bir kapıdır
        // (ApiTestHost'un varsayılanı __all__'dur ve personel için 403 döner). Gerçek kullanıcı gibi
        // davranmak için somut bir şube açılır ve personel o şubeyle giriş yapar.
        _subeA = new DepoWise.Infrastructure.Organization.BranchService(_svc.Factory)
            .Create(adminOturum, new DepoWise.Infrastructure.Organization.NewBranch("A-Merkez"));

        var yetkisizId = _svc.Users.EnsureInitialAdmin(CoA, "znc_yetkisiz", Pass, RoleKeys.Staff);
        var okurId = _svc.Users.EnsureInitialAdmin(CoA, "znc_okur", Pass, RoleKeys.Staff);

        // Yalnız OKUMA izni ver (create/edit/delete YOK).
        Calistir("INSERT INTO user_permissions(id,company_id,user_id,module_key,can_view,can_create,can_edit,can_delete," +
                 "created_at,updated_at,version) VALUES(@id,@c,@u,'materials',1,0,0,0,1,1,1);",
                 ("@id", Guid.NewGuid().ToString("N")), ("@c", CoA), ("@u", okurId));

        _yetkisiz = await _host.LoginAsync("znc_yetkisiz", Pass, CoA, _subeA);
        _yalnizOkur = await _host.LoginAsync("znc_okur", Pass, CoA, _subeA);

        _yetkisizOturum = new SessionContext(yetkisizId, CoA, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        _yalnizOkurOturum = new SessionContext(okurId, CoA, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("materials", true, false, false, false) }));

        // ── B firmasının GİZLİ kaydı ────────────────────────────────────────────────────
        _malzemeB = Guid.NewGuid().ToString("N");
        Calistir("INSERT INTO materials(id,company_id,code,name,unit_id,min_stock,created_at,updated_at,version,is_deleted) " +
                 "VALUES(@id,@c,'B-KOD',@n,NULL,'0',1,1,1,0);",
                 ("@id", _malzemeB), ("@c", CoB), ("@n", GizliMalzeme));
    }

    public Task DisposeAsync() { _host.Dispose(); return Task.CompletedTask; }

    // ══════════════════════ ZN1 — DÖRT HALKA BİRDEN ══════════════════════

    /// <summary>
    /// ⭐ Yetkisiz kullanıcı dört halkanın DÖRDÜNDE de engellenir.
    ///
    /// Tek bir halkanın kapalı olması yetmez: menüde gizlemek arayüz kolaylığıdır, asıl kapı
    /// servis ve API'dir. Bu test dördünü BİRLİKTE kanıtlar.
    /// </summary>
    [Fact]
    public async Task ZN1_Yetkisiz_Kullanici_Dort_Halkada_Da_Engelli()
    {
        // (1) UI kararı — menüde görünmez.
        Assert.False(AccessControl.CanSeeMenu(_yetkisizOturum, "materials"));
        Assert.False(AccessControl.Can(_yetkisizOturum, "materials", PermissionAction.View));

        // (2) API — okuma ve yazma uçları reddeder.
        foreach (var (yontem, yol) in new[]
                 {
                     (HttpMethod.Get,    "/api/materials"),
                     (HttpMethod.Get,    $"/api/materials/{_malzemeA}"),
                     (HttpMethod.Delete, $"/api/materials/{_malzemeA}"),
                 })
        {
            var r = await _yetkisiz.SendAsync(new HttpRequestMessage(yontem, yol));
            YetkiReddi(r, $"{yontem} {yol}");
        }

        var post = await _yetkisiz.PostAsJsonAsync("/api/materials", YeniMalzemeGovdesi("ZN1-KOD", "ZN1"));
        YetkiReddi(post, "POST /api/materials");

        // (3) SERVİS — masaüstünün çevrimdışı yolu da kapalı.
        var materials = new DepoWise.Infrastructure.Materials.MaterialService(_svc.Factory);
        Assert.Throws<ForbiddenException>(() =>
            materials.Create(_yetkisizOturum, new DepoWise.Infrastructure.Materials.NewMaterial("ZN1-S", "ZN1 Servis")));
        Assert.Throws<ForbiddenException>(() => materials.Delete(_yetkisizOturum, _malzemeA));

        // (4) VERİ — reddedilen yazma isteği hiçbir kayıt BIRAKMADI.
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM materials WHERE company_id=@c AND code LIKE 'ZN1%';", ("@c", CoA)));
    }

    // ══════════════════════ ZN2 — "GÖRÜR AMA SİLEMEZ" ══════════════════════

    /// <summary>
    /// ⭐ Yetki ekranının temel vaadi: okuma açık, silme kapalı — ve bu AYRIM dört halkada da tutar.
    ///
    /// En sık yapılan hata burada olur: liste açık olduğu için silme ucunun da açık kalması.
    /// </summary>
    [Fact]
    public async Task ZN2_Gorur_Ama_Silemez_Dort_Halkada_Da_Tutar()
    {
        // (1) UI kararı
        Assert.True(AccessControl.Can(_yalnizOkurOturum, "materials", PermissionAction.View));
        Assert.False(AccessControl.Can(_yalnizOkurOturum, "materials", PermissionAction.Create));
        Assert.False(AccessControl.Can(_yalnizOkurOturum, "materials", PermissionAction.Delete));

        // (2) API — okuma GEÇER
        var liste = await _yalnizOkur.GetAsync("/api/materials");
        Assert.Equal(HttpStatusCode.OK, liste.StatusCode);

        // ...yazma ve silme REDDEDİLİR
        var olustur = await _yalnizOkur.PostAsJsonAsync("/api/materials", YeniMalzemeGovdesi("ZN2-KOD", "ZN2"));
        YetkiReddi(olustur, "POST /api/materials (yalnız okur)");

        var sil = await _yalnizOkur.DeleteAsync($"/api/materials/{_malzemeA}");
        YetkiReddi(sil, "DELETE /api/materials (yalnız okur)");

        // (3) SERVİS — aynı ayrım
        var materials = new DepoWise.Infrastructure.Materials.MaterialService(_svc.Factory);
        materials.List(_yalnizOkurOturum, new DepoWise.Application.Common.PageRequest());   // okuma: istisna ATMAZ
        Assert.Throws<ForbiddenException>(() => materials.Delete(_yalnizOkurOturum, _malzemeA));

        // (4) VERİ — kayıt HÂLÂ duruyor (silme gerçekten olmadı) ve yeni kayıt oluşmadı.
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM materials WHERE company_id=@c AND code LIKE 'ZN2%';", ("@c", CoA)));
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM materials WHERE id=@id AND is_deleted=1;", ("@id", _malzemeA)));
    }

    // ══════════════════════ ZN3 — MENÜYÜ ATLAYARAK DOĞRUDAN ÇAĞRI ══════════════════════

    /// <summary>
    /// ⭐ Menüde ekran görünmüyor diye uç korumasız kalamaz.
    ///
    /// Kullanıcı adresi elle yazabilir ya da API'yi doğrudan çağırabilir. Bu test yetkisiz
    /// kullanıcının, menüsünde HİÇ görünmeyen ekranların uçlarını doğrudan çağırmasını dener.
    /// </summary>
    [Fact]
    public async Task ZN3_Dogrudan_Uc_Cagrisi_Menuyu_Atlayamaz()
    {
        var denenen = new[]
        {
            "/api/materials", "/api/vehicles", "/api/personnel", "/api/branches",
            "/api/maintenance", "/api/audit", "/api/users",
        };

        var sizanlar = new List<string>();
        foreach (var yol in denenen)
        {
            var menudeGorunur = false;
            var modul = yol.Replace("/api/", "");
            if (AppModules.All.Any(m => m.Key == modul))
                menudeGorunur = AccessControl.CanSeeMenu(_yetkisizOturum, modul);

            var r = await _yetkisiz.GetAsync(yol);

            // Menüde görünmeyen bir ekranın ucu 200 + veri dönmemeli.
            if (!menudeGorunur && r.StatusCode == HttpStatusCode.OK)
            {
                var govde = await r.Content.ReadAsStringAsync();
                // Boş liste dönmek KABUL EDİLİR (veri sızmıyor); dolu liste sızıntıdır.
                if (govde.Length > 2 && govde.Contains("\"id\"", StringComparison.Ordinal))
                    sizanlar.Add($"{yol} → 200 + veri");
            }
        }

        Assert.True(sizanlar.Count == 0,
            "Menüde görünmeyen ekranın ucu VERİ döndürdü: " + string.Join(", ", sizanlar));
    }

    // ══════════════════════ ZN4 — VERİ SIZINTISI ══════════════════════

    /// <summary>
    /// ⭐ Durum kodu tek başına kanıt değildir — yanıt GÖVDESİ de kontrol edilir.
    ///
    /// Bir uç 403 dönerken hata mesajında kayıt adını sızdırabilir, ya da 200 dönüp boş görünen
    /// bir gövdede veri taşıyabilir.
    /// </summary>
    [Fact]
    public async Task ZN4_Yetkisiz_Istek_Veri_Sizdirmaz()
    {
        foreach (var istemci in new[] { _yetkisiz, _yalnizOkur })
        {
            foreach (var yol in new[] { "/api/materials", $"/api/materials/{_malzemeB}" })
            {
                var r = await istemci.GetAsync(yol);
                var govde = await r.Content.ReadAsStringAsync();
                Assert.DoesNotContain(GizliMalzeme, govde);
                Assert.DoesNotContain(CoB, govde);
            }
        }
    }

    // ══════════════════════ ZN5 — BUTON YETKİSİ ══════════════════════

    /// <summary>
    /// ⭐ Buton yetkisinde UI kararı ile servis kapısı AYNI kaynaktan beslenmeli.
    ///
    /// İkisi ayrışırsa ya kullanıcı tıklayıp hata alır (yetkisi sanır) ya da butonu göremediği
    /// hâlde API'den yapabilir (gerçek açık).
    /// </summary>
    [Fact]
    public void ZN5_Buton_UI_Karari_Ve_Servis_Kapisi_Ayni()
    {
        foreach (var (buton, _) in SpecialButtons.All)
        {
            // Yetkisiz: UI gizler VE servis reddeder.
            Assert.False(AccessControl.CanUseButton(_yetkisizOturum, buton));
            Assert.Throws<ForbiddenException>(() => AccessControl.RequireButton(_yetkisizOturum, buton));

            // Açıkça verilmiş: UI gösterir VE servis geçirir.
            var izinli = new SessionContext("u-izinli", CoA, new[] { RoleKeys.Staff },
                new PermissionSet(Array.Empty<ModulePermission>(), new[] { buton }));
            Assert.True(AccessControl.CanUseButton(izinli, buton));
            AccessControl.RequireButton(izinli, buton);   // istisna ATMAMALI
        }
    }

    // ══════════════════════ ZN6 — TENANT ══════════════════════

    /// <summary>
    /// ⭐ Tam yetkili A admini bile B firmasının kaydına erişemez.
    ///
    /// Yetki "ne yapabilir", tenant "hangi veride" sorusudur; ikincisi yetkiden bağımsızdır.
    /// ID tahmin ederek erişim denenir (IDOR).
    /// </summary>
    [Fact]
    public async Task ZN6_Yetkili_Admin_Bile_Baska_Firmaya_Erisemez()
    {
        // API üzerinden B'nin kimliğiyle
        var oku = await _admin.GetAsync($"/api/materials/{_malzemeB}");
        var govde = await oku.Content.ReadAsStringAsync();
        Assert.DoesNotContain(GizliMalzeme, govde);

        var sil = await _admin.DeleteAsync($"/api/materials/{_malzemeB}");
        Assert.True(ApiTestHost.IsDenied(sil), $"Başka firmanın kaydı silinebildi: {(int)sil.StatusCode}");

        // VERİ: B'nin kaydı DEĞİŞMEDİ ve SİLİNMEDİ.
        Assert.Equal(1L, Say("SELECT COUNT(*) FROM materials WHERE id=@id AND is_deleted=0;", ("@id", _malzemeB)));
        Assert.Equal(GizliMalzeme, Metin("SELECT name FROM materials WHERE id=@id;", ("@id", _malzemeB)));
    }

    // ══════════════════════ ZN7 — KİMLİK DOĞRULAMASIZ ══════════════════════

    [Fact]
    public async Task ZN7_Kimlik_Dogrulamasiz_Istek_Reddedilir()
    {
        var anon = _host.Anonymous();
        foreach (var yol in new[] { "/api/materials", "/api/vehicles", "/api/personnel", "/api/users" })
        {
            var r = await anon.GetAsync(yol);
            Assert.True(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                $"{yol} kimlik doğrulamasız {(int)r.StatusCode} döndü.");
        }
    }

    // ══════════════════════ ZN8 — DIŞA AKTARIM: İKİ KAPI BİRDEN ══════════════════════

    /// <summary>
    /// ⭐ Dışa aktarım yetkisi TEK BAŞINA veriyi AÇMAZ.
    ///
    /// <b>Denetimde fark edilen yapı:</b> dışa aktarım uçları API katmanında yalnız GENEL
    /// <c>"export"</c> modülünü sorar (<c>/api/personnel/export</c>, <c>/api/inspection/export</c>,
    /// <c>/api/assignments/export</c> …). Tek başına bakıldığında bu bir açık gibi görünür:
    /// "export yetkisi olan herkes personel listesini indirebilir mi?"
    ///
    /// <b>Cevap: hayır</b> — çünkü ucun çağırdığı SERVİS kendi modülünü ayrıca ister
    /// (<c>PersonnelService</c> → <c>personnel/View</c>). Yani iki kapı arka arkayadır ve
    /// <b>ikisinin de açık olması gerekir</b>. Bu bir savunma derinliğidir.
    ///
    /// 🔴 Bu test o ikinci kapıyı MÜHÜRLER. Biri ileride "API zaten kontrol ediyor" diyip servis
    /// çağrısındaki kontrolü kaldırırsa, yalnız <c>export</c> yetkisi verilmiş bir kullanıcı TÜM
    /// personel listesini indirebilir hâle gelir — ve hiçbir mevcut test bunu yakalamaz.
    /// </summary>
    [Fact]
    public async Task ZN8_Export_Yetkisi_Tek_Basina_Veriyi_Acmaz()
    {
        // Yalnız GENEL "export" yetkisi olan personel — personel/muayene modüllerinde izni YOK.
        var kid = _svc.Users.EnsureInitialAdmin(CoA, "znc_export", Pass, RoleKeys.Staff);
        Calistir("INSERT INTO user_permissions(id,company_id,user_id,module_key,can_view,can_create,can_edit,can_delete," +
                 "created_at,updated_at,version) VALUES(@id,@c,@u,'export',1,0,0,0,1,1,1);",
                 ("@id", Guid.NewGuid().ToString("N")), ("@c", CoA), ("@u", kid));
        var istemci = await _host.LoginAsync("znc_export", Pass, CoA, _subeA);

        var oturum = new SessionContext(kid, CoA, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("export", true, false, false, false) }));

        // (1) UI kararı: dışa aktarma yetkisi VAR ama personel ekranı görünmez.
        Assert.True(AccessControl.Can(oturum, "export", PermissionAction.View));
        Assert.False(AccessControl.CanSeeMenu(oturum, "personnel"));

        // (2) API: ilk kapıyı geçer, İKİNCİ kapıda durur → veri inmez.
        foreach (var yol in new[] { "/api/personnel/export", "/api/inspection/export", "/api/assignments/export" })
        {
            var r = await istemci.GetAsync(yol);
            Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
            Assert.True(ApiTestHost.IsDenied(r), $"{yol} → {(int)r.StatusCode}; veri inmemeliydi.");
        }

        // (3) SERVİS: doğrudan çağrı da reddedilir (masaüstünün çevrimdışı yolu).
        Assert.Throws<ForbiddenException>(() => _svc.Personnel.ListAllForExport(oturum));

        // (4) Kontrol: veri modülü DE verilince dışa aktarım gerçekten çalışıyor
        //     (test "her şeyi reddediyor" diye değil, doğru ayrımı yaptığı için geçmeli).
        Calistir("INSERT INTO user_permissions(id,company_id,user_id,module_key,can_view,can_create,can_edit,can_delete," +
                 "created_at,updated_at,version) VALUES(@id,@c,@u,'personnel',1,0,0,0,1,1,1);",
                 ("@id", Guid.NewGuid().ToString("N")), ("@c", CoA), ("@u", kid));
        var ikiliOturum = new SessionContext(kid, CoA, new[] { RoleKeys.Staff },
            new PermissionSet(new[]
            {
                new ModulePermission("export", true, false, false, false),
                new ModulePermission("personnel", true, false, false, false),
            }));
        _svc.Personnel.ListAllForExport(ikiliOturum);   // istisna ATMAMALI
    }

    // ── yardımcılar ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Yetki reddi <b>403</b> (ya da kimliksizse 401) olmalıdır — <b>500 DEĞİL</b>.
    ///
    /// <c>ApiTestHost.IsDenied</c> bilinçli olarak gevşektir (400/401/403/404/500 kabul eder) ve
    /// süpürme testlerinde "veri sızmadı" demek için yeterlidir. Ama yetki SÖZLEŞMESİNİ ölçerken
    /// gevşek olmak tehlikelidir: sunucu hatası (500) da "reddedildi" sayılır ve gerçek bir çökme
    /// yeşil testin arkasında saklanır. Burada kesin kod istenir.
    /// </summary>
    private static void YetkiReddi(HttpResponseMessage r, string ne)
        => Assert.True(r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"{ne} → {(int)r.StatusCode}. Yetki reddi 403 (veya 401) olmalı; 500 sunucu hatasıdır, red değil.");

    private static object YeniMalzemeGovdesi(string kod, string ad) => new
    {
        code = kod, name = ad, type = (string?)null, categoryId = (string?)null, unitId = (string?)null,
        brandId = (string?)null, supplierId = (string?)null, minStock = 0m, unitPrice = 0m,
        description = (string?)null, openingStock = 0m, vehicleIds = (List<string>?)null,
        equivalentIds = (List<string>?)null,
    };

    private void Calistir(string sql, params (string Ad, object Deger)[] p)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (ad, deger) in p) cmd.AddWithValue(ad, deger);
        cmd.ExecuteNonQuery();
    }

    private long Say(string sql, params (string Ad, object Deger)[] p)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (ad, deger) in p) cmd.AddWithValue(ad, deger);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private string? Metin(string sql, params (string Ad, object Deger)[] p)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (ad, deger) in p) cmd.AddWithValue(ad, deger);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : (string)v;
    }
}
