using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 4.3 — ANLAŞILIR LOG + HER KAYDIN KENDİ LOG EKRANI ═══ (kullanıcı isteği 2026-09-06)
///
/// <i>"Log bilgilerin anlaşılır değil. log bilgilerinde işlem tarihi, saati, yaptığı işlemi, yapılan
/// işlemin önceki ve sonraki hallerini günlere ayırıp … kayıta ait hangi alanda neyi güncelledi ise
/// görebilmeliyim. ekranlarla beraber her kaydın kendine ait bir log ekranı olmalı."</i>
///
/// <b>Kök neden (kanıtlanmış, tahmin değil).</b> <c>audit_logs.before_json / after_json</c> sütunları
/// şemada 001'den beri vardı ama 59 dosyadaki 162 <c>AuditEntry</c> çağrısının neredeyse tamamı bunları
/// BOŞ bırakıyordu → log yalnız "kim, ne zaman, hangi tip, hangi işlem" diyordu. Çözüm tek noktada
/// (<c>AuditWriter</c>) anlık görüntü almaktır; iş mantığına DOKUNULMAZ.
///
///  LG1 — Yeni kayıtta anlık görüntü yazılır; alanlar okunur biçimde listelenir
///  LG2 — 🔴 Güncellemede ALAN BAZLI fark çıkar ("Plaka: … → …") — kullanıcının asıl isteği
///  LG3 — 🔴 GÜVENLİK: parola özeti hiçbir log görüntüsüne girmez
///  LG4 — Teknik sütunlar (version/updated_at) farkta GÖSTERİLMEZ — gerçek değişikliği gizlerlerdi
///  LG5 — Tek kaydın geçmişi YALNIZ o kaydı döner (başka kayıt sızmaz)
///  LG6 — Kayıt logu yetkiye bağlıdır (btn-screen-log yoksa reddedilir)
///  LG7 — Ekranı göremeyen, o ekranın kaydının geçmişini de göremez (yan kapı kapalı)
///  LG8 — Eşlemesi olmayan tip sessizce AÇILMAZ (ForbiddenException)
///  LG9 — Gün/saat alanları doldurulur (günlere ayırma bunlarla yapılır)
///  LG10 — Firma sınırı: başka firmanın kaydının geçmişi görünmez
/// </summary>
public class AnlasilirLogTests : IDisposable
{
    private const string Co = "F43";
    private const string Co2 = "F43B";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly TestClock _clock = new();
    private readonly AuditLogService _audit;
    private readonly VehicleService _vehicles;
    private readonly SessionContext _yonetici;
    private readonly string _uid, _aracId;

    private const long Simdi = 1_700_000_000_000;

    private sealed class TestClock : IClock
    { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(Simdi); }

    public AnlasilirLogTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_f43log_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        foreach (var co in new[] { Co, Co2 }) Calistir(
            $"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{co}','{co}',1,1,1,0);");

        _uid = new UserService(_f, _clock).EnsureInitialAdmin(Co, "admin", "Admin!2026", RoleKeys.CompanyAdmin);
        _yonetici = new SessionContext(_uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _vehicles = new VehicleService(_f, _clock);
        _aracId = _vehicles.Create(_yonetici, new NewVehicle("KAM-01", "06 AB 123"));

        _audit = new AuditLogService(_f);
    }

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    /// <summary>Personel oturumu: modül izinleri + istenen özel butonlar (admin bypass'ı YOK).</summary>
    private SessionContext Oturum(string[] moduller, params string[] butonlar)
        => new(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(moduller.Select(m => new ModulePermission(m, true, true, true, false)), butonlar));

    // ══════════════════ LG1 ══════════════════

    [Fact]
    public void LG1_Yeni_Kayitta_Anlik_Goruntu_Yazilir()
    {
        var satirlar = _audit.ForEntity(_yonetici, "vehicle", _aracId);

        var olusturma = Assert.Single(satirlar, x => x.Action == "create");
        Assert.NotNull(olusturma.AfterJson);
        // "Kod: KAM-01" / "Plaka: 06 AB 123" gibi okunur satırlar üretilir.
        Assert.Contains(olusturma.Changes, c => c.Label == "Plaka" && c.New == "06 AB 123");
        Assert.Contains(olusturma.Changes, c => c.Label == "İç Kod" && c.New == "KAM-01");
    }

    // ══════════════════ LG2 — EN ÖNEMLİ ══════════════════

    /// <summary>
    /// 🔴 Kullanıcının asıl isteği: "hangi alanda neyi güncelledi ise görebilmeliyim".
    /// Önceki hâl ayrıca saklanmaz; bir önceki log satırının anlık görüntüsü zaten önceki hâldir.
    /// </summary>
    [Fact]
    public void LG2_Guncellemede_Alan_Bazli_Fark_Cikar()
    {
        _clock.UtcNow = DateTimeOffset.FromUnixTimeMilliseconds(Simdi + 60_000);
        _vehicles.Update(_yonetici, _aracId, new UpdateVehicle("06 XY 999", null, "active", null));

        var satirlar = _audit.ForEntity(_yonetici, "vehicle", _aracId);
        var guncelleme = satirlar.First(x => x.Action == "update");

        var plaka = Assert.Single(guncelleme.Changes, c => c.Label == "Plaka");
        Assert.Equal("06 AB 123", plaka.Old);
        Assert.Equal("06 XY 999", plaka.New);
        Assert.Contains("Plaka: 06 AB 123 → 06 XY 999", guncelleme.ChangeSummary);
    }

