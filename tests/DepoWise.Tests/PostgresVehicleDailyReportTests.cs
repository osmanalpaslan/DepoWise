using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ RPT-GUNLUK (2026-08-29, PK-R1=A) — GÜNLÜK ARAÇ RAPORU POSTGRESQL PARİTESİ.
///
/// Gün anahtarı <c>tarih_ms / 86400000</c> TAM SAYI bölmesidir; bu test aynı bölmenin PostgreSQL'de
/// de (bigint bölmesi) SQLite ile BİREBİR aynı gün gruplamasını ürettiğini, uç fişlerin (gün başı
/// 00:00:00.000 · gün sonu 23:59:59.999) dahil olduğunu, boş günlerin 0 satırıyla geldiğini ve
/// günlük toplamların DÖNEM (`vehicle`) raporuyla tutarlı olduğunu kanıtlar.
///
/// ⚠️ Yalnız DEPOWISE_PG_URL (doğrulanmış BOŞ test veritabanı — PostgresTestGuard çift kilidi) ile
/// koşar; yoksa ATLANIR. Production'a hiçbir koşulda bağlanılmaz.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresVehicleDailyReportTests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    private const long G = 86_400_000;
    private static long Day(long i) => (20_000 + i) * G;

    [SkippableFact]
    public void PostgreSQLde_gunluk_arac_raporu_SQLite_ile_ayni_sozlesmede()
    {
        PostgresTestGuard.SkipUnlessSafe();

        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();

        void Exec(string sql, params (string, object?)[] ps)
        {
            using var c = factory.Create();
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (k, v) in ps) cmd.AddWithValue(k, v ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) " +
             "VALUES('A','A',1,1,1,0,5,20,5);");
        var uid = new UserService(factory).EnsureInitialAdmin("A", "admin_gd", "admin123", RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('B1','A','Merkez',@n,@n);", ("@n", Day(0)));
        Exec(@"INSERT INTO vehicles(id,company_id,internal_code,meter_unit,branch_id,current_meter,created_at,updated_at,version,is_deleted)
               VALUES('v1','A','V1','km','B1','0',@n,@n,1,0);", ("@n", Day(0)));

        void Fuel(string id, string prev, string cur, string liters, string price, long date) =>
            Exec(@"INSERT INTO fuel_distributions(id,company_id,vehicle_id,prev_meter,current_meter,liters,unit_price,currency_code,distribution_date,operation_id,created_at,updated_at,version,is_deleted)
                   VALUES(@id,'A','v1',@p,@c,@l,@pr,'TRY',@d,@op,@n,@n,1,0);",
                ("@id", id), ("@p", prev), ("@c", cur), ("@l", liters), ("@pr", price),
                ("@d", date), ("@op", "op-" + id), ("@n", Day(0)));

        Fuel("f1", "1000", "1200", "100", "40", Day(1));           // gün BAŞI tam sınır → DAHİL
        Fuel("f2", "1200", "1300", "50", "44", Day(1) + 3_600_000);
        Fuel("f3", "1350", "1400", "20", "50", Day(3) + G - 1);    // gün SONU 23:59:59.999 → DAHİL
        Fuel("fx0", "1", "2", "999", "99", Day(0) + 5_000);        // aralık DIŞI
        Fuel("fx4", "1", "2", "999", "99", Day(4));                // aralık DIŞI

        var reports = new ReportService(factory);
        var istek = new ReportRequest(true, Day(1), Day(3) + G - 1);

        // ⭐ ADR-183 (kullanıcı düzeltmesi): BOŞ gün satırı ÜRETİLMEZ → 3 gün aralığında yalnız
        // verisi olan 2 gün (1 ve 3) gelir; 2. gün hiç görünmez.
        var gunluk = reports.Run(s, "vehicle-daily", istek);
        Assert.Equal(2, gunluk.Rows.Count);
        Assert.Equal(16, gunluk.Headers.Count);

        double D(object? v) => v is NumCell n ? n.Value : Convert.ToDouble(v ?? 0);

        var g1 = gunluk.Rows[0]; var g3 = gunluk.Rows[1];
        Assert.Equal(150.0, D(g1[7]), 6);                          // gün 1 litre (sınır fişi DAHİL)
        Assert.Equal(300.0, D(g1[6]), 6);                          // gün 1 km
        Assert.Equal(6200.0, D(g1[9]), 6);                         // gün 1 yakıt maliyeti
        Assert.Equal(1300.0, D(g1[15]), 6);                        // gün içi SON sayaç
        Assert.Equal(20.0, D(g3[7]), 6);                           // gün 3 (gün sonu fişi DAHİL)
        Assert.DoesNotContain(gunluk.Rows, r => D(r[7]) == 0 && D(r[11]) == 0 && D(r[12]) == 0);   // boş satır YOK

        // Günlük toplamlar DÖNEM raporuyla tutarlı (PG'de de).
        var donem = reports.Run(s, "vehicle", istek);
        var dv1 = donem.Rows.Single();
        Assert.Equal(D(dv1[6]), gunluk.Rows.Sum(r => D(r[7])), 6);   // litre
        Assert.Equal(D(dv1[8]), gunluk.Rows.Sum(r => D(r[9])), 6);   // yakıt maliyeti
        Assert.Equal(D(dv1[5]), gunluk.Rows.Sum(r => D(r[6])), 6);   // mesafe

        // RPT-YETKI çift kapı PG hattında da: reports VAR + kategori YOK → 403.
        var yalnizReports = new SessionContext("u1", "A", new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("reports", true, false, false, false) }));
        Assert.Throws<ForbiddenException>(() => reports.Run(yalnizReports, "vehicle-daily", istek));
        var yetkili = new SessionContext("u1", "A", new[] { RoleKeys.Staff },
            new PermissionSet(new[]
            {
                new ModulePermission("reports", true, false, false, false),
                new ModulePermission("report_vehicle", true, false, false, false),
            }));
        Assert.Equal(2, reports.Run(yetkili, "vehicle-daily", istek).Rows.Count);   // ADR-183
    }
}
