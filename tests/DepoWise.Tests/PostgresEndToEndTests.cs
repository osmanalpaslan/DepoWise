using DepoWise.Application.Common;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Requests;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// PostgreSQL şemayı SIFIRLAYAN testler (migration + uçtan uca) AYNI Neon deneme DB'sini kullanır →
/// paralel çalışırlarsa birbirinin şemasını uçururlar. Bu koleksiyon onları SERİLEŞTİRİR.
/// </summary>
[CollectionDefinition("PostgresSchema", DisableParallelization = true)]
public sealed class PostgresSchemaCollection { }

/// <summary>
/// PostgreSQL GEÇİŞİ — Faz 2 Adım 4 (uçtan uca, 2026-07-23): şema kurulmakla kalmaz, GERÇEK servis
/// işlemleri (firma/malzeme/stok/araç/bakım/talep + tenant izolasyonu + idempotency + negatif stok kalkanı
/// + generic snapshot upsert) PostgreSQL'de doğru çalışır mı? Bu, tüm veri yolunun PG'de kanıtıdır.
///
/// ⚠️ Yalnız DEPOWISE_PG_URL (boş Neon deneme DB'si) üzerinde; her koşuda şemayı sıfırlar. Yoksa ATLANIR.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresEndToEndTests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    [SkippableFact]
    public void Servisler_PostgreSQLde_UctanUca_Calisir()
    {
        Skip.If(string.IsNullOrWhiteSpace(PgUrl), "DEPOWISE_PG_URL yok → PostgreSQL uçtan uca testi atlandı.");
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);

        // Temiz şema + tüm migration'lar.
        using (var conn = factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DROP SCHEMA public CASCADE; CREATE SCHEMA public;";
            cmd.ExecuteNonQuery();
        }
        new MigrationRunner(factory).Run();

        var clock = new TestClock();

        // Kurulum: iki firma admini.
        var users = new UserService(factory, clock);
        var aId = users.EnsureInitialAdmin("A", "admin_a", "admin123", RoleKeys.CompanyAdmin);
        var a = new SessionContext(aId, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var bId = users.EnsureInitialAdmin("B", "admin_b", "admin123", RoleKeys.CompanyAdmin);
        var b = new SessionContext(bId, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var materials = new MaterialService(factory, clock);
        var opening = new OpeningStockService(factory, clock);
        var stock = new StockService(factory, clock);
        var vehicles = new VehicleService(factory, clock);
        var defs = new MaintenanceDefinitionService(factory, clock);
        var maint = new MaintenanceService(factory, clock);
        var requests = new RequestService(factory, stock, clock);

        // 1) Malzeme + açılış stoğu (ledger → bakiye).
        var m = materials.Create(a, new NewMaterial("M-1", "Filtre", UnitPrice: 50m, MinStock: 5m));
        opening.RecordOpening(a, m, 100m, "pg-open");
        Assert.Equal(100m, stock.GetBalance(m));

        // 2) İdempotency: AYNI operation_id ile tekrar → çift yazmaz (retry güvenli).
        opening.RecordOpening(a, m, 100m, "pg-open");
        Assert.Equal(100m, stock.GetBalance(m));

        // 3) Araç + sayaç.
        var v = vehicles.Create(a, new NewVehicle("V-1", CurrentMeter: 1000m));
        Assert.Equal(1000m, vehicles.GetMeter(a, v));

        // 4) Bakım: stok düşer (defter), sayaç + uyarı.
        var def = defs.Create(a, new NewMaintenanceDefinition("Periyodik", 100m, "km"));
        maint.Save(a, new NewMaintenance(v, def, PerformedKm: 1000m,
            Materials: new[] { new MaintenanceMaterialLine(m, 10m) }), "pg-mnt");
        Assert.Equal(90m, stock.GetBalance(m));            // 100 - 10
        vehicles.SetMeter(a, v, 1098m);                    // %98 → kritik uyarı
        Assert.Equal(AlertLevel.Critical, maint.GetAlerts(a).Single().Level);

        // 5) Negatif stok kalkanı: eldekinden fazla çıkış REDDEDİLİR (LWW yasağı / defter bütünlüğü).
        Assert.Throws<NegativeStockException>(() =>
            stock.IssueOut(a, new[] { new StockLine(m, 1000m) }, "pg-over", personnelId: null));
        Assert.Equal(90m, stock.GetBalance(m));            // değişmedi

        // 6) Talep: onay stoğu DEĞİŞTİRMEZ; kontrollü çıkış düşürür.
        var req = requests.Create(a, new NewRequest(new[] { new RequestItemInput(m, 20m) }, SubmitImmediately: true));
        requests.Approve(a, req.Id);
        Assert.Equal(90m, stock.GetBalance(m));
        requests.CreateIssueFromRequest(a, req.Id, "pg-issue");
        Assert.Equal(70m, stock.GetBalance(m));            // 90 - 20

        // 7) Tenant izolasyonu: B, A'nın malzemesini görmez.
        Assert.Empty(materials.List(b, new PageRequest { Limit = 50 }).Items);
        Assert.Single(materials.List(a, new PageRequest { Limit = 50 }).Items);

        // 8) Generic snapshot (BuildSnapshot → sunucu-otoriteli okuma) + geri uygula (ON CONFLICT ... = excluded
        //    upsert yolu PostgreSQL'de çalışsın). Aynı snapshot'ı geri uygulamak upsert DO UPDATE'i tetikler.
        var sync = new BusinessSyncService(factory, clock);
        var snapshot = sync.BuildSnapshot("A");
        Assert.Contains("materials", snapshot);
        using var doc = System.Text.Json.JsonDocument.Parse(snapshot);
        var res = sync.ApplyPull("A", doc.RootElement, null);   // upsert (ON CONFLICT DO UPDATE) — PG'de sınar
        Assert.Empty(res.Errors);
        Assert.Equal(70m, stock.GetBalance(m));                 // upsert bakiyeyi bozmadı
    }
}
