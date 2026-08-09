using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Maintenance;
using DepoWise.Infrastructure.Database;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Vehicles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ÇOK-FİRMALI (multi-tenant) İZOLASYON — GERÇEK HTTP HATTI (Paket 1, 2026-08-09).
///
/// İki firma (A ve B) kurulur; A'nın kullanıcısı B'nin kayıt id'leriyle uçlara istek atar.
/// Beklenen: **hiçbir durumda B'nin verisi dönmemeli veya değişmemeli.**
///
/// Bu testler T-1…T-6 ve Y-1/Y-2 açıklarını KANITLAR; düzeltmeden önce kırmızıdırlar.
/// Kurulum (seed) hız ve kesinlik için servis katmanından yapılır; DOĞRULAMA her zaman HTTP'dendir.
/// </summary>
// ApiTestHost SUREC-GENELI ortam degiskenleri yazar (DEPOWISE_SERVER_DATA, DEPOWISE_PG_URL...).
// Ayni degiskenlere dokunan PostgresTestGuardTests ile PARALEL kosarsa nadiren cakisir (flaky).
// Bu yuzden env-hassas testlerle AYNI koleksiyonda serilestirilir. (Is #4, 2026-08-09)
[Collection("PostgresSchema")]
public class ApiMultiCompanyTests : IClassFixture<ApiMultiCompanyTests.Fixture>, IAsyncLifetime
{
    private readonly Fixture _fx;
    public ApiMultiCompanyTests(Fixture fx) => _fx = fx;
    public Task InitializeAsync() => _fx.EnsureSeededAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>İki firmalı sabit kurulum — testler arasında paylaşılır.</summary>
    public sealed class Fixture : IAsyncDisposable
    {
        public ApiTestHost Host { get; } = new();
        private bool _seeded;

        public string CompanyA => "FIRMA-A";
        public string CompanyB => "FIRMA-B";
        public string UserA => "kullanici_a";
        public string UserB => "kullanici_b";
        public const string Pass = "Test!2026";

        public record Seed(string MaterialId, string VehicleId, string DefId, string RequestId);
        public Seed A { get; private set; } = null!;
        public Seed B { get; private set; } = null!;

        public async Task EnsureSeededAsync()
        {
            if (_seeded) return;
            _ = Host.CreateClient();                      // sunucuyu ayağa kaldır (migration + tohum)
            var svc = Host.Services.GetRequiredService<ServerServices>();

            A = SeedCompany(svc, CompanyA, UserA);
            B = SeedCompany(svc, CompanyB, UserB);
            _seeded = true;
            await Task.CompletedTask;
        }

        private Seed SeedCompany(ServerServices svc, string companyId, string userName)
        {
            using (var conn = svc.Factory.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                    "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
                cmd.AddWithValue("@id", companyId);
                cmd.ExecuteNonQuery();
            }
            var uid = svc.Users.EnsureInitialAdmin(companyId, userName, Pass, RoleKeys.CompanyAdmin);
            var s = new SessionContext(uid, companyId, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

            var matId = svc.Materials.Create(s, new NewMaterial("MAT-" + companyId, "Malzeme " + companyId));
            svc.OpeningStock.RecordOpening(s, matId, 100m, "op-" + Guid.NewGuid().ToString("N"));
            var vehId = svc.Vehicles.Create(s, new NewVehicle("ARC-" + companyId, CurrentMeter: 1000m));
            var defId = svc.MaintenanceDefinitions.Create(s, new NewMaintenanceDefinition("Periyodik", 100m, "km"));
            // T-3'ün sızıntısı ancak tanıma araç BAĞLIYSA görünür (boş liste "yeşil" yanılgısı yaratır).
            svc.MaintenanceDefinitions.SetVehicles(s, defId, new[] { vehId });

            var req = svc.Requests.Create(s, new NewRequest(
                Items: new[] { new RequestItemInput(matId, 1m) }));
            // T-5'in sızıntısı ancak OPERASYON geçmişi VARSA görünür → tohum olarak doğrudan eklenir.
            using (var conn = svc.Factory.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO request_status_history(id, request_id, from_status, to_status, by_user, reason, created_at, kind) " +
                    "VALUES(@id, @r, 'pending_ops', 'preparing', @u, @gizli, 1, 'operation');";
                cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                cmd.AddWithValue("@r", req.Id);
                cmd.AddWithValue("@u", uid);
                cmd.AddWithValue("@gizli", "GIZLI-" + companyId);   // sızarsa testte görünsün
                cmd.ExecuteNonQuery();
            }
            return new Seed(matId, vehId, defId, req.Id);
        }

        public Task<HttpClient> ClientAAsync() => Host.LoginAsync(UserA, Pass, CompanyA);
        public Task<HttpClient> ClientBAsync() => Host.LoginAsync(UserB, Pass, CompanyB);

        public async ValueTask DisposeAsync() => await ((IAsyncLifetime)Host).DisposeAsync();
    }

