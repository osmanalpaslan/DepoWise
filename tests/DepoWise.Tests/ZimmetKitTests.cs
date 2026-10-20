using DepoWise.Application.Security;
using DepoWise.Infrastructure.Assignments;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Equipment;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-50 (AR-GE 2026-10-19, 05 F-083) — ZİMMET KİT (SET) ŞABLONU ═══
///  ZK1 — Kit kaydı ve listesi: etiketler güncel, aynı malzemenin satırları birleşir; kit hiçbir zimmet hareketi yazmaz
///  ZK2 — Kurallar toplu teslimle aynı: ad, kalem, ekipman miktarı 1 ve tekrar yok, kalem firmaya ait
///  ZK3 — Güncelle / sil; silinmiş kalem "mevcut değil" görünür; firma ayrımı; eski şema
///  ZK4 — Kitin kalemleriyle toplu teslim tek işlemde; iki platform + API (kaynak kilidi)
/// </summary>
public class ZimmetKitTests : IDisposable
{
    private const string Co = "ZKT";
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_zkt_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly ZimmetKitService _kit;
    private readonly string _sube, _ali, _matkap, _eldiven;

    public ZimmetKitTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "zkt", "KitSeti!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _sube = new BranchService(_f).Create(_s, new NewBranch("Depo"));
        var per = new DepoWise.Infrastructure.Org.PersonnelService(_f, new DepoWise.Infrastructure.Org.ScopeResolver(_f));
        _ali = per.Create(_s, new DepoWise.Infrastructure.Org.NewPersonnel("Ali", null, null, _sube, true, false));
        _matkap = new EquipmentService(_f).Create(_s, new NewEquipment("MT-1", "Matkap"));
        _eldiven = new MaterialService(_f).Create(_s, new NewMaterial("ELD-1", "Eldiven"));
        _kit = new ZimmetKitService(_f);
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

