using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G6-06 (PRT-01 Grup 6, 2026-08-11) — ROL / FİRMA YETKİ KONTROL AUDIT'İ.
///
/// Bulunan durum: <c>RoleGrantService.SetMatrix</c> ve <c>CompanyGrantService.SetLevels</c> tam-değiştirme
/// (DELETE + INSERT) yapıyor ama <c>AuditWriter.Write</c> ÇAĞIRMIYORDU. Oysa aynı ailedeki BranchService,
/// UserService, PermissionService ve CompanyPurgeService hepsi audit yazıyor. Bunlar platformun en yetkili
/// iki işlemidir (bir ekranı bir role / bir firmaya tamamen kapatır) ve izsizdi.
///
/// Testler: (1) iki işlem de audit üretir, (2) aktör + firma + hedef doğru, (3) BAŞARISIZ işlemde
/// SAHTE audit oluşmaz (yetkisiz aktör reddedilir ve iz bırakmaz).
/// </summary>
public class GrantMatrixAuditTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly UserService _users;
    private readonly RoleGrantService _roleGrants;
    private readonly CompanyGrantService _companyGrants;

    public GrantMatrixAuditTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_gaudit_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _users = new UserService(_factory, _clock);
        _roleGrants = new RoleGrantService(_factory, _clock);
        _companyGrants = new CompanyGrantService(_factory, _clock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private SessionContext SuperAdmin(string company)
    {
        var id = _users.EnsureInitialAdmin(company, "sa_" + company, "root123", RoleKeys.SuperAdmin);
        return new SessionContext(id, company, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
    }

    private SessionContext CompanyAdmin(string company)
    {
        var id = _users.EnsureInitialAdmin(company, "adm_" + company, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(id, company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private List<(string Company, string User, string Type, string Entity, string Action, string? After)> Audits()
    {
        var list = new List<(string, string, string, string, string, string?)>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT company_id, user_id, entity_type, entity_id, action, after_json FROM audit_logs " +
            "WHERE entity_type IN ('role_permissions','company_permissions') ORDER BY created_at;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1), r.GetString(2), r.GetString(3),
                r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5)));
        return list;
    }

    [Fact]
    public void Rol_Yetki_Matrisi_Degisimi_AUDIT_Yazar()
    {
        var su = SuperAdmin("AUD");

        _roleGrants.SetMatrix(su, new Dictionary<string, IReadOnlyList<string>>
        {
            [RoleKeys.Staff] = new[] { "fuel", "reports" },
        });

        var a = Assert.Single(Audits());
        Assert.Equal("role_permissions", a.Type);
        Assert.Equal("matrix", a.Entity);
        Assert.Equal(AuditActions.Update, a.Action);
        Assert.Equal(su.UserId, a.User);         // aktör
        Assert.Equal("AUD", a.Company);          // iz aktörün firmasında
        Assert.Equal($"{{\"{RoleKeys.Staff}\":2}}", a.After);   // özet: yalnız SAYI
    }

    [Fact]
    public void Firma_Yetki_Duzeyi_Degisimi_AUDIT_Yazar_Ve_Hedef_Firmayi_Tasir()
    {
        var su = SuperAdmin("AUD");
        _users.EnsureInitialAdmin("HEDEF", "adm_hedef", "admin123", RoleKeys.CompanyAdmin);

        _companyGrants.SetLevels(su, "HEDEF", new Dictionary<string, string>
        {
            ["fuel"] = CompanyGrantService.LevelAdmin,
            ["reports"] = CompanyGrantService.LevelSuper,
        });

        var a = Assert.Single(Audits());
        Assert.Equal("company_permissions", a.Type);
        Assert.Equal("HEDEF", a.Entity);         // hangi firmanın düzeyleri değişti
        Assert.Equal("AUD", a.Company);          // iz aktörün firmasında
        Assert.Equal(su.UserId, a.User);
        Assert.Equal("{\"admin\":1,\"superadmin\":1}", a.After);
    }

    [Fact]
    public void Bos_Matris_De_AUDIT_Yazar()
    {
        // "Hepsini serbest bırak" da bir değişikliktir ve izlenebilir olmalı.
        var su = SuperAdmin("AUD");

        _roleGrants.SetMatrix(su, new Dictionary<string, IReadOnlyList<string>>());

        var a = Assert.Single(Audits());
        Assert.Equal("{}", a.After);
    }

    [Fact]
    public void BASARISIZ_Islemde_SAHTE_Audit_Olusmaz()
    {
        var admin = CompanyAdmin("AUD");   // süper admin DEĞİL → iki işlem de reddedilmeli

        Assert.Throws<ForbiddenException>(() => _roleGrants.SetMatrix(admin,
            new Dictionary<string, IReadOnlyList<string>> { [RoleKeys.Staff] = new[] { "fuel" } }));
        Assert.Throws<ForbiddenException>(() => _companyGrants.SetLevels(admin, "AUD",
            new Dictionary<string, string> { ["fuel"] = CompanyGrantService.LevelAdmin }));

        Assert.Empty(Audits());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }
}
