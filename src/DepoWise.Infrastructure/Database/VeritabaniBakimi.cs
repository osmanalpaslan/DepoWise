namespace DepoWise.Infrastructure.Database;

/// <summary>
/// ⭐ M-20 (AR-GE 2026-10-17) — SORGU PLANLAYICI İSTATİSTİKLERİ (yalnız SQLite / masaüstü).
///
/// <b>Bulgu (07 §4, P18).</b> Masaüstü veritabanında <c>ANALYZE</c> / <c>PRAGMA optimize</c> HİÇ çalışmıyordu; planlayıcı
/// istatistiksiz kalıp büyük veride yanlış indeks seçiyordu (ölçüm: tek bir ANALYZE iki sorguyu 1,7 sn → 0 ms).
///
/// <b>Yöntem (SQLite'ın önerdiği).</b> <c>PRAGMA analysis_limit=400</c> → istatistik her indekste en fazla ~400 satır
/// örnekleyerek çıkarılır (300 bin satırda bile milisaniyeler; yazma kilidi kısa). İstatistik hiç yoksa bir kez
/// <c>ANALYZE</c>, varsa <c>PRAGMA optimize</c> (yalnız gerekiyorsa yeniler). Veri DEĞİŞMEZ; yalnız <c>sqlite_stat1</c> yazılır.
/// PostgreSQL'de hiçbir şey yapmaz (sunucuda autovacuum/analyze çalışır).
/// </summary>
public static class VeritabaniBakimi
{
    /// <summary>Döner: "analyze" (ilk kez), "optimize", ya da "yok" (SQLite değil). Hata fırlatırsa çağıran yutar.</summary>
    public static string IstatistikleriGuncelle(IDbConnectionFactory factory)
    {
        using var conn = factory.Create();
        if (!SqlDialect.IsSqlite(conn)) return "yok";
        bool istatistikVar;
        using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='sqlite_stat1';";
            istatistikVar = Convert.ToInt64(c.ExecuteScalar()) > 0;
        }
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA analysis_limit=400; " + (istatistikVar ? "PRAGMA optimize;" : "ANALYZE;");
        cmd.ExecuteNonQuery();
        return istatistikVar ? "optimize" : "analyze";
    }
}
