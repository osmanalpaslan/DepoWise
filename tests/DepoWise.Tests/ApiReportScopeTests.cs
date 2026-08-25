using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ RAPORLAR — UÇTAN UCA GÜVENLİK MATRİSİ (yayın öncesi denetim, 2026-08-25) ═══
///
/// <b>Neden GERÇEK HTTP hattı:</b> "arayüzde şube seçtirmiyoruz" bir güvenlik kanıtı DEĞİLDİR.
/// Bu testler uçlara doğrudan istek atar: yetkisiz şube kimliği elle yazıldığında, yabancı firma
/// kimliği gönderildiğinde ve hiç kimlik doğrulaması olmadığında sunucunun ne yaptığını ölçer.
///
/// <b>Bu turda bulunan hata (RPR-04):</b> <c>/api/reports/scope</c> şube listesini kullanıcının
/// kapsamıyla kırpıyordu (GUI-04'te düzeltilmişti) ama <b>ARAÇ ve PERSONEL listelerini kırpmıyordu</b> →
/// tek şubeye yetkili bir depo personeli, rapor filtresi açtığında firmanın <b>bütün araç plakalarını</b>
/// ve <b>bütün personel adlarını</b> görüyordu.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiReportScopeTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CoA = "RPT-A";
    private const string CoB = "RPT-B";
    private const string Pass = "Test!2026";

    private ServerServices _svc = null!;
    private HttpClient _adminA = null!, _depoB1 = null!, _adminB = null!;
    private string _b1 = "", _b2 = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        void Firma(string id)
        {
            using var conn = _svc.Factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        Firma(CoA); Firma(CoB);

        var adminAId = _svc.Users.EnsureInitialAdmin(CoA, "rpt_admin_a", Pass, RoleKeys.CompanyAdmin);
        var adminBId = _svc.Users.EnsureInitialAdmin(CoB, "rpt_admin_b", Pass, RoleKeys.CompanyAdmin);
        var sa = new SessionContext(adminAId, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var sb = new SessionContext(adminBId, CoB, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _b1 = _svc.Branches.Create(sa, new NewBranch("ŞUBE 1"));
        _b2 = _svc.Branches.Create(sa, new NewBranch("ŞUBE 2"));

        // Araçlar: her şubede bir tane (plakalar ayırt edilebilir olsun).
        var veh = new VehicleService(_svc.Factory);
        veh.Create(sa, new NewVehicle("ARC-1", Plate: "01AAA01", BranchId: _b1));
        veh.Create(sa, new NewVehicle("ARC-2", Plate: "02BBB02", BranchId: _b2));

        // Personel: her şubede bir tane.
        _svc.Personnel.Create(sa, new NewPersonnel("Ali Bir", null, null, _b1));
        _svc.Personnel.Create(sa, new NewPersonnel("Veli İki", null, null, _b2));

        // B firmasının kendi şubesi ve aracı (tenant sızıntısı testi için).
        var bSube = _svc.Branches.Create(sb, new NewBranch("B-ŞUBE"));
        veh.Create(sb, new NewVehicle("B-ARC", Plate: "34ZZZ34", BranchId: bSube));

        // Yalnız ŞUBE 1'e bağlı DEPO PERSONELİ + rapor görüntüleme yetkisi.
        var depoId = _svc.Users.CreateUser(sa, new NewUser("rpt_depo1", Pass, "Depo 1",
            new[] { RoleKeys.Staff }, CoA, BranchId: _b1));
        _svc.Permissions.SaveForUser(sa, depoId,
            new[] { new ModulePermission("reports", true, false, false, false) }, Array.Empty<string>());

        _adminA = await _host.LoginAsync("rpt_admin_a", Pass, CoA);
        _adminB = await _host.LoginAsync("rpt_admin_b", Pass, CoB);
        _depoB1 = await _host.LoginAsync("rpt_depo1", Pass, CoA, _b1);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private static List<string> Alanlar(JsonElement kok, string dizi, string alan)
    {
        var list = new List<string>();
        if (kok.TryGetProperty(dizi, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
                if (e.TryGetProperty(alan, out var v) && v.ValueKind == JsonValueKind.String)
                    list.Add(v.GetString() ?? "");
        return list;
    }

    private static async Task<JsonElement> ScopeAsync(HttpClient c)
    {
        var r = await c.GetAsync("/api/reports/scope");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await ApiTestHost.JsonAsync(r);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    //  1 · FİLTRE SEÇENEKLERİ (dropdown içeriği) — RPR-04
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Şube listesi kapsamla kırpılır (GUI-04 regresyonu).</summary>
    [Fact]
    public async Task R1_Depo_Personeli_Yalniz_Kendi_Subesini_Gorur()
    {
        var j = await ScopeAsync(_depoB1);
        var adlar = Alanlar(j, "branches", "name");

        Assert.Contains("ŞUBE 1", adlar);
        Assert.DoesNotContain("ŞUBE 2", adlar);
    }

    /// <summary>
    /// ⭐ R2 (RPR-04) — ARAÇ listesi de kapsamla kırpılmalı. Eskiden firmanın TÜM araçları ve
    /// PLAKALARI dönüyordu.
    /// </summary>
    [Fact]
    public async Task R2_Depo_Personeli_Kapsam_Disi_Araci_Gormemeli()
    {
        var j = await ScopeAsync(_depoB1);
        var goruntu = Alanlar(j, "vehicles", "display");

        Assert.Contains(goruntu, x => x.Contains("ARC-1", StringComparison.Ordinal));
        Assert.DoesNotContain(goruntu, x => x.Contains("ARC-2", StringComparison.Ordinal));
        Assert.DoesNotContain(goruntu, x => x.Contains("02BBB02", StringComparison.Ordinal));   // plaka sızmaz
    }

    /// <summary>⭐ R3 (RPR-04) — PERSONEL listesi de kapsamla kırpılmalı (adlar sızmasın).</summary>
    [Fact]
    public async Task R3_Depo_Personeli_Kapsam_Disi_Personeli_Gormemeli()
    {
        var j = await ScopeAsync(_depoB1);
        var adlar = Alanlar(j, "technicians", "name");

        Assert.Contains("Ali Bir", adlar);
        Assert.DoesNotContain("Veli İki", adlar);
    }

    /// <summary>R4 — ADMİN (kapsamsız) için davranış DEĞİŞMEZ: her şeyi görür.</summary>
    [Fact]
    public async Task R4_Admin_Tum_Sube_Arac_Personeli_Gorur()
    {
        var j = await ScopeAsync(_adminA);

        Assert.Equal(2, Alanlar(j, "branches", "name").Count);
        Assert.Equal(2, Alanlar(j, "vehicles", "display").Count);
        Assert.Equal(2, Alanlar(j, "technicians", "name").Count);
    }

    /// <summary>R5 — TENANT: A firmasının admini B firmasının aracını/şubesini GÖRMEZ.</summary>
    [Fact]
    public async Task R5_Baska_Firmanin_Verisi_Sizmaz()
    {
        var j = await ScopeAsync(_adminA);

        Assert.DoesNotContain(Alanlar(j, "vehicles", "display"), x => x.Contains("B-ARC", StringComparison.Ordinal));
        Assert.DoesNotContain("B-ŞUBE", Alanlar(j, "branches", "name"));
    }

    /// <summary>R6 — TENANT (parametre manipülasyonu): yabancı firma kimliği REDDEDİLİR.</summary>
    [Fact]
    public async Task R6_Yabanci_Firma_Kimligi_Reddedilir()
    {
        var r = await _adminA.GetAsync($"/api/reports/scope?companyId={CoB}");
        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }

    /// <summary>R7 — kimlik doğrulaması olmadan erişilemez.</summary>
    [Fact]
    public async Task R7_Anonim_Erisemez()
    {
        var r = await _host.Anonymous().GetAsync("/api/reports/scope");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    //  2 · RAPOR ÇALIŞTIRMA — kapsam gerçekten SORGUDA mı?
    // ═════════════════════════════════════════════════════════════════════════════════════════

    private static object Istek(object? branchIds = null, string? companyId = null) => new
    {
        fromDate = (long?)null,
        toDate = (long?)null,
        branchIds,
        companyId,
    };

    private static async Task<JsonElement> RaporAsync(HttpClient c, string tip, object govde)
    {
        var r = await c.PostAsJsonAsync($"/api/reports/{tip}", govde);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await ApiTestHost.JsonAsync(r);
    }

    private static List<string> SatirMetinleri(JsonElement rapor)
    {
        var list = new List<string>();
        if (rapor.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            foreach (var satir in rows.EnumerateArray()) list.Add(satir.ToString());
        return list;
    }

    /// <summary>⭐ R8 — depo personelinin araç raporu kapsam DIŞI aracı içermez.</summary>
    [Fact]
    public async Task R8_Arac_Raporu_Kapsam_Disi_Araci_Icermez()
    {
        var t = await RaporAsync(_depoB1, "vehicles-nontemplate", Istek());
        var metin = string.Join("\n", SatirMetinleri(t));

        Assert.Contains("ARC-1", metin);
        Assert.DoesNotContain("ARC-2", metin);
        Assert.DoesNotContain("02BBB02", metin);
    }

    /// <summary>
    /// ⭐ R9 — <b>PARAMETRE MANİPÜLASYONU:</b> depo personeli isteğe ELLE yetkisiz şube kimliği yazsa
    /// bile o şubenin verisi DÖNMEZ. (Arayüzde seçici olmaması yeterli değildir.)
    /// </summary>
    [Fact]
    public async Task R9_Elle_Yazilan_Yetkisiz_Sube_Veri_Sizdirmaz()
    {
        var t = await RaporAsync(_depoB1, "vehicles-nontemplate", Istek(branchIds: new[] { _b2 }));
        var metin = string.Join("\n", SatirMetinleri(t));

        Assert.DoesNotContain("ARC-2", metin);
        Assert.DoesNotContain("02BBB02", metin);
    }

    /// <summary>R10 — yabancı FİRMA kimliği rapor çalıştırmada da reddedilir.</summary>
    [Fact]
    public async Task R10_Rapor_Yabanci_Firma_Kimligini_Reddeder()
    {
        var r = await _depoB1.PostAsJsonAsync("/api/reports/vehicles-nontemplate", Istek(companyId: CoB));
        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }

    /// <summary>R11 — B firmasının admini A firmasının aracını raporda GÖREMEZ (tenant).</summary>
    [Fact]
    public async Task R11_Baska_Firma_Admini_A_Verisini_Goremez()
    {
        var t = await RaporAsync(_adminB, "vehicles-nontemplate", Istek());
        var metin = string.Join("\n", SatirMetinleri(t));

        Assert.Contains("B-ARC", metin);
        Assert.DoesNotContain("ARC-1", metin);
        Assert.DoesNotContain("ARC-2", metin);
    }

    /// <summary>R12 — RAPOR YETKİSİ OLMAYAN kullanıcı raporu çalıştıramaz (deny-by-default).</summary>
    [Fact]
    public async Task R12_Yetkisiz_Kullanici_Rapor_Calistiramaz()
    {
        var sa = new SessionContext(_svc.Users.EnsureInitialAdmin(CoA, "rpt_yetkisiz", Pass, RoleKeys.Staff),
            CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _ = sa;   // kullanıcı yalnız oluşturulur; HİÇBİR yetki verilmez

        var c = await _host.LoginAsync("rpt_yetkisiz", Pass, CoA, _b1);
        var r = await c.PostAsJsonAsync("/api/reports/vehicles-nontemplate", Istek());

        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }

    /// <summary>R13 — anonim istek rapor çalıştıramaz.</summary>
    [Fact]
    public async Task R13_Anonim_Rapor_Calistiramaz()
    {
        var r = await _host.Anonymous().PostAsJsonAsync("/api/reports/vehicles-nontemplate", Istek());
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    /// <summary>
    /// ⭐ R14 — EXPORT aynı kapsamı uygulamalı. Excel yetkisi olmayan kullanıcı export edemez;
    /// (kapsam kontrolü aynı BuildReport yolundan geçtiği için rapor sonucuyla birebir aynıdır).
    /// </summary>
    [Fact]
    public async Task R14_Export_Yetkisiz_Kullaniciya_Kapali()
    {
        var r = await _depoB1.PostAsJsonAsync("/api/reports/vehicles-nontemplate/export", Istek());
        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }

    /// <summary>R15 — BOŞ SONUÇ: kayıt yokken uç 200 döner ve ekran çökmez (satır dizisi boş).</summary>
    [Fact]
    public async Task R15_Bos_Sonuc_200_Doner()
    {
        // Gelecek bir tarih aralığı → hiçbir kayıt düşmez.
        var gelecek = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var r = await _depoB1.PostAsJsonAsync("/api/reports/stock-movements",
            new { fromDate = gelecek, toDate = gelecek + 86_400_000, branchIds = (string[]?)null, companyId = (string?)null });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await ApiTestHost.JsonAsync(r);
        Assert.Empty(SatirMetinleri(j));
    }
}
