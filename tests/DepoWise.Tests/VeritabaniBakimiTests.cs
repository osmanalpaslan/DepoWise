using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-20 (AR-GE 2026-10-17) — SORGU PLANLAYICI İSTATİSTİKLERİ ═══
///  VTB1 — İlk çalıştırmada ANALYZE (sqlite_stat1 oluşur, satırları var), sonra PRAGMA optimize; veri değişmez; hızlı
///  VTB2 — Masaüstü açılışı bakımı arka planda çağırır ve yalnız sağlıklı veritabanında (kaynak kilidi)
/// </summary>
public class VeritabaniBakimiTests : IDisposable
{
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "dw_vtb_" + Guid.NewGuid().ToString("N") + ".db");

    public void Dispose() { try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_yol); } catch { } }

    private static long Tek(SqliteConnectionFactory f, string sql)
    {
        using var c = f.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public void VTB1_Ilk_Analyze_Sonra_Optimize_Veri_Degismez()
    {
        var f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(f).Run();
        using (var c = f.Create())
        using (var tx = c.BeginTransaction())
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('VTB','VTB',1,1,1,0);";
            cmd.ExecuteNonQuery();
            for (int i = 0; i < 20000; i++)
            {
                cmd.CommandText = $"INSERT INTO materials(id,company_id,code,name,created_at,updated_at,version,is_deleted) VALUES('m{i}','VTB','K{i}','Ad {i}',1,1,1,0);";
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        var once = Tek(f, "SELECT COUNT(*) FROM materials;");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal("analyze", VeritabaniBakimi.IstatistikleriGuncelle(f));
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 5000, $"bakım {sw.ElapsedMilliseconds} ms sürdü (örneklemeli olmalı)");
        Assert.True(Tek(f, "SELECT COUNT(*) FROM sqlite_stat1 WHERE tbl='materials';") > 0, "malzeme istatistiği çıkmadı");

        Assert.Equal("optimize", VeritabaniBakimi.IstatistikleriGuncelle(f));
        Assert.Equal(once, Tek(f, "SELECT COUNT(*) FROM materials;"));
    }

    [Fact]
    public void VTB2_Acilis_Bakimi_Arka_Planda_Cagirir()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !File.Exists(Path.Combine(dir, "DepoWise.sln")); i++) dir = Directory.GetParent(dir)!.FullName;
        var src = File.ReadAllText(Path.Combine(dir, "src", "DepoWise.Desktop", "DesktopBootstrap.cs"));
        Assert.Contains("if (result.Ok)", src);
        Assert.Contains("Task.Run(() => { try { VeritabaniBakimi.IstatistikleriGuncelle(factory); } catch { } })", src);
    }
}
