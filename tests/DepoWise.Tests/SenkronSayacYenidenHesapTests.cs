using System.Text.Json;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ SAY-01 · DÜZELTİLEN HATALI KM SUNUCUDA KALIYORDU ═══ (gerçek olay 2026-11-05, 06 FZ 4147 + 8 araç, Oze İnşaat)
///
/// Kullanıcı hatalı-yüksek km'li yakıt kaydını (125.880) düzeltti. Masaüstü sayacı yerelde doğru değere (116.753) indirdi,
/// ama sunucudaki SNK-01 koruması gelen DÜŞÜK değeri reddetti → sunucu 125.880'de kaldı → çekmede masaüstüne geri indi;
/// yeni fişin "önceki sayacı" hep hatalı geldi (günlükte 22 kez "125880 → 116753").
///
/// Düzeltme: gönderim uygulandıktan sonra sunucu, paketteki yakıt/bakım/sayaç günlüğü/araç satırlarının araçları için
/// sayacı KENDİ geçerli kayıtlarından yeniden hesaplar. SNK-01 (bayat değer sayacı düşüremez) aynen durur.
/// </summary>
public class SenkronSayacYenidenHesapTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"dw_say01_{Guid.NewGuid():N}.db");
    private readonly SqliteConnectionFactory _factory;
    private readonly BusinessSyncService _sync;
    private const string Co = "SAY-CO";
    private const string Baska = "SAY-BASKA";
    private const string Arac = "arac-say";
    private const string BaskaArac = "arac-baska";
    private const string Kayitsiz = "arac-kayitsiz";

    public SenkronSayacYenidenHesapTests()
    {
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _sync = new BusinessSyncService(_factory);

        foreach (var c in new[] { Co, Baska })
            Calistir("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@c,@c,1,1,1,0);", ("@c", c));
        AracEkle(Arac, Co, "125880");
        AracEkle(Kayitsiz, Co, "50000");      // içe aktarılmış sayaç, hiç dayanak kaydı yok
        AracEkle(BaskaArac, Baska, "125880");

        // Sunucudaki hâl (olaydaki gibi): doğru kayıt 116.753 geçerli, hatalı 125.880 kaydı HENÜZ geçerli.
        YakitEkle("f-dogru", Co, Arac, "116582", "116753", 0);
        YakitEkle("f-hatali", Co, Arac, "125759", "125880", 0);
        YakitEkle("f-baska", Baska, BaskaArac, "116582", "116753", 0);
        YakitEkle("f-baska-hatali", Baska, BaskaArac, "125759", "125880", 1);

        // Gönderen masaüstü: son gönderimi sunucudaki kayıtlardan SONRA (gerçekteki gibi) → iptal çakışma sayılmaz.
        Calistir("INSERT INTO sync_devices(id,company_id,device_name,status,last_business_push_at,created_at,updated_at,version) " +
                 "VALUES('dev-1',@c,'DENEY','active',2000,1,1,1);", ("@c", Co));
    }

    private void AracEkle(string id, string co, string sayac) =>
        Calistir("INSERT INTO vehicles(id,company_id,internal_code,plate,current_meter,meter_unit,status," +
                 "created_at,updated_at,version,is_deleted) VALUES(@id,@c,@id,'34ABC01',@m,'km','active',1,1000,1,0);",
            ("@id", id), ("@c", co), ("@m", sayac));

    private void YakitEkle(string id, string co, string arac, string onceki, string sayac, int silindi) =>
        Calistir("INSERT INTO fuel_distributions(id,company_id,vehicle_id,prev_meter,current_meter,liters,unit_price," +
                 "currency_code,fx_rate,distribution_date,operation_id,created_at,updated_at,version,is_deleted) " +
                 "VALUES(@id,@c,@v,@p,@m,'100','40','TRY','1',1000,@id,1000,1000,1,@d);",
            ("@id", id), ("@c", co), ("@v", arac), ("@p", onceki), ("@m", sayac), ("@d", silindi));

    private void Calistir(string sql, params (string Ad, object Deger)[] p)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (ad, deger) in p) cmd.AddWithValue(ad, deger);
        cmd.ExecuteNonQuery();
    }

    private string Sayac(string arac)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT current_meter FROM vehicles WHERE id=@id;";
        cmd.AddWithValue("@id", arac);
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }

    private long LogSayisi(string arac, string kaynak)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM vehicle_meter_logs WHERE vehicle_id=@v AND source=@s;";
        cmd.AddWithValue("@v", arac);
        cmd.AddWithValue("@s", kaynak);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private BusinessSyncService.ApplyResult Push(string co, object tables)
    {
        var json = JsonSerializer.Serialize(new { machineId = "DENEY", tables });
        using var doc = JsonDocument.Parse(json);
        return _sync.Apply(co, doc.RootElement.Clone());
    }

    private static object AracSatiri(string id, string co, string sayac, long guncelleme = 5000) => new
    {
        id, company_id = co, internal_code = id, plate = "34ABC01", current_meter = sayac, meter_unit = "km",
        status = "active", created_at = 1L, updated_at = guncelleme, version = 9L, is_deleted = 0,
    };

    private static object IptalSatiri(string id, string co, string arac) => new
    {
        id, company_id = co, vehicle_id = arac, prev_meter = "125759", current_meter = "125880", liters = "100",
        unit_price = "40", currency_code = "TRY", fx_rate = "1", distribution_date = 1000L, operation_id = id,
        created_at = 1000L, updated_at = 6000L, version = 2L, is_deleted = 1,
    };

    /// <summary>⭐ SAY-01 a — olayın kendisi: masaüstü hatalı kaydı iptal edip düşük sayacı gönderir → sunucu doğru değere iner.</summary>
    [Fact]
    public void SAY01a_Duzeltilen_Hatali_Kayit_Sunucu_Sayacini_Indirir()
    {
        var r = Push(Co, new
        {
            vehicles = new[] { AracSatiri(Arac, Co, "116753") },
            fuel_distributions = new[] { IptalSatiri("f-hatali", Co, Arac) },
        });

        Assert.Empty(r.Errors);
        Assert.Equal("116753", Sayac(Arac));
        Assert.Equal(1, LogSayisi(Arac, "recalc:sync"));   // iz kaybolmaz
    }

    /// <summary>⭐ SAY-01 b — iptal önceden ulaşmış, yalnız masaüstünün sayaç günlüğü gelir (sunucudaki "zehirli" hâlin onarımı).</summary>
    [Fact]
    public void SAY01b_Sayac_Gunlugu_Gelince_Zehirli_Deger_Onarilir()
    {
        Calistir("UPDATE fuel_distributions SET is_deleted=1 WHERE id='f-hatali';");
        Push(Co, new
        {
            vehicle_meter_logs = new[]
            {
                new { id = "log-1", company_id = Co, vehicle_id = Arac, old_value = "125880", new_value = "116753",
                      source = "recalc:fuel_form", created_at = 7000L },
            },
        });
        Assert.Equal("116753", Sayac(Arac));
    }

    /// <summary>⭐ SAY-01 c — bayat sayaçlı cihaz yalnız araç kartını gönderse de hatalı değer GERİ yükselmez.</summary>
    [Fact]
    public void SAY01c_Bayat_Cihaz_Hatali_Degeri_Geri_Yukseltemez()
    {
        Calistir("UPDATE fuel_distributions SET is_deleted=1 WHERE id='f-hatali';");
        Calistir("UPDATE vehicles SET current_meter='116753' WHERE id=@id;", ("@id", Arac));

        Push(Co, new { vehicles = new[] { AracSatiri(Arac, Co, "125880", guncelleme: 8000) } });

        Assert.Equal("116753", Sayac(Arac));
    }

    /// <summary>Regresyon kilidi: SNK-01 durur — geçerli kayıt 125.880 iken bayat düşük değer sayacı düşüremez.</summary>
    [Fact]
    public void SAY01d_Gecerli_Yuksek_Kayit_Varken_Sayac_Dusmez()
    {
        Push(Co, new { vehicles = new[] { AracSatiri(Arac, Co, "116753") } });
        Assert.Equal("125880", Sayac(Arac));
    }

    /// <summary>Veri kaybı kilidi: hiç dayanak kaydı olmayan aracın (içe aktarılmış) sayacı sıfırlanmaz.</summary>
    [Fact]
    public void SAY01e_Dayanaksiz_Arac_Sayaci_Sifirlanmaz()
    {
        Push(Co, new { vehicles = new[] { AracSatiri(Kayitsiz, Co, "50000") } });
        Assert.Equal("50000", Sayac(Kayitsiz));
        Assert.Equal(0, LogSayisi(Kayitsiz, "recalc:sync"));
    }

    /// <summary>Firma kapısı: başka firmanın aracı, bu firmanın gönderimiyle yeniden hesaplanmaz.</summary>
    [Fact]
    public void SAY01f_Baska_Firmanin_Aracina_Dokunulmaz()
    {
        Push(Co, new
        {
            vehicle_meter_logs = new[]
            {
                new { id = "log-x", company_id = Co, vehicle_id = BaskaArac, old_value = "1", new_value = "2",
                      source = "recalc:fuel_form", created_at = 7000L },
            },
        });
        Assert.Equal("125880", Sayac(BaskaArac));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            try { File.Delete(f); } catch { }
    }
}
