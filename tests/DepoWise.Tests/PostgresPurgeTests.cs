using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// PostgreSQL GEÇİŞİ — Faz 3 (2026-07-23): FİRMA KALICI SİLME (ADR-083) + İŞ VERİSİ SIFIRLAMA PostgreSQL'de.
///
/// SQLite'ta silme FK kapatılarak yapılır; PG'de FK KAPATILAMAZ (Neon owner yetkisi yok — gerçek testle
/// doğrulandı). <see cref="DepoWise.Infrastructure.Database.DialectPurge"/> FK'ye saygılı siler. ASIL RİSK:
/// company_id'SİZ çocuk tablolar (maintenance_materials, material_request_items, ...) — düz silmede PG bunları
/// FK ihlaliyle reddederdi. Bu test onların FK-hatasız temizlendiğini ve başka firmaya dokunulmadığını kanıtlar.
///
/// ⚠️ Yalnız DEPOWISE_PG_URL (boş Neon deneme DB'si) üzerinde; her koşuda şemayı sıfırlar. Yoksa ATLANIR.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresPurgeTests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static PostgresMigrationTests.NpgsqlTestFactory FreshSchema()
    {
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        // GÜVENLİK KAPISI: şema YALNIZ doğrulanmış boş test veritabanında sıfırlanır (bkz. PostgresTestGuard).
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();
        return factory;
    }

    private static long RawCount(PostgresMigrationTests.NpgsqlTestFactory f, string table)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>A (süper admin) + B (zengin iş verisi: malzeme/stok/araç/bakım-malzeme-satırı/talep-satırı).</summary>
    private static (PostgresMigrationTests.NpgsqlTestFactory F, SessionContext Su) SeedRichB()
    {
        var factory = FreshSchema();
        var clock = new TestClock();
        var users = new UserService(factory, clock);
        var companies = new CompanyService(factory, clock);
        var branches = new BranchService(factory, clock);

        var rootId = users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var su = new SessionContext(rootId, "A", new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);

        companies.Create(su, new NewCompany("B Firma"), explicitId: "B");
        var bBranch = branches.Create(su, new NewBranch("B-Merkez"), companyId: "B");
        var bAdminId = users.CreateUser(su, new NewUser("badm", "p12345", "B Admin",
            new[] { RoleKeys.CompanyAdmin }, CompanyId: "B", BranchId: bBranch));
        var b = new SessionContext(bAdminId, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        // B firmasına, company_id'SİZ çocuk tabloları ÜRETEN veri:
        var materials = new MaterialService(factory, clock);
        var opening = new OpeningStockService(factory, clock);
        var vehicles = new VehicleService(factory, clock);
        var defs = new MaintenanceDefinitionService(factory, clock);
        var maint = new MaintenanceService(factory, clock);
        var requests = new RequestService(factory, new StockService(factory, clock), clock);

        var m = materials.Create(b, new NewMaterial("M-1", "Filtre", UnitPrice: 50m, MinStock: 5m));
        opening.RecordOpening(b, m, 100m, "b-open");
        var v = vehicles.Create(b, new NewVehicle("V-1", CurrentMeter: 1000m));
        var def = defs.Create(b, new NewMaintenanceDefinition("Periyodik", 100m, "km"));
        maint.Save(b, new NewMaintenance(v, def, PerformedKm: 1000m,
            Materials: new[] { new MaintenanceMaterialLine(m, 10m) }), "b-mnt");      // → maintenance_materials
        requests.Create(b, new NewRequest(new[] { new RequestItemInput(m, 20m) }, SubmitImmediately: true)); // → material_request_items

        return (factory, su);
    }

    [SkippableFact]
    public void Purge_PostgreSQLde_FKsiz_Cocuklari_Da_Siler()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var (factory, su) = SeedRichB();
        var clock = new TestClock();
        var purge = new CompanyPurgeService(factory, clock);

        // Silmeden ÖNCE company_id'siz çocuklar dolu (düz DELETE bunları PG'de FK ihlaliyle reddederdi).
        Assert.True(RawCount(factory, "maintenance_materials") > 0);
        Assert.True(RawCount(factory, "material_request_items") > 0);

        var res = purge.Purge(su, "B");        // FK hatası fırlatmamalı

        Assert.Equal("B Firma", res.CompanyName);
        Assert.True(res.RowsDeleted > 0);
        Assert.Null(purge.FindName("B"));                                  // firma gitti
        // company_id'SİZ çocuklar da temizlendi (yetim kalmadı, FK ihlali olmadı).
        Assert.Equal(0, RawCount(factory, "maintenance_materials"));
        Assert.Equal(0, RawCount(factory, "material_request_items"));
        Assert.Equal(0, RawCount(factory, "materials"));
        Assert.Equal(0, RawCount(factory, "vehicle_maintenances"));
        // Künye kaldı (masaüstü eşitleme silmeyi öğrensin).
        Assert.NotNull(purge.GetPurge("B"));
        // Aktörün firması (A) DOKUNULMADAN durdu.
        Assert.NotNull(purge.FindName("A"));
    }

    /// <summary>DialectPurge.RunFkSafe — admin/dev sıfırlama uçlarının (Program.cs) kullandığı ortak yardımcı:
    /// tam-tablo DELETE'lerini rastgele sırada versek bile FK sırasını savepoint+retry ile çözer.</summary>
    [SkippableFact]
    public void RunFkSafe_TamTablo_FKsiralamasini_Cozer()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var (factory, _) = SeedRichB();
        Assert.True(RawCount(factory, "materials") > 0);

        // Bilerek "yanlış" sıra (ebeveynler çocuklardan önce) — RunFkSafe kendiliğinden düzeltmeli.
        var toClear = new[]
        {
            "materials", "vehicles", "maintenance_definitions",           // ebeveynler önce (FK ihlali riski)
            "stock_movements", "stock_balances", "stock_documents",
            "vehicle_maintenances", "maintenance_materials", "vehicle_meter_logs", "maintenance_definition_vehicles",
            "material_requests", "material_request_items", "request_status_history",
        };
        using (var conn = factory.Create())
        {
            using var tx = conn.BeginTransaction();
            DepoWise.Infrastructure.Database.DialectPurge.RunFkSafe(conn, tx, toClear.Select(t => $"DELETE FROM \"{t}\";"));
            tx.Commit();
        }

        Assert.Equal(0, RawCount(factory, "materials"));
        Assert.Equal(0, RawCount(factory, "vehicles"));
        Assert.Equal(0, RawCount(factory, "vehicle_maintenances"));
        Assert.Equal(0, RawCount(factory, "maintenance_materials"));
        Assert.Equal(0, RawCount(factory, "material_request_items"));
        Assert.Equal(0, RawCount(factory, "vehicle_meter_logs"));
    }

    [SkippableFact]
    public void ResetBusinessData_PostgreSQLde_IsVerisiniSiler_FirmaKullaniciKORUR()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var (factory, su) = SeedRichB();
        var clock = new TestClock();
        var purge = new CompanyPurgeService(factory, clock);

        var res = purge.ResetBusinessData(su, "B");

        Assert.True(res.RowsDeleted > 0);
        // İş verisi + company_id'siz çocukları GİTTİ …
        Assert.Equal(0, RawCount(factory, "materials"));
        Assert.Equal(0, RawCount(factory, "maintenance_materials"));
        Assert.Equal(0, RawCount(factory, "material_request_items"));
        // … ama firma + kullanıcı KORUNDU (giriş yapabilmeli — company_id tablosu users silinmedi).
        Assert.NotNull(purge.FindName("B"));
        Assert.True(RawCount(factory, "users") > 0);
    }
}
