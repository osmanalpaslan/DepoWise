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
    /// <summary>Zip'i doğrula + staging'e aç + yardımcıyı başlat. Başarılıysa çağıran uygulamayı KAPATMALI.</summary>
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

        var currentTxt = Path.Combine(root, "update", "current.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(currentTxt)!);
        var exePath = Path.Combine(installDir, "DepoWise.Desktop.exe");

        var script = Path.Combine(Path.GetTempPath(), $"dw_update_{version}_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(script, HelperScript());

        var pid = Environment.ProcessId;
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{script}\" " +
                        $"-ProcId {pid} -Staging \"{staging}\" -Install \"{installDir}\" -Exe \"{exePath}\" " +
                        $"-CurrentTxt \"{currentTxt}\" -Version {version}",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    // $PID PowerShell'de ayrılmış otomatik değişkendir → parametre adı $ProcId.
    private static string HelperScript() => """
param([int]$ProcId,[string]$Staging,[string]$Install,[string]$Exe,[string]$CurrentTxt,[string]$Version)
try { Wait-Process -Id $ProcId -Timeout 90 -ErrorAction SilentlyContinue } catch {}
Start-Sleep -Seconds 2
# Dosyaları kurulum dizinine kopyala (DB %AppData%'da, dokunulmaz). Birkaç deneme (kilit için).
for ($i=0; $i -lt 5; $i++) {
    robocopy $Staging $Install /E /R:2 /W:2 | Out-Null
    if ($LASTEXITCODE -lt 8) { break }
    Start-Sleep -Seconds 2
}
try { Set-Content -Path $CurrentTxt -Value $Version -NoNewline -Encoding utf8 } catch {}
try { Remove-Item -Recurse -Force $Staging -ErrorAction SilentlyContinue } catch {}
Start-Sleep -Seconds 1
try { Start-Process -FilePath $Exe } catch {}
try { Remove-Item -Force $MyInvocation.MyCommand.Path -ErrorAction SilentlyContinue } catch {}
""";
}
