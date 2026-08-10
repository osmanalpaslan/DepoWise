using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Adım 5 — Malzeme şablonu: admin şablonu (is_global) herkese; diğer kullanıcının şablonu yalnız
/// OLUŞTURANA görünür. Genel şablonu yalnız admin, kişiseli yalnız sahibi/admin yönetir.</summary>
public class MaterialTemplateTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialTemplateService _svc;

    private readonly SessionContext _admin = new("admin", "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    private readonly SessionContext _u1 = new("u1", "A", new[] { RoleKeys.Staff },
        new PermissionSet(new[] { new ModulePermission("material_templates", true, true, true, true) }));
    private readonly SessionContext _u2 = new("u2", "A", new[] { RoleKeys.Staff },
        new PermissionSet(new[] { new ModulePermission("material_templates", true, true, true, true) }));

    public MaterialTemplateTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_mtpl_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _svc = new MaterialTemplateService(_factory, _clock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    [Fact]
    public void AdminSablonu_Global_HerkeseGorunur()
    {
        var id = _svc.Create(_admin, new NewMaterialTemplate("Standart Cıvata", Code: "CIV", MinStock: 10m));
        Assert.Contains(_svc.List(_u1), t => t.Id == id && t.IsGlobal && !t.Mine);
        Assert.Contains(_svc.List(_u2), t => t.Id == id);
        Assert.NotNull(_svc.Get(_u2, id)); // içerik de görünür
    }

    [Fact]
    public void KullaniciSablonu_YalnizOlusturana()
    {
        var id = _svc.Create(_u1, new NewMaterialTemplate("U1 Şablon"));
        Assert.Contains(_svc.List(_u1), t => t.Id == id && !t.IsGlobal && t.Mine);
        Assert.DoesNotContain(_svc.List(_u2), t => t.Id == id);   // başka kullanıcı GÖRMEZ
        Assert.Null(_svc.Get(_u2, id));                           // içerik de gizli
    }

    [Fact]
    public void Yonetim_GenelYalnizAdmin_KiseselYalnizSahibi()
    {
        var global = _svc.Create(_admin, new NewMaterialTemplate("Genel"));
        var u1p = _svc.Create(_u1, new NewMaterialTemplate("U1"));

        // Personel genel şablonu düzenleyemez
        Assert.Throws<ForbiddenException>(() => _svc.Update(_u1, global, new NewMaterialTemplate("X")));
        // Başka personel kişisel şablonu düzenleyemez
        Assert.Throws<ForbiddenException>(() => _svc.Update(_u2, u1p, new NewMaterialTemplate("X")));
        // Sahibi kendi şablonunu düzenler; admin tümünü düzenler
        _svc.Update(_u1, u1p, new NewMaterialTemplate("U1 v2"));
        _svc.Update(_admin, u1p, new NewMaterialTemplate("U1 v3"));
        _svc.Update(_admin, global, new NewMaterialTemplate("Genel v2"));
    }

    [Fact]
    public void Sablon_Icerik_DoldururPrefill()
    {
        var id = _svc.Create(_admin, new NewMaterialTemplate("Kaynak", Code: "KYN", Type: "sarf", MinStock: 5m, UnitPrice: 12.5m, Currency: "TRY"));
        var rec = _svc.Get(_u1, id)!;
        Assert.Equal("KYN", rec.Code);
        Assert.Equal("sarf", rec.Type);
        Assert.Equal(5m, rec.MinStock);
        Assert.Equal(12.5m, rec.UnitPrice);
    }

    // ══════════ PRT-01 GRUP 2b (2026-08-10) ══════════

    /// <summary>Firma satırını hazırlar. Şablon testleri bugüne dek YALNIZ material_templates kullanıyordu
    /// (o tabloda FK yok); aşağıdaki testler MALZEME ve ARAÇ da oluşturuyor, onların
    /// <c>FOREIGN KEY (company_id) REFERENCES companies(id)</c> kısıtı var.</summary>
    private void EnsureCompany(string companyId)
        => new DepoWise.Infrastructure.Security.UserService(_factory, _clock)
            .EnsureInitialAdmin(companyId, "seed_" + companyId, "admin123", RoleKeys.CompanyAdmin);

    /// <summary>B-3 — ŞABLON SİLİNİNCE BAĞLI MALZEMELERİN BAĞI TEMİZLENİR.
    ///
    /// Eskiden yalnız şablon <c>is_deleted=1</c> yapılıyordu; <c>materials.template_id</c> kalıyordu.
    /// <see cref="Reporting.ReportService"/>.<c>MaterialsByTemplate</c> sorgusunda <c>t.is_deleted</c> filtresi
    /// OLMADIĞI için SİLİNMİŞ şablon raporda görünmeye devam ediyordu; malzemeler ise
    /// <c>MaterialsNonTemplate</c> (<c>template_id IS NULL</c>) kapsamına da giremiyordu.</summary>
    [Fact]
    public void Sablon_Silininde_BagliMalzemelerin_TemplateId_Temizlenir()
    {
        EnsureCompany("A");
        var materials = new MaterialService(_factory, _clock);
        var tpl = _svc.Create(_admin, new NewMaterialTemplate("Silinecek Şablon"));
        var m1 = materials.Create(_admin, new NewMaterial("SIL-1", "Bağlı 1", TemplateId: tpl));
        var m2 = materials.Create(_admin, new NewMaterial("SIL-2", "Bağlı 2", TemplateId: tpl));
        var bagimsiz = materials.Create(_admin, new NewMaterial("SIL-3", "Bağımsız"));

        Assert.Equal(tpl, materials.GetDetail(_admin, m1).TemplateId);

        _svc.Delete(_admin, tpl);

        Assert.Null(materials.GetDetail(_admin, m1).TemplateId);   // bağ temizlendi
        Assert.Null(materials.GetDetail(_admin, m2).TemplateId);
        Assert.Null(materials.GetDetail(_admin, bagimsiz).TemplateId);   // zaten bağsızdı, bozulmadı
        Assert.Null(_svc.Get(_admin, tpl));                              // şablon artık okunamaz
        Assert.DoesNotContain(_svc.List(_admin), t => t.Id == tpl);      // listede yok
    }

    /// <summary>B-3 tenant: bir firmanın şablonunu silmek BAŞKA firmanın malzemelerine dokunmaz.</summary>
    [Fact]
    public void Sablon_Silme_BASKA_FIRMANIN_malzemesine_dokunmaz()
    {
        EnsureCompany("A"); EnsureCompany("B");
        var adminB = new SessionContext("adminB", "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var materials = new MaterialService(_factory, _clock);

        var tplA = _svc.Create(_admin, new NewMaterialTemplate("A Şablonu"));
        var tplB = _svc.Create(adminB, new NewMaterialTemplate("B Şablonu"));
        var mA = materials.Create(_admin, new NewMaterial("T-A", "A malzemesi", TemplateId: tplA));
        var mB = materials.Create(adminB, new NewMaterial("T-B", "B malzemesi", TemplateId: tplB));

        _svc.Delete(_admin, tplA);

        Assert.Null(materials.GetDetail(_admin, mA).TemplateId);    // A firması etkilendi
        Assert.Equal(tplB, materials.GetDetail(adminB, mB).TemplateId);   // B firması ETKİLENMEDİ
    }

    /// <summary>List araması: ad VE kod üzerinden çalışır (servis sorgusunda ikisi de var).</summary>
    [Fact]
    public void Sablon_Aramasi_Ad_ve_Kod_uzerinden_calisir()
    {
        _svc.Create(_admin, new NewMaterialTemplate("Hidrolik Hortum", Code: "HH-100"));
        _svc.Create(_admin, new NewMaterialTemplate("Yağ Filtresi", Code: "YF-200"));

        Assert.Single(_svc.List(_admin, "Hidrolik"));      // ada göre
        Assert.Single(_svc.List(_admin, "YF-2"));          // koda göre
        Assert.Equal(2, _svc.List(_admin).Count);          // aramasız hepsi
        Assert.Empty(_svc.List(_admin, "bulunmayan"));
    }

    /// <summary>B-4 — uyumlu araç bağı kaydedilir ve aynen geri okunur (yuvarlak yolculuk).</summary>
    [Fact]
    public void UyumluAraclar_Kaydedilir_ve_AYNEN_geri_okunur()
    {
        EnsureCompany("A");
        var veh = new DepoWise.Infrastructure.Vehicles.VehicleService(_factory, _clock);
        var v1 = veh.Create(_admin, new DepoWise.Infrastructure.Vehicles.NewVehicle("ARAC-1"));
        var v2 = veh.Create(_admin, new DepoWise.Infrastructure.Vehicles.NewVehicle("ARAC-2"));

        var id = _svc.Create(_admin, new NewMaterialTemplate("Araçlı Şablon", CompatibleVehicleIds: $"{v1},{v2}"));
        var rec = _svc.Get(_admin, id)!;
        Assert.Equal($"{v1},{v2}", rec.CompatibleVehicleIds);

        _svc.Update(_admin, id, new NewMaterialTemplate("Araçlı Şablon", CompatibleVehicleIds: v2), rec.Version);
        Assert.Equal(v2, _svc.Get(_admin, id)!.CompatibleVehicleIds);
    }

    /// <summary>B-4 FİRMA İZOLASYONU — başka firmanın araç id'si şablona YAZILAMAZ (süzülür).</summary>
    [Fact]
    public void UyumluAraclar_BASKA_FIRMANIN_araci_SUZULUR()
    {
        EnsureCompany("A"); EnsureCompany("B");
        var adminB = new SessionContext("adminB", "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var veh = new DepoWise.Infrastructure.Vehicles.VehicleService(_factory, _clock);
        var kendi = veh.Create(_admin, new DepoWise.Infrastructure.Vehicles.NewVehicle("BENIM"));
        var yabanci = veh.Create(adminB, new DepoWise.Infrastructure.Vehicles.NewVehicle("BASKASI"));

        // A firması, B firmasının araç id'sini kendi şablonuna yazmayı deniyor
        var id = _svc.Create(_admin, new NewMaterialTemplate("Sızıntı Denemesi",
            CompatibleVehicleIds: $"{kendi},{yabanci},uydurma-id"));

        // Yalnız kendi aracı kalmalı; yabancı ve var olmayan id'ler düşer.
        Assert.Equal(kendi, _svc.Get(_admin, id)!.CompatibleVehicleIds);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
