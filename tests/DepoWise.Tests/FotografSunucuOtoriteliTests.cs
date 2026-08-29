using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Files;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ADR-182 · S5 — FOTOĞRAF SUNUCU-OTORİTELİ + SİLME KAPISI ═══ (ARA İŞ 2, 2026-08-29)
///
/// <b>Kullanıcının bildirdiği sorun:</b> "Fotoğrafı başka bir makineden başka bir kullanıcı eklediğinde
/// ben aynı kaydı açtığımda göremiyorum."
/// <b>Kök neden:</b> masaüstü fotoğrafı yalnız KENDİ diskine + kendi yerel <c>file_records</c> tablosuna
/// yazıyordu; bu tablo senkronda YOKTUR ve ikili içerik hiçbir pakette taşınmaz → üç ayrı silo.
/// <b>Çözüm (PK-F1=A):</b> Evrak modülündeki "içerik sunucuda durur" deseni fotoğraflara da uygulandı;
/// masaüstü artık web ile AYNI uçları çağırır. Migration YOK, senkron sözleşmesi DEĞİŞMEDİ.
///
/// Bu sınıf (a) sunucu deposunda tek kaynak davranışını, (b) silme yetkisi kapısını, (c) tenant
/// izolasyonunu, (d) taşıma için gereken sha256 künyesini ve (e) iki platformun yerel-yazıma geri
/// dönemeyeceğini (kaynak-düzeyi kilit) doğrular.
/// </summary>
public class FotografSunucuOtoriteliTests : IDisposable
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
    private static readonly byte[] Png2 = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 8, 7 };

    private readonly string _dbPath, _filesRoot;
    private readonly SqliteConnectionFactory _factory;
    private readonly FileService _files;
    private readonly SessionContext _adminA, _adminB;

    public FotografSunucuOtoriteliTests()
    {
        var kok = Path.Combine(Path.GetTempPath(), "depowise_foto_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(kok);
        _filesRoot = Path.Combine(kok, "files");
        _dbPath = Path.Combine(kok, "test.db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _files = new FileService(_factory, new LocalFileStorageProvider(_filesRoot), new SabitSaat());

        var users = new UserService(_factory, new SabitSaat());
        var a = users.EnsureInitialAdmin("A", "admin_a", "admin123", RoleKeys.CompanyAdmin);
        _adminA = new SessionContext(a, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) " +
             "VALUES('B','Baska',1,1,1,0,5,5,2);");
        var b = users.EnsureInitialAdmin("B", "admin_b", "admin123", RoleKeys.CompanyAdmin);
        _adminB = new SessionContext(b, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        Exec("INSERT INTO materials(id,company_id,code,name,unit_id,min_stock,created_at,updated_at,version,is_deleted) " +
             "VALUES('M1','A','MK1','Çimento',NULL,'0',1,1,1,0);");
        Exec(@"INSERT INTO vehicles(id,company_id,internal_code,meter_unit,current_meter,created_at,updated_at,version,is_deleted)
               VALUES('V1','A','VA','km','0',1,1,1,0);");
    }

    private sealed class SabitSaat : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    // ══════════════ A) TEK KAYNAK — "başka makine/kullanıcı görebiliyor" ══════════════

    /// <summary>⭐ ASIL GÜVENCE: A kullanıcısının yüklediği fotoğrafı, AYNI firmadaki başka bir kullanıcı
    /// (başka makine) sunucudan okuyabilir. Fotoğraf tek bir yerde (sunucuda) durur.</summary>
    [Fact]
    public void FTG1_A_Kullanicisinin_Yukledigini_B_Kullanicisi_Gorur()
    {
        _files.SavePhoto(_adminA, "material", "M1", "foto.png", null, Png);

        var baskaMakine = Personel("materials");   // aynı firma, BAŞKA kullanıcı/makine
        var liste = _files.GetPhotos(baskaMakine, "material", "M1");
        Assert.Single(liste);
        // İçerik de tek yerde (sunucu deposunda) durur ve okunabilir.
        Assert.NotEmpty(new LocalFileStorageProvider(_filesRoot).Read(liste[0].StorageKey));
    }

    [Fact]
    public void FTG2_Arac_Fotografi_da_Ayni_Altyapiyi_Kullanir()
    {
        _files.SavePhoto(_adminA, "vehicle", "V1", "arac.png", null, Png);
        Assert.Single(_files.GetPhotos(Personel("vehicles"), "vehicle", "V1"));
    }

    /// <summary>Taşıma (PK-F5=A) için gereken içerik özeti künyede DOLU gelir — masaüstü yerelde kalmış
    /// eskileri sunucuya taşırken mükerrer yüklemeyi bununla önler.</summary>
    [Fact]
    public void FTG3_Sha256_Kunyede_Dolu_Gelir()
    {
        _files.SavePhoto(_adminA, "material", "M1", "foto.png", null, Png);
        var p = _files.GetPhotos(_adminA, "material", "M1").Single();
        Assert.False(string.IsNullOrWhiteSpace(p.Sha256));
        Assert.Equal(64, p.Sha256!.Length);        // SHA-256 onaltılık
    }

    /// <summary>Farklı içerikler farklı özet üretir → taşıma sırasında "aynı foto" yanlışlıkla atlanmaz.</summary>
    [Fact]
    public void FTG4_Farkli_Icerik_Farkli_Ozet()
    {
        _files.SavePhoto(_adminA, "material", "M1", "a.png", null, Png);
        _files.SavePhoto(_adminA, "material", "M1", "b.png", null, Png2);
        var ozetler = _files.GetPhotos(_adminA, "material", "M1").Select(x => x.Sha256).ToList();
        Assert.Equal(2, ozetler.Count);
        Assert.Equal(2, ozetler.Distinct().Count());
    }

    // ══════════════ B) SİLME KAPISI (PK-F3) ══════════════

    /// <summary>⭐ Sunucu SİLME yetkisi ister. Düzenleme yetkisi olup silme yetkisi OLMAYAN kullanıcı
    /// silemez — arayüzdeki düğme de bu yüzden artık <c>CanDeletePhoto</c>'ya bağlıdır (eskiden
    /// <c>CanEdit</c>'e bağlıydı ve kullanıcı düğmeyi görüp hata alıyordu).</summary>
    [Fact]
    public void FTG5_Silme_Duzenleme_Yetkisiyle_Yapilamaz()
    {
        var id = _files.SavePhoto(_adminA, "material", "M1", "foto.png", null, Png).Id;

        var duzenleyen = new SessionContext("u-edit", "A", new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("materials", true, true, true, false) }, Array.Empty<string>()));
        Assert.Throws<ForbiddenException>(() => _files.DeletePhoto(duzenleyen, id));
        Assert.Single(_files.GetPhotos(_adminA, "material", "M1"));   // duruyor

        var silebilen = new SessionContext("u-del", "A", new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("materials", true, true, true, true) }, Array.Empty<string>()));
        _files.DeletePhoto(silebilen, id);
        Assert.Empty(_files.GetPhotos(_adminA, "material", "M1"));
    }

    /// <summary>Yükleme DÜZENLEME yetkisi ister (silme yetkisi gerekmez) — kapılar ayrıdır.</summary>
    [Fact]
    public void FTG6_Yukleme_Duzenleme_Yetkisi_Ister()
    {
        var okuyan = new SessionContext("u-view", "A", new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("materials", true, false, false, false) }, Array.Empty<string>()));
        Assert.Throws<ForbiddenException>(() => _files.SavePhoto(okuyan, "material", "M1", "f.png", null, Png));
    }

    // ══════════════ C) TENANT ══════════════

    [Fact]
    public void FTG7_Baska_Firma_Fotografi_Gormez_Silemez()
    {
        var id = _files.SavePhoto(_adminA, "material", "M1", "foto.png", null, Png).Id;
        Assert.Empty(_files.GetPhotos(_adminB, "material", "M1"));           // B firması göremez
        Assert.Throws<ForbiddenException>(() => _files.DeletePhoto(_adminB, id));   // ve silemez
        Assert.Single(_files.GetPhotos(_adminA, "material", "M1"));          // A'nın fotoğrafı duruyor
    }

    // ══════════════ D) KAYNAK-DÜZEYİ KİLİTLER (yerel-yazıma geri dönülemez) ══════════════

    /// <summary>⭐ Masaüstü fotoğrafı ARTIK yerele yazmaz: iki ekran da ortak sunucu katmanını kullanır.
    /// Bu kilit düşerse hata (üç ayrı silo) geri gelmiş demektir.</summary>
    [Fact]
    public void FTG8_Masaustu_Yerele_Yazmaz_Sunucu_Katmanini_Kullanir()
    {
        var (mat, arac, matView, aracView) = MasaustuKaynaklari();

        foreach (var vm in new[] { mat, arac })
        {
            Assert.DoesNotContain("DesktopServices.Files.SavePhoto", vm, StringComparison.Ordinal);
            Assert.DoesNotContain("DesktopServices.Files.DeletePhoto", vm, StringComparison.Ordinal);
            Assert.Contains("DesktopPhotos.KaydetAsync", vm, StringComparison.Ordinal);
            Assert.Contains("DesktopPhotos.SilAsync", vm, StringComparison.Ordinal);
            Assert.Contains("DesktopPhotos.YukleAsync", vm, StringComparison.Ordinal);
        }

        // PK-F3: silme düğmesi düzenleme moduna + SİLME yetkisine bağlı (eskiden CanEdit idi).
        foreach (var view in new[] { matView, aracView })
        {
            Assert.Contains("CanDeletePhoto", view, StringComparison.Ordinal);
            Assert.Contains("DeleteDetailPhotoCommand", view, StringComparison.Ordinal);
        }
    }

    /// <summary>⭐ Web kayıtlı fotoğrafları GÖSTERİR (eskiden yüklüyor ama hiç çizmiyordu) ve silme
    /// düğmesi SİLME yetkisine bağlıdır.</summary>
    [Fact]
    public void FTG9_Web_Kayitli_Fotograflari_Gosterir_ve_Silme_Yetkiye_Bagli()
    {
        var kok = RepoKok();
        var malzeme = File.ReadAllText(Path.Combine(kok, "src", "DepoWise.Web", "Components", "Pages", "Materials.razor"));
        var arac = File.ReadAllText(Path.Combine(kok, "src", "DepoWise.Web", "Components", "Pages", "Vehicles.razor"));

        Assert.Contains("Kayıtlı fotoğraflar", malzeme, StringComparison.Ordinal);
        Assert.Contains("Kayıtlı fotoğraflar", arac, StringComparison.Ordinal);
        Assert.Contains("Auth.CanDelete(\"materials\")", malzeme, StringComparison.Ordinal);
        Assert.Contains("Auth.CanDelete(\"vehicles\")", arac, StringComparison.Ordinal);
        Assert.Contains("DeletePhoto(p.Id)", malzeme, StringComparison.Ordinal);
        Assert.Contains("DeletePhoto(p.Id)", arac, StringComparison.Ordinal);
    }

    /// <summary>Senkron sözleşmesine DOKUNULMADI: fotoğraf künyeleri hâlâ iş senkronunda YOK
    /// (ikili içerik pakete girmez — çözüm sunucu-otoriteliktir, senkron değil).</summary>
    [Fact]
    public void FTG10_Senkron_Sozlesmesi_Degismedi()
        => Assert.DoesNotContain("file_records", DepoWise.Infrastructure.Sync.BusinessSyncService.Tables);

    // ══════════════ Yardımcılar ══════════════

    private static string RepoKok()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);
        return kok!.FullName;
    }

    private static (string Mat, string Arac, string MatView, string AracView) MasaustuKaynaklari()
    {
        var k = RepoKok();
        return (File.ReadAllText(Path.Combine(k, "src", "DepoWise.Desktop", "ViewModels", "MaterialsViewModel.cs")),
                File.ReadAllText(Path.Combine(k, "src", "DepoWise.Desktop", "ViewModels", "VehiclesViewModel.cs")),
                File.ReadAllText(Path.Combine(k, "src", "DepoWise.Desktop", "Views", "MaterialsView.axaml")),
                File.ReadAllText(Path.Combine(k, "src", "DepoWise.Desktop", "Views", "VehiclesView.axaml")));
    }

    private static SessionContext Personel(params string[] moduller)
        => new("u-p", "A", new[] { RoleKeys.Staff },
            new PermissionSet(moduller.Select(m => new ModulePermission(m, true, true, true, true)).ToArray(), Array.Empty<string>()));

    private void Exec(string sql)
    {
        using var c = _factory.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_dbPath)!, recursive: true); } catch { }
    }
}
