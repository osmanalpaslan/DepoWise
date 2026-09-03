# Alpnex - masaustu pencere goruntusu yakalama yardimcisi (gorsel QA icin)
#
# Neden: Claude Code'un yerlesik tarayici araclari bir MASAUSTU penceresini goremez (tarayici/DOM yok).
# Ama yerel bir PNG dosyasini okuyup gorsel olarak degerlendirebilir. Eksik halka yalnizca
# "pencereyi PNG'ye ceviren bir sey" oldugu icin bunun icin MCP kurmak gereksizdir; bu 40 satirlik
# betik ayni isi kalici bir baglam maliyeti olmadan yapar.
#
# Kullanim:
#   powershell -File scripts/capture_window.ps1 -Exe "<yol>\AlpnexSetup.exe" -Out "<klasor>" -DelaysMs 1500,4000
#
# Guvenlik: yalnizca EKRAN GORUNTUSU alir. Hicbir kurulum/yazma islemi yapmaz; uygulamayi acar,
# belirtilen anlarda goruntu alir ve kapatir.

param(
  [Parameter(Mandatory=$true)][string]$Exe,
  [Parameter(Mandatory=$true)][string]$Out,
  [int[]]$DelaysMs = @(1500),
  [string]$Prefix = "ekran"
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if (-not (Test-Path $Out)) { New-Item -ItemType Directory -Force $Out | Out-Null }

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

$p = Start-Process -FilePath $Exe -PassThru
$sw = [Diagnostics.Stopwatch]::StartNew()
$prev = 0

foreach ($d in $DelaysMs) {
  $bekle = $d - $prev
  if ($bekle -gt 0) { Start-Sleep -Milliseconds $bekle }
  $prev = $d

  $p.Refresh()
  if ($p.HasExited) { Write-Output "surec kapandi ($d ms)"; break }
  $h = $p.MainWindowHandle
  if ($h -eq 0) { Write-Output "pencere yok ($d ms)"; continue }

  [void][Win]::SetForegroundWindow($h)
  Start-Sleep -Milliseconds 250

  $r = New-Object Win+RECT
  if (-not [Win]::GetWindowRect($h, [ref]$r)) { Write-Output "olcu alinamadi ($d ms)"; continue }

  $w = $r.R - $r.L; $ht = $r.B - $r.T
  if ($w -le 0 -or $ht -le 0) { Write-Output "gecersiz olcu ($d ms)"; continue }

  $bmp = New-Object System.Drawing.Bitmap $w, $ht
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
  $dosya = Join-Path $Out "$Prefix-$d.png"
  $bmp.Save($dosya, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output "kaydedildi: $dosya ($w x $ht)"
}

try { if (-not $p.HasExited) { $p.Kill(); $p.WaitForExit(5000) } } catch {}
Write-Output "bitti"
