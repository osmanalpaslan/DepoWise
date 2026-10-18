using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ AR-GE 2026-10-17 — SAYFALI GÖNDERİM (sunucu belleği) + M-81 GEÇİŞTE TAM GÖNDERİM ═══
///
///  GSY1 — Sınırın altındaki paket AYNEN tek sayfa (davranış değişmez)
///  GSY2 — Büyük paket: her satır tam bir kez, tablo sırası korunur, sayfa ≤ sınır (+ ebeveynin çocukları), pushPage bayrakları
///  GSY3 — Ebeveynin (malzeme) çocukları (muadil) AYNI sayfada; aynı çocuk ikinci kez gönderilmez
///  GSY4 — Sunucuda çakışma tabanı (son gönderim anı) yalnız SON sayfadan sonra ilerler → 2. sayfadaki eşzamanlı düzenleme yakalanır
///  GSY5 — Uçtan uca: sayfalarla gönderilen paketin sunucudaki sonucu tek paketle AYNI
///  GSY6 — M-81: sayaç imlecine geçiş bir kez TAM gönderimdir; bekleyen gönderim olarak görünür (kaynak kilidi)
/// </summary>
public class GonderimSayfaTests : IDisposable
{
    private const string Co = "GSY";
    private readonly List<string> _yollar = new();

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var y in _yollar) try { File.Delete(y); } catch { }
    }

    private (SqliteConnectionFactory F, SessionContext S) Db(string ad, bool sunucu = false)
    {
        var yol = Path.Combine(Path.GetTempPath(), $"dw_gsy_{ad}_" + Guid.NewGuid().ToString("N") + ".db");
        _yollar.Add(yol);
        var f = new SqliteConnectionFactory(yol);
        new MigrationRunner(f).Run(); if (sunucu) Migration099_SyncTxid.SunucuRoluKur(f);
        using (var c = f.Create())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = 
                $"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);";
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(f).EnsureInitialAdmin(Co, "admin_" + ad, "admin123", RoleKeys.CompanyAdmin);
        return (f, new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty));
    }

    private static List<(string Tablo, string Ham)> Satirlar(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("tables").EnumerateObject()
            .SelectMany(t => t.Value.EnumerateArray().Select(r => (t.Name, r.GetRawText()))).ToList();
    }

    private static string Paket(SqliteConnectionFactory f, int malzeme, SessionContext s)
    {
        var svc = new MaterialService(f);
        string? onceki = null;
        for (int i = 0; i < malzeme; i++)
        {
            var m = svc.Create(s, new NewMaterial($"G-{i:D5}", $"Malzeme {i}"));
            if (onceki is not null && i % 7 == 0) svc.AddEquivalent(s, m, onceki);
            onceki = m;
        }
        return new BusinessSyncService(f).BuildPushSnapshot(Co, "MPC", -1);
    }

    [Fact]
    public void GSY1_Kucuk_Paket_Aynen_Tek_Sayfa()
    {
        var (f, s) = Db("k");
        var paket = Paket(f, 10, s);
        Assert.Equal(new[] { paket }, BusinessSyncService.GonderimSayfalari(paket, 2000));
    }

    [Fact]
    public void GSY2_GSY3_Buyuk_Paket_Bolunur_Sira_Ve_Cocuklar_Korunur()
    {
        var (f, s) = Db("b");
        var paket = Paket(f, 260, s);
        var tum = Satirlar(paket);
        var sayfalar = BusinessSyncService.GonderimSayfalari(paket, 50);
        Assert.True(sayfalar.Count >= 5);

        var gelen = new List<(string Tablo, string Ham)>();
        for (int i = 0; i < sayfalar.Count; i++)
        {
            using var doc = JsonDocument.Parse(sayfalar[i]);
            var pp = doc.RootElement.GetProperty("pushPage");
            Assert.Equal(i + 1, pp.GetProperty("no").GetInt32());
            Assert.Equal(i == sayfalar.Count - 1, pp.GetProperty("last").GetBoolean());
            Assert.Equal("MPC", doc.RootElement.GetProperty("machineId").GetString());
            var satirlar = Satirlar(sayfalar[i]);
            Assert.True(satirlar.Count(x => x.Tablo != "material_equivalents") <= 50);
            // GSY3: sayfadaki her malzemenin muadilleri aynı sayfada.
            var ids = satirlar.Where(x => x.Tablo == "materials").Select(x => JsonDocument.Parse(x.Ham).RootElement.GetProperty("id").GetString()).ToHashSet();
            foreach (var e in tum.Where(x => x.Tablo == "material_equivalents"))
            {
                var mid = JsonDocument.Parse(e.Ham).RootElement.GetProperty("material_id").GetString();
                if (ids.Contains(mid)) Assert.Contains(e, satirlar);
            }
            gelen.AddRange(satirlar);
        }
        // Her satır TAM bir kez (çocuklar tekrar gönderilmez) ve tablo sırası korunur.
        Assert.Equal(tum.OrderBy(x => x.Tablo).ThenBy(x => x.Ham), gelen.OrderBy(x => x.Tablo).ThenBy(x => x.Ham));
        var anaSira = gelen.Where(x => x.Tablo != "material_equivalents").Select(x => Array.IndexOf(BusinessSyncService.Tables, x.Tablo)).ToList();
        Assert.Equal(anaSira.OrderBy(x => x), anaSira);
    }

    [Fact]
    public void GSY4_Cakisma_Tabani_Son_Sayfaya_Kadar_Ilerlemez()
    {
        var (srv, _) = Db("srv", sunucu: true);
        using (var c = srv.Create())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"INSERT INTO sync_devices(id,company_id,device_name,status,last_business_push_at,created_at,updated_at,version) VALUES('d1','{Co}','MPC','active',100,1,1,1);";
            cmd.ExecuteNonQuery();
        }
        long Taban()
        {
            using var c = srv.Create(); using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT last_business_push_at FROM sync_devices WHERE id='d1';";
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
        var sync = new BusinessSyncService(srv);
        JsonElement Sayfa(bool son) => JsonDocument.Parse($$"""{ "machineId": "MPC", "tables": {}, "pushPage": { "no": 1, "total": 2, "last": {{(son ? "true" : "false")}} } }""").RootElement.Clone();
        sync.Apply(Co, Sayfa(false));
        Assert.Equal(100, Taban());                   // ara sayfa: taban korunur
        sync.Apply(Co, Sayfa(true));
        Assert.True(Taban() > 100);                   // son sayfa: ilerler
        sync.Apply(Co, JsonDocument.Parse("""{ "machineId": "MPC", "tables": {} }""").RootElement);   // eski istemci (alan yok) → eskisi gibi
    }

    [Fact]
    public void GSY5_Sayfali_Gonderim_Sonucu_Tek_Paketle_Ayni()
    {
        var (pc, s) = Db("pc");
        var paket = Paket(pc, 180, s);
        var (tek, _) = Db("tek", sunucu: true);
        var (cok, _) = Db("cok", sunucu: true);
        new BusinessSyncService(tek).Apply(Co, JsonDocument.Parse(paket).RootElement);
        foreach (var sayfa in BusinessSyncService.GonderimSayfalari(paket, 40))
            new BusinessSyncService(cok).Apply(Co, JsonDocument.Parse(sayfa).RootElement);
        string Ozet(SqliteConnectionFactory f, string sql) { using var c = f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToString(cmd.ExecuteScalar())!; }
        foreach (var sql in new[] {
                     $"SELECT COUNT(*) FROM materials WHERE company_id='{Co}'",
                     "SELECT COUNT(*) FROM material_equivalents",
                     $"SELECT group_concat(code, ',') FROM (SELECT code FROM materials WHERE company_id='{Co}' ORDER BY code)" })
            Assert.Equal(Ozet(tek, sql), Ozet(cok, sql));
        Assert.Equal("50", Ozet(cok, "SELECT COUNT(*) FROM material_equivalents"));   // 25 muadil bağı, iki yönlü kayıt
    }

    [Fact]
    public void GSY6_M81_Geciste_Tam_Gonderim()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !File.Exists(Path.Combine(dir, "DepoWise.sln")); i++) dir = Directory.GetParent(dir)!.FullName;
        var src = File.ReadAllText(Path.Combine(dir, "src", "DepoWise.Desktop", "BusinessSyncPushService.cs"));
        Assert.Contains("if (gecis && localSeq >= 0) { tamGonderim = true; gecis = false; seqWm = -1; }", src);
        Assert.Contains("if (seqWm < 0 && localSeq >= 0) return true;", src);
        Assert.Contains("BusinessSyncService.GonderimSayfalari(snapshot)", src);
    }
}
