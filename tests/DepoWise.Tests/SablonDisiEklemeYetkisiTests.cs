using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 4.10 — ŞABLON DIŞI ARAÇ / MALZEME EKLEME YETKİSİ (2026-09-06) ═══
///
/// <b>Kullanıcı isteği.</b> <i>"Şablon dışı araç ve malzeme eklemekte yetkiye dahil olan bir durum
/// olmalı; firmalar bunu kontrol edemeyebilirler."</i>
///
/// Şablon seçmeden kayıt açmak firmanın tanım düzenini bozar (aynı şey farklı adlarla çoğalır,
/// raporlarda "şablon dışı" kovası şişer). Artık bu ayrı bir yetkidir.
///
/// <b>Çözümsüzlük koruması (bilinçli tasarım).</b> Yetki deny-by-default'tur; ama HİÇ şablonu
/// olmayan bir firmada "şablon seç" demek kullanıcıyı kilitler (seçecek şablon yok). Bu yüzden
/// kural yalnız şablon düzeni KURMUŞ firmalarda işler.
///
///  SD1 — Şablonlu kayıt her zaman serbest (kural yalnız şablon DIŞI kayda bakar)
///  SD2 — 🔴 Şablonu olan firmada, yetkisiz kullanıcı şablonsuz MALZEME açamaz
///  SD3 — 🔴 Aynı kural ARAÇ için de geçerli (iki modül aynı kapı)
///  SD4 — Yetki verilince şablonsuz kayıt yeniden serbest (çift yönlü)
///  SD5 — Admin bypass sürer (firma yöneticisi kilitlenmez)
///  SD6 — 🔴 Şablonu OLMAYAN firmada davranış eskisi gibi (çözümsüzlük koruması)
///  SD7 — Yetki kalemi yetki ağacında görünür
/// </summary>
public class SablonDisiEklemeYetkisiTests : IDisposable
{
    private const string Co = "SBL";
    private const string Pass = "Sbl!2026";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly MaterialService _malzeme;
    private readonly VehicleService _arac;
    private readonly PermissionService _yetkiler;
    private readonly AuthService _auth;
    private readonly PermissionSnapshotCache _cache = new();
    private readonly string _personelId;

    public SablonDisiEklemeYetkisiTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_sablon_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");

        var users = new UserService(_f);
        users.EnsureInitialAdmin(Co, "sbl_admin", Pass, RoleKeys.CompanyAdmin);
        _personelId = users.EnsureInitialAdmin(Co, "sbl_personel", Pass, RoleKeys.Staff);

        _malzeme = new MaterialService(_f);
        _arac = new VehicleService(_f);
        _yetkiler = new PermissionService(_f, null, _cache);
        _auth = new AuthService(_f, null, _cache);

        YetkiVer();   // modül yetkileri tam; sınanan şey ŞABLON yetkisi
    }

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static SessionContext SuperAdmin() => new("sa", Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);

    private void YetkiVer(params string[] butonlar)
        => _yetkiler.SaveForUser(SuperAdmin(), _personelId,
            new[]
            {
                new ModulePermission("materials", true, true, true, true),
                new ModulePermission("vehicles", true, true, true, true),
                new ModulePermission("material_templates", true, true, true, true),
                new ModulePermission("vehicle_templates", true, true, true, true),
            },
            butonlar);

    private SessionContext Oturum(string ad)
    {
        var r = _auth.Login(Co, ad, Pass);
        Assert.True(r.Success, "Giriş başarısız: " + ad);
        return r.Session!;
    }

    private SessionContext Admin() => Oturum("sbl_admin");
    private SessionContext Personel() => Oturum("sbl_personel");

    /// <summary>Firmaya şablon tanımlar — kural yalnız şablonu OLAN firmada işler.</summary>
    private string MalzemeSablonu()
        => new MaterialTemplateService(_f).Create(Admin(), new NewMaterialTemplate("Standart Çimento"));

    private void AracSablonu()
        => Calistir($"INSERT INTO vehicle_templates(id,company_id,name,created_at,updated_at,version,is_deleted) " +
                    $"VALUES('{Guid.NewGuid():N}','{Co}','Standart Kamyon',1,1,1,0);");

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }

    // ══════════════════ SD1 ══════════════════

    [Fact]
    public void SD1_Sablonlu_Kayit_Her_Zaman_Serbest()
    {
        var tpl = MalzemeSablonu();

        var id = _malzeme.Create(Personel(), new NewMaterial("SBL-1", "Çimento", TemplateId: tpl));

        Assert.False(string.IsNullOrEmpty(id));
    }

    // ══════════════════ SD2 / SD3 ══════════════════

    [Fact]
    public void SD2_Yetkisiz_Kullanici_Sablonsuz_Malzeme_Acamaz()
    {
        MalzemeSablonu();   // firmanın şablon düzeni var

        var ex = Assert.Throws<ForbiddenException>(
            () => _malzeme.Create(Personel(), new NewMaterial("SBL-2", "Serbest Malzeme")));

        Assert.Contains("Şablon Dışı", ex.Message);
    }

    [Fact]
    public void SD3_Ayni_Kural_Arac_Icin_De_Gecerli()
    {
        AracSablonu();

        Assert.Throws<ForbiddenException>(
            () => _arac.Create(Personel(), new NewVehicle("SBL-ARC", "34 SBL 34")));
    }

    // ══════════════════ SD4 — ÇİFT YÖNLÜ ══════════════════

    [Fact]
    public void SD4_Yetki_Verilince_Sablonsuz_Kayit_Serbest()
    {
        MalzemeSablonu();
        YetkiVer(SpecialButtons.TemplateFreeCreate);

        var id = _malzeme.Create(Personel(), new NewMaterial("SBL-3", "Serbest Malzeme"));

        Assert.False(string.IsNullOrEmpty(id));
    }

    // ══════════════════ SD5 — ADMIN ══════════════════

    [Fact]
    public void SD5_Admin_Bypass_Surer()
    {
        MalzemeSablonu();

        var id = _malzeme.Create(Admin(), new NewMaterial("SBL-4", "Yönetici Malzemesi"));

        Assert.False(string.IsNullOrEmpty(id));
    }

    // ══════════════════ SD6 — ÇÖZÜMSÜZLÜK KORUMASI ══════════════════

    /// <summary>
    /// 🔴 Yeni yetki deny-by-default. Hiç şablonu olmayan firmada "şablon seç" demek kullanıcıyı
    /// ÇÖZÜMSÜZ bırakırdı (seçecek şablon yok). Böyle firmada davranış eskisi gibi kalmalı —
    /// aksi hâlde yeni kural sahada işi durdururdu.
    /// </summary>
    [Fact]
    public void SD6_Sablonu_Olmayan_Firmada_Davranis_Degismez()
    {
        // Hiç şablon tanımlanmadı.
        var id = _malzeme.Create(Personel(), new NewMaterial("SBL-5", "Şablonsuz Firma Malzemesi"));

        Assert.False(string.IsNullOrEmpty(id));
    }

    // ══════════════════ SD7 ══════════════════

    [Fact]
    public void SD7_Yetki_Kalemi_Agacta_Gorunur()
        => Assert.Contains(SpecialButtons.All, b => b.Key == SpecialButtons.TemplateFreeCreate);
}
