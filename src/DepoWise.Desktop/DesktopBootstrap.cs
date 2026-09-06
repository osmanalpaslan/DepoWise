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
        new MigrationRunner(factory).Run();

        var result = new DatabaseHealth(factory).CheckAsync().GetAwaiter().GetResult();
        WriteLog(result);

        var settings = new SettingsService(factory);
        // Oturum öncesi global tema/branding (firma override login sonrası uygulanır — Faz 05).
        return new BootstrapResult(result, settings.GetTheme(null), settings.GetBranding(null));
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
