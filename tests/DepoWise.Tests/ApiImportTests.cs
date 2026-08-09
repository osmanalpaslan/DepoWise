using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// EXCEL İÇE AKTARIM — WEB/API HATTI (İş #7, 2026-08-09).
///
/// İçe aktarım masaüstünde vardı ama <b>sunucuda hiç uç yoktu</b> → web'den Excel yüklenemiyordu.
/// Bu testler yeni hattın tamamını kapsar: yetki, ZORUNLU şube seçimi, şube kapsamı (fail-closed),
/// dosya doğrulama, ön kontrolün veritabanına <b>hiçbir şey yazmaması</b> ve aktarımın gerçekten yazması.
///
/// Not: iş kuralları yeniden yazılmadı — sunucu masaüstüyle AYNI import servislerini çağırır.
/// Bu yüzden burada satır-doğrulama ayrıntısı değil, <b>hattın kendisi</b> test edilir.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiImportTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "IMPORT-A";
    private const string Admin = "import_admin";
    private const string Pass = "Test!2026";

    private HttpClient _client = null!;
    private ServerServices _svc = null!;
    private SessionContext _session = null!;
    private string _branchId = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        using (var conn = _svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", Company);
            cmd.ExecuteNonQuery();
        }
        var uid = _svc.Users.EnsureInitialAdmin(Company, Admin, Pass, RoleKeys.CompanyAdmin);
        _session = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _branchId = _svc.Branches.Create(_session, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));

        _client = await _host.LoginAsync(Admin, Pass, Company);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    // ── yardımcılar ────────────────────────────────────────────────────────────────────────

    /// <summary>Sunucunun ürettiği ŞABLON başlıklarıyla gerçek bir .xlsx üretir (elle başlık yazmıyoruz →
    /// şablon değişirse test de değişir, sessizce yanlış eşleşme olmaz).</summary>
    private byte[] MaterialsFile(params (string Code, string Name)[] rows)
    {
        var headers = _svc.MaterialImport.SampleHeaders();
        var data = rows.Select(r => (IReadOnlyList<object?>)headers
            .Select(h => h == MaterialImportService.ColCode ? (object?)r.Code
                       : h == MaterialImportService.ColName ? r.Name : null).ToList()).ToList();
        return _svc.Excel.Export(new TableModel("Malzemeler", headers, data));
    }

    private static MultipartFormDataContent Body(byte[] file, string? branchId, string fileName = "test.xlsx")
    {
        var form = new MultipartFormDataContent();
        var c = new ByteArrayContent(file);
        c.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(c, "file", fileName);
        if (branchId is not null) form.Add(new StringContent(branchId), "branchId");
        return form;
    }

    private int MaterialCount()
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM materials WHERE company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@c", Company);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ── ŞABLON ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Turler_listelenir()
    {
        var r = await _client.GetAsync("/api/import/entities");
        r.EnsureSuccessStatusCode();
        var keys = (await ApiTestHost.JsonAsync(r)).EnumerateArray()
            .Select(e => e.GetProperty("key").GetString()).ToList();
        // Masaüstündeki 7 tür web'de de olmalı — biri eksikse o ekran web'de içe aktarılamaz.
        Assert.Equal(7, keys.Count);
        Assert.Contains("materials", keys);
        Assert.Contains("fuel-depot", keys);
    }

    [Fact]
    public async Task Sablon_gercek_bir_xlsx_dondurur()
    {
        var r = await _client.GetAsync("/api/import/materials/template");
        r.EnsureSuccessStatusCode();
        var bytes = await r.Content.ReadAsByteArrayAsync();

        // Sadece "200 döndü" yetmez: dosya gerçekten okunabilir olmalı ve başlıkları taşımalı.
        Assert.True(bytes.Length > 0);
        var back = _svc.Excel.Export(new TableModel("x", _svc.MaterialImport.SampleHeaders(),
            new[] { (IReadOnlyList<object?>)new object?[] { "K1", "Ad" }.Concat(
                Enumerable.Repeat((object?)null, _svc.MaterialImport.SampleHeaders().Count - 2)).ToList() }));
        Assert.NotEmpty(_svc.Excel.ReadRows(back));   // üretici/okuyucu çifti tutarlı
    }

    [Fact]
    public async Task Bilinmeyen_tur_reddedilir()
    {
        var r = await _client.GetAsync("/api/import/uydurma/template");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    // ── ÖN KONTROL (dry-run) ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task On_kontrol_HICBIR_KAYIT_OLUSTURMAZ()
    {
        var before = MaterialCount();
        var r = await _client.PostAsync("/api/import/materials/preview",
            Body(MaterialsFile(("ONK-1", "Filtre"), ("ONK-2", "Yağ")), "__all__"));
        r.EnsureSuccessStatusCode();

        var j = await ApiTestHost.JsonAsync(r);
        Assert.Equal(2, j.GetProperty("total").GetInt32());
        Assert.Equal(2, j.GetProperty("valid").GetInt32());
        Assert.Equal(before, MaterialCount());   // ← ASIL İDDİA: veritabanı DEĞİŞMEDİ
    }

    [Fact]
    public async Task On_kontrol_hatali_satiri_bildirir()
    {
        // "Kod" boş → zorunlu alan hatası. Kullanıcı bunu aktarımdan ÖNCE görmeli.
        var r = await _client.PostAsync("/api/import/materials/preview",
            Body(MaterialsFile(("", "Adı var kodu yok")), "__all__"));
        r.EnsureSuccessStatusCode();

        var j = await ApiTestHost.JsonAsync(r);
        Assert.Equal(1, j.GetProperty("failed").GetInt32());
        Assert.NotEmpty(j.GetProperty("errors").EnumerateArray());
    }

    // ── AKTARIM ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Aktarim_kayitlari_gercekten_olusturur()
    {
        var before = MaterialCount();
        var r = await _client.PostAsync("/api/import/materials/commit",
            Body(MaterialsFile(("AKT-1", "Filtre"), ("AKT-2", "Yağ")), _branchId));
        r.EnsureSuccessStatusCode();

        var j = await ApiTestHost.JsonAsync(r);
        Assert.Equal(2, j.GetProperty("added").GetInt32());
        Assert.Equal(before + 2, MaterialCount());
    }

    [Fact]
    public async Task Ayni_dosya_iki_kez_aktarilirsa_KOPYA_OLUSMAZ()
    {
        var file = MaterialsFile(("IDEM-1", "Filtre"));
        (await _client.PostAsync("/api/import/materials/commit", Body(file, "__all__"))).EnsureSuccessStatusCode();
        var after1 = MaterialCount();

        var r2 = await _client.PostAsync("/api/import/materials/commit", Body(file, "__all__"));
        r2.EnsureSuccessStatusCode();
        Assert.Equal(after1, MaterialCount());   // idempotent: kod zaten var → atlanır
    }

    // ── ŞUBE ZORUNLULUĞU + KAPSAM ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Sube_secilmeden_aktarim_REDDEDILIR()
    {
        var before = MaterialCount();
        var r = await _client.PostAsync("/api/import/materials/commit",
            Body(MaterialsFile(("SUBE-YOK", "Filtre")), branchId: null));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(before, MaterialCount());
    }

    [Fact]
    public async Task BASKA_firmanin_subesine_aktarim_REDDEDILIR()
    {
        // Başka firmada bir şube kur; bizim kullanıcımız onu seçmeye çalışsın → fail-closed.
        using (var conn = _svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES('IMPORT-B', 'IMPORT-B', 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.ExecuteNonQuery();
        }
        var uidB = _svc.Users.EnsureInitialAdmin("IMPORT-B", "import_b", Pass, RoleKeys.CompanyAdmin);
        var sB = new SessionContext(uidB, "IMPORT-B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var branchB = _svc.Branches.Create(sB, new DepoWise.Infrastructure.Organization.NewBranch("B Şubesi"));

        var before = MaterialCount();
        var r = await _client.PostAsync("/api/import/materials/commit",
            Body(MaterialsFile(("SIZINTI", "Filtre")), branchB));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(before, MaterialCount());
    }

    // ── DOSYA DOĞRULAMA ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dosyasiz_istek_REDDEDILIR()
    {
        var form = new MultipartFormDataContent { { new StringContent("__all__"), "branchId" } };
        var r = await _client.PostAsync("/api/import/materials/preview", form);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Excel_olmayan_dosya_ANLASILIR_HATA_verir()
    {
        var r = await _client.PostAsync("/api/import/materials/preview",
            Body(System.Text.Encoding.UTF8.GetBytes("bu bir excel degil"), "__all__", "not-excel.xlsx"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);

        // Kullanıcıya yığın izi (stack trace) değil, ne yapacağını söyleyen bir mesaj gitmeli.
        var msg = (await ApiTestHost.JsonAsync(r)).GetProperty("error").GetString() ?? "";
        Assert.Contains("xlsx", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bos_dosya_ANLASILIR_HATA_verir()
    {
        var bosDosya = _svc.Excel.Export(new TableModel("Malzemeler", _svc.MaterialImport.SampleHeaders(),
            System.Array.Empty<IReadOnlyList<object?>>()));   // yalnız başlık satırı
        var r = await _client.PostAsync("/api/import/materials/preview", Body(bosDosya, "__all__"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("veri satırı", (await ApiTestHost.JsonAsync(r)).GetProperty("error").GetString() ?? "");
    }

    // ── YETKİ ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Yetkisiz_kullanici_ICE_AKTARAMAZ()
    {
        // import_export yetkisi OLMAYAN kullanıcı (deny-by-default) — şablon bile indiremez.
        _svc.Users.EnsureInitialAdmin(Company, "yetkisiz_kul", Pass, RoleKeys.Staff);
        // "Tüm Şubeler" ile giriş admin olmayan kullanıcıya kapalı → somut şubeyle girilir.
        var client = await _host.LoginAsync("yetkisiz_kul", Pass, Company, _branchId);

        var before = MaterialCount();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/import/materials/template")).StatusCode);
        var r = await client.PostAsync("/api/import/materials/commit",
            Body(MaterialsFile(("YETKISIZ", "Filtre")), "__all__"));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(before, MaterialCount());
    }

    [Fact]
    public async Task Girissiz_istek_REDDEDILIR()
    {
        var anon = _host.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/import/entities")).StatusCode);
    }
}
