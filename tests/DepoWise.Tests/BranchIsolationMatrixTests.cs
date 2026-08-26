using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Infrastructure.Database;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ŞUBE İZOLASYONU — TAM MATRİS ═══ (denetim 2026-08-26)
///
/// <b>Neden bu sınıf ayrıca gerekli:</b> ÜRETİM veritabanında bugün <b>hiç şube tanımlı değil</b> (0 şube).
/// Bu yüzden şube izolasyonu üretim verisiyle gözlemlenemez ve "canlıda çalışıyor" denemez. Kural ancak
/// gerçekçi bir kurguda kanıtlanabilir:
///
/// <code>
///   FİRMA A → ŞUBE A1, ŞUBE A2
///   FİRMA B → ŞUBE B1
/// </code>
///
/// Her rapor ve her rol için "görebilir / göremez / seçemez / elle yazsa da geçemez" durumları GERÇEK HTTP
/// istekleriyle ölçülür. Dışa aktarma (Excel) da aynı kapsamı uygulamak zorundadır — Excel içeriği açılıp
/// içine bakılır, "200 döndü" ile yetinilmez.
/// </summary>
[Collection("PostgresSchema")]
public class BranchIsolationMatrixTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CoA = "MTX-A";
    private const string CoB = "MTX-B";
    private const string Pass = "Test!2026";

    private ServerServices _svc = null!;
    private string _a1 = "", _a2 = "", _b1 = "";

    /// <summary>A1'e bağlı depo personeli (operasyon kullanıcısı).</summary>
    private HttpClient _a1Depo = null!;
    /// <summary>A1+A2'ye yetkili, şube SEÇEBİLEN yönetici — firma admini DEĞİL, kapsamı gerçekten sınırlı.</summary>
    private HttpClient _aYonetici = null!;
    /// <summary>B firmasının admini.</summary>
    private HttpClient _bAdmin = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        void Firma(string id, string ad)
            => Sql("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) " +
                   "VALUES('" + id + "','" + ad + "',1,1,1,0,5,20,5) ON CONFLICT(id) DO NOTHING;");

        Firma(CoA, "A Firmasi");
        Firma(CoB, "B Firmasi");

        var aId = _svc.Users.EnsureInitialAdmin(CoA, "mtx_admin_a", Pass, RoleKeys.CompanyAdmin);
        var bId = _svc.Users.EnsureInitialAdmin(CoB, "mtx_admin_b", Pass, RoleKeys.CompanyAdmin);
        var sa = new SessionContext(aId, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var sb = new SessionContext(bId, CoB, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _a1 = _svc.Branches.Create(sa, new NewBranch("SUBE-A1"));
        _a2 = _svc.Branches.Create(sa, new NewBranch("SUBE-A2"));
        _b1 = _svc.Branches.Create(sb, new NewBranch("SUBE-B1"));

        // ── Her şubede ayırt edilebilir veri ──────────────────────────────────────────────────
        var veh = new VehicleService(_svc.Factory);
        veh.Create(sa, new NewVehicle("ARC-A1", Plate: "01AAA01", BranchId: _a1));
        veh.Create(sa, new NewVehicle("ARC-A2", Plate: "02AAA02", BranchId: _a2));
        veh.Create(sb, new NewVehicle("ARC-B1", Plate: "03BBB03", BranchId: _b1));

        _svc.Personnel.Create(sa, new NewPersonnel("KisiA1", null, null, _a1));
        _svc.Personnel.Create(sa, new NewPersonnel("KisiA2", null, null, _a2));
        _svc.Personnel.Create(sb, new NewPersonnel("KisiB1", null, null, _b1));

        Sql("INSERT INTO materials(id,company_id,code,name,unit_id,min_stock,created_at,updated_at,version,is_deleted) " +
            "VALUES('MTXM','" + CoA + "','MK1','Malzeme A',NULL,'0',1,1,1,0);");
        Sql("INSERT INTO materials(id,company_id,code,name,unit_id,min_stock,created_at,updated_at,version,is_deleted) " +
            "VALUES('MTXMB','" + CoB + "','MK1','Malzeme B',NULL,'0',1,1,1,0);");

        Hareket("MVA1", CoA, "MTXM", _a1, "IZA1");
        Hareket("MVA2", CoA, "MTXM", _a2, "IZA2");
        Hareket("MVB1", CoB, "MTXMB", _b1, "IZB1");

        Belge("INSA1", CoA, VehId(CoA, "ARC-A1"), "IZA1");
        Belge("INSA2", CoA, VehId(CoA, "ARC-A2"), "IZA2");
        Belge("INSB1", CoB, VehId(CoB, "ARC-B1"), "IZB1");

        // ── Kullanıcılar ──────────────────────────────────────────────────────────────────────
        var depoId = _svc.Users.CreateUser(sa, new NewUser("mtx_a1_depo", Pass, "A1 Depo",
            new[] { RoleKeys.Staff }, CoA, BranchId: _a1));
        _svc.Permissions.SaveForUser(sa, depoId, RaporIzinleri(), Array.Empty<string>());

        var yonId = _svc.Users.CreateUser(sa, new NewUser("mtx_a_yonetici", Pass, "A Yonetici",
            new[] { RoleKeys.Staff }, CoA, BranchId: _a1));
        _svc.Permissions.SaveForUser(sa, yonId, RaporIzinleri(),
            new[] { SpecialButtons.BranchSelect, SpecialButtons.ExportReports });
        _svc.Permissions.SaveBranchScope(sa, yonId, new[] { _a1, _a2 });

        _a1Depo = await _host.LoginAsync("mtx_a1_depo", Pass, CoA, _a1);
        _aYonetici = await _host.LoginAsync("mtx_a_yonetici", Pass, CoA, _a1);
        _bAdmin = await _host.LoginAsync("mtx_admin_b", Pass, CoB);
    }

    public Task DisposeAsync() { _host.Dispose(); return Task.CompletedTask; }

    private static IEnumerable<ModulePermission> RaporIzinleri() => new[]
    {
        new ModulePermission("reports", true, false, false, false),
        new ModulePermission("inspection", true, false, false, false),
        new ModulePermission("personnel", true, false, false, false),
    };

    private static long Simdi => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void Sql(string sql)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void Hareket(string id, string firma, string mat, string sube, string not)
        => Sql("INSERT INTO stock_movements(id,company_id,material_id,branch_id,movement_type,direction,quantity,operation_id,note,created_at) " +
               "VALUES('" + id + "','" + firma + "','" + mat + "','" + sube + "','in',1,'1','OP" + id + "','" + not + "'," + Simdi + ");");

    private void Belge(string id, string firma, string arac, string yer)
        => Sql("INSERT INTO vehicle_inspections(id,company_id,vehicle_id,doc_type,last_date,next_date,result,place,created_at,updated_at,version,is_deleted) " +
               "VALUES('" + id + "','" + firma + "','" + arac + "','inspection'," + Simdi + "," + (Simdi + 86_400_000L * 60) +
               ",'Gecti','" + yer + "',1,1,1,0);");

    private string VehId(string firma, string kod)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM vehicles WHERE company_id=@c AND internal_code=@k;";
        cmd.AddWithValue("@c", firma);
        cmd.AddWithValue("@k", kod);
        return (string)cmd.ExecuteScalar()!;
    }

    // ── istek yardımcıları ────────────────────────────────────────────────────────────────────
    private static object Istek(string? calismaSubesi = null, IEnumerable<string>? subeler = null, string? firma = null)
        => new
        {
            fromDate = (long?)null,
            toDate = (long?)null,
            branchIds = subeler?.ToArray(),
            companyId = firma,
            operatingBranchId = calismaSubesi,
        };

    private static async Task<string> MetinAsync(HttpClient c, string rapor, object govde)
    {
        var r = await c.PostAsJsonAsync("/api/reports/" + rapor, govde);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadAsStringAsync();
    }

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

    /// <summary>Şube kapsamı uygulayan OPERASYON raporları (izler not/yer/ad alanlarından okunur).</summary>
    public static IEnumerable<object[]> KapsamliRaporlar => new[]
    {
        new object[] { "stock-movements" },
        new object[] { "inspection" },
        new object[] { "personnel" },
    };

    // ═══════════ 1) A1 KULLANICISI: KENDİ ŞUBESİ ✔ · DİĞER ŞUBE ✘ · DİĞER FİRMA ✘ ═══════════

    [Theory]
    [MemberData(nameof(KapsamliRaporlar))]
    public async Task M1_A1_Kullanicisi_Yalniz_A1_Gorur(string rapor)
    {
        var metin = await MetinAsync(_a1Depo, rapor, Istek(calismaSubesi: _a1));

        Assert.Contains("A1", metin, StringComparison.Ordinal);          // kendi şubesinin izi var
        Assert.DoesNotContain("IZA2", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("KisiA2", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("02AAA02", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("SUBE-A2", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("IZB1", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("KisiB1", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("SUBE-B1", metin, StringComparison.Ordinal);
    }

    // ═══════════ 2) ELLE ŞUBE / FİRMA KİMLİĞİ YAZMA ═══════════

    [Theory]
    [MemberData(nameof(KapsamliRaporlar))]
    public async Task M2_A1_Istege_A2_Yazarsa_Gecmez(string rapor)
    {
        var metin = await MetinAsync(_a1Depo, rapor, Istek(calismaSubesi: _a1, subeler: new[] { _a2 }));
        Assert.DoesNotContain("IZA2", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("KisiA2", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("02AAA02", metin, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(KapsamliRaporlar))]
    public async Task M3_A1_Istege_B1_Yazarsa_Gecmez(string rapor)
    {
        var metin = await MetinAsync(_a1Depo, rapor, Istek(calismaSubesi: _a1, subeler: new[] { _b1 }));
        Assert.DoesNotContain("IZB1", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("KisiB1", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("03BBB03", metin, StringComparison.Ordinal);
    }

    /// <summary>⭐ Çalışma şubesi olarak YETKİSİZ şube yazmak REDDEDİLİR (sessizce yok sayılmaz).</summary>
    [Fact]
    public async Task M4_A1_Calisma_Subesi_Olarak_A2_Yazarsa_Reddedilir()
    {
        var r = await _a1Depo.PostAsJsonAsync("/api/reports/stock-movements", Istek(calismaSubesi: _a2));
        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: red, gelen: {(int)r.StatusCode}");
    }

    [Fact]
    public async Task M5_A1_Calisma_Subesi_Olarak_B1_Yazarsa_Reddedilir()
    {
        var r = await _a1Depo.PostAsJsonAsync("/api/reports/stock-movements", Istek(calismaSubesi: _b1));
        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: red, gelen: {(int)r.StatusCode}");
    }

    [Theory]
    [MemberData(nameof(KapsamliRaporlar))]
    public async Task M6_Yabanci_Firma_Kimligi_Sizdirmaz(string rapor)
    {
        var r = await _a1Depo.PostAsJsonAsync("/api/reports/" + rapor, Istek(calismaSubesi: _a1, firma: CoB));
        if (ApiTestHost.IsDenied(r)) return;                    // reddetmek de kabul

        var metin = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("IZB1", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("KisiB1", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("03BBB03", metin, StringComparison.Ordinal);
    }

    // ═══════════ 3) YÖNETİCİ: YETKİLİ ŞUBELERİ SEÇEBİLİR, YETKİSİZİ SEÇEMEZ ═══════════

    [Fact]
    public async Task M7_Yonetici_Yetkili_Subeleri_Secebilir()
    {
        // Yönetici kipi = çalışma şubesi beyanı YOK.
        var a1 = await MetinAsync(_aYonetici, "stock-movements", Istek(subeler: new[] { _a1 }));
        Assert.Contains("IZA1", a1, StringComparison.Ordinal);
        Assert.DoesNotContain("IZA2", a1, StringComparison.Ordinal);

        var a2 = await MetinAsync(_aYonetici, "stock-movements", Istek(subeler: new[] { _a2 }));
        Assert.Contains("IZA2", a2, StringComparison.Ordinal);
        Assert.DoesNotContain("IZA1", a2, StringComparison.Ordinal);

        var ikisi = await MetinAsync(_aYonetici, "stock-movements", Istek(subeler: new[] { _a1, _a2 }));
        Assert.Contains("IZA1", ikisi, StringComparison.Ordinal);
        Assert.Contains("IZA2", ikisi, StringComparison.Ordinal);
    }

    [Fact]
    public async Task M8_Yonetici_Yetkisiz_Subeyi_Secemez()
    {
        var metin = await MetinAsync(_aYonetici, "stock-movements", Istek(subeler: new[] { _b1 }));

        Assert.DoesNotContain("IZB1", metin, StringComparison.Ordinal);
        // Kapsam dışı istek "filtre kalktı" anlamına GELMEZ: kendi şubeleri de gelmemeli (fail-closed).
        Assert.DoesNotContain("IZA1", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("IZA2", metin, StringComparison.Ordinal);
    }

    [Fact]
    public async Task M9_Yonetici_Kapsam_Listesi_Yalniz_Yetkili_Subeleri_Verir()
    {
        var r = await _aYonetici.GetAsync("/api/reports/scope");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var metin = await r.Content.ReadAsStringAsync();

        Assert.Contains("SUBE-A1", metin, StringComparison.Ordinal);
        Assert.Contains("SUBE-A2", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("SUBE-B1", metin, StringComparison.Ordinal);
        // RPR-04: araç ve personel listeleri de kapsamla kırpılır.
        Assert.DoesNotContain("03BBB03", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("KisiB1", metin, StringComparison.Ordinal);
    }

    [Fact]
    public async Task M10_Depo_Personelinin_Kapsam_Listesi_Yalniz_Kendi_Subesi()
    {
        var metin = await (await _a1Depo.GetAsync("/api/reports/scope")).Content.ReadAsStringAsync();

        Assert.Contains("SUBE-A1", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("SUBE-A2", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("SUBE-B1", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("02AAA02", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("KisiA2", metin, StringComparison.Ordinal);
    }

    // ═══════════ 4) DIŞA AKTARMA AYNI KAPSAMI UYGULAR (içerik açılarak) ═══════════

    [Fact]
    public async Task M11_Export_Operasyon_Kapsamini_Uygular()
    {
        var r = await _aYonetici.PostAsJsonAsync("/api/reports/stock-movements/export", Istek(calismaSubesi: _a1));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var metin = ExcelMetni(await r.Content.ReadAsByteArrayAsync());
        Assert.Contains("IZA1", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("IZA2", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("IZB1", metin, StringComparison.Ordinal);
    }

    [Fact]
    public async Task M12_Export_Yetkisiz_Subeyi_Vermez()
    {
        var r = await _aYonetici.PostAsJsonAsync("/api/reports/stock-movements/export", Istek(subeler: new[] { _b1 }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var metin = ExcelMetni(await r.Content.ReadAsByteArrayAsync());
        Assert.DoesNotContain("IZB1", metin, StringComparison.Ordinal);
    }

    /// <summary>Depo personelinin dışa aktarma yetkisi YOK → export kapalı (UI'da gizlemek yetmez).</summary>
    [Fact]
    public async Task M13_Depo_Personeli_Export_Yapamaz()
    {
        var r = await _a1Depo.PostAsJsonAsync("/api/reports/stock-movements/export", Istek(calismaSubesi: _a1));
        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: red, gelen: {(int)r.StatusCode}");
    }

    // ═══════════ 5) DİĞER FİRMANIN ADMİNİ A'YI HİÇ GÖREMEZ ═══════════

    [Theory]
    [MemberData(nameof(KapsamliRaporlar))]
    public async Task M14_B_Admini_A_Verisini_Goremez(string rapor)
    {
        var metin = await MetinAsync(_bAdmin, rapor, Istek());

        Assert.DoesNotContain("IZA1", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("IZA2", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("KisiA1", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("01AAA01", metin, StringComparison.Ordinal);
    }

    [Fact]
    public async Task M15_B_Admini_A_Subesini_Isteyemez()
    {
        var r = await _bAdmin.PostAsJsonAsync("/api/reports/stock-movements", Istek(calismaSubesi: _a1));
        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: red, gelen: {(int)r.StatusCode}");
    }

    // ═══════════ 6) YÖNETİCİ RAPORU KAPISI (doğrudan API) ═══════════

    [Fact]
    public async Task M16_Yonetici_Raporu_Depo_Personeline_Kapali()
    {
        foreach (var k in new[] { "status", "vehicles-template", "vehicles-nontemplate", "materials-template", "materials-nontemplate" })
        {
            var r = await _a1Depo.PostAsJsonAsync("/api/reports/" + k, Istek(calismaSubesi: _a1));
            Assert.True(ApiTestHost.IsDenied(r), $"{k} → beklenen: red, gelen: {(int)r.StatusCode}");
        }
    }

    /// <summary>Yönetici raporu ADMİNE açık kalmalı (yanlış pozitif yok) ve firma sınırını korumalı.</summary>
    [Fact]
    public async Task M17_Yonetici_Raporu_Admine_Acik_Ve_Firma_Sinirli()
    {
        var metin = await MetinAsync(_bAdmin, "status", Istek());

        Assert.Contains("SUBE-B1", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("SUBE-A1", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("SUBE-A2", metin, StringComparison.Ordinal);
    }
}
