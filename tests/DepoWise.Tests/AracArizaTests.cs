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
/// ═══ M-35 (AR-GE 2026-10-19, 05 F-077) — ARAÇ KARTINDAN ARIZA BİLDİR → İŞ EMRİ ═══
///  AA1 — Arıza: taslak iş emri + araç ataması + araç "Arızalı" + durum notu; numara otomatik
///  AA2 — Aynı araç için açık arıza varken ikincisi açılmaz; iş emri (normal) açılabilir ve araç durumu değişmez
///  AA3 — Tamamlanınca araç yeniden "Aktif", arıza süresi ölçülür; iptalde araç durumu değişmez
///  AA4 — Kullanıcı aracı elle başka duruma aldıysa tamamlama onu ezmez; başka açık arıza varken Aktif yapılmaz
///  AA5 — Yetki ve firma ayrımı; açıklama zorunlu; eski şema anlaşılır hata
///  AA6 — İki platform + API (kaynak kilidi)
/// </summary>
public class AracArizaTests : IDisposable
{
    private const string Co = "ARZ";
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_arz_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly WorkOrderService _wo;
    private readonly string _arac;

    public AracArizaTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "arz", "Ariza!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _arac = new VehicleService(_f).Create(_s, new NewVehicle("EKS-1", "06 AR 1", CurrentMeter: 100m));
        _wo = new WorkOrderService(_f);
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

    private string Durum() => (string)Tek($"SELECT status FROM vehicles WHERE id='{_arac}';")!;

    [Fact]
    public void AA1_Ariza_Bildirimi()
    {
        var (id, no) = _wo.AractanIsEmri(_s, _arac, " Motor yağ kaçırıyor ", ariza: true);
        Assert.StartsWith("IE-", no);
        Assert.Equal("draft", Tek($"SELECT status FROM work_orders WHERE id='{id}';"));
        Assert.Equal(_arac, Tek($"SELECT fault_vehicle_id FROM work_orders WHERE id='{id}';"));
        Assert.Equal("Motor yağ kaçırıyor", Tek($"SELECT description FROM work_orders WHERE id='{id}';"));
        Assert.Equal("high", Tek($"SELECT priority FROM work_orders WHERE id='{id}';"));
        Assert.Equal(1L, Convert.ToInt64(Tek($"SELECT COUNT(*) FROM work_order_assignments WHERE work_order_id='{id}' AND resource_type='vehicle' AND resource_id='{_arac}';")));
        Assert.Equal(VehicleStatus.Faulty, Durum());
        Assert.Contains(no, (string)Tek($"SELECT status_note FROM vehicles WHERE id='{_arac}';")!);
        Assert.Equal(no, _wo.AcikArizasi(_s, _arac)!.WoNo);
    }

    [Fact]
    public void AA2_Ikinci_Ariza_Acilmaz_Is_Emri_Acilir()
    {
        var (_, no) = _wo.AractanIsEmri(_s, _arac, "Fren", ariza: true);
        Assert.Contains(no, Assert.Throws<InvalidOperationException>(() => _wo.AractanIsEmri(_s, _arac, "Fren yine", ariza: true)).Message);
        Exec($"UPDATE vehicles SET status='{VehicleStatus.Active}' WHERE id='{_arac}';");
        var (isId, _) = _wo.AractanIsEmri(_s, _arac, "250 saat bakımı", ariza: false);
        Assert.True(Tek($"SELECT fault_vehicle_id FROM work_orders WHERE id='{isId}';") is DBNull);
        Assert.Equal(VehicleStatus.Active, Durum());   // normal iş emri araç durumunu değiştirmez
        Assert.Equal("normal", Tek($"SELECT priority FROM work_orders WHERE id='{isId}';"));
    }

