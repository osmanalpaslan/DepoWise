using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// "Tüm Şubeler" modu + süper adminin aktif firma değiştirmesi (2026-07-16 kullanıcı kuralı).
///
/// Kural: "Tüm Şubeler" modunda (OperatingBranchId = null) şube bazlı ekranlarda ŞUBESİZ KAYIT
/// OLUŞAMAZ — aksi halde stok hareketi branch_id NULL düşer ve hangi şantiyeye ait olduğu kaybolur.
/// Koruma sınır katmanındadır (web sayfaları + masaüstü VM'leri); bu testler korumanın dayandığı
/// oturum/veri gerçeklerini sabitler.
///
/// ⭐ STK-12 (2026-09-04): STOK ekranlarında (Giriş-Çıkış · Sayım) koruma <b>kaldırılmadı, yeri
/// değişti</b> — "hiçbir şey yapamazsın" yerine "yazılacak depoyu açıkça seç". Belirsiz kayıt hâlâ
/// imkânsızdır; kullanıcı yalnız çıkıp yeniden giriş yapmak zorunda kalmaz. Diğer ekranlar
/// (Yakıt · Bakım · Muayene · Malzemeler · Araçlar) iki platformda da eskisi gibi işlem yapmaz.
/// </summary>
public class AllBranchesGuardTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly UserService _users;
    private readonly CompanyService _companies;
    private readonly BranchService _branches;
    private readonly AuthService _auth;

    public AllBranchesGuardTests()
    {
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "depowise_abg_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _users = new UserService(_factory, _clock);
        _companies = new CompanyService(_factory, _clock);
        _branches = new BranchService(_factory, _clock);
        _auth = new AuthService(_factory, _clock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private SessionContext SuperAdmin()
    {
        _users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        return _auth.Login("A", "root", "root123").Session!;
    }

    /// <summary>"Tüm Şubeler" = çalışma şubesi YOK. Koruma tam olarak bu duruma bakar.</summary>
    [Fact]
    public void TumSubeler_CalismaSubesiYok()
    {
        var su = SuperAdmin();

        su.OperatingBranchId = null;                       // girişte "🌐 Tüm Şubeler" seçildi
        Assert.True(string.IsNullOrEmpty(su.OperatingBranchId));

        var b = _branches.Create(su, new NewBranch("Merkez"));
        su.OperatingBranchId = b;                          // girişte gerçek şube seçildi
        Assert.False(string.IsNullOrEmpty(su.OperatingBranchId));
    }

    /// <summary>Şube seçiliyken kapsam kontrolü geçmeli (koruma yalnız şubesiz modda devreye girer).</summary>
    [Fact]
    public void SubeSecili_KapsamGecerli()
    {
        var su = SuperAdmin();
        var scope = new DepoWise.Infrastructure.Org.ScopeResolver(_factory);
        var b = _branches.Create(su, new NewBranch("Merkez"));

        su.OperatingBranchId = b;
        Assert.True(scope.IsBranchAllowed(su, b));
        scope.EnsureBranchAllowed(su, b);   // fırlatmamalı
    }

    /// <summary>Aktif firma değişince o firmanın şubeleri gelir; eski firmanın şubesi ASLA görünmez.
    /// Üst bardaki "Aktif Firma" seçicisi bu uca dayanır (oturum firması değişir).</summary>
    [Fact]
    public void AktifFirmaDegisince_SadeceOFirmaninSubeleriGelir()
    {
        var su = SuperAdmin();
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "B");
        _branches.Create(su, new NewBranch("A-Merkez"));                       // A firması
        _branches.Create(su, new NewBranch("B-Merkez"), companyId: "B");       // B firması

        var aList = _branches.List(su, "A");
        var bList = _branches.List(su, "B");

        Assert.Single(aList);
        Assert.Equal("A-Merkez", aList[0].Name);
        Assert.Single(bList);
        Assert.Equal("B-Merkez", bList[0].Name);
        Assert.DoesNotContain(bList, x => x.Name == "A-Merkez");
    }

    /// <summary>Firma değiştikten sonra ESKİ firmanın şubesi yeni firmada geçersizdir — kullanıcıya
    /// atanamaz. (Üst bar seçicisi firma değişince şube bağlamını bu yüzden sıfırlar.)</summary>
    [Fact]
    public void FirmaDegisince_EskiFirmaninSubesi_YeniFirmadaGecersiz()
    {
        var su = SuperAdmin();
        _companies.Create(su, new NewCompany("B Firma"), explicitId: "B");
        var aBranch = _branches.Create(su, new NewBranch("A-Merkez"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _users.ValidateBranchForNewUser(su, "B", new[] { RoleKeys.Staff }, aBranch));
        Assert.Contains("bu firmaya ait değil", ex.Message);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); System.IO.File.Delete(_dbPath); } catch { }
    }
}
