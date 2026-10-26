using System.Diagnostics;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using Xunit;
using Xunit.Abstractions;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-70 (3/3) (AR-GE 2026-10-26, F-020) — GERÇEK BOYUTTA MIGRATION PROVASI (PostgreSQL) ═══
/// Migration testleri birkaç elle kurulan satırla çalışıyordu. Canlı şema 98, AR-GE dalı 120: aradaki zincir yayında BİR KEZ, dolu
/// veritabanında koşacak. En riskli adım 099 — tüm iş tablolarında HER SATIRI günceller (server_txid). Burada 98'de duran bir
/// veritabanına 100.000 stok hareketi + 2.000 malzeme yazılır, sonra 99→120 zinciri koşar:
///  MP1 — zincir hatasız biter, makul sürede (eşik 120 sn; süre test çıktısına yazılır), satır sayıları aynı,
///        her harekete eşitleme damgası (server_txid) verilmiş, servis yeni şemada bakiyeyi defterden doğru hesaplar
/// ⚠ Yalnız izole test PG'sinde (PostgresTestGuard). Üretim veritabanına dokunmaz.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresMigrationProvasiTests
{
    private const int CanliSurum = 98;
    private const int Malzeme = 2_000, Hareket = 100_000;
    private readonly ITestOutputHelper _cikti;
    public PostgresMigrationProvasiTests(ITestOutputHelper cikti) => _cikti = cikti;

    private sealed class SabitSaat : IClock { public DateTimeOffset UtcNow => DateTimeOffset.FromUnixTimeMilliseconds(1_760_000_000_000); }

    private static long Sayi(IDbConnectionFactory f, string sql)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [SkippableFact]
    public void MP1_98den_120ye_Zincir_100bin_Harekette_Dogru_Ve_Makul_Surede()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = new PostgresMigrationTests.NpgsqlTestFactory(PostgresTestGuard.Url!);
        PostgresTestGuard.ResetSchema(f);
        try { Prova(f); }
        finally
        {
            // ⭐ Prova ~60 MB veri yazar; geride kalırsa sonraki PG testleri 50 MB güvenlik kapısına takılıp REDDEDİLİR
            // (2026-10-26 paket koşusunda 29 test böyle reddedildi). Bu test kapıdan geçmiş AYNI veritabanında kendi verisini
            // siler ve alanı geri verir — kapı gevşetilmedi.
            using var conn = f.Create();
            using (var cmd = conn.CreateCommand()) { cmd.CommandText = "DROP SCHEMA public CASCADE; CREATE SCHEMA public;"; cmd.ExecuteNonQuery(); }
            using (var vac = conn.CreateCommand()) { vac.CommandText = "VACUUM FULL;"; vac.ExecuteNonQuery(); }
        }
    }

    private void Prova(IDbConnectionFactory f)
    {
        // 1) Canlıdaki şema: yalnız 98'e kadar.
        new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= CanliSurum)).Run();
        Assert.Equal(CanliSurum, Sayi(f, "SELECT MAX(version) FROM schema_migrations;"));

        // 2) Gerçek boyutta veri (98 şemasının kolonlarıyla; sonradan eklenen kolonlar varsayılanlarını alır).
        using (var conn = f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandTimeout = 300;
            cmd.CommandText = $@"
INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted) VALUES('MP', 'Prova', 1, 1, 1, 0);
INSERT INTO materials(id, company_id, code, name, created_at, updated_at)
SELECT 'mp-m' || g, 'MP', 'MP-' || g, 'Prova malzemesi ' || g, 1, 1 FROM generate_series(1, {Malzeme}) g;
INSERT INTO stock_movements(id, company_id, material_id, branch_id, movement_type, direction, quantity, operation_id, created_at)
SELECT 'mp-h' || g, 'MP', 'mp-m' || (1 + g % {Malzeme}), NULL,
       CASE WHEN g % 4 = 0 THEN 'out' ELSE 'in' END, CASE WHEN g % 4 = 0 THEN -1 ELSE 1 END,
       '1.5', 'mp-op' || g, 1700000000000 + g
FROM generate_series(1, {Hareket}) g;";
            cmd.ExecuteNonQuery();
        }
        Assert.Equal(Hareket, Sayi(f, "SELECT COUNT(*) FROM stock_movements WHERE company_id='MP';"));

        // 3) Yayında koşacak zincir: 99 → 120.
        var sure = Stopwatch.StartNew();
        var uygulanan = new MigrationRunner(f).Run();
        sure.Stop();
        _cikti.WriteLine($"99→120 zinciri: {uygulanan.Count} adım, {sure.Elapsed.TotalSeconds:0.0} sn ({Hareket:N0} hareket, {Malzeme:N0} malzeme)");

        Assert.Equal(Enumerable.Range(CanliSurum + 1, MigrationCatalog.All().Max(m => m.Version) - CanliSurum), uygulanan);
        Assert.True(sure.Elapsed < TimeSpan.FromSeconds(120), $"zincir çok uzun sürdü: {sure.Elapsed.TotalSeconds:0} sn");

        // 4) Veri yerinde ve yeni şemada kullanılabilir.
        Assert.Equal(Hareket, Sayi(f, "SELECT COUNT(*) FROM stock_movements WHERE company_id='MP';"));
        Assert.Equal(Malzeme, Sayi(f, "SELECT COUNT(*) FROM materials WHERE company_id='MP';"));
        Assert.Equal(0, Sayi(f, "SELECT COUNT(*) FROM stock_movements WHERE server_txid IS NULL;"));   // 099 her satıra damga verdi

        var stok = new StockService(f, new SabitSaat());
        stok.RecomputeBalances("MP");
        var s = new SessionContext("mp-kullanici", "MP", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        // mp-m1: g % 2000 == 0 olan hareketler → g = 2000k (k=1..50); hepsi 4'e bölünür → 50 çıkış × 1,5 = −75
        Assert.Equal(-75m, stok.GetBalance(s, "mp-m1"));
        // mp-m2: g % 2000 == 1 → g = 1, 2001, … (50 adet); hiçbiri 4'e bölünmez → 50 giriş × 1,5 = 75
        Assert.Equal(75m, stok.GetBalance(s, "mp-m2"));
    }
}
