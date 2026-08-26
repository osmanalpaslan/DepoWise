using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ TENANT SÜPÜRMESİ · <b>OKUMA + DEĞİŞTİRME + SİLME</b> ═══ (denetim 2026-08-26, dördüncü tur)
///
/// Mevcut süpürmeler iki boyutu ölçüyordu: liste uçları (<see cref="ApiTenantSweepTests"/>) ve yazma
/// uçları (<see cref="ApiYazmaTenantSweepTests"/>). Eksik olan üçüncü boyut <b>tek kayıt</b> uçlarıydı:
/// <c>GET/PUT/DELETE /api/&lt;varlık&gt;/&lt;B firmasının kimliği&gt;</c>.
///
/// <b>Kanıt ölçütü (kullanıcı şartı):</b> HTTP durum kodu TEK BAŞINA yeterli sayılmaz. Her senaryoda
/// üç şey birden doğrulanır:
/// <list type="number">
///   <item>B firmasının <b>gizli içeriği</b> yanıt gövdesinde GEÇMEMELİ (okunmadı),</item>
///   <item>B firmasının satırı veritabanında <b>DEĞİŞMEMELİ</b> (yazılmadı),</item>
///   <item>B firmasının satırı <b>SİLİNMEMELİ</b> (soft-delete dahil).</item>
/// </list>
/// </summary>
[Collection("PostgresSchema")]
public class ApiTenantOkumaSilmeSweepTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CoA = "TOS-A";
    private const string CoB = "TOS-B";
    private const string Pass = "Tos!2026";

    private ServerServices _svc = null!;
    private HttpClient _adminA = null!;
    private string _malzemeB = "", _aracB = "", _personelB = "", _subeB = "", _kullaniciB = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        foreach (var (id, ad) in new[] { (CoA, "A Firmasi"), (CoB, "B Firmasi") })
            Calistir("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) " +
                     "VALUES(@c,@n,1,1,1,0,5,20,5) ON CONFLICT(id) DO NOTHING;", ("@c", id), ("@n", ad));

        // B firmasının GİZLİ kayıtları — içerikleri A'nın hiçbir yanıtında görünmemeli.
        _subeB = Yeni();
        Calistir("INSERT INTO branches(id,company_id,parent_id,name,kind,created_at,updated_at,version,is_deleted) " +
                 "VALUES(@id,@c,NULL,'B-GIZLI-SUBE','branch',1,1,1,0);", ("@id", _subeB), ("@c", CoB));

        _malzemeB = Yeni();
        Calistir("INSERT INTO materials(id,company_id,code,name,unit_id,min_stock,created_at,updated_at,version,is_deleted) " +
                 "VALUES(@id,@c,'B-GIZLI-KOD','B-GIZLI-MALZEME',NULL,'0',1,1,1,0);", ("@id", _malzemeB), ("@c", CoB));

        _aracB = Yeni();
        Calistir("INSERT INTO vehicles(id,company_id,internal_code,plate,current_meter,meter_unit,status," +
                 "created_at,updated_at,version,is_deleted) " +
                 "VALUES(@id,@c,'B-GIZLI-ARAC','34BGZ99','500','km','active',1,1,1,0);", ("@id", _aracB), ("@c", CoB));

        _personelB = Yeni();
        Calistir("INSERT INTO personnel(id,company_id,branch_id,full_name,is_active,is_field_staff," +
                 "created_at,updated_at,version,is_deleted) " +
                 "VALUES(@id,@c,@b,'B GIZLI PERSONEL',1,0,1,1,1,0);", ("@id", _personelB), ("@c", CoB), ("@b", _subeB));

        _kullaniciB = _svc.Users.EnsureInitialAdmin(CoB, "b_gizli_kullanici", Pass, RoleKeys.CompanyAdmin);

        _svc.Users.EnsureInitialAdmin(CoA, "tos_admin_a", Pass, RoleKeys.CompanyAdmin);
        _adminA = await _host.LoginAsync("tos_admin_a", Pass, CoA);
    }

    public Task DisposeAsync() { _host.Dispose(); return Task.CompletedTask; }

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

    /// <summary>Yanıt gövdesinde B'nin gizli metni GEÇMEMELİ.</summary>
    private static async Task IcerikSizmadi(HttpResponseMessage r, string gizli)
    {
        var govde = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain(gizli, govde, StringComparison.OrdinalIgnoreCase);
    }

    // ── OKUMA ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TOS1_Baska_Firmanin_Malzeme_Karti_Okunamaz()
        => await IcerikSizmadi(await _adminA.GetAsync($"/api/materials/{_malzemeB}"), "B-GIZLI-MALZEME");

    [Fact]
    public async Task TOS2_Baska_Firmanin_Arac_Karti_Okunamaz()
        => await IcerikSizmadi(await _adminA.GetAsync($"/api/vehicles/{_aracB}"), "B-GIZLI-ARAC");

    [Fact]
    public async Task TOS3_Baska_Firmanin_Kullanici_Rolleri_Okunamaz()
    {
        var r = await _adminA.GetAsync($"/api/users/{_kullaniciB}/roles");
        Assert.True(ApiTestHost.IsDenied(r), $"beklenen: reddedilme, gelen: {(int)r.StatusCode}");
    }

    [Fact]
    public async Task TOS4_Baska_Firmanin_Arac_Fotograflari_Okunamaz()
        => await IcerikSizmadi(await _adminA.GetAsync($"/api/vehicles/{_aracB}/photos"), "B-GIZLI");

    [Fact]
    public async Task TOS5_Baska_Firmanin_Personeli_Listede_Gorunmez()
        => await IcerikSizmadi(await _adminA.GetAsync("/api/personnel"), "B GIZLI PERSONEL");

    [Fact]
    public async Task TOS6_Baska_Firmanin_Subesi_Listede_Gorunmez()
        => await IcerikSizmadi(await _adminA.GetAsync("/api/branches"), "B-GIZLI-SUBE");

    // ── DEĞİŞTİRME ────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ B'nin araç kaydı A tarafından DEĞİŞTİRİLEMEZ (satır veritabanında aynı kalmalı).</summary>
    [Fact]
    public async Task TOS7_Baska_Firmanin_Araci_Degistirilemez()
    {
        await _adminA.PutAsJsonAsync($"/api/vehicles/{_aracB}", new
        {
            internalCode = "ELE-GECIRILDI", plate = "00XXX00", productionYear = 2000,
            currentMeter = 1, meterUnit = "km",
        });

        Assert.Equal("B-GIZLI-ARAC", Oku<string>("SELECT internal_code FROM vehicles WHERE id=@i;", ("@i", _aracB)));
        Assert.Equal("34BGZ99", Oku<string>("SELECT plate FROM vehicles WHERE id=@i;", ("@i", _aracB)));
    }

    /// <summary>⭐ B'nin personel kaydı A tarafından DEĞİŞTİRİLEMEZ.</summary>
    [Fact]
    public async Task TOS8_Baska_Firmanin_Personeli_Degistirilemez()
    {
        await _adminA.PutAsJsonAsync($"/api/personnel/{_personelB}", new
        {
            fullName = "ELE GECIRILDI", title = "X", phone = "0", isActive = false,
        });

        Assert.Equal("B GIZLI PERSONEL", Oku<string>("SELECT full_name FROM personnel WHERE id=@i;", ("@i", _personelB)));
    }

    /// <summary>⭐ B'nin kullanıcısının PAROLASI A tarafından değiştirilemez (hesap ele geçirme).</summary>
    [Fact]
    public async Task TOS9_Baska_Firmanin_Kullanici_Parolasi_Degistirilemez()
    {
        var oncekiHash = Oku<string>("SELECT password_hash FROM users WHERE id=@i;", ("@i", _kullaniciB));

        await _adminA.PostAsJsonAsync($"/api/users/{_kullaniciB}/password", new { password = "Saldirgan!2026" });

        Assert.Equal(oncekiHash, Oku<string>("SELECT password_hash FROM users WHERE id=@i;", ("@i", _kullaniciB)));
    }

    /// <summary>⭐ B'nin kullanıcısı A tarafından PASİFE ALINAMAZ (hizmet engelleme).</summary>
    [Fact]
    public async Task TOS10_Baska_Firmanin_Kullanicisi_Pasife_Alinamaz()
    {
        await _adminA.PostAsJsonAsync($"/api/users/{_kullaniciB}/active", new { active = false });

        Assert.Equal(1, Oku<long>("SELECT is_active FROM users WHERE id=@i;", ("@i", _kullaniciB)));
    }

    // ── SİLME ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ B'nin malzemesi A tarafından SİLİNEMEZ (soft-delete dahil).</summary>
    [Fact]
    public async Task TOS11_Baska_Firmanin_Malzemesi_Silinemez()
    {
        await _adminA.DeleteAsync($"/api/materials/{_malzemeB}");

        Assert.Equal(1, Oku<long>("SELECT COUNT(*) FROM materials WHERE id=@i AND is_deleted=0;", ("@i", _malzemeB)));
    }

    /// <summary>⭐ B'nin aracı A tarafından SİLİNEMEZ.</summary>
    [Fact]
    public async Task TOS12_Baska_Firmanin_Araci_Silinemez()
    {
        await _adminA.DeleteAsync($"/api/vehicles/{_aracB}");

        Assert.Equal(1, Oku<long>("SELECT COUNT(*) FROM vehicles WHERE id=@i AND is_deleted=0;", ("@i", _aracB)));
    }

    /// <summary>⭐ B'nin şubesi A tarafından SİLİNEMEZ.</summary>
    [Fact]
    public async Task TOS13_Baska_Firmanin_Subesi_Silinemez()
    {
        await _adminA.DeleteAsync($"/api/branches/{_subeB}");

        Assert.Equal(1, Oku<long>("SELECT COUNT(*) FROM branches WHERE id=@i AND is_deleted=0;", ("@i", _subeB)));
    }

    /// <summary>⭐ B'nin personeli A tarafından SİLİNEMEZ.</summary>
    [Fact]
    public async Task TOS14_Baska_Firmanin_Personeli_Silinemez()
    {
        await _adminA.DeleteAsync($"/api/personnel/{_personelB}");

        Assert.Equal(1, Oku<long>("SELECT COUNT(*) FROM personnel WHERE id=@i AND is_deleted=0;", ("@i", _personelB)));
    }

    /// <summary>Kontrol: A KENDİ kaydını okuyabiliyor (test aşırı kısıtlayıcı değil).</summary>
    [Fact]
    public async Task TOS15_Kendi_Firmasinin_Kaydini_Okuyabiliyor()
    {
        var kendi = Yeni();
        Calistir("INSERT INTO materials(id,company_id,code,name,unit_id,min_stock,created_at,updated_at,version,is_deleted) " +
                 "VALUES(@id,@c,'A-KOD','A-MALZEME',NULL,'0',1,1,1,0);", ("@id", kendi), ("@c", CoA));

        var govde = await (await _adminA.GetAsync("/api/materials")).Content.ReadAsStringAsync();

        Assert.Contains("A-MALZEME", govde, StringComparison.Ordinal);
    }
}
