using DepoWise.Application.Approvals;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Approvals;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Purchasing;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-33 (AR-GE 2026-10-18, 05 F-115 / F-092) — TEK ONAY MERKEZİ: KALEM ÖNİZLEME + TOPLU KARAR ═══
///  TO1 — Kalem önizleme: zincir adımının belgesi (talep / sipariş) kalemleriyle; sipariş satırında tutar
///  TO2 — Önizleme yalnız adımın sahibine; başka kullanıcı / başka firma göremez
///  TO3 — Toplu onay: her adım ayrı işlemde; geçersiz (başkasının / kapanmış) adım satır satır hata, diğerleri onaylanır
///  TO4 — Toplu ret: gerekçe zorunlu; tek gerekçe hepsine yazılır
///  TO5 — Zincirsiz talepler toplu onay/ret; zincirli talep tek adımlı yoldan GEÇEMEZ (bypass yok)
///  TO6 — Sınırlar: boş liste ve 200'den fazla kayıt reddedilir
///  TO7 — İki platform + API (kaynak kilidi)
/// </summary>
public class TekOnayMerkeziTests : IDisposable
{
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "dw_tom_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly UserHierarchyService _hier;
    private readonly ApprovalService _appr;
    private readonly RequestService _req;
    private readonly PurchaseOrderService _po;
    private readonly SessionContext _admin, _yabanciAdmin, _ust;
    private readonly string _ustId, _m;

