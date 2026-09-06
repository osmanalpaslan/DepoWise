using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Chat;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ UYGULAMA İÇİ SOHBET ═══ (kullanıcı isteği 2026-09-06, Migration096)
///
/// <para>Sohbet YENİ bir yazma yüzeyidir; en kritik testler güvenlik tarafındadır:
/// <b>firma sınırı</b> (bir firmanın mesajı diğerine sızmamalı, başka firmaya mesaj
/// gönderilememeli) ve <b>girdi doğrulaması</b> (boş/aşırı uzun mesaj).</para>
///
/// <para>İşlevsel taraf da sınanır: okunmamış sayacı, okundu işaretleme, çevrimiçi eşiği ve
/// artımlı yoklama (<c>since</c>) — sonuncusu yanlışsa her yoklama tüm geçmişi taşır.</para>
/// </summary>
public class ChatServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly ChatService _chat;
    private readonly UserService _users;
    private readonly SessionContext _su = new("root", "A", new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);

    public ChatServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_chat_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _chat = new ChatService(_factory, _clock);
        _users = new UserService(_factory, _clock);
        Firma("A"); Firma("B");
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Ilerle(int saniye) => UtcNow = UtcNow.AddSeconds(saniye);
    }

    private void Firma(string id)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@c,@c,0,0,1,0);";
        cmd.AddWithValue("@c", id);
        cmd.ExecuteNonQuery();
    }

    private string Kullanici(string ad, string firma = "A")
        => _users.CreateUser(_su, new NewUser(ad, "Sifre123", ad.ToUpperInvariant(),
            new[] { RoleKeys.Staff }, firma, null, null, false, null));

    private static SessionContext Oturum(string userId, string firma = "A")
        => new(userId, firma, new[] { RoleKeys.Staff }, PermissionSet.Empty);

    // ── Temel akış ───────────────────────────────────────────────────────────────────────
    [Fact]
    public void Gonder_MesajKaydedilir_KonusmadaGorunur()
    {
        var a = Kullanici("ali"); var v = Kullanici("veli");
        _chat.Gonder(Oturum(a), v, "Merhaba");

        var konusma = _chat.Konusma(Oturum(v), a);
        Assert.Single(konusma);
        Assert.Equal("Merhaba", konusma[0].Body);
        Assert.False(konusma[0].Mine);   // Veli için bu mesaj KENDİSİNİN değil
    }

    /// <summary>Konuşma İKİ YÖNLÜDÜR: gönderdiklerim de aldıklarım da aynı listede olmalı.</summary>
    [Fact]
    public void Konusma_HerIkiYonuIcerir_EskidenYeniye()
    {
        var a = Kullanici("ali"); var v = Kullanici("veli");
        _chat.Gonder(Oturum(a), v, "birinci");
        _clock.Ilerle(1);
        _chat.Gonder(Oturum(v), a, "ikinci");
        _clock.Ilerle(1);
        _chat.Gonder(Oturum(a), v, "üçüncü");

        var k = _chat.Konusma(Oturum(a), v);
        Assert.Equal(3, k.Count);
        Assert.Equal(new[] { "birinci", "ikinci", "üçüncü" }, k.Select(m => m.Body));
        Assert.True(k[0].Mine);
        Assert.False(k[1].Mine);
    }

    /// <summary>Başka bir kişiyle olan konuşma bu listeye SIZMAMALI.</summary>
    [Fact]
    public void Konusma_BaskaKisiyleOlanlariIcermez()
    {
        var a = Kullanici("ali"); var v = Kullanici("veli"); var c = Kullanici("can");
        _chat.Gonder(Oturum(a), v, "veliye");
        _chat.Gonder(Oturum(a), c, "cana");

        var k = _chat.Konusma(Oturum(a), v);
        Assert.Single(k);
        Assert.Equal("veliye", k[0].Body);
    }

    /// <summary>Artımlı yoklama: since'ten SONRAKİ mesajlar. Yanlışsa her tur tüm geçmişi taşır.</summary>
    [Fact]
    public void Konusma_SinceIleYalnizYeniMesajlar()
    {
        var a = Kullanici("ali"); var v = Kullanici("veli");
        _chat.Gonder(Oturum(a), v, "eski");
        var isaret = _clock.UtcNow.ToUnixTimeMilliseconds();
        _clock.Ilerle(5);
        _chat.Gonder(Oturum(a), v, "yeni");

        var k = _chat.Konusma(Oturum(a), v, sinceMs: isaret);
        Assert.Single(k);
        Assert.Equal("yeni", k[0].Body);
    }

    // ── Okunmamış / okundu ───────────────────────────────────────────────────────────────
    [Fact]
    public void Kisiler_OkunmamisSayisiniVerir()
    {
        var a = Kullanici("ali"); var v = Kullanici("veli");
        _chat.Gonder(Oturum(a), v, "bir");
        _chat.Gonder(Oturum(a), v, "iki");

        var kisi = _chat.Kisiler(Oturum(v)).Single(k => k.UserId == a);
        Assert.Equal(2, kisi.Unread);
        Assert.Equal(2, _chat.ToplamOkunmamis(Oturum(v)));
    }

    [Fact]
    public void OkunduIsaretle_SayaciSifirlar()
    {
        var a = Kullanici("ali"); var v = Kullanici("veli");
        _chat.Gonder(Oturum(a), v, "bir");
        Assert.Equal(1, _chat.OkunduIsaretle(Oturum(v), a));
        Assert.Equal(0, _chat.ToplamOkunmamis(Oturum(v)));
    }

    /// <summary>Gönderenin kendi mesajı ona okunmamış SAYILMAZ.</summary>
    [Fact]
    public void KendiMesajim_BanaOkunmamisSayilmaz()
    {
        var a = Kullanici("ali"); var v = Kullanici("veli");
        _chat.Gonder(Oturum(a), v, "bir");
        Assert.Equal(0, _chat.ToplamOkunmamis(Oturum(a)));
    }

    // ── Çevrimiçi ────────────────────────────────────────────────────────────────────────
    /// <summary>Kişi listesini istemek "buradayım" damgasıdır; karşı taraf beni çevrimiçi görür.</summary>
    [Fact]
    public void Kisiler_CagiraniCevrimiciYapar()
    {
        var a = Kullanici("ali"); var v = Kullanici("veli");
        _chat.Kisiler(Oturum(a));                       // ali "görüldü"
        var aliBak = _chat.Kisiler(Oturum(v)).Single(k => k.UserId == a);
        Assert.True(aliBak.Online);
    }

    /// <summary>Eşik aşılınca çevrimdışına düşer — "hep çevrimiçi" göstermek yalan olur.</summary>
    [Fact]
    public void Kisiler_EsikAsilinca_CevrimdisiOlur()
    {
        var a = Kullanici("ali"); var v = Kullanici("veli");
        _chat.Kisiler(Oturum(a));
        _clock.Ilerle(ChatService.CevrimiciSaniye + 10);
        Assert.False(_chat.Kisiler(Oturum(v)).Single(k => k.UserId == a).Online);
    }

    /// <summary>Kişi listesinde kullanıcının KENDİSİ yer almaz (kendine mesaj atılmaz).</summary>
    [Fact]
    public void Kisiler_KendisiniIcermez()
    {
        var a = Kullanici("ali"); Kullanici("veli");
        Assert.DoesNotContain(_chat.Kisiler(Oturum(a)), k => k.UserId == a);
    }

    // ── GÜVENLİK: firma sınırı ───────────────────────────────────────────────────────────
    /// <summary>BAŞKA FİRMADAKİ kullanıcıya mesaj gönderilemez.</summary>
    [Fact]
    public void Gonder_BaskaFirmayaMesajReddedilir()
    {
        var a = Kullanici("ali", "A");
        var b = Kullanici("bveli", "B");
        Assert.Throws<ForbiddenException>(() => _chat.Gonder(Oturum(a, "A"), b, "sızıntı"));
    }

    /// <summary>Kişi listesi YALNIZ kendi firmasını gösterir.</summary>
    [Fact]
    public void Kisiler_YalnizKendiFirmasi()
    {
        var a = Kullanici("ali", "A");
        Kullanici("veli", "A");
        var b = Kullanici("bveli", "B");
        var kisiler = _chat.Kisiler(Oturum(a, "A"));
        Assert.DoesNotContain(kisiler, k => k.UserId == b);
        Assert.Contains(kisiler, k => k.Username == "veli");
    }

    /// <summary>
    /// Başka firmanın oturumu, A firmasındaki bir konuşmayı OKUYAMAZ. (Kullanıcı kimliklerini
    /// bilse bile company_id süzgeci mesajları döndürmez.)
    /// </summary>
    [Fact]
    public void Konusma_BaskaFirmadanOkunamaz()
    {
        var a = Kullanici("ali", "A"); var v = Kullanici("veli", "A");
        _chat.Gonder(Oturum(a, "A"), v, "gizli");

        var b = Kullanici("bveli", "B");
        Assert.Empty(_chat.Konusma(Oturum(b, "B"), a));
    }

    /// <summary>Başka firmanın oturumu, A firmasındaki mesajları okundu İŞARETLEYEMEZ.</summary>
    [Fact]
    public void OkunduIsaretle_BaskaFirmayiEtkilemez()
    {
        var a = Kullanici("ali", "A"); var v = Kullanici("veli", "A");
        _chat.Gonder(Oturum(a, "A"), v, "gizli");
        var b = Kullanici("bveli", "B");

        Assert.Equal(0, _chat.OkunduIsaretle(Oturum(b, "B"), a));
        Assert.Equal(1, _chat.ToplamOkunmamis(Oturum(v, "A")));   // hâlâ okunmamış
    }

    // ── Girdi doğrulaması ────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n ")]
    public void Gonder_BosMesajReddedilir(string govde)
    {
        var a = Kullanici("ali"); var v = Kullanici("veli");
        Assert.Throws<InvalidOperationException>(() => _chat.Gonder(Oturum(a), v, govde));
    }

    [Fact]
    public void Gonder_CokUzunMesajReddedilir()
    {
        var a = Kullanici("ali"); var v = Kullanici("veli");
        var uzun = new string('x', ChatService.AzamiUzunluk + 1);
        Assert.Throws<InvalidOperationException>(() => _chat.Gonder(Oturum(a), v, uzun));
    }

    [Fact]
    public void Gonder_SinirdakiUzunlukKabulEdilir()
    {
        var a = Kullanici("ali"); var v = Kullanici("veli");
        var tam = new string('x', ChatService.AzamiUzunluk);
        _chat.Gonder(Oturum(a), v, tam);
        Assert.Single(_chat.Konusma(Oturum(a), v));
    }

    [Fact]
    public void Gonder_KendineMesajReddedilir()
    {
        var a = Kullanici("ali");
        Assert.Throws<InvalidOperationException>(() => _chat.Gonder(Oturum(a), a, "kendime"));
    }

    /// <summary>Mesaj kırpılır: başta/sonda boşlukla gönderilen metin temiz saklanır.</summary>
    [Fact]
    public void Gonder_BasSonBosluklariKirpar()
    {
        var a = Kullanici("ali"); var v = Kullanici("veli");
        _chat.Gonder(Oturum(a), v, "   selam   ");
        Assert.Equal("selam", _chat.Konusma(Oturum(a), v)[0].Body);
    }

    /// <summary>Pasif (silinmiş) kullanıcıya mesaj gönderilemez.</summary>
    [Fact]
    public void Gonder_PasifKullaniciyaReddedilir()
    {
        var a = Kullanici("ali"); var v = Kullanici("veli");
        _users.SetActive(_su, v, false);
        Assert.Throws<ForbiddenException>(() => _chat.Gonder(Oturum(a), v, "merhaba"));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}
