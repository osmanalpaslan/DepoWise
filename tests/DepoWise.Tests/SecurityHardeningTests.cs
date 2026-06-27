using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using DepoWise.Application.Sync;
using Xunit;

namespace DepoWise.Tests;

public class SecurityHardeningTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();

    public SecurityHardeningTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_sec_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(TimeSpan t) => UtcNow = UtcNow.Add(t);
    }

    // ---- Log redaction (PII'siz / secret'siz) ----
    [Fact]
    public void Redaction_SecretleriMaskeler()
    {
        var line = "user=admin password=Gizli123 token=abc.def authorization=Bearer eyJhbGciOi";
        var red = LogRedactor.Redact(line);
        Assert.DoesNotContain("Gizli123", red);
        Assert.DoesNotContain("abc.def", red);
        Assert.DoesNotContain("eyJhbGciOi", red);
        Assert.Contains("user=admin", red); // hassas olmayan korunur
    }

    [Fact]
    public void Redaction_JsonVeConnString()
    {
        var json = "{\"password\":\"p@ss\",\"ConnectionString\":\"Server=x;Pwd=secret\"}";
        var red = LogRedactor.Redact(json);
        Assert.DoesNotContain("p@ss", red);
        Assert.True(LogRedactor.IsSensitiveKey("Password"));
        Assert.False(LogRedactor.IsSensitiveKey("username"));
    }

    // ---- Rate limit (fail-closed) ----
    [Fact]
    public void RateLimit_Login_5Sonra_Engeller_PencereSonra_Acilir()
    {
        var rl = RateLimiter.Login(() => _clock.UtcNow);
        for (int i = 0; i < 5; i++) Assert.True(rl.Check("ip:1").Allowed);
        var blocked = rl.Check("ip:1");
        Assert.False(blocked.Allowed);
        Assert.True(blocked.RetrySeconds > 0);

        // Pencere dolunca tekrar açılır
        _clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        Assert.True(rl.Check("ip:1").Allowed);
    }

    [Fact]
    public void RateLimit_AnahtarBazli_Izole()
    {
        var rl = new RateLimiter(2, TimeSpan.FromMinutes(1), () => _clock.UtcNow);
        Assert.True(rl.Check("a").Allowed);
        Assert.True(rl.Check("a").Allowed);
        Assert.False(rl.Check("a").Allowed);
        Assert.True(rl.Check("b").Allowed); // farklı anahtar etkilenmez
    }

    // ---- Cihaz token rotasyonu / revoke cascade ----
    [Fact]
    public void TokenRotasyonu_EskiTokenGecersiz()
    {
        var users = new UserService(_factory, _clock);
        var admin = new SessionContext(users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin), "A",
            new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var enroll = new EnrollmentService(_factory, _clock);
        var server = new SyncServer(_factory, _clock);

        var key = enroll.CreateEnrollmentKey(admin);
        var dev = enroll.Enroll("A", key, "Cihaz");
        var oldToken = enroll.ApproveDevice(admin, dev.DeviceId).Token;

        // Eski token çalışır
        server.Pull(oldToken, 0);

        // Rotasyon → eski token geçersiz, yeni token çalışır
        var newToken = enroll.RotateDeviceToken(admin, dev.DeviceId).Token;
        Assert.Throws<ForbiddenException>(() => server.Pull(oldToken, 0));
        server.Pull(newToken, 0);
    }

    [Fact]
    public void Revoke_Cascade_PushPull_403()
    {
        var users = new UserService(_factory, _clock);
        var admin = new SessionContext(users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin), "A",
            new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var enroll = new EnrollmentService(_factory, _clock);
        var server = new SyncServer(_factory, _clock);
        var key = enroll.CreateEnrollmentKey(admin);
        var dev = enroll.Enroll("A", key, "Cihaz");
        var token = enroll.ApproveDevice(admin, dev.DeviceId).Token;

        enroll.RevokeDevice(admin, dev.DeviceId);
        Assert.Throws<ForbiddenException>(() => server.Pull(token, 0));
        Assert.Throws<ForbiddenException>(() => server.Push(token, new[] { new SyncOperation("o", "material", "m", "{}") }));
    }

    // ---- Audit kapsamı: correlation id taşınabilir ----
    [Fact]
    public void Audit_CorrelationId_Taşınır()
    {
        var cid = Correlation.New();
        var e = new AuditEntry("A", "material", "m1", AuditActions.Create, "u1", CorrelationId: cid);
        Assert.Equal(cid, e.CorrelationId);
        Assert.False(string.IsNullOrEmpty(cid));
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
