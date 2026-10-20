using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using DepoWise.Infrastructure.WorkOrders;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-43 (AR-GE 2026-10-19, 05 F-087) — GÜNLÜK MAKİNE KONTROL LİSTESİ ═══
///  GK1 — Varsayılan / firmaya özel madde listesi (yalnız yönetici düzenler); tümü uygun kayıt iş emri açmaz
///  GK2 — Uygun olmayan madde + "arıza bildir": araç Arızalı, taslak arıza iş emri, kayda bağ
///  GK3 — Açık arıza varken yeni iş emri açılmaz, kontrol yine kaydedilir ve uyarı döner; "arıza bildir" seçilmezse iş emri yok
///  GK4 — Tekrar gönderim tek kayıt; firma ayrımı; doğrulamalar; iş emri yetkisi olmadan arıza açılamaz; eski şema
///  GK5 — İki platform + API (kaynak kilidi)
/// </summary>
public class GunlukKontrolTests : IDisposable
{
    private const string Co = "GKL";
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_gkl_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly GunlukKontrolService _g;
    private readonly string _arac;

    public GunlukKontrolTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "gkl", "Kontrol!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _arac = new VehicleService(_f).Create(_s, new NewVehicle("EKS-9", "06 GK 9", CurrentMeter: 100m));
        _g = new GunlukKontrolService(_f);
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

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");
    private long IsEmri() => Convert.ToInt64(Tek($"SELECT COUNT(*) FROM work_orders WHERE company_id='{Co}';"));
    private List<KontrolSonucu> Hepsi(bool uygun = true) => _g.Maddeler(_s).Select(m => new KontrolSonucu(m, uygun)).ToList();
    private List<KontrolSonucu> BiriBozuk()
    {
        var l = Hepsi();
        l[1] = new KontrolSonucu(l[1].Madde, false, "sızıntı var");
        return l;
    }

