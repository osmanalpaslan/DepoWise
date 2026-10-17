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
/// ═══ M-03 (AR-GE 2026-10-17, B1 adım 2) — ÇEKME v2: SUNUCU İŞLEM KİMLİĞİ UFKU (SQLite sunucu rolü) ═══
///
/// Eski çekme imleci istemci saatli <c>updated_at</c>'e dayanıyordu: geriye damgalı geç satır kaçabiliyor, ayrıca imleç hiç
/// ilerlemediği için her tur tam paket iniyordu (canlıda ≈4–6 GB/ay). v2 imleci sunucunun işlem-kimliği ufkudur
/// (Migration099). PostgreSQL'deki eşzamanlılık garantisi <see cref="PostgresSenkronUfukTests"/>'te sınanır.
///
///  UFK1 — İlk çekim (after=0) her şeyi getirir; ufuktan sonraki çekim DEĞİŞİKLİK YOKKEN BOŞTUR (trafik düzeltmesi)
///  UFK2 — Geriye damgalı GEÇ satır (çevrimdışı makine) v2'de gelir — eski damga imleci onu KAÇIRIRDI
///  UFK3 — Değişen ebeveynin (malzeme) DEĞİŞMEMİŞ çocukları (muadil) da gelir — alıcı çocuk kümesini silmesin
///  UFK4 — Firma kapsamı korunur: başka firmanın satırı v2'de görünmez
///  UFK5 — Masaüstü (sunucu rolü YOK): tetikleyici etkisiz, çekilen sunucu değeri olduğu gibi saklanır
///  UFK6 — Eşitlenen HER tabloda server_txid kolonu + iki tetikleyici var (ileride eklenen tablo unutulmasın)
/// </summary>
public class SenkronUfukTests : IDisposable
{
    private const string Co = "UFK", Yabanci = "UFK2";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly BusinessSyncService _sync;
    private readonly MaterialService _materials;
    private readonly SessionContext _admin, _yabanciAdmin;

