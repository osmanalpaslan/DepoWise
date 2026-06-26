using System;
using System.IO;
using DepoWise.Application.Common;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Desktop;

/// <summary>
/// Açılış health kontrolü + log. COMODO kanıtı (host=dotnet, gerçek DB yolu, WAL, write/read)
/// burada üretilir ve %LOCALAPPDATA%\DepoWise\Logs altına yazılır.
/// </summary>
public static class DesktopBootstrap
{
    public static string Environment =>
        System.Environment.GetEnvironmentVariable("DEPOWISE_ENVIRONMENT") ?? "Development";

    public static HealthResult RunStartupHealth()
    {
        var factory = SqliteConnectionFactory.ForEnvironment(Environment);
        var health = new DatabaseHealth(factory);
        var result = health.CheckAsync().GetAwaiter().GetResult();
        WriteLog(result);
        return result;
    }

    private static void WriteLog(HealthResult r)
    {
        try
        {
            var root = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(root, AppPaths.AppFolderName, "Logs");
            Directory.CreateDirectory(dir);
            var line = $"{DateTimeOffset.UtcNow:O}\tstartup\thost={r.Host}\tdb={r.DatabasePath}\t" +
                       $"journal={r.JournalMode}\tfk={r.ForeignKeysOn}\twriteRead={r.WriteReadOk}\tok={r.Ok}\terr={r.Error}";
            File.AppendAllText(Path.Combine(dir, "startup.log"), line + System.Environment.NewLine);
        }
        catch { /* log hatası açılışı engellemez */ }
    }
}
