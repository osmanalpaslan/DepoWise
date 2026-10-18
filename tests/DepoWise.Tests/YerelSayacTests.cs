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
/// ═══ M-03 adım 3 (AR-GE 2026-10-17) — GÖNDERME İMLECİ: YEREL DEĞİŞİKLİK SAYACI (masaüstü SQLite, Migration100) ═══
///
///  YRL1 — Yerel yazım sayacı artırır; paket yalnız imleçten sonraki değişiklikleri taşır, değişiklik yokken boştur
///  YRL2 — SAATİ GERİ ALINMIŞ makinede yeni kayıt gönderilir (eski damga imleci onu KAÇIRIRDI — kusurun kanıtı)
///  YRL3 — YANKI KESME: çekmeyle inen satır gönderme paketine girmez; koruma satırı çekme sonrası temizdir
///  YRL4 — Yalnız ebeveyn (malzeme) değişince değişmemiş çocuk (muadil) de pakete girer (sunucu silmesin)
///  YRL5 — Tek seferlik GEÇİŞ: güncelleme öncesi bekleyen (sayaçsız) kayıt eski damga koşuluyla pakete girer
///  YRL6 — Sunucu rolünde (API'nin SQLite'ı) yerel sayaç tetikleyicisi etkisizdir
///  YRL7 — Eşitlenen HER tabloda local_change_seq kolonu + iki tetikleyici var
/// </summary>
public class YerelSayacTests : IDisposable
{
    private const string Co = "YRL";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly TestClock _clock = new();
    private readonly BusinessSyncService _sync;
    private readonly MaterialService _materials;
    private readonly SessionContext _admin;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
    }

    public YerelSayacTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_yrl_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();   // MASAÜSTÜ: sunucu rolü YOK
        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "admin_yrl", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _sync = new BusinessSyncService(_f, _clock);
        _materials = new MaterialService(_f, _clock);
    }

    public void Dispose() { try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { } }

    private void Calistir(string sql, SqliteConnectionFactory? f = null)
    {
        using var conn = (f ?? _f).Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static List<string> Idler(string json, string table, string kolon = "id")
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("tables").TryGetProperty(table, out var rows)
            ? rows.EnumerateArray().Select(r => r.GetProperty(kolon).ToString()).ToList() : new List<string>();
    }

    [Fact]
    public void YRL1_Yerel_Yazim_Sayaci_Artirir_Paket_Yalniz_Yenileri_Tasir()
    {
        var m1 = _materials.Create(_admin, new NewMaterial("Y-1", "Bir"));
        var s1 = _sync.LocalChangeSeq();
        Assert.True(s1 > 0);

        Assert.Empty(Idler(_sync.BuildPushSnapshot(Co, "pc", s1), "materials"));   // değişiklik yok → boş

        var m2 = _materials.Create(_admin, new NewMaterial("Y-2", "İki"));
        var paket = Idler(_sync.BuildPushSnapshot(Co, "pc", s1), "materials");
        Assert.Equal(new[] { m2 }, paket);
        Assert.Contains(m1, Idler(_sync.BuildPushSnapshot(Co, "pc", -1), "materials"));   // -1 = tam
    }

    [Fact]
    public void YRL2_Saati_Geri_Alinmis_Makinede_Yeni_Kayit_Gonderilir()
    {
        _materials.Create(_admin, new NewMaterial("Y-10", "Önce"));
        var eskiImlec = _sync.CompanyVersion(Co);   // eski yöntemin imleci (yerel en büyük damga)
        var seqImlec = _sync.LocalChangeSeq();

        _clock.UtcNow = _clock.UtcNow.AddDays(-3);   // bilgisayarın saati 3 gün GERİ alındı
        var yeni = _materials.Create(_admin, new NewMaterial("Y-11", "Saat geride"));

        // Eski yöntem: damga imlecin gerisinde → kayıt HİÇ gönderilmezdi (kusurun kanıtı).
        Assert.DoesNotContain(yeni, Idler(_sync.BuildSnapshot(Co, "pc", eskiImlec), "materials"));
        // Yeni yöntem: sayaç saatten bağımsız → gönderilir.
        Assert.Contains(yeni, Idler(_sync.BuildPushSnapshot(Co, "pc", seqImlec), "materials"));
    }

    [Fact]
    public void YRL3_Cekmeyle_Inen_Satir_Geri_Gonderilmez()
    {
        _materials.Create(_admin, new NewMaterial("Y-20", "Yerel"));
        var imlec = _sync.LocalChangeSeq();

        var paket = JsonDocument.Parse($$"""
            { "tables": { "materials": [ { "id": "SRV-20", "company_id": "{{Co}}", "code": "S-20", "name": "Sunucudan",
              "created_at": 1, "updated_at": 1900000000000, "version": 1, "is_deleted": 0 } ] } }
            """).RootElement.Clone();
        _sync.ApplyPull(Co, paket, null);

        Assert.Equal(imlec, _sync.LocalChangeSeq());                                    // sayaç kıpırdamadı
        Assert.DoesNotContain("SRV-20", Idler(_sync.BuildPushSnapshot(Co, "pc", imlec), "materials"));
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM sync_apply_guard;";
            Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));                     // koruma çekmeden sonra kalkar
        }

        // Kullanıcı inen kaydı sonra YERELDE düzenlerse gönderilir.
        Calistir("UPDATE materials SET name='Yerelde düzenlendi' WHERE id='SRV-20';");
        Assert.Contains("SRV-20", Idler(_sync.BuildPushSnapshot(Co, "pc", imlec), "materials"));
    }

    [Fact]
    public void YRL4_Degisen_Ebeveynin_Degismemis_Cocuklari_Da_Gonderilir()
    {
        var ana = _materials.Create(_admin, new NewMaterial("Y-30", "Ana"));
        var muadil = _materials.Create(_admin, new NewMaterial("Y-31", "Muadil"));
        _materials.AddEquivalent(_admin, ana, muadil);
        var imlec = _sync.LocalChangeSeq();

        Calistir($"UPDATE materials SET name='Ana (yeni)' WHERE id='{ana}';");   // yalnız ebeveyn
        var json = _sync.BuildPushSnapshot(Co, "pc", imlec);
        Assert.Contains(ana, Idler(json, "materials"));
        Assert.Contains(ana, Idler(json, "material_equivalents", "material_id"));
    }

    [Fact]
    public void YRL5_Gecis_Bekleyen_Sayacsiz_Kayit_Eski_Damgayla_Gider()
    {
        var gonderilmis = _materials.Create(_admin, new NewMaterial("Y-40", "Gönderilmiş"));
        var eskiImlec = _sync.CompanyVersion(Co);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(5);
        var bekleyen = _materials.Create(_admin, new NewMaterial("Y-41", "Bekleyen"));
        // Güncellemeden ÖNCE yazılmış gibi: sayaçları yok.
        Calistir("INSERT INTO sync_apply_guard(id) VALUES(1);");
        Calistir("UPDATE materials SET local_change_seq = NULL;");
        Calistir("DELETE FROM sync_apply_guard;");

        var json = _sync.BuildPushSnapshot(Co, "pc", 0, eskiDamgaAfter: eskiImlec);
        Assert.Contains(bekleyen, Idler(json, "materials"));
        Assert.DoesNotContain(gonderilmis, Idler(json, "materials"));

        var hicGondermemis = _sync.BuildPushSnapshot(Co, "pc", 0, eskiDamgaAfter: 0);   // ilk kurulum: tam
        Assert.Contains(gonderilmis, Idler(hicGondermemis, "materials"));
    }

    [Fact]
    public void YRL6_Sunucu_Rolunde_Yerel_Sayac_Calismaz()
    {
        Migration099_SyncTxid.SunucuRoluKur(_f);
        var once = _sync.LocalChangeSeq();
        _materials.Create(_admin, new NewMaterial("Y-50", "Sunucuda"));
        Assert.Equal(once, _sync.LocalChangeSeq());
    }

    [Fact]
    public void YRL7_Her_Esitleme_Tablosunda_Kolon_Ve_Tetikleyici_Var()
    {
        var eksik = new List<string>();
        using var conn = _f.Create();
        foreach (var t in BusinessSyncService.Tables)
        {
            if (!DbIntrospect.TableExists(conn, null, t)) continue;
            if (!DbIntrospect.ColumnExists(conn, null, t, "local_change_seq")) eksik.Add($"{t}: kolon yok");
            foreach (var trg in new[] { $"trg_{t}_lseq_ins", $"trg_{t}_lseq_upd" })
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name=@n;";
                cmd.AddWithValue("@n", trg);
                if (Convert.ToInt64(cmd.ExecuteScalar()) == 0) eksik.Add($"{t}: {trg} yok");
            }
        }
        Assert.True(eksik.Count == 0, "Yerel sayacın dışında kalan tablo:\n  " + string.Join("\n  ", eksik));
    }
}
