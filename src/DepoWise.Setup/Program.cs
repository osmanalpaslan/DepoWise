using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

// ── DepoWise Kurulum Aracı ──
// Sunucudan uygulamayı indirir, seçilen klasöre kurar, sunucu adresini otomatik yazar, kısayol oluşturur.
// Elle bağlantı ayarı YOK. Uygulama self-contained (içinde .NET) olduğundan ek yazılım gerekmez.

string server = Assembly.GetExecutingAssembly()
    .GetCustomAttributes<AssemblyMetadataAttribute>()
    .FirstOrDefault(a => a.Key == "ServerUrl")?.Value?.TrimEnd('/')
    ?? "https://depowise-erp.fly.dev";

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("=====================================");
Console.WriteLine("        DepoWise Kurulum");
Console.WriteLine("=====================================");
Console.WriteLine($"Sunucu: {server}");
Console.WriteLine();

var defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DepoWise", "app");
Console.WriteLine($"Kurulum klasörü [{defaultDir}]:");
Console.Write("> ");
var input = Console.ReadLine();
var installDir = string.IsNullOrWhiteSpace(input) ? defaultDir : input.Trim();

try
{
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

    Console.WriteLine("\nSunucudan sürüm bilgisi alınıyor...");
    var metaJson = await http.GetStringAsync($"{server}/api/releases/latest");
    if (string.IsNullOrWhiteSpace(metaJson) || metaJson == "null")
    {
        Console.WriteLine("HATA: Sunucuda kurulum paketi bulunamadı (yönetici henüz sürüm yayınlamamış).");
        return End(1);
    }
    using var doc = JsonDocument.Parse(metaJson);
    var root = doc.RootElement;
    var version = root.GetProperty("version").GetString() ?? "?";
    var downloadUrl = root.TryGetProperty("downloadUrl", out var d) ? d.GetString() : null;
    if (string.IsNullOrWhiteSpace(downloadUrl))
    {
        Console.WriteLine("HATA: Paket indirme adresi yok.");
        return End(1);
    }
    if (downloadUrl.StartsWith("/")) downloadUrl = server + downloadUrl;
    Console.WriteLine($"Sürüm: {version}");

    Console.WriteLine("Uygulama indiriliyor...");
    var tmpZip = Path.Combine(Path.GetTempPath(), $"depowise-{version}.zip");
    using (var resp = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
    {
        resp.EnsureSuccessStatusCode();
        await using var fs = File.Create(tmpZip);
        await resp.Content.CopyToAsync(fs);
    }

    Console.WriteLine($"Kuruluyor: {installDir}");
    Directory.CreateDirectory(installDir);
    ZipFile.ExtractToDirectory(tmpZip, installDir, overwriteFiles: true);
    File.Delete(tmpZip);

    // Sunucu adresini OTOMATİK yaz (elle ayar yok)
    File.WriteAllText(Path.Combine(installDir, "serverurl.txt"), server);

    var exe = Path.Combine(installDir, "DepoWise.Desktop.exe");
    if (File.Exists(exe)) TryCreateShortcut(exe, installDir);

    Console.WriteLine("\nKurulum tamamlandi.");
    Console.WriteLine("Masaustundeki 'DepoWise' kisayolundan acabilirsiniz.");
    return End(0);
}
catch (Exception ex)
{
    Console.WriteLine("\nHATA: " + ex.Message);
    return End(1);
}

static int End(int code)
{
    Console.WriteLine("\nCikmak icin Enter'a basin...");
    Console.ReadLine();
    return code;
}

static void TryCreateShortcut(string exePath, string workingDir)
{
    try
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var lnk = Path.Combine(desktop, "DepoWise.lnk");
        var t = Type.GetTypeFromProgID("WScript.Shell");
        if (t is null) return;
        dynamic shell = Activator.CreateInstance(t)!;
        var sc = shell.CreateShortcut(lnk);
        sc.TargetPath = exePath;
        sc.WorkingDirectory = workingDir;
        sc.Description = "DepoWise";
        sc.Save();
    }
    catch { /* kısayol başarısız olsa da kurulum tamam */ }
}
