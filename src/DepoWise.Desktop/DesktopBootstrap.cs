using System;
using System.IO;
using DepoWise.Application.Common;
using DepoWise.Application.Theming;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Settings;

namespace DepoWise.Desktop;

public sealed record BootstrapResult(HealthResult Health, ThemeTokens Theme, BrandingSettings Branding);

/// <summary>
/// Açılış health kontrolü + log. COMODO kanıtı (host=dotnet, gerçek DB yolu, WAL, write/read)
/// burada üretilir ve %LOCALAPPDATA%\DepoWise\Logs altına yazılır.
/// </summary>
public static class DesktopBootstrap
{
    public static string Environment =>
        System.Environment.GetEnvironmentVariable("DEPOWISE_ENVIRONMENT") ?? "Development";

    /// <summary>Migration + health + merkezi tema/branding yükler (renkler ayarlardan, sabit değil).</summary>
    public static BootstrapResult Run()
    {
        var factory = SqliteConnectionFactory.ForEnvironment(Environment);
        MigrasyonlariYedekleCalistir(factory, new MigrationRunner(factory), new DepoWise.Infrastructure.Files.BackupService(factory));

        var result = new DatabaseHealth(factory).CheckAsync().GetAwaiter().GetResult();
        WriteLog(result);

        // ⭐ M-20 (AR-GE 2026-10-17): sorgu planlayıcı istatistikleri — arka planda, açılışı bekletmeden; hata açılışı etkilemez.
        if (result.Ok)
            System.Threading.Tasks.Task.Run(() => { try { VeritabaniBakimi.IstatistikleriGuncelle(factory); } catch { } });

        var settings = new SettingsService(factory);
        // Oturum öncesi global tema/branding (firma override login sonrası uygulanır — Faz 05).
        return new BootstrapResult(result, settings.GetTheme(null), settings.GetBranding(null));
    }

    /// <summary>
    /// ⭐ M-83 (AR-GE 2026-10-18, 05 F-185): yeni sürümün veritabanı güncellemesi (migration) varsa, mevcut (dolu) veritabanının
    /// "guncelleme_oncesi" kopyası ÖNCE alınır; süreler açılış günlüğüne yazılır (uzun migration "takıldı" sanılmasın, sorun
    /// olursa yedekten dönülebilsin). Yedek alınamazsa açılış durdurulmaz (günlüğe yazılır) — program açılmalıdır.
    /// </summary>
    internal static void MigrasyonlariYedekleCalistir(IDbConnectionFactory factory, MigrationRunner runner,
        DepoWise.Infrastructure.Files.BackupService yedek, Action<string>? gunluk = null)
    {
        gunluk ??= Gunluk;
        var bekleyen = runner.Pending();
        if (bekleyen.Count > 0 && runner.CurrentVersion() > 0)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { gunluk($"migration_oncesi_yedek\t{yedek.EtiketliYedek($"guncelleme_oncesi_v{runner.CurrentVersion()}")}\t{sw.ElapsedMilliseconds}ms"); }
            catch (Exception ex) { gunluk($"migration_oncesi_yedek_HATA\t{ex.Message}"); }
        }
        var sure = System.Diagnostics.Stopwatch.StartNew();
        var uygulanan = runner.Run();
        if (uygulanan.Count > 0) gunluk($"migration\t{string.Join(",", uygulanan)}\t{sure.ElapsedMilliseconds}ms");
    }

    private static void Gunluk(string satir)
    {
        try
        {
            var dir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), AppPaths.AppFolderName, "Logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "startup.log"), $"{DateTimeOffset.UtcNow:O}\t{satir}{System.Environment.NewLine}");
        }
        catch { }
    }

    private static void WriteLog(HealthResult r)
    {
        try
        {
            var root = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(root, AppPaths.AppFolderName, "Logs");
            Directory.CreateDirectory(dir);
            // ⭐ Onarım bilgisi de yazılır (2026-09-07): bozuk indeks kendiliğinden onarıldıysa
            // bunun izi kalmalı — "uygulama neden bir kez geç açıldı" sorusu sonradan cevaplanabilsin.
            var line = $"{DateTimeOffset.UtcNow:O}\tstartup\thost={r.Host}\tdb={r.DatabasePath}\t" +
                       $"journal={r.JournalMode}\tfk={r.ForeignKeysOn}\twriteRead={r.WriteReadOk}\tok={r.Ok}\terr={r.Error}" +
                       (string.IsNullOrEmpty(r.Onarim) ? "" : $"\tonarim={r.Onarim}");
            File.AppendAllText(Path.Combine(dir, "startup.log"), line + System.Environment.NewLine);
        }
        catch { /* log hatası açılışı engellemez */ }
    }
}
