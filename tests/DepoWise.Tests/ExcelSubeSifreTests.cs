using System.Net;
using System.Net.Http.Headers;
using DepoWise.Api;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ EXCEL MERKEZİ — ŞUBE SEÇİMİ + ŞUBE ŞİFRESİ (kullanıcı isteği 2026-09-03) ═══
///
/// Kural: içe/dışa aktarımda oturumun çalışma şubesinden FARKLI gerçek bir şube seçilirse o şubenin
/// ŞİFRESİ doğrulanır (girişteki L1/L2 kuralının aynısı). Şifresi olmayan şube serbesttir; "Tüm
/// Şubeler" şube değildir. ⚠️ Kapı SUNUCUDADIR — arayüz alanı gizlense de şifresiz istek geçemez.
///
///  ES1 — Şifreli şubeye şifresiz/yanlış şifreli içe aktarım 403 döner ve HİÇBİR kayıt oluşmaz.
///  ES2 — Doğru şifreyle içe aktarım çalışır.
///  ES3 — Şifresiz şube ve "Tüm Şubeler" ESKİSİ GİBİ şifre istemez (davranış korunur).
///  ES4 — Şube seçimli dışa aktarım (POST) aynı kapıdan geçer; GET ucu eski davranışını sürdürür.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-geneli ortam değişkeni yazar → seri koşmalı
public class ExcelSubeSifreTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Co = "XLS-SIFRE";
    private const string Pass = "Test!2026";
    private const string SubeSifresi = "Sube!123";

    private HttpClient _c = null!;
    private ServerServices _svc = null!;
    private SessionContext _s = null!;
    private string _sifreliSube = "", _sifresizSube = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();
        var uid = _svc.Users.EnsureInitialAdmin(Co, "xls_admin", Pass, RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _sifreliSube = _svc.Branches.Create(_s, new DepoWise.Infrastructure.Organization.NewBranch("Kilitli Şube", Password: SubeSifresi));
        _sifresizSube = _svc.Branches.Create(_s, new DepoWise.Infrastructure.Organization.NewBranch("Açık Şube"));
        _c = await _host.LoginAsync("xls_admin", Pass, Co);   // "Tüm Şubeler" oturumu
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private byte[] MaterialsFile(string code)
    {
        var headers = _svc.MaterialImport.SampleHeaders();
        var data = new List<IReadOnlyList<object?>>
        {
            headers.Select(h => h == MaterialImportService.ColCode ? (object?)code
                              : h == MaterialImportService.ColName ? code + " Adı" : null).ToList(),
        };
        return _svc.Excel.Export(new TableModel("Malzemeler", headers, data));
    }

    private static MultipartFormDataContent Body(byte[] file, string branchId, string? branchPassword = null)
    {
        var form = new MultipartFormDataContent();
        var c = new ByteArrayContent(file);
        c.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(c, "file", "test.xlsx");
        form.Add(new StringContent(branchId), "branchId");
        if (branchPassword is not null) form.Add(new StringContent(branchPassword), "branchPassword");
        return form;
    }

    private int MaterialCount()
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM materials WHERE company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@c", Co);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [Fact]
    public async Task ES1_Sifreli_Subeye_Sifresiz_Veya_Yanlis_Sifreli_Aktarim_403()
    {
        var once = MaterialCount();

        var sifresiz = await _c.PostAsync("/api/import/materials/commit", Body(MaterialsFile("ES1-A"), _sifreliSube));
        Assert.Equal(HttpStatusCode.Forbidden, sifresiz.StatusCode);

        var yanlis = await _c.PostAsync("/api/import/materials/commit", Body(MaterialsFile("ES1-B"), _sifreliSube, "yanlis"));
        Assert.Equal(HttpStatusCode.Forbidden, yanlis.StatusCode);

        // Ön kontrol (preview) da aynı kapıdan geçer (dosya içeriği bile okunmadan reddedilir).
        var onKontrol = await _c.PostAsync("/api/import/materials/preview", Body(MaterialsFile("ES1-C"), _sifreliSube));
        Assert.Equal(HttpStatusCode.Forbidden, onKontrol.StatusCode);

        Assert.Equal(once, MaterialCount());   // hiçbir kayıt oluşmadı
    }

    [Fact]
    public async Task ES2_Dogru_Sifreyle_Aktarim_Calisir()
    {
        var r = await _c.PostAsync("/api/import/materials/commit", Body(MaterialsFile("ES2-OK"), _sifreliSube, SubeSifresi));
        r.EnsureSuccessStatusCode();
        Assert.True(MaterialCount() >= 1);
    }

    [Fact]
    public async Task ES3_Sifresiz_Sube_ve_TumSubeler_Eskisi_Gibi_Serbest()
    {
        (await _c.PostAsync("/api/import/materials/commit", Body(MaterialsFile("ES3-ACIK"), _sifresizSube)))
            .EnsureSuccessStatusCode();
        (await _c.PostAsync("/api/import/materials/commit", Body(MaterialsFile("ES3-ALL"), "__all__")))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ES4_Sube_Secimli_Disa_Aktarim_Ayni_Kapidan_Gecer()
    {
        // Yanlış şifre → 403.
        using (var form = new MultipartFormDataContent
               { { new StringContent(_sifreliSube), "branchId" }, { new StringContent("yanlis"), "branchPassword" } })
        {
            var red = await _c.PostAsync("/api/export/materials", form);
            Assert.Equal(HttpStatusCode.Forbidden, red.StatusCode);
        }

        // Doğru şifre → Excel dosyası döner.
        using (var form = new MultipartFormDataContent
               { { new StringContent(_sifreliSube), "branchId" }, { new StringContent(SubeSifresi), "branchPassword" } })
        {
            var ok = await _c.PostAsync("/api/export/materials", form);
            ok.EnsureSuccessStatusCode();
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ok.Content.Headers.ContentType?.MediaType);
        }

        // GET ucu (şubesiz) ESKİ davranışını sürdürür — şifre istemez.
        (await _c.GetAsync("/api/export/materials")).EnsureSuccessStatusCode();

        // Şifresiz şube POST ile de serbesttir.
        using (var form = new MultipartFormDataContent { { new StringContent(_sifresizSube), "branchId" } })
            (await _c.PostAsync("/api/export/materials", form)).EnsureSuccessStatusCode();
    }
}