    [Fact]
    public void GK1_Maddeler_Ve_Uygun_Kayit()
    {
        Assert.Equal(GunlukKontrolService.VarsayilanMaddeler, _g.Maddeler(_s));
        _g.MaddeleriKaydet(_s, new[] { "Yağ", " Fren ", "yağ", "" });
        Assert.Equal(new[] { "Yağ", "Fren" }, _g.Maddeler(_s));
        var op = new SessionContext(_s.UserId, Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("vehicles", true, true, true, false) }));
        Assert.Throws<ForbiddenException>(() => _g.MaddeleriKaydet(op, new[] { "X" }));
        _g.MaddeleriKaydet(_s, Array.Empty<string>());
        Assert.Equal(GunlukKontrolService.VarsayilanMaddeler, _g.Maddeler(_s));   // boş → varsayılana döner

        var r = _g.Kaydet(op, _arac, null, Hepsi(), null, arizaAc: true, Op());
        Assert.Null(r.IsEmriNo);
        Assert.Equal(0L, IsEmri());
        var k = Assert.Single(_g.Liste(_s, _arac));
        Assert.Equal(0, k.UygunDegil);
        Assert.Contains("tüm maddeler uygun", k.Satir);
        Assert.Equal(VehicleStatus.Active, Tek($"SELECT status FROM vehicles WHERE id='{_arac}';"));
    }

    [Fact]
    public void GK2_Uygun_Degil_Ariza_Bildir()
    {
        var r = _g.Kaydet(_s, _arac, null, BiriBozuk(), "vardiya başı", arizaAc: true, Op());
        Assert.False(string.IsNullOrEmpty(r.IsEmriNo));
        Assert.Null(r.Uyari);
        Assert.Equal(1L, IsEmri());
        Assert.Equal(VehicleStatus.Faulty, Tek($"SELECT status FROM vehicles WHERE id='{_arac}';"));
        var wo = (string)Tek($"SELECT work_order_id FROM daily_checks WHERE id='{r.Id}';")!;
        Assert.Equal(_arac, Tek($"SELECT fault_vehicle_id FROM work_orders WHERE id='{wo}';"));
        Assert.Contains("sızıntı var", (string)Tek($"SELECT description FROM work_orders WHERE id='{wo}';")!);
        var k = Assert.Single(_g.Liste(_s, _arac));
        Assert.Equal(1, k.UygunDegil);
        Assert.Contains("UYGUN DEĞİL", k.Satir);
        Assert.Contains("arıza iş emri açıldı", k.Satir);
    }

    [Fact]
    public void GK3_Acik_Ariza_Ve_Bildirsiz()
    {
        _g.Kaydet(_s, _arac, null, BiriBozuk(), null, arizaAc: true, Op());
        var r = _g.Kaydet(_s, _arac, null, BiriBozuk(), null, arizaAc: true, Op());
        Assert.Null(r.IsEmriNo);
        Assert.Contains("açık arıza", r.Uyari);
        Assert.Equal(1L, IsEmri());
        Assert.Equal(2, _g.Liste(_s, _arac).Count);   // kontrol yine kaydedildi

        var a2 = new VehicleService(_f).Create(_s, new NewVehicle("EKS-10", null, CurrentMeter: 1m));
        var r2 = _g.Kaydet(_s, a2, null, BiriBozuk(), null, arizaAc: false, Op());
        Assert.Null(r2.IsEmriNo);
        Assert.Equal(1L, IsEmri());
        Assert.Equal(VehicleStatus.Active, Tek($"SELECT status FROM vehicles WHERE id='{a2}';"));
    }

    [Fact]
    public void GK4_Tekrar_Firma_Dogrulama_Yetki_Eski_Sema()
    {
        var op = Op();
        var r1 = _g.Kaydet(_s, _arac, null, BiriBozuk(), null, arizaAc: true, op);
        var r2 = _g.Kaydet(_s, _arac, null, BiriBozuk(), null, arizaAc: true, op);
        Assert.Equal(r1.Id, r2.Id);
        Assert.Equal(r1.IsEmriNo, r2.IsEmriNo);
        Assert.Single(_g.Liste(_s, _arac));
        Assert.Equal(1L, IsEmri());

        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('GKB','GKB',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin("GKB", "gkb", "Kontrol!2026", RoleKeys.CompanyAdmin);
        var b = new SessionContext(uid, "GKB", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _g.Kaydet(b, _arac, null, Hepsi(), null, false, Op()));
        Assert.Empty(_g.Liste(b, _arac));

        Assert.Throws<ArgumentException>(() => _g.Kaydet(_s, _arac, null, Array.Empty<KontrolSonucu>(), null, false, Op()));
        Assert.Throws<ArgumentException>(() => _g.Kaydet(_s, _arac, null, new[] { new KontrolSonucu(" ", true) }, null, false, Op()));
        Assert.Throws<ArgumentException>(() => _g.Kaydet(_s, _arac, null, Hepsi(), null, false, " "));

        var a2 = new VehicleService(_f).Create(_s, new NewVehicle("EKS-11", null, CurrentMeter: 1m));
        var operator_ = new SessionContext(_s.UserId, Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("vehicles", true, true, true, false) }));   // iş emri yetkisi YOK
        Assert.Throws<ForbiddenException>(() => _g.Kaydet(operator_, a2, null, BiriBozuk(), null, arizaAc: true, Op()));
        Assert.Empty(_g.Liste(_s, a2));                                   // yarım kayıt yok
        _g.Kaydet(operator_, a2, null, BiriBozuk(), null, arizaAc: false, Op());   // arızasız kayıt yapılabilir

        Exec("DROP TABLE daily_checks;");
        Assert.Empty(_g.Liste(_s, _arac));
        Assert.Contains("desteklemiyor", Assert.Throws<InvalidOperationException>(() => _g.Kaydet(_s, _arac, null, Hepsi(), null, false, Op())).Message);
        Exec("DROP TABLE daily_check_templates;");
        using (var c = _f.Create()) new Migration120_DailyChecks().Up(c, null!);
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void GK5_Iki_Platform_Ve_Api()
    {
        var api = Oku("src/DepoWise.Api/Program.cs");
        Assert.Contains("app.MapGet(\"/api/daily-checks/items\"", api);
        Assert.Contains("app.MapPut(\"/api/daily-checks/items\"", api);
        Assert.Contains("app.MapPost(\"/api/vehicles/{id}/daily-check\"", api);
        Assert.Contains("app.MapGet(\"/api/vehicles/{id}/daily-checks\"", api);
        Assert.Contains("DesktopServices.GunlukKontrol.Kaydet(", Oku("src/DepoWise.Desktop/ViewModels/VehiclesViewModel.cs"));
        Assert.Contains("KontrolKaydetCommand", Oku("src/DepoWise.Desktop/Views/VehiclesView.axaml"));
        Assert.Contains("/daily-check\"", Oku("src/DepoWise.Web/Components/Pages/Vehicles.razor"));
    }
}
