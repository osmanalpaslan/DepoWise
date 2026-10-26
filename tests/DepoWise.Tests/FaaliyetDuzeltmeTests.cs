using DepoWise.Application.Common;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ GÜNLÜK FAALİYET TAM DÜZENLEME (kullanıcı isteği 2026-10-25) ═══
/// "Malzemelerin miktarlarını düzenleyemiyorum veya kaldıramıyorum; tüm alanlarda güncelleme yapabilmek istiyorum — sorun çıkaracak
/// durumda uyarı paneliyle engelle." Düzeltme = TEK atomik işlemde eski bakımın iptali (malzeme aynı depoya geri döner, sayaç
/// geçerli kayıtlardan yeniden hesaplanır) + yeni değerlerle yeni bakım; faaliyet satırı aynı kalır.
///  FD1 — miktar değişir (10 → 4): stok 90 → 96, eski bakım iptal, faaliyet tek ve yeni bakıma bağlı
///  FD2 — malzeme kaldırılır / yeni malzeme eklenir: stok doğru
///  FD3 — tekrar gönderim (aynı işlem kimliği) ikinci düzeltme yapmaz
///  FD4 — daha DÜŞÜK km'ye düzeltme yapılabilir (eski kayıt önce iptal edilir; "sayaç geriye gidemez" yanlış alarmı yok)
///  FD5 — ATOMİK: yeni kayıt hata verirse (yabancı araç) hiçbir şey değişmez
///  FD6 — engeller: iptal edilmiş kayıt (Engel metni + red); başka firma
///  FD7 — düzenleme verisi formu doldurur (tanım, km, malzemeler, depo)
///  FD8 — hareket kaydı: alanlar düzenlenir; araç DEVRİ (transfer) kaydında araç/tür kilitli
/// </summary>
public class FaaliyetDuzeltmeTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), "depowise_fduz_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly MaterialService _materials;
    private readonly OpeningStockService _opening;
    private readonly StockService _stock;
    private readonly VehicleService _vehicles;
    private readonly MaintenanceDefinitionService _defs;
    private readonly MaintenanceService _maint;
    private readonly DailyActivityService _daily;
    private readonly SessionContext _a, _b;

    public FaaliyetDuzeltmeTests()
    {
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        _materials = new MaterialService(_f);
        _opening = new OpeningStockService(_f);
        _stock = new StockService(_f);
        _vehicles = new VehicleService(_f);
        _defs = new MaintenanceDefinitionService(_f);
        _maint = new MaintenanceService(_f);
        _daily = new DailyActivityService(_f, _maint, null, _defs);
        var users = new UserService(_f);
        Firma("A"); Firma("B");
        _a = new SessionContext(users.EnsureInitialAdmin("A", "fd_a", "Test!2026", RoleKeys.CompanyAdmin), "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _b = new SessionContext(users.EnsureInitialAdmin("B", "fd_b", "Test!2026", RoleKeys.CompanyAdmin), "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" }) try { File.Delete(p); } catch { }
    }

    private void Firma(string id)
    {
        using var c = _f.Create(); using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) VALUES(@id,@id,1,1,1,0,5,20,5) ON CONFLICT(id) DO NOTHING;";
        cmd.AddWithValue("@id", id); cmd.ExecuteNonQuery();
    }
    private object? Tek(string sql) { using var c = _f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar(); }
    private long Say(string sql) => Convert.ToInt64(Tek(sql));
    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    private (string Act, string Mnt, string Mat, string Veh, string Def) Tohum(SessionContext s)
    {
        var v = _vehicles.Create(s, new NewVehicle("FD-" + s.CompanyId, CurrentMeter: 1000m));
        var m = _materials.Create(s, new NewMaterial("FDM-" + s.CompanyId, "Filtre"));
        _opening.RecordOpening(s, m, 100m, Op());
        var d = _defs.Create(s, new NewMaintenanceDefinition("Periyodik", 100m, "km"));
        var act = _daily.SaveMaintenanceActivity(s, new NewMaintenance(v, d, PerformedKm: 1100m,
            Materials: new[] { new MaintenanceMaterialLine(m, 10m) }), Op());
        return (act, (string)Tek($"SELECT maintenance_id FROM daily_activities WHERE id='{act}';")!, m, v, d);
    }

    [Fact]
    public void FD1_Miktar_Degisir_Stok_Dogru_Faaliyet_Tek()
    {
        var (act, eskiMnt, mat, veh, def) = Tohum(_a);
        Assert.Equal(90m, _stock.GetBalance(_a, mat));
        _daily.DuzeltBakim(_a, act, new NewMaintenance(veh, def, PerformedKm: 1100m, Description: "düzeltildi",
            Materials: new[] { new MaintenanceMaterialLine(mat, 4m) }), Op());
        Assert.Equal(96m, _stock.GetBalance(_a, mat));
        var yeniMnt = (string)Tek($"SELECT maintenance_id FROM daily_activities WHERE id='{act}';")!;
        Assert.NotEqual(eskiMnt, yeniMnt);
        Assert.Equal(1L, Say($"SELECT is_cancelled FROM vehicle_maintenances WHERE id='{eskiMnt}';"));
        Assert.Equal(1L, Say("SELECT COUNT(*) FROM daily_activities WHERE is_deleted=0;"));   // listede TEK kayıt
        Assert.Equal("düzeltildi", (string)Tek($"SELECT description FROM daily_activities WHERE id='{act}';")!);
    }

    [Fact]
    public void FD2_Malzeme_Kaldir_Ve_Ekle()
    {
        var (act, _, mat, veh, def) = Tohum(_a);
        var m2 = _materials.Create(_a, new NewMaterial("FDM-2", "Yağ"));
        _opening.RecordOpening(_a, m2, 50m, Op());
        _daily.DuzeltBakim(_a, act, new NewMaintenance(veh, def, PerformedKm: 1100m,
            Materials: new[] { new MaintenanceMaterialLine(m2, 5m) }), Op());   // eski malzeme KALDIRILDI, yenisi eklendi
        Assert.Equal(100m, _stock.GetBalance(_a, mat));
        Assert.Equal(45m, _stock.GetBalance(_a, m2));
    }

    [Fact]
    public void FD3_Tekrar_Gonderim_Ikinci_Duzeltme_Yapmaz()
    {
        var (act, _, mat, veh, def) = Tohum(_a);
        var op = Op();
        var yeni = new NewMaintenance(veh, def, PerformedKm: 1100m, Materials: new[] { new MaintenanceMaterialLine(mat, 4m) });
        _daily.DuzeltBakim(_a, act, yeni, op);
        _daily.DuzeltBakim(_a, act, yeni, op);
        Assert.Equal(96m, _stock.GetBalance(_a, mat));
        Assert.Equal(2L, Say("SELECT COUNT(*) FROM vehicle_maintenances;"));   // eski (iptal) + yeni — üçüncü yok
    }

    [Fact]
    public void FD4_Daha_Dusuk_Km_Ye_Duzeltilebilir()
    {
        var (act, _, mat, veh, def) = Tohum(_a);
        _daily.DuzeltBakim(_a, act, new NewMaintenance(veh, def, PerformedKm: 1050m,
            Materials: new[] { new MaintenanceMaterialLine(mat, 10m) }, RequireMeter: true), Op());
        Assert.Equal(1050m, _vehicles.GetMeter(_a, veh));
    }

    [Fact]
    public void FD5_Atomik_Hata_Olursa_Hicbir_Sey_Degismez()
    {
        var (act, eskiMnt, mat, _, def) = Tohum(_a);
        var yabanciArac = _vehicles.Create(_b, new NewVehicle("FD-YABANCI"));
        Assert.ThrowsAny<Exception>(() => _daily.DuzeltBakim(_a, act, new NewMaintenance(yabanciArac, def, PerformedKm: 1100m,
            Materials: new[] { new MaintenanceMaterialLine(mat, 4m) }), Op()));
        Assert.Equal(90m, _stock.GetBalance(_a, mat));                                          // iptal de geri alındı
        Assert.Equal(0L, Say($"SELECT is_cancelled FROM vehicle_maintenances WHERE id='{eskiMnt}';"));
        Assert.Equal(eskiMnt, (string)Tek($"SELECT maintenance_id FROM daily_activities WHERE id='{act}';")!);
    }

    [Fact]
    public void FD6_Engeller()
    {
        var (act, _, mat, veh, def) = Tohum(_a);
        _daily.Delete(_a, act);
        var v = _daily.DuzenlemeIcinOku(_a, act);
        Assert.NotNull(v.Engel);
        var ex = Assert.Throws<InvalidOperationException>(() => _daily.DuzeltBakim(_a, act, new NewMaintenance(veh, def, PerformedKm: 1100m), Op()));
        Assert.Contains("İptal edilmiş", ex.Message);
        var (actB, _, _, vehB, defB) = Tohum(_b);
        Assert.Throws<ForbiddenException>(() => _daily.DuzeltBakim(_a, actB, new NewMaintenance(vehB, defB, PerformedKm: 1100m), Op()));
    }

    [Fact]
    public void FD7_Duzenleme_Verisi_Formu_Doldurur()
    {
        var (act, _, mat, veh, def) = Tohum(_a);
        var v = _daily.DuzenlemeIcinOku(_a, act);
        Assert.Null(v.Engel);
        Assert.Equal(veh, v.VehicleId);
        Assert.NotNull(v.Bakim);
        Assert.Equal(def, v.Bakim!.DefinitionId);
        Assert.Equal(1100m, v.Bakim.PerformedKm);
        var satir = Assert.Single(v.Bakim.Malzemeler);
        Assert.Equal(mat, satir.MaterialId);
        Assert.Equal(10m, satir.Quantity);
    }

    /// <summary>FD9 — iki ortam + API kaynak kilidi: "Düzenle" tam forma gider (eski yalnız-açıklama paneline değil); düzeltme uçları var.</summary>
    [Fact]
    public void FD9_Iki_Ortam_Ve_Api()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        string Oku(string rel) => File.ReadAllText(Path.Combine(d!.FullName, rel));
        var v = Oku("src/DepoWise.Desktop/Views/DailyActivityView.axaml");
        Assert.Contains("DailyActivityViewModel)DataContext).BeginEditCommand}", v);
        Assert.DoesNotContain("DailyActivityViewModel)DataContext).BeginMetaEditCommand}", v);
        var vm = Oku("src/DepoWise.Desktop/ViewModels/DailyActivityViewModel.cs");
        Assert.Contains("DesktopServices.DailyActivity.DuzeltBakim(", vm);
        Assert.Contains("DesktopServices.DailyActivity.DuzeltHareket(", vm);
        Assert.Contains("DesktopServices.Stock.YoldaGonder(", vm);                     // istek 3: faaliyetteki şube dışı çıkış da iki adımlı
        Assert.DoesNotContain("DesktopServices.Stock.Transfer(", vm);
        var w = Oku("src/DepoWise.Web/Components/Pages/Daily.razor");
        Assert.Contains("/api/daily/{id}/duzenleme", w);
        Assert.Contains("/duzelt-bakim?version=", w);
        Assert.Contains("/duzelt-hareket?version=", w);
        var p = Oku("src/DepoWise.Api/Program.cs");
        Assert.Contains("app.MapPut(\"/api/daily/{id}/duzelt-bakim\"", p);
        Assert.Contains("app.MapPut(\"/api/daily/{id}/duzelt-hareket\"", p);
        Assert.Contains("app.MapGet(\"/api/daily/{id}/duzenleme\"", p);
    }

    [Fact]
    public void FD8_Hareket_Duzenlenir_Transferde_Arac_Kilitli()
    {
        var veh = _vehicles.Create(_a, new NewVehicle("FD-H1"));
        var veh2 = _vehicles.Create(_a, new NewVehicle("FD-H2"));
        var h = _daily.SaveMovement(_a, new NewMovementActivity("movement", veh, null, null, null, 2, "ilk"), Op());
        _daily.DuzeltHareket(_a, h, new NewMovementActivity("movement", veh2, null, null, null, 3, "yeni"));
        Assert.Equal(veh2, (string)Tek($"SELECT vehicle_id FROM daily_activities WHERE id='{h}';")!);
        Assert.Equal("yeni", (string)Tek($"SELECT description FROM daily_activities WHERE id='{h}';")!);

        var t = _daily.SaveMovement(_a, new NewMovementActivity("transfer", veh, null, null, null, null, "devir"), Op());
        Assert.NotNull(_daily.DuzenlemeIcinOku(_a, t).KilitliAlanlar);
        Assert.Throws<InvalidOperationException>(() => _daily.DuzeltHareket(_a, t, new NewMovementActivity("transfer", veh2, null, null, null, null, "x")));
        Assert.Throws<InvalidOperationException>(() => _daily.DuzeltHareket(_a, t, new NewMovementActivity("movement", veh, null, null, null, null, "x")));
        _daily.DuzeltHareket(_a, t, new NewMovementActivity("transfer", veh, null, null, null, 5, "açıklama düzeldi"));   // diğer alanlar serbest
        Assert.Equal("açıklama düzeldi", (string)Tek($"SELECT description FROM daily_activities WHERE id='{t}';")!);
    }
}
