using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ALAN ZORUNLULUĞU (kullanıcı isteği 2026-09-03, Migration087) ═══
///
/// Firma yöneticisi opsiyonel form alanlarını FİRMA bazında zorunlu yapar. Kilitler:
///  AZ1 — Migration087 additive: tablo oluşur, hiçbir mevcut tabloya dokunmaz; kayıt yokken TÜM
///        alanlar katalog varsayılanındadır (hiçbir form davranışı değişmez).
///  AZ2 — Zorunlu yapılan alan eksikse EksikAlanlar etiketiyle bildirir; doldurulunca geçer;
///        opsiyonele döndürülünce kontrol düşer.
///  AZ3 — SİSTEM zorunlusu gevşetilemez; katalogda olmayan alan yazılamaz (fail-closed).
///  AZ4 — FİRMA İZOLASYONU: A firmasının ayarı B firmasını ETKİLEMEZ (kullanıcının açık şartı).
///  AZ5 — Yetki: field_settings olmayan kullanıcı listeleyemez/yazamaz; okuma yolu
///        (RequiredFieldsFor) yetkisiz de çalışır (formlar herkes için doğrular).
/// </summary>
public class AlanZorunluluguTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly FieldRequirementService _svc;
    private readonly SessionContext _adminA, _adminB;

    public AlanZorunluluguTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_alanzor_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        FieldRequirementService.InvalidateAll();   // süreç-geneli önbellek: önceki testin firması karışmasın
        var users = new UserService(_f);
        var ua = users.EnsureInitialAdmin("A", "admin_a", "admin123", RoleKeys.CompanyAdmin);
        var ub = users.EnsureInitialAdmin("B", "admin_b", "admin123", RoleKeys.CompanyAdmin);
        _adminA = new SessionContext(ua, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _adminB = new SessionContext(ub, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _svc = new FieldRequirementService(_f);
    }

    [Fact]
    public void AZ1_Kayit_Yokken_Katalog_Varsayilani_Gecerli()
    {
        // Hiçbir firma ayarı yok → zorunlu listesi BOŞ (formların davranışı değişmedi).
        Assert.Empty(_svc.RequiredFieldsFor("A", "vehicles"));

        // Yönetim listesi katalogla birebir: sistem zorunluları kilitli, diğerleri opsiyonel.
        var liste = _svc.List(_adminA);
        Assert.Equal(FieldCatalog.All.Count, liste.Count);
        Assert.True(liste.Single(r => r.ScreenKey == "vehicles" && r.FieldKey == "internal_code").SystemRequired);
        Assert.False(liste.Single(r => r.ScreenKey == "vehicles" && r.FieldKey == "plate").Required);
    }

    [Fact]
    public void AZ2_Zorunlu_Yapilan_Alan_Denetlenir_Geri_Alininca_Duser()
    {
        _svc.Set(_adminA, "vehicles", "plate", required: true);

        var eksik = _svc.EksikAlanlar("A", "vehicles", new Dictionary<string, bool> { ["plate"] = false });
        Assert.Equal(new[] { "Plaka" }, eksik);   // kullanıcıya ETİKETİYLE söylenir (ham anahtar değil)

        Assert.Empty(_svc.EksikAlanlar("A", "vehicles", new Dictionary<string, bool> { ["plate"] = true }));

        _svc.Set(_adminA, "vehicles", "plate", required: false);
        Assert.Empty(_svc.EksikAlanlar("A", "vehicles", new Dictionary<string, bool> { ["plate"] = false }));
    }

    [Fact]
    public void AZ3_Sistem_Zorunlusu_Gevsetilemez_Bilinmeyen_Alan_Yazilamaz()
    {
        Assert.Throws<InvalidOperationException>(() => _svc.Set(_adminA, "vehicles", "internal_code", false));
        Assert.Throws<InvalidOperationException>(() => _svc.Set(_adminA, "fuel", "liters", true));   // kilitli — dokunulamaz
        Assert.Throws<ArgumentException>(() => _svc.Set(_adminA, "vehicles", "boyle_alan_yok", true));
        Assert.Throws<ArgumentException>(() => _svc.Set(_adminA, "boyle_ekran_yok", "plate", true));
    }

    [Fact]
    public void AZ4_Firma_Izolasyonu_A_nin_Ayari_B_yi_Etkilemez()
    {
        _svc.Set(_adminA, "materials", "supplier", required: true);

        Assert.Contains("supplier", _svc.RequiredFieldsFor("A", "materials"));
        Assert.Empty(_svc.RequiredFieldsFor("B", "materials"));   // B firması etkilenmedi

        // B kendi ayarını bağımsız yapar; A'nınki değişmez.
        _svc.Set(_adminB, "materials", "brand", required: true);
        Assert.DoesNotContain("brand", _svc.RequiredFieldsFor("A", "materials"));
        Assert.Contains("brand", _svc.RequiredFieldsFor("B", "materials"));
    }

    [Fact]
    public void AZ5_Yetki_Kapilari()
    {
        var yetkisiz = new SessionContext("u-x", "A", new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _svc.List(yetkisiz));
        Assert.Throws<ForbiddenException>(() => _svc.Set(yetkisiz, "vehicles", "plate", true));

        // Okuma yolu yetkisiz de çalışır: formlar HER kullanıcı için doğrulamak zorunda.
        _svc.Set(_adminA, "vehicles", "plate", true);
        Assert.Contains("plate", _svc.RequiredFieldsFor("A", "vehicles"));

        // Yetki ağacı kaydı (kalıcı kural: yeni ekran otomatik eklenir) + kategori eşlemesi.
        Assert.Contains(AppModules.All, m => m.Key == "field_settings");
        Assert.Contains(AppModules.Grouped(), g => g.Items.Any(i => i.Key == "field_settings"));
        Assert.True(AppModules.IsAdminRestricted("field_settings"));
    }

    public void Dispose()
    {
        FieldRequirementService.InvalidateAll();
        GC.SuppressFinalize(this);
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
