using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 4.9 — GÜNLÜK FAALİYET: TARİH ARALIĞI + ÇOKLU ARAÇ (2026-09-06) ═══
///
/// <b>Kullanıcı isteği.</b> <i>"Günlük faaliyet ekranında tarih aralığı sorgulayabileceğim alan
/// olmalı; sonrasında tablo üzerindeki filtre kısımlarından kendim sorgu yapabileyim. Birden fazla
/// araç seçebileceğim yapıyı buraya da ekleyebilirsin."</i>
///
/// Tarih aralığı servis tarafında ZATEN destekleniyordu (eksik olan ekran); çoklu araç için araç
/// KİMLİĞİ süzgeci eklendi — metin araması değil kesin süzme (SQL'e liste GÖMÜLMEZ, parametre bağlanır).
///
///  GF1 — Tarih aralığı süzer; aralık dışı kayıt gelmez
///  GF2 — Bitiş günü DAHİLDİR (gün sonu sınırı)
///  GF3 — 🔴 Çoklu araç: yalnız seçilen araçların kaydı gelir
///  GF4 — Seçim boşsa süzme yoktur (bugünkü davranış korunur)
///  GF5 — Tarih + araç BİRLİKTE çalışır (kesişim)
///  GF6 — Kolon filtresi (tablo üstü) tarih aralığının İÇİNDE çalışmaya devam eder
/// </summary>
public class GunlukFaaliyetSuzgecTests : IDisposable
{
    private const string Co = "GFS";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly DailyActivityService _gunluk;
    private readonly SessionContext _admin;
    private readonly string _aracA, _aracB;

    private static readonly long Gun10 = new DateTimeOffset(2024, 3, 10, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private static readonly long Gun20 = new DateTimeOffset(2024, 3, 20, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    public GunlukFaaliyetSuzgecTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_gfs_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");

        var uid = new UserService(_f).EnsureInitialAdmin(Co, "gfs_admin", "Gfs!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var araclar = new VehicleService(_f);
        _aracA = araclar.Create(_admin, new NewVehicle("GFS-A", "01 AAA 01"));
        _aracB = araclar.Create(_admin, new NewVehicle("GFS-B", "02 BBB 02"));

        _gunluk = new DailyActivityService(_f, new DepoWise.Infrastructure.Maintenance.MaintenanceService(_f));
        Hareket(_aracA, Gun10, "A-onuncu");
        Hareket(_aracB, Gun20, "B-yirminci");
    }

    private void Hareket(string aracId, long gun, string aciklama)
        => _gunluk.SaveMovement(_admin,
            new NewMovementActivity("movement", aracId, Description: aciklama, ActivityDate: gun),
            Guid.NewGuid().ToString("N"));

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private IReadOnlyList<DailyActivityGridRow> Ara(
        long? from = null, long? to = null, IReadOnlyList<string>? araclar = null, string? aciklama = null)
        => _gunluk.SearchGrid(_admin, new DailyActivityGridFilter(Description: aciklama, VehicleIds: araclar),
            1, 50, null, false, false, from, to).Items;

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }

    // ══════════════════ GF1 / GF2 — TARİH ══════════════════

    [Fact]
    public void GF1_Tarih_Araligi_Suzer()
    {
        var sonuc = Ara(from: Gun10, to: Gun10 + 86_399_999);

        Assert.Single(sonuc);
        Assert.Contains("A-onuncu", sonuc[0].Description);
    }

    /// <summary>🔴 Bitiş gün BAŞI gönderilseydi o günün kayıtları düşerdi ("kaydım kayboldu").</summary>
    [Fact]
    public void GF2_Bitis_Gunu_Dahildir()
    {
        Assert.Single(Ara(from: Gun20, to: Gun20 + 86_399_999));
        Assert.Empty(Ara(from: Gun20 + 1, to: Gun20 + 86_399_999));
    }

    // ══════════════════ GF3 / GF4 — ÇOKLU ARAÇ ══════════════════

    [Fact]
    public void GF3_Coklu_Arac_Yalniz_Secilenleri_Getirir()
    {
        var yalnizA = Ara(araclar: new[] { _aracA });
        Assert.Single(yalnizA);
        Assert.Contains("A-onuncu", yalnizA[0].Description);

        var ikisi = Ara(araclar: new[] { _aracA, _aracB });
        Assert.Equal(2, ikisi.Count);
    }

    [Fact]
    public void GF4_Secim_Yoksa_Suzme_Yok()
        => Assert.Equal(2, Ara().Count);

    // ══════════════════ GF5 — KESİŞİM ══════════════════

    [Fact]
    public void GF5_Tarih_Ve_Arac_Birlikte_Calisir()
    {
        // 10 Mart aralığı + YALNIZ B aracı → kesişim boş (B'nin kaydı 20 Mart'ta).
        Assert.Empty(Ara(from: Gun10, to: Gun10 + 86_399_999, araclar: new[] { _aracB }));
        // 20 Mart aralığı + B aracı → gelir.
        Assert.Single(Ara(from: Gun20, to: Gun20 + 86_399_999, araclar: new[] { _aracB }));
    }

    // ══════════════════ GF6 — KOLON FİLTRESİ KORUNUR ══════════════════

    /// <summary>Kullanıcının şartı: tarih aralığından SONRA tablo üstü filtrelerle kendi sorgusunu
    /// yapabilmeli. Yani iki süzgeç birbirini iptal etmemeli.</summary>
    [Fact]
    public void GF6_Kolon_Filtresi_Tarih_Icinde_Calisir()
    {
        Assert.Single(Ara(from: Gun10, to: Gun20 + 86_399_999, aciklama: "onuncu"));
        Assert.Empty(Ara(from: Gun10, to: Gun10 + 86_399_999, aciklama: "yirminci"));
    }
}