    public SenkronUfukTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_ufk_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Calistir("INSERT OR IGNORE INTO sync_server_role(id) VALUES (1);");   // API'nin yaptığı gibi: SUNUCU rolü
        _admin = Firma(Co, "admin_ufk");
        _yabanciAdmin = Firma(Yabanci, "admin_ufk2");
        _sync = new BusinessSyncService(_f);
        _materials = new MaterialService(_f);
    }

    public void Dispose() { try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { } }

    private SessionContext Firma(string co, string user)
    {
        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{co}','{co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(co, user, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(uid, co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private void Calistir(string sql, SqliteConnectionFactory? f = null)
    {
        using var conn = (f ?? _f).Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private object? Tek(string sql, SqliteConnectionFactory? f = null)
    {
        using var conn = (f ?? _f).Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    /// <summary>v2 paketindeki bir tablonun satır kimlikleri + yeni ufuk.</summary>
    private (List<string> Ids, long Next) V2(string table, long after, string company = Co, string idKolon = "id")
    {
        var (json, next) = _sync.BuildDeltaV2(company, after);
        using var doc = JsonDocument.Parse(json);
        var ids = new List<string>();
        if (doc.RootElement.GetProperty("tables").TryGetProperty(table, out var rows))
            ids = rows.EnumerateArray().Select(r => r.GetProperty(idKolon).ToString()).ToList();
        Assert.Equal(next, doc.RootElement.GetProperty("nextAfter").GetInt64());
        return (ids, next);
    }

    [Fact]
    public void UFK1_Ilk_Cekim_Hepsini_Getirir_Degisiklik_Yokken_Bos_Doner()
    {
        var m1 = _materials.Create(_admin, new NewMaterial("U-1", "Çimento"));
        var m2 = _materials.Create(_admin, new NewMaterial("U-2", "Demir"));

        var (ilk, ufuk) = V2("materials", after: 0);
        Assert.Contains(m1, ilk);
        Assert.Contains(m2, ilk);

        // ⭐ Trafik düzeltmesi: değişiklik yokken aynı ufuktan çekim BOŞ (eskiden her tur tam paket iniyordu).
        var (ikinci, ufuk2) = V2("materials", after: ufuk);
        Assert.Empty(ikinci);
        Assert.Equal(ufuk, ufuk2);

        // Yeni değişiklik → yalnız o gelir.
        var m3 = _materials.Create(_admin, new NewMaterial("U-3", "Kum"));
        var (ucuncu, _) = V2("materials", after: ufuk2);
        Assert.Equal(new[] { m3 }, ucuncu);
    }

    [Fact]
    public void UFK2_Geriye_Damgali_Gec_Satir_Kacmaz()
    {
        const long T = 1_800_000_000_000;
        var guncel = _materials.Create(_admin, new NewMaterial("U-10", "Güncel"));
        Calistir($"UPDATE materials SET updated_at={T} WHERE id='{guncel}';");
        var (_, ufuk) = V2("materials", after: 0);

        // Çevrimdışı çalışmış başka makine, ESKİ damgalı (T'den çok önce) bir kaydı ŞİMDİ gönderiyor.
        var gec = Guid.NewGuid().ToString("N");
        var paket = JsonDocument.Parse($$"""
            { "tables": { "materials": [ { "id": "{{gec}}", "code": "U-GEC", "name": "Geç Gelen", "created_at": 1,
              "updated_at": {{T - 86_400_000}}, "version": 1, "is_deleted": 0 } ] } }
            """).RootElement.Clone();
        _sync.Apply(Co, paket);

        // Eski yöntem: damga imleci T'deyken geç satır (damgası < T) HİÇ gelmezdi — kusurun kanıtı.
        using (var eski = JsonDocument.Parse(_sync.BuildSnapshot(Co, sinceVersion: T)))
            Assert.DoesNotContain(gec, eski.RootElement.GetProperty("tables").GetProperty("materials").EnumerateArray()
                .Select(r => r.GetProperty("id").GetString()));

        // v2: geç satır, onu yazan işlem ufuktan SONRA bittiği için gelir.
        var (sonraki, _) = V2("materials", after: ufuk);
        Assert.Contains(gec, sonraki);
    }

    [Fact]
    public void UFK3_Degisen_Ebeveynin_Degismemis_Cocuklari_Da_Gelir()
    {
        var ana = _materials.Create(_admin, new NewMaterial("U-20", "Ana"));
        var muadil = _materials.Create(_admin, new NewMaterial("U-21", "Muadil"));
        _materials.AddEquivalent(_admin, ana, muadil);
        var (_, ufuk) = V2("materials", after: 0);

        // Yalnız EBEVEYN değişir (ad); muadil satırı DEĞİŞMEZ.
        Calistir($"UPDATE materials SET name='Ana (yeni ad)' WHERE id='{ana}';");

        var (json, _) = _sync.BuildDeltaV2(Co, ufuk);
        using var doc = JsonDocument.Parse(json);
        var tablolar = doc.RootElement.GetProperty("tables");
        Assert.True(tablolar.TryGetProperty("materials", out var mats) && mats.EnumerateArray().Any(r => r.GetProperty("id").GetString() == ana));
        // Alıcı (ApplyPull → ReconcileParentReplacedChildren) gelen ebeveynin çocuk kümesini paketteki hâliyle değiştirir:
        // çocuk pakette olmasaydı muadil bağı SİLİNİRDİ.
        Assert.True(tablolar.TryGetProperty("material_equivalents", out var eqs)
            && eqs.EnumerateArray().Any(r => r.GetProperty("material_id").GetString() == ana),
            "değişen ebeveynin değişmemiş çocuk satırı pakette olmalı");
    }

    [Fact]
    public void UFK4_Baska_Firmanin_Satiri_Sizmaz()
    {
        var bizim = _materials.Create(_admin, new NewMaterial("U-30", "Bizim"));
        var onlarin = _materials.Create(_yabanciAdmin, new NewMaterial("U-30", "Onların"));
        var (ids, _) = V2("materials", after: 0);
        Assert.Contains(bizim, ids);
        Assert.DoesNotContain(onlarin, ids);
    }

    [Fact]
    public void UFK5_Masaustunde_Tetikleyici_Etkisiz_Sunucu_Degeri_Saklanir()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_ufk_masa_" + Guid.NewGuid().ToString("N") + ".db");
        var masa = new SqliteConnectionFactory(yol);
        try
        {
            new MigrationRunner(masa).Run();   // sunucu rolü satırı YOK (masaüstü)
            Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);", masa);
            var uid = new UserService(masa).EnsureInitialAdmin(Co, "masa", "admin123", RoleKeys.CompanyAdmin);
            var s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
            var yerel = new MaterialService(masa).Create(s, new NewMaterial("Y-1", "Yerel"));
            Assert.True(Tek($"SELECT server_txid FROM materials WHERE id='{yerel}';", masa) is null or DBNull,
                "masaüstünde yerel yazım sunucu işlem kimliği ALMAMALI");

            // Sunucudan gelen satırın server_txid'si masaüstünde AYNEN saklanır (tetikleyici ezmez).
            var paket = JsonDocument.Parse($$"""
                { "tables": { "materials": [ { "id": "SRV-1", "company_id": "{{Co}}", "code": "S-1", "name": "Sunucudan",
                  "created_at": 1, "updated_at": 2, "version": 1, "is_deleted": 0, "server_txid": 777 } ] } }
                """).RootElement.Clone();
            new BusinessSyncService(masa).ApplyPull(Co, paket, null);
            Assert.Equal(777L, Convert.ToInt64(Tek("SELECT server_txid FROM materials WHERE id='SRV-1';", masa)));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { File.Delete(yol); } catch { } }
    }

    [Fact]
    public void UFK6_Her_Esitleme_Tablosunda_Kolon_Ve_Tetikleyici_Var()
    {
        var eksik = new List<string>();
        using var conn = _f.Create();
        foreach (var t in BusinessSyncService.Tables)
        {
            if (!DbIntrospect.TableExists(conn, null, t)) continue;
            if (BusinessSyncService.YalnizGonderim.Contains(t)) continue;   // M-06: masaüstüne inmeyen tablo, ufuk gerekmez
            if (!DbIntrospect.ColumnExists(conn, null, t, "server_txid")) eksik.Add($"{t}: server_txid kolonu yok");
            foreach (var trg in new[] { $"trg_{t}_txid_ins", $"trg_{t}_txid_upd" })
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name=@n;";
                cmd.AddWithValue("@n", trg);
                if (Convert.ToInt64(cmd.ExecuteScalar()) == 0) eksik.Add($"{t}: {trg} yok");
            }
        }
        Assert.True(eksik.Count == 0, "Eşitleme tablosu ufuk mekanizmasının dışında kaldı:\n  " + string.Join("\n  ", eksik));
    }
}
