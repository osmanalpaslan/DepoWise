using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Equipment;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-48 (AR-GE 2026-10-19, 05 F-058) — EKİPMAN TAŞIMA HAREKETİ + KONUM GEÇMİŞİ ═══
///  ET1 — Taşıma: kartın şube/konumu güncellenir, geçmiş satırı yazılır (kimden → kime, kullanıcı, not)
///  ET2 — Tekrar gönderim zararsız; aynı yere taşıma reddedilir; hedef şube firmaya ait olmalı
///  ET3 — Firma ayrımı; şube kapsamı dışındaki hedefe taşınamaz; eski şema anlaşılır hata
///  ET4 — İki platform + API (kaynak kilidi)
/// </summary>
public class EkipmanTasimaTests : IDisposable
{
    private const string Co = "EKT";
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_ekt_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly EkipmanTasimaService _t;
    private readonly string _a, _b, _e;

    public EkipmanTasimaTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "ekt", "Tasima!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var sb = new BranchService(_f);
        _a = sb.Create(_s, new NewBranch("Merkez"));
        _b = sb.Create(_s, new NewBranch("Şantiye 2"));
        _e = new EquipmentService(_f).Create(_s, new NewEquipment("JN-1", "Jeneratör", BranchId: _a, Location: "Depo rafı"));
        _t = new EkipmanTasimaService(_f);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _yol, _yol + "-wal", _yol + "-shm" }) try { File.Delete(p); } catch { }
    }

    private void Exec(string sql)
    {
        using var c = _f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
    }

    private object? Tek(string sql)
    {
        using var c = _f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar();
    }

    [Fact]
    public void ET1_Tasima_Ve_Gecmis()
    {
        _t.Tasi(_s, _e, _b, "Konteyner", null, "kamyonla", "op1");
        Assert.Equal(_b, Tek($"SELECT branch_id FROM equipment WHERE id='{_e}';"));
        Assert.Equal("Konteyner", Tek($"SELECT location FROM equipment WHERE id='{_e}';"));
        var g = Assert.Single(_t.Gecmis(_s, _e));
        Assert.Equal("Merkez", g.KaynakAdi);
        Assert.Equal("Şantiye 2", g.HedefAdi);
        Assert.Equal("Depo rafı", g.KaynakKonum);
        Assert.Contains("kamyonla", g.Satir);
        Assert.Equal("ekt", g.Kullanici);
        _t.Tasi(_s, _e, _a, null, null, null, "op2");
        Assert.Equal(2, _t.Gecmis(_s, _e).Count);
    }

    [Fact]
    public void ET2_Tekrar_Ayni_Yer_Yabanci_Sube()
    {
        _t.Tasi(_s, _e, _b, null, null, null, "op1");
        _t.Tasi(_s, _e, _b, null, null, null, "op1");   // tekrar gönderim
        Assert.Single(_t.Gecmis(_s, _e));
        Assert.Contains("zaten", Assert.Throws<ArgumentException>(() => _t.Tasi(_s, _e, _b, null, null, null, "op2")).Message);
        Assert.Throws<ArgumentException>(() => _t.Tasi(_s, _e, "yok-sube", null, null, null, "op3"));
        Assert.Throws<ArgumentException>(() => _t.Tasi(_s, _e, _a, null, null, null, " "));
    }

    [Fact]
    public void ET3_Firma_Kapsam_Eski_Sema()
    {
        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('EKB','EKB',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin("EKB", "ekb", "Tasima!2026", RoleKeys.CompanyAdmin);
        var b = new SessionContext(uid, "EKB", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _t.Tasi(b, _e, null, "x", null, null, "opx"));
        Assert.Empty(_t.Gecmis(b, _e));

        // Yalnız Merkez kapsamındaki kullanıcı Şantiye 2'ye taşıyamaz
        var kisitli = new SessionContext(_s.UserId, Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission(EquipmentService.Module, true, true, true, false) })) { ScopeBranchIds = new[] { _a } };
        Assert.Throws<ForbiddenException>(() => _t.Tasi(kisitli, _e, _b, null, null, null, "opk"));
        Assert.Equal(_a, Tek($"SELECT branch_id FROM equipment WHERE id='{_e}';"));

        Exec("DROP TABLE equipment_moves;");
        Assert.Empty(_t.Gecmis(_s, _e));
        Assert.Contains("desteklemiyor", Assert.Throws<InvalidOperationException>(() => _t.Tasi(_s, _e, _b, null, null, null, "op9")).Message);
        Assert.Equal(_a, Tek($"SELECT branch_id FROM equipment WHERE id='{_e}';"));   // yarım taşıma yok
        using (var c = _f.Create()) new Migration118_EquipmentMoves().Up(c, null!);
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void ET4_Iki_Platform_Ve_Api()
    {
        var api = Oku("src/DepoWise.Api/Program.cs");
        Assert.Contains("app.MapPost(\"/api/equipment/{id}/move\"", api);
        Assert.Contains("app.MapGet(\"/api/equipment/{id}/moves\"", api);
        Assert.Contains("DesktopServices.EkipmanTasima.Tasi(", Oku("src/DepoWise.Desktop/ViewModels/EquipmentViewModel.cs"));
        Assert.Contains("TasiCommand", Oku("src/DepoWise.Desktop/Views/EquipmentView.axaml"));
        Assert.Contains("/move\", new { branchId = _tasimaSube", Oku("src/DepoWise.Web/Components/Pages/Equipment.razor"));
    }
}
