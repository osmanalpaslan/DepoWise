using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using DepoWise.Infrastructure.Update;

namespace DepoWise.Desktop;

/// <summary>
/// GERÇEK otomatik güncelleme: self-contained paketi (zip) staging'e açar, uygulama kapanınca harici
/// PowerShell yardımcısı dosyaları kurulum dizinine kopyalar, sürümü yazar ve uygulamayı YENİDEN başlatır.
/// Çalışan exe kendini değiştiremeyeceği için kopyalama/yeniden başlatma dış süreçte yapılır.
/// powershell.exe Microsoft imzalı → COMODO sandbox'lamaz (dotnet host ile aynı mantık).
/// </summary>
public static class UpdateInstaller
{
    private const string MainExeName = "DepoWise.Desktop.exe";

    /// <summary>Zip'i doğrula + staging'e aç + yardımcıyı başlat. Başarılıysa çağıran uygulamayı KAPATMALI.
    /// Yardımcı önce mevcut kurulumu YEDEKLER; kopyalama başarısızsa yedekten GERİ ALIR ve eski sürümü başlatır
    /// (bozuk/yarım güncelleme kalıcı olmaz). Paket bütünlüğü checksum + ana exe varlığı ile doğrulanır.</summary>
    public static void InstallAndRestart(byte[] zipBytes, string version, string expectedSha)
    {
        if (!string.IsNullOrWhiteSpace(expectedSha) && !UpdateService.VerifyChecksum(zipBytes, expectedSha))
            throw new InvalidOperationException("Paket checksum doğrulamasını geçemedi (bozuk indirme).");

        var installDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DepoWise");
        var stagingRoot = Path.Combine(root, "staging");
        var staging = Path.Combine(stagingRoot, version);
        Directory.CreateDirectory(stagingRoot);
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);

        var zipPath = Path.Combine(stagingRoot, version + ".zip");
        File.WriteAllBytes(zipPath, zipBytes);
        ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true);
        try { File.Delete(zipPath); } catch { }

        // Bütünlük guard'ı: paket ana exe'yi içermeli (yanlış yapı/bozuk zip → kurulumu hiç başlatma).
        if (!File.Exists(Path.Combine(staging, MainExeName)))
        {
            try { Directory.Delete(staging, true); } catch { }
            throw new InvalidOperationException($"Güncelleme paketi geçersiz: {MainExeName} bulunamadı. Kurulum iptal edildi.");
        }

        var currentTxt = Path.Combine(root, "update", "current.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(currentTxt)!);
        var backupDir = Path.Combine(root, "backup");   // mevcut kurulumun geri-alma yedeği
        var exePath = Path.Combine(installDir, MainExeName);

        var script = Path.Combine(Path.GetTempPath(), $"dw_update_{version}_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(script, HelperScript());

        var pid = Environment.ProcessId;
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{script}\" " +
                        $"-ProcId {pid} -Staging \"{staging}\" -Install \"{installDir}\" -Exe \"{exePath}\" " +
                        $"-CurrentTxt \"{currentTxt}\" -Version {version} -Backup \"{backupDir}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    // $PID PowerShell'de ayrılmış otomatik değişkendir → parametre adı $ProcId.
    // Akış: (1) mevcut kurulumu yedekle → (2) staging'i kur → (3) kopyalama başarısızsa yedekten GERİ AL →
    // (4) yalnız BAŞARIDA sürümü yaz. Her durumda uygulama yeniden başlatılır (yeni ya da geri-alınmış sürüm).
    private static string HelperScript() => """
param([int]$ProcId,[string]$Staging,[string]$Install,[string]$Exe,[string]$CurrentTxt,[string]$Version,[string]$Backup)
try { Wait-Process -Id $ProcId -Timeout 90 -ErrorAction SilentlyContinue } catch {}
Start-Sleep -Seconds 2

# (1) YEDEK: mevcut kurulumu backup dizinine kopyala (rollback için). Yedek alınamazsa güncellemeyi
# hiç başlatma — eski sürüm bozulmasın; sadece uygulamayı tekrar başlat.
try { if (Test-Path $Backup) { Remove-Item -Recurse -Force $Backup -ErrorAction SilentlyContinue } } catch {}
robocopy $Install $Backup /E /R:2 /W:2 | Out-Null
if ($LASTEXITCODE -ge 8) {
    try { Start-Process -FilePath $Exe } catch {}
    try { Remove-Item -Force $MyInvocation.MyCommand.Path -ErrorAction SilentlyContinue } catch {}
    exit 1
}

# (2) KUR: staging → kurulum dizini (DB %AppData%'da, dokunulmaz). Kilit için birkaç deneme.
$ok = $false
for ($i=0; $i -lt 5; $i++) {
    robocopy $Staging $Install /E /R:2 /W:2 | Out-Null
    if ($LASTEXITCODE -lt 8) { $ok = $true; break }
    Start-Sleep -Seconds 2
}

if ($ok) {
    # (4a) BAŞARI: sürümü yaz, staging'i temizle. Yedek bir sonraki güncellemeye kadar durur.
    try { Set-Content -Path $CurrentTxt -Value $Version -NoNewline -Encoding utf8 } catch {}
    try { Remove-Item -Recurse -Force $Staging -ErrorAction SilentlyContinue } catch {}
} else {
    # (3) BAŞARISIZ: yedekten geri al (kurulum dizinini eski haline döndür), sürümü YAZMA.
    for ($j=0; $j -lt 5; $j++) {
        robocopy $Backup $Install /E /R:2 /W:2 | Out-Null
        if ($LASTEXITCODE -lt 8) { break }
        Start-Sleep -Seconds 2
    }
}

Start-Sleep -Seconds 1
try { Start-Process -FilePath $Exe } catch {}
try { Remove-Item -Force $MyInvocation.MyCommand.Path -ErrorAction SilentlyContinue } catch {}
""";
}
