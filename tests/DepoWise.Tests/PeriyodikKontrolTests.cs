using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-41 (AR-GE 2026-10-19, 05 F-116) — YASAL PERİYODİK KONTROL ═══
///  PK1 — Ortak belge türü listesi: etiket/kod/doğrulama; periyodik tür dahil; bilinmeyen kod ham döner
///  PK2 — Araç: periyodik kontrol yetkili kişi + sonraki tarih zorunlu; kaydedilir, listede "Periyodik Kontrol"
///  PK3 — Ekipman: aynı kurallar
///  PK4 — Eski şema: alan doluysa anlaşılır hata, boşsa (diğer türler) eskisi gibi
///  PK5 — İki platform + API (kaynak kilidi)
/// </summary>
public class PeriyodikKontrolTests : IDisposable
{
    private const string Co = "PKT";
    private const long G = 86_400_000L;
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_pkt_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly InspectionService _muayene;
    private readonly string _arac;

    public PeriyodikKontrolTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "pkt", "Kontrol!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _arac = new VehicleService(_f).Create(_s, new NewVehicle("VNC-1", "06 VN 1"));
        _muayene = new InspectionService(_f);
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

    private long Simdi => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [Fact]
    public void PK1_Ortak_Liste()
    {
        Assert.Equal(5, InspectionDocTypes.All.Count);
        Assert.Equal("Periyodik Kontrol", InspectionDocTypes.Label("periodic"));
        Assert.Equal("Kalibrasyon", InspectionDocTypes.Label("calibration"));
        Assert.Equal("eski-tur", InspectionDocTypes.Label("eski-tur"));
        Assert.True(InspectionDocTypes.IsValid("periodic"));
        Assert.False(InspectionDocTypes.IsValid("yok"));
        Assert.Equal("periodic", InspectionDocTypes.FromLabel("Periyodik Kontrol"));
        Assert.Equal("periodic", InspectionDocTypes.FromLabel("periyodik"));
        Assert.Equal("insurance", InspectionDocTypes.FromLabel("Sigorta"));
        Assert.Equal("inspection", InspectionDocTypes.FromLabel("bilinmiyor"));
    }

    [Fact]
    public void PK2_Arac_Periyodik_Kontrol()
    {
        Assert.Contains("yetkili", Assert.Throws<ArgumentException>(() =>
            _muayene.Save(_s, new NewInspection(_arac, "periodic", Simdi, Simdi + 365 * G))).Message);
        Assert.Contains("sonraki", Assert.Throws<ArgumentException>(() =>
            _muayene.Save(_s, new NewInspection(_arac, "periodic", Simdi, null, Inspector: "Ali Usta"))).Message);
        var id = _muayene.Save(_s, new NewInspection(_arac, "periodic", Simdi, Simdi + 365 * G, Inspector: " XYZ Muayene Ltd. ", ReportNo: "PK-2026-77"));
        Assert.Equal("XYZ Muayene Ltd.", Tek($"SELECT inspector FROM vehicle_inspections WHERE id='{id}';"));
        Assert.Equal("PK-2026-77", Tek($"SELECT report_no FROM vehicle_inspections WHERE id='{id}';"));
        Assert.Equal("Periyodik Kontrol", _muayene.List(_s).Single(x => x.Id == id).DocTypeText);
        _muayene.Save(_s, new NewInspection(_arac, "insurance", Simdi, Simdi + 30 * G));   // diğer türlerde yetkili zorunlu değil
        Assert.Throws<ArgumentException>(() => _muayene.Save(_s, new NewInspection(_arac, "yok", Simdi, Simdi)));
    }

    [Fact]
    public void PK3_Ekipman_Periyodik_Kontrol()
    {
        var ekipman = new DepoWise.Infrastructure.Equipment.EquipmentService(_f).Create(_s,
            new DepoWise.Infrastructure.Equipment.NewEquipment("FRK-1", "Forklift"));
        var svc = new EquipmentInspectionService(_f);
        Assert.Throws<ArgumentException>(() => svc.Save(_s, new NewEquipmentInspection(ekipman, "periodic", Simdi, Simdi + 90 * G)));
        var id = svc.Save(_s, new NewEquipmentInspection(ekipman, "periodic", Simdi, Simdi + 90 * G, Inspector: "Yetkili Mühendis", ReportNo: "R-1"));
        Assert.Equal("Yetkili Mühendis", Tek($"SELECT inspector FROM equipment_inspections WHERE id='{id}';"));
    }

    [Fact]
    public void PK4_Eski_Sema()
    {
        using (var c = _f.Create()) new Migration115_PeriodicControl().Up(c, null!);   // tekrar çalışabilir
        Exec("ALTER TABLE vehicle_inspections DROP COLUMN report_no;");
        Exec("ALTER TABLE vehicle_inspections DROP COLUMN inspector;");
        Assert.Contains("desteklemiyor", Assert.Throws<InvalidOperationException>(() =>
            _muayene.Save(_s, new NewInspection(_arac, "periodic", Simdi, Simdi + G, Inspector: "X"))).Message);
        Assert.Equal(0L, Convert.ToInt64(Tek("SELECT COUNT(*) FROM vehicle_inspections;")));   // yarım kayıt kalmadı
        _muayene.Save(_s, new NewInspection(_arac, "inspection", Simdi, Simdi + G));          // eski türler eskisi gibi
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void PK5_Iki_Platform_Ve_Api()
    {
        Assert.Contains("Doc(d.Inspector), Doc(d.ReportNo)", Oku("src/DepoWise.Api/Program.cs"));
        Assert.Contains("Inspector: IsPeriodicType ? FInspector : null", Oku("src/DepoWise.Desktop/ViewModels/InspectionViewModel.cs"));
        Assert.Contains("{Binding FInspector}", Oku("src/DepoWise.Desktop/Views/InspectionView.axaml"));
        var web = Oku("src/DepoWise.Web/Components/Pages/Inspection.razor");
        Assert.Contains("Value=\"@(\"periodic\")\"", web);
        Assert.Contains("inspector = _docType == \"periodic\"", web);
    }
}
