using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// WEB-01 (2026-08-10) — API HATA SÖZLEŞMESİ.
///
/// Web'de kullanıcıya <c>Hata 409: {"error":"..."}</c> gibi ham JSON gösterilmesinin çözümü
/// <c>ApiClient.ErrorMessageAsync</c>'in gövdedeki <c>error</c> alanını ÇIKARMASIDIR. Bu düzeltme
/// tamamen sunucunun şu sözleşmeyi sürdürmesine dayanır:
///
///   • gövde JSON nesnesidir ve <c>error</c> alanı taşır,
///   • mesaj SON KULLANICI diliyle (Türkçe) yazılmıştır,
///   • 500'de ham exception SIZDIRILMAZ (dosya yolu/SQL detayı loga gider).
///
/// Sözleşme sessizce bozulursa web anlamlı mesaj gösteremez ve genel karşılığa düşer — bu testler
/// o kırılmayı yakalar. <b>Test projesi DepoWise.Web'i referanslamaz</b> (mimari sınır bilinçlidir),
/// bu yüzden istemci tarafı ayrıştırma gerçek tarayıcı QA'siyle ayrıca doğrulanmıştır.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiErrorContractTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "HATA-A";
    private const string User = "hata_kullanici";
    private const string Other = "HATA-B";
    private const string OtherUser = "hata_yabanci";
    private const string Pass = "Test!2026";
    private HttpClient _client = null!;
    private HttpClient _otherClient = null!;
    private string _materialId = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        var svc = _host.Services.GetRequiredService<ServerServices>();

        void EnsureCompany(string id)
        {
            using var conn = svc.Factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        EnsureCompany(Company);
        EnsureCompany(Other);
        var uid = svc.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);
        svc.Users.EnsureInitialAdmin(Other, OtherUser, Pass, RoleKeys.CompanyAdmin);

        var s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _materialId = svc.Materials.Create(s, new NewMaterial("MAT-HATA", "Hata sözleşmesi malzemesi"));

        _client = await _host.LoginAsync(User, Pass, Company);
        _otherClient = await _host.LoginAsync(OtherUser, Pass, Other);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    /// <summary>Gövde JSON nesnesi olmalı, <c>error</c> alanı dolu olmalı ve ham JSON parçası içermemeli.</summary>
    private static async Task<string> AssertErrorEnvelopeAsync(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.TryGetProperty("error", out var e), "Gövdede 'error' alanı yok.");
        Assert.Equal(JsonValueKind.String, e.ValueKind);
        var msg = e.GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(msg));
        // Mesaj kullanıcıya OLDUĞU GİBİ gösterilir → içinde JSON/teknik kalıntı olmamalı.
        Assert.DoesNotContain("{", msg);
        Assert.DoesNotContain("Exception", msg);
        return msg;
    }

    [Fact]
    public async Task Is_kurali_400_error_alani_dondurur()
    {
        // Kalemsiz talep → ArgumentException("En az bir kalem gerekli.")
        var r = await _client.PostAsJsonAsync("/api/requests", new
        {
            items = Array.Empty<object>(), branchId = (string?)null, requesterId = (string?)null,
            warehouseId = (string?)null, approverId = (string?)null, description = (string?)null,
            requestDate = (long?)null, submitImmediately = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("En az bir kalem gerekli.", await AssertErrorEnvelopeAsync(r));
    }

    [Fact]
    public async Task Yetki_403_error_alani_dondurur()
    {
        // Başka firmanın malzemesiyle talep → ForbiddenException
        var r = await _otherClient.PostAsJsonAsync("/api/requests", new
        {
            items = new[] { new { materialId = _materialId, quantity = 1m, vehicleId = (string?)null, note = (string?)null } },
            branchId = (string?)null, requesterId = (string?)null, warehouseId = (string?)null,
            approverId = (string?)null, description = (string?)null, requestDate = (long?)null,
            submitImmediately = true,
        });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        await AssertErrorEnvelopeAsync(r);
    }

    [Fact]
    public async Task Duzenleme_kilidi_409_error_alani_dondurur()
    {
        var create = await _client.PostAsJsonAsync("/api/maintenance/definitions", new
        {
            name = "Hata testi", intervalValue = 1000m, intervalUnit = "km",
            parentDefId = (string?)null, description = (string?)null, vehicleIds = new List<string>(),
        });
        create.EnsureSuccessStatusCode();

        var list = await ApiTestHost.JsonAsync(await _client.GetAsync("/api/maintenance/definitions"));
        var def = list.EnumerateArray().First();
        var id = def.GetProperty("id").GetString();
        var version = def.GetProperty("version").GetInt64();

        object Body(decimal iv, long v) => new
        {
            name = "Hata testi", intervalValue = iv, intervalUnit = "km", parentDefId = (string?)null,
            description = (string?)null, vehicleIds = new List<string>(), version = v,
        };
        (await _client.PutAsJsonAsync($"/api/maintenance/definitions/{id}", Body(2000m, version))).EnsureSuccessStatusCode();

        var stale = await _client.PutAsJsonAsync($"/api/maintenance/definitions/{id}", Body(3000m, version));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var msg = await AssertErrorEnvelopeAsync(stale);
        Assert.Contains("başkası tarafından değiştirildi", msg);
    }

    [Fact]
    public async Task Kimlik_dogrulanmamis_istek_401_doner()
    {
        // Anonim istemci: gövde BOŞ gelir (framework 401'i) → web genel karşılığa düşer.
        // Test bunu belgeler: 401'de 'error' alanı BEKLENMEZ.
        var r = await _host.Anonymous().GetAsync("/api/requests");

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.True(string.IsNullOrWhiteSpace(await r.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Bilinmeyen_uc_404_doner_ve_govde_bos_kalir()
    {
        var r = await _client.GetAsync("/api/boyle-bir-uc-yok");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        // Gövde boş → istemci durum koduna göre anlaşılır karşılık üretir (WEB-01 fallback'i).
        Assert.True(string.IsNullOrWhiteSpace(await r.Content.ReadAsStringAsync()));
    }
}
