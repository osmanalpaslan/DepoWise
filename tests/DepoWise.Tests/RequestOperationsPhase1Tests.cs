using DepoWise.Application.Common;
using DepoWise.Application.Requests;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// TALEP OPERASYONLARI — FAZ 1 (kullanıcı isteği 2026-08-08, Migration060).
/// Kapsam: (a) operasyon durumu ONAY durumundan AYRI ve yalnız ONAY ile başlar (kullanıcı kararı "B"),
/// (b) ONAY VEREN kısıtı (yalnız formda seçilen kişi; admin/süper admin istisna),
/// (c) öncelik varsayılanı Normal, (d) mevcut onay akışının BOZULMADIĞI (regresyon).
/// Faz 1'de operasyon durumu DEĞİŞTİRME/geçiş kuralları YOKTUR — Faz 2'de gelecek.
/// </summary>
public class RequestOperationsPhase1Tests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly RequestService _requests;
    private readonly MaterialService _materials;
    private readonly UserService _users;
    private readonly SessionContext _admin;
    private readonly string _material;

    public RequestOperationsPhase1Tests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_reqops_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _requests = new RequestService(_factory, new StockService(_factory, _clock), _clock);
        _users = new UserService(_factory, _clock);
        var uid = _users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _material = _materials.Create(_admin, new NewMaterial("M-1", "Filtre"));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private void Exec(string sql, params (string, object?)[] ps)
    {
        using var c = _factory.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private string? OperationStatusDb(string requestId)
    {
        using var c = _factory.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT operation_status FROM material_requests WHERE id=@id;";
        cmd.AddWithValue("@id", requestId);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : (string)v;
    }

    private string PriorityDb(string requestId)
    {
        using var c = _factory.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT priority FROM material_requests WHERE id=@id;";
        cmd.AddWithValue("@id", requestId);
        return (string)cmd.ExecuteScalar()!;
    }

    private string NewPendingRequest(string? approverPersonnelId = null, RequestPriority priority = RequestPriority.Normal)
    {
        var h = _requests.Create(_admin, new NewRequest(
            new[] { new RequestItemInput(_material, 5m) },
            ApproverId: approverPersonnelId, SubmitImmediately: true, Priority: priority));
        return h.Id;
    }

    /// <summary>Personel + ona BAĞLI kullanıcı hesabı (users.personnel_id) oluşturur; oturumu döner.</summary>
    private (string PersonnelId, SessionContext Session) CreateLinkedUser(string username, string fullName)
    {
        var personnelId = Guid.NewGuid().ToString("N");
        Exec("INSERT INTO personnel(id,company_id,full_name,created_at,updated_at) VALUES(@id,'A',@n,1,1);",
            ("@id", personnelId), ("@n", fullName));
        // Kullanıcı hesabı personele BAĞLI oluşturulur (users.personnel_id — Migration033).
        var userId = _users.CreateUser(_admin, new NewUser(
            Username: username, Password: "pass12345", FullName: fullName,
            RoleKeys: new[] { DepoWise.Application.Security.RoleKeys.Staff },
            CompanyId: "A", PersonnelId: personnelId));
        // Talep Onaylama yetkisi (deny-by-default): onay için gerekli.
        var set = new PermissionSet(new[] { new ModulePermission("request_approval", true, false, true, false) }, Array.Empty<string>());
        return (personnelId, new SessionContext(userId, "A", new[] { DepoWise.Application.Security.RoleKeys.Staff }, set));
    }

    // ── (a) Operasyon durumu: onaydan ÖNCE yok, onayla birlikte Beklemede ──
    [Fact]
    public void YeniTalep_OperasyonDurumu_YOK()
    {
        var id = NewPendingRequest();
        Assert.Null(OperationStatusDb(id));   // onaylanana kadar boş (kullanıcı kararı B)
        var row = _requests.List(_admin).First(r => r.Id == id);
        Assert.Equal("—", row.OperationStatusText);
    }

    [Fact]
    public void Onaylandiginda_OperasyonDurumu_BeklemedeOlur()
    {
        var id = NewPendingRequest();
        _requests.Approve(_admin, id);

        Assert.Equal("pending_ops", OperationStatusDb(id));
        var row = _requests.List(_admin).First(r => r.Id == id);
        Assert.Equal("Beklemede", row.OperationStatusText);
        Assert.Equal(RequestStatus.Approved, row.Status);   // ONAY durumu ayrı ve doğru
    }

    [Fact]
    public void Reddedilen_TalepteOperasyonDurumu_OLUSMAZ()
    {
        var id = NewPendingRequest();
        _requests.Reject(_admin, id, "uygun değil");
        Assert.Null(OperationStatusDb(id));
    }

    [Fact]
    public void IptalEdilen_TalepteOperasyonDurumu_OLUSMAZ()
    {
        var id = NewPendingRequest();
        _requests.Cancel(_admin, id, "vazgeçildi");
        Assert.Null(OperationStatusDb(id));
    }

    // ── (b) ONAY VEREN kısıtı ──
    [Fact]
    public void OnayVeren_SecilmisSe_YalnizOKisi_Onaylayabilir()
    {
        var (approverPersonnel, approverSession) = CreateLinkedUser("onaycı", "Onay Veren");
        var (_, otherSession) = CreateLinkedUser("baskasi", "Başka Kişi");
        var id = NewPendingRequest(approverPersonnel);

        // Yetkisi olan AMA onay veren OLMAYAN kullanıcı → reddedilir
        Assert.Throws<ForbiddenException>(() => _requests.Approve(otherSession, id));
        Assert.Null(OperationStatusDb(id));

        // Onay veren kullanıcı → onaylayabilir
        _requests.Approve(approverSession, id);
        Assert.Equal("pending_ops", OperationStatusDb(id));
    }

    [Fact]
    public void OnayVeren_Secilmisse_AdminIstisnasi_Gecerli()
    {
        var (approverPersonnel, _) = CreateLinkedUser("onaycı2", "Onay Veren 2");
        var id = NewPendingRequest(approverPersonnel);

        _requests.Approve(_admin, id);   // firma admini istisna (kullanıcı kararı)
        Assert.Equal(RequestStatus.Approved, _requests.GetStatus(_admin, id));
    }

    [Fact]
    public void OnayVeren_SECILMEMISSE_EskiDavranis_YetkiliOnaylar()
    {
        var (_, otherSession) = CreateLinkedUser("yetkili", "Yetkili Kişi");
        var id = NewPendingRequest(approverPersonnelId: null);   // onay veren yok

        _requests.Approve(otherSession, id);   // geriye uyumluluk: kilitlenmez
        Assert.Equal(RequestStatus.Approved, _requests.GetStatus(_admin, id));
    }

    [Fact]
    public void OnayVeren_Kisiti_RettedeGecerli()
    {
        var (approverPersonnel, _) = CreateLinkedUser("onaycı3", "Onay Veren 3");
        var (_, otherSession) = CreateLinkedUser("baskasi3", "Başka 3");
        var id = NewPendingRequest(approverPersonnel);

        Assert.Throws<ForbiddenException>(() => _requests.Reject(otherSession, id, "olmaz"));
        Assert.Equal(RequestStatus.Pending, _requests.GetStatus(_admin, id));   // durum değişmedi
    }

    // ── (c) Öncelik ──
    [Fact]
    public void Oncelik_Varsayilan_Normal()
    {
        var id = NewPendingRequest();
        Assert.Equal("normal", PriorityDb(id));
        Assert.Equal("Normal", _requests.List(_admin).First(r => r.Id == id).PriorityText);
    }

    [Theory]
    [InlineData(RequestPriority.High, "high", "Yüksek")]
    [InlineData(RequestPriority.Urgent, "urgent", "Acil")]
    [InlineData(RequestPriority.Critical, "critical", "Kritik")]
    public void Oncelik_Secilebilir(RequestPriority p, string db, string label)
    {
        var id = NewPendingRequest(priority: p);
        Assert.Equal(db, PriorityDb(id));
        Assert.Equal(label, _requests.List(_admin).First(r => r.Id == id).PriorityText);
    }

    // ── (d) Regresyon: mevcut onay akışı ve durum makinesi korunuyor ──
    [Fact]
    public void Regresyon_OnayAkisi_VeCiftOnayEngeli_Korunuyor()
    {
        var id = NewPendingRequest();
        _requests.Approve(_admin, id);
        Assert.Equal(RequestStatus.Approved, _requests.GetStatus(_admin, id));
        // Onaylı terminal: ikinci onay/ret engellenir (durum makinesi korunuyor)
        Assert.Throws<InvalidOperationException>(() => _requests.Approve(_admin, id));
        Assert.Throws<InvalidOperationException>(() => _requests.Reject(_admin, id, "x"));
    }

    [Fact]
    public void Regresyon_Gecmis_Yaziliyor_VeSilinmiyor()
    {
        var id = NewPendingRequest();
        _requests.Approve(_admin, id);
        var history = _requests.GetHistory(id);
        Assert.True(history.Count >= 2);                       // oluşturma + onay
        Assert.Contains(history, h => h.To == RequestStatus.Approved);
    }

    [Fact]
    public void Gecmis_Kaydi_OnayTuruyle_Etiketleniyor()
    {
        var id = NewPendingRequest();
        _requests.Approve(_admin, id);
        using var c = _factory.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM request_status_history WHERE request_id=@r AND kind='approval';";
        cmd.AddWithValue("@r", id);
        Assert.True(Convert.ToInt32(cmd.ExecuteScalar()) >= 2);   // Faz 2'de 'operation' türü eklenecek
    }

    // ── Durum/renk ortak kaynağı (iki platform aynı) ──
    [Fact]
    public void OperasyonDurumlari_13Adet_SirasiSartnameyleAyni()
    {
        Assert.Equal(13, RequestOperationStatusInfo.All.Count);
        Assert.Equal(RequestOperationStatus.PendingOps, RequestOperationStatusInfo.All[0]);
        Assert.Equal(RequestOperationStatus.CancelledOps, RequestOperationStatusInfo.All[^1]);
        Assert.Equal("Depodan Karşılanacak", RequestOperationStatusInfo.Label(RequestOperationStatus.FromWarehouse));
        Assert.Equal("Kısmen Karşılandı", RequestOperationStatusInfo.Label(RequestOperationStatus.PartiallyFulfilled));
        // Bilinmeyen/boş → "—" (onaylanmamış talep)
        Assert.Equal("—", RequestOperationStatusInfo.LabelOrDash(null));
        Assert.Equal("—", RequestOperationStatusInfo.LabelOrDash("bilinmeyen"));
    }

    [Fact]
    public void DurumRenkleri_TumDurumlarIcin_Tanimli()
    {
        foreach (var s in RequestOperationStatusInfo.All)
        {
            var color = RequestOperationStatusInfo.Color(s);
            Assert.Contains(color, new[] { "neutral", "info", "warning", "primary", "success", "danger" });
            Assert.False(string.IsNullOrWhiteSpace(RequestOperationStatusInfo.Label(s)));
        }
    }

    [Fact]
    public void YeniYetkiler_YetkiAgacinda_Tanimli()
    {
        var keys = AppModules.All.Select(m => m.Key).ToList();
        Assert.Contains("request_ops", keys);
        Assert.Contains("request_ops_warehouse", keys);
        Assert.Contains("request_ops_purchase", keys);
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + ext); } catch { }
        }
    }
}
