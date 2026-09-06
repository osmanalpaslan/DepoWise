using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ YAKIT DAĞITIMI DÜZELTME (kullanıcı isteği + kararı 2026-09-02) ═══
///
/// Kullanıcı: "yakıt dağıtımları ekranında girilen kayıtlarda güncelleme yapmam gerekebiliyor ...
/// bağlantılı olduğu her alanda güncellenmeli." Karar: <b>güvenli yöntem</b> — kaydın üzerine YAZILMAZ;
/// eski kayıt İPTAL edilir ve düzeltilmiş YENİ kayıt oluşturulur, ikisi de TEK transaction'da
/// (CLAUDE.md §4: yakıt/stok/sayaçta LWW yasak, operasyonel kayıt fiziksel silinmez).
///
/// Kilitlenen davranışlar:
///  YD1 — Düzeltme eski kaydı iptal eder + yeni kayıt oluşturur; depo bakiyesi YENİ litreye göre olur.
///  YD2 — Araç sayacı GERİ ALINMAZ (yalnız ileri gider); başlangıç sayacı yeni kayda TAŞINIR (Y2 zinciri).
///  YD3 — Aynı operation_id ile ikinci çağrı yeni kayıt OLUŞTURMAZ (idempotent).
///  YD4 — İptal edilmiş kayıt düzeltilemez.
///  YD5 — <b>ATOMİKLİK:</b> yeni kayıt reddedilirse (depo yetersiz) eski kayıt İPTAL EDİLMİŞ KALMAZ.
///  YD6 — Tenant: başka firmanın kaydı düzeltilemez.
///  YD7 — Yetki: "Ters Kayıt" (btn-reverse) özel butonu olmadan düzeltilemez.
/// </summary>
public class FuelUpdateTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly FuelService _fuel;
    private readonly VehicleService _vehicles;
    private readonly SessionContext _admin;

    public FuelUpdateTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_fuelupd_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _fuel = new FuelService(_factory, _clock);
        _vehicles = new VehicleService(_factory, _clock);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private string Depot(decimal liters, decimal price = 40m)
        => _fuel.AddDepotEntry(_admin, new NewDepotEntry(liters, price), "op-depot-" + Guid.NewGuid().ToString("N"));

    private string Vehicle(decimal meter = 10_000m)
        => _vehicles.Create(_admin, new NewVehicle("ARC-" + Guid.NewGuid().ToString("N")[..6], CurrentMeter: meter));

    private string Distribute(string vehicleId, decimal liters, decimal currentMeter)
        => _fuel.Distribute(_admin, new NewDistribution(vehicleId, liters, currentMeter),
            "op-dist-" + Guid.NewGuid().ToString("N"));

    private decimal VehicleMeter(string id) => _vehicles.Get(_admin, id)!.CurrentMeter;

    private FuelDistributionRow? Row(string id) =>
        _fuel.ListDistributions(_admin, 500, includeCancelled: true).FirstOrDefault(x => x.Id == id);

    /// <summary>YD1 — Düzeltme: eski kayıt iptal olur, yeni kayıt oluşur, bakiye YENİ litreye göre hesaplanır.</summary>
    [Fact]
    public void YD1_Duzeltme_Eskiyi_Iptal_Eder_Yeniyi_Olusturur()
    {
        Depot(500m);
        var v = Vehicle(10_000m);
        var eski = Distribute(v, 100m, 10_050m);
        Assert.Equal(400m, _fuel.GetDepotBalance(_admin));   // 500 − 100

        var yeni = _fuel.UpdateDistribution(_admin, eski,
            new NewDistribution(v, 60m, 10_050m), "op-fix-1", "Litre yanlış girilmiş");

        Assert.NotEqual(eski, yeni);
        Assert.True(Row(eski)!.IsCancelled);
        Assert.False(Row(yeni)!.IsCancelled);
        Assert.Equal(60m, Row(yeni)!.Liters);
        // Bakiye: eski 100 L geri döndü, yeni 60 L düşüldü → 500 − 60.
        Assert.Equal(440m, _fuel.GetDepotBalance(_admin));
        // Aktif listede (iptaller gizli) TEK kayıt kalır — mükerrer görünmez.
        Assert.Single(_fuel.ListDistributions(_admin, 500).Where(x => x.VehicleId == v));
    }

    /// <summary>
    /// YD2 — Düzeltmede başlangıç sayacı yeni kayda TAŞINIR; araç sayacı ise GEÇERLİ kayda çekilir.
    ///
    /// ⭐ <b>KARAR DEĞİŞTİ — FAZ 4.1 (kullanıcı talimatı 2026-09-06).</b> Bu test eskiden "araç sayacı
    /// GERİ ALINMAZ" diyordu (10.500 kalırdı). Kullanıcının canlı veride yaşadığı hata tam olarak buydu:
    /// <i>"yanlış sayaç girildi, kayıt düzeltildi ama yanlış sayaç kalmaya devam ediyor"</i>. Sayaç artık
    /// <c>VehicleMeterService</c> ile GEÇERLİ kayıtlardan türetilir → iptal edilen 10.500 sayılmaz,
    /// geçerli düzeltme 10.200 geçerli olur (elle bildirilen 10.000 tabandır).
    ///
    /// Başlangıç sayacının taşınması DEĞİŞMEDİ: rapor km'si (10.000 → 10.200) bozulmaz.
    /// </summary>
    [Fact]
    public void YD2_Duzeltmede_Sayac_Gecerli_Kayda_Cekilir()
    {
        Depot(500m);
        var v = Vehicle(10_000m);
        var eski = Distribute(v, 100m, 10_500m);
        Assert.Equal(10_500m, VehicleMeter(v));               // sayaç ilerledi
        Assert.Equal(10_000m, Row(eski)!.PrevMeter);          // başlangıç: aracın o anki sayacı

        // Düzeltmede güncel sayaç GERİYE çekiliyor (yanlış girilmişti).
        var yeni = _fuel.UpdateDistribution(_admin, eski,
            new NewDistribution(v, 100m, 10_200m), "op-fix-2", "Sayaç yanlış okunmuş");

        Assert.Equal(10_000m, Row(yeni)!.PrevMeter);          // başlangıç TAŞINDI (araçtan yeniden okunmadı)
        Assert.Equal(10_200m, Row(yeni)!.CurrentMeter);
        // ⭐ FAZ 4.1: iptal edilen kayıt artık sayılmaz → araç sayacı GEÇERLİ kayda çekilir.
        Assert.Equal(10_200m, VehicleMeter(v));
    }

    /// <summary>YD3 — Aynı operation_id ile ikinci çağrı yeni kayıt oluşturmaz (ağ yeniden denemesi).</summary>
    [Fact]
    public void YD3_Idempotent_Ikinci_Cagri_Yeni_Kayit_Olusturmaz()
    {
        Depot(500m);
        var v = Vehicle();
        var eski = Distribute(v, 100m, 10_050m);

        var ilk = _fuel.UpdateDistribution(_admin, eski, new NewDistribution(v, 60m, 10_050m), "op-fix-3", "düzeltme");
        var ikinci = _fuel.UpdateDistribution(_admin, eski, new NewDistribution(v, 60m, 10_050m), "op-fix-3", "düzeltme");

        Assert.Equal(ilk, ikinci);
        Assert.Equal(2, _fuel.ListDistributions(_admin, 500, includeCancelled: true).Count(x => x.VehicleId == v));
        Assert.Equal(440m, _fuel.GetDepotBalance(_admin));   // ikinci çağrı bakiyeyi TEKRAR düşmedi
    }

    /// <summary>YD4 — İptal edilmiş kayıt düzeltilemez (iptal geri alınamaz — Y4 ile tutarlı).</summary>
    [Fact]
    public void YD4_Iptal_Edilmis_Kayit_Duzeltilemez()
    {
        Depot(500m);
        var v = Vehicle();
        var id = Distribute(v, 100m, 10_050m);
        _fuel.CancelDistribution(_admin, id, "hatalı");

        Assert.Throws<InvalidOperationException>(() =>
            _fuel.UpdateDistribution(_admin, id, new NewDistribution(v, 60m, 10_050m), "op-fix-4", "düzeltme"));
    }

    /// <summary>YD5 — <b>ATOMİKLİK:</b> yeni kayıt reddedilirse eski kayıt iptal edilmiş KALMAZ ve
    /// bakiye bozulmaz. (Yerinde UPDATE yerine iptal+yeniden yazmanın en kritik riski budur.)</summary>
    [Fact]
    public void YD5_Yeni_Kayit_Reddedilirse_Eski_Kayit_Iptal_Kalmaz()
    {
        Depot(200m);
        var v = Vehicle();
        var eski = Distribute(v, 100m, 10_050m);
        Assert.Equal(100m, _fuel.GetDepotBalance(_admin));

        // Depoda (iptal sonrası) 200 L olur; 500 L istemek REDDEDİLİR.
        Assert.Throws<InvalidOperationException>(() =>
            _fuel.UpdateDistribution(_admin, eski, new NewDistribution(v, 500m, 10_050m), "op-fix-5", "düzeltme"));

        Assert.False(Row(eski)!.IsCancelled);                 // eski kayıt AYAKTA
        Assert.Equal(100m, _fuel.GetDepotBalance(_admin));    // bakiye değişmedi
        Assert.Single(_fuel.ListDistributions(_admin, 500, includeCancelled: true).Where(x => x.VehicleId == v));
    }

    /// <summary>YD6 — Tenant: başka firmanın yakıt kaydı düzeltilemez.</summary>
    [Fact]
    public void YD6_Baska_Firmanin_Kaydi_Duzeltilemez()
    {
        Depot(500m);
        var v = Vehicle();
        var eski = Distribute(v, 100m, 10_050m);

        var users = new UserService(_factory, _clock);
        var bUid = users.EnsureInitialAdmin("B", "adminb", "admin123", RoleKeys.CompanyAdmin);
        var b = new SessionContext(bUid, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        Assert.Throws<ForbiddenException>(() =>
            _fuel.UpdateDistribution(b, eski, new NewDistribution(v, 60m, 10_050m), "op-fix-6", "düzeltme"));
        Assert.False(Row(eski)!.IsCancelled);
    }

    /// <summary>YD7 — Yetki: "Ters Kayıt" (btn-reverse) özel butonu olmayan kullanıcı düzeltemez.
    /// Düzeltme bir ters kayıt içerdiği için iptalle AYNI kapıdan geçer.</summary>
    [Fact]
    public void YD7_TersKayit_Butonu_Yoksa_Duzeltilemez()
    {
        Depot(500m);
        var v = Vehicle();
        var eski = Distribute(v, 100m, 10_050m);

        // Yalnız fuel modülüne tam yetki; ÖZEL BUTON verilmedi.
        var izinler = new PermissionSet(new[] { new ModulePermission("fuel", true, true, true, false) });
        var kisitli = new SessionContext("u2", "A", new[] { RoleKeys.Staff }, izinler);

        Assert.Throws<ForbiddenException>(() =>
            _fuel.UpdateDistribution(kisitli, eski, new NewDistribution(v, 60m, 10_050m), "op-fix-7", "düzeltme"));
        Assert.False(Row(eski)!.IsCancelled);
    }

    /// <summary>YD8 — Gerekçe zorunludur (denetim kaydı gerekçesiz kalmaz).</summary>
    [Fact]
    public void YD8_Gerekce_Zorunlu()
    {
        Depot(500m);
        var v = Vehicle();
        var eski = Distribute(v, 100m, 10_050m);

        Assert.Throws<ArgumentException>(() =>
            _fuel.UpdateDistribution(_admin, eski, new NewDistribution(v, 60m, 10_050m), "op-fix-8", "   "));
        Assert.False(Row(eski)!.IsCancelled);
    }

    /// <summary>YD9 — Düzeltme formunu ön-doldurmak için gereken alanlar listede DÖNER
    /// (personel / yakıtı alan / açıklama). Aksi hâlde düzeltmede bu alanlar sessizce boşalırdı.</summary>
    [Fact]
    public void YD9_Liste_Duzeltme_Icin_Gerekli_Alanlari_Dondurur()
    {
        Depot(500m);
        var v = Vehicle();
        var id = _fuel.Distribute(_admin, new NewDistribution(v, 100m, 10_050m,
            PersonnelId: "p-veren", RecipientPersonnelId: "p-alan", Note: "sahaya gönderildi"), "op-dist-9");

        var satir = Row(id)!;
        Assert.Equal("p-veren", satir.PersonnelId);
        Assert.Equal("p-alan", satir.RecipientPersonnelId);
        Assert.Equal("sahaya gönderildi", satir.Note);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
