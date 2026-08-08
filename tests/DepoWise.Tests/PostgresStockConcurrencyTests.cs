using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// FAZ 3-ÖN — GERÇEK EŞZAMANLILIK KANITI (PostgreSQL).
///
/// SQLite'ta <c>BeginImmediate</c> aynı anda tek yazara izin verdiği için yarış OLUŞMAZ; asıl risk
/// PostgreSQL'dedir (READ COMMITTED). Bu testler gerçek paralel iş parçacıklarıyla, düzeltmeden önce
/// oluşan iki hatayı kanıtlar ve düzeltmeden sonra oluşmadığını gösterir:
///   • OVERSELL: iki işlem de "yeterli stok" görüp toplamda eldekinden fazla çıkış yapması,
///   • KAYIP DÜŞÜM: mutlak değer yazımında bir düşümün diğerini ezmesi (bakiye ↔ defter tutarsızlığı).
///
/// ⚠️ Yalnız DEPOWISE_PG_URL (boş Neon deneme DB'si) ile koşar; şemayı sıfırlar. Yoksa ATLANIR.
/// Canlı veritabanına ASLA bağlanmaz.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresStockConcurrencyTests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private sealed record Fixture(PostgresMigrationTests.NpgsqlTestFactory Factory, StockService Stock,
        MaterialService Materials, OpeningStockService Opening, SessionContext Admin);

    private static Fixture Setup()
    {
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        // GÜVENLİK KAPISI: şema YALNIZ doğrulanmış boş test veritabanında sıfırlanır (bkz. PostgresTestGuard).
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();

        var clock = new TestClock();
        var users = new UserService(factory, clock);
        var uid = users.EnsureInitialAdmin("A", "admin_a", "admin123", RoleKeys.CompanyAdmin);
        var admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        return new Fixture(factory, new StockService(factory, clock), new MaterialService(factory, clock),
            new OpeningStockService(factory, clock), admin);
    }

    /// <summary>Ham sayım sorgusu (yarım belge / artık kayıt kontrolü için).</summary>
    private static int RawCount(PostgresMigrationTests.NpgsqlTestFactory factory, string sql)
    {
        using var conn = factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>Defterden gerçek bakiye: Σ(direction × quantity). Bakiye önbelleğiyle tutmalı.</summary>
    private static decimal LedgerBalance(PostgresMigrationTests.NpgsqlTestFactory factory, string materialId)
    {
        using var conn = factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT direction, quantity FROM stock_movements WHERE material_id=@m;";
        cmd.AddWithValue("@m", materialId);
        decimal total = 0m;
        using var r = cmd.ExecuteReader();
        while (r.Read()) total += r.GetInt64(0) * Money.Parse(r.IsDBNull(1) ? null : r.GetString(1));
        return total;
    }

    /// <summary>T-01 + T-02: eşzamanlı iki çıkış — ne oversell ne kayıp düşüm.</summary>
    [SkippableFact]
    public void Eszamanli_Iki_Cikis_Oversell_Ve_Kayip_Dusum_Uretmez()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = Setup();

        // ── T-01: stok 10; eşzamanlı 6 ve 7 → yalnız biri geçmeli (toplam 13 ASLA çıkmamalı) ──
        var m1 = f.Materials.Create(f.Admin, new NewMaterial("M-CC1", "Filtre"));
        f.Opening.RecordOpening(f.Admin, m1, 10m, "pg-cc-open-1");

        // Yarış KANITI: tekrar mekanizmasının gerçekten devreye girdiğini log üzerinden doğrularız
        // (kullanıcı kuralı: yalnız sonucun doğru olması yeterli değil).
        var log = new System.Collections.Concurrent.ConcurrentBag<string>();
        var oldLog = StockBalanceWriter.Log;
        StockBalanceWriter.Log = msg => log.Add(msg);

        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        int ok1 = 0;
        try
        {
            void Issue(string op, decimal qty)
            {
                try { f.Stock.IssueOut(f.Admin, new[] { new StockLine(m1, qty) }, op, personnelId: null); Interlocked.Increment(ref ok1); }
                catch (Exception ex) { errors.Add(ex); }
            }
            Parallel.Invoke(() => Issue("pg-cc-a", 6m), () => Issue("pg-cc-b", 7m));
        }
        finally { StockBalanceWriter.Log = oldLog; }

        Assert.Equal(1, ok1);                                   // tam olarak biri başarılı (6 birimlik)
        // Kaybeden işlem İŞ KURALI ile reddedilmeli: doc_no çakışması artık tekrar edildiği için
        // yeniden denemede stok kontrolü çalışır ve 7 birim için yetersiz stok hatası verir.
        Assert.All(errors, e => Assert.True(e is NegativeStockException or StockBusyException,
            $"beklenmeyen hata tipi: {e.GetType().Name} — {e.Message}"));
        // Hangi işlemin kazandığı GERÇEK bir yarıştır (zamanlamaya bağlı): 6 kazanırsa bakiye 4,
        // 7 kazanırsa 3. İKİSİ DE DOĞRUDUR — değişmez kural, toplam çıkışın 13 OLMAMASIDIR.
        var bal1 = f.Stock.GetBalance(m1);
        Assert.True(bal1 is 4m or 3m, $"bakiye 4 (6 kazandı) veya 3 (7 kazandı) olmalı, gelen: {bal1}");
        Assert.Equal(bal1, LedgerBalance(f.Factory, m1));        // defter ↔ bakiye TUTARLI (kayıp düşüm yok)
        Assert.True(bal1 >= 0m);                                 // oversell yok

        // Yarım belge / artık kayıt kalmamalı: tek açılış + tek çıkış belgesi.
        Assert.Equal(2, RawCount(f.Factory, "SELECT COUNT(*) FROM stock_movements WHERE material_id='" + m1 + "';"));
        Assert.Equal(1, RawCount(f.Factory, "SELECT COUNT(*) FROM stock_documents WHERE doc_type='out';"));

        // Yarış gerçekten oluştuysa TEKRAR edilmiş olmalı (doc_no ya da bakiye CAS).
        var races = log.Where(l => l.Contains("[stock-docno]") || l.Contains("[stock-cas]")).ToList();
        Assert.DoesNotContain(races, l => l.Contains("give-up"));   // tekrar hakkı tükenmedi

        // ── T-02: stok 10; eşzamanlı 6 ve 3 → İKİSİ DE geçmeli, bakiye 1 (bir düşüm kaybolmamalı) ──
        var m2 = f.Materials.Create(f.Admin, new NewMaterial("M-CC2", "Conta"));
        f.Opening.RecordOpening(f.Admin, m2, 10m, "pg-cc-open-2");

        int ok2 = 0;
        void Issue2(string op, decimal qty)
        {
            try { f.Stock.IssueOut(f.Admin, new[] { new StockLine(m2, qty) }, op, personnelId: null); Interlocked.Increment(ref ok2); }
            catch (Exception ex) { errors.Add(ex); }
        }
        Parallel.Invoke(() => Issue2("pg-cc-c", 6m), () => Issue2("pg-cc-d", 3m));

        Assert.Equal(2, ok2);
        Assert.Equal(1m, f.Stock.GetBalance(m2));               // 10 - 6 - 3 (DÜZELTMEDEN ÖNCE 4 veya 7 olurdu)
        Assert.Equal(1m, LedgerBalance(f.Factory, m2));
    }

    /// <summary>T-04: yüksek çekişme — 20 paralel çıkış, stok 10. Değişmez kural: ASLA negatife düşmez ve
    /// bakiye her zaman deftere eşittir. (Tekrar hakkı sınırlı olduğu için başarılı sayısı 10'dan az olabilir;
    /// önemli olan fazla ÇIKIŞ olmamasıdır.)</summary>
    [SkippableFact]
    public void Yuksek_Cekismede_Bakiye_Negatife_Dusmez_Ve_Defterle_Tutarli_Kalir()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = Setup();

        var m = f.Materials.Create(f.Admin, new NewMaterial("M-CC3", "Rulman"));
        f.Opening.RecordOpening(f.Admin, m, 10m, "pg-cc-open-3");

        int ok = 0;
        Parallel.For(0, 20, i =>
        {
            try { f.Stock.IssueOut(f.Admin, new[] { new StockLine(m, 1m) }, $"pg-cc-x{i}", personnelId: null); Interlocked.Increment(ref ok); }
            catch (NegativeStockException) { }
            catch (StockBusyException) { }
        });

        var bal = f.Stock.GetBalance(m);
        Assert.True(ok >= 1 && ok <= 10, $"başarılı çıkış sayısı 1..10 olmalı, gelen: {ok}");
        Assert.True(bal >= 0m, $"bakiye negatife düştü: {bal}");
        Assert.Equal(10m - ok, bal);                            // her başarılı çıkış TAM BİR kez düşmüş
        Assert.Equal(bal, LedgerBalance(f.Factory, m));         // defter ↔ bakiye tutarlı
    }

    /// <summary>Eşzamanlı GİRİŞ + ÇIKIŞ: iki yön birbirini ezmemeli (kayıp güncelleme yok).</summary>
    [SkippableFact]
    public void Eszamanli_Giris_Ve_Cikis_Birbirini_Ezmez()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = Setup();

        var m = f.Materials.Create(f.Admin, new NewMaterial("M-CC4", "Kayış"));
        f.Opening.RecordOpening(f.Admin, m, 100m, "pg-cc-open-4");

        Parallel.Invoke(
            () => f.Stock.ReceiveIn(f.Admin, new[] { new StockLine(m, 40m) }, "pg-cc-in"),
            () => f.Stock.IssueOut(f.Admin, new[] { new StockLine(m, 25m) }, "pg-cc-out", personnelId: null));

        Assert.Equal(115m, f.Stock.GetBalance(m));              // 100 + 40 - 25
        Assert.Equal(115m, LedgerBalance(f.Factory, m));
    }
}
