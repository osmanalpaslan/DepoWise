using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ S1 (ADR-118) — KALICI EŞİTLEME HATALARI KUYRUĞU KİLİTLEMEZ ═══
///
/// <b>Saha durumu:</b> firma verisi sıfırlandıktan sonra yereldeki 6 test satırı sunucuya gitmeye
/// çalışıyordu: biri yinelenen kategori, dördü ebeveyni silinmiş şablon satırı, biri ebeveyni silinmiş
/// bakım malzemesi. Bunlar <b>hiçbir denemede</b> başarılı olamaz; buna rağmen "atlandı" sayıldıkları
/// için gönderim damgası ilerlemiyor, 5 turdan sonra da temizlenemeyen kalıcı bir uyarı bırakıyorlardı.
///
/// Bu testler iki garantiyi kilitler:
/// <list type="bullet">
///   <item>öksüz çocuk satırı veritabanına HİÇ gönderilmez (yabancı anahtar hatası oluşmaz),</item>
///   <item>kalıcı olarak atlanan satırlar istemcide "yeniden denenecek" SAYILMAZ.</item>
/// </list>
/// </summary>
public class SyncPermanentSkipTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private const string Co = "SNKP-CO";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public SyncPermanentSkipTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_snkp_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        Sql($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','A',1,1,1,0);");
    }

    private void Sql(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private long Scalar(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 1 · ÖKSÜZ ÇOCUK SATIRI
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ P1 — Ebeveyni (araç şablonu) sunucuda OLMAYAN şablon-malzeme satırı <b>kalıcı olarak</b>
    /// atlanır: veritabanına hiç yazılmaz ve "yeniden denenecek" sayılmaz.
    /// </summary>
    [Fact]
    public void P1_Oksuz_Sablon_Malzemesi_Kalici_Atlanir()
    {
        var svc = new BusinessSyncService(_factory, _clock);
        var payload = Payload($$"""
        {
          "machineId": "TEST",
          "tables": {
            "vehicle_template_materials": [
              { "template_id": "YOK-SABLON", "material_id": "YOK-MALZEME", "quantity": "1" }
            ]
          }
        }
        """);

        var res = svc.Apply(Co, payload);

        Assert.Equal(0, res.Upserted);
        Assert.Equal(1, res.Skipped);
        Assert.Equal(1, res.PermanentSkipped);                 // ⭐ tekrar denemek anlamsız
        Assert.Contains(res.Errors, e => e.Contains("sunucuda yok"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM vehicle_template_materials;"));
    }

    /// <summary>P2 — Ebeveyni VAR olan satır normal şekilde uygulanır (kontrol geçerli veriyi engellemez).</summary>
    [Fact]
    public void P2_Ebeveyni_Olan_Satir_Uygulanir()
    {
        // Şablon + malzeme önce oluşturulur (ebeveynler).
        Sql($"INSERT INTO vehicle_templates(id,company_id,name,created_at,updated_at,version,is_deleted) " +
            $"VALUES('T1','{Co}','Şablon',1,1,1,0);");
        Sql($"INSERT INTO materials(id,company_id,code,name,unit_id,created_at,updated_at,version,is_deleted) " +
            $"VALUES('M1','{Co}','K1','Malzeme',NULL,1,1,1,0);");

        var svc = new BusinessSyncService(_factory, _clock);
        var payload = Payload($$"""
        {
          "machineId": "TEST",
          "tables": {
            "vehicle_template_materials": [
              { "template_id": "T1", "material_id": "M1", "quantity": "2" }
            ]
          }
        }
        """);

        var res = svc.Apply(Co, payload);

        Assert.Equal(0, res.PermanentSkipped);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM vehicle_template_materials;"));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 2 · İSTEMCİ KARARI (kaynak kilidi — test projesi masaüstüne referans vermiyor)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ P3 — İstemci KALICI atlananları "yeniden denenecek" saymaz: gönderim damgası ilerler ve
    /// kuyruk kilitlenmez. Sahada kuyruğu kilitleyen tam olarak buydu.
    /// Ayrıca eski sunucu uyumu korunur: alan yoksa 0 kalır → davranış eskisiyle birebir aynıdır.
    /// </summary>
    [Fact]
    public void P3_Istemci_Kalici_Atlananlari_Yeniden_Denemez()
    {
        var dir = AppContext.BaseDirectory;
        for (int k = 0; k < 8 && dir is not null; k++)
        {
            if (File.Exists(Path.Combine(dir, "DepoWise.sln"))) break;
            dir = Directory.GetParent(dir)?.FullName;
        }
        var src = File.ReadAllText(Path.Combine(dir!, "src", "DepoWise.Desktop", "BusinessSyncPushService.cs"));

        // Yeniden denenecek = atlanan − kalıcı atlanan
        Assert.Contains("public int Retryable => System.Math.Max(0, Skipped - PermanentSkipped);", src);
        // "Sorun var" kararı YALNIZ yeniden denenecek satırlara bakar.
        Assert.Contains("public bool HasProblem => Retryable > 0;", src);
        // Sunucu alanı okunur; yoksa 0 kalır (eski sunucu uyumu).
        Assert.Contains("permanentSkipped", src);
        Assert.Contains("int upserted = 0, skipped = 0, permanent = 0;", src);
    }

    /// <summary>
    /// ⭐ P4 — PostgreSQL KURTARMA YOLUNDA çift sayım olmamalı. Hızlı yol bir satırda patlayınca tablo
    /// geri alınır ve satırlar BAŞTAN uygulanır; kalıcı sayaç sıfırlanmazsa aynı satırlar iki kez
    /// sayılır, <c>PermanentSkipped &gt; Skipped</c> olur ve istemci "yeniden denenecek satır yok"
    /// sonucuna varıp GERÇEKTEN yeniden denenmesi gereken satırları sessizce düşürür (veri kaybı).
    ///
    /// Kaynak kilidi: üç sayaç (up/sk/perm) kurtarma yolunda BİRLİKTE sıfırlanmalı.
    /// </summary>
    [Fact]
    public void P4_Kurtarma_Yolunda_Kalici_Sayac_Cift_Saymaz()
    {
        var dir = AppContext.BaseDirectory;
        for (int k = 0; k < 8 && dir is not null; k++)
        {
            if (File.Exists(Path.Combine(dir, "DepoWise.sln"))) break;
            dir = Directory.GetParent(dir)?.FullName;
        }
        var src = File.ReadAllText(Path.Combine(dir!, "src", "DepoWise.Infrastructure", "Sync", "BusinessSyncService.cs"));
        Assert.Contains("up = 0; sk = 0; perm = 0;", src);
        // "perm = 0" olmadan sıfırlama satırı KALMAMALI (çift sayım kapısı).
        Assert.DoesNotContain("up = 0; sk = 0;" + Environment.NewLine, src);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
