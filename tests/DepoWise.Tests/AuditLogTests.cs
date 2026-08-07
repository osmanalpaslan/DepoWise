using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Sistem Logu filtreleri (madde 4, kullanıcı isteği 2026-08-06): Tarih Aralığı + kayıt sayısı,
/// performans için limit 1-5000 arasına sıkıştırılır (bkz. AuditLogService.List).</summary>
public class AuditLogTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly AuditLogService _audit;
    private readonly SessionContext _admin;

    public AuditLogTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_audit_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _audit = new AuditLogService(_factory);

        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    [Fact]
    public void TarihAraligi_YalnizAraliktakiKayitlariDoner()
    {
        _clock.UtcNow = DateTimeOffset.FromUnixTimeMilliseconds(1_000_000);
        var m1 = _materials.Create(_admin, new NewMaterial("M1", "M1"));
        _clock.UtcNow = DateTimeOffset.FromUnixTimeMilliseconds(2_000_000);
        var m2 = _materials.Create(_admin, new NewMaterial("M2", "M2"));
        _clock.UtcNow = DateTimeOffset.FromUnixTimeMilliseconds(3_000_000);
        _materials.Create(_admin, new NewMaterial("M3", "M3"));

        var result = _audit.List(_admin, fromMs: 1_500_000, toMs: 2_500_000);
        Assert.Single(result);
        Assert.Equal(m2, result[0].EntityId);
    }

    [Fact]
    public void TarihAraligiBos_TumKayitlariDoner()
    {
        _materials.Create(_admin, new NewMaterial("M1", "M1"));
        _materials.Create(_admin, new NewMaterial("M2", "M2"));

        var result = _audit.List(_admin);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void KayitSayisi_LimitUygulanir()
    {
        for (int i = 0; i < 5; i++) _materials.Create(_admin, new NewMaterial($"M{i}", $"M{i}"));

        var result = _audit.List(_admin, limit: 3);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void KayitSayisi_UstSinirBesBinileSikistirilir_CokBuyukLimitCokerse()
    {
        _materials.Create(_admin, new NewMaterial("M1", "M1"));

        // Performans korumasi (madde 4): asiri buyuk limit istense bile sorgu 5000 ile sinirlanir, cokmez.
        var result = _audit.List(_admin, limit: 1_000_000);
        Assert.Single(result);
    }

    [Fact]
    public void KayitlarSonTarihtenEskiyeSiralanir()
    {
        _clock.UtcNow = DateTimeOffset.FromUnixTimeMilliseconds(1_000_000);
        var first = _materials.Create(_admin, new NewMaterial("M1", "M1"));
        _clock.UtcNow = DateTimeOffset.FromUnixTimeMilliseconds(2_000_000);
        var second = _materials.Create(_admin, new NewMaterial("M2", "M2"));

        var result = _audit.List(_admin);
        Assert.Equal(second, result[0].EntityId);
        Assert.Equal(first, result[1].EntityId);
    }
}