    private long Hareket()
    {
        using var c = _f.Create(); using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM assignment_movements WHERE company_id='{Co}';";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private AssignmentLine M(decimal q) => new("material", _eldiven, q);
    private AssignmentLine E() => new("equipment", _matkap, 1m);

    [Fact]
    public void ZK1_Kayit_Liste_Hareket_Yok()
    {
        var id = _kit.Kaydet(_s, null, " Yeni işçi seti ", new[] { M(2m), E(), M(1m) });
        var k = Assert.Single(_kit.Liste(_s));
        Assert.Equal(id, k.Id);
        Assert.Equal("Yeni işçi seti", k.Ad);
        Assert.Equal(2, k.Kalemler.Count);
        Assert.Equal(3m, k.Kalemler.Single(x => x.AssetType == "material").Quantity);   // satırlar birleşti
        Assert.Equal("MT-1 — Matkap", k.Kalemler.Single(x => x.AssetType == "equipment").Label);
        Assert.All(k.Kalemler, x => Assert.True(x.Mevcut));
        Assert.Equal(0L, Hareket());   // kit yalnız şablon
    }

    [Fact]
    public void ZK2_Kurallar()
    {
        Assert.Throws<ArgumentException>(() => _kit.Kaydet(_s, null, " ", new[] { E() }));
        Assert.Throws<ArgumentException>(() => _kit.Kaydet(_s, null, "Set", Array.Empty<AssignmentLine>()));
        Assert.Throws<ArgumentException>(() => _kit.Kaydet(_s, null, "Set", new[] { new AssignmentLine("equipment", _matkap, 2m) }));
        Assert.Throws<ArgumentException>(() => _kit.Kaydet(_s, null, "Set", new[] { E(), E() }));
        Assert.Throws<ArgumentException>(() => _kit.Kaydet(_s, null, "Set", new[] { M(0m) }));
        Assert.Throws<ArgumentException>(() => _kit.Kaydet(_s, null, "Set", new[] { new AssignmentLine("vehicle", _matkap, 1m) }));
        Assert.Throws<ArgumentException>(() => _kit.Kaydet(_s, null, "Set", new[] { new AssignmentLine("material", "yok-kayit", 1m) }));
        Assert.Throws<ArgumentException>(() => _kit.Kaydet(_s, null, new string('x', 101), new[] { E() }));
        var salt = new SessionContext(_s.UserId, Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission(AssignmentService.Module, true, false, false, false) }));
        Assert.Throws<ForbiddenException>(() => _kit.Kaydet(salt, null, "Set", new[] { E() }));
        Assert.Empty(_kit.Liste(_s));
    }

    [Fact]
    public void ZK3_Guncelle_Sil_Firma_Eski_Sema()
    {
        var id = _kit.Kaydet(_s, null, "Set A", new[] { E(), M(1m) });
        _kit.Kaydet(_s, id, "Set B", new[] { M(5m) });
        var k = Assert.Single(_kit.Liste(_s));
        Assert.Equal("Set B", k.Ad);
        Assert.Equal(5m, Assert.Single(k.Kalemler).Quantity);

        var id2 = _kit.Kaydet(_s, null, "Set C", new[] { E() });
        Exec($"UPDATE equipment SET is_deleted=1 WHERE id='{_matkap}';");
        Assert.False(_kit.Liste(_s).Single(x => x.Id == id2).Kalemler.Single().Mevcut);

        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('ZKB','ZKB',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin("ZKB", "zkb", "KitSeti!2026", RoleKeys.CompanyAdmin);
        var b = new SessionContext(uid, "ZKB", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Empty(_kit.Liste(b));
        var yabanci = new MaterialService(_f).Create(b, new NewMaterial("B-ELD", "Başka firma eldiveni"));
        Assert.Throws<ArgumentException>(() => _kit.Kaydet(_s, null, "Yabancı", new[] { new AssignmentLine("material", yabanci, 1m) }));   // başka firmanın kalemi
        Assert.Throws<ForbiddenException>(() => _kit.Sil(b, id));
        Assert.Throws<ForbiddenException>(() => _kit.Kaydet(b, id, "Ele geçir", new[] { new AssignmentLine("material", _eldiven, 1m) }));

        _kit.Sil(_s, id);
        Assert.DoesNotContain(_kit.Liste(_s), x => x.Id == id);
        Assert.Throws<ForbiddenException>(() => _kit.Sil(_s, id));

        Exec("DROP TABLE assignment_kits;");
        Assert.Empty(_kit.Liste(_s));
        Assert.Contains("desteklemiyor", Assert.Throws<InvalidOperationException>(() => _kit.Kaydet(_s, null, "Set", new[] { M(1m) })).Message);
        using (var c = _f.Create()) new Migration119_AssignmentKits().Up(c, null!);
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void ZK4_Kitle_Teslim_Ve_Kaynak()
    {
        _kit.Kaydet(_s, null, "Set", new[] { E(), M(3m) });
        var k = Assert.Single(_kit.Liste(_s));
        new StockService(_f).ReceiveIn(_s, new[] { new StockLine(_eldiven, 10m) }, "stok-1", branchId: _sube);
        var z = new AssignmentService(_f);
        z.IssueMany(_s, _ali, k.Kalemler.Where(x => x.Mevcut).Select(x => new AssignmentLine(x.AssetType, x.AssetId, x.Quantity)).ToList(),
            _sube, null, null, "kit-op1");
        var h = z.Holdings(_s, personnelId: _ali);
        Assert.Equal(2, h.Count);
        Assert.Equal(3m, h.Single(x => x.AssetType == "material").Quantity);

        var api = Oku("src/DepoWise.Api/Program.cs");
        Assert.Contains("app.MapGet(\"/api/assignments/kits\"", api);
        Assert.Contains("app.MapPost(\"/api/assignments/kits\"", api);
        Assert.Contains("app.MapDelete(\"/api/assignments/kits/{id}\"", api);
        Assert.Contains("DesktopServices.ZimmetKit.Kaydet(", Oku("src/DepoWise.Desktop/ViewModels/AssignmentsViewModel.cs"));
        Assert.Contains("KitYukleCommand", Oku("src/DepoWise.Desktop/Views/AssignmentsView.axaml"));
        Assert.Contains("/api/assignments/kits", Oku("src/DepoWise.Web/Components/Pages/Assignments.razor"));
    }
}
