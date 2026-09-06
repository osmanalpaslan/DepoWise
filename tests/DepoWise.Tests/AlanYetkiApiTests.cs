using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 3b-5 — ALAN YETKİSİNİN GERÇEK HTTP HATTINDA SINANMASI ═══
///
/// <b>Kullanıcı şartı §8 (Senaryo E):</b> <i>"UI'ın gizlemesi güvenlik kanıtı değildir."</i>
/// Bu yüzden her senaryo arayüz olmadan, doğrudan uç çağrısıyla ölçülür.
///
/// <b>Kullanıcı şartı §34:</b> yetki reddi <b>403 (veya 401)</b> olmalıdır; <b>500 TEST HATASIDIR</b>.
/// <see cref="YetkiReddi"/> bunu açıkça uygular.
///
/// 🔒 <see cref="ApiTestHost"/> bellek içinde çalışır, <c>DEPOWISE_PG_URL</c> temizlenir → canlı
/// veritabanına hiçbir istek gitmez. Parolalar bu testin kendi oluşturduğu hesaplara aittir.
///
///  AA1 — Senaryo A (tam yetkili): alan yanıtta VAR, yazılabilir
///  AA2 — Senaryo C (göremeyen): alan yanıtta HİÇ YOK — liste · kart · özet
///  AA3 — 🔴 Senaryo C: FİLTRE ile gizli alan daraltılamaz
///  AA4 — 🔴 Senaryo C: SIRALAMA ile gizli alan sızdırılamaz
///  AA5 — 🔴 Senaryo C: EXPORT'ta kolon yok (başlıkla birlikte düşer)
///  AA6 — 🔴 Senaryo C: ön muhasebe RAPORU 403 (500 değil)
///  AA7 — Senaryo B (görür, düzenleyemez): PUT ile değiştirme 403, veri BOZULMAZ
///  AA8 — Senaryo C: PUT ile alan gönderilse de kayıttaki değer KORUNUR (veri kaybı yok)
///  AA9 — /api/field-protections yetkisiz kullanıcıya kapalı (okuma ve yazma)
///  AA10 — /api/field-access sunucunun kararını döner; iki platform aynı kaynağı okur
///  AA11 — /api/modules korumalı alanı fieldItem olarak listeler; koruma yokken HİÇ listelemez
///  AA12 — Senaryo D: ROL üzerinden verilen alan izni HTTP'de de açar; geri alınınca kapanır
///  AA13 — 🔴 FAZ 3c: STOK HAREKETİ ucundan da fiyat sızmaz (kaçak kanal)
/// </summary>
[Collection("PostgresSchema")]
public class AlanYetkiApiTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Co = "ALA";
    private const string Pass = "Ala!2026";

    private ServerServices _svc = null!;
    private HttpClient _admin = null!, _personel = null!;
    private string _personelId = "", _malzemeId = "", _subeId = "";

    private static readonly string FiyatAnahtari =
        FieldAccess.Key(FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice);

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        Calistir("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) " +
                 "VALUES(@c,'ALA Firma',1,1,1,0,5,20,5) ON CONFLICT(id) DO NOTHING;", ("@c", Co));

        var adminId = _svc.Users.EnsureInitialAdmin(Co, "ala_admin", Pass, RoleKeys.CompanyAdmin);
        _admin = await _host.LoginAsync("ala_admin", Pass, Co);
        var adminOturum = new SessionContext(adminId, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        // Personel "Tüm Şubeler" ile giremez → çalışma şubesi gerekir (mevcut oturum kuralı).
        _subeId = new DepoWise.Infrastructure.Organization.BranchService(_svc.Factory)
            .Create(adminOturum, new DepoWise.Infrastructure.Organization.NewBranch("ALA-Merkez"));

        _personelId = _svc.Users.EnsureInitialAdmin(Co, "ala_personel", Pass, RoleKeys.Staff);

        // Personele malzeme modülünün tamamı verilir; ALAN yetkisi VERİLMEZ.
        _svc.Permissions.SaveForUser(SuperAdmin(), _personelId,
            new[] { Tam("materials"), Tam("export"), Tam("reports"), Tam("parties"), Tam("invoices"), Tam("finance") },
            Array.Empty<string>());
        _personel = await _host.LoginAsync("ala_personel", Pass, Co, _subeId);

        var mat = new MaterialService(_svc.Factory);
        _malzemeId = mat.Create(adminOturum, new NewMaterial("ALA-1", "ALA Malzeme", UnitPrice: 250.75m));
    }

    public Task DisposeAsync() { _host.Dispose(); return Task.CompletedTask; }

    // ── yardımcılar ─────────────────────────────────────────────────────────────────────────

    private static SessionContext SuperAdmin()
        => new("sa", Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);

    private static ModulePermission Tam(string m) => new(m, true, true, true, true);

    /// <summary>⭐ Yetki reddi 403 (veya 401) OLMALI. 500 = TEST BAŞARISIZ (kullanıcı şartı §34).</summary>
    private static void YetkiReddi(HttpResponseMessage r, string ne)
    {
        Assert.False(r.StatusCode == HttpStatusCode.InternalServerError,
            $"{ne} → 500. Yetki reddi ASLA 500 olmamalı; 403/401 bekleniyor.");
        Assert.True(r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"{ne} → {(int)r.StatusCode}. Yetki reddi 403 (veya 401) olmalı.");
    }

    private void Calistir(string sql, params (string Ad, object Deger)[] p)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (ad, deger) in p) cmd.AddWithValue(ad, deger);
        cmd.ExecuteNonQuery();
    }

    private decimal HamFiyat()
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT unit_price FROM materials WHERE id=@i;";
        cmd.AddWithValue("@i", _malzemeId);
        return DepoWise.Application.Common.Money.Parse(cmd.ExecuteScalar() as string);
    }

    private void FiyatiKoru(bool korumali = true)
        => _svc.FieldProtections.Set(SuperAdmin(), FieldProtectionCatalog.Materials,
            FieldProtectionCatalog.UnitPrice, korumali);

    private async Task<HttpClient> TazePersonel() => await _host.LoginAsync("ala_personel", Pass, Co, _subeId);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    /// <summary>Bir JSON nesnesinde alan VAR MI? (Gizli alan "null" değil, HİÇ olmamalı.)</summary>
    private static bool AlanVar(JsonElement e, string ad) => e.TryGetProperty(ad, out _);

    // ══════════════════ AA1 — SENARYO A ══════════════════

    [Fact]
    public async Task AA1_Tam_Yetkili_Alani_Gorur_Ve_Yazar()
    {
        // Koruma YOK → herkes görür (bugünkü davranış).
        var kart = await Json(await _personel.GetAsync($"/api/materials/{_malzemeId}"));
        Assert.True(AlanVar(kart, "unitPrice"), "Korumasızken birim fiyat yanıtta OLMALI.");
        Assert.Equal(250.75m, kart.GetProperty("unitPrice").GetDecimal());

        var grid = await Json(await _personel.GetAsync("/api/materials/grid?page=1&pageSize=25"));
        Assert.True(AlanVar(grid.GetProperty("summary"), "stockValue"));
        Assert.True(AlanVar(grid.GetProperty("items")[0], "unitPrice"));
    }

    // ══════════════════ AA2 — SENARYO C ══════════════════

    /// <summary>
    /// ⭐ Kullanıcı şartı K2: gizli alan yanıta <b>null olarak da konmaz — HİÇ konmaz</b>.
    /// Üç kanal birden ölçülür: hızlı arama listesi · ızgara · kart. Ayrıca fiyattan TÜREYEN
    /// "stok değeri" özeti de yanıttan çıkmalıdır.
    /// </summary>
    [Fact]
    public async Task AA2_Goremeyen_Kullanicinin_Yanitinda_Alan_Hic_Yok()
    {
        FiyatiKoru();
        var c = await TazePersonel();

        var liste = await Json(await c.GetAsync("/api/materials"));
        Assert.False(AlanVar(liste[0], "unitPrice"), "Gizli alan listede YANITTA OLMAMALI.");
        Assert.True(AlanVar(liste[0], "code"), "Diğer alanlar etkilenmemeli.");

        var grid = await Json(await c.GetAsync("/api/materials/grid?page=1&pageSize=25"));
        Assert.False(AlanVar(grid.GetProperty("items")[0], "unitPrice"));
        Assert.False(AlanVar(grid.GetProperty("summary"), "stockValue"));   // türev değer de yok
        Assert.True(AlanVar(grid.GetProperty("summary"), "criticalCount")); // diğer kutular durur

        var kart = await Json(await c.GetAsync($"/api/materials/{_malzemeId}"));
        Assert.False(AlanVar(kart, "unitPrice"));
        Assert.True(AlanVar(kart, "code"));

        // 🔴 Veri yerinde: gizlenen yalnız GÖRÜNÜM.
        Assert.Equal(250.75m, HamFiyat());
    }

    // ══════════════════ AA3–AA5 — ÇIKARIM KANALLARI (HTTP) ══════════════════

    /// <summary>🔴 "Birim fiyat = 250,75" filtresiyle gizli değer daraltılabilseydi gizleme sahte olurdu.</summary>
    [Fact]
    public async Task AA3_Filtre_Ile_Gizli_Alan_Daraltilamaz()
    {
        var mat = new MaterialService(_svc.Factory);
        mat.Create(SuperAdmin(), new NewMaterial("ALA-2", "İkinci", UnitPrice: 9999m));

        // Koruma YOKKEN filtre gerçekten çalışıyor (ölçüm — varsayım değil).
        var acik = await Json(await _personel.GetAsync("/api/materials/grid?page=1&pageSize=25&unitPrice=9999"));
        Assert.Equal(1, acik.GetProperty("totalCount").GetInt32());

        FiyatiKoru();
        var c = await TazePersonel();
        var kapali = await Json(await c.GetAsync("/api/materials/grid?page=1&pageSize=25&unitPrice=9999"));
        Assert.Equal(2, kapali.GetProperty("totalCount").GetInt32());   // filtre düştü → bilgi sızmadı
    }

    [Fact]
    public async Task AA4_Siralama_Ile_Gizli_Alan_Sizdirilamaz()
    {
        var mat = new MaterialService(_svc.Factory);
        mat.Create(SuperAdmin(), new NewMaterial("ALA-Z", "Ucuz", UnitPrice: 1m));   // koda göre SON

        var acik = await Json(await _personel.GetAsync("/api/materials/grid?page=1&pageSize=25&sort=unitPrice"));
        Assert.Equal("ALA-Z", acik.GetProperty("items")[0].GetProperty("code").GetString());

        FiyatiKoru();
        var c = await TazePersonel();
        var kapali = await Json(await c.GetAsync("/api/materials/grid?page=1&pageSize=25&sort=unitPrice"));
        Assert.Equal("ALA-1", kapali.GetProperty("items")[0].GetProperty("code").GetString());   // koda düştü
    }

    /// <summary>🔴 Excel'de boş kolon bırakmak "gizleme" değildir; kolon BAŞLIĞIYLA düşmelidir.</summary>
    [Fact]
    public async Task AA5_Export_Kolonu_Basligiyla_Birlikte_Duser()
    {
        var acikBytes = await (await _personel.GetAsync("/api/materials/grid/export")).Content.ReadAsByteArrayAsync();
        Assert.True(acikBytes.Length > 0);

        FiyatiKoru();
        var c = await TazePersonel();
        var r = await c.GetAsync("/api/materials/grid/export");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var kapaliBytes = await r.Content.ReadAsByteArrayAsync();
        Assert.True(kapaliBytes.Length > 0);

        // Kolon düştüğü için içerik FARKLI olmalı (aynı olsaydı süzme hiç uygulanmamış demektir).
        Assert.False(acikBytes.AsSpan().SequenceEqual(kapaliBytes),
            "Export korumadan etkilenmedi — kolon düşmemiş olabilir.");
    }

    /// <summary>🔴 Ön muhasebe raporları tutarları SQL'den okur; kapı olmasa rapor kaçak olurdu.
    /// Reddin 500 DEĞİL 403 olması ayrıca ölçülür (kullanıcı şartı §34).</summary>
    [Fact]
    public async Task AA6_On_Muhasebe_Raporu_Yetkisizken_403_Doner()
    {
        _svc.FieldProtections.Set(SuperAdmin(), FieldProtectionCatalog.Parties, FieldProtectionCatalog.Balance, true);
        var c = await TazePersonel();

        // Rapor ucu: POST /api/reports/{type}. Yetkili yönetici için ÇALIŞTIĞI de ölçülür ki
        // "her koşulda 403" gibi sahte bir yeşil elde edilmesin.
        var ok = await _admin.PostAsJsonAsync("/api/reports/acc-balances", new { });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var r = await c.PostAsJsonAsync("/api/reports/acc-balances", new { });
        YetkiReddi(r, "POST /api/reports/acc-balances");
    }

    // ══════════════════ AA7–AA8 — YAZMA YOLU (HTTP) ══════════════════

    /// <summary>Senaryo B: alanı GÖRÜR ama DÜZENLEYEMEZ. Değeri değiştirmeye çalışırsa 403 alır ve
    /// kayıttaki değer BOZULMAZ.</summary>
    [Fact]
    public async Task AA7_Goren_Ama_Duzenleyemeyen_Degistiremez()
    {
        FiyatiKoru();
        _svc.Permissions.SaveForUser(SuperAdmin(), _personelId,
            new[] { Tam("materials"), new ModulePermission(FiyatAnahtari, true, false, false, false) },
            Array.Empty<string>());
        var c = await TazePersonel();

        var kart = await Json(await c.GetAsync($"/api/materials/{_malzemeId}"));
        Assert.Equal(250.75m, kart.GetProperty("unitPrice").GetDecimal());   // görür

        var r = await c.PutAsJsonAsync($"/api/materials/{_malzemeId}", new
        {
            code = "ALA-1", name = "ALA Malzeme", minStock = 0m, unitPrice = 1m,
        });
        YetkiReddi(r, "PUT /api/materials (düzenleme yetkisi olmayan alan)");
        Assert.Equal(250.75m, HamFiyat());   // veri BOZULMADI
    }

    /// <summary>🔴 Senaryo C: alanı GÖREMEYEN kullanıcı kaydı güncellerse fiyat SIFIRLANMAMALI.
    /// Bu, "sessiz veri kaybı" sınıfının en tehlikeli örneğidir.</summary>
    [Fact]
    public async Task AA8_Goremeyen_Kullanici_Kaydederse_Deger_Korunur()
    {
        FiyatiKoru();
        var c = await TazePersonel();

        var r = await c.PutAsJsonAsync($"/api/materials/{_malzemeId}", new
        {
            code = "ALA-1", name = "ALA yeni ad", minStock = 0m, unitPrice = 0m,
        });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(250.75m, HamFiyat());   // 🔴 KORUNDU
    }

    // ══════════════════ AA9–AA11 — YÖNETİM UÇLARI ══════════════════

    [Fact]
    public async Task AA9_Koruma_Ucu_Yetkisize_Kapali()
    {
        YetkiReddi(await _personel.GetAsync("/api/field-protections"), "GET /api/field-protections");

        var yaz = await _personel.PostAsJsonAsync("/api/field-protections", new
        {
            screenKey = FieldProtectionCatalog.Materials,
            fieldKey = FieldProtectionCatalog.UnitPrice,
            isProtected = true,
        });
        YetkiReddi(yaz, "POST /api/field-protections");

        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM field_protections;";
        Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));

        // Yetkili yönetici yapabilir.
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync("/api/field-protections")).StatusCode);
    }

    /// <summary>
    /// ⭐ Kullanıcı şartı §9: web ve masaüstü AYNI kararı kullanmalı. Web kararı bu uçtan okur;
    /// masaüstü <c>FieldAccess</c>'i doğrudan çağırır. Test ikisinin AYNI sonucu verdiğini ölçer.
    /// </summary>
    [Fact]
    public async Task AA10_Field_Access_Ucu_Sunucunun_Kararini_Doner()
    {
        FiyatiKoru();
        var c = await TazePersonel();

        var liste = await Json(await c.GetAsync("/api/field-access"));
        var satir = liste.EnumerateArray().Single(x =>
            x.GetProperty("key").GetString() == FieldProtectionCatalog.Materials + "." + FieldProtectionCatalog.UnitPrice);

        Assert.False(satir.GetProperty("canView").GetBoolean());
        Assert.False(satir.GetProperty("canEdit").GetBoolean());

        // MASAÜSTÜ YOLU (API'siz): aynı oturum, aynı fonksiyon → AYNI sonuç.
        var oturum = _svc.Auth.CreateSessionForUser(Co, _personelId)!;
        Assert.Equal(satir.GetProperty("canView").GetBoolean(),
            FieldAccess.Gorunur(oturum, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));
        Assert.Equal(satir.GetProperty("canEdit").GetBoolean(),
            FieldAccess.Duzenlenebilir(oturum, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));
    }

    [Fact]
    public async Task AA11_Modules_Ucu_Korumali_Alani_FieldItem_Olarak_Listeler()
    {
        var once = await Json(await _admin.GetAsync("/api/modules"));
        Assert.DoesNotContain(once.EnumerateArray(), x => x.GetProperty("key").GetString() == FiyatAnahtari);

        FiyatiKoru();
        var tazeAdmin = await _host.LoginAsync("ala_admin", Pass, Co);
        var sonra = await Json(await tazeAdmin.GetAsync("/api/modules"));

        var satir = sonra.EnumerateArray().Single(x => x.GetProperty("key").GetString() == FiyatAnahtari);
        Assert.True(satir.GetProperty("fieldItem").GetBoolean());
        Assert.Equal("Malzeme & Stok", satir.GetProperty("group").GetString());
        Assert.DoesNotContain("fld_", satir.GetProperty("label").GetString());
    }

    // ══════════════════ AA12 — SENARYO D (ROL) ══════════════════

    [Fact]
    public async Task AA12_Rol_Uzerinden_Alan_Izni_Http_De_Calisir()
    {
        FiyatiKoru();

        var ver = await _admin.PostAsJsonAsync($"/api/permissions/role/{RoleKeys.Staff}", new
        {
            modules = new[] { new { moduleKey = FiyatAnahtari, canView = true, canCreate = false, canEdit = false, canDelete = false } },
            buttons = Array.Empty<string>(),
        });
        Assert.Equal(HttpStatusCode.OK, ver.StatusCode);

        var c = await TazePersonel();
        var kart = await Json(await c.GetAsync($"/api/materials/{_malzemeId}"));
        Assert.True(AlanVar(kart, "unitPrice"), "Rol üzerinden verilen izin HTTP'de açmalı.");
        Assert.Equal(250.75m, kart.GetProperty("unitPrice").GetDecimal());

        // Geri alınınca ANINDA kapanır (yeniden login gerekmeden etkin yetki değişir).
        var geri = await _admin.PostAsJsonAsync($"/api/permissions/role/{RoleKeys.Staff}", new
        {
            modules = Array.Empty<object>(),
            buttons = Array.Empty<string>(),
        });
        Assert.Equal(HttpStatusCode.OK, geri.StatusCode);

        var oturum = _svc.Auth.CreateSessionForUser(Co, _personelId)!;
        Assert.False(FieldAccess.Gorunur(oturum, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));

        var sonra = await Json(await c.GetAsync($"/api/materials/{_malzemeId}"));
        Assert.False(AlanVar(sonra, "unitPrice"));
    }

    // ══════════════════ AA13 — FAZ 3c: KAÇAK KANAL (HTTP) ══════════════════

    /// <summary>
    /// 🔴 FAZ 3c: birim fiyat korumalıyken kullanıcı aynı fiyatı STOK HAREKETLERİ ucundan
    /// okuyabiliyordu. Bu test kaçağın HTTP hattında da kapandığını ölçer; önce korumasız hâlde
    /// fiyatın GERÇEKTEN geldiğini doğrular (aksi hâlde sahte yeşil olurdu).
    /// </summary>
    [Fact]
    public async Task AA13_Stok_Hareketi_Ucundan_Fiyat_Sizmaz()
    {
        var stok = new DepoWise.Infrastructure.Materials.StockService(_svc.Factory);
        _svc.Permissions.SaveForUser(SuperAdmin(), _personelId,
            new[] { Tam("materials"), Tam("stock") }, Array.Empty<string>());
        stok.ReceiveIn(SuperAdmin(), new[]
        {
            new DepoWise.Infrastructure.Materials.StockLine(_malzemeId, 4m, 88.80m),
        }, Guid.NewGuid().ToString("N"));

        var acik = await TazePersonel();
        var oncekiler = await Json(await acik.GetAsync("/api/stock/movements"));
        Assert.Equal(88.80m, oncekiler.EnumerateArray().First().GetProperty("unitPrice").GetDecimal());

        FiyatiKoru();
        var c = await TazePersonel();
        var sonrakiler = await Json(await c.GetAsync("/api/stock/movements"));
        foreach (var satir in sonrakiler.EnumerateArray())
        {
            Assert.True(satir.GetProperty("unitPrice").ValueKind == System.Text.Json.JsonValueKind.Null,
                "Korumalıyken stok hareketi fiyatı HTTP yanıtında değer taşımamalı.");
            Assert.Equal("—", satir.GetProperty("priceText").GetString());
        }

        Assert.Equal(88.80m, HamHareketFiyati());   // veri yerinde: koruma yalnız GÖRÜNÜMÜ etkiler
    }

    private decimal? HamHareketFiyati()
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT unit_price FROM stock_movements WHERE material_id=@m ORDER BY created_at DESC LIMIT 1;";
        cmd.AddWithValue("@m", _malzemeId);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : DepoWise.Application.Common.Money.Parse((string)v);
    }
}
