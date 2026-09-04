using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Vehicles;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ARA İŞ 6 — YAKIT DAĞITIMLARI: GÖRÜNMEYEN KAYIT · SAYFALAMA · ARAMA (2026-09-04) ═══
///
/// <b>Kullanıcının yaşadığı:</b> raporda 02.08.2026 tarihli bir yakıt dağıtımı var, ama Yakıt
/// Dağıtımları ekranında o kayıt <b>bulunamıyor</b>.
///
/// <b>Kök neden:</b> ekranlar <c>ListDistributions(s, limit: 200)</c> çağırıyordu ve sorgu
/// <c>ORDER BY distribution_date DESC</c> ile en yeniden başlıyordu → yalnız <b>en yeni 200</b> kayıt
/// görünüyor, daha eskiler sessizce düşüyordu. Rapor limitsiz okuduğu için aynı kayıt orada görünüyordu.
/// Kesilme kullanıcıya bildirilmiyordu da — kayıt "kaybolmuş" gibi duruyordu.
///
/// <b>Bu testler neyi kilitler:</b> 200'den fazla kayıt varken ESKİ kaydın erişilebilir kaldığını,
/// toplam sayının doğru döndüğünü ve filtrelerin SQL'de (bellekte değil) süzüldüğünü. Filtre bellekte
/// uygulansaydı toplam sayı yanlış çıkar, sayfalama sessizce bozulurdu.
///
///  YKT1 — 200'den fazla kayıt: ESKİ kayıt sayfalanarak ERİŞİLEBİLİR (asıl şikayet)
///  YKT2 — Toplam sayı gerçek kayıt sayısıdır (sayfa boyutuyla kesilmez)
///  YKT3 — Tarih aralığı filtresi SQL'de süzer; toplam da süzülmüş sayıdır
///  YKT4 — Araç filtresi iç kod VE plaka üzerinde çalışır
///  YKT5 — Serbest arama araç kodu/plaka/açıklama içinde arar
///  YKT6 — İptal edilenler varsayılan GİZLİ (mevcut davranış korunur)
///  YKT7 — Sayfa boyutu üst sınırı vardır (tek istekle tüm tablo çekilemez)
/// </summary>
public class YakitListeSayfalamaTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly FuelService _fuel;
    private readonly SessionContext _admin;
    private const string Co = "YAKITSAYFA";

    private string _aracA = "", _aracB = "";

    public YakitListeSayfalamaTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_yakitsayfa_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();

        var users = new UserService(_f);
        var uid = users.EnsureInitialAdmin(Co, "admin", "Test!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var vehicles = new VehicleService(_f);
        _aracA = vehicles.Create(_admin, new NewVehicle("EKS-001", "34 ABC 001"));
        _aracB = vehicles.Create(_admin, new NewVehicle("KMY-002", "06 XYZ 002"));

        _fuel = new FuelService(_f);
    }

    /// <summary>Depoya yakıt koyar (dağıtım yapabilmek için stok gerekir).</summary>
    private void DepoyaKoy(decimal litre)
        => _fuel.AddDepotEntry(_admin, new NewDepotEntry(litre, 40m), "op-depo-" + Guid.NewGuid().ToString("N"));

    private static long Gun(int y, int a, int g)
        => new DateTimeOffset(new DateTime(y, a, g, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    private void Dagit(string aracId, long tarih, decimal litre, decimal sayac, string? not = null)
        => _fuel.Distribute(_admin,
            new NewDistribution(aracId, litre, sayac, DistributionDate: tarih, Note: not),
            "op-dagitim-" + Guid.NewGuid().ToString("N"));

    // ── TESTLER ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void YKT1_Ikiyuzden_Fazla_Kayitta_Eski_Kayit_Erisilebilir()
    {
        DepoyaKoy(100_000m);

        // ⭐ Kullanıcının senaryosu: ESKİ bir kayıt (02.08.2026), üstüne 250 yeni kayıt.
        var eskiTarih = Gun(2026, 8, 2);
        Dagit(_aracA, eskiTarih, 10m, 100m, "ESKI-KAYIT");
        for (int i = 1; i <= 250; i++) Dagit(_aracB, Gun(2026, 8, 3) + i * 86_400_000L, 5m, 100m + i);

        // Eski davranış: en yeni 200 → eski kayıt PENCEREDEN DÜŞÜYORDU.
        var eskiYol = _fuel.ListDistributions(_admin, 200);
        Assert.DoesNotContain(eskiYol, x => x.Note == "ESKI-KAYIT");   // kusurun kendisi (kanıt)

        // Yeni davranış: sayfalanarak MUTLAKA erişilebilir.
        var ilk = _fuel.SearchDistributions(_admin, page: 1, pageSize: 50);
        Assert.Equal(251, ilk.TotalCount);

        var bulundu = false;
        for (int sayfa = 1; sayfa <= ilk.TotalPages && !bulundu; sayfa++)
            bulundu = _fuel.SearchDistributions(_admin, sayfa, 50).Items.Any(x => x.Note == "ESKI-KAYIT");

        Assert.True(bulundu, "02.08.2026 tarihli kayıt sayfalanarak da bulunamadı — asıl şikayet sürüyor.");
    }

    [Fact]
    public void YKT2_Toplam_Sayi_Sayfa_Boyutuyla_Kesilmez()
    {
        DepoyaKoy(10_000m);
        for (int i = 1; i <= 30; i++) Dagit(_aracA, Gun(2026, 3, 1) + i * 86_400_000L, 5m, 100m + i);

        var r = _fuel.SearchDistributions(_admin, page: 1, pageSize: 10);
        Assert.Equal(10, r.Items.Count);    // sayfada 10 satır
        Assert.Equal(30, r.TotalCount);     // ama TOPLAM 30 — kullanıcı kaç kaydı olduğunu görebilir
    }

    [Fact]
    public void YKT3_Tarih_Araligi_SQL_de_Suzer_Toplam_da_Suzulur()
    {
        DepoyaKoy(10_000m);
        Dagit(_aracA, Gun(2026, 1, 10), 5m, 110m, "OCAK");
        Dagit(_aracA, Gun(2026, 6, 15), 5m, 120m, "HAZIRAN");
        Dagit(_aracA, Gun(2026, 6, 20), 5m, 130m, "HAZIRAN2");
        Dagit(_aracA, Gun(2026, 9, 1), 5m, 140m, "EYLUL");

        var r = _fuel.SearchDistributions(_admin, 1, 50,
            fromDateMs: Gun(2026, 6, 1), toDateMs: Gun(2026, 6, 30));

        // Filtre bellekte uygulansaydı TotalCount 4 kalır, sayfalama sessizce bozulurdu.
        Assert.Equal(2, r.TotalCount);
        Assert.All(r.Items, x => Assert.StartsWith("HAZIRAN", x.Note));
    }

    [Fact]
    public void YKT4_Arac_Filtresi_Ic_Kod_ve_Plaka_Uzerinde_Calisir()
    {
        DepoyaKoy(10_000m);
        Dagit(_aracA, Gun(2026, 5, 1), 5m, 150m, "A-ARAC");
        Dagit(_aracB, Gun(2026, 5, 2), 5m, 160m, "B-ARAC");

        // İç kod ile
        var koda = _fuel.SearchDistributions(_admin, 1, 50, vehicleQuery: "EKS");
        Assert.Equal(1, koda.TotalCount);
        Assert.Equal("A-ARAC", koda.Items[0].Note);

        // Plaka ile — kullanıcı aracı plakasından da arayabilmeli
        var plaka = _fuel.SearchDistributions(_admin, 1, 50, vehicleQuery: "06 XYZ");
        Assert.Equal(1, plaka.TotalCount);
        Assert.Equal("B-ARAC", plaka.Items[0].Note);
    }

    [Fact]
    public void YKT5_Serbest_Arama_Kod_Plaka_ve_Aciklamada_Arar()
    {
        DepoyaKoy(10_000m);
        Dagit(_aracA, Gun(2026, 5, 1), 5m, 170m, "santiye teslimi");
        Dagit(_aracB, Gun(2026, 5, 2), 5m, 180m, "merkez depo");

        Assert.Equal(1, _fuel.SearchDistributions(_admin, 1, 50, freeText: "santiye").TotalCount);   // açıklama
        Assert.Equal(1, _fuel.SearchDistributions(_admin, 1, 50, freeText: "KMY").TotalCount);       // iç kod
        Assert.Equal(1, _fuel.SearchDistributions(_admin, 1, 50, freeText: "34 ABC").TotalCount);    // plaka
        Assert.Equal(2, _fuel.SearchDistributions(_admin, 1, 50).TotalCount);                        // filtresiz
    }

    [Fact]
    public void YKT6_Iptal_Edilenler_Varsayilan_Gizli()
    {
        DepoyaKoy(10_000m);
        Dagit(_aracA, Gun(2026, 5, 1), 5m, 190m, "DURAN");
        Dagit(_aracA, Gun(2026, 5, 2), 5m, 200m, "IPTAL");

        var iptalEdilecek = _fuel.ListDistributions(_admin, 50).First(x => x.Note == "IPTAL");
        _fuel.CancelDistribution(_admin, iptalEdilecek.Id, "test");

        // Mevcut davranış KORUNMALI: iptal edilen varsayılan olarak listede YOK.
        Assert.Equal(1, _fuel.SearchDistributions(_admin, 1, 50).TotalCount);
        Assert.Equal(2, _fuel.SearchDistributions(_admin, 1, 50, includeCancelled: true).TotalCount);
    }

    [Fact]
    public void YKT7_Sayfa_Boyutu_Ust_Siniri_Var()
    {
        DepoyaKoy(1_000m);
        Dagit(_aracA, Gun(2026, 5, 1), 5m, 210m);

        // Tek istekle tüm tabloyu çekmek mümkün olmamalı (sayfalamanın anlamı kalmazdı).
        var r = _fuel.SearchDistributions(_admin, 1, pageSize: 100_000);
        Assert.True(r.PageSize <= 500, $"Sayfa boyutu sınırlanmadı: {r.PageSize}");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
