using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ KULLANICI İLETİŞİM ALANLARI — UÇTAN UCA ═══ (kullanıcı isteği 2026-09-06, Migration095)
///
/// <para>Kapsam: alanlar oluştururken YAZILIYOR mu, listede GERİ OKUNUYOR mu, düzenleme yolu
/// çalışıyor mu, doğrulama sunucuda gerçekten uygulanıyor mu ve <b>yetki/tenant kapıları</b>
/// bu yeni yolda da kapalı mı.</para>
///
/// <para>Son madde en kritiğidir: yeni bir yazma ucu her zaman yeni bir yetki yükseltme riskidir.
/// Bu yüzden "yetkisiz kullanıcı düzenleyemez" ve "başka firmanın kullanıcısına dokunulamaz"
/// ayrı ayrı sınanır.</para>
/// </summary>
public class KullaniciIletisimAlanlariTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly UserService _users;
    private readonly SessionContext _su = new("root", "A", new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);

    public KullaniciIletisimAlanlariTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_uia_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _users = new UserService(_factory, _clock);
        EnsureCompany("A");
        EnsureCompany("B");
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private void EnsureCompany(string companyId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@c,@c,0,0,1,0);";
        cmd.AddWithValue("@c", companyId);
        cmd.ExecuteNonQuery();
    }

    private string Olustur(string kullaniciAdi, string? eposta = null, string? telefon = null,
        string? unvan = null, string? not = null, string companyId = "A")
        => _users.CreateUser(_su, new NewUser(kullaniciAdi, "Sifre123", "Ad Soyad",
            new[] { RoleKeys.Staff }, companyId, null, null, false, null,
            eposta, telefon, unvan, not));

    private UserRow Bul(string id, SessionContext? aktor = null)
        => _users.ListUsers(aktor ?? _su).Single(u => u.Id == id);

    // ── Şema ─────────────────────────────────────────────────────────────────────────────
    /// <summary>Migration095 sütunları gerçekten açtı mı? (Yoksa aşağıdaki her test anlamsızdır.)</summary>
    [Fact]
    public void Migration095_DortSutunuAcar()
    {
        using var conn = _factory.Create();
        foreach (var sutun in new[] { "email", "phone", "title", "notes" })
            Assert.True(DbIntrospect.ColumnExists(conn, null, "users", sutun), $"users.{sutun} yok");
    }

    // ── Oluşturma ────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Olusturma_AlanlariYazar_ListedeGeriOkunur()
    {
        var id = Olustur("iletisim1", "ad@firma.com", "0500 111 22 33", "Depo Sorumlusu", "şantiye tableti");
        var u = Bul(id);
        Assert.Equal("ad@firma.com", u.Email);
        Assert.Equal("0500 111 22 33", u.Phone);
        Assert.Equal("Depo Sorumlusu", u.Title);
        Assert.Equal("şantiye tableti", u.Notes);
    }

    /// <summary>Alanlar ZORUNLU değildir; verilmezse boş (null) kalır — eski çağıranlar da bozulmaz.</summary>
    [Fact]
    public void Olusturma_AlanlarVerilmezse_NullKalir()
    {
        var id = Olustur("iletisim2");
        var u = Bul(id);
        Assert.Null(u.Email);
        Assert.Null(u.Phone);
        Assert.Null(u.Title);
        Assert.Null(u.Notes);
    }

    /// <summary>Boş/boşluk metni veritabanına "" olarak DEĞİL, null olarak yazılır (karışıklık olmasın).</summary>
    [Fact]
    public void Olusturma_BosluklarNullaCevrilir()
    {
        var id = Olustur("iletisim3", "   ", "  ", " ", "  ");
        var u = Bul(id);
        Assert.Null(u.Email);
        Assert.Null(u.Phone);
    }

    // ── Düzenleme ────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Duzenleme_AlanlariGunceller()
    {
        var id = Olustur("duzenle1", "eski@firma.com", "0500 111 22 33");
        _users.UpdateProfile(_su, id, "Yeni Ad", "yeni@firma.com", "0532 999 88 77", "Şantiye Şefi", "not");
        var u = Bul(id);
        Assert.Equal("Yeni Ad", u.FullName);
        Assert.Equal("yeni@firma.com", u.Email);
        Assert.Equal("0532 999 88 77", u.Phone);
        Assert.Equal("Şantiye Şefi", u.Title);
        Assert.Equal("not", u.Notes);
    }

    /// <summary>Alan boşaltılabilmeli — yanlış girilen bir e-posta silinebilsin.</summary>
    [Fact]
    public void Duzenleme_AlanBosaltilabilir()
    {
        var id = Olustur("duzenle2", "yanlis@firma.com");
        _users.UpdateProfile(_su, id, "Ad Soyad", null, null, null, null);
        Assert.Null(Bul(id).Email);
    }

    /// <summary>Düzenleme kullanıcı adına ve şifreye DOKUNMAZ — giriş bozulmamalı.</summary>
    [Fact]
    public void Duzenleme_KullaniciAdiniDegistirmez()
    {
        var id = Olustur("duzenle3");
        _users.UpdateProfile(_su, id, "Başka Ad", "a@b.com", null, null, null);
        Assert.Equal("duzenle3", Bul(id).Username);
    }

    // ── Doğrulama sunucuda ───────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("bozukadres")]
    [InlineData("ad@firma")]
    [InlineData("@firma.com")]
    public void Duzenleme_GecersizEposta_Reddedilir(string eposta)
    {
        var id = Olustur("dogrula1");
        Assert.Throws<InvalidOperationException>(() => _users.UpdateProfile(_su, id, null, eposta, null, null, null));
        Assert.Null(Bul(id).Email);   // hatalı değer YAZILMAMIŞ olmalı
    }

    [Theory]
    [InlineData("123")]
    [InlineData("0500 abc 22 33")]
    public void Duzenleme_GecersizTelefon_Reddedilir(string telefon)
    {
        var id = Olustur("dogrula2");
        Assert.Throws<InvalidOperationException>(() => _users.UpdateProfile(_su, id, null, null, telefon, null, null));
        Assert.Null(Bul(id).Phone);
    }

    // ── Yetki ve tenant (YENİ YAZMA UCU = YENİ RİSK) ─────────────────────────────────────
    /// <summary>Yetkisiz (personel) bir kullanıcı BAŞKASINI düzenleyemez.</summary>
    [Fact]
    public void Duzenleme_YetkisizKullanici_BaskasiniDuzenleyemez()
    {
        var hedef = Olustur("hedef1");
        var saldirganId = Olustur("saldirgan1");
        var saldirgan = new SessionContext(saldirganId, "A", new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _users.UpdateProfile(saldirgan, hedef, "Ele Geçirildi", null, null, null, null));
        Assert.Equal("Ad Soyad", Bul(hedef).FullName);
    }

    /// <summary>Kullanıcı KENDİ bilgilerini düzenleyebilir (admin olmasa da).</summary>
    [Fact]
    public void Duzenleme_KullaniciKendiniDuzenleyebilir()
    {
        var id = Olustur("kendisi1");
        var oturum = new SessionContext(id, "A", new[] { RoleKeys.Staff }, PermissionSet.Empty);
        _users.UpdateProfile(oturum, id, "Kendi Adım", "kendi@firma.com", null, null, null);
        Assert.Equal("kendi@firma.com", Bul(id).Email);
    }

    /// <summary>BAŞKA FİRMANIN kullanıcısına dokunulamaz (tenant sızıntısı).</summary>
    [Fact]
    public void Duzenleme_BaskaFirmaninKullanicisinaDokunamaz()
    {
        var bFirmaKullanici = Olustur("bfirma1", "b@firma.com", companyId: "B");
        var aAdminId = Olustur("aadmin1");
        var aAdmin = new SessionContext(aAdminId, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        // Tenant kapısı: A firmasının admini B firmasının kullanıcısını GÖREMEZ/DEĞİŞTİREMEZ.
        Assert.ThrowsAny<Exception>(() => _users.UpdateProfile(aAdmin, bFirmaKullanici, "Sızdı", "sizdi@x.com", null, null, null));
        Assert.Equal("b@firma.com", Bul(bFirmaKullanici).Email);   // değer DEĞİŞMEMİŞ olmalı
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}
