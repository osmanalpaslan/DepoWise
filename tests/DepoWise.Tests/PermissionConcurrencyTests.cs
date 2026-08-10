using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// KLT-01c — Yetki kaydetmede düzenleme kilidi (2026-08-10).
///
/// Sorun: <see cref="PermissionService.SaveForUser"/> "DELETE hepsi + INSERT hepsi" ile çalışıyordu ve
/// sürüm kontrolü YOKTU. İki yönetici aynı kullanıcının yetki ekranını açtığında, ikinci kaydeden
/// birincinin verdiği yetkileri SESSİZCE siliyordu (kimse uyarı almıyordu). Güvenlik etkisi vardı:
/// verilmiş bir yetki farkında olunmadan geri alınabiliyordu.
///
/// Çözüm: yetki kümesinin sürüm jetonu <c>users.version</c>. Kolon şemada zaten vardı ama hiç
/// artırılmıyordu; okuyucusu ve senkron bağımlılığı olmadığı için MIGRATION GEREKMEDİ.
/// <c>user_permissions</c> satır sürümü işe yaramazdı — satırlar zaten silinip yeniden yazılıyor.
/// </summary>
public class PermissionConcurrencyTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly PermissionService _perms;
    private readonly UserService _users;

    public PermissionConcurrencyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_klt01c_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _perms = new PermissionService(_factory, _clock);
        _users = new UserService(_factory, _clock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    /// <summary>Firma yöneticisi oturumu (yetki ekranını kullanabilen aktör).</summary>
    private SessionContext Admin(string company)
    {
        var id = _users.EnsureInitialAdmin(company, "admin_" + company, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(id, company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    /// <summary>Yetkileri düzenlenecek HEDEF kullanıcı (admin DEĞİL — admine granular yetki uygulanmaz).</summary>
    private string TargetUser(SessionContext admin, string username)
        => _users.CreateUser(admin, new NewUser(username, "parola123", username, new[] { RoleKeys.Staff }));

    private static ModulePermission Mod(string key, bool v = false, bool c = false, bool e = false, bool d = false)
        => new(key, v, c, e, d);

    private static bool Has(UserPermissionData data, string moduleKey, Func<ModulePermission, bool> flag)
        => data.Modules.Any(m => m.ModuleKey == moduleKey && flag(m));

    // ───────────── 1-6: iki yönetici, aynı sürüm, ikincisi ezemez ─────────────

    [Fact]
    public void IkiYonetici_AyniSurumle_IkincisiEzemez_VeBirincininYetkisiKorunur()
    {
        var a = Admin("A");
        var u = TargetUser(a, "hedef1");

        // (1) Her iki yönetici de AYNI sürümle ekranı açar.
        var opened = _perms.GetForUser(a, u);
        var versionA = opened.Version;
        var versionB = opened.Version;
        Assert.Equal(versionA, versionB);

        // (2) Birinci yönetici kaydeder: malzeme görüntüleme + ekleme verir.
        _perms.SaveForUser(a, u, new[] { Mod("materials", v: true, c: true) }, Array.Empty<string>(), versionA);

        // (3+4) İkinci yönetici ESKİ sürümle kaydetmeye çalışır → çakışma hatası.
        var ex = Assert.Throws<ConcurrencyException>(() =>
            _perms.SaveForUser(a, u, new[] { Mod("vehicles", v: true) }, Array.Empty<string>(), versionB));
        Assert.NotNull(ex);

        // (5) Birinci yöneticinin verdiği yetkiler KORUNUR.
        var after = _perms.GetForUser(a, u);
        Assert.True(Has(after, "materials", m => m.CanView));
        Assert.True(Has(after, "materials", m => m.CanCreate));

        // (6) İkinci işlemin HİÇBİR kısmi değişikliği yazılmadı (transaction geri alındı).
        Assert.DoesNotContain(after.Modules, m => m.ModuleKey == "vehicles");
        Assert.Single(after.Modules);
    }

    [Fact]
    public void CakismaSonrasi_GuncelSurumleTekrarDenemeBasarili()
    {
        var a = Admin("A");
        var u = TargetUser(a, "hedef2");

        var v0 = _perms.GetForUser(a, u).Version;
        _perms.SaveForUser(a, u, new[] { Mod("materials", v: true) }, Array.Empty<string>(), v0);

        Assert.Throws<ConcurrencyException>(() =>
            _perms.SaveForUser(a, u, new[] { Mod("vehicles", v: true) }, Array.Empty<string>(), v0));

        // Ekranı yenileyip GÜNCEL sürümle tekrar denemek çalışmalı (kullanıcı kilitlenip kalmaz).
        var fresh = _perms.GetForUser(a, u);
        Assert.NotEqual(v0, fresh.Version);
        _perms.SaveForUser(a, u, new[] { Mod("vehicles", v: true) }, Array.Empty<string>(), fresh.Version);

        var after = _perms.GetForUser(a, u);
        Assert.True(Has(after, "vehicles", m => m.CanView));
    }

    // ───────────── 7: farklı kullanıcılar birbirini engellemez ─────────────

    [Fact]
    public void FarkliKullanicilarinYetkileri_BirbiriniEngellemez()
    {
        var a = Admin("A");
        var u1 = TargetUser(a, "hedef3");
        var u2 = TargetUser(a, "hedef4");

        var v1 = _perms.GetForUser(a, u1).Version;
        var v2 = _perms.GetForUser(a, u2).Version;

        _perms.SaveForUser(a, u1, new[] { Mod("materials", v: true) }, Array.Empty<string>(), v1);
        // u1'e kaydetmek u2'nin sürümünü ETKİLEMEZ → u2 kaydı sorunsuz geçmeli.
        _perms.SaveForUser(a, u2, new[] { Mod("vehicles", v: true) }, Array.Empty<string>(), v2);

        Assert.True(Has(_perms.GetForUser(a, u1), "materials", m => m.CanView));
        Assert.True(Has(_perms.GetForUser(a, u2), "vehicles", m => m.CanView));
    }

    // ───────────── Geriye uyumluluk: sürüm verilmezse kontrol yok ─────────────

    [Fact]
    public void SurumVerilmezse_KontrolYapilmaz_EskiCagrilarBozulmaz()
    {
        var a = Admin("A");
        var u = TargetUser(a, "hedef5");

        // Yeni kullanıcı oluşturma akışı sürüm göndermez (çakışacak önceki kayıt yoktur).
        _perms.SaveForUser(a, u, new[] { Mod("materials", v: true) }, Array.Empty<string>());
        _perms.SaveForUser(a, u, new[] { Mod("vehicles", v: true) }, Array.Empty<string>(), null);

        var after = _perms.GetForUser(a, u);
        Assert.True(Has(after, "vehicles", m => m.CanView));
    }

    // ───────────── Sürüm gerçekten artıyor mu (jeton çalışıyor mu) ─────────────

    [Fact]
    public void HerBasariliKayit_SurumuArtirir()
    {
        var a = Admin("A");
        var u = TargetUser(a, "hedef6");

        var v0 = _perms.GetForUser(a, u).Version;
        _perms.SaveForUser(a, u, new[] { Mod("materials", v: true) }, Array.Empty<string>(), v0);
        var v1 = _perms.GetForUser(a, u).Version;
        _perms.SaveForUser(a, u, new[] { Mod("materials", v: true, c: true) }, Array.Empty<string>(), v1);
        var v2 = _perms.GetForUser(a, u).Version;

        Assert.True(v1 > v0, $"ilk kayıt sürümü artırmalı (v0={v0}, v1={v1})");
        Assert.True(v2 > v1, $"ikinci kayıt sürümü artırmalı (v1={v1}, v2={v2})");
    }

    // ───────────── 8: firma izolasyonu ve mevcut yetki kontrolleri bozulmadı ─────────────

    [Fact]
    public void BaskaFirmaninKullanicisininYetkisi_Duzenlenemez()
    {
        var a = Admin("A");
        var b = Admin("B");
        var uB = TargetUser(b, "hedefB");

        // A firmasının admini, B firmasının kullanıcısına ne OKUYABİLİR ne YAZABİLİR.
        Assert.ThrowsAny<Exception>(() => _perms.GetForUser(a, uB));
        Assert.ThrowsAny<Exception>(() =>
            _perms.SaveForUser(a, uB, new[] { Mod("materials", v: true) }, Array.Empty<string>()));

        // B'nin kendi kullanıcısı etkilenmedi.
        Assert.Empty(_perms.GetForUser(b, uB).Modules);
    }

    [Fact]
    public void YetkisizAktor_YetkiKaydedemez()
    {
        var a = Admin("A");
        var u = TargetUser(a, "hedef7");
        var v = _perms.GetForUser(a, u).Version;

        // "permissions" modülünde Edit yetkisi olmayan kullanıcı (deny-by-default).
        var yetkisiz = new SessionContext("u-yetkisiz", "A", new[] { RoleKeys.Staff }, PermissionSet.Empty);

        Assert.Throws<ForbiddenException>(() =>
            _perms.SaveForUser(yetkisiz, u, new[] { Mod("materials", v: true) }, Array.Empty<string>(), v));

        // Yetki kontrolü sürüm kontrolünden ÖNCE çalışmalı; hiçbir şey yazılmamalı.
        Assert.Empty(_perms.GetForUser(a, u).Modules);
    }

    // ───────────── Buton izinleri de aynı korumadan geçer ─────────────

    [Fact]
    public void ButonIzinleri_De_CakismadaKorunur()
    {
        var a = Admin("A");
        var u = TargetUser(a, "hedef8");

        var v0 = _perms.GetForUser(a, u).Version;
        _perms.SaveForUser(a, u, new[] { Mod("reports", v: true) }, new[] { SpecialButtons.ExportReports }, v0);

        Assert.Throws<ConcurrencyException>(() =>
            _perms.SaveForUser(a, u, new[] { Mod("reports", v: true) }, Array.Empty<string>(), v0));

        // Birincinin verdiği buton izni silinmedi.
        Assert.Contains(SpecialButtons.ExportReports, _perms.GetForUser(a, u).Buttons);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}
