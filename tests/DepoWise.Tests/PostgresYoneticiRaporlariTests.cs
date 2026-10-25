using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ M-57 (AR-GE 2026-10-25) — YÖNETİCİ RAPORLARI POSTGRESQL PARİTESİ (üretim DB PostgreSQL). Eksi stok (metin
/// bakiye LIKE '-%', depo kapsamı COALESCE … IN), ay sonu stok değeri (işlem tarihi COALESCE, BIGINT yön) ve makine
/// kullanım oranı (sayaç kaynağı NOT LIKE, arıza duruşu) PG lehçesinde sınanır.
/// ⚠️ Yalnız DEPOWISE_PG_URL (doğrulanmış BOŞ test veritabanı) ile koşar; yoksa ATLANIR.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresYoneticiRaporlariTests
{
    private const long GunMs = 86_400_000L;
    private const long Simdi = 1_700_000_000_000;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(Simdi);
    }

    [SkippableFact]
    public void PostgreSQLde_eksi_stok_ay_sonu_degeri_ve_makine_kullanimi_calisir()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PostgresTestGuard.Url!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();
        void Exec(string sql) { using var c = factory.Create(); using var k = c.CreateCommand(); k.CommandText = sql; k.ExecuteNonQuery(); }
        Exec("INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
             "VALUES('A', 'A', 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;");

        var clock = new TestClock();
        var uid = new UserService(factory, clock).EnsureInitialAdmin("A", "admin_yr", "admin123", RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var depo = new BranchService(factory, clock).Create(s, new NewBranch("PG Depo"));
        var mats = new MaterialService(factory, clock);
        var m1 = mats.Create(s, new NewMaterial("PG-YF", "Yağ filtresi"));
        var m2 = mats.Create(s, new NewMaterial("PG-HF", "Hava filtresi"));
        Exec($"UPDATE materials SET unit_price='12.5' WHERE id='{m1}';");
        var stock = new StockService(factory, clock);
        var reports = new ReportService(factory, clock);

        // ── stok: m1 10 giriş (−40 gün), 4 çıkış (−35 gün), 5 giriş (−10 gün) · m2 2 giriş + 5 bakım tüketimi → −3
        stock.ReceiveIn(s, new[] { new StockLine(m1, 10m, 12.5m) }, "pg-yr-1", depo, docDate: Simdi - 40 * GunMs);
        stock.IssueOut(s, new[] { new StockLine(m1, 4m, 12.5m) }, "pg-yr-2", depo, docDate: Simdi - 35 * GunMs);
        stock.ReceiveIn(s, new[] { new StockLine(m1, 5m, 12.5m) }, "pg-yr-3", depo, docDate: Simdi - 10 * GunMs);
        stock.ReceiveIn(s, new[] { new StockLine(m2, 2m, 4m) }, "pg-yr-4", depo, docDate: Simdi - 10 * GunMs);
        Exec("INSERT INTO stock_movements(id,company_id,material_id,branch_id,movement_type,direction,quantity,operation_id,note,created_at) " +
             $"VALUES('pg-yr-u','A','{m2}','{depo}','usage',-1,'5','pg-yr-5','bakım',{Simdi - GunMs});");
        stock.RecomputeBalances("A");

        var eksi = reports.Run(s, "stock-negative", new ReportRequest(true));
        var e = Assert.Single(eksi.Rows);
        Assert.Equal("PG-HF", e[0]);
        Assert.Equal(-3d, ((NumCell)e[3]!).Value);
        Assert.Equal("bakım", e[6]);
        Assert.Single(reports.Run(s, "stock-negative", new ReportRequest(true, LocationIds: new[] { depo })).Rows);

        var gecmis = reports.Run(s, "stock-value-month", new ReportRequest(true, Simdi - 60 * GunMs, Simdi - 20 * GunMs));
        var g = Assert.Single(gecmis.Rows);
        Assert.Equal(6d, ((NumCell)g[2]!).Value);
        Assert.Equal(75d, ((NumCell)gecmis.TotalRow![5]!).Value);
        var bugun = reports.Run(s, "stock-value-month", new ReportRequest(true, Simdi - 60 * GunMs, Simdi));
        Assert.Equal(11d, ((NumCell)bugun.Rows.Single(r => (string?)r[0] == "PG-YF")[2]!).Value);
        Assert.Equal(-3d, ((NumCell)bugun.Rows.Single(r => (string?)r[0] == "PG-HF")[2]!).Value);

        // ── makine: yakıt fişi bugün (+100) + form kaydı −5. gün (+20) + yeniden hesaplama (sayılmaz) + 6 saat arıza
        var arac = new VehicleService(factory, clock).Create(s, new NewVehicle("PG-EKS", "34PG01", 2020, 1000m, "hour", depo));
        var yakit = new FuelService(factory, clock);
        yakit.AddDepotEntry(s, new NewDepotEntry(500m, 40m, EntryDate: Simdi - 30 * GunMs), "pg-yr-d");
        yakit.Distribute(s, new NewDistribution(arac, 50m, 1100m, 40m, DistributionDate: Simdi - 3_600_000), "pg-yr-f");
        var bugunGun = Simdi / GunMs;
        Exec("INSERT INTO vehicle_meter_logs(id,company_id,vehicle_id,old_value,new_value,source,created_at) " +
             $"VALUES('pg-ml1','A','{arac}','1100','1120','vehicle_form',{(bugunGun - 5) * GunMs + 1000});");
        Exec("INSERT INTO vehicle_meter_logs(id,company_id,vehicle_id,old_value,new_value,source,created_at) " +
             $"VALUES('pg-ml2','A','{arac}','1120','1400','recalc:fuel',{(bugunGun - 4) * GunMs});");
        Exec("INSERT INTO work_orders(id,company_id,wo_no,title,status,priority,created_by,created_at,updated_at,version,is_deleted,fault_vehicle_id,actual_end) " +
             $"VALUES('pg-wo','A','PG-1','Arıza','completed','normal','u',{Simdi - 10 * 3_600_000L},{Simdi},1,0,'{arac}',{Simdi - 4 * 3_600_000L});");

        var t = reports.Run(s, "machine-utilization", new ReportRequest(true, (bugunGun - 9) * GunMs, Simdi));
        var a = Assert.Single(t.Rows);
        int K(string b) => t.Headers.ToList().IndexOf(b);
        Assert.Equal(2d, ((NumCell)a[K("Çalışılan Gün")]!).Value);
        Assert.Equal(20d, ((NumCell)a[K("Kullanım Oranı")]!).Value);
        Assert.Equal(120d, ((NumCell)a[K("Sayaç Artışı")]!).Value);
        Assert.Equal(6d, ((NumCell)a[K("Arıza Duruşu (Saat)")]!).Value);
    }
}
