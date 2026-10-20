using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Assignments;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Equipment;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-46 (AR-GE 2026-10-19, 05 F-081) — ZİMMET BEKLENEN İADE · GECİKME · PERSONEL KARTI ═══
///  ZI1 — İade tarihi verilir/kaldırılır; defter satırı DEĞİŞMEZ; işlem geçmişine yazılır
///  ZI2 — Tarih geçince gecikme günü ve "yalnız gecikenler" listesi; geçmemiş tarih gecikme değil
///  ZI3 — İade edilen varlık listeden düşer (beklenti anlamsızlaşır); kişide olmayan varlığa tarih verilemez
///  ZI4 — Personel kartı: yalnız o kişinin zimmetleri; firma ayrımı; eski şema
///  ZI5 — İki platform + API (kaynak kilidi)
/// </summary>
public class ZimmetIadeTests : IDisposable
{
    private const string Co = "ZIA";
    private const long G = 86_400_000L;
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_zia_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly TestClock _saat = new();
    private readonly AssignmentService _z;
    private readonly string _sube, _ali, _veli, _e1, _e2;

    private sealed class TestClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_760_000_000_000); }

    public ZimmetIadeTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "zia", "Zimmet!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _sube = new BranchService(_f).Create(_s, new NewBranch("Depo"));
        var per = new DepoWise.Infrastructure.Org.PersonnelService(_f, new DepoWise.Infrastructure.Org.ScopeResolver(_f));
        _ali = per.Create(_s, new DepoWise.Infrastructure.Org.NewPersonnel("Ali", null, null, _sube, true, false));
        _veli = per.Create(_s, new DepoWise.Infrastructure.Org.NewPersonnel("Veli", null, null, _sube, true, false));
        var ekp = new EquipmentService(_f);
        _e1 = ekp.Create(_s, new NewEquipment("MT-1", "Matkap"));
        _e2 = ekp.Create(_s, new NewEquipment("KS-1", "Kesici"));
        _z = new AssignmentService(_f, _saat);
        _z.Issue(_s, "equipment", _e1, _ali, 1m, _sube, null, null, "z1");
        _z.Issue(_s, "equipment", _e2, _veli, 1m, _sube, null, null, "z2");
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

    private long Bugun { get { var n = _saat.UtcNow.ToUnixTimeMilliseconds(); return n - n % G; } }

    [Fact]
    public void ZI1_Tarih_Ver_Kaldir_Defter_Degismez()
    {
        var defter = Tek("SELECT group_concat(id || ':' || quantity, ',') FROM assignment_movements;");
        _z.IadeTarihiAyarla(_s, _ali, "equipment", _e1, Bugun + 7 * G);
        Assert.Equal(Bugun + 7 * G, _z.IadeDurumu(_s, _ali).Single().BeklenenIade);
        Assert.Equal(defter, Tek("SELECT group_concat(id || ':' || quantity, ',') FROM assignment_movements;"));   // defter dokunulmadı
        Assert.Contains("beklenenIade", (string)Tek("SELECT after_json FROM audit_logs WHERE entity_type='assignment_due' ORDER BY created_at DESC, rowid DESC LIMIT 1;")!);
        _z.IadeTarihiAyarla(_s, _ali, "equipment", _e1, null);
        Assert.Null(_z.IadeDurumu(_s, _ali).Single().BeklenenIade);
    }

    [Fact]
    public void ZI2_Gecikme()
    {
        _z.IadeTarihiAyarla(_s, _ali, "equipment", _e1, Bugun - 3 * G);
        _z.IadeTarihiAyarla(_s, _veli, "equipment", _e2, Bugun + 3 * G);
        var g = Assert.Single(_z.IadeDurumu(_s, yalnizGeciken: true));
        Assert.Equal(_ali, g.Zimmet.PersonnelId);
        Assert.Equal(3, g.GecikmeGun);
        Assert.Contains("3 gün gecikti", g.Ozet);
        Assert.False(_z.IadeDurumu(_s, _veli).Single().Gecikti);
    }

    [Fact]
    public void ZI3_Iade_Edilen_Duser_Kiside_Olmayan_Reddedilir()
    {
        _z.IadeTarihiAyarla(_s, _ali, "equipment", _e1, Bugun - 3 * G);
        _z.Return(_s, "equipment", _e1, _ali, 1m, _sube, null, null, "r1");
        Assert.Empty(_z.IadeDurumu(_s, yalnizGeciken: true));   // iade edildi → gecikme yok
        Assert.Throws<ArgumentException>(() => _z.IadeTarihiAyarla(_s, _ali, "equipment", _e2, Bugun));   // Veli'de
        Assert.Throws<ArgumentException>(() => _z.IadeTarihiAyarla(_s, _ali, "arac", _e1, Bugun));
    }

    [Fact]
    public void ZI4_Personel_Karti_Firma_Eski_Sema()
    {
        var ali = Assert.Single(_z.IadeDurumu(_s, _ali));
        Assert.Equal("Matkap", ali.Zimmet.AssetLabel);   // zimmet satırı ekipmanı adıyla gösterir (mevcut HoldingRow)
        Assert.Equal(_e1, ali.Zimmet.AssetId);

        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('ZIB','ZIB',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin("ZIB", "zib", "Zimmet!2026", RoleKeys.CompanyAdmin);
        var b = new SessionContext(uid, "ZIB", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Empty(_z.IadeDurumu(b));
        Assert.Throws<ArgumentException>(() => _z.IadeTarihiAyarla(b, _ali, "equipment", _e1, Bugun));

        Exec("DROP TABLE assignment_due_dates;");
        Assert.Single(_z.IadeDurumu(_s, _ali));   // eski şemada liste çalışır (beklenti yok)
        Assert.Contains("desteklemiyor", Assert.Throws<InvalidOperationException>(() =>
            _z.IadeTarihiAyarla(_s, _ali, "equipment", _e1, Bugun)).Message);
        using (var c = _f.Create()) new Migration116_AssignmentDueDates().Up(c, null!);
        _z.IadeTarihiAyarla(_s, _ali, "equipment", _e1, Bugun);
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void ZI5_Iki_Platform_Ve_Api()
    {
        var api = Oku("src/DepoWise.Api/Program.cs");
        Assert.Contains("app.MapGet(\"/api/assignments/returns\"", api);
        Assert.Contains("app.MapPut(\"/api/assignments/due-date\"", api);
        Assert.Contains("DesktopServices.Assignments.IadeTarihiAyarla(", Oku("src/DepoWise.Desktop/ViewModels/AssignmentsViewModel.cs"));
        Assert.Contains("DesktopServices.Assignments.IadeDurumu(_session, value.Record.Id)", Oku("src/DepoWise.Desktop/ViewModels/PersonnelViewModel.cs"));
        Assert.Contains("/api/assignments/due-date", Oku("src/DepoWise.Web/Components/Pages/Assignments.razor"));
        Assert.Contains("/api/assignments/returns?personnelId=", Oku("src/DepoWise.Web/Components/Pages/Personnel.razor"));
    }
}
