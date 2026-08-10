using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// GUV-01 (2026-08-10) — TOHUM HESAPLARDA İLK GİRİŞ PAROLA ZORUNLULUĞU (servis katmanı).
///
/// Sunucu tohumlaması ortam değişkeni verilmediğinde RASTGELE geçici parola üretip konsola yazar.
/// Bu parola değiştirilmezse kurulumda kalıcı hâle geliyordu: <c>EnsureInitialAdmin</c> INSERT'ü
/// <c>must_change_password</c> kolonunu hiç yazmıyor, kolon varsayılanı (0) kalıyordu.
///
/// Mekanizma projede ZATEN vardı (Migration042 kolonu · <c>AuthService.Login</c> bayrağı okur ·
/// <c>ChangeOwnPassword</c> sıfırlar · iki arayüz de ilk giriş ekranını gösterir) — eksik olan yalnız
/// tohuma uygulanmasıydı. Bu testler bayrağın uçtan uca taşındığını doğrular.
/// </summary>
public class SeedPasswordPolicyTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly UserService _users;
    private readonly AuthService _auth;

    public SeedPasswordPolicyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_guv01_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _users = new UserService(_factory, _clock);
        _auth = new AuthService(_factory, _clock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private long FlagOf(string userId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(must_change_password,0) FROM users WHERE id=@u;";
        cmd.AddWithValue("@u", userId);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public void Tohum_Zorunluluk_Acikken_Bayrak_Yazilir()
    {
        var id = _users.EnsureInitialAdmin("A", "admin", "GeciciParola!1", RoleKeys.CompanyAdmin,
            mustChangePassword: true);

        Assert.Equal(1, FlagOf(id));
    }

    [Fact]
    public void Tohum_Varsayilan_Davranis_DEGISMEDI_Geriye_Uyumlu()
    {
        // 90'dan fazla test bu metodu parametresiz çağırıyor; varsayılan davranış korunmalı.
        var id = _users.EnsureInitialAdmin("B", "admin", "Parola!1", RoleKeys.CompanyAdmin);

        Assert.Equal(0, FlagOf(id));
    }

    [Fact]
    public void Giris_Yaniti_Zorunlulugu_Bildirir()
    {
        _users.EnsureInitialAdmin("C", "admin", "GeciciParola!1", RoleKeys.CompanyAdmin,
            mustChangePassword: true);

        var res = _auth.Login("C", "admin", "GeciciParola!1");

        Assert.True(res.Success);
        Assert.NotNull(res.Session);
        // Bayrak KİLİTLEMEZ — oturum yine kurulur; istemci ilk giriş şifre ekranını gösterir.
        Assert.True(res.MustChangePassword);
    }

    [Fact]
    public void Kullanici_Kendi_Sifresini_Belirleyince_Zorunluluk_Kalkar()
    {
        var id = _users.EnsureInitialAdmin("D", "admin", "GeciciParola!1", RoleKeys.CompanyAdmin,
            mustChangePassword: true);
        var session = new SessionContext(id, "D", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _users.ChangeOwnPassword(session, "YeniKaliciParola!2");

        Assert.Equal(0, FlagOf(id));
        var res = _auth.Login("D", "admin", "YeniKaliciParola!2");
        Assert.True(res.Success);
        Assert.False(res.MustChangePassword);   // ikinci girişte artık sorulmaz
    }

    [Fact]
    public void Eski_Gecici_Parola_Degisimden_Sonra_Gecersizdir()
    {
        var id = _users.EnsureInitialAdmin("E", "admin", "GeciciParola!1", RoleKeys.CompanyAdmin,
            mustChangePassword: true);
        var session = new SessionContext(id, "E", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _users.ChangeOwnPassword(session, "YeniKaliciParola!2");

        // Konsola bir kez yazılan geçici parola artık çalışmamalı.
        Assert.False(_auth.Login("E", "admin", "GeciciParola!1").Success);
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
