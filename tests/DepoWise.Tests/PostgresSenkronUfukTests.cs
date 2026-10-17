using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-03 (AR-GE 2026-10-17) — ÇEKME v2 UFKU: PostgreSQL EŞZAMANLILIK GARANTİSİ ═══
///
/// Ufuk = pg_snapshot_xmin(pg_current_snapshot()): hâlâ SÜREN en eski işlemin kimliği. Çekme yalnız ufuktan KÜÇÜK
/// (bitmiş) işlemlerin satırlarını verir; süren işlem bir sonraki tura kalır, ASLA kaybolmaz.
///
///  PUF1 — Uzun işlem (T1) açıkken başka işlem (T2) kaydeder ve çekim yapılır: T2 GELMEZ (ufuk T1'de takılı), T1 sonra
///         bitince sonraki çekim İKİSİNİ de getirir — "sonra başlayıp önce biten" işlem uzun işlemi atlatamaz
///  PUF2 — Değişiklik yokken aynı ufuktan çekim boş
///  PUF3 — Eşitlenen her tabloda server_txid tetikleyicisi var
///
/// ⚠️ Yalnız DEPOWISE_PG_URL test veritabanında; şemayı sıfırlar. Yoksa ATLANIR.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresSenkronUfukTests
{
    private static string? PgUrl => PostgresTestGuard.Url;

    private static (PostgresMigrationTests.NpgsqlTestFactory F, BusinessSyncService Sync) Kur()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();
        var users = new UserService(factory);
        var rootId = users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var su = new SessionContext(rootId, "A", new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        new CompanyService(factory).Create(su, new NewCompany("B Firma"), explicitId: "B");
        return (factory, new BusinessSyncService(factory));
    }

    private static void Ekle(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction? tx, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO materials(id,company_id,code,name,created_at,updated_at,version,is_deleted) " +
                          "VALUES(@id,'B',@id,@id,1,1,1,0);";
        cmd.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private static (List<string> Ids, long Next) Cek(BusinessSyncService sync, long after)
    {
        var (json, next) = sync.BuildDeltaV2("B", after);
        using var doc = JsonDocument.Parse(json);
        var ids = doc.RootElement.GetProperty("tables").TryGetProperty("materials", out var rows)
            ? rows.EnumerateArray().Select(r => r.GetProperty("id").GetString()!).ToList() : new List<string>();
        return (ids, next);
    }

    [SkippableFact]
    public void PUF1_Uzun_Islem_Acikken_Sonra_Biten_Islem_Onu_Atlatamaz()
    {
        var (f, sync) = Kur();
        using (var c = f.Create()) Ekle(c, null, "m_once");
        var (ilk, ufuk0) = Cek(sync, 0);
        Assert.Contains("m_once", ilk);

        using var uzunConn = f.Create();
        using var uzun = uzunConn.BeginTransaction();
        Ekle(uzunConn, uzun, "m_uzun");                        // T1: kimliğini aldı, henüz BİTMEDİ

        using (var c = f.Create()) Ekle(c, null, "m_kisa");    // T2: T1'den SONRA başladı, ÖNCE bitti

        var (ara, ufuk1) = Cek(sync, ufuk0);
        Assert.DoesNotContain("m_uzun", ara);                   // görünmez (bitmedi)
        Assert.DoesNotContain("m_kisa", ara);                   // ufuk T1'de takılı → T2 de bekletilir

        uzun.Commit();

        var (son, ufuk2) = Cek(sync, ufuk1);
        Assert.Contains("m_uzun", son);                         // ⭐ eski "en büyük damga/sıra" imleci bunu atlatabilirdi
        Assert.Contains("m_kisa", son);
        Assert.True(ufuk2 > ufuk1);

        var (bos, _) = Cek(sync, ufuk2);
        Assert.Empty(bos);
    }

    [SkippableFact]
    public void PUF2_Degisiklik_Yokken_Bos_Doner()
    {
        var (f, sync) = Kur();
        using (var c = f.Create()) Ekle(c, null, "m_1");
        var (_, ufuk) = Cek(sync, 0);
        var (bos, ufuk2) = Cek(sync, ufuk);
        Assert.Empty(bos);
        var (bos2, _) = Cek(sync, ufuk2);
        Assert.Empty(bos2);
    }

    [SkippableFact]
    public void PUF4_Degismeyen_Guncelleme_Satiri_Yeniden_Indirtmez()
    {
        var (f, sync) = Kur();
        using (var c = f.Create()) Ekle(c, null, "m_ayni");
        var (_, ufuk) = Cek(sync, 0);

        void Calistir(string sql) { using var c = f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        Calistir("UPDATE materials SET name = name WHERE id = 'm_ayni';");   // aynı değerle yeniden yazım
        var (bos, ufuk2) = Cek(sync, ufuk);
        Assert.Empty(bos);

        Calistir("UPDATE materials SET name = 'yeni' WHERE id = 'm_ayni';"); // gerçek değişiklik
        var (dolu, _) = Cek(sync, ufuk2);
        Assert.Equal(new[] { "m_ayni" }, dolu);
    }

    /// <summary>PUF5 — M-15: PostgreSQL KURTARMA YOLUNDA (bir satır veritabanı hatasıyla patlar, tablo baştan satır satır
    /// uygulanır) red listesi ÇİFT kayıt içermez ve veritabanı hatası da kimliğiyle listelenir.</summary>
    [SkippableFact]
    public void PUF5_Kurtarma_Yolunda_Red_Listesi_Tekil()
    {
        var (f, sync) = Kur();
        using (var c = f.Create()) Ekle(c, null, "m_baskasi");
        using (var c = f.Create())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "UPDATE materials SET company_id='A' WHERE id='m_baskasi';";
            cmd.ExecuteNonQuery();
        }
        var paket = JsonDocument.Parse("""
            { "tables": { "materials": [
              { "id": "m_baskasi", "code": "Z1", "name": "Ele geçirme", "created_at": 1, "updated_at": 9 },
              { "id": "m_fk", "code": "Z2", "name": "Kategori yok", "category_id": "OLMAYAN", "created_at": 1, "updated_at": 1 },
              { "id": "m_iyi", "code": "Z3", "name": "Geçerli", "created_at": 1, "updated_at": 1 } ] } }
            """).RootElement;
        var res = sync.Apply("B", paket);
        var red = res.Rejected!;
        Assert.Equal(2, red.Count);
        Assert.Single(red, r => r.Key == "m_baskasi");
        Assert.Single(red, r => r.Key == "m_fk" && r.Permanent);
        Assert.DoesNotContain(red, r => r.Key == "m_iyi");
    }

    [SkippableFact]
    public void PUF6_Donem_Kimligi_Sabit_Sema_Yeniden_Kurulunca_Degisir()
    {
        var (f, sync) = Kur();
        string Donem() { using var d = JsonDocument.Parse(sync.BuildDeltaV2("B", 0).Json); return d.RootElement.GetProperty("epoch").GetString()!; }
        var d1 = Donem();
        Assert.Matches(@"^\d+-\d+$", d1);
        Assert.Equal(d1, Donem());                    // aynı sunucu → aynı dönem
        PostgresTestGuard.ResetSchema(f);             // "yedekten geri yükleme" benzeri: tablolar yeniden kurulur
        new MigrationRunner(f).Run();
        Assert.NotEqual(d1, Donem());
    }

    [SkippableFact]
    public void PUF3_Her_Esitleme_Tablosunda_Tetikleyici_Var()
    {
        var (f, _) = Kur();
        var eksik = new List<string>();
        using var conn = f.Create();
        foreach (var t in BusinessSyncService.Tables)
        {
            if (!DepoWise.Infrastructure.Database.DbIntrospect.TableExists(conn, null, t)) continue;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM pg_trigger WHERE tgname = @n AND NOT tgisinternal;";
            cmd.AddWithValue("@n", $"trg_{t}_txid");
            if (Convert.ToInt64(cmd.ExecuteScalar()) == 0) eksik.Add(t);
        }
        Assert.True(eksik.Count == 0, "server_txid tetikleyicisi eksik: " + string.Join(", ", eksik));
    }
}
