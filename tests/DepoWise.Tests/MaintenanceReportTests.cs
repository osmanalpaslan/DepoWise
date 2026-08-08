using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// Bakım Raporu (2026-08-08 — ortak standarda taşındı) hesaplama + davranış doğruluğu. Her satır bir bakım kaydı
/// (detay). Senaryolar: normal kayıt, çok malzemeli bakım, malzemesiz bakım, iptal edilen kaydın hariç kalması,
/// araç/araç türü/bakım tanımı/teknisyen/şube filtreleri, yetkisiz şube (fail-closed), malzeme maliyeti + kalem
/// sayısı, NumCell HAM/görüntü, TotalRow ayrımı (kayıt+kalem+maliyet toplamı), Sayaç'ın toplanmaması. Derived-table
/// (correlated subquery yok) çıktısı test edilir. Kolon sırası:
/// 0 Şube · 1 Tarih · 2 İç Kod · 3 Plaka · 4 Araç Adı · 5 Araç Türü · 6 Bakım · 7 Alt Bakım · 8 Sayaç ·
/// 9 Teknisyen · 10 Malzeme Kalem Sayısı · 11 Malzeme Maliyeti.
/// </summary>
public class MaintenanceReportTests : IDisposable
{
    private const long Base = 1_700_000_000_000;
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly ReportService _reports;
    private readonly SessionContext _admin;
    private readonly string _mat;

    public MaintenanceReportTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_maintrep_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _reports = new ReportService(_factory);
        var clock = new TestClock();
        var users = new UserService(_factory, clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var materials = new DepoWise.Infrastructure.Materials.MaterialService(_factory, clock);
        _mat = materials.Create(_admin, new DepoWise.Infrastructure.Materials.NewMaterial("MAT1", "Parça"));
        Seed();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(Base);
    }

    private void Seed()
    {
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('B1','A','Merkez',@n,@n);", ("@n", Base));
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('B2','A','Sahra',@n,@n);", ("@n", Base));
        Exec("INSERT INTO vehicle_types(id,company_id,name,created_at,updated_at) VALUES('T1','A','Kamyon',@n,@n);", ("@n", Base));
        Exec("INSERT INTO vehicle_types(id,company_id,name,created_at,updated_at) VALUES('T2','A','Is Makinesi',@n,@n);", ("@n", Base));
        Exec("INSERT INTO brands(id,company_id,name,created_at,updated_at) VALUES('BR1','A','Ford',@n,@n);", ("@n", Base));
        Exec("INSERT INTO vehicle_models(id,company_id,brand_id,name,created_at,updated_at) VALUES('MD1','A','BR1','Cargo',@n,@n);", ("@n", Base));
        // Bakım tanımları: DEF1 ana + SUB1 alt; DEF2 ana.
        Def("DEF1", "Periyodik", null);
        Def("SUB1", "10.000 km", "DEF1");
        Def("DEF2", "Yag Degisimi", null);
        // Teknisyenler
        Pers("P1", "Ahmet Usta");
        Pers("P2", "Mehmet Usta");
        // Araçlar
        Veh("v1", "V1", "34ABC01", "km", "B1", "T1", "BR1", "MD1");
        Veh("v2", "V2", null, "hour", "B2", "T2", null, null);

        // m1: V1, Periyodik + alt 10.000 km, tekn Ahmet, işlenen şube B1, sayaç 1200 km, 2 malzeme → 2*150+1*100=400
        Maint("m1", "v1", "DEF1", "SUB1", "P1", "B1", "1200", null, Base, cancelled: false);
        Material("m1", "2", "150"); Material("m1", "1", "100");
        // m2: V2 (saat), Yag Degisimi, tekn Mehmet, işlenen şube B2, sayaç 560 saat, 1 malzeme → 3*50=150
        Maint("m2", "v2", "DEF2", null, "P2", "B2", null, "560", Base, cancelled: false);
        Material("m2", "3", "50");
        // m3: V1, Yag Degisimi, teknisyensiz, işlenen şube B1, MALZEMESİZ → maliyet 0, kalem 0
        Maint("m3", "v1", "DEF2", null, null, "B1", "1500", null, Base, cancelled: false);
        // m4: V1, Periyodik, İPTAL → rapor dışı
        Maint("m4", "v1", "DEF1", null, "P1", "B1", "1300", null, Base, cancelled: true);
        Material("m4", "9", "999");
        // m5: V1, tarih DIŞI (uzak gelecek) → geçerli aralıkta elenir
        Maint("m5", "v1", "DEF1", null, "P1", "B1", "1400", null, Base + 500_000_000_000L, cancelled: false);
    }

    [Fact]
    public void Rapor_TemelYapi_IptalVeTarihDisiHaric()
    {
        var t = Run();
        Assert.Equal("Bakım Raporu", t.Title);
        Assert.Equal(12, t.Headers.Count);
        Assert.Equal(3, t.Rows.Count);   // m1, m2, m3 (m4 iptal, m5 tarih dışı)
        Assert.NotNull(t.Numeric);
        Assert.NotNull(t.TotalRow);
    }

