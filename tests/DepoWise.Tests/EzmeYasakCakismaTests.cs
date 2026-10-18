using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-18 (AR-GE 2026-10-17, kullanıcı kararları K-04 a + K-07 a) — YAKIT / BAKIM / TALEPTE EZME YOK ═══
///
///  EZY1 — Eşzamanlı düzenlenen YAKIT kaydı: bilgisayarın daha yeni sürümü bile sunucuyu EZMEZ; çakışma listesine
///         "kaybeden" olarak yazılır; satır yeniden deneme/red listesine girmez
///  EZY2 — Sunucu sürümü kaydın zamanı ilerletilerek yeniden yayılır → bilgisayar sonraki çekimde sunucu sürümünü alır
///  EZY3 — TALEP (onay durumu): ilk ulaşan onay kazanır; sonra gelen "ret" çakışma listesine düşer (K-04)
///  EZY4 — Eşzamanlı OLMAYAN düzenleme (sunucu son gönderimden beri değişmedi) normal uygulanır
///  EZY5 — Malzeme gibi kart tablolarında davranış DEĞİŞMEDİ (son yazan kazanır + çakışma kaydı)
///  EZY6 — Yönetici "Üzerine Yazılanı Kazanan Yap" ile bilgisayar sürümünü seçebilir (yakıt kaydında)
/// </summary>
public class EzmeYasakCakismaTests : IDisposable
{
    private const string Co = "EZY";
    private readonly List<string> _yollar = new();
    private readonly SqliteConnectionFactory _srv;
    private readonly TestClock _clock = new();

