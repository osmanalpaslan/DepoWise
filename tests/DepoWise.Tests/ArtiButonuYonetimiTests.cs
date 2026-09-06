using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Settings;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 4.6 — SATIR İÇİ "+" (HIZLI TANIM EKLEME) YÖNETİMİ (2026-09-06) ═══
///
/// <b>Kullanıcının kalan isteği.</b> Serbest metni sabit tanımlıya çevirme İPTAL edildi; geriye
/// <i>"sabit tanımlı alanların yanına '+' ekleyip kaldırabileceğim bir yer"</i> kaldı. Ayar,
/// Alan Ayarları ekranına yerleştirildi (yeni ekran açılmadı).
///
/// <b>Değişmezler.</b> Yeni yetki motoru YOK (mevcut <c>btn-add-lookup</c> aynen duruyor; bu onun
/// ÜSTÜNE binen bir FİRMA ayarıdır). MIGRATION YOK (app_settings anahtarı). Kayıt yoksa AÇIK →
/// hiçbir firmada bugünkü davranış değişmez.
///
///  AB1 — Varsayılan AÇIK (geri uyumluluk: hiçbir firma etkilenmez)
///  AB2 — Kapatılınca satır içi "+" ekleme SERVİSTE reddedilir
///  AB3 — 🔴 Kapatmak "Tanım Düzenle" yolunu ENGELLEMEZ (firma kendi tanımını ekleyemez duruma düşmez)
///  AB4 — Ayar FİRMAYA özeldir (başka firmaya sızmaz)
///  AB5 — Yeniden açılınca "+" tekrar çalışır (çift yönlü)
///  AB6 — Katalog yalnız SABİT TANIM alanlarını içerir (personel/şube/bakım tanımı YOK)
/// </summary>
public class ArtiButonuYonetimiTests : IDisposable
{
    private const string CoA = "ART";
    private const string CoB = "ARTB";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly LookupService _lookups;
    private readonly SettingsService _ayarlar;
    private readonly SessionContext _a, _b;

    public ArtiButonuYonetimiTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_arti_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        foreach (var co in new[] { CoA, CoB })
            Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{co}','{co}',1,1,1,0);");

        var users = new UserService(_f);
        _a = new SessionContext(users.EnsureInitialAdmin(CoA, "art_a", "Art!2026", RoleKeys.CompanyAdmin), CoA,
            new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _b = new SessionContext(users.EnsureInitialAdmin(CoB, "art_b", "Art!2026", RoleKeys.CompanyAdmin), CoB,
            new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _lookups = new LookupService(_f);
        _ayarlar = new SettingsService(_f);
    }

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void ArtiKapat(SessionContext s, string tablo)
        => _ayarlar.Set(s.CompanyId, LookupPlusCatalog.Key(tablo), LookupPlusCatalog.Kapali, s.UserId);

    private void ArtiAc(SessionContext s, string tablo)
        => _ayarlar.Set(s.CompanyId, LookupPlusCatalog.Key(tablo), "1", s.UserId);

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }

    // ══════════════════ AB1 ══════════════════

    [Fact]
    public void AB1_Varsayilan_Acik()
    {
        Assert.True(_lookups.QuickAddEnabled(_a, "units"));
        var id = _lookups.AddUnit(_a, "adet", quick: true);   // satır içi yol serbest
        Assert.False(string.IsNullOrEmpty(id));
    }

    // ══════════════════ AB2 ══════════════════

    [Fact]
    public void AB2_Kapaliyken_Satir_Ici_Ekleme_Reddedilir()
    {
        ArtiKapat(_a, "units");

        Assert.False(_lookups.QuickAddEnabled(_a, "units"));
        var ex = Assert.Throws<ForbiddenException>(() => _lookups.AddUnit(_a, "kg", quick: true));
        Assert.Contains("Tanım Düzenle", ex.Message);   // kullanıcıya ÇIKIŞ YOLU söylenir
    }

    // ══════════════════ AB3 — EN ÖNEMLİ ══════════════════

    /// <summary>
    /// 🔴 Kapatmak yönetim yolunu da kapatsaydı firma kendi tanımını hiçbir yerden ekleyemezdi
    /// (çözümsüzlük). "Tanım Düzenle" ekranı quick GÖNDERMEZ → her zaman çalışır.
    /// </summary>
    [Fact]
    public void AB3_Tanim_Duzenle_Yolu_Engellenmez()
    {
        ArtiKapat(_a, "units");

        var id = _lookups.AddUnit(_a, "litre");   // quick DEĞİL → yönetim yolu
        Assert.False(string.IsNullOrEmpty(id));
    }

    // ══════════════════ AB4 ══════════════════

    [Fact]
    public void AB4_Ayar_Firmaya_Ozeldir()
    {
        ArtiKapat(_a, "brands");

        Assert.False(_lookups.QuickAddEnabled(_a, "brands"));
        Assert.True(_lookups.QuickAddEnabled(_b, "brands"));            // diğer firma etkilenmez
        Assert.False(string.IsNullOrEmpty(_lookups.AddBrand(_b, "Bosch", "material", quick: true)));
    }

    // ══════════════════ AB5 ══════════════════

    [Fact]
    public void AB5_Yeniden_Acilinca_Calisir()
    {
        ArtiKapat(_a, "suppliers");
        Assert.Throws<ForbiddenException>(() => _lookups.AddSupplier(_a, "ABC", quick: true));

        ArtiAc(_a, "suppliers");

        Assert.False(string.IsNullOrEmpty(_lookups.AddSupplier(_a, "ABC", quick: true)));
    }

    // ══════════════════ AB6 ══════════════════

    /// <summary>Katalog yalnız TANIM LİSTESİ alanlarını içerir; personel/şube/bakım tanımı kendi
    /// ekranı ve yetkisi olan modüllerdir — buraya girerlerse yanlış yerden yönetiliyor sanılır.</summary>
    [Fact]
    public void AB6_Katalog_Yalniz_Sabit_Tanimlari_Icerir()
    {
        Assert.Contains(LookupPlusCatalog.All, x => x.Table == "units");
        Assert.Contains(LookupPlusCatalog.All, x => x.Table == "vehicle_models");
        Assert.DoesNotContain(LookupPlusCatalog.All, x => x.Table is "personnel" or "branches" or "maintenance_definitions");
    }
}
