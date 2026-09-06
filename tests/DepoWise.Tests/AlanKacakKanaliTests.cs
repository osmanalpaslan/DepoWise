using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 3c — ALAN SÜZMESİNİN SERVİS KATMANINA YAYILMASI (ADR-222 §12) ═══
///
/// <b>Kapatılan gerçek sızıntı:</b> <c>fld_materials_unit_price</c> korumalıyken kullanıcı aynı
/// fiyatı <b>Stok Hareketleri</b> ekranından okuyabiliyordu (<c>stock_movements.unit_price</c>,
/// işlem anı snapshot'ı) ve <b>Malzeme Şablonu</b> kartından okuyabiliyordu
/// (<c>material_templates.unit_price</c>, malzeme fiyatının kaynağı).
///
/// Yeni katalog alanı EKLENMEDİ — mevcut alanın diğer taşıyıcıları aynı karara bağlandı.
///
///  KK1 — Koruma YOKKEN davranış birebir bugünkü gibi (fiyat görünür, yazılır)
///  KK2 — 🔴 Stok hareketi listesinde fiyat gizlenir (liste + ızgara), veri YERİNDE kalır
///  KK3 — 🔴 Fiyatı göremeyen kullanıcının GİRDİĞİ fiyat harekete yazılmaz
///  KK4 — 🔴 Malzeme şablonunda fiyat gizlenir
///  KK5 — 🔴 Şablon güncellemesinde gizli fiyat KORUNUR (sessiz veri kaybı yok)
///  KK6 — İzin verilince aynı kullanıcı fiyatı yeniden görür (kapı çift yönlü)
///  KK7 — SMOKE: koruma açıkken malzeme modülünün kendi yetkisi bozulmadı
/// </summary>
public class AlanKacakKanaliTests : IDisposable
{
    private const string Co = "KAC";
    private const string Pass = "Kac!2026";

    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly AuthService _auth;
    private readonly PermissionService _perms;
    private readonly FieldProtectionService _koruma;
    private readonly PermissionSnapshotCache _cache = new();
    private readonly MaterialService _mat;
    private readonly StockService _stok;
    private readonly MaterialTemplateService _sablon;
    private readonly string _personelId;

    private static readonly string FiyatAnahtari =
        FieldAccess.Key(FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice);

    public AlanKacakKanaliTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_kacak_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");

        var users = new UserService(_f);
        users.EnsureInitialAdmin(Co, "kac_admin", Pass, RoleKeys.CompanyAdmin);
        _personelId = users.EnsureInitialAdmin(Co, "kac_personel", Pass, RoleKeys.Staff);

        _auth = new AuthService(_f, null, _cache);
        _perms = new PermissionService(_f, null, _cache);
        _koruma = new FieldProtectionService(_f, null, _cache);
        _mat = new MaterialService(_f);
        _stok = new StockService(_f);
        _sablon = new MaterialTemplateService(_f);

