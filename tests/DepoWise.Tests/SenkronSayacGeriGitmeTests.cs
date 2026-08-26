using System.Text.Json;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ SNK-01 · SENKRON YOLU ARAÇ SAYACINI GERİYE ALABİLİYORDU ═══ (denetim 2026-08-26, üçüncü tur)
///
/// <b>Mimari kural</b> (CLAUDE.md §4): <i>"Stok, sayaç, yakıt, bakım ve onayda LWW yasaktır."</i>
/// Doğrudan yol bu kurala uyuyor: <c>VehicleService.SetMeter</c> geriye gitmeyi
/// <c>MeterBackwardException</c> ile reddeder ve <c>MeterRule</c> tek doğru kaynaktır.
///
/// <b>Bulunan durum.</b> <c>POST /api/sync/business-push</c> araç satırını <b>düz LWW ile</b> upsert
/// ediyordu; <c>current_meter</c> için hiçbir kontrol yoktu. Gerçek istekle doğrulandı: sunucudaki
/// sayaç <b>1000 iken 10'a düştü</b>, yanıt <c>{"upserted":1,"errors":[]}</c> — <b>sessiz</b>.
///
/// <b>Neden önemli:</b> sayaç, yakıt tüketimi (km/saat başına) ve bakım periyodu hesaplarının girdisidir.
/// Geriye giden bir sayaç yanlış tüketim raporu üretir ve <b>bakım uyarılarının kaçırılmasına</b> yol açar.
/// Çevrimdışı çalışmış, sayacı eski kalmış bir masaüstü bunu farkında olmadan tetikleyebilir.
///
/// <b>Düzeltme (mevcut kuralın aynısı, yeni kural YOK).</b> Senkron yolunda <c>MeterRule.ShouldAdvance</c>
/// uygulanır: gelen değer büyükse ilerler, <b>küçükse DOKUNULMAZ</b> (satırın diğer alanları normal
/// uygulanır — meşru düzenlemeler kaybolmaz). Bu, bakım/yakıt modüllerinin zaten kullandığı kuraldır.
///
/// ⚠️ Kapsam: yalnız <b>istemci → sunucu</b> yönü. Sunucu → masaüstü (pull) yönü sunucu-otoriteldir ve
/// bilinçli olarak DEĞİŞTİRİLMEDİ.
/// </summary>
public class SenkronSayacGeriGitmeTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"dw_snk01_{Guid.NewGuid():N}.db");
    private readonly SqliteConnectionFactory _factory;
    private readonly BusinessSyncService _sync;
    private const string Co = "SNK-CO";
    private const string Arac = "arac-1";

    public SenkronSayacGeriGitmeTests()
    {
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _sync = new BusinessSyncService(_factory);

        Calistir("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@c,'Firma',1,1,1,0);",
            ("@c", Co));
        Calistir("INSERT INTO vehicles(id,company_id,internal_code,plate,current_meter,meter_unit,status," +
                 "created_at,updated_at,version,is_deleted) VALUES(@id,@c,'AR-1','34ABC01','1000','km','active',1,1000,1,0);",
            ("@id", Arac), ("@c", Co));
    }

    private void Calistir(string sql, params (string Ad, object Deger)[] p)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (ad, deger) in p) cmd.AddWithValue(ad, deger);
        cmd.ExecuteNonQuery();
    }

    private string Oku(string kolon)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {kolon} FROM vehicles WHERE id=@id;";
        cmd.AddWithValue("@id", Arac);
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }

    /// <summary>Masaüstünün gönderdiği anlık görüntü (snapshot) biçimi.</summary>
    private BusinessSyncService.ApplyResult Push(string sayac, string plaka = "34ABC01", long guncelleme = 5000)
    {
        var json = JsonSerializer.Serialize(new
        {
            machineId = "DENEY",
            tables = new
            {
                vehicles = new[]
                {
                    new
                    {
                        id = Arac, company_id = Co, internal_code = "AR-1", plate = plaka,
                        current_meter = sayac, meter_unit = "km", status = "active",
                        created_at = 1L, updated_at = guncelleme, version = 9L, is_deleted = 0,
                    },
                },
            },
        });
        using var doc = JsonDocument.Parse(json);
        return _sync.Apply(Co, doc.RootElement.Clone());
    }

    /// <summary>⭐ SNK-01 — GERİYE giden sayaç sunucuda uygulanmamalı.</summary>
    [Fact]
    public void SNK01a_Sayac_Geriye_Alinamaz()
    {
        Push("10");
        Assert.Equal("1000", Oku("current_meter"));
    }

    /// <summary>⭐ SNK-01 — sayaç geri tutulsa bile satırın DİĞER alanları uygulanmalı (veri kaybı yok).</summary>
    [Fact]
    public void SNK01b_Diger_Alanlar_Yine_Uygulanir()
    {
        Push("10", plaka: "06YENI99");

        Assert.Equal("1000", Oku("current_meter"));
        Assert.Equal("06YENI99", Oku("plate"));
    }

    /// <summary>Regresyon kilidi: İLERİ giden sayaç normal şekilde uygulanmaya devam eder.</summary>
    [Fact]
    public void SNK01c_Ileri_Giden_Sayac_Uygulanir()
    {
        Push("2500");
        Assert.Equal("2500", Oku("current_meter"));
    }

    /// <summary>Regresyon kilidi: AYNI değer sorun çıkarmaz.</summary>
    [Fact]
    public void SNK01d_Ayni_Sayac_Sorunsuz()
    {
        var r = Push("1000");
        Assert.Empty(r.Errors);
        Assert.Equal("1000", Oku("current_meter"));
    }

    /// <summary>Regresyon kilidi: sayaç alanı HİÇ gönderilmezse mevcut değer korunur.</summary>
    [Fact]
    public void SNK01e_Sayacsiz_Satir_Mevcut_Degeri_Bozmaz()
    {
        var json = JsonSerializer.Serialize(new
        {
            machineId = "DENEY",
            tables = new
            {
                vehicles = new[]
                {
                    new { id = Arac, company_id = Co, internal_code = "AR-1", plate = "34YENI01",
                          created_at = 1L, updated_at = 6000L, version = 10L, is_deleted = 0 },
                },
            },
        });
        using var doc = JsonDocument.Parse(json);
        _sync.Apply(Co, doc.RootElement.Clone());

        Assert.Equal("1000", Oku("current_meter"));
        Assert.Equal("34YENI01", Oku("plate"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
