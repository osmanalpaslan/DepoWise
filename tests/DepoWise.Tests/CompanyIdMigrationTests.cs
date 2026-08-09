using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// M-S1a — <c>material_request_items</c> + <c>maintenance_materials</c> firma (company_id) migration'ı
/// (Migration062). Veri TAŞIYAN migration olduğu için testler "sonuç doğru mu"yu değil, "hiçbir kayıt
/// kaybolmuyor / yanlış firmaya gitmiyor / çözülemeyen kayıtta DURUYOR mu"yu da kanıtlar.
///
/// Senaryolar: boş DB · mevcut (dolu) DB · tek firma · çok firma · tekrar çalıştırma · rollback ·
/// çözülemeyen kayıtta güvenli duruş · NOT NULL zorlaması · firma sızıntısının engellenmesi.
/// Aynı davranış PostgreSQL'de <see cref="PostgresCompanyIdMigrationTests"/> ile doğrulanır.
/// </summary>
public class CompanyIdMigrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;

    public CompanyIdMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_ms1a_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }

    // ── yardımcılar ────────────────────────────────────────────────────────────────────────

    /// <summary>Migration ÖNCESİ şema: yalnız 61'e kadar uygulanır (company_id kolonu henüz YOK).</summary>
    private void MigrateTo61() =>
        new MigrationRunner(_factory, MigrationCatalog.All().Where(m => m.Version <= 61)).Run();

    private void MigrateAll() => new MigrationRunner(_factory).Run();

    private void Exec(string sql, params (string, object?)[] ps)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>FK KAPALI ham bağlantı — yalnız "bozuk veri" senaryosunu KURMAK için. Gerçek hayatta
    /// FK'ler bunu engeller (bkz. testin yorumu); migration'daki guard ikinci savunma hattıdır.</summary>
    private void ExecNoFk(string sql, params (string, object?)[] ps)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString());
        conn.Open();
        using (var pragma = conn.CreateCommand()) { pragma.CommandText = "PRAGMA foreign_keys=OFF;"; pragma.ExecuteNonQuery(); }
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private T Scalar<T>(string sql, params (string, object?)[] ps)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.AddWithValue(k, v ?? DBNull.Value);
        var o = cmd.ExecuteScalar();
        return o is null or DBNull ? default! : (T)Convert.ChangeType(o, typeof(T));
    }

    private void SeedCompany(string id) =>
        Exec("INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
             "VALUES(@id, @id, 1, 1, 1, 0, 5, 10, 3);", ("@id", id));

    /// <summary>Migration ÖNCESİ hâliyle bir talep + kalem (kalemde company_id YOK).</summary>
    private (string Request, string Item) SeedLegacyRequestItem(string companyId)
    {
        var req = Guid.NewGuid().ToString("N");
        var item = Guid.NewGuid().ToString("N");
        var mat = Guid.NewGuid().ToString("N");
        Exec("INSERT INTO materials(id, company_id, code, name, min_stock, unit_price, created_at, updated_at, version, is_deleted) " +
             "VALUES(@id, @c, @id, 'Malzeme', '0', '0', 1, 1, 1, 0);", ("@id", mat), ("@c", companyId));
        Exec("INSERT INTO material_requests(id, company_id, doc_no, request_date, status, created_at, updated_at, version, is_deleted, priority) " +
             "VALUES(@id, @c, @id, 1, 'draft', 1, 1, 1, 0, 'normal');",
             ("@id", req), ("@c", companyId));
        Exec("INSERT INTO material_request_items(id, request_id, material_id, quantity) VALUES(@id, @r, @m, '1');",
             ("@id", item), ("@r", req), ("@m", mat));
        return (req, item);
    }

    /// <summary>Migration ÖNCESİ hâliyle bir bakım + malzeme satırı (satırda company_id YOK).</summary>
    private (string Maintenance, string Line) SeedLegacyMaintenanceMaterial(string companyId)
    {
        var veh = Guid.NewGuid().ToString("N");
        var def = Guid.NewGuid().ToString("N");
        var mnt = Guid.NewGuid().ToString("N");
        var line = Guid.NewGuid().ToString("N");
        var mat = Guid.NewGuid().ToString("N");
        Exec("INSERT INTO materials(id, company_id, code, name, min_stock, unit_price, created_at, updated_at, version, is_deleted) " +
             "VALUES(@id, @c, @id, 'Malzeme', '0', '0', 1, 1, 1, 0);", ("@id", mat), ("@c", companyId));
        Exec("INSERT INTO vehicles(id, company_id, internal_code, current_meter, meter_unit, created_at, updated_at, version, is_deleted) " +
             "VALUES(@id, @c, @id, '0', 'km', 1, 1, 1, 0);", ("@id", veh), ("@c", companyId));
        Exec("INSERT INTO maintenance_definitions(id, company_id, name, interval_value, interval_unit, created_at, updated_at, version, is_deleted) " +
             "VALUES(@id, @c, 'Periyodik', '100', 'km', 1, 1, 1, 0);", ("@id", def), ("@c", companyId));
        Exec("INSERT INTO vehicle_maintenances(id, company_id, vehicle_id, maintenance_def_id, operation_id, is_cancelled, created_at, updated_at, version, is_deleted) " +
             "VALUES(@id, @c, @v, @d, @id, 0, 1, 1, 1, 0);", ("@id", mnt), ("@c", companyId), ("@v", veh), ("@d", def));
        Exec("INSERT INTO maintenance_materials(id, maintenance_id, material_id, quantity, from_team_stock) VALUES(@id, @mt, @m, '1', 0);",
             ("@id", line), ("@mt", mnt), ("@m", mat));
        return (mnt, line);
    }

    private bool ColumnExists(string table, string column)
    {
        using var conn = _factory.Create();
        return DbIntrospect.ColumnNames(conn, table).Contains(column);
    }

    private string? CompanyOf(string table, string id) =>
        Scalar<string>($"SELECT company_id FROM {table} WHERE id=@id;", ("@id", id));

    // ── 1. BOŞ VERİTABANI ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Bos_veritabaninda_migration_kolonlari_ve_indeksi_olusturur()
    {
        MigrateAll();

        Assert.True(ColumnExists("material_request_items", "company_id"));
        Assert.True(ColumnExists("maintenance_materials", "company_id"));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ix_material_request_items_company';"));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ix_maintenance_materials_company';"));
    }

    [Fact]
    public void Bos_veritabaninda_eski_indeksler_KORUNUR()
    {
        MigrateAll();
        // Tablo yeniden kurulduğu için eski indeksin de geri gelmesi ZORUNLU (yoksa liste sorguları yavaşlar).
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ix_material_request_items';"));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ix_maintenance_materials';"));
    }

    // ── 2. MEVCUT (DOLU) VERİTABANI — TEK FİRMA ────────────────────────────────────────────

    [Fact]
    public void Mevcut_kayitlar_UST_KAYITTAN_gelen_firmaya_tasinir()
    {
        MigrateTo61();
        SeedCompany("A");
        var (_, item) = SeedLegacyRequestItem("A");
        var (_, line) = SeedLegacyMaintenanceMaterial("A");

        MigrateAll();

        Assert.Equal("A", CompanyOf("material_request_items", item));
        Assert.Equal("A", CompanyOf("maintenance_materials", line));
    }

    [Fact]
    public void Migration_HICBIR_KAYDI_SILMEZ_sayilar_ayni_kalir()
    {
        MigrateTo61();
        SeedCompany("A");
        for (int i = 0; i < 5; i++) SeedLegacyRequestItem("A");
        for (int i = 0; i < 3; i++) SeedLegacyMaintenanceMaterial("A");
        var beforeItems = Scalar<int>("SELECT COUNT(*) FROM material_request_items;");
        var beforeLines = Scalar<int>("SELECT COUNT(*) FROM maintenance_materials;");

        MigrateAll();

        Assert.Equal(5, beforeItems);
        Assert.Equal(3, beforeLines);
        Assert.Equal(beforeItems, Scalar<int>("SELECT COUNT(*) FROM material_request_items;"));
        Assert.Equal(beforeLines, Scalar<int>("SELECT COUNT(*) FROM maintenance_materials;"));
        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM material_request_items WHERE company_id IS NULL OR company_id='';"));
        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM maintenance_materials WHERE company_id IS NULL OR company_id='';"));
    }

    [Fact]
    public void Migration_diger_kolonlarin_degerlerini_BOZMAZ()
    {
        MigrateTo61();
        SeedCompany("A");
        var (req, item) = SeedLegacyRequestItem("A");
        Exec("UPDATE material_request_items SET quantity='7.5', note='deneme' WHERE id=@id;", ("@id", item));

        MigrateAll();

        Assert.Equal("7.5", Scalar<string>("SELECT quantity FROM material_request_items WHERE id=@id;", ("@id", item)));
        Assert.Equal("deneme", Scalar<string>("SELECT note FROM material_request_items WHERE id=@id;", ("@id", item)));
        Assert.Equal(req, Scalar<string>("SELECT request_id FROM material_request_items WHERE id=@id;", ("@id", item)));
    }

    // ── 3. BİRDEN FAZLA FİRMA ──────────────────────────────────────────────────────────────

    [Fact]
    public void Iki_firmanin_kayitlari_KENDI_firmasina_tasinir_karismaz()
    {
        MigrateTo61();
        SeedCompany("A"); SeedCompany("B");
        var (_, itemA) = SeedLegacyRequestItem("A");
        var (_, itemB) = SeedLegacyRequestItem("B");
        var (_, lineA) = SeedLegacyMaintenanceMaterial("A");
        var (_, lineB) = SeedLegacyMaintenanceMaterial("B");

        MigrateAll();

        Assert.Equal("A", CompanyOf("material_request_items", itemA));
        Assert.Equal("B", CompanyOf("material_request_items", itemB));
        Assert.Equal("A", CompanyOf("maintenance_materials", lineA));
        Assert.Equal("B", CompanyOf("maintenance_materials", lineB));
        // Çapraz kontrol: hiçbir satır YANLIŞ firmaya gitmemiş olmalı.
        Assert.Equal(0, Scalar<int>(@"
SELECT COUNT(*) FROM material_request_items i JOIN material_requests p ON p.id=i.request_id
WHERE i.company_id <> p.company_id;"));
        Assert.Equal(0, Scalar<int>(@"
SELECT COUNT(*) FROM maintenance_materials mm JOIN vehicle_maintenances p ON p.id=mm.maintenance_id
WHERE mm.company_id <> p.company_id;"));
    }

    // ── 4. ÇÖZÜLEMEYEN KAYIT → GÜVENLİ DURUŞ ───────────────────────────────────────────────

    /// <summary>Üst kaydı OLMAYAN (yetim) kalem üretir. FK açıkken imkânsızdır; bu yüzden FK kapalı ham
    /// bağlantıyla kurulur — amaç migration'ın guard'ını kanıtlamaktır.</summary>
    private string SeedOrphanRequestItem()
    {
        var item = Guid.NewGuid().ToString("N");
        ExecNoFk("INSERT INTO material_request_items(id, request_id, material_id, quantity) VALUES(@id, @yok, @yok, '1');",
                 ("@id", item), ("@yok", "OLMAYAN-" + Guid.NewGuid().ToString("N")));
        return item;
    }

    [Fact]
    public void Firmasi_belirlenemeyen_kayit_varsa_migration_DURUR_ve_hicbir_sey_degismez()
    {
        MigrateTo61();
        SeedCompany("A");
        SeedOrphanRequestItem();   // üst kaydı yok → firması KESİN belirlenemez
        var before = Scalar<int>("SELECT COUNT(*) FROM material_request_items;");

        var ex = Assert.ThrowsAny<Exception>(() => MigrateAll());

        Assert.Contains("M-S1a", ex.Message);
        Assert.Contains("material_request_items", ex.Message);
        // Transaction geri alındı: kolon eklenmedi, kayıt sayısı değişmedi, sürüm işlenmedi.
        Assert.False(ColumnExists("material_request_items", "company_id"));
        Assert.Equal(before, Scalar<int>("SELECT COUNT(*) FROM material_request_items;"));
        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM schema_migrations WHERE version=62;"));
    }

    [Fact]
    public void Cozulemeyen_kayit_TAHMINLE_tasinmaz_hicbir_satir_varsayilan_firmaya_baglanmaz()
    {
        MigrateTo61();
        SeedCompany("A");
        SeedLegacyRequestItem("A");   // çözülebilir
        SeedOrphanRequestItem();      // çözülemez

        Assert.ThrowsAny<Exception>(() => MigrateAll());

        // Kısmi taşıma OLMAMALI: kolon hiç eklenmemiş olmalı (ya hep ya hiç).
        Assert.False(ColumnExists("material_request_items", "company_id"));
        Assert.Equal(2, Scalar<int>("SELECT COUNT(*) FROM material_request_items;"));   // iki satır da DURUYOR
    }

    // ── 5. TEKRAR ÇALIŞTIRMA (idempotent) ──────────────────────────────────────────────────

    [Fact]
    public void Migration_ikinci_kez_calistirildiginda_veri_degismez()
    {
        MigrateTo61();
        SeedCompany("A");
        var (_, item) = SeedLegacyRequestItem("A");
        MigrateAll();
        var afterFirst = Scalar<int>("SELECT COUNT(*) FROM material_request_items;");

        MigrateAll();   // runner zaten atlar
        MigrateAll();

        Assert.Equal(afterFirst, Scalar<int>("SELECT COUNT(*) FROM material_request_items;"));
        Assert.Equal("A", CompanyOf("material_request_items", item));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM schema_migrations WHERE version=62;"));
    }

    [Fact]
    public void Surum_kaydi_silinip_tekrar_calistirilirsa_bile_guvenli_calisir()
    {
        MigrateTo61();
        SeedCompany("A");
        var (_, item) = SeedLegacyRequestItem("A");
        MigrateAll();

        // Kolon zaten var; sürüm kaydı elle silinse bile migration hiçbir şeyi bozmamalı (ikinci savunma hattı).
        Exec("DELETE FROM schema_migrations WHERE version=62;");
        MigrateAll();

        Assert.Equal("A", CompanyOf("material_request_items", item));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM material_request_items;"));
    }

    // ── 6. ROLLBACK (geri alma) ────────────────────────────────────────────────────────────

    [Fact]
    public void Rollback_kolonu_kaldirir_is_kayitlari_KALIR_ve_yeniden_uygulanabilir()
    {
        MigrateTo61();
        SeedCompany("A");
        var (_, item) = SeedLegacyRequestItem("A");
        MigrateAll();

        // Belgelenen geri alma adımları (SQLite + PostgreSQL ortak sözdizimi).
        // ÖNCE İNDEKS: SQLite, indeksin kullandığı kolonu DROP ettirmez (bu test onu yakaladı).
        Exec("DROP INDEX IF EXISTS ix_material_request_items_company;");
        Exec("DROP INDEX IF EXISTS ix_maintenance_materials_company;");
        Exec("ALTER TABLE material_request_items DROP COLUMN company_id;");
        Exec("ALTER TABLE maintenance_materials DROP COLUMN company_id;");
        Exec("DELETE FROM schema_migrations WHERE version=62;");

        Assert.False(ColumnExists("material_request_items", "company_id"));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM material_request_items;"));   // iş kaydı DURUYOR

        MigrateAll();   // yeniden uygulanabilir
        Assert.Equal("A", CompanyOf("material_request_items", item));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM material_request_items;"));
    }

    // ── 7. NOT NULL ZORLAMASI ──────────────────────────────────────────────────────────────

    [Fact]
    public void company_id_VARSAYILANI_YOKTUR_eksik_birakan_INSERT_hata_verir()
    {
        MigrateAll();
        SeedCompany("A");
        var (req, _) = SeedLegacyRequestItemAfterMigration("A");

        // company_id verilmeden INSERT → NOT NULL ihlali (sessizce boş/yanlış firmaya bağlanmaz)
        var ex = Assert.ThrowsAny<DbException>(() =>
            Exec("INSERT INTO material_request_items(id, request_id, material_id, quantity) VALUES(@id, @r, @r, '1');",
                 ("@id", Guid.NewGuid().ToString("N")), ("@r", req)));
        Assert.Contains("NOT NULL", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Migration SONRASI şemada talep + kalem (company_id dolu).</summary>
    private (string Request, string Item) SeedLegacyRequestItemAfterMigration(string companyId)
    {
        var req = Guid.NewGuid().ToString("N");
        var item = Guid.NewGuid().ToString("N");
        Exec("INSERT INTO materials(id, company_id, code, name, min_stock, unit_price, created_at, updated_at, version, is_deleted) " +
             "VALUES(@id, @c, @id, 'Malzeme', '0', '0', 1, 1, 1, 0);", ("@id", req), ("@c", companyId));
        Exec("INSERT INTO material_requests(id, company_id, doc_no, request_date, status, created_at, updated_at, version, is_deleted, priority) " +
             "VALUES(@id, @c, @id, 1, 'draft', 1, 1, 1, 0, 'normal');", ("@id", req), ("@c", companyId));
        Exec("INSERT INTO material_request_items(id, company_id, request_id, material_id, quantity) VALUES(@id, @c, @r, @r, '1');",
             ("@id", item), ("@c", companyId), ("@r", req));
        return (req, item);
    }

    // ── 8. ASIL AMAÇ: FİRMA SIZINTISININ ENGELLENMESİ ──────────────────────────────────────

    [Fact]
    public void Senkron_snapshotu_artik_YALNIZ_kendi_firmasinin_kalemlerini_tasir()
    {
        MigrateTo61();
        SeedCompany("A"); SeedCompany("B");
        var (_, itemA) = SeedLegacyRequestItem("A");
        var (_, itemB) = SeedLegacyRequestItem("B");
        var (_, lineA) = SeedLegacyMaintenanceMaterial("A");
        var (_, lineB) = SeedLegacyMaintenanceMaterial("B");
        MigrateAll();

        var sync = new BusinessSyncService(_factory, new FixedClock());
        var snapshotA = sync.BuildSnapshot("A");

        Assert.Contains(itemA, snapshotA);
        Assert.Contains(lineA, snapshotA);
        Assert.DoesNotContain(itemB, snapshotA);    // ← M-S1a'nın asıl amacı
        Assert.DoesNotContain(lineB, snapshotA);
    }

    [Fact]
    public void Migration_ONCESI_snapshot_diger_firmanin_kalemlerini_TASIYORDU_regresyon_kaniti()
    {
        // Bu test, düzeltilen açığın gerçek olduğunu kanıtlar: 61. sürümde (kolon yokken) filtre uygulanamıyordu.
        MigrateTo61();
        SeedCompany("A"); SeedCompany("B");
        SeedLegacyRequestItem("A");
        var (_, itemB) = SeedLegacyRequestItem("B");

        var sync = new BusinessSyncService(_factory, new FixedClock());
        Assert.Contains(itemB, sync.BuildSnapshot("A"));   // ESKİ DAVRANIŞ: B'nin kalemi A'nın paketinde
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }
}
