using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// Kullanıcı yönetimi (2026-07-25):
/// (1) Şifre SIFIRLA — admin belirli şifre yazmaz; şifre kullanıcı adına sıfırlanır + kullanıcı ilk girişte
///     kendi şifresini belirler (must_change). Yalnız admin yapar.
/// (2) Kullanıcı listesi TÜM oturum sahiplerine açık; admin OLMAYAN aktör SINIRLI görür (rol gizli).
/// </summary>
public class UserResetVisibilityTests : IDisposable
{
    private readonly string _db;
    private readonly SqliteConnectionFactory _f;
    private readonly TestClock _clock = new();

    public UserResetVisibilityTests()
    {
        _db = Path.Combine(Path.GetTempPath(), "dw_userreset_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_db);
        new MigrationRunner(_f).Run();
    }
    private sealed class TestClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000); }

    private SessionContext Admin(UserService u, string co)
    {
        var id = u.EnsureInitialAdmin(co, "adm_" + co, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(id, co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    [Fact]
    public void SifreSifirla_KullaniciAdinaSifirlar_IlkGirisZorunlu()
    {
        var users = new UserService(_f, _clock);
        var auth = new AuthService(_f, _clock);
        var admin = Admin(users, "A");
        var staffId = users.CreateUser(admin, new NewUser("per", "gizli123", null, new[] { RoleKeys.Staff }, CompanyId: "A"));

        var temp = users.ResetPassword(admin, staffId);
        Assert.Equal("per", temp);                                  // geçici şifre = kullanıcı adı

        var login = auth.Login("A", "per", "per");
        Assert.True(login.Success);                                 // kullanıcı adıyla girilir
        Assert.True(login.MustChangePassword);                      // ilk girişte kendi şifresini belirleyecek
        Assert.False(auth.Login("A", "per", "gizli123").Success);   // eski şifre geçersiz
    }

    [Fact]
    public void SifreSifirla_YalnizAdmin()
    {
        var users = new UserService(_f, _clock);
        var auth = new AuthService(_f, _clock);
        var admin = Admin(users, "A");
        var staffId = users.CreateUser(admin, new NewUser("per", "gizli123", null, new[] { RoleKeys.Staff }, CompanyId: "A"));
        var staff = auth.Login("A", "per", "gizli123").Session!;

        // Admin olmayan (Personel) şifre sıfırlayamaz
        Assert.Throws<ForbiddenException>(() => users.ResetPassword(staff, staffId));
    }

    [Fact]
    public void KullaniciListesi_HerkeseAcik_PersonelRolGizli()
    {
        var users = new UserService(_f, _clock);
        var auth = new AuthService(_f, _clock);
        var admin = Admin(users, "A");
        users.CreateUser(admin, new NewUser("per", "gizli123", null, new[] { RoleKeys.Staff }, CompanyId: "A"));
        var staff = auth.Login("A", "per", "gizli123").Session!;

        // Personel listeyi GÖREBİLİR (throw yok) ama rol alanı GİZLİ (sınırlı liste)
        var asStaff = users.ListUsers(staff);
        Assert.NotEmpty(asStaff);
        Assert.All(asStaff, r => Assert.Equal("", r.Roles));

        // Admin TAM liste görür — rol dolu
        var asAdmin = users.ListUsers(admin);
        Assert.Contains(asAdmin, r => !string.IsNullOrEmpty(r.Roles));
    }

    [Fact]
    public void ImportServerUser_SunucuIdisiyle_Yerele_Isler_GirisCalisir()
    {
        var users = new UserService(_f, _clock);
        var auth = new AuthService(_f, _clock);
        var admin = Admin(users, "A");   // firma A + admin oluşur

        // Masaüstü çevrimiçi create → sunucu id'siyle yerele işlenir (çift kayıt olmasın diye SUNUCU id'si).
        users.ImportServerUser("srv-id-123", "A", "sube.kul", "parola12", "Şube Kul",
            branchId: null, canViewAllBranches: false, mustChangePassword: true, new[] { RoleKeys.Staff });

        // Yerelde sunucu id'siyle görünür (kaybolmaz)
        var list = users.ListUsers(admin);
        Assert.Contains(list, u => u.Id == "srv-id-123" && u.Username == "sube.kul");

        // Yerel giriş plaintext ile çalışır + ilk girişte şifre belirleme zorunlu
        var login = auth.Login("A", "sube.kul", "parola12");
        Assert.True(login.Success);
        Assert.True(login.MustChangePassword);
    }

    public void Dispose() { try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_db); } catch { } }
}