    // ══════════════════ LG3 — GÜVENLİK ══════════════════

    /// <summary>
    /// 🔴 Log ekranını görebilen herkes bu görüntüleri okur. Parola özeti (hash) oraya düşerse,
    /// log okuma yetkisi sessizce bir kimlik bilgisi sızıntısına dönerdi.
    /// </summary>
    [Fact]
    public void LG3_Parola_Ozeti_Loga_Asla_Yazilmaz()
    {
        var users = new UserService(_f, _clock);
        var yeniId = users.CreateUser(_yonetici, new NewUser("operator", "Gecici!2026", "Operatör", new[] { RoleKeys.Staff }));

        var satirlar = _audit.ForEntity(_yonetici, "user", yeniId);
        Assert.NotEmpty(satirlar);
        foreach (var r in satirlar)
        {
            Assert.DoesNotContain("password_hash", r.AfterJson ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pbkdf2", r.AfterJson ?? "", StringComparison.OrdinalIgnoreCase);
        }
        Assert.True(AuditFields.Gizli("password_hash"));
    }

    // ══════════════════ LG4 ══════════════════

    /// <summary>Teknik sütunlar her güncellemede değişir; farkta görünselerdi gerçek değişikliği
    /// gürültüyle gizlerlerdi ("Version: 1 → 2" kullanıcıya hiçbir şey anlatmaz).</summary>
    [Fact]
    public void LG4_Teknik_Sutunlar_Farkta_Gosterilmez()
    {
        _clock.UtcNow = DateTimeOffset.FromUnixTimeMilliseconds(Simdi + 60_000);
        _vehicles.Update(_yonetici, _aracId, new UpdateVehicle("06 XY 999", null, "active", null));

        var guncelleme = _audit.ForEntity(_yonetici, "vehicle", _aracId).First(x => x.Action == "update");
        Assert.DoesNotContain(guncelleme.Changes, c => c.Field is "version" or "updated_at" or "id" or "company_id");
    }

    // ══════════════════ LG5 ══════════════════

    [Fact]
    public void LG5_Kayit_Gecmisi_Yalniz_O_Kaydi_Doner()
    {
        var ikinciId = _vehicles.Create(_yonetici, new NewVehicle("KAM-02", "34 ZZ 111"));

        var satirlar = _audit.ForEntity(_yonetici, "vehicle", _aracId);
        Assert.NotEmpty(satirlar);
        Assert.All(satirlar, r => Assert.Equal(_aracId, r.EntityId));
        Assert.DoesNotContain(satirlar, r => r.EntityId == ikinciId);
    }

    // ══════════════════ LG6 / LG7 — YETKİ ══════════════════

    [Fact]
    public void LG6_Log_Yetkisi_Yoksa_Kayit_Gecmisi_Reddedilir()
    {
        var s = Oturum(new[] { "vehicles" });   // btn-screen-log YOK
        Assert.Throws<ForbiddenException>(() => _audit.ForEntity(s, "vehicle", _aracId));
    }

    [Fact]
    public void LG7_Ekrani_Goremeyen_Kaydin_Gecmisini_De_Goremez()
    {
        var s = Oturum(new[] { "materials" }, SpecialButtons.ScreenLog);   // vehicles izni YOK
        Assert.Throws<ForbiddenException>(() => _audit.ForEntity(s, "vehicle", _aracId));

        var izinli = Oturum(new[] { "vehicles" }, SpecialButtons.ScreenLog);
        Assert.NotEmpty(_audit.ForEntity(izinli, "vehicle", _aracId));
    }

    // ══════════════════ LG8 ══════════════════

    /// <summary>Eşlemesi olmayan tip sessizce açılsaydı, bu uç yetki sisteminin etrafından dolaşan
    /// genel bir log okuyucusuna dönerdi.</summary>
    [Fact]
    public void LG8_Eslemesiz_Tip_Sessizce_Acilmaz()
    {
        var s = Oturum(new[] { "vehicles" }, SpecialButtons.ScreenLog);
        Assert.Throws<ForbiddenException>(() => _audit.ForEntity(s, "sessions", "x"));
    }

    // ══════════════════ LG9 ══════════════════

    [Fact]
    public void LG9_Gun_Ve_Saat_Alanlari_Doldurulur()
    {
        var r = _audit.ForEntity(_yonetici, "vehicle", _aracId).First();
        var beklenenGun = DateTimeOffset.FromUnixTimeMilliseconds(r.CreatedAt).LocalDateTime.ToString("dd.MM.yyyy");
        Assert.Equal(beklenenGun, r.DayText);
        Assert.Matches(@"^\d{2}:\d{2}:\d{2}$", r.TimeText);
        Assert.Equal("Araç", r.EntityLabel);
    }

    // ══════════════════ LG10 ══════════════════

    /// <summary>Firma sınırı: sorgu <c>company_id</c> ile kısıtlıdır — başka firmanın kaydının
    /// kimliğini bilmek bile geçmişini açmaya yetmez.</summary>
    [Fact]
    public void LG10_Baska_Firmanin_Kaydinin_Gecmisi_Gorunmez()
    {
        var uid2 = new UserService(_f, _clock).EnsureInitialAdmin(Co2, "admin2", "Admin!2026", RoleKeys.CompanyAdmin);
        var digerFirma = new SessionContext(uid2, Co2, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        Assert.Empty(_audit.ForEntity(digerFirma, "vehicle", _aracId));
    }
}