    private sealed class TestClock : IClock
    { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000); }

    public EzmeYasakCakismaTests()
    {
        _srv = Db("srv", sunucu: true);
        // Bilgisayarın son gönderimi = 100 → 100'den sonra iki tarafta da değişen kayıt "eşzamanlı" sayılır.
        Exec(_srv, $"INSERT INTO sync_devices(id,company_id,device_name,status,last_business_push_at,created_at,updated_at,version) VALUES('dev1','{Co}','MPC','active',100,1,1,1);");
        Exec(_srv, $"INSERT INTO vehicles(id,company_id,internal_code,current_meter,meter_unit,status,created_at,updated_at,version,is_deleted) VALUES('V1','{Co}','A-1','0','km','active',1,1,1,0);");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var y in _yollar) try { File.Delete(y); } catch { }
    }

    private SqliteConnectionFactory Db(string ad, bool sunucu = false)
    {
        var yol = Path.Combine(Path.GetTempPath(), $"dw_ezy_{ad}_" + Guid.NewGuid().ToString("N") + ".db");
        _yollar.Add(yol);
        var f = new SqliteConnectionFactory(yol);
        new MigrationRunner(f).Run(); if (sunucu) Migration099_SyncTxid.SunucuRoluKur(f);
        Exec(f, 
                $"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        return f;
    }

    private static void Exec(SqliteConnectionFactory f, string sql)
    {
        using var c = f.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string? Tek(SqliteConnectionFactory f, string sql)
    {
        using var c = f.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Yakit(string litre, long guncelleme) =>
        $"('F1','{Co}','V1','{litre}',1,'op-f1',1,{guncelleme},1,0)";

    private static JsonElement YakitPaketi(string litre, long guncelleme) => JsonDocument.Parse($$"""
        { "machineId": "MPC", "tables": { "fuel_distributions": [ { "id": "F1", "vehicle_id": "V1", "liters": "{{litre}}",
          "distribution_date": 1, "operation_id": "op-f1", "created_at": 1, "updated_at": {{guncelleme}}, "version": 1, "is_deleted": 0 } ] } }
        """).RootElement.Clone();

    private const string YakitKolon = "INSERT INTO fuel_distributions(id,company_id,vehicle_id,liters,distribution_date,operation_id,created_at,updated_at,version,is_deleted) VALUES ";

    [Fact]
    public void EZY1_EZY2_Yakit_Sunucu_Surumu_Korunur_Ve_Yeniden_Yayilir()
    {
        Exec(_srv, YakitKolon + Yakit("40", 200));    // web 200'de 40 L yaptı
        var sync = new BusinessSyncService(_srv, _clock);
        var (_, ufuk) = sync.BuildDeltaV2(Co, 0);

        var res = sync.Apply(Co, YakitPaketi("55", 300));   // bilgisayar 300'de (daha yeni) 55 L yaptı
        Assert.Equal("40", Tek(_srv, "SELECT liters FROM fuel_distributions WHERE id='F1';"));   // ⭐ EZİLMEDİ
        Assert.Empty(res.Rejected!);
        Assert.Equal(0, res.Skipped);                                                          // yeniden deneme döngüsü yok
        var c = Assert.Single(sync.ListConflicts(Co));
        Assert.Equal(("fuel_distributions", "admin"), (c.EntityType, c.Winner));
        Assert.Equal("Yakıt dağıtımı", c.EntityLabel);
        // Ekran "son yazan kazandı / admin (web)" DEMEZ: sunucudaki ilk ulaşan sürüm korundu (iki ortam denetimi 2026-10-18).
        Assert.True(c.SunucuKorundu);
        Assert.Equal("Sunucudaki (ilk ulaşan) sürüm korundu", c.WinnerText);
        Assert.Equal("Masaüstü (sonra gelen)", c.LoserWho);
        Assert.Contains("55", c.LoserJson);

        // EZY2: sunucu sürümü bilgisayarın sürümünün ÜSTÜNE taşındı ve yeniden "değişti" sayıldı.
        Assert.True(long.Parse(Tek(_srv, "SELECT updated_at FROM fuel_distributions WHERE id='F1';")!) > 300);
        var pc = Db("pc");
        Exec(pc, $"INSERT INTO vehicles(id,company_id,internal_code,current_meter,meter_unit,status,created_at,updated_at,version,is_deleted) VALUES('V1','{Co}','A-1','0','km','active',1,1,1,0);");
        Exec(pc, YakitKolon + Yakit("55", 300));
        var (json, _) = sync.BuildDeltaV2(Co, ufuk);
        using var doc = JsonDocument.Parse(json);
        new BusinessSyncService(pc).ApplyPull(Co, doc.RootElement, null);
        Assert.Equal("40", Tek(pc, "SELECT liters FROM fuel_distributions WHERE id='F1';"));     // bilgisayar sunucu sürümünü aldı
    }

    [Fact]
    public void EZY3_Talep_Ilk_Onay_Kazanir()
    {
        Exec(_srv, $"INSERT INTO material_requests(id,company_id,doc_no,status,request_date,created_at,updated_at,version,is_deleted) VALUES('R1','{Co}','TLP-1','approved',1,1,200,2,0);");
        var res = new BusinessSyncService(_srv, _clock).Apply(Co, JsonDocument.Parse($$"""
            { "machineId": "MPC", "tables": { "material_requests": [ { "id": "R1", "doc_no": "TLP-1", "status": "rejected",
              "request_date": 1, "created_at": 1, "updated_at": 250, "version": 2, "is_deleted": 0 } ] } }
            """).RootElement);
        Assert.Equal("approved", Tek(_srv, "SELECT status FROM material_requests WHERE id='R1';"));
        Assert.Empty(res.Rejected!);
        Assert.Contains(new BusinessSyncService(_srv).ListConflicts(Co), x => x.EntityId == "R1" && x.Winner == "admin");
    }

    [Fact]
    public void EZY4_Eszamanli_Olmayan_Duzenleme_Uygulanir()
    {
        Exec(_srv, YakitKolon + Yakit("40", 50));   // sunucu kaydı bilgisayarın son gönderiminden (100) ÖNCE
        var res = new BusinessSyncService(_srv, _clock).Apply(Co, YakitPaketi("55", 300));
        Assert.Equal("55", Tek(_srv, "SELECT liters FROM fuel_distributions WHERE id='F1';"));
        Assert.Empty(new BusinessSyncService(_srv).ListConflicts(Co));
        Assert.Equal(1, res.Upserted);
    }

    [Fact]
    public void EZY5_Kart_Tablolarinda_Davranis_Degismedi()
    {
        Exec(_srv, $"INSERT INTO materials(id,company_id,code,name,created_at,updated_at,version,is_deleted) VALUES('M1','{Co}','K-1','Web adı',1,200,1,0);");
        new BusinessSyncService(_srv, _clock).Apply(Co, JsonDocument.Parse("""
            { "machineId": "MPC", "tables": { "materials": [ { "id": "M1", "code": "K-1", "name": "Masaüstü adı",
              "created_at": 1, "updated_at": 300, "version": 1, "is_deleted": 0 } ] } }
            """).RootElement);
        Assert.Equal("Masaüstü adı", Tek(_srv, "SELECT name FROM materials WHERE id='M1';"));   // son yazan kazanır (bilinçli)
        var m = Assert.Single(new BusinessSyncService(_srv).ListConflicts(Co), x => x.EntityId == "M1" && x.Winner == "device");
        Assert.False(m.SunucuKorundu);
        Assert.Equal("Personel (masaüstü) kazandı", m.WinnerText);   // kart tablolarında metin aynen
    }

    [Fact]
    public void EZY6_Yonetici_Bilgisayar_Surumunu_Secebilir()
    {
        Exec(_srv, YakitKolon + Yakit("40", 200));
        var sync = new BusinessSyncService(_srv, _clock);
        sync.Apply(Co, YakitPaketi("55", 300));
        var c = Assert.Single(sync.ListConflicts(Co));
        Assert.True(c.CanPromoteLoser);
        var yonetici = new DepoWise.Application.Security.SessionContext("u1", Co,
            new[] { DepoWise.Application.Security.RoleKeys.CompanyAdmin }, DepoWise.Application.Security.PermissionSet.Empty);
        sync.PromoteLoser(yonetici, c.Id);
        Assert.Equal("55", Tek(_srv, "SELECT liters FROM fuel_distributions WHERE id='F1';"));
    }
}
