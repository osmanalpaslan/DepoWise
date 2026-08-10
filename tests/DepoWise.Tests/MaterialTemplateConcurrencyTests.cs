using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// KLT-01d — Malzeme şablonu düzenlemede düzenleme kilidi (2026-08-10).
///
/// Sorun: <see cref="MaterialTemplateService.Update"/> <b>12 alanı körlemesine</b> yazıyordu
/// (mevcut değerlerle karşılaştırma yoktu). Aynı GENEL şablonu iki firma yöneticisi eşzamanlı
/// düzenlerse ikincisi birincinin tüm değişikliklerini SESSİZCE eziyordu.
///
/// Kapsam notu (kullanıcı kararı): KLT-01d yalnız bu servisi kapsar.
/// • <c>PersonnelTitleService</c> → gerçek Update/Rename yolu YOK (tek UPDATE soft-delete,
///   <c>WHERE ... AND is_deleted=0</c> atomik CAS zaten var) → kapsam dışı.
/// • <c>CompanyService</c> → yalnız süper admin erişimli, çakışma gerçekçi değil → kapsam dışı.
///
/// Kişisel şablonda çakışma İMKÂNSIZDIR: EnsureManageable yalnız created_by sahibine izin verir.
/// Bu yüzden testler GENEL (is_global) şablon + iki admin senaryosu üzerinden kurulur.
/// </summary>
public class MaterialTemplateConcurrencyTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialTemplateService _templates;
    private readonly UserService _users;
    private readonly SessionContext _adminA1;
    private readonly SessionContext _adminA2;

    public MaterialTemplateConcurrencyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_klt01d_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _templates = new MaterialTemplateService(_factory, _clock);
        _users = new UserService(_factory, _clock);

        // AYNI firmanın İKİ farklı yöneticisi — gerçek çakışma senaryosu budur.
        var u1 = _users.EnsureInitialAdmin("A", "admin1", "admin123", RoleKeys.CompanyAdmin);
        _adminA1 = new SessionContext(u1, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var u2 = _users.CreateUser(_adminA1, new NewUser("admin2", "admin123", "İkinci Yönetici",
            new[] { RoleKeys.CompanyAdmin }));
        _adminA2 = new SessionContext(u2, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    /// <summary>Admin oluşturduğu için is_global=1 → her iki yönetici de düzenleyebilir.</summary>
    private string GlobalTemplate(string name = "Şablon") =>
        _templates.Create(_adminA1, new NewMaterialTemplate(name, Code: "K-1", Type: "Sarf Malzeme",
            MinStock: 1m, UnitPrice: 10m, Currency: "TRY", Description: "ilk açıklama"));

    private MaterialTemplateRecord Read(SessionContext s, string id) => _templates.Get(s, id)!;

    /// <summary>Denetim (audit) kaydı sayısı — çakışan işlemde ARTMAMALI.</summary>
    private long AuditCount(string templateId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE entity_id=@i AND entity_type='material_template';";
        cmd.AddWithValue("@i", templateId);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    // ───────────── Stale update → ConcurrencyException ─────────────

    [Fact]
    public void AyniSurumle_IkiYonetici_IkincisiConcurrencyHatasiAlir()
    {
        var id = GlobalTemplate();
        var v = Read(_adminA1, id).Version;   // her iki yönetici de AYNI sürümle formu açtı

        _templates.Update(_adminA1, id, new NewMaterialTemplate("Birinci Ad", Code: "BIR"), expectedVersion: v);

        Assert.Throws<ConcurrencyException>(() =>
            _templates.Update(_adminA2, id, new NewMaterialTemplate("İkinci Ad", Code: "IKI"), expectedVersion: v));
    }

    // ───────────── Stale sonrası veri: ilk değişiklik AYNEN korunur (12 alan) ─────────────

    [Fact]
    public void Cakismada_BirincininTumAlanlari_AynenKorunur_KismiYazmaYok()
    {
        var id = GlobalTemplate();
        var v = Read(_adminA1, id).Version;

        _templates.Update(_adminA1, id, new NewMaterialTemplate(
            "Birinci Ad", Code: "BIR", Type: "Yedek Parça", MinStock: 5m, UnitPrice: 99m,
            Currency: "USD", Description: "birinci açıklama", CompatibleVehicleIds: "v1,v2"), expectedVersion: v);

        var after1 = Read(_adminA1, id);

        Assert.Throws<ConcurrencyException>(() =>
            _templates.Update(_adminA2, id, new NewMaterialTemplate(
                "İkinci Ad", Code: "IKI", Type: "Lastik", MinStock: 77m, UnitPrice: 1m,
                Currency: "EUR", Description: "ikinci açıklama", CompatibleVehicleIds: "v9"), expectedVersion: v));

        // 12 alanın HİÇBİRİ ikinci işlemden etkilenmemeli.
        var after2 = Read(_adminA1, id);
        Assert.Equal(after1.Name, after2.Name);
        Assert.Equal(after1.Code, after2.Code);
        Assert.Equal(after1.Type, after2.Type);
        Assert.Equal(after1.CategoryId, after2.CategoryId);
        Assert.Equal(after1.UnitId, after2.UnitId);
        Assert.Equal(after1.BrandId, after2.BrandId);
        Assert.Equal(after1.SupplierId, after2.SupplierId);
        Assert.Equal(after1.MinStock, after2.MinStock);
        Assert.Equal(after1.UnitPrice, after2.UnitPrice);
        Assert.Equal(after1.Currency, after2.Currency);
        Assert.Equal(after1.Description, after2.Description);
        Assert.Equal(after1.CompatibleVehicleIds, after2.CompatibleVehicleIds);
        Assert.Equal(after1.Version, after2.Version);   // sürüm de artmamalı
    }

    // ───────────── Audit: çakışan işlem için kayıt OLUŞMAZ ─────────────

    [Fact]
    public void Cakisan_Islem_Icin_AuditKaydi_OLUSMAZ()
    {
        var id = GlobalTemplate();
        var v = Read(_adminA1, id).Version;

        _templates.Update(_adminA1, id, new NewMaterialTemplate("Birinci"), expectedVersion: v);
        var auditAfterFirst = AuditCount(id);

        Assert.Throws<ConcurrencyException>(() =>
            _templates.Update(_adminA2, id, new NewMaterialTemplate("İkinci"), expectedVersion: v));

        // Transaction commit edilmediği için audit de yazılmamalı.
        Assert.Equal(auditAfterFirst, AuditCount(id));
    }

    // ───────────── Güncel sürümle tekrar deneme başarılı ─────────────

    [Fact]
    public void GuncelSurumle_TekrarDeneme_Basarili()
    {
        var id = GlobalTemplate();
        var v0 = Read(_adminA1, id).Version;

        _templates.Update(_adminA1, id, new NewMaterialTemplate("Birinci"), expectedVersion: v0);
        Assert.Throws<ConcurrencyException>(() =>
            _templates.Update(_adminA2, id, new NewMaterialTemplate("İkinci"), expectedVersion: v0));

        // İkinci yönetici formu tazeler → güncel sürümle kaydı geçmeli (kilitlenip kalmaz).
        var fresh = Read(_adminA2, id).Version;
        Assert.NotEqual(v0, fresh);
        _templates.Update(_adminA2, id, new NewMaterialTemplate("İkinci"), expectedVersion: fresh);

        Assert.Equal("İkinci", Read(_adminA1, id).Name);
    }

    // ───────────── Farklı şablonlar birbirini engellemez ─────────────

    [Fact]
    public void FarkliSablonlar_BirbiriniEngellemez()
    {
        var id1 = GlobalTemplate("Şablon 1");
        var id2 = GlobalTemplate("Şablon 2");
        var v1 = Read(_adminA1, id1).Version;
        var v2 = Read(_adminA1, id2).Version;

        _templates.Update(_adminA1, id1, new NewMaterialTemplate("Yeni 1"), expectedVersion: v1);
        // id1'e yazmak id2'nin sürümünü ETKİLEMEZ.
        _templates.Update(_adminA2, id2, new NewMaterialTemplate("Yeni 2"), expectedVersion: v2);

        Assert.Equal("Yeni 1", Read(_adminA1, id1).Name);
        Assert.Equal("Yeni 2", Read(_adminA1, id2).Name);
    }

    // ───────────── Sürüm gönderilmezse eski davranış korunur ─────────────

    [Fact]
    public void SurumGonderilmezse_EskiDavranis_Korunur()
    {
        var id = GlobalTemplate();
        // Geriye uyumluluk: sürüm taşımayan eski çağrılar bozulmamalı.
        _templates.Update(_adminA1, id, new NewMaterialTemplate("Bir"));
        _templates.Update(_adminA2, id, new NewMaterialTemplate("İki"));   // kontrol yok → geçer
        Assert.Equal("İki", Read(_adminA1, id).Name);
    }

    [Fact]
    public void HerBasariliGuncelleme_SurumuArtirir()
    {
        var id = GlobalTemplate();
        var v0 = Read(_adminA1, id).Version;
        _templates.Update(_adminA1, id, new NewMaterialTemplate("Bir"), expectedVersion: v0);
        var v1 = Read(_adminA1, id).Version;
        _templates.Update(_adminA1, id, new NewMaterialTemplate("İki"), expectedVersion: v1);
        var v2 = Read(_adminA1, id).Version;

        Assert.True(v1 > v0, $"ilk güncelleme sürümü artırmalı (v0={v0}, v1={v1})");
        Assert.True(v2 > v1, $"ikinci güncelleme sürümü artırmalı (v1={v1}, v2={v2})");
    }

    // ───────────── Yetki: yanlış sürümle bile ForbiddenException ─────────────

    [Fact]
    public void YetkisizKullanici_YanlisSurumleBile_ForbiddenAlir()
    {
        var id = GlobalTemplate();
        var v = Read(_adminA1, id).Version;
        // "material_templates" modülünde Edit yetkisi olmayan kullanıcı (deny-by-default).
        var yetkisiz = new SessionContext("u-yetkisiz", "A", new[] { RoleKeys.Staff }, PermissionSet.Empty);

        // Yetki kontrolü sürüm kontrolünden ÖNCE çalışmalı → çakışma bilgisi sızmamalı.
        Assert.Throws<ForbiddenException>(() =>
            _templates.Update(yetkisiz, id, new NewMaterialTemplate("Sızıntı"), expectedVersion: v - 99));

        Assert.Equal("Şablon", Read(_adminA1, id).Name);
    }

    // ───────────── Tenant izolasyonu ─────────────

    [Fact]
    public void BaskaFirmanin_Sablonu_Duzenlenemez()
    {
        var id = GlobalTemplate();
        var v = Read(_adminA1, id).Version;

        var ub = _users.EnsureInitialAdmin("B", "adminB", "admin123", RoleKeys.CompanyAdmin);
        var adminB = new SessionContext(ub, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        Assert.ThrowsAny<Exception>(() =>
            _templates.Update(adminB, id, new NewMaterialTemplate("Sızıntı"), expectedVersion: v));

        Assert.Equal("Şablon", Read(_adminA1, id).Name);
    }

    // ───────────── Mevcut kapsam kuralları bozulmadı (regresyon) ─────────────

    [Fact]
    public void REGRESYON_KisiselSablonu_YalnizSahibi_Duzenleyebilir()
    {
        // Admin olmayan kullanıcının şablonu → is_global=0, yalnız sahibi düzenler.
        var staff = _users.CreateUser(_adminA1, new NewUser("personel", "p12345", "Personel",
            new[] { RoleKeys.Staff }, Permissions: new[]
            {
                new ModulePermission("material_templates", true, true, true, false),
            }));
        var staffSession = new SessionContext(staff, "A", new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("material_templates", true, true, true, false) },
                Array.Empty<string>()));

        var personal = _templates.Create(staffSession, new NewMaterialTemplate("Kişisel Şablon"));
        var v = _templates.Get(staffSession, personal)!.Version;

        // Sahibi düzenleyebilir (sürüm kontrolü bunu ENGELLEMEMELİ).
        _templates.Update(staffSession, personal, new NewMaterialTemplate("Kişisel Güncel"), expectedVersion: v);
        Assert.Equal("Kişisel Güncel", _templates.Get(staffSession, personal)!.Name);
    }

    [Fact]
    public void REGRESYON_GenelSablonu_AdminDuzenleyebilir()
    {
        var id = GlobalTemplate();
        var v = Read(_adminA2, id).Version;
        // İkinci admin de genel şablonu düzenleyebilmeli (EnsureManageable admin'e izin verir).
        _templates.Update(_adminA2, id, new NewMaterialTemplate("Admin2 Güncelledi"), expectedVersion: v);
        Assert.Equal("Admin2 Güncelledi", Read(_adminA1, id).Name);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}
