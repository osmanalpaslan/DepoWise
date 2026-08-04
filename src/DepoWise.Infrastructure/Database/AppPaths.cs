namespace DepoWise.Infrastructure.Database;

/// <summary>
/// COMODO kuralı: yerel SQLite DAİMA mutlak LocalAppData yolunda olmalı (çalışma klasörüne
/// bağlı relative path YASAK — sandbox sanal DB tuzağı). Yol environment'a göre ayrışır.
/// </summary>
public static class AppPaths
{
    // Marka değişimi (2026-07-26): yerel veri klasörü artık "Alpnex". Veriler sıfırlandığı için taşıma gerekmedi.
    // (İç kod adı DepoWise.* namespace olarak kalır — kullanıcı görmez; bu yalnız disk klasör adıdır.)
    public const string AppFolderName = "Alpnex";

    public static string DataDirectory(string environment)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(root, AppFolderName, "Data", Sanitize(environment));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string DatabasePath(string environment)
        => Path.Combine(DataDirectory(environment), "alpnex.db");

    private static string Sanitize(string environment)
    {
        if (string.IsNullOrWhiteSpace(environment)) return "Development";
        foreach (var c in Path.GetInvalidFileNameChars())
            environment = environment.Replace(c, '_');
        return environment;
    }
}
