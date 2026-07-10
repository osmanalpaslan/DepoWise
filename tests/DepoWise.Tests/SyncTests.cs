using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Sync;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

public class SyncTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly EnrollmentService _enroll;
    private readonly SyncServer _server;
    private readonly SessionContext _admin;

    public SyncTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_sync_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _enroll = new EnrollmentService(_factory, _clock);
        _server = new SyncServer(_factory, _clock);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(TimeSpan t) => UtcNow = UtcNow.Add(t);
    }

    private string ActiveDeviceToken()
    {
        var key = _enroll.CreateEnrollmentKey(_admin);
        var dev = _enroll.Enroll("A", key, "Personel-1");
        return _enroll.ApproveDevice(_admin, dev.DeviceId).Token;
    }

    // ---- Makine şube ataması (admin otoriter, login ezmez) ----
    [Fact]
    public void Makine_Sube_AdminAtar_LoginEzmez()
    {
        var branches = new BranchService(_factory, _clock);
        var branchId = branches.Create(_admin, new NewBranch("Merkez"));

        // Yeni makine kaydı: login şubesi gönderilse bile ARTIK yazılmaz → şubesiz.
        var reg = _enroll.RegisterSelf("A", "PC-1", null, branchId);
        Assert.Null(reg.BranchId);

        // Admin şube atar → otoriter.
        _enroll.AssignBranch(_admin, reg.DeviceId, branchId);

        // Sonraki heartbeat login şubesini ezemez; admin ataması korunur.
        var reg2 = _enroll.RegisterSelf("A", "PC-1", null, "baska-sube-denemesi");
        Assert.Equal(branchId, reg2.BranchId);
        Assert.Equal("Merkez", reg2.BranchName);
    }

    [Fact]
    public void Makine_Sube_Atama_YalnizAdmin_VeGecerliSube()
    {
        var branches = new BranchService(_factory, _clock);
        var branchId = branches.Create(_admin, new NewBranch("Merkez"));
        var reg = _enroll.RegisterSelf("A", "PC-2");

        // Personel (admin değil) atayamaz.
        var staff = new SessionContext("staff1", "A", new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _enroll.AssignBranch(staff, reg.DeviceId, branchId));

        // Var olmayan/başka firmanın şubesi atanamaz.
        Assert.Throws<ForbiddenException>(() => _enroll.AssignBranch(_admin, reg.DeviceId, "olmayan-sube"));

        // Geçerli şube → atanır; boş → atama kaldırılır (şubesiz).
        _enroll.AssignBranch(_admin, reg.DeviceId, branchId);
        Assert.Equal(branchId, _enroll.RegisterSelf("A", "PC-2").BranchId);
        _enroll.AssignBranch(_admin, reg.DeviceId, null);
        Assert.Null(_enroll.RegisterSelf("A", "PC-2").BranchId);
    }

    // ---- Enrollment ----
    [Fact]
    public void Enrollment_Anahtar_TekKullanimlik()
    {
        var key = _enroll.CreateEnrollmentKey(_admin);
        _enroll.Enroll("A", key, "Cihaz-1"); // ilk kullanım ok
        Assert.Throws<ForbiddenException>(() => _enroll.Enroll("A", key, "Cihaz-2")); // ikinci → red
    }

    [Fact]
    public void Enrollment_Anahtar_10dk_SonraGecersiz()
    {
        var key = _enroll.CreateEnrollmentKey(_admin);
        _clock.Advance(EnrollmentService.KeyTtl + TimeSpan.FromMinutes(1));
        Assert.Throws<ForbiddenException>(() => _enroll.Enroll("A", key, "Cihaz"));
    }

    [Fact]
    public void Enrollment_YanlisAnahtar_Reddedilir()
        => Assert.Throws<ForbiddenException>(() => _enroll.Enroll("A", "yanlis", "Cihaz"));

    // ---- Cihaz durumu ----
    [Fact]
    public void Push_OnaysizCihaz_403()
    {
        var key = _enroll.CreateEnrollmentKey(_admin);
        _enroll.Enroll("A", key, "Cihaz"); // pending (onaysız)
        // pending cihazın token'ı yok → geçersiz token = 403
        Assert.Throws<ForbiddenException>(() => _server.Push("herhangi", Ops(("material", "m1"))));
    }

    [Fact]
    public void Push_RevokedCihaz_403()
    {
        var key = _enroll.CreateEnrollmentKey(_admin);
        var dev = _enroll.Enroll("A", key, "Cihaz");
        var token = _enroll.ApproveDevice(_admin, dev.DeviceId).Token;
        _enroll.RevokeDevice(_admin, dev.DeviceId);
        Assert.Throws<ForbiddenException>(() => _server.Push(token, Ops(("material", "m1"))));
        Assert.Throws<ForbiddenException>(() => _server.Pull(token, 0));
    }

    // ---- Idempotency / retry ----
    [Fact]
    public void Push_AyniOperation_IkinciKez_AlreadyApplied()
    {
        var token = ActiveDeviceToken();
        var ops = new[] { new SyncOperation("op-1", "material", "m1", "{\"name\":\"x\"}") };
        var first = _server.Push(token, ops);
        var second = _server.Push(token, ops); // retry
        Assert.Equal(SyncOpResult.Accepted, first[0].Result);
        Assert.Equal(SyncOpResult.AlreadyApplied, second[0].Result);

        // server_changes'te tek kayıt (çift yazılmadı)
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM server_changes WHERE operation_id='op-1';";
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    // ---- Kritik işlem: LWW yok, sunucu doğrulaması zorunlu ----
    [Fact]
    public void Push_Kritik_DogrulayiciYok_Reddedilir()
    {
        var token = ActiveDeviceToken();
        var ops = new[] { new SyncOperation("op-stk", "stock_movement", "s1", "{}") };
        var res = _server.Push(token, ops); // validator null
        Assert.Equal(SyncOpResult.Rejected, res[0].Result);
    }

    [Fact]
    public void Push_Kritik_SunucuDogrulamasi_RedVeyaKabul()
    {
        var token = ActiveDeviceToken();
        // Sunucu otoriteli: geçersiz stok → reddet
        SyncServer.CriticalValidator validator = (companyId, op) =>
            op.PayloadJson.Contains("\"qty\":-") ? (false, "Negatif stok") : (true, null);

        var bad = _server.Push(token, new[] { new SyncOperation("op-b", "stock_movement", "s1", "{\"qty\":-5}") }, validator);
        Assert.Equal(SyncOpResult.Rejected, bad[0].Result);

        var good = _server.Push(token, new[] { new SyncOperation("op-g", "stock_movement", "s2", "{\"qty\":5}") }, validator);
        Assert.Equal(SyncOpResult.Accepted, good[0].Result);

        // Reddedilen → conflict kuyruğunda
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sync_conflicts WHERE operation_id='op-b';";
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    // ---- Düşük-riskli: version mismatch → conflict (kör LWW yok) ----
    [Fact]
    public void Push_DusukRiskli_VersionUyusmazligi_Conflict()
    {
        var token = ActiveDeviceToken();
        var materials = new MaterialService(_factory, _clock);
        var mid = materials.Create(_admin, new NewMaterial("M-1", "Filtre")); // version=1

        // base_version 99 ≠ mevcut 1 → conflict
        var res = _server.Push(token, new[] { new SyncOperation("op-m", "material", mid, "{\"name\":\"y\"}", BaseVersion: 99) });
        Assert.Equal(SyncOpResult.Conflict, res[0].Result);
    }

    // ---- Pull cursor + sayfa rollback ----
    [Fact]
    public void Pull_Cursor_Ilerler_VeBozukSayfaIlerletmez()
    {
        var token = ActiveDeviceToken();
        _server.Push(token, new[] { new SyncOperation("p1", "material", "m1", "{}") });
        _server.Push(token, new[] { new SyncOperation("p2", "material", "m2", "{}") });

        var page = _server.Pull(token, afterSeq: 0);
        Assert.Equal(2, page.Items.Count);
        Assert.True(page.NextCursor > 0);

        // Bozuk server_change ekle → pull sayfa rollback, cursor ilerlemez
        InsertBrokenChange("A");
        Assert.Throws<InvalidOperationException>(() => _server.Pull(token, afterSeq: page.NextCursor));
    }

    // ---- Offline kalıcılık ----
    [Fact]
    public void Offline_Kalicilik_YenidenAcilis()
    {
        // Yerel write + outbox aynı transaction
        using (var conn = _factory.Create())
        using (var tx = conn.BeginTransaction())
        {
            OutboxWriter.Enqueue(conn, tx, "A", "op-offline", "material", "m1", "{\"name\":\"offline\"}", null, null,
                _clock.UtcNow.ToUnixTimeMilliseconds());
            tx.Commit();
        }
        // Yeni factory (uygulama yeniden açıldı) → veri kalıcı
        var reopened = new SqliteConnectionFactory(_dbPath);
        using var c2 = reopened.Create();
        Assert.Equal(1, OutboxWriter.PendingCount(c2, "A"));
    }

    [Fact]
    public void Outbox_YerelWriteIleAtomik_RollbackHicbiriniBirakmaz()
    {
        try
        {
            using var conn = _factory.Create();
            using var tx = conn.BeginTransaction();
            OutboxWriter.Enqueue(conn, tx, "A", "op-x", "material", "m1", "{}", null, null,
                _clock.UtcNow.ToUnixTimeMilliseconds());
            throw new Exception("simüle hata"); // commit yok → rollback
        }
        catch { /* beklenen */ }
        using var c = _factory.Create();
        Assert.Equal(0, OutboxWriter.PendingCount(c, "A"));
    }

    private static SyncOperation[] Ops(params (string Type, string Id)[] items)
        => items.Select(i => new SyncOperation(Guid.NewGuid().ToString("N"), i.Type, i.Id, "{}")).ToArray();

    private void InsertBrokenChange(string companyId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO server_changes(company_id, operation_id, entity_type, entity_id, payload_json, valid, created_at) " +
            "VALUES($c,'broken','material','mx','{}',0,$now);";
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$now", _clock.UtcNow.ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();
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
