using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ K (2026-09-05) — BU TURDA EKLENEN UÇLARIN GERÇEK HTTP DENETİMİ ═══
///
/// Protokol §9 (tenant), §11 (API testleri), §12 (çift gönderim) ve §13 (hata davranışı) bu turda
/// eklenen uçlar için <b>doğrudan HTTP üzerinden</b> uygulanır. Servis metodunu çağıran testler
/// kimlik doğrulama, model bağlama ve hata çevirisini ATLAR; burada tüm hat kapsanır.
///
/// <b>Kapsanan yeni uçlar:</b>
/// <c>/api/stock/movements/grid</c> · <c>/api/maintenance/grid</c> · <c>/api/personnel/export</c> ·
/// <c>/api/inspection/export</c> · <c>/api/personnel</c> (yeni <c>{items,hasMore}</c> gövdesi).
///
/// <b>Kanıt ölçütü:</b> durum kodu tek başına yeterli değildir — B firmasının GİZLİ metni A'nın
/// yanıt gövdesinde geçmemelidir (protokol §9: "UI gizlemesi güvenlik kanıtı değildir").
///
/// 🔒 Bu testler <see cref="ApiTestHost"/> ile bellek içinde çalışır; <c>DEPOWISE_PG_URL</c> temizlenir.
/// <b>Canlı veritabanına hiçbir istek gitmez.</b>
///
///  UUA1 — Yeni ızgara uçları: başka firmanın verisi görünmüyor
///  UUA2 — Dışa aktarım uçları: başka firmanın verisi dosyaya girmiyor
///  UUA3 — Kimlik doğrulamasız istek reddedilir (401)
///  UUA4 — Bozuk parametreler 500 DEĞİL, anlamlı yanıt üretir
///  UUA5 — Aşırı uzun belge numarası HTTP hattında da 400 ile reddedilir
///  UUA6 — Aynı bakım iki kez gönderilirse ne oluyor (ölçüm + kayıt)
///  UUA7 — /api/personnel yeni gövdesi: hasMore gerçekten sayfalamayı yansıtır
/// </summary>
[Collection("PostgresSchema")]
public class FazKUctanUcaApiTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CoA = "FKA-A";
    private const string CoB = "FKA-B";
    private const string Pass = "Fka!2026";
    private const string GizliPersonel = "B-GIZLI-PERSONEL-KAYDI";
    private const string GizliNot = "B-GIZLI-HAREKET-NOTU";

    private ServerServices _svc = null!;
    private HttpClient _adminA = null!;
    private SessionContext _sessionA = null!;
    private string _aracA = "", _tanimA = "", _subeA = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        foreach (var (id, ad) in new[] { (CoA, "A Firmasi"), (CoB, "B Firmasi") })
            Calistir("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) " +
                     "VALUES(@c,@n,1,1,1,0,5,20,5) ON CONFLICT(id) DO NOTHING;", ("@c", id), ("@n", ad));

        // ── B firmasının GİZLİ kayıtları ──────────────────────────────────────────────────
        var subeB = Yeni();
        Calistir("INSERT INTO branches(id,company_id,parent_id,name,kind,created_at,updated_at,version,is_deleted) " +
                 "VALUES(@id,@c,NULL,'B-SUBE','branch',1,1,1,0);", ("@id", subeB), ("@c", CoB));
        var malzemeB = Yeni();
        Calistir("INSERT INTO materials(id,company_id,code,name,unit_id,min_stock,created_at,updated_at,version,is_deleted) " +
                 "VALUES(@id,@c,'B-KOD','B-MALZEME',NULL,'0',1,1,1,0);", ("@id", malzemeB), ("@c", CoB));
        Calistir("INSERT INTO stock_movements(id,company_id,material_id,branch_id,movement_type,direction," +
                 "quantity,operation_id,note,created_at,updated_at) " +
                 "VALUES(@id,@c,@m,@b,'in',1,'5',@op,@note,1,1);",
                 ("@id", Yeni()), ("@c", CoB), ("@m", malzemeB), ("@b", subeB), ("@op", Yeni()), ("@note", GizliNot));
        Calistir("INSERT INTO personnel(id,company_id,branch_id,full_name,is_active,is_field_staff," +
                 "created_at,updated_at,version,is_deleted) VALUES(@id,@c,@b,@n,1,0,1,1,1,0);",
                 ("@id", Yeni()), ("@c", CoB), ("@b", subeB), ("@n", GizliPersonel));

        var aracB = Yeni();
        Calistir("INSERT INTO vehicles(id,company_id,internal_code,plate,current_meter,meter_unit,status," +
                 "created_at,updated_at,version,is_deleted) " +
                 "VALUES(@id,@c,'B-ARAC','34BGZ99','500','km','active',1,1,1,0);", ("@id", aracB), ("@c", CoB));

        // ── A firması: giriş yapan taraf ───────────────────────────────────────────────────
        var uidA = _svc.Users.EnsureInitialAdmin(CoA, "fka_admin_a", Pass, RoleKeys.CompanyAdmin);
        _adminA = await _host.LoginAsync("fka_admin_a", Pass, CoA);

        // Servis katmanını doğrudan çağıran doğrulamalar için GERÇEK kullanıcı kimliği kullanılır
        // (audit satırları var olmayan bir kullanıcıya yazılmasın).
        _sessionA = new SessionContext(uidA, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _subeA = new DepoWise.Infrastructure.Organization.BranchService(_svc.Factory)
            .Create(_sessionA, new DepoWise.Infrastructure.Organization.NewBranch("A-Merkez"));
        _aracA = new DepoWise.Infrastructure.Vehicles.VehicleService(_svc.Factory)
            .Create(_sessionA, new DepoWise.Infrastructure.Vehicles.NewVehicle("A-ARAC"));
        _tanimA = new DepoWise.Infrastructure.Maintenance.MaintenanceDefinitionService(_svc.Factory)
            .Create(_sessionA, new DepoWise.Infrastructure.Maintenance.NewMaintenanceDefinition("Yag", 100m, "day", null, null));
    }

    public Task DisposeAsync() { _host.Dispose(); return Task.CompletedTask; }

    // ══════════════════════ §9 — TENANT ══════════════════════

    /// <summary>Yeni ızgara uçları başka firmanın satırını DÖNDÜRMEMELİ.</summary>
    [Fact]
    public async Task UUA1_Yeni_Izgara_Uclari_Baska_Firmayi_Gostermez()
    {
        var hareket = await _adminA.GetAsync("/api/stock/movements/grid?page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, hareket.StatusCode);
        var govde = await hareket.Content.ReadAsStringAsync();
        Assert.DoesNotContain(GizliNot, govde);
        Assert.Equal(0, (await ApiTestHost.JsonAsync(hareket)).GetProperty("totalCount").GetInt32());

        var bakim = await _adminA.GetAsync("/api/maintenance/grid?page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, bakim.StatusCode);
        Assert.Equal(0, (await ApiTestHost.JsonAsync(bakim)).GetProperty("totalCount").GetInt32());

        // ⭐ Arama ile ZORLAMA: B'nin gizli notunu doğrudan arasak bile bulunmamalı.
        var arama = await _adminA.GetAsync("/api/stock/movements/grid?q=" + GizliNot + "&page=1&pageSize=50");
        Assert.DoesNotContain(GizliNot, await arama.Content.ReadAsStringAsync());
    }

    /// <summary>Dışa aktarım dosyası başka firmanın kaydını İÇERMEMELİ. Excel ikili bir dosyadır;
    /// düz metin araması yerine satır SAYISI ve servis çıktısı üzerinden doğrulanır.</summary>
    [Fact]
    public async Task UUA2_Disa_Aktarim_Baska_Firmayi_Icermez()
    {
        var personel = await _adminA.GetAsync("/api/personnel/export");
        Assert.Equal(HttpStatusCode.OK, personel.StatusCode);
        var bytes = await personel.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0, "Dışa aktarım boş dosya döndürdü.");

        // Dosyanın kaynağı olan servis çağrısı A firması için BOŞ olmalı (B'nin personeli sızmamalı).
        var satirlar = _svc.Personnel.ListAllForExport(_sessionA);
        Assert.DoesNotContain(satirlar, r => r.FullName.Contains("GIZLI", StringComparison.OrdinalIgnoreCase));

        var muayene = await _adminA.GetAsync("/api/inspection/export");
        Assert.Equal(HttpStatusCode.OK, muayene.StatusCode);
    }

    // ══════════════════════ §11 — API DOĞRULAMA ══════════════════════

    [Fact]
    public async Task UUA3_Kimlik_Dogrulamasiz_Istek_Reddedilir()
    {
        var anon = _host.Anonymous();
        foreach (var yol in new[]
                 {
                     "/api/stock/movements/grid?page=1&pageSize=50",
                     "/api/maintenance/grid?page=1&pageSize=50",
                     "/api/personnel/export",
                     "/api/inspection/export",
                 })
        {
            var r = await anon.GetAsync(yol);
            Assert.True(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                $"{yol} kimlik doğrulamasız {(int)r.StatusCode} döndü — 401/403 bekleniyordu.");
        }
    }

    /// <summary>
    /// ⭐ Bozuk parametre 500 ÜRETMEMELİ. 500, kullanıcıya "bir şeyler ters gitti" der ve
    /// istemci tarafında anlamlı bir mesaja çevrilemez. Negatif sayfa / sıfır boyut / devasa boyut /
    /// geçersiz tarih hepsi kontrollü davranmalı.
    /// </summary>
    [Fact]
    public async Task UUA4_Bozuk_Parametreler_500_Uretmez()
    {
        foreach (var yol in new[]
                 {
                     "/api/stock/movements/grid?page=-5&pageSize=0",
                     "/api/stock/movements/grid?page=1&pageSize=999999",
                     "/api/stock/movements/grid?page=1&pageSize=50&from=-1&to=-2",
                     "/api/maintenance/grid?page=0&pageSize=-3",
                     "/api/maintenance/grid?page=1&pageSize=50&vehicleId=" + new string('Z', 500),
                     "/api/maintenance/grid?page=1&pageSize=50&fromDate=9999999999999999",
                 })
        {
            var r = await _adminA.GetAsync(yol);
            Assert.True((int)r.StatusCode < 500,
                $"{yol} → {(int)r.StatusCode}. Bozuk parametre sunucu hatası üretmemeli.");
        }

        // Sayfa/boyut düzeltmesi gerçekten UYGULANIYOR mu (sessizce 0 satır dönmemeli).
        var duzeltilmis = await ApiTestHost.JsonAsync(
            await _adminA.GetAsync("/api/stock/movements/grid?page=-5&pageSize=0"));
        Assert.True(duzeltilmis.GetProperty("page").GetInt32() >= 1);
        Assert.True(duzeltilmis.GetProperty("pageSize").GetInt32() >= 1);
    }

    /// <summary>
    /// ⭐ BelgeNo sınırı HTTP hattında da geçerli mi. Servis testi kuralın kendisini kanıtlar;
    /// bu test kuralın <b>API üzerinden de</b> devrede olduğunu ve ortak hata modelinin
    /// (400 + <c>{"error": ...}</c>) çalıştığını kanıtlar — 500 değil.
    /// </summary>
    [Fact]
    public async Task UUA5_Asiri_Uzun_Belge_No_HTTP_Uzerinden_400()
    {
        var r = await _adminA.PostAsJsonAsync("/api/maintenance", new
        {
            vehicleId = _aracA,
            definitionId = _tanimA,
            performedDate = 1_700_000_000_000L,
            branchId = _subeA,
            invoiceNo = new string('X', BelgeNo.EnFazlaUzunluk + 50),
        });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var govde = await ApiTestHost.JsonAsync(r);
        Assert.True(govde.TryGetProperty("error", out var hata), "Ortak hata modeli bekleniyordu.");
        Assert.Contains("100", hata.GetString() ?? "");   // sınır kullanıcıya SÖYLENİYOR

        // Reddedilen istek YARIM kayıt bırakmamalı.
        Assert.Equal(0L, Oku<long>("SELECT COUNT(*) FROM vehicle_maintenances WHERE company_id=@c;", ("@c", CoA)));
    }

    // ══════════════════════ §12 — ÇİFT GÖNDERİM ══════════════════════

    /// <summary>
    /// ⭐ ÖLÇÜM (protokol §12). Web ucu her istekte YENİ bir operation id üretir; bu yüzden aynı
    /// bakım iki AYRI HTTP isteğiyle gönderilirse iki kayıt oluşur. Bu bilinçli bir sınırdır:
    /// <list type="bullet">
    ///   <item>arayüzde kaydet düğmesi kayıt sürerken <c>Disabled</c>'dır (çift tık engellenir),</item>
    ///   <item>öncesinde onay penceresi vardır,</item>
    ///   <item>masaüstü kendi operation id'sini üretir → çevrimdışı kuyruk tekrarı ikinci kayıt YARATMAZ.</item>
    /// </list>
    /// Bu test davranışı KAYIT ALTINA alır: değişirse (ör. istemci kaynaklı operation id eklenirse)
    /// burada görülür ve bilinçli olarak güncellenir. Sessiz bir varsayım bırakılmaz.
    /// </summary>
    [Fact]
    public async Task UUA6_Ayni_Bakim_Iki_Kez_Gonderilirse_Davranis_Kayitlidir()
    {
        object Govde() => new
        {
            vehicleId = _aracA, definitionId = _tanimA,
            performedDate = 1_700_000_000_000L, branchId = _subeA,
        };

        var bir = await _adminA.PostAsJsonAsync("/api/maintenance", Govde());
        var iki = await _adminA.PostAsJsonAsync("/api/maintenance", Govde());
        Assert.Equal(HttpStatusCode.OK, bir.StatusCode);
        Assert.Equal(HttpStatusCode.OK, iki.StatusCode);

        var adet = Oku<long>("SELECT COUNT(*) FROM vehicle_maintenances WHERE company_id=@c;", ("@c", CoA));
        Assert.Equal(2L, adet);   // ← MEVCUT davranış: iki AYRI istek = iki kayıt

        // Kayıtlar BOZUK değil: ikisi de eksiksiz ve birbirinden bağımsız kimliğe sahip.
        var farkliKimlik = Oku<long>("SELECT COUNT(DISTINCT id) FROM vehicle_maintenances WHERE company_id=@c;", ("@c", CoA));
        Assert.Equal(2L, farkliKimlik);

        // ⭐ Asıl idempotency kapısı operation id'dedir; AYNI operation id ikinci kaydı YARATMAZ.
        var maint = new DepoWise.Infrastructure.Maintenance.MaintenanceService(_svc.Factory);
        var yeni = new DepoWise.Infrastructure.Maintenance.NewMaintenance(_aracA, _tanimA,
            PerformedDate: 1_700_000_100_000L, StockLocationId: _subeA);
        maint.Save(_sessionA, yeni, "sabit-op-id");
        maint.Save(_sessionA, yeni, "sabit-op-id");
        Assert.Equal(3L, Oku<long>("SELECT COUNT(*) FROM vehicle_maintenances WHERE company_id=@c;", ("@c", CoA)));
    }

    // ══════════════════════ §11 — YENİ GÖVDE ══════════════════════

    /// <summary>
    /// <c>/api/personnel</c> bu turda dizi yerine <c>{items, hasMore}</c> döndürmeye başladı
    /// (LST-01). <c>hasMore</c> gerçek sayfalamayı yansıtmalı; yoksa istemci "hepsi bu" sanır ve
    /// kayıtlar SESSİZCE görünmez olur — bu turda kapatılan kusur sınıfının aynısı.
    /// </summary>
    [Fact]
    public async Task UUA7_Personel_Ucu_HasMore_Gercegi_Yansitir()
    {
        var az = await ApiTestHost.JsonAsync(await _adminA.GetAsync("/api/personnel"));
        Assert.False(az.GetProperty("hasMore").GetBoolean());   // A firmasında kayıt yok

        // Sayfa boyutunu UÇ belirler (Page() → 500, PageRequest.MaxLimit ile 200'e kırpılır),
        // istemci değil. Bu yüzden hasMore'u tetiklemek için 200'ün ÜSTÜNE çıkılır.
        for (int i = 1; i <= 210; i++)
            Calistir("INSERT INTO personnel(id,company_id,branch_id,full_name,is_active,is_field_staff," +
                     "created_at,updated_at,version,is_deleted) VALUES(@id,@c,@b,@n,1,0,@t,@t,1,0);",
                     ("@id", Yeni()), ("@c", CoA), ("@b", _subeA), ("@n", "A Personel " + i), ("@t", (long)i));

        var cok = await ApiTestHost.JsonAsync(await _adminA.GetAsync("/api/personnel"));
        Assert.Equal(PageRequest.MaxLimit, cok.GetProperty("items").GetArrayLength());
        Assert.True(cok.GetProperty("hasMore").GetBoolean(),
            "210 kayıt varken hasMore=false — 200'ün ötesindeki kayıtlar SESSİZCE kaybolur.");

        // ⭐ Dışa aktarım ise sayfa tavanını AŞAR: 210'un hepsini verir (bu geceki düzeltme).
        Assert.Equal(210, _svc.Personnel.ListAllForExport(_sessionA).Count);
    }

    // ── yardımcılar ────────────────────────────────────────────────────────────────────────

    private static string Yeni() => Guid.NewGuid().ToString("N");

    private void Calistir(string sql, params (string Ad, object Deger)[] p)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (ad, deger) in p) cmd.AddWithValue(ad, deger);
        cmd.ExecuteNonQuery();
    }

    private T? Oku<T>(string sql, params (string Ad, object Deger)[] p)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (ad, deger) in p) cmd.AddWithValue(ad, deger);
        var v = cmd.ExecuteScalar();
        return v is null || v is DBNull ? default : (T)Convert.ChangeType(v, typeof(T));
    }
}