    [Fact]
    public void NormalKayit_TumAlanlar_Dogru()
    {
        var m1 = Row(Run(), r => (string)r[9]! == "Ahmet Usta");
        Assert.Equal("Merkez", (string)m1[0]!);        // işlenen şube
        Assert.Equal("V1", (string)m1[2]!);
        Assert.Equal("34ABC01", (string)m1[3]!);
        Assert.Equal("Ford Cargo", (string)m1[4]!);
        Assert.Equal("Kamyon", (string)m1[5]!);
        Assert.Equal("Periyodik", (string)m1[6]!);
        Assert.Equal("10.000 km", (string)m1[7]!);     // alt bakım
        Assert.Equal(1200.0, D(m1[8]), 3);             // sayaç HAM
        Assert.Equal("1.200 km", Disp(m1[8]));         // sayaç görüntü (km)
        Assert.Equal(2.0, D(m1[10]), 3);               // kalem sayısı
        Assert.Equal(400.0, D(m1[11]), 3);             // malzeme maliyeti
    }

    [Fact]
    public void CokMalzemeliBakim_MaliyetVeKalem_Dogru()
    {
        var m1 = Row(Run(), r => (string)r[9]! == "Ahmet Usta");
        Assert.Equal(2.0, D(m1[10]), 3);               // 2 kalem
        Assert.Equal(400.0, D(m1[11]), 3);             // 2*150 + 1*100
    }

    [Fact]
    public void MalzemesizBakim_SifirVeTire()
    {
        var m3 = Row(Run(), r => (string)r[6]! == "Yag Degisimi" && (string)r[2]! == "V1");
        Assert.Equal("-", Disp(m3[10]));               // kalem "-"
        Assert.Equal(0.0, D(m3[10]), 3);
        Assert.Equal("-", Disp(m3[11]));               // maliyet "-"
        Assert.Equal(0.0, D(m3[11]), 3);               // HAM 0
    }

    [Fact]
    public void IptalBakim_RaporDisi()
    {
        // V1'in yalnız 2 kaydı gelir (m1 + m3); iptal edilen m4 ve tarih dışı m5 gelmez.
        var t = Run();
        Assert.Equal(2, t.Rows.Count(r => (string)r[2]! == "V1"));
    }

    [Fact]
    public void SaatArac_SayacSaatUzerinden()
    {
        var m2 = Row(Run(), r => (string)r[9]! == "Mehmet Usta");
        Assert.Equal("Is Makinesi", (string)m2[5]!);
        Assert.Equal(560.0, D(m2[8]), 3);
        Assert.EndsWith(" Saat", Disp(m2[8]));         // sayaç birimi SAAT
    }

    // ── Filtreler ──
    [Fact]
    public void AracFiltresi_YalnizSeciliArac()
    {
        var t = _reports.Maintenance(_admin, Req(vehicles: new[] { "v2" }));
        Assert.Single(t.Rows);
        Assert.Equal("V2", (string)t.Rows[0][2]!);
    }

    [Fact]
    public void AracTuruFiltresi_SQLdeUygulanir()
    {
        var t = _reports.Maintenance(_admin, Req(types: new[] { "T2" }));
        Assert.Single(t.Rows);
        Assert.Equal("V2", (string)t.Rows[0][2]!);
    }

    [Fact]
    public void BakimTanimiFiltresi_YalnizSeciliTanim()
    {
        var t = _reports.Maintenance(_admin, Req(defs: new[] { "DEF2" }));
        Assert.Equal(2, t.Rows.Count);                 // m2 + m3 (Yag Degisimi)
        Assert.All(t.Rows, r => Assert.Equal("Yag Degisimi", (string)r[6]!));
    }

    [Fact]
    public void TeknisyenFiltresi_YalnizSeciliTeknisyen()
    {
        var t = _reports.Maintenance(_admin, Req(techs: new[] { "P1" }));
        Assert.Single(t.Rows);                         // yalnız m1
        Assert.Equal("Ahmet Usta", (string)t.Rows[0][9]!);
    }

    [Fact]
    public void SubeFiltresi_YetkiliAdmin_AcikSecim()
    {
        var t = _reports.Maintenance(_admin, Req(branches: new[] { "B1" }));
        Assert.Equal(2, t.Rows.Count);                 // B1 = m1, m3
        Assert.All(t.Rows, r => Assert.Equal("Merkez", (string)r[0]!));
    }

    [Fact]
    public void YetkisizKullanici_SubeDegistiremez_OturumSubesineDuser()
    {
        var set = new PermissionSet(new[] { new ModulePermission("reports", true, false, false, false) }, Array.Empty<string>());
        var staff = new SessionContext("u2", "A", new[] { RoleKeys.Staff }, set) { OperatingBranchId = "B1" };
        var t = _reports.Maintenance(staff, Req(branches: new[] { "B2" }));   // B2 istese de B1'e kilitli
        Assert.All(t.Rows, r => Assert.Equal("Merkez", (string)r[0]!));
        Assert.DoesNotContain(t.Rows, r => (string)r[2]! == "V2");
    }

