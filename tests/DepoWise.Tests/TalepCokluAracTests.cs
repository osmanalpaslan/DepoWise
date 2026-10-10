using DepoWise.Application.Common;
using DepoWise.Application.Requests;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ 2026-10-10 (kullanıcı isteği) — TALEP KALEMİNDE BİRDEN FAZLA ARAÇ + PDF'te MALZEME KODU.
/// TCA1: iki araçlı kalem → düzenleme formu, detay ve PDF verisi İKİ aracı da döndürür; vehicle_id = ilk araç.
/// TCA2: eski kayıt (yalnız vehicle_id, vehicle_ids boş) tek araç olarak okunur (geriye uyum).
/// TCA3: başka firmanın aracı kaleme yazılamaz (tenant) — eskiden araç kimliği hiç doğrulanmıyordu.
/// TCA4: düzenlemede araç listesi tam değişir (çıkarılan araç geri gelmez).
/// TCA5: standart + ekonomik PDF çok araçlı kalemle üretilir.
/// </summary>
public class TalepCokluAracTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), "depowise_tca_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _a, _b;
    private readonly RequestService _req;
    private readonly string _mat, _v1, _v2, _vB;

    public TalepCokluAracTests()
    {
        _f = new SqliteConnectionFactory(_db);
        new MigrationRunner(_f).Run();
        var clock = new SystemClock();
        var users = new UserService(_f, clock);
        _a = new SessionContext(users.EnsureInitialAdmin("A", "a", "a123456", RoleKeys.CompanyAdmin), "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _b = new SessionContext(users.EnsureInitialAdmin("B", "b", "b123456", RoleKeys.CompanyAdmin), "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _req = new RequestService(_f, new StockService(_f, clock), clock);
        _mat = new MaterialService(_f, clock).Create(_a, new NewMaterial("FLT-01", "Yağ Filtresi"));
        var veh = new VehicleService(_f, clock);
        _v1 = veh.Create(_a, new NewVehicle("EKS-01", "34 ABC 01", ChassisNo: "SASE1"));
        _v2 = veh.Create(_a, new NewVehicle("EKS-02", "34 ABC 02"));
        _vB = veh.Create(_b, new NewVehicle("B-ARAC"));
    }

    public void Dispose() { try { File.Delete(_db); } catch { } }

    [Fact]
    public void TCA1_Iki_Aracli_Kalem_Her_Yerde_Iki_Araci_Gosterir()
    {
        var id = _req.Create(_a, new NewRequest(new[] { new RequestItemInput(_mat, 4m, VehicleIds: new[] { _v1, _v2 }) })).Id;

        var edit = _req.GetForEdit(_a, id).Items.Single();
        Assert.Equal(new[] { _v1, _v2 }, edit.Vehicles!.Select(v => v.Id));
        Assert.Equal(_v1, edit.VehicleId);                         // eski alan = ilk araç
        Assert.Equal("FLT-01", edit.Code);

        var detay = _req.GetItems(_a, id).Single();
        Assert.Contains("EKS-01", detay.VehiclesText);
        Assert.Contains("EKS-02", detay.VehiclesText);

        var pdf = _req.GetPdfData(_a, id).Items.Single();
        Assert.Equal(2, pdf.Vehicles!.Count);
        Assert.Equal("SASE1", pdf.Vehicles[0].Chassis);
        Assert.Equal("FLT-01", pdf.Code);

        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT vehicle_id, vehicle_ids FROM material_request_items WHERE request_id=@r;";
        cmd.AddWithValue("@r", id);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal(_v1, r.GetString(0));
        Assert.Equal($"{_v1},{_v2}", r.GetString(1));
    }

    [Fact]
    public void TCA2_Eski_Kayit_Tek_Arac_Olarak_Okunur()
    {
        var id = _req.Create(_a, new NewRequest(new[] { new RequestItemInput(_mat, 1m, _v2) })).Id;
        using (var conn = _f.Create())
        {
            using var cmd = conn.CreateCommand();   // eski istemcinin yazdığı satırı taklit et
            cmd.CommandText = "UPDATE material_request_items SET vehicle_ids=NULL WHERE request_id=@r;";
            cmd.AddWithValue("@r", id);
            cmd.ExecuteNonQuery();
        }
        var edit = _req.GetForEdit(_a, id).Items.Single();
        Assert.Equal(_v2, Assert.Single(edit.Vehicles!).Id);
        Assert.Contains("EKS-02", _req.GetItems(_a, id).Single().VehiclesText);
    }

    [Fact]
    public void TCA3_Baska_Firmanin_Araci_Yazilamaz()
    {
        Assert.Throws<ForbiddenException>(() =>
            _req.Create(_a, new NewRequest(new[] { new RequestItemInput(_mat, 1m, VehicleIds: new[] { _v1, _vB }) })));
        Assert.Throws<ForbiddenException>(() =>
            _req.Create(_a, new NewRequest(new[] { new RequestItemInput(_mat, 1m, _vB) })));   // eski tek-alan yolu da
    }

    [Fact]
    public void TCA4_Duzenlemede_Arac_Listesi_Tam_Degisir()
    {
        var id = _req.Create(_a, new NewRequest(new[] { new RequestItemInput(_mat, 2m, VehicleIds: new[] { _v1, _v2 }) })).Id;
        _req.Update(_a, id, new NewRequest(new[] { new RequestItemInput(_mat, 2m, VehicleIds: new[] { _v2 }) }));
        var edit = _req.GetForEdit(_a, id).Items.Single();
        Assert.Equal(_v2, Assert.Single(edit.Vehicles!).Id);
    }

    [Fact]
    public void TCA5_Pdf_Cok_Aracli_Kalemle_Uretilir()
    {
        var id = _req.Create(_a, new NewRequest(new[] { new RequestItemInput(_mat, 4m, VehicleIds: new[] { _v1, _v2 }) })).Id;
        var d = _req.GetPdfData(_a, id);
        var model = new RequestPdfModel("Firma", d.DocNo, "10.10.2026", "Beklemede", null, null, null, null, null,
            d.Items.Select(i => new RequestPdfItem(i.Code, i.Name, i.Unit, i.Quantity, i.VehicleCode, i.VehicleChassis,
                i.Vehicles?.Select(v => new RequestPdfVehicle(v.Code, v.Chassis)).ToList())).ToList());
        var pdf = new RequestPdfService();
        Assert.True(pdf.Generate(model, economic: false).Length > 1000);
        Assert.True(pdf.Generate(model, economic: true).Length > 1000);
    }
}
