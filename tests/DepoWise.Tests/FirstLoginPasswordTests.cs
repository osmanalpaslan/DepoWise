using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>İlk giriş şifre belirleme (Migration042): yeni kullanıcı must_change_password=1 ile başlar;
/// ilk girişte MustChangePassword=true döner; kendi şifresini belirleyince sıfırlanır; admin sıfırlaması
/// yeniden zorunlu kılar.</summary>
public class FirstLoginPasswordTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();

    public FirstLoginPasswordTests()
    {
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "depowise_fl_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    [Fact]
    public void YeniKullanici_IlkGiris_SifreZorunlu_SonraTemiz()
    {
        var users = new UserService(_factory, _clock);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var auth = new AuthService(_factory, _clock);
        var su = auth.Login("A", "root", "root123").Session!;
        // Süper admin ilk kurulum kullanıcısı must_change taşımaz.
        Assert.False(auth.Login("A", "root", "root123").MustChangePassword);

        var uid = users.CreateUser(su, new NewUser("pers", "gecici1", null, new[] { RoleKeys.Staff }, CompanyId: "A"));

        // İlk giriş: parola doğru ama şifre değiştirme zorunlu.
        var first = auth.Login("A", "pers", "gecici1");
        Assert.True(first.Success);
        Assert.True(first.MustChangePassword);

        // Kendi şifresini belirler → zorunluluk kalkar, yeni şifre geçerli.
        users.ChangeOwnPassword(first.Session!, "kalici9");
        var second = auth.Login("A", "pers", "kalici9");
        Assert.True(second.Success);
        Assert.False(second.MustChangePassword);
        Assert.False(auth.Login("A", "pers", "gecici1").Success); // eski şifre artık geçersiz
    }

    [Fact]
    public void AdminSifreSifirlar_YenidenZorunlu_KendiDegistirirse_Zorunlu_Degil()
    {
        var users = new UserService(_factory, _clock);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var auth = new AuthService(_factory, _clock);
        var su = auth.Login("A", "root", "root123").Session!;
        var uid = users.CreateUser(su, new NewUser("pers", "gecici1", null, new[] { RoleKeys.Staff }, CompanyId: "A"));
        users.ChangeOwnPassword(auth.Login("A", "pers", "gecici1").Session!, "kalici9"); // ilk giriş tamam

        // Admin başkasının şifresini sıfırlar → yeniden zorunlu.
        users.ChangePassword(su, uid, "reset123");
        Assert.True(auth.Login("A", "pers", "reset123").MustChangePassword);

        // Süper admin KENDİ şifresini değiştirirse zorunlu değil.
        users.ChangePassword(su, su.UserId, "root999");
        Assert.False(auth.Login("A", "root", "root999").MustChangePassword);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); System.IO.File.Delete(_dbPath); } catch { }
    }
}
