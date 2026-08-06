using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
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

    // ---- İşlem Geçmişi: şube transferi okunaklı metin üretir (madde 4, kullanıcı isteği 2026-08-06) ----
    [Fact]
    public void RecentHistory_SubeTransferi_OkunakliMetinUretir()
    {
        var branches = new BranchService(_factory, _clock);
        var brA = branches.Create(_admin, new NewBranch("Nevşehir"), companyId: "A");
        var brB = branches.Create(_admin, new NewBranch("Karaman"), companyId: "A");
        var v = _vehicles.Create(_admin, new NewVehicle("V-TRF", BranchId: brA));

        var history0 = _vehicles.RecentHistory(_admin, v);
        Assert.Contains(history0, h => h.Label == "Araç oluşturuldu.");

        _vehicles.Update(_admin, v, new UpdateVehicle(Plate: null, ProductionYear: null, Status: "active", StatusNote: null, BranchId: brB));

        var history = _vehicles.RecentHistory(_admin, v);
        Assert.Contains(history, h => h.Label.Contains("Nevşehir") && h.Label.Contains("Karaman") && h.Label.Contains("transfer edildi"));

        // Şube DEĞİŞMEYEN bir güncelleme → genel metin, transfer metni YOK.
        _vehicles.Update(_admin, v, new UpdateVehicle(Plate: "34 XX 99", ProductionYear: null, Status: "active", StatusNote: null, BranchId: brB));
        var history2 = _vehicles.RecentHistory(_admin, v);
        Assert.Contains(history2, h => h.Label == "Araç bilgileri güncellendi.");
    }

    // ---- Plaka benzersiz (kullanıcı isteği 2026-08-05) ----
    [Fact]
    public void Plaka_AyniFirmada_Tekrar_Reddedilir()
    {
        _vehicles.Create(_admin, new NewVehicle("KM-A", Plate: "34 ABC 01", CurrentMeter: 100m));
        // Aynı plaka ikinci araçta REDDEDİLİR
        Assert.Throws<InvalidOperationException>(() =>
            _vehicles.Create(_admin, new NewVehicle("KM-B", Plate: "34 ABC 01")));
        // Baştaki/sondaki boşluk farkı da aynı sayılır (Trim)
        Assert.Throws<InvalidOperationException>(() =>
            _vehicles.Create(_admin, new NewVehicle("KM-C", Plate: " 34 ABC 01 ")));
        // Plakasız araçlar serbest (birden çok plakasız olabilir)
        _vehicles.Create(_admin, new NewVehicle("KM-D"));
        _vehicles.Create(_admin, new NewVehicle("KM-E"));
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

    // ---- Araç tanımları (lookup) — form dropdown'ları ----
    [Fact]
    public void AracTanimlari_MarkaModelSantiyeSurucu_Calisir()
    {
        var look = new DepoWise.Infrastructure.Materials.LookupService(_factory, _clock);
        look.AddVehicleType(_admin, "İş Makinesi");
        var brand = look.AddVehicleBrand(_admin, "Caterpillar");
        look.AddVehicleModel(_admin, brand, "320D");
        look.AddBranch(_admin, "Şantiye A");
        look.AddPersonnel(_admin, "Ahmet Yılmaz", "Şoför");

        Assert.Single(look.List(_admin, "vehicle_types"));
        Assert.Single(look.ListBrands(_admin, "vehicle"));
        var models = look.ListVehicleModels(_admin, brand);
        Assert.Single(models);
        Assert.Equal("320D", models[0].Name);
        Assert.Single(look.List(_admin, "branches"));
        Assert.Single(look.ListPersonnel(_admin));

        // FK'lerle araç oluşturma çalışır
        var v = _vehicles.Create(_admin, new NewVehicle("KM-T", BrandId: brand, VehicleModelId: models[0].Id));
        Assert.False(string.IsNullOrEmpty(v));
    }

    // ---- Şablon (Araç Genel Tanım) liste/sil + iç kod ----
    [Fact]
    public void Sablon_OlusturListeSil_VeIcKodUretimi()
    {
        var look = new DepoWise.Infrastructure.Materials.LookupService(_factory, _clock);
        var brand = look.AddVehicleBrand(_admin, "Caterpillar");
        var tpl = new VehicleTemplateService(_factory, _clock);
        var id = tpl.Create(_admin, new NewVehicleTemplate("Ekskavatör Genel", InternalCode: "EX-001", BrandId: brand));

        var list = tpl.List(_admin);
        Assert.Single(list);
        Assert.Equal("Ekskavatör Genel", list[0].Name);
        Assert.Equal("Caterpillar", list[0].BrandName);

        // İç kod üretimi: mevcut araç EX-001 varsa sonrası EX-002
        _vehicles.Create(_admin, new NewVehicle("EX-001"));
        Assert.Equal("EX-002", tpl.GenerateNextInternalCode(_admin, "EX-001"));

        // Güncelle + uyumlu malzeme (SetMaterials → GetMaterialRows)
        var mat = new MaterialService(_factory, _clock);
        var m1 = mat.Create(_admin, new NewMaterial("MM-1", "Filtre"));
        tpl.Update(_admin, id, new NewVehicleTemplate("Ekskavatör Güncel", InternalCode: "EX-001", BrandId: brand));
        tpl.SetMaterials(_admin, id, new[] { m1 });
        Assert.Equal("Ekskavatör Güncel", tpl.List(_admin)[0].Name);
        var rows = tpl.GetMaterialRows(_admin, id);
        Assert.Single(rows);
        Assert.Equal("MM-1", rows[0].Code);

        tpl.Delete(_admin, id);
        Assert.Empty(tpl.List(_admin));
    }

    // ---- Detay / Güncelle / Sil (Faz: araç detay ekranı) ----
    [Fact]
    public void Detay_Guncelle_Sil_Calisir()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KM-5", Plate: "34 A 1", ProductionYear: 2020, CurrentMeter: 100m));

        var d = _vehicles.Get(_admin, v);
        Assert.Equal("34 A 1", d.Plate);
        Assert.Equal(2020, d.ProductionYear);

        _vehicles.Update(_admin, v, new UpdateVehicle(Plate: "06 B 2", ProductionYear: 2021, Status: "maintenance", StatusNote: "Servis"));
        var d2 = _vehicles.Get(_admin, v);
        Assert.Equal("06 B 2", d2.Plate);
        Assert.Equal(2021, d2.ProductionYear);
        Assert.Equal("maintenance", d2.Status);
        Assert.Equal("Servis", d2.StatusNote);

        // Durum maintenance değilse not saklanmaz
        _vehicles.Update(_admin, v, new UpdateVehicle(Plate: "06 B 2", ProductionYear: 2021, Status: "active", StatusNote: "x"));
        Assert.Null(_vehicles.Get(_admin, v).StatusNote);

        _vehicles.Delete(_admin, v);
        Assert.Empty(_vehicles.List(_admin));
        Assert.Throws<ForbiddenException>(() => _vehicles.Get(_admin, v)); // silinmiş → bulunamaz
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
    public void AracSablonu_Gorunurluk_OlusturanaGore()
    {
        // Admin şablonu global → personel görür; personel şablonu yalnız kendisine.
        var staff = new SessionContext("st", "A", new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("vehicle_templates", true, true, true, true) }));
        var adminTpl = _templates.Create(_admin, new NewVehicleTemplate("Admin Şablon"));
        var staffTpl = _templates.Create(staff, new NewVehicleTemplate("Personel Şablon"));

        var staffList = _templates.List(staff).Select(t => t.Id).ToHashSet();
        Assert.Contains(adminTpl, staffList);   // admin global görünür
        Assert.Contains(staffTpl, staffList);   // kendi şablonu görünür

        var adminList = _templates.List(_admin).Select(t => t.Id).ToHashSet();
        Assert.Contains(adminTpl, adminList);
        Assert.DoesNotContain(staffTpl, adminList); // personelin kişisel şablonu admin listesinde YOK
    }

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
            cmd.CommandText = "SELECT brand_id, production_year, meter_unit FROM vehicles WHERE id=@id;";
            cmd.AddWithValue("@id", vid);
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
        cmd.CommandText = "SELECT brand_id FROM vehicles WHERE id=@id;";
        cmd.AddWithValue("@id", vid);
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

    // ---- DÜZENLEME KİLİDİ (2026-07-22) ----

    [Fact]
    public void DuzenlemeKilidi_EskiSurumle_Kaydetmek_UzerineYazmaz()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KILIT-1", Plate: "06 AA 1"));
        var acilistakiSurum = _vehicles.Get(_admin, v).Version; // Kullanıcı-1 formu açtı

        // Kullanıcı-2 (ya da eşitlemeyle gelen başka makine) arada kaydı değiştirdi.
        _vehicles.Update(_admin, v, new UpdateVehicle(Plate: "06 BB 2", ProductionYear: null, Status: "active", StatusNote: null),
            expectedVersion: acilistakiSurum);

        // Kullanıcı-1 hâlâ ESKİ sürümü tutuyor → engellenmeli.
        var ex = Assert.Throws<ConcurrencyException>(() =>
            _vehicles.Update(_admin, v, new UpdateVehicle(Plate: "06 CC 3", ProductionYear: null, Status: "active", StatusNote: null),
                expectedVersion: acilistakiSurum));
        Assert.True(ex.ActualVersion > ex.ExpectedVersion);

        // Kullanıcı-2'nin verisi korundu.
        Assert.Equal("06 BB 2", _vehicles.Get(_admin, v).Plate);
    }

    [Fact]
    public void DuzenlemeKilidi_GuncelSurumle_Calisir_SurumsuzCagriBozulmaz()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KILIT-2"));
        _vehicles.Update(_admin, v, new UpdateVehicle(Plate: "34 XX 1", ProductionYear: null, Status: "active", StatusNote: null),
            expectedVersion: _vehicles.Get(_admin, v).Version);
        Assert.Equal("34 XX 1", _vehicles.Get(_admin, v).Plate);

        // Sürüm verilmezse eski davranış korunur (geriye uyumluluk).
        _vehicles.Update(_admin, v, new UpdateVehicle(Plate: "34 YY 2", ProductionYear: null, Status: "active", StatusNote: null));
        Assert.Equal("34 YY 2", _vehicles.Get(_admin, v).Plate);
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
