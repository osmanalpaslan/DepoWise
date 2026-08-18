using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// DENETİM G2 (2026-08-18) — RAPORLARDA ŞUBE KAPSAMI.
///
/// <b>DEN-E2</b> — "Stok Durumu" raporu şube kapsamını HİÇ uygulamıyordu:
/// <c>NormalizeLocations(req.LocationIds)</c> istekten geleni AYNEN alıyordu. İki sonuç vardı:
/// (a) filtre boşken FİRMA GENELİ toplam dönüyor, şubeyle sınırlı kullanıcı tüm firmanın stoğunu
/// görüyordu; (b) istek gövdesine BAŞKA şubenin depo kimliği yazılırsa o deponun stoğu dönüyordu
/// (parametre manipülasyonu — fail-open). Kardeş rapor <c>StockMovements</c> aynı işi doğru yapıyordu.
///
/// <b>DEN-E1</b> — "Şube Bazlı Özet" raporu tüm şubelerin adlarını ve kayıt sayılarını gösteriyordu.
/// </summary>
public class ReportBranchScopeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly ReportService _reports;
    private readonly BranchService _branches;
    private readonly SessionContext _admin;
    private readonly string _subeA, _subeB;
    private const string Co = "RPT-CO";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public ReportBranchScopeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_rptscope_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _reports = new ReportService(_factory, _clock);
        _branches = new BranchService(_factory, _clock);

        Sql($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','T',1,1,1,0);");
        _admin = new SessionContext("admin", Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _subeA = _branches.Create(_admin, new NewBranch("ŞUBE A"));
        _subeB = _branches.Create(_admin, new NewBranch("ŞUBE B"));

        // İki malzeme, iki şubede bakiye.
        Sql($"INSERT INTO materials(id,company_id,code,name,unit_id,min_stock,created_at,updated_at,version,is_deleted) " +
            $"VALUES('M1','{Co}','K1','Çimento',NULL,'0',1,1,1,0);");
        Sql($"INSERT INTO materials(id,company_id,code,name,unit_id,min_stock,created_at,updated_at,version,is_deleted) " +
            $"VALUES('M2','{Co}','K2','Demir',NULL,'0',1,1,1,0);");
        Sql($"INSERT INTO stock_balances(company_id,material_id,location_id,quantity,updated_at) VALUES('{Co}','M1','{_subeA}','100',1);");
        Sql($"INSERT INTO stock_balances(company_id,material_id,location_id,quantity,updated_at) VALUES('{Co}','M2','{_subeB}','250',1);");
    }

    private void Sql(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Yalnız ŞUBE A'ya yetkili personel (admin bypass YOK).</summary>
    private SessionContext SadeceA() => new("kul", Co, new[] { RoleKeys.Staff },
        new PermissionSet(new[] { new ModulePermission("reports", true, false, false, false) }, Array.Empty<string>()))
    { ScopeBranchIds = new[] { _subeA } };

    private static decimal Toplam(TableModel t, int kolon)
    {
        decimal s = 0;
        foreach (var r in t.Rows) s += Money.Parse(Convert.ToString(r[kolon]));
        return s;
    }

    // ── DEN-E2 ───────────────────────────────────────────────────────────────────────────────────
    /// <summary>Filtre BOŞKEN bile kapsam uygulanmalı — eskiden firma geneli toplam dönüyordu.</summary>
    [Fact]
    public void StokDurumu_FiltresizKen_Yalniz_Izinli_Subeyi_Toplar()
    {
        var t = _reports.StockStatus(SadeceA(), new ReportRequest(Executed: true));

        // M1 (ŞUBE A) = 100 görünmeli, M2 (ŞUBE B) = 250 GÖRÜNMEMELİ → toplam 100.
        Assert.Equal(100m, Toplam(t, 2));
    }

    /// <summary>Admin/sınırsız kullanıcıda eski davranış BOZULMAMALI (firma geneli).</summary>
    [Fact]
    public void StokDurumu_Sinirsiz_Kullanicida_Firma_Geneli_Kalir()
    {
        var t = _reports.StockStatus(_admin, new ReportRequest(Executed: true));
        Assert.Equal(350m, Toplam(t, 2));   // 100 + 250
    }

    /// <summary>⭐ Parametre manipülasyonu: kapsam dışı depo istenirse veri SIZDIRILMAMALI.</summary>
    [Fact]
    public void StokDurumu_Kapsam_Disi_Depo_Istenirse_BOS_Doner()
    {
        var t = _reports.StockStatus(SadeceA(),
            new ReportRequest(Executed: true, LocationIds: new[] { _subeB }));

        Assert.Empty(t.Rows);
    }

    /// <summary>Kendi şubesini açıkça seçmek ÇALIŞMAYA devam etmeli.</summary>
    [Fact]
    public void StokDurumu_Kendi_Subesini_Secmek_Calisir()
    {
        var t = _reports.StockStatus(SadeceA(),
            new ReportRequest(Executed: true, LocationIds: new[] { _subeA }));

        Assert.Single(t.Rows);
        Assert.Equal("K1", t.Rows[0][0]);
    }

    /// <summary>Karışık istek: izinli olan gelir, olmayan düşer (sessiz genişleme YOK).</summary>
    [Fact]
    public void StokDurumu_Karisik_Istekte_Yalniz_Izinli_Gelir()
    {
        var t = _reports.StockStatus(SadeceA(),
            new ReportRequest(Executed: true, LocationIds: new[] { _subeA, _subeB }));

        Assert.Single(t.Rows);
        Assert.Equal("K1", t.Rows[0][0]);
    }

    // ── DEN-E1 ───────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void SubeBazliOzet_Yalniz_Izinli_Subeleri_Listeler()
    {
        var t = _reports.StatusReport(SadeceA(), new ReportRequest(Executed: true));

        var subeAdlari = t.Rows.Select(r => Convert.ToString(r[0]) ?? "").Distinct().ToList();
        Assert.Contains("ŞUBE A", subeAdlari);
        Assert.DoesNotContain("ŞUBE B", subeAdlari);   // ⭐ kapsam dışı şubenin ADI bile görünmemeli
    }

    [Fact]
    public void SubeBazliOzet_Sinirsiz_Kullanicida_Tum_Subeler_Gorunur()
    {
        var t = _reports.StatusReport(_admin, new ReportRequest(Executed: true));

        var subeAdlari = t.Rows.Select(r => Convert.ToString(r[0]) ?? "").Distinct().ToList();
        Assert.Contains("ŞUBE A", subeAdlari);
        Assert.Contains("ŞUBE B", subeAdlari);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
