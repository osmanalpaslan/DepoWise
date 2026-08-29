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
    private HttpClient _adminA = null!, _depoB1 = null!, _adminB = null!, _cokSubeli = null!, _secici = null!;
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

        // Stok hareketleri: her şubede bir tane ("HRK-1" / "HRK-2" notuyla ayırt edilir).
        SqlCalistir($"INSERT INTO materials(id,company_id,code,name,unit_id,min_stock,created_at,updated_at,version,is_deleted) " +
            $"VALUES('RM1','{CoA}','RK1','Çimento',NULL,'0',1,1,1,0);");
        StokHareketi("RMV1", CoA, _b1, "HRK-1");
        StokHareketi("RMV2", CoA, _b2, "HRK-2");

        // B firmasının kendi şubesi ve aracı (tenant sızıntısı testi için).
        var bSube = _svc.Branches.Create(sb, new NewBranch("B-ŞUBE"));
        veh.Create(sb, new NewVehicle("B-ARC", Plate: "34ZZZ34", BranchId: bSube));

        // Yalnız ŞUBE 1'e bağlı DEPO PERSONELİ + rapor görüntüleme yetkisi.
        var depoId = _svc.Users.CreateUser(sa, new NewUser("rpt_depo1", Pass, "Depo 1",
            new[] { RoleKeys.Staff }, CoA, BranchId: _b1));
        // RPT-YETKI (2026-08-29, PK-R2=A): rapor türleri kategori yetkili — bu sınıf stok hareketleri
        // raporunu çalıştırır; "reports" üst kapısına report_stock eklenir (kapsam testleri değişmez).
        _svc.Permissions.SaveForUser(sa, depoId,
            new[]
            {
                new ModulePermission("reports", true, false, false, false),
                new ModulePermission("report_stock", true, false, false, false),
            }, Array.Empty<string>());

        _adminA = await _host.LoginAsync("rpt_admin_a", Pass, CoA);
        _adminB = await _host.LoginAsync("rpt_admin_b", Pass, CoB);
        _depoB1 = await _host.LoginAsync("rpt_depo1", Pass, CoA, _b1);

        // RPR-07: İKİ şubeye birden yetkili kullanıcı — "izinli şubeler" ile "giriş yapılan şube"
        // ayrımını ancak böyle bir kullanıcı ortaya çıkarır (tek şubelide ikisi aynıdır).
        var cokId = _svc.Users.CreateUser(sa, new NewUser("rpt_cok", Pass, "Çok Şubeli",
            new[] { RoleKeys.Staff }, CoA, BranchId: _b1));
        _svc.Permissions.SaveForUser(sa, cokId,
            new[]
            {
                new ModulePermission("reports", true, false, false, false),
                new ModulePermission("report_stock", true, false, false, false),   // RPT-YETKI (PK-R2=A)
            }, Array.Empty<string>());
        _svc.Permissions.SaveBranchScope(sa, cokId, new[] { _b1, _b2 });
        _cokSubeli = await _host.LoginAsync("rpt_cok", Pass, CoA, _b1);

        // ⭐ RPR-09: AYNI kullanıcı profili + "şube seçme" ÖZEL BUTONU. Bu buton olmadan sunucu
        // gövdedeki branchIds'i zaten yok sayıyordu; açığın görünür olması için yetkili biri gerekir.
        var seciciId = _svc.Users.CreateUser(sa, new NewUser("rpt_secici", Pass, "Seçici",
            new[] { RoleKeys.Staff }, CoA, BranchId: _b1));
        _svc.Permissions.SaveForUser(sa, seciciId,
            new[]
            {
                new ModulePermission("reports", true, false, false, false),
                new ModulePermission("report_stock", true, false, false, false),   // RPT-YETKI (PK-R2=A)
            },
            new[] { SpecialButtons.BranchSelect, SpecialButtons.ExportReports });
        _svc.Permissions.SaveBranchScope(sa, seciciId, new[] { _b1, _b2 });
        _secici = await _host.LoginAsync("rpt_secici", Pass, CoA, _b1);
    }

    private void SqlCalistir(string sql)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Rapor tarih varsayılanı "Bu Ay" olduğu için kayıtlar ŞİMDİ damgalanır.</summary>
    private static long Simdi => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void StokHareketi(string id, string firma, string sube, string not)
        => SqlCalistir($"INSERT INTO stock_movements(id,company_id,material_id,branch_id,movement_type," +
            $"direction,quantity,operation_id,note,created_at) VALUES('{id}','{firma}','RM1','{sube}'," +
            $"'in',1,'1','OP-{id}','{not}',{Simdi});");

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

    /// <summary>XLSX bir ZIP arşividir; hücre metinleri içindeki XML parçalarında durur.
    /// Dışa aktarmanın KAPSAMINI gerçekten ölçebilmek için içerik düz metne çevrilir.</summary>
    private static string ExcelMetni(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
        var sb = new System.Text.StringBuilder();
        foreach (var e in zip.Entries)
        {
            if (!e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            using var sr = new StreamReader(e.Open());
            sb.Append(sr.ReadToEnd());
        }
        return sb.ToString();
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
    public async Task R8_Rapor_Kapsam_Disi_Sube_Verisi_Icermez()
    {
        var t = await RaporAsync(_depoB1, "stock-movements", Istek());
        var metin = string.Join("\n", SatirMetinleri(t));

        Assert.Contains("HRK-1", metin);
        Assert.DoesNotContain("HRK-2", metin);
    }

    /// <summary>
    /// ⭐ R9 — <b>PARAMETRE MANİPÜLASYONU:</b> depo personeli isteğe ELLE yetkisiz şube kimliği yazsa
    /// bile o şubenin verisi DÖNMEZ. (Arayüzde seçici olmaması yeterli değildir.)
    /// </summary>
    [Fact]
    public async Task R9_Elle_Yazilan_Yetkisiz_Sube_Veri_Sizdirmaz()
    {
        var t = await RaporAsync(_depoB1, "stock-movements", Istek(branchIds: new[] { _b2 }));
        var metin = string.Join("\n", SatirMetinleri(t));

        Assert.DoesNotContain("HRK-2", metin);
    }

    /// <summary>R10 — yabancı FİRMA kimliği rapor çalıştırmada da reddedilir.</summary>
    [Fact]
    public async Task R10_Rapor_Yabanci_Firma_Kimligini_Reddeder()
    {
        var r = await _depoB1.PostAsJsonAsync("/api/reports/stock-movements", Istek(companyId: CoB));
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
        var r = await c.PostAsJsonAsync("/api/reports/stock-movements", Istek());

        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }

    /// <summary>R13 — anonim istek rapor çalıştıramaz.</summary>
    [Fact]
    public async Task R13_Anonim_Rapor_Calistiramaz()
    {
        var r = await _host.Anonymous().PostAsJsonAsync("/api/reports/stock-movements", Istek());
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    /// <summary>
    /// ⭐ R14 — EXPORT aynı kapsamı uygulamalı. Excel yetkisi olmayan kullanıcı export edemez;
    /// (kapsam kontrolü aynı BuildReport yolundan geçtiği için rapor sonucuyla birebir aynıdır).
    /// </summary>
    [Fact]
    public async Task R14_Export_Yetkisiz_Kullaniciya_Kapali()
    {
        var r = await _depoB1.PostAsJsonAsync("/api/reports/stock-movements/export", Istek());
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

    // ═════════════════════════════════════════════════════════════════════════════════════════
    //  3 · RPR-07 — OPERASYON RAPORU: ÇALIŞMA ŞUBESİ (web oturumu bunu taşımıyordu, R33)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    private static object OpIstek(string? operatingBranchId, object? branchIds = null) => new
    {
        fromDate = (long?)null,
        toDate = (long?)null,
        branchIds,
        companyId = (string?)null,
        operatingBranchId,
    };

    /// <summary>
    /// ⭐ R16 — ÇOK ŞUBELİ kullanıcı ŞUBE 1 ile giriş yaptığında Operasyon Raporu YALNIZ ŞUBE 1'i
    /// göstermeli. (Masaüstünde bu zaten böyleydi; web oturumu çalışma şubesini taşımadığı için
    /// kullanıcı TÜM izinli şubelerini görüyordu — parite kırığı.)
    /// </summary>
    [Fact]
    public async Task R16_Operasyon_Raporu_Calisma_Subesine_Daralir()
    {
        var t = await RaporAsync(_cokSubeli, "stock-movements", OpIstek(_b1));
        var metin = string.Join("|", SatirMetinleri(t));

        Assert.Contains("HRK-1", metin);
        Assert.DoesNotContain("HRK-2", metin);   // izinli AMA giriş yapılmayan şube
    }

    /// <summary>
    /// ⭐ R25 (RPR-09, denetim 2026-08-26) — <b>OPERASYON EKRANINDA ELLE ŞUBE LİSTESİ GEÇMEZ.</b>
    ///
    /// Operasyon ekranında şube seçici YOKTUR; ama sunucu, gövdede gelen <c>branchIds</c>'i "şube seçme"
    /// yetkisi olan kullanıcılar için uyguluyordu ve bu liste çalışma şubesinin YERİNE geçiyordu
    /// (<c>BranchAccess.Effective</c> sözleşmesi). Yetki kapısı korunduğu için veri SIZMIYORDU — ama
    /// "operasyon raporu yalnız giriş yapılan şubeyi gösterir" güvencesi yetkiye bağlı hâle geliyordu.
    /// Artık çalışma şubesi beyanı varsa kapsam koşulsuz o şubedir.
    /// </summary>
    [Fact]
    public async Task R25_Operasyon_Ekraninda_Elle_Sube_Listesi_Yoksayilir()
    {
        // ŞUBE 1 ile giriş + gövdede elle ŞUBE 2 → yine YALNIZ ŞUBE 1 gelmeli.
        var t = await RaporAsync(_secici, "stock-movements",
            new { fromDate = (long?)null, toDate = (long?)null, branchIds = new[] { _b2 }, operatingBranchId = _b1 });
        var metin = string.Join("|", SatirMetinleri(t));

        Assert.Contains("HRK-1", metin);
        Assert.DoesNotContain("HRK-2", metin);
    }

    /// <summary>R26 — aynı kapı DIŞA AKTARMADA da geçerli (Excel ekranla aynı kapsamı almalı).</summary>
    [Fact]
    public async Task R26_Operasyon_Exportunda_Elle_Sube_Listesi_Yoksayilir()
    {
        var r = await _secici.PostAsJsonAsync("/api/reports/stock-movements/export",
            new { fromDate = (long?)null, toDate = (long?)null, branchIds = new[] { _b2 }, operatingBranchId = _b1 });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        // Excel = ZIP; hücre metinleri içindeki XML parçalarında durur. Kapsam GERÇEKTEN ölçülür.
        var metin = ExcelMetni(await r.Content.ReadAsByteArrayAsync());
        Assert.Contains("HRK-1", metin);
        Assert.DoesNotContain("HRK-2", metin);
    }

    /// <summary>KİLİT: YÖNETİCİ ekranında (çalışma şubesi beyanı YOK) şube seçimi ÇALIŞMAYA devam eder.</summary>
    [Fact]
    public async Task R27_Yonetici_Ekraninda_Sube_Secimi_Calisir()
    {
        var t = await RaporAsync(_secici, "stock-movements",
            new { fromDate = (long?)null, toDate = (long?)null, branchIds = new[] { _b2 }, operatingBranchId = (string?)null });
        var metin = string.Join("|", SatirMetinleri(t));

        Assert.Contains("HRK-2", metin);
        Assert.DoesNotContain("HRK-1", metin);
    }

    /// <summary>
    /// ⭐ R28 (RPR-12, denetim 2026-08-26) — RAPOR LİSTESİ, KULLANICININ ÇALIŞTIRABİLDİKLERİDİR.
    ///
    /// Bazı raporlar başka bir ekranın verisini gösterir ve servisleri O ekranın iznini ister
    /// (Cari Ekstre → parties, Personel Listesi → personnel …). Katalog bunu bilmediği için liste
    /// izni olmayan kullanıcıya da gösteriliyor, kullanıcı "Sorgula"ya basınca 403 alıyordu.
    /// </summary>
    [Fact]
    public async Task R28_Katalog_Izni_Olmayan_Raporu_Listelemez()
    {
        var anahtarlar = await KatalogAnahtarlariAsync(_depoB1);

        Assert.Contains("stock-movements", anahtarlar);      // yetkili olduğu rapor DURUR
        Assert.DoesNotContain("personnel", anahtarlar);      // personel izni yok
        Assert.DoesNotContain("inspection", anahtarlar);     // muayene izni yok
        Assert.DoesNotContain("acc-statement", anahtarlar);  // cari izni yok
    }

    /// <summary>KİLİT: adminin listesi DARALMAZ (yanlış pozitif yok).</summary>
    [Fact]
    public async Task R29_Admin_Katalogda_Tum_Raporlari_Gorur()
    {
        var anahtarlar = await KatalogAnahtarlariAsync(_adminA);

        Assert.Contains("personnel", anahtarlar);
        Assert.Contains("inspection", anahtarlar);
        Assert.Contains("acc-statement", anahtarlar);
        Assert.Contains("stock-movements", anahtarlar);
    }

    private static async Task<List<string>> KatalogAnahtarlariAsync(HttpClient c)
    {
        var r = await c.GetAsync("/api/reports/catalog");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await ApiTestHost.JsonAsync(r);
        return j.EnumerateArray().Select(x => x.GetProperty("key").GetString() ?? "").ToList();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    //  RPT-YETKI (2026-08-29, PK-R2=A) — RAPOR KATEGORİ YETKİSİ HTTP KAPISI
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>⭐ R30 — kategori yetkisi endpoint'te de zorlanır: depo kullanıcısında yalnız
    /// report_stock var → rapor TÜR ADINI değiştirerek (vehicle) doğrudan API çağrısı REDDEDİLİR;
    /// export ucu da aynı kapıdadır. UI gizlemesi tek başına güvence DEĞİLDİR.</summary>
    [Fact]
    public async Task R30_Kategori_Yetkisi_Endpointte_Zorlanir()
    {
        var r = await _depoB1.PostAsJsonAsync("/api/reports/vehicle", Istek());
        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");

        var rd = await _depoB1.PostAsJsonAsync("/api/reports/vehicle-daily", Istek());
        Assert.True(ApiTestHost.IsDenied(rd), $"beklenen: reddedilme, gelen: {(int)rd.StatusCode}");

        // Export yolu da aynı merkezi kapıdan geçer (secici kullanıcının Export butonu VAR ama
        // araç kategorisi YOK → buton yetkisi kategori kapısını AŞAMAZ).
        var re = await _secici.PostAsJsonAsync("/api/reports/vehicle/export", Istek());
        Assert.True(ApiTestHost.IsDenied(re), $"beklenen: reddedilme, gelen: {(int)re.StatusCode}");
    }

    /// <summary>⭐ R31 — katalog süzmesi: kategori yetkisi olmayan tür LİSTEDE DE görünmez;
    /// yetkili olduğu stok raporları durur (yanlış pozitif yok). Admin daralmaz (R29 ayrıca kilitler).</summary>
    [Fact]
    public async Task R31_Katalog_Kategori_Yetkisine_Gore_Suzulur()
    {
        var anahtarlar = await KatalogAnahtarlariAsync(_depoB1);

        Assert.Contains("stock-movements", anahtarlar);   // report_stock VAR
        Assert.Contains("stock", anahtarlar);
        Assert.DoesNotContain("vehicle", anahtarlar);      // report_vehicle YOK
        Assert.DoesNotContain("vehicle-daily", anahtarlar);
        Assert.DoesNotContain("fuel", anahtarlar);         // report_fuel YOK

        var admin = await KatalogAnahtarlariAsync(_adminA);
        Assert.Contains("vehicle-daily", admin);           // admin bypass: yeni tür admin listesinde
    }

    /// <summary>R17 — çalışma şubesi GÖNDERİLMEZSE eski davranış: tüm izinli şubeler.</summary>
    [Fact]
    public async Task R17_Calisma_Subesi_Yoksa_Tum_Izinli_Subeler()
    {
        var t = await RaporAsync(_cokSubeli, "stock-movements", OpIstek(null));
        var metin = string.Join("|", SatirMetinleri(t));

        Assert.Contains("HRK-1", metin);
        Assert.Contains("HRK-2", metin);
    }

    /// <summary>
    /// ⭐ R18 — <b>KAPSAM GENİŞLETİLEMEZ:</b> kullanıcı çalışma şubesi olarak YETKİSİ OLMAYAN bir
    /// şubeyi yazarsa istek REDDEDİLİR (sessizce yok sayılmaz).
    /// </summary>
    [Fact]
    public async Task R18_Yetkisiz_Calisma_Subesi_Reddedilir()
    {
        var r = await _depoB1.PostAsJsonAsync("/api/reports/stock-movements", OpIstek(_b2));
        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }

    /// <summary>R19 — kendi şubesini çalışma şubesi olarak göndermek ÇALIŞIR (yanlış pozitif yok).</summary>
    [Fact]
    public async Task R19_Kendi_Subesi_Calisma_Subesi_Olarak_Kabul_Edilir()
    {
        var t = await RaporAsync(_depoB1, "stock-movements", OpIstek(_b1));
        Assert.Contains("HRK-1", string.Join("|", SatirMetinleri(t)));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    //  4 · RPR-07 — YÖNETİCİ RAPORU KAPISI
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ R20 — YÖNETİCİ raporu, yönetici OLMAYAN kullanıcıya kapalıdır. Bu raporlar oturumun
    /// ÇALIŞMA ŞUBESİNİ bilinçli olarak yok sayar (ürün kararı, BranchScopeTests ile kilitli) →
    /// depo personeli için istenen "yalnız giriş yapılan şube" kuralı orada sağlanamaz.
    /// </summary>
    [Fact]
    public async Task R20_Yonetici_Raporu_Personele_Kapali()
    {
        var r = await _depoB1.PostAsJsonAsync("/api/reports/vehicles-nontemplate", Istek());
        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }

    /// <summary>R21 — ADMİN için yönetici raporu ÇALIŞIR (mevcut davranış korunur).</summary>
    [Fact]
    public async Task R21_Yonetici_Raporu_Admine_Acik()
    {
        var t = await RaporAsync(_adminA, "vehicles-nontemplate", Istek());
        Assert.Contains("ARC-1", string.Join("|", SatirMetinleri(t)));
    }

    /// <summary>R23 — yönetici raporu EXPORT'u da personele kapalı (kapı iki uçta da var).</summary>
    [Fact]
    public async Task R23_Yonetici_Raporu_Exportu_Personele_Kapali()
    {
        var r = await _depoB1.PostAsJsonAsync("/api/reports/vehicles-nontemplate/export", Istek());
        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }

    /// <summary>R22 — STANDARD rapor personele AÇIK kalmalı (kapı fazla geniş kapanmadı).</summary>
    [Fact]
    public async Task R22_Standart_Rapor_Personele_Acik()
    {
        var t = await RaporAsync(_depoB1, "stock-movements", Istek());
        Assert.Contains("HRK-1", string.Join("|", SatirMetinleri(t)));
    }

    /// <summary>
    /// ⭐ R24 — EKRAN KAPISI (gerçek arayüz turunda bulundu): menüden gizlemek YETMEZ, kullanıcı
    /// adresi elle yazabiliyordu. Yönetici rapor ekranı hem WEB'de hem MASAÜSTÜNDE yönetici olmayana
    /// açılmamalı. (Veri zaten sunucuda korunuyordu — bu kapı kuralı adres/gezinme yolunda da uygular.)
    /// </summary>
    [Fact]
    public void R24_Yonetici_Rapor_Ekrani_Route_Kapili()
    {
        var dir = AppContext.BaseDirectory;
        for (int k = 0; k < 8 && dir is not null; k++)
        {
            if (File.Exists(Path.Combine(dir, "DepoWise.sln"))) break;
            dir = Directory.GetParent(dir)?.FullName;
        }

        var web = File.ReadAllText(Path.Combine(dir!, "src", "DepoWise.Web", "Components", "Pages", "Reports.razor"));
        Assert.Contains("@if (_manager && !Auth.IsAdmin)", web);

        var shell = File.ReadAllText(Path.Combine(dir!, "src", "DepoWise.Desktop", "ViewModels", "ShellViewModel.cs"));
        var i = shell.IndexOf("case \"reports:manager\":", StringComparison.Ordinal);
        Assert.True(i > 0, "reports:manager gezinme kaydı bulunamadı");
        var blok = shell.Substring(i, Math.Min(600, shell.Length - i));
        Assert.Contains("AccessControl.IsAdmin(_session)", blok);
    }
}