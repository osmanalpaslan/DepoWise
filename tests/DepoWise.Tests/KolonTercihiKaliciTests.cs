using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Settings;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 4.14 — KOLON SEÇİMİ HER LOGIN'DE KALICI (2026-09-06) ═══
///
/// <b>Kullanıcı isteği.</b> <i>"Kolonları ayarla butonundaki seçimleri kaydet dediğinde, kullanıcı
/// kolonda yeni bir değişiklik yapana kadar HER LOGIN'DE kaydettiği seçimler geçerli kalsın."</i>
///
/// Bu testler kalıcılık zincirinin her halkasını ayrı ayrı kilitler: kaydet → yeni oturum → oku,
/// başka tercihlerin (sayfa boyutu / sıralama / genişlik) kolonları EZMEMESİ, ve kullanıcı
/// ayrımı (bir kullanıcının tercihi diğerininkini etkilemez).
///
///  KT1 — Kaydedilen kolonlar YENİ oturumda aynen gelir
///  KT2 — 🔴 Sayfa boyutu kaydetmek kolon seçimini EZMEZ (ortak satır, ayrı alanlar)
///  KT3 — 🔴 Sıralama kaydetmek kolon seçimini EZMEZ
///  KT4 — Kolon seçimi KİŞİSELDİR (başka kullanıcıya sızmaz)
///  KT5 — Yeni seçim eskisinin yerine geçer (kullanıcı değiştirene kadar geçerli kalır)
///  KT6 — Katalogda olmayan kolon süzülür; hepsi geçersizse VARSAYILANA düşer (hayalet kolon yok)
/// </summary>
public class KolonTercihiKaliciTests : IDisposable
{
    private const string Co = "KLN";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly UserListPreferenceService _tercih;
    private readonly string _kullanici1, _kullanici2;

    public KolonTercihiKaliciTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_kolon_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");

        var users = new UserService(_f);
        _kullanici1 = users.EnsureInitialAdmin(Co, "kln_a", "Kln!2026", RoleKeys.CompanyAdmin);
        _kullanici2 = users.EnsureInitialAdmin(Co, "kln_b", "Kln!2026", RoleKeys.Staff);
        _tercih = new UserListPreferenceService(_f);
    }

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Yeni bir LOGIN'i temsil eden taze oturum (aynı kullanıcı, yeni SessionContext).</summary>
    private static SessionContext Oturum(string userId) =>
        new(userId, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }

    // ══════════════════ KT1 ══════════════════

    [Fact]
    public void KT1_Kaydedilen_Kolonlar_Yeni_Oturumda_Gelir()
    {
        var secim = new[] { "code", "name", "unit" };
        _tercih.SaveColumns(Oturum(_kullanici1), "materials", secim);

        // ⭐ YENİ OTURUM = yeni login
        var okunan = _tercih.GetColumns(Oturum(_kullanici1), "materials");

        Assert.NotNull(okunan);
        Assert.Equal(secim, okunan!);
    }

    // ══════════════════ KT2 / KT3 — DİĞER TERCİHLER EZMEMELİ ══════════════════

    /// <summary>
    /// 🔴 En sinsi kalıcılık hatası burada olurdu: sayfa boyutu/sıralama AYNI satıra yazılır
    /// (user_id, list_key). Upsert yanlış yazılsaydı kolon listesi sessizce '[]' olur ve kullanıcı
    /// "kolonlarım her login sıfırlanıyor" derdi. Bu test o davranışı kilitler.
    /// </summary>
    [Fact]
    public void KT2_Sayfa_Boyutu_Kolonlari_Ezmez()
    {
        var s = Oturum(_kullanici1);
        _tercih.SaveColumns(s, "vehicles", new[] { "internalCode", "plate" });

        _tercih.SavePageSize(s, "vehicles", 100);

        Assert.Equal(new[] { "internalCode", "plate" }, _tercih.GetColumns(Oturum(_kullanici1), "vehicles")!);
        Assert.Equal(100, _tercih.GetPageSize(Oturum(_kullanici1), "vehicles"));
    }

    [Fact]
    public void KT3_Siralama_Kolonlari_Ezmez()
    {
        var s = Oturum(_kullanici1);
        _tercih.SaveColumns(s, "vehicles", new[] { "internalCode", "plate" });

        _tercih.SaveSort(s, "vehicles", "plate", true);

        Assert.Equal(new[] { "internalCode", "plate" }, _tercih.GetColumns(Oturum(_kullanici1), "vehicles")!);
    }

    // ══════════════════ KT4 — KİŞİSEL ══════════════════

    [Fact]
    public void KT4_Kolon_Secimi_Kisiseldir()
    {
        _tercih.SaveColumns(Oturum(_kullanici1), "materials", new[] { "code" });

        Assert.Null(_tercih.GetColumns(Oturum(_kullanici2), "materials"));   // diğer kullanıcı etkilenmez
    }

    // ══════════════════ KT5 ══════════════════

    [Fact]
    public void KT5_Yeni_Secim_Eskisinin_Yerine_Gecer()
    {
        var s = Oturum(_kullanici1);
        _tercih.SaveColumns(s, "materials", new[] { "code", "name" });
        _tercih.SaveColumns(s, "materials", new[] { "code", "name", "stock" });

        Assert.Equal(new[] { "code", "name", "stock" }, _tercih.GetColumns(Oturum(_kullanici1), "materials")!);
    }

    // ══════════════════ KT6 — HAYALET KOLON ══════════════════

    [Fact]
    public void KT6_Katalog_Disi_Kolon_Suzulur()
    {
        // Katalogda gerçekten olan bir kolon + olmayan bir kolon
        var gecerli = MaterialListColumns.All[0].Key;
        var sonuc = MaterialListColumns.Sanitize(new[] { gecerli, "artik_olmayan_kolon" });

        Assert.Contains(gecerli, sonuc);
        Assert.DoesNotContain("artik_olmayan_kolon", sonuc);

        // Hiçbiri geçerli değilse VARSAYILANA düşülür (ekran boş kolonla açılmaz)
        Assert.Equal(MaterialListColumns.DefaultVisible, MaterialListColumns.Sanitize(new[] { "yok1", "yok2" }));
    }
}
