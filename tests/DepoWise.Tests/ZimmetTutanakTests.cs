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
/// ═══ M-47 (AR-GE 2026-10-19, 05 F-082) — KİŞİ BAZLI İMZALI ZİMMET TUTANAĞI ═══
///  ZT1 — Tutanak oluşturulur: numara ZT-, durum "bekliyor", kişinin zimmetleri DONDURULUR; zimmeti olmayana tutanak yok
///  ZT2 — Donmuş liste: sonradan iade/teslim tutanağı değiştirmez
///  ZT3 — Karar: kabul; ret gerekçe ister; karar bir kez verilir
///  ZT4 — PDF üretilir (geçerli PDF başlığı); firma ayrımı; eski şema
///  ZT5 — İki platform + API (kaynak kilidi)
/// </summary>
public class ZimmetTutanakTests : IDisposable
{
    private const string Co = "ZTT";
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_ztt_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly AssignmentService _z;
    private readonly ZimmetTutanakService _t;
    private readonly string _sube, _ali, _veli, _e1, _e2;

    public ZimmetTutanakTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','Örnek İnşaat',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "ztt", "Tutanak!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _sube = new BranchService(_f).Create(_s, new NewBranch("Depo"));
        var per = new DepoWise.Infrastructure.Org.PersonnelService(_f, new DepoWise.Infrastructure.Org.ScopeResolver(_f));
        _ali = per.Create(_s, new DepoWise.Infrastructure.Org.NewPersonnel("Ali Usta", null, null, _sube, true, false));
        _veli = per.Create(_s, new DepoWise.Infrastructure.Org.NewPersonnel("Veli", null, null, _sube, true, false));
        var ekp = new EquipmentService(_f);
        _e1 = ekp.Create(_s, new NewEquipment("MT-1", "Matkap"));
        _e2 = ekp.Create(_s, new NewEquipment("KS-1", "Kesici"));
        _z = new AssignmentService(_f);
        _t = new ZimmetTutanakService(_f, _z);
        _z.Issue(_s, "equipment", _e1, _ali, 1m, _sube, null, null, "z1");
        _z.Issue(_s, "equipment", _e2, _ali, 1m, _sube, null, null, "z2");
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

    [Fact]
    public void ZT1_Olustur_Ve_Dondur()
    {
        var t = _t.Olustur(_s, _ali);
        Assert.StartsWith("ZT-", t.No);
        Assert.Equal("bekliyor", t.Durum);
        Assert.Equal("Ali Usta", t.PersonelAdi);
        Assert.Equal(2, t.Kalemler.Count);
        Assert.Contains(t.Kalemler, k => k.Varlik.Contains("Matkap"));
        Assert.Equal(t.Id, Assert.Single(_t.Liste(_s, _ali)).Id);
        Assert.Contains("kalem yok", Assert.Throws<ArgumentException>(() => _t.Olustur(_s, _veli)).Message);
    }

    [Fact]
    public void ZT2_Donmus_Liste_Degismez()
    {
        var t = _t.Olustur(_s, _ali);
        _z.Return(_s, "equipment", _e1, _ali, 1m, _sube, null, null, "r1");
        Assert.Equal(2, _t.Liste(_s, _ali).Single(x => x.Id == t.Id).Kalemler.Count);   // tutanak iade sonrası da 2 kalem
        Assert.Single(_z.Holdings(_s, personnelId: _ali));                               // canlı zimmet 1
    }

    [Fact]
    public void ZT3_Karar()
    {
        var t = _t.Olustur(_s, _ali);
        Assert.Throws<ArgumentException>(() => _t.Karar(_s, t.Id, false, " "));
        _t.Karar(_s, t.Id, true, null);
        Assert.Equal("kabul", _t.Liste(_s).Single().Durum);
        Assert.Throws<InvalidOperationException>(() => _t.Karar(_s, t.Id, false, "geç kaldı"));   // bir kez
        var t2 = _t.Olustur(_s, _ali);
        _t.Karar(_s, t2.Id, false, "listede olmayan kalem var");
        Assert.Equal("Reddedildi", _t.Liste(_s).Single(x => x.Id == t2.Id).DurumMetni);
    }

    [Fact]
    public void ZT4_Pdf_Firma_Eski_Sema()
    {
        var t = _t.Olustur(_s, _ali);
        var pdf = _t.Pdf(_s, t.Id);
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));

        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('ZTB','ZTB',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin("ZTB", "ztb", "Tutanak!2026", RoleKeys.CompanyAdmin);
        var b = new SessionContext(uid, "ZTB", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Empty(_t.Liste(b));
        Assert.Throws<ForbiddenException>(() => _t.Pdf(b, t.Id));
        Assert.Throws<InvalidOperationException>(() => _t.Karar(b, t.Id, true, null));
        Assert.Equal("bekliyor", _t.Liste(_s).Single().Durum);

        Exec("DROP TABLE assignment_receipts;");
        Assert.Empty(_t.Liste(_s));
        Assert.Contains("desteklemiyor", Assert.Throws<InvalidOperationException>(() => _t.Olustur(_s, _ali)).Message);
        using (var c = _f.Create()) new Migration117_AssignmentReceipts().Up(c, null!);
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void ZT5_Iki_Platform_Ve_Api()
    {
        var api = Oku("src/DepoWise.Api/Program.cs");
        Assert.Contains("app.MapPost(\"/api/assignments/receipts\"", api);
        Assert.Contains("app.MapGet(\"/api/assignments/receipts/{id}/pdf\"", api);
        Assert.Contains("app.MapPost(\"/api/assignments/receipts/{id}/decision\"", api);
        Assert.Contains("DesktopServices.ZimmetTutanak.Olustur(", Oku("src/DepoWise.Desktop/ViewModels/PersonnelViewModel.cs"));
        Assert.Contains("if (value is not null) TutanaklariYukle(value.Record.Id);", Oku("src/DepoWise.Desktop/ViewModels/PersonnelViewModel.cs"));   // seçimde tutanaklar yüklenir
        Assert.Contains("TutanakOlusturCommand", Oku("src/DepoWise.Desktop/Views/PersonnelView.axaml"));
        Assert.Contains("/api/assignments/receipts", Oku("src/DepoWise.Web/Components/Pages/Personnel.razor"));
    }
}
