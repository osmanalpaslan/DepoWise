using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

public class VehicleTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly VehicleTemplateService _templates;
    private readonly VehicleService _vehicles;
    private readonly SessionContext _admin;

    public VehicleTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_veh_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _templates = new VehicleTemplateService(_factory, _clock);
        _vehicles = new VehicleService(_factory, _clock);
        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    // ---- Sayaç geriye gitmez ----
    [Fact]
    public void Sayac_DogrudanGeriye_Reddedilir()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 1000m));
        Assert.Throws<MeterBackwardException>(() => _vehicles.SetMeter(_admin, v, 900m));
        Assert.Equal(1000m, _vehicles.GetMeter(_admin, v)); // değişmedi
    }

    // ---- Liste (Faz 7b read-query) ----
    [Fact]
    public void Liste_AramaIcKodVePlakaUzerinde_Calisir()
    {
        _vehicles.Create(_admin, new NewVehicle("KM-001", Plate: "34 ABC 01", CurrentMeter: 500m));
        _vehicles.Create(_admin, new NewVehicle("KM-002", Plate: "06 XYZ 99"));

        Assert.Equal(2, _vehicles.List(_admin).Count);

        var byCode = _vehicles.List(_admin, "KM-001");
        Assert.Single(byCode);
        Assert.Equal("KM-001", byCode[0].InternalCode);
        Assert.Equal(500m, byCode[0].CurrentMeter);

        var byPlate = _vehicles.List(_admin, "XYZ");
        Assert.Single(byPlate);
        Assert.Equal("KM-002", byPlate[0].InternalCode);
    }

    [Fact]
    public void Sayac_Ileri_GuncellenirVeLoglanir()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 1000m));
        _vehicles.SetMeter(_admin, v, 1500m, "vehicle_form");
        Assert.Equal(1500m, _vehicles.GetMeter(_admin, v));

        var history = _vehicles.MeterHistory(v);
        // create logu (0→1000) + set logu (1000→1500)
        Assert.Equal(2, history.Count);
        Assert.Equal((1000m, 1500m, "vehicle_form"), history[1]);
    }

    [Fact]
    public void Sayac_Advance_KucukDeger_NoOp_GecmisKaydiEngellemez()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 1000m));
        // Geçmiş tarihli düşük okuma → ilerletmez ama hata da vermez
        Assert.False(_vehicles.AdvanceMeter(_admin, v, 800m, "maintenance"));
        Assert.Equal(1000m, _vehicles.GetMeter(_admin, v));
        // İleri okuma → ilerletir + loglar
        Assert.True(_vehicles.AdvanceMeter(_admin, v, 1200m, "maintenance"));
        Assert.Equal(1200m, _vehicles.GetMeter(_admin, v));
    }

    [Fact]
    public void Sayac_TumDegisimler_Loglanir()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 100m));
        _vehicles.AdvanceMeter(_admin, v, 200m, "fuel");
        _vehicles.SetMeter(_admin, v, 350m);
        var history = _vehicles.MeterHistory(v);
        Assert.Equal(3, history.Count); // create + advance + set
        Assert.All(history, h => Assert.True(h.New >= h.Old)); // hiçbiri geriye gitmez
    }

    // ---- İç kod benzersiz + otomatik ----
    [Fact]
    public void IcKod_Benzersiz()
    {
        _vehicles.Create(_admin, new NewVehicle("KM-001"));
        Assert.Throws<InvalidOperationException>(() => _vehicles.Create(_admin, new NewVehicle("KM-001")));
    }

    [Fact]
    public void IcKod_OtomatikUretim_EnBuyukArtiBir()
    {
        _vehicles.Create(_admin, new NewVehicle("KM-001"));
        _vehicles.Create(_admin, new NewVehicle("KM-005"));
        // baz KM-001; mevcut en büyük 005 → sonraki 006 (genişlik korunur)
        Assert.Equal("KM-006", _templates.GenerateNextInternalCode(_admin, "KM-001"));
    }

    // ---- Şablon ----
    [Fact]
    public void Sablon_YeniAraciDoldurur_VeMalzemeleriKopyalar()
    {
        var brand = new LookupService(_factory, _clock).AddBrand(_admin, "Cat", "vehicle");
        var m1 = _materials.Create(_admin, new NewMaterial("M-1", "Filtre"));
        var m2 = _materials.Create(_admin, new NewMaterial("M-2", "Yağ"));

        var tpl = _templates.Create(_admin,
            new NewVehicleTemplate("Ekskavatör", InternalCode: "EX-001", BrandId: brand, ProductionYear: 2020, DefaultMeterUnit: "hour"),
            materialIds: new[] { m1, m2 });

        var vid = _vehicles.Create(_admin, new NewVehicle("EX-010", TemplateId: tpl));

        // Şablon alanları doldu
        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT brand_id, production_year, meter_unit FROM vehicles WHERE id=$id;";
            cmd.Parameters.AddWithValue("$id", vid);
            using var r = cmd.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal(brand, r.GetString(0));
            Assert.Equal(2020, r.GetInt32(1));
            Assert.Equal("hour", r.GetString(2));
        }

        // Şablon malzemeleri araca kopyalandı → uyumlu malzeme detayı (çift tık) stoğu gösterir
        var forVehicle = _materials.MaterialsForVehicle(_admin, vid);
        Assert.Equal(2, forVehicle.Count);
        Assert.Contains(forVehicle, x => x.MaterialId == m1);
        Assert.Contains(forVehicle, x => x.MaterialId == m2);
    }

    [Fact]
    public void Sablon_KullaniciDegeri_Onceliklidir()
    {
        var b1 = new LookupService(_factory, _clock).AddBrand(_admin, "Cat", "vehicle");
        var b2 = new LookupService(_factory, _clock).AddBrand(_admin, "Komatsu", "vehicle");
        var tpl = _templates.Create(_admin, new NewVehicleTemplate("T", BrandId: b1, ProductionYear: 2018));
        var vid = _vehicles.Create(_admin, new NewVehicle("V-1", BrandId: b2, TemplateId: tpl)); // kullanıcı b2 verdi

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT brand_id FROM vehicles WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", vid);
        Assert.Equal(b2, cmd.ExecuteScalar()); // şablon b1'i ezmedi
    }

    // ---- Tenant / yetki ----
    [Fact]
    public void Arac_DenyByDefault()
    {
        var noPerm = new SessionContext("u", "A", Array.Empty<string>(), PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _vehicles.Create(noPerm, new NewVehicle("V-1")));
    }

    [Fact]
    public void Arac_TenantIzolasyonu_SayacBaskaFirmaErisemez()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 100m));
        var users = new UserService(_factory, _clock);
        var bid = users.EnsureInitialAdmin("B", "admin_b", "admin123", RoleKeys.CompanyAdmin);
        var adminB = new SessionContext(bid, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _vehicles.GetMeter(adminB, v));
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
