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
/// ═══ M-06 (AR-GE 2026-10-17, 05 F-035) — MASAÜSTÜ İŞLEM GEÇMİŞİ SUNUCUYA (yalnız gönderim, yalnız ekleme) ═══
///
///  AUD1 — Masaüstünde yapılan işlemin geçmiş satırı gönderilir; sunucuda kaynak bilgisayar işaretlenir
///  AUD2 — Sunucudaki geçmiş satırı push ile DEĞİŞTİRİLEMEZ (aynı kimlikle farklı içerik yok sayılır)
///  AUD3 — Geçmiş masaüstüne İNMEZ (v2 çekme + eski sunucu anlık görüntüsü)
///  AUD4 — Modül yetkisi olmayan kullanıcı da kendi geçmişini gönderebilir; ama malzeme gibi tablolar yine yetkiye bağlı
///  AUD5 — Firma oturumdan zorlanır (paketteki başka firma kimliği yok sayılır); gelen "source_device" yok sayılır
/// </summary>
public class IslemGecmisiGonderimTests : IDisposable
{
    private const string Co = "AUD";
    private readonly List<string> _yollar = new();

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var y in _yollar) try { File.Delete(y); } catch { }
    }

    private (SqliteConnectionFactory F, SessionContext S) Db(string ad, bool sunucu = false)
    {
        var yol = Path.Combine(Path.GetTempPath(), $"dw_aud_{ad}_" + Guid.NewGuid().ToString("N") + ".db");
        _yollar.Add(yol);
        var f = new SqliteConnectionFactory(yol);
        new MigrationRunner(f).Run(); if (sunucu) Migration099_SyncTxid.SunucuRoluKur(f);
        Exec(f, 
                $"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);" +
                "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('BASKA','BASKA',1,1,1,0);");
        var uid = new UserService(f).EnsureInitialAdmin(Co, "admin_" + ad, "admin123", RoleKeys.CompanyAdmin);
        return (f, new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty));
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
        return v is null or DBNull ? null : Convert.ToString(v);
    }

    private static JsonElement Gecmis(string id, string company, string after, string? kaynak = null) => JsonDocument.Parse($$"""
        { "machineId": "MPC-7", "tables": { "audit_logs": [ { "id": "{{id}}", "company_id": "{{company}}", "user_id": "u1",
          "entity_type": "material", "entity_id": "m1", "action": "update", "after_json": "{{after}}",
          "created_at": 5{{(kaynak is null ? "" : $", \"source_device\": \"{kaynak}\"")}} } ] } }
        """).RootElement.Clone();

    [Fact]
    public void AUD1_Masaustu_Gecmisi_Gonderilir_Kaynak_Isaretlenir()
    {
        var (pc, s) = Db("pc");
        new MaterialService(pc).Create(s, new NewMaterial("A-1", "Geçmişi olan malzeme"));
        var paket = new BusinessSyncService(pc).BuildPushSnapshot(Co, "MPC-7", -1);
        using var doc = JsonDocument.Parse(paket);
        var gecmis = doc.RootElement.GetProperty("tables").GetProperty("audit_logs").EnumerateArray().ToList();
        Assert.NotEmpty(gecmis);

        var (srv, _) = Db("srv", sunucu: true);
        new BusinessSyncService(srv).Apply(Co, doc.RootElement);
        var id = gecmis[0].GetProperty("id").GetString();
        Assert.Equal("MPC-7", Tek(srv, $"SELECT source_device FROM audit_logs WHERE id='{id}';"));
    }

    [Fact]
    public void AUD2_Sunucudaki_Gecmis_Degistirilemez()
    {
        var (srv, _) = Db("srv2", sunucu: true);
        var sync = new BusinessSyncService(srv);
        sync.Apply(Co, Gecmis("a1", Co, "ilk"));
        sync.Apply(Co, Gecmis("a1", Co, "SAHTE"));
        Assert.Equal("ilk", Tek(srv, "SELECT after_json FROM audit_logs WHERE id='a1';"));
    }

    [Fact]
    public void AUD3_Gecmis_Masaustune_Inmez()
    {
        var (srv, _) = Db("srv3", sunucu: true);
        var sync = new BusinessSyncService(srv);
        sync.Apply(Co, Gecmis("a3", Co, "x"));
        using (var v2 = JsonDocument.Parse(sync.BuildDeltaV2(Co, 0).Json))
            Assert.False(v2.RootElement.GetProperty("tables").TryGetProperty("audit_logs", out _));
        using var eski = JsonDocument.Parse(sync.BuildSnapshot(Co, "server"));
        Assert.False(eski.RootElement.GetProperty("tables").TryGetProperty("audit_logs", out _));
    }

    [Fact]
    public void AUD4_Yetkisiz_Kullanici_Gecmisini_Gonderebilir_Ama_Kart_Gonderemez()
    {
        var (srv, _) = Db("srv4", sunucu: true);
        var personel = new SessionContext("u9", Co, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        var paket = JsonDocument.Parse($$"""
            { "machineId": "MPC-7", "tables": {
                "materials": [ { "id": "mz", "code": "Z", "name": "Yetkisiz", "created_at": 1, "updated_at": 1, "version": 1, "is_deleted": 0 } ],
                "audit_logs": [ { "id": "a4", "entity_type": "material", "entity_id": "mz", "action": "create", "created_at": 5 } ] } }
            """).RootElement;
        new BusinessSyncService(srv).Apply(personel, paket);
        Assert.Equal("1", Tek(srv, "SELECT COUNT(*) FROM audit_logs WHERE id='a4';"));
        Assert.Equal("0", Tek(srv, "SELECT COUNT(*) FROM materials WHERE id='mz';"));
    }

    [Fact]
    public void AUD5_Firma_Ve_Kaynak_Sunucuda_Zorlanir()
    {
        var (srv, _) = Db("srv5", sunucu: true);
        new BusinessSyncService(srv).Apply(Co, Gecmis("a5", "BASKA", "x", kaynak: "SAHTE-PC"));
        Assert.Equal(Co, Tek(srv, "SELECT company_id FROM audit_logs WHERE id='a5';"));
        Assert.Equal("MPC-7", Tek(srv, "SELECT source_device FROM audit_logs WHERE id='a5';"));
    }
}
