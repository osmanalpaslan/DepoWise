using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ TDR-01 (kullanıcı isteği 2026-09-05) — GİRİŞ-ÇIKIŞ EKRANINDA TEDARİKÇİ "+" ═══
///
/// <b>İki ortam da ölçüldü — kusur YALNIZ MASAÜSTÜNDEYDİ:</b>
/// <list type="bullet">
///   <item><b>Web</b> (<c>Stock.razor</c>): Tedarikçi, Birim, Kategori ve Marka kutularında satır içi
///     "+" ZATEN vardı.</item>
///   <item><b>Masaüstü</b> (<c>StockEntryView.axaml</c>): bu ekranda hiç "+" yoktu. Kullanıcı yeni bir
///     tedarikçiyle karşılaşınca formu bırakıp Malzemeler ekranına gitmek zorunda kalıyor, döndüğünde
///     girdiği veriler kaybolmuş oluyordu.</item>
/// </list>
///
/// <b>Ayrıca bulunan parite farkı:</b> masaüstü "+" düğmesini yetkisiz kullanıcıdan GİZLİYOR
/// (<c>CanAddLookup</c>), web ise HERKESE gösteriyordu; yetkisiz kullanıcı diyaloğu açıp adı yazıyor
/// ve ancak kaydederken hata alıyordu. Güvenlik açığı DEĞİLDİ (gerçek kapı serviste), ama
/// CLAUDE.md §5'in "UI ≡ API" kuralını çiğniyordu → web de aynı kapıya bağlandı.
///
///  TDR1 — Masaüstünde "+" ve satır içi ekleme kutusu VAR, yetkiye bağlı
///  TDR2 — Masaüstü görünüm modelinde komutlar var ve ortak servisi çağırıyor
///  TDR3 — Web'de "+" yetki kapısına bağlı (artık herkese görünmüyor)
///  TDR4 — Gerçek kapı SERVİSTE: yetkisiz kullanıcı tanım ekleyemez
///  TDR5 — Aynı ad ikinci kez eklenince YENİ kayıt açılmaz (tek Tanım ID)
/// </summary>
public class TedarikciHizliEklemeTests : IDisposable
{
    private const string Co = "TDR";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _admin;

    public TedarikciHizliEklemeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_tdr_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Oku(params string[] p)
        => File.ReadAllText(Path.Combine(new[] { RepoKok() }.Concat(p).ToArray()));

    // ══════════════ ARAYÜZ SÖZLEŞMESİ ══════════════

    [Fact]
    public void TDR1_Masaustunde_Arti_Ve_Satir_Ici_Kutu_Var()
    {
        var gorunum = Oku("src", "DepoWise.Desktop", "Views", "StockEntryView.axaml");

        // Tedarikçi bloğunun tamamı alınır — "+" başka bir alana ait olmasın.
        var bas = gorunum.IndexOf("Label=\"Tedarikçi\"", StringComparison.Ordinal);
        Assert.True(bas > 0, "Giriş-Çıkış ekranında Tedarikçi alanı bulunamadı.");
        var blok = gorunum[bas..Math.Min(gorunum.Length, bas + 2000)];

        Assert.Contains("Content=\"+\"", blok);
        Assert.Contains("StartAddSupplierCommand", blok);
        Assert.Contains("ConfirmAddSupplierCommand", blok);
        Assert.Contains("CancelAddSupplierCommand", blok);
        // ⭐ FAZ 4.6 (kullanıcı isteği 2026-09-06): görünürlük artık TABLO BAZLI bağlanır
        // ({Binding [suppliers]}). Bu, yetki kapısını GEVŞETMEZ — indeksleyici önce CanAddLookup
        // yetkisine bakar, sonra firmanın "+" ayarına. Yani yetki kuralı aynen sürüyor, üstüne
        // firma bazlı bir kapatma imkânı eklendi (ViewModelBase.this[string]).
        Assert.Contains("IsVisible=\"{Binding [suppliers]}\"", blok);
        // Ekleme kutusu yalnız "+" tıklanınca açılır (form kalabalıklaşmasın).
        Assert.Contains("IsVisible=\"{Binding IsAddingSupplier}\"", blok);
    }

    [Fact]
    public void TDR2_Masaustu_Gorunum_Modelinde_Komutlar_Var()
    {
        var vm = Oku("src", "DepoWise.Desktop", "ViewModels", "StockEntryViewModel.cs");
        Assert.Contains("private void StartAddSupplier()", vm);
        Assert.Contains("private void CancelAddSupplier()", vm);
        Assert.Contains("private void ConfirmAddSupplier()", vm);
        // Yeni bir yol icat edilmedi: Malzemeler ekranıyla AYNI ortak servis çağrılıyor.
        Assert.Contains("DesktopServices.Lookups.AddSupplier(_session,", vm);
    }

    [Fact]
    public void TDR3_Webde_Arti_Yetki_Kapisina_Bagli()
    {
        var web = Oku("src", "DepoWise.Web", "Components", "Pages", "Stock.razor");

        Assert.Contains("Auth.CanButton(\"btn-add-lookup\")", web);
        Assert.Contains("Adornment=\"@LkEkleSuslemesi\"", web);
        // Eski hâli: süsleme koşulsuz açıktı → yetkisiz kullanıcıya da görünüyordu.
        Assert.DoesNotContain("Adornment=\"Adornment.End\" AdornmentIcon=\"@Icons.Material.Filled.Add\"", web);

        // ⭐ Ortak seçim bileşeni de aynı kapıya bağlı olmalı — aksi hâlde web KENDİ İÇİNDE tutarsız
        // kalırdı: Stok ekranında gizli, Malzemeler ekranında (LookupSelect) görünür.
        var bilesen = Oku("src", "DepoWise.Web", "Components", "LookupSelect.razor");
        Assert.Contains("Auth.CanButton(\"btn-add-lookup\")", bilesen);
    }

