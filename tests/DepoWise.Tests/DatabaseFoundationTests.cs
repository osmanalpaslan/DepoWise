using DepoWise.Application.Common;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using Xunit;

namespace DepoWise.Tests;

public class DatabaseFoundationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();

    public DatabaseFoundationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_fnd_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    [Fact]
    public void Migration_SifirDB_Uygulanir_VeIdempotent()
    {
        var runner = new MigrationRunner(_factory);
        var first = runner.Run();
        Assert.Contains(1, first);
        Assert.Contains(2, first);
        var latest = runner.CurrentVersion();
        Assert.Equal(first.Max(), latest);

        // İkinci çalıştırma mevcut DB üzerinde güvenli: yeni uygulanan yok.
        var second = new MigrationRunner(_factory).Run();
        Assert.Empty(second);
        Assert.Equal(latest, new MigrationRunner(_factory).CurrentVersion());
    }

    [Fact]
    public void Tenant_Izolasyonu_BaskaFirmaGorunmez()
    {
        new MigrationRunner(_factory).Run();
        var repo = new BranchRepository(_factory, _clock);
        repo.EnsureCompany("A", "Firma A");
        repo.EnsureCompany("B", "Firma B");

        repo.Add(new TenantContext("A"), "A-Şube-1");
        repo.Add(new TenantContext("A"), "A-Şube-2");
        repo.Add(new TenantContext("B"), "B-Şube-1");

        var a = repo.List(new TenantContext("A"), new PageRequest { Limit = 50 });
        var b = repo.List(new TenantContext("B"), new PageRequest { Limit = 50 });

        Assert.Equal(2, a.Items.Count);
        Assert.All(a.Items, x => Assert.Equal("A", x.CompanyId));
        Assert.Single(b.Items);
        Assert.All(b.Items, x => Assert.Equal("B", x.CompanyId));
    }

    [Fact]
    public void TenantGuard_BosCompany_FailClosed()
    {
        Assert.Throws<InvalidOperationException>(() => new TenantContext(""));
    }

    [Fact]
    public void SoftDelete_KayitFizikselSilinmez_ListedenDuser()
    {
        new MigrationRunner(_factory).Run();
        var repo = new BranchRepository(_factory, _clock);
        repo.EnsureCompany("A", "Firma A");
        var tenant = new TenantContext("A");
        var id = repo.Add(tenant, "Silinecek");

        repo.SoftDelete(tenant, id);

        // Listede görünmez
        var list = repo.List(tenant, new PageRequest { Limit = 50 });
        Assert.DoesNotContain(list.Items, x => x.Id == id);

        // Ama satır fiziksel olarak durur (is_deleted=1)
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT is_deleted FROM branches WHERE id = $id;";
        cmd.AddWithValue("$id", id);
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    [Fact]
    public void SoftDelete_BaskaFirmaSilemez()
    {
        new MigrationRunner(_factory).Run();
        var repo = new BranchRepository(_factory, _clock);
        repo.EnsureCompany("A", "A"); repo.EnsureCompany("B", "B");
        var id = repo.Add(new TenantContext("A"), "A-Şube");

        repo.SoftDelete(new TenantContext("B"), id); // farklı tenant → etkisiz

        var list = repo.List(new TenantContext("A"), new PageRequest { Limit = 50 });
        Assert.Contains(list.Items, x => x.Id == id);
    }

    [Fact]
    public void Audit_CreateVeDelete_Yazilir()
    {
        new MigrationRunner(_factory).Run();
        var repo = new BranchRepository(_factory, _clock);
        repo.EnsureCompany("A", "A");
        var tenant = new TenantContext("A");
        var id = repo.Add(tenant, "Şube", userId: "u1");
        repo.SoftDelete(tenant, id, userId: "u1");

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT action FROM audit_logs WHERE entity_type='branch' AND entity_id=$id ORDER BY created_at;";
        cmd.AddWithValue("$id", id);
        var actions = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) actions.Add(r.GetString(0));

        Assert.Contains(AuditActions.Create, actions);
        Assert.Contains(AuditActions.Delete, actions);
    }

    [Fact]
    public void Keyset_Sayfalama_TumKayitlar_TekrarYok()
    {
        new MigrationRunner(_factory).Run();
        var repo = new BranchRepository(_factory, _clock);
        repo.EnsureCompany("A", "A");
        var tenant = new TenantContext("A");

        var created = new HashSet<string>();
        for (int i = 0; i < 25; i++)
        {
            _clock.Advance(1000); // benzersiz created_at → kararlı sıralama
            created.Add(repo.Add(tenant, $"Şube-{i:00}"));
        }

        var seen = new HashSet<string>();
        string? cursor = null;
        int pages = 0;
        do
        {
            var page = repo.List(tenant, new PageRequest { Limit = 10, Cursor = cursor });
            foreach (var item in page.Items)
                Assert.True(seen.Add(item.Id), "Aynı kayıt iki sayfada görünmemeli");
            cursor = page.NextCursor;
            pages++;
            Assert.True(pages <= 10, "Sonsuz döngü koruması");
        } while (cursor is not null);

        Assert.Equal(created, seen); // tüm kayıtlar tam bir kez
        Assert.Equal(3, pages);      // 25 kayıt / 10 → 3 sayfa
    }

    [Fact]
    public void ZamanDamgalari_UnixMs_Yazilir()
    {
        new MigrationRunner(_factory).Run();
        var repo = new BranchRepository(_factory, _clock);
        repo.EnsureCompany("A", "A");
        var id = repo.Add(new TenantContext("A"), "Şube");

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT created_at, updated_at, version FROM branches WHERE id=$id;";
        cmd.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal(_clock.UtcNow.ToUnixTimeMilliseconds(), r.GetInt64(0));
        Assert.Equal(r.GetInt64(0), r.GetInt64(1));
        Assert.Equal(1L, r.GetInt64(2));
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