        _perms.SaveForUser(SuperAdmin(), _personelId,
            new[] { Tam("materials"), Tam("stock"), Tam("material_templates") }, Array.Empty<string>());
    }

    // ── yardımcılar ─────────────────────────────────────────────────────────────────────────

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static SessionContext SuperAdmin() => new("sa", Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
    private static ModulePermission Tam(string m) => new(m, true, true, true, true);

    private SessionContext Oturum(string ad)
    {
        var r = _auth.Login(Co, ad, Pass);
        Assert.True(r.Success, "Giriş başarısız: " + ad);
        return r.Session!;
    }

    private void FiyatiKoru(bool korumali = true)
        => _koruma.Set(SuperAdmin(), FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice, korumali);

    /// <summary>Veritabanındaki HAM hareket fiyatı — maskeleme değil, gerçek kayıt.</summary>
    private decimal? HamHareketFiyati(string materialId)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT unit_price FROM stock_movements WHERE material_id=@m ORDER BY created_at DESC LIMIT 1;";
        cmd.AddWithValue("@m", materialId);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : Money.Parse((string)v);
    }

    private decimal HamSablonFiyati(string templateId)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT unit_price FROM material_templates WHERE id=@i;";
        cmd.AddWithValue("@i", templateId);
        return Money.Parse(cmd.ExecuteScalar() as string);
    }

    private (string MaterialId, SessionContext Admin) Hazirla(decimal hareketFiyati = 123.45m)
    {
        var admin = Oturum("kac_admin");
        var id = _mat.Create(admin, new NewMaterial("KAC-1", "Kaçak Testi", UnitPrice: 500m));
        _stok.ReceiveIn(admin, new[] { new StockLine(id, 10m, hareketFiyati) }, Guid.NewGuid().ToString("N"));
        return (id, admin);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }

    // ══════════════════ KK1 — GERİYE UYUMLULUK ══════════════════

    /// <summary>⭐ Koruma yokken hiçbir şey değişmez: hareket fiyatı görünür ve yazılır.</summary>
    [Fact]
    public void KK1_Koruma_Yokken_Hareket_Fiyati_Gorunur_Ve_Yazilir()
    {
        var (id, _) = Hazirla();
        var s = Oturum("kac_personel");

        var hareket = _stok.SearchMovements(s, null, null, null, 100).Single(x => x.Code == "KAC-1");
        Assert.Equal(123.45m, hareket.UnitPrice);
        Assert.Equal(123.45m, HamHareketFiyati(id));

        var grid = _stok.SearchMovementsGrid(s, null, null, null, null, null, null, 1, 50);
        Assert.Equal(123.45m, grid.Items.Single(x => x.Code == "KAC-1").UnitPrice);
    }

    // ══════════════════ KK2 — ASIL SIZINTI ══════════════════

    /// <summary>
    /// 🔴 Bu testin kapattığı hata: malzeme kartında fiyat gizliyken Stok Hareketleri ekranında
    /// AYNI fiyat görünüyordu. Hem eski liste hem sayfalanmış ızgara ölçülür.
    /// </summary>
    [Fact]
    public void KK2_Korumaliyken_Hareket_Fiyati_Gizlenir_Veri_Yerinde_Kalir()
    {
        var (id, _) = Hazirla();
        FiyatiKoru();
        var s = Oturum("kac_personel");

        Assert.False(MaterialService.FiyatGorunur(s));   // önkoşul

        var hareket = _stok.SearchMovements(s, null, null, null, 100).Single(x => x.Code == "KAC-1");
        Assert.Null(hareket.UnitPrice);
        Assert.Equal("—", hareket.PriceText);            // ekranda yanıltıcı sayı YOK

        var grid = _stok.SearchMovementsGrid(s, null, null, null, null, null, null, 1, 50);
        Assert.Null(grid.Items.Single(x => x.Code == "KAC-1").UnitPrice);

        var son = _stok.RecentMovements(s).Single(x => x.Code == "KAC-1");
        Assert.Null(son.UnitPrice);

        // 🔴 Gizlenen yalnız GÖRÜNÜMDÜR — kayıt yerinde.
        Assert.Equal(123.45m, HamHareketFiyati(id));
    }

    // ══════════════════ KK3 — YAZMA KAPISI ══════════════════

    /// <summary>
    /// Fiyatı göremeyen kullanıcının girdiği fiyat harekete YAZILMAZ. Hareket yeni kayıttır;
    /// korunacak eski değer yoktur → 403 yerine "fiyatsız hareket" yazılır ve işlem tamamlanır
    /// (stok girişi engellenmez).
    /// </summary>
    [Fact]
    public void KK3_Goremeyen_Kullanicinin_Girdigi_Fiyat_Yazilmaz()
    {
        var (id, _) = Hazirla();
        FiyatiKoru();
        var s = Oturum("kac_personel");

        _stok.ReceiveIn(s, new[] { new StockLine(id, 5m, 999m) }, Guid.NewGuid().ToString("N"));

        Assert.Null(HamHareketFiyati(id));               // 🔴 gönderilen 999 YAZILMADI
        Assert.Equal(15m, _stok.GetBalance(s, id));      // ama stok girişi gerçekleşti
    }

    // ══════════════════ KK4–KK5 — ŞABLON ══════════════════

    [Fact]
    public void KK4_Sablon_Fiyati_Gizlenir()
    {
        var admin = Oturum("kac_admin");
        var tid = _sablon.Create(admin, new NewMaterialTemplate("KAC Şablon", UnitPrice: 250m));

        Assert.Equal(250m, _sablon.Get(admin, tid)!.UnitPrice);   // önce görünüyor

        FiyatiKoru();
        var s = Oturum("kac_personel");
        Assert.Equal(0m, _sablon.Get(s, tid)!.UnitPrice);         // gizlendi
        Assert.Equal(250m, HamSablonFiyati(tid));                 // veri yerinde
    }

    /// <summary>🔴 Fiyatı göremeyen kullanıcı şablonu düzenlerse fiyat SIFIRLANMAMALI.</summary>
    [Fact]
    public void KK5_Sablon_Guncellemesinde_Gizli_Fiyat_Korunur()
    {
        // Şablonu PERSONELİN KENDİSİ oluşturur: global şablonu yalnız admin düzenleyebilir
        // (mevcut ve doğru iş kuralı — EnsureManageable). Senaryo kişisel şablon üzerinden kurulur.
        var sahip = Oturum("kac_personel");
        var tid = _sablon.Create(sahip, new NewMaterialTemplate("KAC Şablon", UnitPrice: 250m));
        FiyatiKoru();

        var s = Oturum("kac_personel");
        var t = _sablon.Get(s, tid)!;
        Assert.Equal(0m, t.UnitPrice);   // kullanıcı 0 görür

        _sablon.Update(s, tid, new NewMaterialTemplate("KAC Şablon yeni ad", UnitPrice: t.UnitPrice), t.Version);

        Assert.Equal(250m, HamSablonFiyati(tid));                          // 🔴 KORUNDU
        Assert.Equal("KAC Şablon yeni ad", _sablon.Get(s, tid)!.Name);     // istenen değişiklik oldu
    }

    // ══════════════════ KK6 — KAPI ÇİFT YÖNLÜ ══════════════════

    [Fact]
    public void KK6_Izin_Verilince_Fiyat_Yeniden_Gorunur()
    {
        var (id, _) = Hazirla();
        FiyatiKoru();
        Assert.Null(_stok.SearchMovements(Oturum("kac_personel"), null, null, null, 100)
            .Single(x => x.Code == "KAC-1").UnitPrice);

        _perms.SaveForUser(SuperAdmin(), _personelId, new[]
        {
            Tam("materials"), Tam("stock"), Tam("material_templates"),
            new ModulePermission(FiyatAnahtari, true, false, true, false),
        }, Array.Empty<string>());

        var s = Oturum("kac_personel");
        Assert.True(MaterialService.FiyatGorunur(s));
        Assert.Equal(123.45m, _stok.SearchMovements(s, null, null, null, 100)
            .Single(x => x.Code == "KAC-1").UnitPrice);
        _ = id;
    }

    // ══════════════════ KK7 — SMOKE ══════════════════

    /// <summary>
    /// Alan koruması MODÜL yetkisine dokunmaz: koruma açıkken kullanıcı stok ve malzeme
    /// modüllerini kullanmaya devam eder, yetkisiz modül yine kapalıdır (deny-by-default).
    /// </summary>
    [Fact]
    public void KK7_Alan_Korumasi_Modul_Yetkisini_Bozmaz()
    {
        FiyatiKoru();
        var s = Oturum("kac_personel");

        Assert.True(AccessControl.Can(s, "stock", PermissionAction.View));
        Assert.True(AccessControl.Can(s, "materials", PermissionAction.Create));
        Assert.False(AccessControl.Can(s, "vehicles", PermissionAction.View));   // deny-by-default sürüyor
        Assert.False(AccessControl.Can(s, "permissions", PermissionAction.Edit));
    }
}