    [Fact]
    public void AA3_Tamamlaninca_Arac_Aktif_Ve_Sure()
    {
        var (id, _) = _wo.AractanIsEmri(_s, _arac, "Hidrolik", ariza: true);
        _wo.SetStatus(_s, id, "assigned");
        _wo.SetStatus(_s, id, "in_progress");
        Assert.Equal(VehicleStatus.Faulty, Durum());
        _wo.SetStatus(_s, id, "completed", "değiştirildi");
        Assert.Equal(VehicleStatus.Active, Durum());
        Assert.True(Tek($"SELECT status_note FROM vehicles WHERE id='{_arac}';") is DBNull);
        Assert.NotNull(_wo.ArizaSuresiSaat(_s, id));
        Assert.Null(_wo.AcikArizasi(_s, _arac));

        var (iptal, _) = _wo.AractanIsEmri(_s, _arac, "Lastik", ariza: true);
        _wo.SetStatus(_s, iptal, "cancelled", "yanlış bildirim");
        Assert.Equal(VehicleStatus.Faulty, Durum());   // iptal aracı "çalışıyor" yapmaz — kullanıcı kontrol eder
        Assert.Null(_wo.ArizaSuresiSaat(_s, iptal));
    }

    [Fact]
    public void AA4_Elle_Degisen_Durum_Ezilmez()
    {
        var (id, _) = _wo.AractanIsEmri(_s, _arac, "Kaporta", ariza: true);
        Exec($"UPDATE vehicles SET status='{VehicleStatus.Maintenance}' WHERE id='{_arac}';");   // kullanıcı bakıma aldı
        _wo.SetStatus(_s, id, "assigned"); _wo.SetStatus(_s, id, "in_progress"); _wo.SetStatus(_s, id, "completed");
        Assert.Equal(VehicleStatus.Maintenance, Durum());
    }

    [Fact]
    public void AA5_Yetki_Firma_Ve_Eski_Sema()
    {
        var okur = new SessionContext(_s.UserId, Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission(WorkOrderService.Module, true, false, false, false) }));
        Assert.Throws<ForbiddenException>(() => _wo.AractanIsEmri(okur, _arac, "x", ariza: true));
        Assert.Throws<ArgumentException>(() => _wo.AractanIsEmri(_s, _arac, " ", ariza: true));

        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('ARB','ARB',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin("ARB", "arb", "Ariza!2026", RoleKeys.CompanyAdmin);
        var b = new SessionContext(uid, "ARB", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _wo.AractanIsEmri(b, _arac, "yabancı", ariza: true));
        Assert.Equal(VehicleStatus.Active, Durum());
        Assert.Equal(0L, Convert.ToInt64(Tek("SELECT COUNT(*) FROM work_orders;")));

        using (var c = _f.Create()) new Migration111_WorkOrderFault().Up(c, null!);   // tekrar çalışabilir
        Exec("DROP INDEX ix_work_orders_fault;");
        Exec("ALTER TABLE work_orders DROP COLUMN fault_vehicle_id;");
        Assert.Contains("desteklemiyor", Assert.Throws<InvalidOperationException>(() => _wo.AractanIsEmri(_s, _arac, "x", ariza: true)).Message);
        Assert.Null(_wo.AcikArizasi(_s, _arac));
        var normal = _wo.Create(_s, new NewWorkOrder("", "Normal iş"));   // eski şemada iş emri eskisi gibi
        _wo.SetStatus(_s, normal, "assigned"); _wo.SetStatus(_s, normal, "in_progress"); _wo.SetStatus(_s, normal, "completed");
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void AA6_Iki_Platform_Ve_Api()
    {
        var api = Oku("src/DepoWise.Api/Program.cs");
        Assert.Contains("app.MapPost(\"/api/vehicles/{id}/work-order\"", api);
        Assert.Contains("svc.WorkOrders.AractanIsEmri(s, id, d.Description ?? \"\", d.Fault, d.Priority)", api);
        Assert.Contains("DesktopServices.WorkOrders.AractanIsEmri(_session, Selected.Id, aciklama, ariza)", Oku("src/DepoWise.Desktop/ViewModels/VehiclesViewModel.cs"));
        Assert.Contains("ArizaBildirCommand", Oku("src/DepoWise.Desktop/Views/VehiclesView.axaml"));
        Assert.Contains("/work-order\", new { description = aciklama, fault = ariza }", Oku("src/DepoWise.Web/Components/Pages/Vehicles.razor"));
    }
}
