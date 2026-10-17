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
/// ═══ K-22 b (AR-GE 2026-10-17, kullanıcı kararı) — AYNI KOD İKİ BİLGİSAYARDA → "KOD-2" + BİLDİRİM ═══
///
///  KCK1 — Sunucuda MLZ-100 varken başka bilgisayarın MLZ-100'ü REDDEDİLMEZ: MLZ-100-2 olarak kaydedilir ve bildirilir
///  KCK2 — Üçüncü kez → MLZ-100-3 (ilk boş ek)
///  KCK3 — İsimle benzersiz tanım (birim "Adet") ve işlem kimliği çakışması yeniden ADLANDIRILMAZ (red listesinde kalır)
///  KCK4 — Masaüstü bildirimi: "kod" türünde, red sayısına girmez, kayıt sonradan kabul edilse de durur, kaldırılınca gider
///  KCK5 — Gönderen bilgisayar sonraki çekimde yeni kodu alır (aynı kimlik, kod MLZ-100-2)
/// </summary>
public class KodCakismasiTests : IDisposable
{
    private const string Co = "KCK";
    private readonly List<string> _yollar = new();
    private readonly SqliteConnectionFactory _srv;

    public KodCakismasiTests()
    {
        _srv = Db("srv", sunucu: true);
        Calistir(_srv, $"INSERT INTO materials(id,company_id,code,name,created_at,updated_at,version,is_deleted) VALUES('X','{Co}','MLZ-100','Sunucudaki',1,1,1,0);");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var y in _yollar) try { File.Delete(y); } catch { }
    }

    private SqliteConnectionFactory Db(string ad, bool sunucu = false)
    {
        var yol = Path.Combine(Path.GetTempPath(), $"dw_kck_{ad}_" + Guid.NewGuid().ToString("N") + ".db");
        _yollar.Add(yol);
        var f = new SqliteConnectionFactory(yol);
        new MigrationRunner(f).Run();
        Calistir(f, (sunucu ? "INSERT OR IGNORE INTO sync_server_role(id) VALUES (1); " : "") +
                    $"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        return f;
    }

    private static void Calistir(SqliteConnectionFactory f, string sql)
    {
        using var c = f.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string? Kod(SqliteConnectionFactory f, string id)
    {
        using var c = f.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT code FROM materials WHERE id=@id;";
        cmd.AddWithValue("@id", id);
        return cmd.ExecuteScalar() as string;
    }

    private static JsonElement Malzeme(string id, string kod) => JsonDocument.Parse($$"""
        { "tables": { "materials": [ { "id": "{{id}}", "code": "{{kod}}", "name": "Masaüstünde açıldı {{id}}",
          "created_at": 5, "updated_at": 5, "version": 1, "is_deleted": 0 } ] } }
        """).RootElement.Clone();

    [Fact]
    public void KCK1_KCK2_Ayni_Kod_Yeni_Ekle_Kaydedilir_Ve_Bildirilir()
    {
        var sync = new BusinessSyncService(_srv);
        var r1 = sync.Apply(Co, Malzeme("Y", "MLZ-100"));
        Assert.Equal(1, r1.Upserted);
        Assert.Empty(r1.Rejected!);
        var ad = Assert.Single(r1.Renamed!);
        Assert.Equal(("materials", "Y", "code", "MLZ-100", "MLZ-100-2"), (ad.Table, ad.Key, ad.Column, ad.Old, ad.New));
        Assert.Equal("MLZ-100", Kod(_srv, "X"));     // ilk gelen kodunu korur
        Assert.Equal("MLZ-100-2", Kod(_srv, "Y"));

        var r2 = sync.Apply(Co, Malzeme("Z", "MLZ-100"));
        Assert.Equal("MLZ-100-3", Assert.Single(r2.Renamed!).New);
        Assert.Equal("MLZ-100-3", Kod(_srv, "Z"));
    }

    [Fact]
    public void KCK3_Isim_Ve_Islem_Kimligi_Cakismasi_Adlandirilmaz()
    {
        Calistir(_srv, $"INSERT INTO units(id,company_id,name,created_at,updated_at,version,is_deleted) VALUES('U1','{Co}','Adet',1,1,1,0);");
        var res = new BusinessSyncService(_srv).Apply(Co, JsonDocument.Parse("""
            { "tables": { "units": [ { "id": "U2", "name": "Adet", "created_at": 1, "updated_at": 2, "version": 1, "is_deleted": 0 } ] } }
            """).RootElement);
        Assert.Empty(res.Renamed!);
        Assert.Single(res.Rejected!, r => r.Key == "U2");
    }

    [Fact]
    public void KCK4_Masaustu_Bildirimi_Kod_Turunde_Ve_Kalici()
    {
        var pc = Db("pc");
        var svc = new GonderimRedleriService(pc);
        var paket = """{ "tables": { "materials": [ { "id": "Y", "code": "MLZ-100" } ] } }""";
        svc.Isle(Co, paket, Array.Empty<RejectedRow>(), new[] { new BusinessSyncService.RenamedRow("materials", "Y", "code", "MLZ-100", "MLZ-100-2") });
        Assert.Equal(0, svc.AcikSayisi(Co));             // "gönderilemedi" sayısına girmez
        Assert.Equal(1, svc.AcikSayisi(Co, "kod"));
        var k = Assert.Single(svc.Acik(Co));
        Assert.True(k.KodBildirimi);
        Assert.False(k.YenidenGonderilebilir);
        Assert.Contains("MLZ-100-2", k.NedenMetni);
        Assert.StartsWith("Bilgi:", k.NedenMetni);

        svc.Isle(Co, paket, Array.Empty<RejectedRow>());   // aynı satır sonra yine kabul edildi
        Assert.Equal(1, svc.AcikSayisi(Co, "kod"));        // bildirim kendiliğinden kaybolmaz
        svc.Yoksay(Co, k.Anahtar);
        Assert.Equal(0, svc.AcikSayisi(Co, null));
    }

    /// <summary>KCK6 — Çekme paketinde sunucunun MLZ-100'ü, yerel kaydın "-2" hâlinden ÖNCE gelse bile ikisi de iner
    /// (çakışan satır tablonun sonuna ertelenir). Eskiden sunucudaki kayıt bu bilgisayara hiç inmezdi.</summary>
    [Fact]
    public void KCK6_Cekmede_Sira_Ters_Olsa_Da_Iki_Kayit_Iner()
    {
        var pc = Db("pc6");
        Calistir(pc, $"INSERT INTO materials(id,company_id,code,name,created_at,updated_at,version,is_deleted) VALUES('Y','{Co}','MLZ-100','Yerel',1,5,1,0);");
        var paket = JsonDocument.Parse($$"""
            { "tables": { "materials": [
              { "id": "X", "company_id": "{{Co}}", "code": "MLZ-100", "name": "Sunucudaki", "created_at": 1, "updated_at": 1, "version": 1, "is_deleted": 0 },
              { "id": "Y", "company_id": "{{Co}}", "code": "MLZ-100-2", "name": "Yerel", "created_at": 1, "updated_at": 5, "version": 1, "is_deleted": 0 } ] } }
            """).RootElement;
        var res = new BusinessSyncService(pc).ApplyPull(Co, paket, null);
        Assert.Equal(2, res.Upserted);
        Assert.Equal("MLZ-100", Kod(pc, "X"));
        Assert.Equal("MLZ-100-2", Kod(pc, "Y"));
    }

    [Fact]
    public void KCK5_Gonderen_Bilgisayar_Yeni_Kodu_Ceker()
    {
        var pc = Db("pc5");
        var uid = new UserService(pc).EnsureInitialAdmin(Co, "admin_kck", "admin123", RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var yerel = new MaterialService(pc).Create(s, new NewMaterial("MLZ-100", "Masaüstünde açıldı"));
        var paket = JsonDocument.Parse(new BusinessSyncService(pc).BuildPushSnapshot(Co, "pc", -1)).RootElement.Clone();
        var res = new BusinessSyncService(_srv).Apply(Co, paket);
        Assert.Contains(res.Renamed!, r => r.Key == yerel && r.New == "MLZ-100-2");

        var (json, _) = new BusinessSyncService(_srv).BuildDeltaV2(Co, 0);
        using var doc = JsonDocument.Parse(json);
        new BusinessSyncService(pc).ApplyPull(Co, doc.RootElement, null);
        Assert.Equal("MLZ-100-2", Kod(pc, yerel));
    }
}
