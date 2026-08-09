using DepoWise.Application.Common;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// M-S1a — Migration062'nin PostgreSQL karşılığı. SQLite tarafı <see cref="CompanyIdMigrationTests"/>;
/// burada AYNI son durumun PostgreSQL'de de oluştuğu kanıtlanır (kolon + NOT NULL + indeks + doğru taşıma).
/// Lehçeye özel yollar farklıdır (PG: ALTER/SET NOT NULL · SQLite: tabloyu yeniden kurma) — SONUÇ AYNI olmalıdır.
///
/// ⚠️ Yalnız DEPOWISE_PG_URL (doğrulanmış BOŞ test veritabanı) ile koşar; yoksa ATLANIR.
/// Canlı veritabanına ASLA bağlanmaz (bkz. PostgresTestGuard).
/// </summary>
[Collection("PostgresSchema")]
public class PostgresCompanyIdMigrationTests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    /// <summary>Şemayı sıfırlar ve YALNIZ 61. sürüme kadar uygular (company_id kolonu henüz yok).</summary>
    private static PostgresMigrationTests.NpgsqlTestFactory SetupAt61()
    {
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory, MigrationCatalog.All().Where(m => m.Version <= 61)).Run();
        return factory;
    }

    private static void Exec(IDbConnectionFactory f, string sql, params (string, object?)[] ps)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static T Scalar<T>(IDbConnectionFactory f, string sql, params (string, object?)[] ps)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.AddWithValue(k, v ?? DBNull.Value);
        var o = cmd.ExecuteScalar();
        return o is null or DBNull ? default! : (T)Convert.ChangeType(o, typeof(T));
    }

    private static void SeedCompany(IDbConnectionFactory f, string id) =>
        Exec(f, "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES(@id, @id, 1, 1, 1, 0, 5, 10, 3);", ("@id", id));

    private static string SeedLegacyRequestItem(IDbConnectionFactory f, string companyId)
    {
        var req = Guid.NewGuid().ToString("N");
        var item = Guid.NewGuid().ToString("N");
        var mat = Guid.NewGuid().ToString("N");
        Exec(f, "INSERT INTO materials(id, company_id, code, name, min_stock, unit_price, created_at, updated_at, version, is_deleted) " +
                "VALUES(@id, @c, @id, 'Malzeme', '0', '0', 1, 1, 1, 0);", ("@id", mat), ("@c", companyId));
        Exec(f, "INSERT INTO material_requests(id, company_id, doc_no, request_date, status, created_at, updated_at, version, is_deleted, priority) " +
                "VALUES(@id, @c, @id, 1, 'draft', 1, 1, 1, 0, 'normal');", ("@id", req), ("@c", companyId));
        Exec(f, "INSERT INTO material_request_items(id, request_id, material_id, quantity) VALUES(@id, @r, @m, '1');",
                ("@id", item), ("@r", req), ("@m", mat));
        return item;
    }

    private static string SeedLegacyMaintenanceMaterial(IDbConnectionFactory f, string companyId)
    {
        var veh = Guid.NewGuid().ToString("N");
        var def = Guid.NewGuid().ToString("N");
        var mnt = Guid.NewGuid().ToString("N");
        var line = Guid.NewGuid().ToString("N");
        var mat = Guid.NewGuid().ToString("N");
        Exec(f, "INSERT INTO materials(id, company_id, code, name, min_stock, unit_price, created_at, updated_at, version, is_deleted) " +
                "VALUES(@id, @c, @id, 'Malzeme', '0', '0', 1, 1, 1, 0);", ("@id", mat), ("@c", companyId));
        Exec(f, "INSERT INTO vehicles(id, company_id, internal_code, current_meter, meter_unit, created_at, updated_at, version, is_deleted) " +
                "VALUES(@id, @c, @id, '0', 'km', 1, 1, 1, 0);", ("@id", veh), ("@c", companyId));
        Exec(f, "INSERT INTO maintenance_definitions(id, company_id, name, interval_value, interval_unit, created_at, updated_at, version, is_deleted) " +
                "VALUES(@id, @c, 'Periyodik', '100', 'km', 1, 1, 1, 0);", ("@id", def), ("@c", companyId));
        Exec(f, "INSERT INTO vehicle_maintenances(id, company_id, vehicle_id, maintenance_def_id, operation_id, is_cancelled, created_at, updated_at, version, is_deleted) " +
                "VALUES(@id, @c, @v, @d, @id, 0, 1, 1, 1, 0);", ("@id", mnt), ("@c", companyId), ("@v", veh), ("@d", def));
        Exec(f, "INSERT INTO maintenance_materials(id, maintenance_id, material_id, quantity, from_team_stock) VALUES(@id, @mt, @m, '1', 0);",
                ("@id", line), ("@mt", mnt), ("@m", mat));
        return line;
    }

    /// <summary>PostgreSQL şema sözlüğünden kolonun NOT NULL olup olmadığını okur (son durum kanıtı).</summary>
    private static bool IsNotNull(IDbConnectionFactory f, string table, string column) =>
        Scalar<string>(f, "SELECT is_nullable FROM information_schema.columns " +
                          "WHERE table_schema='public' AND table_name=@t AND column_name=@c;",
                       ("@t", table), ("@c", column)) == "NO";

    // ── testler ────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void PostgreSQLde_kolonlar_NOT_NULL_ve_indeksli_olusur()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = SetupAt61();

        new MigrationRunner(f).Run();

        Assert.True(IsNotNull(f, "material_request_items", "company_id"));
        Assert.True(IsNotNull(f, "maintenance_materials", "company_id"));
        Assert.Equal(1, Scalar<int>(f, "SELECT COUNT(*)::int FROM pg_indexes WHERE schemaname='public' AND indexname='ix_material_request_items_company';"));
        Assert.Equal(1, Scalar<int>(f, "SELECT COUNT(*)::int FROM pg_indexes WHERE schemaname='public' AND indexname='ix_maintenance_materials_company';"));
    }

    [SkippableFact]
    public void PostgreSQLde_mevcut_kayitlar_dogru_firmaya_tasinir_ve_hicbiri_kaybolmaz()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = SetupAt61();
        SeedCompany(f, "A"); SeedCompany(f, "B");
        var itemA = SeedLegacyRequestItem(f, "A");
        var itemB = SeedLegacyRequestItem(f, "B");
        var lineA = SeedLegacyMaintenanceMaterial(f, "A");
        var lineB = SeedLegacyMaintenanceMaterial(f, "B");
        var beforeItems = Scalar<int>(f, "SELECT COUNT(*)::int FROM material_request_items;");
        var beforeLines = Scalar<int>(f, "SELECT COUNT(*)::int FROM maintenance_materials;");

        new MigrationRunner(f).Run();

        Assert.Equal(beforeItems, Scalar<int>(f, "SELECT COUNT(*)::int FROM material_request_items;"));
        Assert.Equal(beforeLines, Scalar<int>(f, "SELECT COUNT(*)::int FROM maintenance_materials;"));
        Assert.Equal("A", Scalar<string>(f, "SELECT company_id FROM material_request_items WHERE id=@id;", ("@id", itemA)));
        Assert.Equal("B", Scalar<string>(f, "SELECT company_id FROM material_request_items WHERE id=@id;", ("@id", itemB)));
        Assert.Equal("A", Scalar<string>(f, "SELECT company_id FROM maintenance_materials WHERE id=@id;", ("@id", lineA)));
        Assert.Equal("B", Scalar<string>(f, "SELECT company_id FROM maintenance_materials WHERE id=@id;", ("@id", lineB)));
        // Yanlış firma eşleşmesi: 0 olmalı
        Assert.Equal(0, Scalar<int>(f, @"
SELECT COUNT(*)::int FROM material_request_items i JOIN material_requests p ON p.id=i.request_id
WHERE i.company_id <> p.company_id;"));
        Assert.Equal(0, Scalar<int>(f, @"
SELECT COUNT(*)::int FROM maintenance_materials mm JOIN vehicle_maintenances p ON p.id=mm.maintenance_id
WHERE mm.company_id <> p.company_id;"));
    }

    [SkippableFact]
    public void PostgreSQLde_cozulemeyen_kayit_varsa_migration_DURUR()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = SetupAt61();
        SeedCompany(f, "A");
        SeedLegacyRequestItem(f, "A");
        // Yetim satır: FK geçici olarak devre dışı bırakılıp kurulur (gerçek hayatta FK bunu engeller).
        Exec(f, "ALTER TABLE material_request_items DROP CONSTRAINT material_request_items_request_id_fkey;");
        Exec(f, "INSERT INTO material_request_items(id, request_id, material_id, quantity) " +
                "SELECT @id, 'OLMAYAN', material_id, '1' FROM material_request_items LIMIT 1;",
                ("@id", Guid.NewGuid().ToString("N")));
        var before = Scalar<int>(f, "SELECT COUNT(*)::int FROM material_request_items;");

        var ex = Assert.ThrowsAny<Exception>(() => new MigrationRunner(f).Run());

        Assert.Contains("M-S1a", ex.Message);
        // Geri alındı: kolon eklenmedi, hiçbir satır silinmedi, sürüm işlenmedi.
        Assert.Equal(0, Scalar<int>(f, "SELECT COUNT(*)::int FROM information_schema.columns " +
                                       "WHERE table_schema='public' AND table_name='material_request_items' AND column_name='company_id';"));
        Assert.Equal(before, Scalar<int>(f, "SELECT COUNT(*)::int FROM material_request_items;"));
        Assert.Equal(0, Scalar<int>(f, "SELECT COUNT(*)::int FROM schema_migrations WHERE version=62;"));
    }

    [SkippableFact]
    public void PostgreSQLde_varsayilan_YOKTUR_eksik_INSERT_hata_verir()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = SetupAt61();
        SeedCompany(f, "A");
        var item = SeedLegacyRequestItem(f, "A");
        new MigrationRunner(f).Run();

        var reqId = Scalar<string>(f, "SELECT request_id FROM material_request_items WHERE id=@id;", ("@id", item));
        var matId = Scalar<string>(f, "SELECT material_id FROM material_request_items WHERE id=@id;", ("@id", item));

        Assert.ThrowsAny<System.Data.Common.DbException>(() =>
            Exec(f, "INSERT INTO material_request_items(id, request_id, material_id, quantity) VALUES(@id, @r, @m, '1');",
                    ("@id", Guid.NewGuid().ToString("N")), ("@r", reqId), ("@m", matId)));
    }

    [SkippableFact]
    public void PostgreSQLde_snapshot_artik_diger_firmanin_kalemlerini_TASIMAZ()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = SetupAt61();
        SeedCompany(f, "A"); SeedCompany(f, "B");
        var itemA = SeedLegacyRequestItem(f, "A");
        var itemB = SeedLegacyRequestItem(f, "B");
        var lineB = SeedLegacyMaintenanceMaterial(f, "B");
        new MigrationRunner(f).Run();

        var snapshotA = new BusinessSyncService(f, new FixedClock()).BuildSnapshot("A");

        Assert.Contains(itemA, snapshotA);
        Assert.DoesNotContain(itemB, snapshotA);
        Assert.DoesNotContain(lineB, snapshotA);
    }

    [SkippableFact]
    public void PostgreSQLde_tekrar_calistirma_ve_rollback_guvenlidir()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = SetupAt61();
        SeedCompany(f, "A");
        var item = SeedLegacyRequestItem(f, "A");
        new MigrationRunner(f).Run();

        new MigrationRunner(f).Run();   // idempotent
        Assert.Equal("A", Scalar<string>(f, "SELECT company_id FROM material_request_items WHERE id=@id;", ("@id", item)));
        Assert.Equal(1, Scalar<int>(f, "SELECT COUNT(*)::int FROM material_request_items;"));

        // Belgelenen geri alma (SQLite ile AYNI betik)
        Exec(f, "DROP INDEX IF EXISTS ix_material_request_items_company;");
        Exec(f, "DROP INDEX IF EXISTS ix_maintenance_materials_company;");
        Exec(f, "ALTER TABLE material_request_items DROP COLUMN company_id;");
        Exec(f, "ALTER TABLE maintenance_materials DROP COLUMN company_id;");
        Exec(f, "DELETE FROM schema_migrations WHERE version=62;");
        Assert.Equal(1, Scalar<int>(f, "SELECT COUNT(*)::int FROM material_request_items;"));   // iş kaydı DURUYOR

        new MigrationRunner(f).Run();   // yeniden uygulanabilir
        Assert.Equal("A", Scalar<string>(f, "SELECT company_id FROM material_request_items WHERE id=@id;", ("@id", item)));
    }
}
