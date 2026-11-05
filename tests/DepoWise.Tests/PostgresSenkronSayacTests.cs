using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ SAY-01 (2026-11-05) — PostgreSQL'de (üretim lehçesi) sunucu tarafı sayaç yeniden hesabı ═══
/// SQLite sürümü: <see cref="SenkronSayacYenidenHesapTests"/>. Burada PG'ye özgü yol (savepoint, metin sayaç kolonları,
/// ham BEGIN içinde Hesapla sorguları, server_txid tetikleyicisi) gerçek veritabanında sınanır.
///  PSAY1 — düzeltilen hatalı kayıt (iptal) gelince sunucu sayacı doğru değere iner + iz kaydı yazılır
///  PSAY2 — dayanak kaydı olmayan aracın sayacı sıfırlanmaz
/// ⚠️ Yalnız DEPOWISE_PG_URL test veritabanında; şemayı sıfırlar. Yoksa ATLANIR.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresSenkronSayacTests
{
    private static (PostgresMigrationTests.NpgsqlTestFactory F, BusinessSyncService Sync) Kur()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PostgresTestGuard.Url!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();
        var users = new UserService(factory);
        var rootId = users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var su = new SessionContext(rootId, "A", new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        new CompanyService(factory).Create(su, new NewCompany("B Firma"), explicitId: "B");

        Calistir(factory, "INSERT INTO vehicles(id,company_id,internal_code,plate,current_meter,meter_unit,status,created_at,updated_at,version,is_deleted) " +
                          "VALUES('v1','B','AR-1','34ABC01','125880','km','active',1,1000,1,0),('v2','B','AR-2','34ABC02','50000','km','active',1,1000,1,0);");
        foreach (var (id, onceki, sayac) in new[] { ("f-dogru", "116582", "116753"), ("f-hatali", "125759", "125880") })
            Calistir(factory, "INSERT INTO fuel_distributions(id,company_id,vehicle_id,prev_meter,current_meter,liters,unit_price,currency_code,fx_rate," +
                              $"distribution_date,operation_id,created_at,updated_at,version,is_deleted) VALUES('{id}','B','v1','{onceki}','{sayac}','100','40','TRY','1',1000,'{id}',1000,1000,1,0);");
        Calistir(factory, "INSERT INTO sync_devices(id,company_id,device_name,status,last_business_push_at,created_at,updated_at,version) " +
                          "VALUES('dev-1','B','DENEY','active',2000,1,1,1);");
        return (factory, new BusinessSyncService(factory));
    }

    private static void Calistir(IDbConnectionFactory f, string sql)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string Tek(IDbConnectionFactory f, string sql)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }

    private static BusinessSyncService.ApplyResult Push(BusinessSyncService sync, object tables)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { machineId = "DENEY", tables }));
        return sync.Apply("B", doc.RootElement.Clone());
    }

    [SkippableFact]
    public void PSAY1_Duzeltilen_Hatali_Kayit_PGde_Sayaci_Indirir()
    {
        var (f, sync) = Kur();
        var r = Push(sync, new
        {
            vehicles = new[] { new { id = "v1", company_id = "B", internal_code = "AR-1", plate = "34ABC01", current_meter = "116753",
                                     meter_unit = "km", status = "active", created_at = 1L, updated_at = 5000L, version = 9L, is_deleted = 0 } },
            fuel_distributions = new[] { new { id = "f-hatali", company_id = "B", vehicle_id = "v1", prev_meter = "125759", current_meter = "125880",
                liters = "100", unit_price = "40", currency_code = "TRY", fx_rate = "1", distribution_date = 1000L, operation_id = "f-hatali",
                created_at = 1000L, updated_at = 6000L, version = 2L, is_deleted = 1 } },
        });

        Assert.Empty(r.Errors);
        Assert.Equal("116753", Tek(f, "SELECT current_meter FROM vehicles WHERE id='v1';"));
        Assert.Equal("1", Tek(f, "SELECT COUNT(*) FROM vehicle_meter_logs WHERE vehicle_id='v1' AND source='recalc:sync';"));
    }

    [SkippableFact]
    public void PSAY2_Dayanaksiz_Arac_PGde_Sifirlanmaz()
    {
        var (f, sync) = Kur();
        var r = Push(sync, new
        {
            vehicles = new[] { new { id = "v2", company_id = "B", internal_code = "AR-2", plate = "34ABC02", current_meter = "50000",
                                     meter_unit = "km", status = "active", created_at = 1L, updated_at = 5000L, version = 9L, is_deleted = 0 } },
        });

        Assert.Empty(r.Errors);
        Assert.Equal("50000", Tek(f, "SELECT current_meter FROM vehicles WHERE id='v2';"));
    }
}