    /// <summary>
    /// ⭐ Kullanıcı isteği: aynı desen ekrandaki DİĞER tanım alanlarına da uygulandı.
    /// Tek bir kutuda "+" olup komşularında olmaması tutarsız görünüyordu; web'de Birim/Kategori/Marka'da
    /// "+" zaten vardı, masaüstünde hiçbirinde yoktu. Artık BEŞ alan da iki platformda "+" taşıyor.
    /// </summary>
    [Fact]
    public void TDR6_Bes_Tanim_Alani_Da_Iki_Platformda_Arti_Tasiyor()
    {
        var masaustu = Oku("src", "DepoWise.Desktop", "Views", "StockEntryView.axaml");

        foreach (var (alan, komut) in new[]
                 {
                     ("Birim", "StartAddUnitCommand"),
                     ("Kategori", "StartAddCategoryCommand"),
                     ("Alt Kategori", "StartAddSubCategoryCommand"),
                     ("Marka", "StartAddBrandCommand"),
                     ("Tedarikçi", "StartAddSupplierCommand"),
                 })
        {
            Assert.True(masaustu.Contains(komut, StringComparison.Ordinal),
                $"Masaüstünde '{alan}' alanının \"+\" komutu ({komut}) bağlanmamış.");
        }

        // Masaüstünde beş "+" düğmesinin hepsi görünürlük kapısına bağlı.
        // ⭐ FAZ 4.6 (kullanıcı isteği 2026-09-06): kapı artık TABLO BAZLI ({Binding [units]} gibi).
        // Yetki GEVŞEMEDİ — indeksleyici (ViewModelBase.this[string]) önce CanAddLookup yetkisine
        // bakar, sonra firmanın "+" ayarına; ayar yoksa eski davranış aynen sürer.
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(
            masaustu, "Content=\"\\+\" IsVisible=\"\\{Binding \\[[a-z_]+\\]\\}\"").Count);

        // Web: beş kutunun beşi de aynı süslemeyi kullanıyor (Alt Kategori bu turda eklendi).
        var web = Oku("src", "DepoWise.Web", "Components", "Pages", "Stock.razor");
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(
            web, "Adornment=\"@LkEkleSuslemesi\"").Count);
        // Alt kategori ÜST kayda bağlı olduğu için ayrı uca gider.
        Assert.Contains("OnAdornmentClick=\"AddAltKategori\"", web);
    }

    /// <summary>
    /// ⭐ ALT KATEGORİ SAHİPSİZ KALMAMALI. Genel <c>/api/lookups/material_categories</c> ucu
    /// <c>parentId</c> taşımaz; alt kategori oradan eklenseydi ÜST kategori olarak açılır ve kullanıcı
    /// "eklediğim alt kategori listede yok" derdi. Servis düzeyinde doğru davranışı kilitler.
    /// </summary>
    [Fact]
    public void TDR7_Alt_Kategori_Ust_Kategoriye_Bagli_Eklenir()
    {
        var lookups = new LookupService(_f);

        var ust = lookups.AddCategory(_admin, "İnşaat Malzemeleri");
        var alt = lookups.AddCategory(_admin, "Çimento", ust);

        Assert.NotEqual(ust, alt);
        // Alt kategori ÜST seviyede görünmemeli...
        Assert.DoesNotContain(lookups.ListCategories(_admin), c => c.Id == alt);
        // ...ama üst kategorinin çocukları arasında görünmeli.
        Assert.Contains(lookups.ListCategories(_admin, ust), c => c.Id == alt);

        // Web diyaloğu üst kaydı taşıyabiliyor olmalı (aksi hâlde arayüz yanlış uca giderdi).
        var diyalog = Oku("src", "DepoWise.Web", "Components", "AddLookupDialog.razor");
        Assert.Contains("ParentId", diyalog);
        Assert.Contains("/api/materials/subcategories", diyalog);
    }

    // ══════════════ GERÇEK KAPI: SERVİS ══════════════

    /// <summary>
    /// ⭐ Butonun gizlenmesi bir KOLAYLIKTIR, güvenlik değildir. Yetkisiz oturum servisi doğrudan
    /// çağırsa bile tanım eklenememelidir — masaüstü bu servisi ÇEVRİMDIŞI da çağırır.
    /// </summary>
    [Fact]
    public void TDR4_Yetkisiz_Kullanici_Tanim_Ekleyemez()
    {
        var yetkisiz = new SessionContext("u-yetkisiz", Co, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        var lookups = new LookupService(_f);

        Assert.ThrowsAny<Exception>(() => lookups.AddSupplier(yetkisiz, "Gizlice Eklenen"));
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM suppliers WHERE company_id='" + Co + "';"));
    }

    /// <summary>Aynı ad iki kez eklenirse ikinci satır AÇILMAZ — "+" düğmesi hızlı olduğu için
    /// kullanıcı aynı tedarikçiyi kolayca iki kez yazabilir; katalog mükerrer kayıtla dolmamalı.</summary>
    [Fact]
    public void TDR5_Ayni_Ad_Ikinci_Kayit_Acmaz()
    {
        var lookups = new LookupService(_f);

        var ilk = lookups.AddSupplier(_admin, "Akın Nakliyat");
        var ikinci = lookups.AddSupplier(_admin, "  Akın Nakliyat  ");   // kenar boşluğu da aynı sayılır

        Assert.Equal(ilk, ikinci);
        Assert.Equal(1L, Say("SELECT COUNT(*) FROM suppliers WHERE company_id='" + Co + "';"));
    }

    private long Say(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
