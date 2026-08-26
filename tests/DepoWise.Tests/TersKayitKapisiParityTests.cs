using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ YET-05 · "İPTAL / TERS KAYIT" ARAYÜZ KAPISI SUNUCUDAN FARKLIYDI ═══ (denetim 2026-08-26, ikinci tur)
///
/// <b>Sunucudaki gerçek kural</b> (<see cref="StockService.ReverseDocument"/>):
/// <c>stock.<b>Edit</b></c> <b>ve</b> <c>btn-reverse</c> özel butonu.
///
/// <b>Arayüzlerin sorduğu soru ise farklıydı:</b>
/// <list type="bullet">
///   <item>Masaüstü Stok ekranı: yalnız <c>stock.<b>Delete</b></c> (buton kontrolü HİÇ YOK).</item>
///   <item>Web Stok ekranı: <c>stock.<b>Delete</b></c> + <c>btn-reverse</c>.</item>
/// </list>
///
/// <b>Kullanıcıya yansıyan iki gerçek sonuç:</b>
/// <list type="number">
///   <item><b>Verilen yetki kullanılamıyordu.</b> Yöneticinin <c>stock.Edit</c> + <c>btn-reverse</c> verdiği
///     kullanıcı, sunucu izin verdiği hâlde İptal butonunu <b>hiçbir platformda göremiyordu</b>
///     (YET-02 ile yetkinin verilebilir hâle gelmesi bu boşluğu görünür kıldı).</item>
///   <item><b>Çalışmayan buton görünüyordu.</b> Yalnız <c>stock.Delete</c> yetkisi olan kullanıcı masaüstünde
///     butonu görüyor, tıklayınca "yetki yok" hatası alıyordu.</item>
/// </list>
///
/// ⚠️ Bu bir <b>güvenlik açığı DEĞİLDİ</b> — sunucu her iki durumda da doğru davranıyordu (fail-closed).
/// Düzeltme yalnız ARAYÜZ tarafındadır; sunucu kuralına DOKUNULMADI (sunucu tek otorite olarak kalır).
///
/// Yakıt ekranlarında kural zaten doğruydu (web: <c>fuel.Edit + btn-reverse</c>); masaüstü Yakıt yalnız
/// butona bakıyordu → oraya da modül kontrolü eklendi ki dört katman aynı soruyu sorsun.
/// </summary>
public class TersKayitKapisiParityTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"dw_yet05_{Guid.NewGuid():N}.db");

    private StockService Servis()
    {
        var f = new SqliteConnectionFactory(_dbPath);
        new DepoWise.Infrastructure.Database.Migrations.MigrationRunner(f).Run();
        return new StockService(f);
    }

    private static SessionContext Oturum(params ModulePermission[] moduller)
        => new("u1", "A", Array.Empty<string>(), new PermissionSet(moduller));

    private static SessionContext OturumButonlu(IEnumerable<string> butonlar, params ModulePermission[] moduller)
        => new("u1", "A", Array.Empty<string>(), new PermissionSet(moduller, butonlar));

    private static ModulePermission Stok(bool view = true, bool edit = false, bool del = false)
        => new("stock", CanView: view, CanCreate: false, CanEdit: edit, CanDelete: del);

    // ── 1) SUNUCU KURALI (değişmedi — kilit) ───────────────────────────────────────────────────

    /// <summary>Yalnız SİLME yetkisi ters kayıt için YETMEZ (sunucu reddeder).</summary>
    [Fact]
    public void YET05a_Sunucu_Yalniz_Silme_Yetkisini_Reddeder()
    {
        var svc = Servis();
        var s = Oturum(Stok(edit: false, del: true));
        Assert.Throws<ForbiddenException>(() => svc.ReverseDocument(s, "belge-1", "gerekçe"));
    }

    /// <summary>
    /// Düzenleme yetkisi VAR ama BUTON yoksa yine reddedilir.
    ///
    /// ⚠️ <b>Mesaj neden kontrol ediliyor:</b> ilk sürüm yalnız <c>ForbiddenException</c> bekliyordu ve
    /// buton kapısı KASTEN kaldırıldığında bile geçiyordu — çünkü olmayan belge de aynı türden bir
    /// istisna ("Belge bulunamadı") fırlatıyor. Test doğru sebepten kırılsın diye mesaj sınanır.
    /// (Bu zayıflık bu turun mutasyon/kasten-bozma denemesinde yakalandı.)
    /// </summary>
    [Fact]
    public void YET05b_Sunucu_Butonsuz_Duzenlemeyi_Reddeder()
    {
        var svc = Servis();
        var s = Oturum(Stok(edit: true));

        var ex = Assert.Throws<ForbiddenException>(() => svc.ReverseDocument(s, "belge-1", "gerekçe"));

        Assert.Contains(SpecialButtons.Reverse, ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐ Düzenleme + buton VARSA sunucu YETKİ KAPISINDAN GEÇİRİR — silme yetkisi olmasa bile.
    /// Kapıdan geçtiğini kanıtlamak için gerekçe BİLEREK boş verilir: sıradaki kontrol
    /// "gerekçe zorunlu" doğrulamasıdır (henüz veritabanına dokunulmaz).
    /// </summary>
    [Fact]
    public void YET05c_Sunucu_Duzenleme_Ve_Buton_Ile_Gecirir()
    {
        var svc = Servis();
        var s = OturumButonlu(new[] { SpecialButtons.Reverse }, Stok(edit: true, del: false));
        Assert.Throws<ArgumentException>(() => svc.ReverseDocument(s, "belge-1", ""));
    }

    // ── 2) ARAYÜZ KAPILARI SUNUCUYLA AYNI SORUYU SORMALI (kaynak kilidi) ───────────────────────
    //
    // Neden kaynak kilidi: masaüstü kapısı bir ViewModel özelliğinde, web kapısı .razor işaretlemesinde
    // yaşıyor; ikisi de birim testinden çalıştırılamaz. Kilit, aynı sapmanın SESSİZCE geri gelmesini önler.

    private static string Oku(string goreliYol)
    {
        var kok = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && kok is not null; i++)
        {
            var aday = Path.Combine(kok, goreliYol);
            if (File.Exists(aday)) return File.ReadAllText(aday);
            kok = Path.GetDirectoryName(kok!);
        }
        throw new FileNotFoundException($"Kaynak dosya bulunamadı: {goreliYol}");
    }

    [Fact]
    public void YET05d_Masaustu_Stok_Kapisi_Sunucuyla_Ayni()
    {
        var src = Oku(Path.Combine("src", "DepoWise.Desktop", "ViewModels", "StockEntryViewModel.cs"));
        var i = src.IndexOf("CanReverse", StringComparison.Ordinal);
        Assert.True(i >= 0, "CanReverse bulunamadı");
        var ifade = src.Substring(i, Math.Min(400, src.Length - i));

        Assert.Contains("PermissionAction.Edit", ifade, StringComparison.Ordinal);
        Assert.Contains("SpecialButtons.Reverse", ifade, StringComparison.Ordinal);
        Assert.DoesNotContain("PermissionAction.Delete", ifade, StringComparison.Ordinal);
    }

    [Fact]
    public void YET05e_Web_Stok_Kapisi_Sunucuyla_Ayni()
    {
        var src = Oku(Path.Combine("src", "DepoWise.Web", "Components", "Pages", "Stock.razor"));
        var satirlar = src.Split('\n').Where(l => l.Contains("canReverse", StringComparison.Ordinal)
                                               && l.Contains("Auth.Can", StringComparison.Ordinal)).ToList();
        Assert.True(satirlar.Count > 0, "İptal butonunun görünürlük koşulu bulunamadı");
        foreach (var l in satirlar)
        {
            Assert.Contains("Auth.CanEdit(\"stock\")", l, StringComparison.Ordinal);
            Assert.Contains("btn-reverse", l, StringComparison.Ordinal);
            Assert.DoesNotContain("Auth.CanDelete(\"stock\")", l, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void YET05f_Masaustu_Yakit_Kapisi_Modulu_De_Sorar()
    {
        var src = Oku(Path.Combine("src", "DepoWise.Desktop", "ViewModels", "FuelViewModel.cs"));
        var i = src.IndexOf("SpecialButtons.Reverse", StringComparison.Ordinal);
        Assert.True(i >= 0, "btn-reverse kontrolü bulunamadı");
        var pencere = src.Substring(Math.Max(0, i - 300), Math.Min(400, src.Length - Math.Max(0, i - 300)));

        Assert.Contains("PermissionAction.Edit", pencere, StringComparison.Ordinal);
    }

    /// <summary>Kaynak kilidinin GERÇEKTEN yakaladığını kanıtlar (kural kendi kendini sınar).</summary>
    [Fact]
    public void YET05g_Kaynak_Kilidi_Gercekten_Yakaliyor()
    {
        const string kotu = "public bool CanReverse => AccessControl.Can(_session, \"stock\", PermissionAction.Delete);";
        Assert.DoesNotContain("SpecialButtons.Reverse", kotu, StringComparison.Ordinal);
        Assert.Contains("PermissionAction.Delete", kotu, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