    public TekOnayMerkeziTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        _hier = new UserHierarchyService(_f);
        _appr = new ApprovalService(_f);
        _req = new RequestService(_f, new StockService(_f)) { Approvals = _appr };
        _po = new PurchaseOrderService(_f) { Approvals = _appr };
        _appr.Register(ApprovalEntityTypes.MaterialRequest,
            (conn, tx, s, _, id, ok, reason, now) => _req.ApplyChainDecision(conn, tx, s, id, ok, reason, now));
        _appr.Register(ApprovalEntityTypes.PurchaseOrder, (_, _, _, _, _, _, _, _) => { });
        _admin = Firma("TO-A", "admina");
        _yabanciAdmin = Firma("TO-B", "adminb");
        _ustId = Kullanici("TO-A", "ust");
        _ust = new SessionContext(_ustId, "TO-A", new[] { RoleKeys.Staff }, new PermissionSet(new[]
        {
            new ModulePermission("request_approval", true, true, true, true),
            new ModulePermission("purchasing", true, true, true, true),
            new ModulePermission("requests", true, true, true, true),
        }));
        _m = new MaterialService(_f).Create(_admin, new NewMaterial("TO-1", "Kask"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _yol, _yol + "-wal", _yol + "-shm" }) try { File.Delete(p); } catch { }
    }

    private SessionContext Firma(string co, string user)
    {
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
            cmd.AddWithValue("@i", co);
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(_f).EnsureInitialAdmin(co, user, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(uid, co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private string Kullanici(string co, string username)
    {
        var id = Guid.NewGuid().ToString("N");
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO users(id,company_id,username,password_hash,is_active,created_at,updated_at,version,is_deleted) " +
                          "VALUES(@i,@c,@u,'x',1,1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.AddWithValue("@c", co);
        cmd.AddWithValue("@u", username);
        cmd.ExecuteNonQuery();
        return id;
    }

    private string Durum(string requestId)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM material_requests WHERE id=@i;";
        cmd.AddWithValue("@i", requestId);
        return (string)cmd.ExecuteScalar()!;
    }

    private string Talep(decimal miktar = 3m)
        => _req.Create(_admin, new NewRequest(new[] { new RequestItemInput(_m, miktar) }, SubmitImmediately: true)).Id;

    private PendingApprovalRow Adim(string entityId) => _appr.MyPending(_ust).Single(x => x.EntityId == entityId);

    [Fact]
    public void TO1_Kalem_Onizleme()
    {
        _hier.SetManager(_admin, _admin.UserId, _ustId);
        var t = Talep(3m);
        var k = Assert.Single(_appr.KalemOnizleme(_ust, Adim(t).StepId));
        Assert.Equal("Kask", k.MaterialName);
        Assert.Equal(3m, k.Quantity);

        var sp = _po.Create(_admin, new NewPurchaseOrder("", Lines: new[] { new NewPurchaseOrderLine(_m, 4m, 12.5m) }));
        var s = Assert.Single(_appr.KalemOnizleme(_ust, Adim(sp).StepId));
        Assert.Equal(50m, s.Tutar);
        Assert.Contains("= 50,00", s.Satir.Replace(".", ","));
    }

    [Fact]
    public void TO2_Onizleme_Yalniz_Adim_Sahibine()
    {
        _hier.SetManager(_admin, _admin.UserId, _ustId);
        var adim = Adim(Talep()).StepId;
        Assert.Throws<ForbiddenException>(() => _appr.KalemOnizleme(_admin, adim));          // başkasına atanmış
        Assert.Throws<ForbiddenException>(() => _appr.KalemOnizleme(_yabanciAdmin, adim));   // başka firma
    }

    [Fact]
    public void TO3_Toplu_Onay_Kismi_Basari()
    {
        _hier.SetManager(_admin, _admin.UserId, _ustId);
        var t1 = Talep(); var t2 = Talep();
        var a1 = Adim(t1).StepId; var a2 = Adim(t2).StepId;
        var sonuc = _appr.TopluOnayla(_ust, new[] { a1, a2, "olmayan-adim", a1 });
        Assert.Equal(3, sonuc.Count);   // tekrar eden kimlik bir kez işlenir
        Assert.True(sonuc.Single(x => x.StepId == a1).Basarili);
        Assert.True(sonuc.Single(x => x.StepId == a2).Basarili);
        Assert.False(sonuc.Single(x => x.StepId == "olmayan-adim").Basarili);
        Assert.Equal("approved", Durum(t1));
        Assert.Equal("approved", Durum(t2));
        var ikinci = Assert.Single(_appr.TopluOnayla(_ust, new[] { a1 }));   // kapanmış adım
        Assert.False(ikinci.Basarili);
        Assert.False(string.IsNullOrWhiteSpace(ikinci.Hata));
        Assert.False(Assert.Single(_appr.TopluOnayla(_admin, new[] { a2 })).Basarili);   // başkasının adımı
    }

    [Fact]
    public void TO4_Toplu_Ret_Gerekce()
    {
        _hier.SetManager(_admin, _admin.UserId, _ustId);
        var t1 = Talep(); var t2 = Talep();
        Assert.Throws<ArgumentException>(() => _appr.TopluReddet(_ust, new[] { Adim(t1).StepId }, " "));
        var sonuc = _appr.TopluReddet(_ust, new[] { Adim(t1).StepId, Adim(t2).StepId }, "bütçe yok");
        Assert.All(sonuc, x => Assert.True(x.Basarili));
        Assert.Equal("rejected", Durum(t1));
        Assert.Equal("rejected", Durum(t2));
    }

    [Fact]
    public void TO5_Zincirsiz_Talepler_Ve_Bypass_Yok()
    {
        var t1 = Talep(); var t2 = Talep();   // hiyerarşi yok → zincirsiz
        Assert.All(_req.TopluKarar(_admin, new[] { t1, t2 }, true, null), x => Assert.True(x.Basarili));
        Assert.Equal("approved", Durum(t1));
        Assert.Equal("approved", Durum(t2));
        Assert.Throws<ArgumentException>(() => _req.TopluKarar(_admin, new[] { Talep() }, false, null));   // ret gerekçesiz olmaz

        _hier.SetManager(_admin, _admin.UserId, _ustId);
        var zincirli = Talep();
        var r = Assert.Single(_req.TopluKarar(_admin, new[] { zincirli }, true, null));
        Assert.False(r.Basarili);
        Assert.Contains("zincir", r.Hata);
        Assert.Equal("pending", Durum(zincirli));
    }

    [Fact]
    public void TO6_Sinirlar()
    {
        Assert.Throws<ArgumentException>(() => _appr.TopluOnayla(_ust, Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => _appr.TopluOnayla(_ust, Enumerable.Range(0, 201).Select(i => "a" + i).ToArray()));
        Assert.Throws<ArgumentException>(() => _req.TopluKarar(_admin, Array.Empty<string>(), true, null));
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void TO7_Iki_Platform_Ve_Api()
    {
        var api = Oku("src/DepoWise.Api/Program.cs");
        Assert.Contains("app.MapGet(\"/api/approvals/steps/{stepId}/items\"", api);
        Assert.Contains("app.MapPost(\"/api/approvals/bulk\"", api);
        Assert.Contains("app.MapPost(\"/api/requests/bulk-decision\"", api);

        var istemci = Oku("src/DepoWise.Desktop/OnlineApprovalClient.cs");
        Assert.Contains("/api/approvals/bulk", istemci);
        Assert.Contains("/api/requests/bulk-decision", istemci);
        Assert.Contains("TopluOnaylaCommand", Oku("src/DepoWise.Desktop/Views/ApprovalsView.axaml"));
        Assert.Contains("PendingRequestsAsync()", Oku("src/DepoWise.Desktop/ViewModels/ApprovalsViewModel.cs"));

        var web = Oku("src/DepoWise.Web/Components/Pages/Approvals.razor");
        Assert.Contains("/api/approvals/bulk", web);
        Assert.Contains("/api/requests/bulk-decision", web);
        Assert.Contains("/items", web);
    }
}
