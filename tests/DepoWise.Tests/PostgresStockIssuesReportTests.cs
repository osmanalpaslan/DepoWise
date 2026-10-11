using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ 2026-10-11 — DEPO ÇIKIŞLARI raporu POSTGRESQL PARİTESİ (üretim DB PostgreSQL). Teslim alan, çoklu malzeme,
/// iptal edilen çıkışın düşmesi, maliyet merkezi alt sorgusu ve tutar PG lehçesinde sınanır.
/// ⚠️ Yalnız DEPOWISE_PG_URL (doğrulanmış BOŞ test veritabanı) ile koşar; yoksa ATLANIR.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresStockIssuesReportTests
{
    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    [SkippableFact]
    public void PostgreSQLde_depo_cikislari_teslim_alan_ve_filtreler_calisir()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PostgresTestGuard.Url!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();
        void Exec(string sql) { using var c = factory.Create(); using var k = c.CreateCommand(); k.CommandText = sql; k.ExecuteNonQuery(); }
        Exec("INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
             "VALUES('A', 'A', 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;");

        var clock = new TestClock();
        var uid = new UserService(factory, clock).EnsureInitialAdmin("A", "admin_cks", "admin123", RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var depo = new BranchService(factory, clock).Create(s, new NewBranch("PG Depo"));
        var mats = new MaterialService(factory, clock);
        var m1 = mats.Create(s, new NewMaterial("PG-YF", "Yağ filtresi"));
        var m2 = mats.Create(s, new NewMaterial("PG-EL", "Eldiven"));
        Exec("INSERT INTO personnel(id,company_id,full_name,is_active,created_at,updated_at,version,is_deleted) VALUES('pg-ali','A','Ali Usta',1,1,1,1,0);");
        Exec("INSERT INTO cost_centers(id,company_id,code,name,status,created_at,updated_at,version,is_deleted) VALUES('pg-cc','A','MM1','Şantiye 1','active',1,1,1,0);");

        var stock = new StockService(factory, clock);
        var reports = new ReportService(factory);
        stock.ReceiveIn(s, new[] { new StockLine(m1, 50m, 40m), new StockLine(m2, 50m, 5m) }, "pg-in", depo);
        var cikis = stock.IssueOut(s, new[] { new StockLine(m1, 3m, 40m), new StockLine(m2, 2m, 5m) }, "pg-out", depo, personnelId: "pg-ali");
        Exec($"INSERT INTO cost_center_links(id,company_id,cost_center_id,entity_type,entity_id,created_at,updated_at,version,is_deleted) VALUES('pg-ccl','A','pg-cc','stock_document','{cikis.DocumentId}',1,1,1,0);");
        var iptal = stock.IssueOut(s, new[] { new StockLine(m1, 7m, 40m) }, "pg-out2", depo, personnelId: "pg-ali");
        stock.ReverseDocument(s, iptal.DocumentId, "test");

        var genis = new ReportRequest(true, FromDate: 0, ToDate: 4_102_444_800_000L);
        var t = reports.Run(s, "stock-issues", genis);
        Assert.Equal(2, t.Rows.Count);                                       // giriş + iptal edilen çıkış YOK
        Assert.All(t.Rows, r => Assert.Equal("Ali Usta", (string)r[1]!));    // Teslim Alan
        int cc = t.Headers.ToList().IndexOf("Maliyet Merkezi"), tutar = t.Headers.ToList().IndexOf("Tutar");
        Assert.All(t.Rows, r => Assert.Equal("Şantiye 1", (string)r[cc]!));
        Assert.Equal(130.0, ((NumCell)t.TotalRow![tutar]!).Value, 3);       // 3×40 + 2×5

        Assert.Single(reports.Run(s, "stock-issues", genis with { MaterialIds = new[] { m2 } }).Rows);
        Assert.Equal(2, reports.Run(s, "stock-issues", genis with { MaterialIds = new[] { m1, m2 }, RecipientIds = new[] { "pg-ali" } }).Rows.Count);
        Assert.Empty(reports.Run(s, "stock-issues", genis with { RecipientIds = new[] { "yok" } }).Rows);
    }
}
