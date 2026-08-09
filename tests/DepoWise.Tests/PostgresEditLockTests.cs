using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// DÜZENLEME KİLİDİ — PostgreSQL karşılığı (İş #6, 2026-08-09).
///
/// Sunucu ve web PostgreSQL'de çalışır (CLAUDE.md §4); masaüstü SQLite'ta. Kilit iki lehçede de
/// AYNI davranmalıdır. <see cref="EditLockCoverageTests"/> SQLite tarafını kanıtlar; bu dosya aynı
/// senaryoları PostgreSQL'de tekrarlar.
///
/// ⚠️ Yalnız DEPOWISE_PG_URL (doğrulanmış BOŞ test veritabanı) ile koşar; yoksa ATLANIR.
/// Canlı veritabanına ASLA bağlanmaz (bkz. PostgresTestGuard).
/// </summary>
[Collection("PostgresSchema")]
public class PostgresEditLockTests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private sealed record Fixture(RequestService Requests, BranchService Branches, MaterialService Materials,
        SessionContext Admin, TestClock Clock);

    private static Fixture Setup()
    {
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();

        using (var conn = factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES('A', 'A', 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.ExecuteNonQuery();
        }

        var clock = new TestClock();
        var users = new UserService(factory, clock);
        var uid = users.EnsureInitialAdmin("A", "admin_a", "admin123", RoleKeys.CompanyAdmin);
        var admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        return new Fixture(
            new RequestService(factory, new StockService(factory, clock), clock),
            new BranchService(factory, clock),
            new MaterialService(factory, clock),
            admin, clock);
    }

    private static string NewRequestFor(Fixture f, string desc)
    {
        var mat = f.Materials.Create(f.Admin, new NewMaterial("M-" + Guid.NewGuid().ToString("N")[..6], "Filtre"));
        return f.Requests.Create(f.Admin, new NewRequest(new[] { new RequestItemInput(mat, 1m) }, Description: desc)).Id;
    }

    private static NewRequest DtoFor(Fixture f, string requestId, string desc)
    {
        var cur = f.Requests.GetForEdit(f.Admin, requestId);
        var items = cur.Items.Select(i => new RequestItemInput(i.MaterialId, i.Quantity, i.VehicleId)).ToList();
        return new NewRequest(items, Description: desc);
    }

    [SkippableFact]
    public void PostgreSQLde_talep_ESKI_surumle_kaydedilemez()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = Setup();
        var id = NewRequestFor(f, "ilk");
        var eskiSurum = f.Requests.GetForEdit(f.Admin, id).Version;      // B formu açtı

        f.Clock.UtcNow = f.Clock.UtcNow.AddMilliseconds(1000);
        f.Requests.Update(f.Admin, id, DtoFor(f, id, "A kaydetti"));     // A araya girdi

        Assert.Throws<ConcurrencyException>(() =>
            f.Requests.Update(f.Admin, id, DtoFor(f, id, "B'nin eski verisi"), eskiSurum));

        Assert.Equal("A kaydetti", f.Requests.GetForEdit(f.Admin, id).Description);
    }

    [SkippableFact]
    public void PostgreSQLde_talep_DOGRU_surumle_kaydedilir()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = Setup();
        var id = NewRequestFor(f, "ilk");
        var v = f.Requests.GetForEdit(f.Admin, id).Version;
        f.Requests.Update(f.Admin, id, DtoFor(f, id, "guncel"), v);

        var sonra = f.Requests.GetForEdit(f.Admin, id);
        Assert.Equal("guncel", sonra.Description);
        Assert.True(sonra.Version > v);
    }

    [SkippableFact]
    public void PostgreSQLde_talep_kalemleri_reddedilen_kayitta_DEGISMEZ()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = Setup();
        var id = NewRequestFor(f, "ilk");
        var eskiSurum = f.Requests.GetForEdit(f.Admin, id).Version;
        var eskiKalemSayisi = f.Requests.GetForEdit(f.Admin, id).Items.Count;

        f.Requests.Update(f.Admin, id, DtoFor(f, id, "A kaydetti"));

        var yeniMat = f.Materials.Create(f.Admin, new NewMaterial("M-EK", "Ek malzeme"));
        var kalemler = f.Requests.GetForEdit(f.Admin, id).Items
            .Select(i => new RequestItemInput(i.MaterialId, i.Quantity)).ToList();
        kalemler.Add(new RequestItemInput(yeniMat, 5m));

        Assert.Throws<ConcurrencyException>(() =>
            f.Requests.Update(f.Admin, id, new NewRequest(kalemler, Description: "eski veri"), eskiSurum));

        var sonra = f.Requests.GetForEdit(f.Admin, id);
        Assert.Equal(eskiKalemSayisi, sonra.Items.Count);   // rollback: kalem EKLENMEDİ
        Assert.Equal("A kaydetti", sonra.Description);
    }

    [SkippableFact]
    public void PostgreSQLde_sube_ESKI_surumle_kaydedilemez()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = Setup();
        var id = f.Branches.Create(f.Admin, new NewBranch("Merkez"));
        var eskiSurum = f.Branches.List(f.Admin).Single(b => b.Id == id).Version;

        f.Clock.UtcNow = f.Clock.UtcNow.AddMilliseconds(1000);
        f.Branches.Update(f.Admin, id, new NewBranch("A kaydetti"));

        Assert.Throws<ConcurrencyException>(() =>
            f.Branches.Update(f.Admin, id, new NewBranch("B'nin eski verisi"), expectedVersion: eskiSurum));

        Assert.Equal("A kaydetti", f.Branches.List(f.Admin).Single(b => b.Id == id).Name);
    }

    [SkippableFact]
    public void PostgreSQLde_sube_DOGRU_surumle_kaydedilir()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = Setup();
        var id = f.Branches.Create(f.Admin, new NewBranch("Merkez"));
        var v = f.Branches.List(f.Admin).Single(b => b.Id == id).Version;
        f.Branches.Update(f.Admin, id, new NewBranch("Merkez Yeni"), expectedVersion: v);

        var sonra = f.Branches.List(f.Admin).Single(b => b.Id == id);
        Assert.Equal("Merkez Yeni", sonra.Name);
        Assert.True(sonra.Version > v);
    }

    [SkippableFact]
    public void PostgreSQLde_sube_SIFRESI_kilit_reddinde_DEGISMEZ()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = Setup();
        var id = f.Branches.Create(f.Admin, new NewBranch("Merkez", "branch", null, "K1", "ilkSifre"));
        var eskiSurum = f.Branches.List(f.Admin).Single(b => b.Id == id).Version;

        f.Branches.Update(f.Admin, id, new NewBranch("A kaydetti", "branch", null, "K1", "yeniSifre"));
        var araSurum = f.Branches.List(f.Admin).Single(b => b.Id == id).Version;

        Assert.Throws<ConcurrencyException>(() =>
            f.Branches.Update(f.Admin, id, new NewBranch("B eski", "branch", null, "K1", "bSifresi"), expectedVersion: eskiSurum));

        var sonra = f.Branches.List(f.Admin).Single(b => b.Id == id);
        Assert.Equal("A kaydetti", sonra.Name);
        Assert.Equal(araSurum, sonra.Version);   // sürüm İLERLEMEDİ → UPDATE hiç uygulanmadı
        Assert.True(sonra.HasPassword);
    }
}
