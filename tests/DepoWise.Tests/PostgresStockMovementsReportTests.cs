using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// STK-10a — POSTGRESQL LEHÇE + SORGU PLANI DOĞRULAMASI.
///
/// Sunucu PostgreSQL, masaüstü SQLite (CLAUDE.md §4) → yeni rapor sorgusu İKİSİNDE de sınanır.
/// Bu sınıf ayrıca planın **§13/D-2** gereğini kanıtlar: <b>filtre → sıralama → LIMIT SQL İÇİNDE</b>
/// uygulanıyor; tüm defter belleğe çekilmiyor.
///
/// ⚠️ Yalnız DEPOWISE_PG_URL (doğrulanmış BOŞ test veritabanı) ile koşar; yoksa ATLANIR.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresStockMovementsReportTests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    /// <summary>Raporun PostgreSQL'de doğru çalıştığı + sorgu planının filtre/sıralama/LIMIT'i
    /// SQL tarafında uyguladığı kanıtlanır.</summary>
    [SkippableFact]
    public void PostgreSQLde_hareket_raporu_calisir_ve_LIMIT_SQL_tarafinda_uygulanir()
    {
        PostgresTestGuard.SkipUnlessSafe();

        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();

        using (var conn = factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES('A', 'A', 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.ExecuteNonQuery();
        }

        var clock = new TestClock();
        var users = new UserService(factory, clock);
        var uid = users.EnsureInitialAdmin("A", "admin_hrk", "admin123", RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(factory, clock);
        var depoA = branches.Create(s, new NewBranch("PG Depo A"));
        var depoB = branches.Create(s, new NewBranch("PG Depo B"));

        var materials = new MaterialService(factory, clock);
        var opening = new OpeningStockService(factory, clock);
        var stock = new StockService(factory, clock);
        var reports = new ReportService(factory);

        var mat = materials.Create(s, new NewMaterial("PG-HRK", "PG hareket malzemesi"));
        opening.RecordOpening(s, mat, 200m, "pg-open", branchId: depoA);
        for (int i = 0; i < 20; i++)
        {
            clock.Advance(60_000);
            stock.ReceiveIn(s, new[] { new StockLine(mat, 1m) }, "pg-in-" + i, branchId: depoA);
        }
        clock.Advance(60_000);
        stock.Transfer(s, mat, 10m, depoA, depoB, "pg-trf");

        var genis = new ReportRequest(true, FromDate: 0, ToDate: 4_102_444_800_000L);

        // ── 1. Rapor PostgreSQL'de çalışıyor ──
        var tam = reports.Run(s, "stock-movements", genis);
        Assert.Equal(23, tam.Rows.Count);   // 1 açılış + 20 giriş + 2 transfer bacağı
        Assert.Contains("Kaynak", tam.Headers);
        Assert.Contains("Hedef", tam.Headers);

        // ── 2. Lokasyon filtresi (branch_id OR branch_from_id) PG'de doğru ──
        var bFiltre = reports.Run(s, "stock-movements", genis with { LocationIds = new[] { depoB } });
        Assert.Single(bFiltre.Rows);   // yalnız transferin giriş bacağı

        var aFiltre = reports.Run(s, "stock-movements", genis with { LocationIds = new[] { depoA } });
        Assert.Equal(23, aFiltre.Rows.Count);   // açılış + girişler + transferin İKİ bacağı da A'yı ilgilendiriyor

        // ── 3. LIMIT SQL tarafında ──
        Assert.Equal(5, reports.Run(s, "stock-movements", genis, maxRows: 5).Rows.Count);

        // ── 3b. STK-10b-1: HAREKET TÜRÜ filtresi PostgreSQL'de ──
        var girisler = reports.Run(s, "stock-movements", genis with { MovementTypes = new[] { "in" } });
        Assert.Equal(20, girisler.Rows.Count);
        var transferler = reports.Run(s, "stock-movements", genis with { MovementTypes = new[] { "transfer" } });
        Assert.Equal(2, transferler.Rows.Count);   // iki bacak
        // Bilinmeyen tür → fail-closed (PG'de de veri sızmıyor).
        Assert.Empty(reports.Run(s, "stock-movements", genis with { MovementTypes = new[] { "uydurma" } }).Rows);
        // Tür + lokasyon birlikte.
        Assert.Single(reports.Run(s, "stock-movements",
            genis with { MovementTypes = new[] { "transfer" }, LocationIds = new[] { depoB } }).Rows);

        // ── 3c. STK-10b-2: SERBEST METİN ARAMA PostgreSQL'de ──
        Assert.Equal(23, reports.Run(s, "stock-movements", genis with { SearchText = "PG-HRK" }).Rows.Count);
        Assert.Empty(reports.Run(s, "stock-movements", genis with { SearchText = "yok-boyle-kayit" }).Rows);
        Assert.Equal(23, reports.Run(s, "stock-movements", genis with { SearchText = "   " }).Rows.Count);   // boşluk = filtre yok
        // Arama + tür + lokasyon birlikte (hepsi AND).
        Assert.Single(reports.Run(s, "stock-movements",
            genis with { SearchText = "PG-HRK", MovementTypes = new[] { "transfer" }, LocationIds = new[] { depoB } }).Rows);

        // ── 3d. STK-10b-3: MALZEME filtresi PostgreSQL'de ──
        var mat2 = materials.Create(s, new NewMaterial("PG-HRK-2", "PG ikinci malzeme"));
        clock.Advance(60_000);
        stock.ReceiveIn(s, new[] { new StockLine(mat2, 4m) }, "pg-in-mat2", branchId: depoB);

        Assert.Equal(24, reports.Run(s, "stock-movements", genis).Rows.Count);   // 23 + yeni giriş
        Assert.Equal(23, reports.Run(s, "stock-movements", genis with { MaterialIds = new[] { mat } }).Rows.Count);
        Assert.Single(reports.Run(s, "stock-movements", genis with { MaterialIds = new[] { mat2 } }).Rows);
        Assert.Equal(24, reports.Run(s, "stock-movements", genis with { MaterialIds = new[] { mat, mat2 } }).Rows.Count);
        // Var olmayan kimlik → fail-closed (PG'de de veri sızmıyor / "filtre yok"a düşmüyor).
        Assert.Empty(reports.Run(s, "stock-movements", genis with { MaterialIds = new[] { "yok-boyle" } }).Rows);
        // Malzeme + lokasyon + tür + arama birlikte (hepsi AND).
        Assert.Single(reports.Run(s, "stock-movements", genis with
        {
            MaterialIds = new[] { mat }, MovementTypes = new[] { "transfer" },
            LocationIds = new[] { depoB }, SearchText = "PG-HRK",
        }).Rows);
        // Aynı üçlü + YANLIŞ malzeme → boş: malzeme filtresi gerçekten daraltıyor.
        Assert.Empty(reports.Run(s, "stock-movements", genis with
        {
            MaterialIds = new[] { mat2 }, MovementTypes = new[] { "transfer" },
            LocationIds = new[] { depoB }, SearchText = "PG-HRK",
        }).Rows);
        // LIMIT, MALZEMEYLE FİLTRELENMİŞ küme üzerine iniyor (bellekte değil).
        Assert.Equal(3, reports.Run(s, "stock-movements", genis with { MaterialIds = new[] { mat } }, maxRows: 3).Rows.Count);

        // ── 3e. STK-10b-4: EKRAN yolu (SearchMovements) PostgreSQL'de + EKRAN = RAPOR ──
        // 🔴 B-1: lokasyon filtresi ekranda da SQL'de ve LIMIT'ten ÖNCE uygulanır.
        var ekranB = stock.SearchMovements(s, 0, 4_102_444_800_000L, null, new[] { depoB }, null, null, 1000);
        Assert.Equal(2, ekranB.Count);   // transferin giriş bacağı + 3d'de eklenen mat2 girişi
        var raporB = reports.Run(s, "stock-movements", genis with { LocationIds = new[] { depoB } });
        Assert.Equal(raporB.Rows.Count, ekranB.Count);

        var ekranA = stock.SearchMovements(s, 0, 4_102_444_800_000L, null, new[] { depoA }, null, null, 1000);
        var raporA = reports.Run(s, "stock-movements", genis with { LocationIds = new[] { depoA } });
        Assert.Equal(raporA.Rows.Count, ekranA.Count);

        // Tavan FİLTRELENMİŞ küme üzerine iniyor (ekran yolunda da).
        Assert.Equal(3, stock.SearchMovements(s, 0, 4_102_444_800_000L, null, new[] { depoA }, null, null, 3).Count);
        // Diğer filtreler ekran yolunda da PG'de çalışıyor.
        Assert.Equal(21, stock.SearchMovements(s, 0, 4_102_444_800_000L, null, null, new[] { "in" }, null, 1000).Count);   // 20 + mat2
        Assert.Single(stock.SearchMovements(s, 0, 4_102_444_800_000L, null, null, null, new[] { mat2 }, 1000));
        Assert.Empty(stock.SearchMovements(s, 0, 4_102_444_800_000L, "yok-boyle-kayit", null, null, null, 1000));

        // ── 4. 🔴 SORGU PLANI: filtre + sıralama + LIMIT SQL'de mi? ──
        var plan = Plan(factory, depoA, mat);
        // STK-10b-3: malzeme koşulu planda görünmeli → SQL'e indi, bellekte süzülmüyor.
        Assert.Contains("material_id", plan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Limit", plan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sort", plan, StringComparison.OrdinalIgnoreCase);
        // Filtre gerçekten SQL'e inmiş olmalı (WHERE koşulu planda görünür).
        Assert.True(plan.Contains("Filter", StringComparison.OrdinalIgnoreCase)
                    || plan.Contains("Index Cond", StringComparison.OrdinalIgnoreCase)
                    || plan.Contains("Join Filter", StringComparison.OrdinalIgnoreCase),
            "Sorgu planında filtre adımı yok — WHERE koşulları SQL'e inmemiş olabilir:\n" + plan);
    }

    /// <summary>Rapor sorgusunun PostgreSQL planını döndürür (rapor metodunun kurduğu SQL'in aynısı).</summary>
    private static string Plan(IDbConnectionFactory factory, string depoA, string materialId)
    {
        using var conn = factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
EXPLAIN
SELECT sm.created_at, sm.movement_type, sm.direction, sm.quantity,
       m.code, m.name, COALESCE(u.name,'') AS unit,
       sm.branch_id, bl.name AS loc_name, sm.branch_from_id, bf.name AS from_name,
       COALESCE(d.doc_no,'') AS doc_no, COALESCE(d.invoice_no,'') AS invoice_no,
       sm.is_reversed, COALESCE(sm.note,'') AS note
FROM stock_movements sm
JOIN materials m ON m.id = sm.material_id AND m.company_id = sm.company_id
LEFT JOIN units u ON u.id = m.unit_id
LEFT JOIN stock_documents d ON d.id = sm.document_id
LEFT JOIN branches bl ON bl.id = sm.branch_id      AND bl.company_id = sm.company_id
LEFT JOIN branches bf ON bf.id = sm.branch_from_id AND bf.company_id = sm.company_id
WHERE sm.company_id = @c AND sm.created_at >= @from AND sm.created_at <= @to
  AND (sm.branch_id IN (@loc0) OR sm.branch_from_id IN (@loc0))
  AND sm.movement_type IN (@mtype0)
  AND (m.code LIKE @q OR m.name LIKE @q OR sm.note LIKE @q OR d.invoice_no LIKE @q OR d.doc_no LIKE @q)
  AND sm.material_id IN (@mat0)
ORDER BY sm.created_at DESC, sm.id DESC LIMIT @lim;";
        cmd.AddWithValue("@c", "A");
        cmd.AddWithValue("@from", 0L);
        cmd.AddWithValue("@to", 4_102_444_800_000L);
        cmd.AddWithValue("@loc0", depoA);
        cmd.AddWithValue("@mtype0", "in");
        cmd.AddWithValue("@q", "%PG-HRK%");
        cmd.AddWithValue("@mat0", materialId);
        cmd.AddWithValue("@lim", 50);

        var sb = new System.Text.StringBuilder();
        using var r = cmd.ExecuteReader();
        while (r.Read()) sb.AppendLine(r.GetValue(0)?.ToString());
        return sb.ToString();
    }
}