    // ── Toplam + NumCell ──
    [Fact]
    public void ToplamSatiri_KayitKalemMaliyet_Dogru_SatirlardaDegil()
    {
        var t = Run();
        Assert.DoesNotContain(t.Rows, r => ((string)r[0]!).StartsWith("TOPLAM"));
        var top = t.TotalRow!;
        Assert.StartsWith("TOPLAM", (string)top[0]!);
        Assert.Contains("3 kayıt", (string)top[0]!);   // 3 bakım kaydı
        Assert.Equal(3.0, D(top[10]), 3);              // kalem 2+1+0
        Assert.Equal(550.0, D(top[11]), 3);            // maliyet 400+150+0
        Assert.Equal("", Disp(top[8]));                // Sayaç TOPLANMAZ (boş)
    }

    [Fact]
    public void NumCell_HamDeger_GoruntudenBagimsiz()
    {
        var m1 = Row(Run(), r => (string)r[9]! == "Ahmet Usta");
        Assert.IsType<NumCell>(m1[11]);
        var n = (NumCell)m1[11]!;
        Assert.Equal(400.0, n.Value, 3);
        Assert.Contains("₺", n.Display);
    }

    // ── Yardımcılar ──
    private TableModel Run() => _reports.Maintenance(_admin, Req());

    private static ReportRequest Req(string[]? branches = null, string[]? vehicles = null, string[]? types = null,
        string[]? defs = null, string[]? techs = null)
        => new(true, 1, 2_000_000_000_000L, branches, vehicles, null, types, defs, techs);

    private static double D(object? v) => v switch
    {
        NumCell n => n.Value,
        double d => d,
        null => 0,
        _ => System.Convert.ToDouble(v),
    };

    private static string Disp(object? v) => v switch { NumCell n => n.Display, null => "", _ => v.ToString() ?? "" };

    private static IReadOnlyList<object?> Row(TableModel t, Func<IReadOnlyList<object?>, bool> pred)
        => t.Rows.First(pred);

    private void Def(string id, string name, string? parent)
        => Exec("INSERT INTO maintenance_definitions(id,company_id,parent_def_id,name,created_at,updated_at) VALUES(@id,'A',@p,@n2,@n,@n);",
            ("@id", id), ("@p", (object?)parent), ("@n2", name), ("@n", Base));

    private void Pers(string id, string name)
        => Exec("INSERT INTO personnel(id,company_id,full_name,created_at,updated_at) VALUES(@id,'A',@fn,@n,@n);",
            ("@id", id), ("@fn", name), ("@n", Base));

    private void Veh(string id, string code, string? plate, string unit, string branch, string type, string? brand, string? model)
        => Exec(@"INSERT INTO vehicles(id,company_id,internal_code,plate,meter_unit,branch_id,vehicle_type_id,brand_id,vehicle_model_id,current_meter,created_at,updated_at,version,is_deleted)
                  VALUES(@id,'A',@code,@plate,@unit,@branch,@type,@brand,@model,'0',@n,@n,1,0);",
            ("@id", id), ("@code", code), ("@plate", (object?)plate), ("@unit", unit), ("@branch", branch),
            ("@type", type), ("@brand", (object?)brand), ("@model", (object?)model), ("@n", Base));

    private void Maint(string id, string veh, string def, string? sub, string? tech, string opBranch,
        string? km, string? hour, long date, bool cancelled)
        => Exec(@"INSERT INTO vehicle_maintenances(id,company_id,vehicle_id,maintenance_def_id,sub_definition_id,technician_id,
                     performed_km,performed_hour,performed_date,op_branch_id,is_cancelled,operation_id,created_at,updated_at,version,is_deleted)
                  VALUES(@id,'A',@v,@def,@sub,@tech,@km,@hour,@d,@ob,@canc,@op,@n,@n,1,0);",
            ("@id", id), ("@v", veh), ("@def", def), ("@sub", (object?)sub), ("@tech", (object?)tech),
            ("@km", (object?)km), ("@hour", (object?)hour), ("@d", date), ("@ob", opBranch),
            ("@canc", cancelled ? 1 : 0), ("@op", "op-" + id), ("@n", Base));

    private void Material(string maintId, string qty, string price)
        => Exec("INSERT INTO maintenance_materials(id,maintenance_id,material_id,quantity,unit_price) VALUES(@id,@m,@mat,@q,@pr);",
            ("@id", maintId + "-mm-" + Guid.NewGuid().ToString("N")[..6]), ("@m", maintId), ("@mat", _mat), ("@q", qty), ("@pr", price));

    private void Exec(string sql, params (string, object?)[] ps)
    {
        using var c = _factory.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + ext); } catch { }
        }
    }
}
