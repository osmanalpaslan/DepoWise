using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Equipment;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-49 (AR-GE 2026-10-19, 05 F-139 / F-026) — FORMDA QR / BARKOD OKUTARAK KAYIT SEÇME ═══
///  KO1 — Araç iç kodu / plaka (boşluksuz), ekipman ve malzeme kodu; büyük-küçük harf ve okuyucunun satır sonu önemsiz
///  KO2 — Bilinmeyen ya da KISMİ kod seçilmez; boş kod ve geçersiz tür reddedilir
///  KO3 — Firma ayrımı ve modül görme yetkisi
///  KO4 — Belirsizlik: iç kod plakaya üstün; iki eşit eşleşmede hiçbiri seçilmez
///  KO5 — İki platform + API (kaynak kilidi)
/// </summary>
public class KodOkutmaTests : IDisposable
{
    private const string Co = "KOK";
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_kok_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly KodOkutmaService _k;
    private readonly string _arac, _ekp, _mlz;

    public KodOkutmaTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "kok", "Okutma!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _arac = new VehicleService(_f).Create(_s, new NewVehicle("EKS-07", "06 KO 7", CurrentMeter: 10m));
        _ekp = new EquipmentService(_f).Create(_s, new NewEquipment("JN-12", "Jeneratör"));
        _mlz = new MaterialService(_f).Create(_s, new NewMaterial("ELD-3", "Eldiven"));
        _k = new KodOkutmaService(_f);
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
    public void KO1_Kod_Plaka_Ekipman_Malzeme()
    {
        Assert.Equal(_arac, _k.Bul(_s, KodOkutmaService.Arac, "eks-07\r\n")!.Id);
        Assert.Equal(_arac, _k.Bul(_s, KodOkutmaService.Arac, "06KO7")!.Id);
        Assert.Equal(_arac, _k.Bul(_s, KodOkutmaService.Arac, " 06 ko 7 ")!.Id);
        var e = _k.Bul(_s, KodOkutmaService.Ekipman, "JN-12")!;
        Assert.Equal(_ekp, e.Id);
        Assert.Equal("Jeneratör", e.Ad);
        Assert.Equal(_mlz, _k.Bul(_s, KodOkutmaService.Malzeme, "eld-3\t")!.Id);
    }

    [Fact]
    public void KO2_Bilinmeyen_Kismi_Bos()
    {
        Assert.Null(_k.Bul(_s, KodOkutmaService.Arac, "EKS"));          // kısmi → seçilmez
        Assert.Null(_k.Bul(_s, KodOkutmaService.Arac, "EKS-077"));
        Assert.Null(_k.Bul(_s, KodOkutmaService.Ekipman, "JN"));        // kısmi ekipman kodu → seçilmez
        Assert.Null(_k.Bul(_s, KodOkutmaService.Malzeme, "ELD"));
        Assert.Null(_k.Bul(_s, KodOkutmaService.Ekipman, "EKS-07"));    // araç kodu ekipmanda aranmaz
        Assert.Throws<ArgumentException>(() => _k.Bul(_s, KodOkutmaService.Arac, " \r\n"));
        Assert.Throws<ArgumentException>(() => _k.Bul(_s, "personel", "X"));
        Exec($"UPDATE equipment SET is_deleted=1 WHERE id='{_ekp}';");
        Assert.Null(_k.Bul(_s, KodOkutmaService.Ekipman, "JN-12"));       // silinmiş kayıt seçilmez
    }

    [Fact]
    public void KO3_Firma_Ve_Yetki()
    {
        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('KOB','KOB',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin("KOB", "kob", "Okutma!2026", RoleKeys.CompanyAdmin);
        var b = new SessionContext(uid, "KOB", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Null(_k.Bul(b, KodOkutmaService.Arac, "EKS-07"));
        Assert.Null(_k.Bul(b, KodOkutmaService.Ekipman, "JN-12"));

        var yalnizArac = new SessionContext(_s.UserId, Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("vehicles", true, false, false, false) }));
        Assert.NotNull(_k.Bul(yalnizArac, KodOkutmaService.Arac, "EKS-07"));
        Assert.Throws<ForbiddenException>(() => _k.Bul(yalnizArac, KodOkutmaService.Ekipman, "JN-12"));
        Assert.Throws<ForbiddenException>(() => _k.Bul(yalnizArac, KodOkutmaService.Malzeme, "ELD-3"));
    }

    [Fact]
    public void KO4_Belirsizlik()
    {
        var v = new VehicleService(_f);
        var a = v.Create(_s, new NewVehicle("34ABC12", null, CurrentMeter: 1m));
        var b = v.Create(_s, new NewVehicle("KMY-2", "34 ABC 12", CurrentMeter: 1m));
        Assert.Equal(a, _k.Bul(_s, KodOkutmaService.Arac, "34ABC12")!.Id);    // iç kod, plakaya üstün
        Assert.Equal(b, _k.Bul(_s, KodOkutmaService.Arac, "KMY-2")!.Id);
        v.Create(_s, new NewVehicle("KMY-3", "06 ZZ 9", CurrentMeter: 1m));
        v.Create(_s, new NewVehicle("KMY-4", "06ZZ9", CurrentMeter: 1m));
        Assert.Null(_k.Bul(_s, KodOkutmaService.Arac, "06ZZ9"));              // iki plaka eşleşmesi → seçilmez
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void KO5_Iki_Platform_Ve_Api()
    {
        Assert.Contains("app.MapGet(\"/api/scan/resolve\"", Oku("src/DepoWise.Api/Program.cs"));
        Assert.Contains("DesktopServices.KodOkutma.Bul(", Oku("src/DepoWise.Desktop/ViewModels/FuelViewModel.cs"));
        Assert.Contains("KodOkutCommand", Oku("src/DepoWise.Desktop/Views/FuelView.axaml"));
        Assert.Contains("/api/scan/resolve", Oku("src/DepoWise.Web/Components/Pages/Fuel.razor"));
        Assert.Contains("DesktopServices.KodOkutma.Bul(", Oku("src/DepoWise.Desktop/ViewModels/AssignmentsViewModel.cs"));
        Assert.Contains("KodOkutCommand", Oku("src/DepoWise.Desktop/Views/AssignmentsView.axaml"));
        Assert.Contains("/api/scan/resolve", Oku("src/DepoWise.Web/Components/Pages/Assignments.razor"));
    }
}