    private static async Task<string> BodyAsync(HttpResponseMessage r) => await r.Content.ReadAsStringAsync();

    // ── S1 · Kendi verisine erişebilmeli (regresyon: düzeltmeler meşru kullanımı bozmasın) ──────

    [Fact]
    public async Task S1_Firma_kendi_stok_bakiyesini_gorebilir()
    {
        var a = await _fx.ClientAAsync();
        var r = await a.GetAsync($"/api/stock/balance/{_fx.A.MaterialId}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await ApiTestHost.JsonAsync(r);
        Assert.Equal(100m, j.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task S1_Firma_kendi_talep_gecmisini_gorebilir()
    {
        var a = await _fx.ClientAAsync();
        var r = await a.GetAsync($"/api/requests/{_fx.A.RequestId}/history");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task S1_Firma_kendi_bakim_tanimi_araclarini_gorebilir()
    {
        var a = await _fx.ClientAAsync();
        var r = await a.GetAsync($"/api/maintenance/definitions/{_fx.A.DefId}/vehicles");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task S1_Iki_firma_da_kendi_verisine_erisebilir()
    {
        var b = await _fx.ClientBAsync();
        var r = await b.GetAsync($"/api/stock/balance/{_fx.B.MaterialId}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(100m, (await ApiTestHost.JsonAsync(r)).GetProperty("balance").GetDecimal());
    }

    // ── S2 · Başka firmanın verisini OKUYAMAMALI ────────────────────────────────────────────

    [Fact]  // T-1
    public async Task S2_T1_Baska_firmanin_stok_bakiyesi_OKUNAMAZ()
    {
        var a = await _fx.ClientAAsync();
        var r = await a.GetAsync($"/api/stock/balance/{_fx.B.MaterialId}");
        Assert.True(ApiTestHost.IsDenied(r) || (await ApiTestHost.JsonAsync(r)).GetProperty("balance").GetDecimal() == 0m,
            $"B firmasının stok bakiyesi A'ya SIZDI: {(int)r.StatusCode} {await BodyAsync(r)}");
    }

    [Fact]  // T-3
    public async Task S2_T3_Baska_firmanin_bakim_tanimi_araclari_OKUNAMAZ()
    {
        var a = await _fx.ClientAAsync();
        var r = await a.GetAsync($"/api/maintenance/definitions/{_fx.B.DefId}/vehicles");
        if (!ApiTestHost.IsDenied(r))
        {
            var arr = await ApiTestHost.JsonAsync(r);
            Assert.True(arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() == 0,
                $"B firmasının araç id'leri A'ya SIZDI: {await BodyAsync(r)}");
        }
    }

    [Fact]  // T-4
    public async Task S2_T4_Baska_firmanin_talep_gecmisi_OKUNAMAZ()
    {
        var a = await _fx.ClientAAsync();
        var r = await a.GetAsync($"/api/requests/{_fx.B.RequestId}/history");
        if (!ApiTestHost.IsDenied(r))
        {
            var arr = await ApiTestHost.JsonAsync(r);
            Assert.True(arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() == 0,
                $"B firmasının talep geçmişi A'ya SIZDI: {await BodyAsync(r)}");
        }
    }

    [Fact]  // T-5
    public async Task S2_T5_Baska_firmanin_operasyon_gecmisi_OKUNAMAZ()
    {
        var a = await _fx.ClientAAsync();
        var r = await a.GetAsync($"/api/request-ops/{_fx.B.RequestId}/history");
        if (!ApiTestHost.IsDenied(r))
        {
            var arr = await ApiTestHost.JsonAsync(r);
            Assert.True(arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() == 0,
                $"B firmasının operasyon geçmişi A'ya SIZDI: {await BodyAsync(r)}");
        }
    }

    [Fact]  // T-6
    public async Task S2_T6_Baska_firmanin_kullanici_rolleri_OKUNAMAZ()
    {
        var a = await _fx.ClientAAsync();
        var svc = _fx.Host.Services.GetRequiredService<ServerServices>();
        var userBId = UserIdOf(svc, _fx.CompanyB, _fx.UserB);

        var r = await a.GetAsync($"/api/users/{userBId}/roles");
        if (!ApiTestHost.IsDenied(r))
        {
            var arr = await ApiTestHost.JsonAsync(r);
            Assert.True(arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() == 0,
                $"B firmasının kullanıcı rolleri A'ya SIZDI: {await BodyAsync(r)}");
        }
    }

    // ── S3/S5 · Başka firmanın verisini DEĞİŞTİREMEMELİ ─────────────────────────────────────

    [Fact]  // T-2a
    public async Task S3_T2_Baska_firmanin_bakim_tanimi_GUNCELLENEMEZ()
    {
        var a = await _fx.ClientAAsync();
        var r = await a.PutAsJsonAsync($"/api/maintenance/definitions/{_fx.B.DefId}",
            new { name = "ELE GECIRILDI", intervalValue = 999m, intervalUnit = "km", vehicleIds = new[] { _fx.A.VehicleId } });
        Assert.True(ApiTestHost.IsDenied(r), $"B'nin bakım tanımı A tarafından güncellendi: {(int)r.StatusCode}");

        // B'nin tanımı DEĞİŞMEMİŞ olmalı
        var svc = _fx.Host.Services.GetRequiredService<ServerServices>();
        Assert.NotEqual("ELE GECIRILDI", DefNameOf(svc, _fx.B.DefId));
    }

    [Fact]  // T-2b / Y-2 — asıl açık: KENDİ tanımına YABANCI araç bağlama
    public async Task S5_T2b_Kendi_tanimina_BASKA_firmanin_araci_BAGLANAMAZ()
    {
        var a = await _fx.ClientAAsync();
        var r = await a.PutAsJsonAsync($"/api/maintenance/definitions/{_fx.A.DefId}",
            new { name = "Periyodik", intervalValue = 100m, intervalUnit = "km", vehicleIds = new[] { _fx.B.VehicleId } });

        var svc = _fx.Host.Services.GetRequiredService<ServerServices>();
        var linked = LinkedVehicles(svc, _fx.A.DefId);
        Assert.False(linked.Contains(_fx.B.VehicleId),
            $"A'nın tanımına B'nin aracı BAĞLANDI (istek {(int)r.StatusCode}) — çapraz-firma referans oluştu.");
    }

    [Fact]  // Y-2 — aynı açık Create yolunda
    public async Task S5_Y2_Yeni_tanim_olustururken_BASKA_firmanin_araci_BAGLANAMAZ()
    {
        var a = await _fx.ClientAAsync();
        var r = await a.PostAsJsonAsync("/api/maintenance/definitions",
            new { name = "Yeni " + Guid.NewGuid().ToString("N")[..6], intervalValue = 50m, intervalUnit = "km", vehicleIds = new[] { _fx.B.VehicleId } });

        var svc = _fx.Host.Services.GetRequiredService<ServerServices>();
        if (r.IsSuccessStatusCode)
        {
            var newId = (await ApiTestHost.JsonAsync(r)).GetProperty("id").GetString()!;
            Assert.False(LinkedVehicles(svc, newId).Contains(_fx.B.VehicleId),
                "Create ile A'nın yeni tanımına B'nin aracı BAĞLANDI.");
        }
    }

    // ── S4 · child kayıt üzerinden başka firmaya ulaşma ─────────────────────────────────────

    [Fact]
    public async Task S4_Baska_firmanin_talep_kalemleri_OKUNAMAZ()
    {
        var a = await _fx.ClientAAsync();
        var r = await a.GetAsync($"/api/requests/{_fx.B.RequestId}/items");
        if (!ApiTestHost.IsDenied(r))
        {
            var arr = await ApiTestHost.JsonAsync(r);
            Assert.True(arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0,
                $"B'nin talep kalemleri A'ya SIZDI: {await BodyAsync(r)}");
        }
    }

    // ── S6 · kimlik doğrulamasız erişim ────────────────────────────────────────────────────

    [Fact]
    public async Task S6_Kimlik_dogrulamasiz_erisim_REDDEDILIR()
    {
        var anon = _fx.Host.Anonymous();
        foreach (var path in new[]
                 {
                     $"/api/stock/balance/{_fx.A.MaterialId}",
                     $"/api/requests/{_fx.A.RequestId}/history",
                     $"/api/maintenance/definitions/{_fx.A.DefId}/vehicles",
                 })
        {
            var r = await anon.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }
    }

    // ── S8 · KD-1 regresyonu: stok hareketi uçları çalışmalı ───────────────────────────────

    [Fact]
    public async Task S8_KD1_Stok_hareketi_uclari_CALISIR()
    {
        var a = await _fx.ClientAAsync();
        foreach (var path in new[] { "/api/stock", "/api/stock/movements", $"/api/materials/{_fx.A.MaterialId}/movements" })
        {
            var r = await a.GetAsync(path);
            Assert.True(r.StatusCode == HttpStatusCode.OK, $"{path} → {(int)r.StatusCode}: {await BodyAsync(r)}");
        }
    }

    // ── ham okuma yardımcıları (yalnız DOĞRULAMA için) ─────────────────────────────────────

    private static string UserIdOf(ServerServices svc, string companyId, string username)
    {
        using var conn = svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM users WHERE company_id=@c AND username=@u;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@u", username);
        return (string)cmd.ExecuteScalar()!;
    }

    private static string DefNameOf(ServerServices svc, string defId)
    {
        using var conn = svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM maintenance_definitions WHERE id=@id;";
        cmd.AddWithValue("@id", defId);
        return (string)cmd.ExecuteScalar()!;
    }

    private static HashSet<string> LinkedVehicles(ServerServices svc, string defId)
    {
        using var conn = svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT vehicle_id FROM maintenance_definition_vehicles WHERE definition_id=@d;";
        cmd.AddWithValue("@d", defId);
        var set = new HashSet<string>(StringComparer.Ordinal);
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }
}
