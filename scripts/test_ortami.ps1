# ⭐ 2026-10-30 — KALICI TEST ORTAMI (kullanıcı isteği: "çalışmaları canlıya almadan önce masaüstünde her yaptığın çalışmayı görebilmem
# için test ortamı oluştur; çalışmalarından sonra otomatik derle; giriş yapacağım uygulamayı masaüstüne simge olarak oluştur").
#
#   -Derle  : API + web + masaüstünü %LOCALAPPDATA%\DepoWiseTest altına derler; masaüstü YENİ bir sürüm klasörüne gider (açık test
#             uygulaması kapatılmaz, bir sonraki açılışta yeni sürüm gelir); çalışan test sunucusu/web yeni sürümle yeniden başlar;
#             masaüstüne "DepoWise Test" simgesi kurulur. Claude HER kullanıcıya görünür işten (commit) sonra çalıştırır.
#   -Baslat : (simgenin yaptığı) test sunucusu + web kapalıysa açar, deneme kullanıcılarını (yoksa) kurar, en yeni masaüstü sürümünü başlatır.
#
# GÜVENLİK: canlıya HİÇ bağlanmaz — DEPOWISE_PG_URL boşaltılır, sunucu yalnız http://localhost:5491 (web :5492), veri kalıcı ama AYRI
# (%LOCALAPPDATA%\DepoWiseTest\sunucu-veri, SQLite). Masaüstü verisi ayrı ortamda (DEPOWISE_ENVIRONMENT=TestOrtami). Gerçek veriye dokunmaz.
param([switch]$Derle, [switch]$Baslat)
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [Text.Encoding]::UTF8   # git günlüğü Türkçe karakterleri bozmasın
$kok = Join-Path $env:LOCALAPPDATA "DepoWiseTest"
$apiKlasor = Join-Path $kok "api"; $webKlasor = Join-Path $kok "web"; $veri = Join-Path $kok "sunucu-veri"
$apiAdres = "http://localhost:5491"; $webAdres = "http://localhost:5492"
$tohumSifre = "TestOrtami!2026"; $denemeSifre = "Deneme2026!"
New-Item -ItemType Directory -Force $kok, $veri | Out-Null
$gunluk = Join-Path $kok "test_ortami.log"
function Yaz($m) { $s = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $m"; Add-Content -Path $gunluk -Value $s -Encoding utf8; Write-Output $m }

function SunucuSurecleri {
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and ($_.CommandLine -like "*$apiKlasor*" -or $_.CommandLine -like "*$webKlasor*") }
}
function SunuculariDurdur { SunucuSurecleri | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue } }
function Ayakta($adres) { try { (Invoke-WebRequest $adres -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200 } catch { $false } }

function SunuculariBaslat {
    if ((Ayakta "$apiAdres/health") -and (Ayakta "$webAdres/login")) { return }
    SunuculariDurdur
    if (-not (Test-Path (Join-Path $apiKlasor "DepoWise.Api.dll"))) { throw "Test ortamı henüz derlenmemiş: scripts\test_ortami.ps1 -Derle" }
    # Yalnız bu süreçlerin ortamı (kalıcı Windows ayarı DEĞİL).
    $env:DEPOWISE_PG_URL = $null; [Environment]::SetEnvironmentVariable("DEPOWISE_PG_URL", $null, "Process")
    $env:DEPOWISE_SERVER_DATA = $veri
    $env:DEPOWISE_JWT_KEY = "test-ortami-yerel-jwt-anahtari-0123456789-abcdef"
    $env:DEPOWISE_SEED_SUPERADMIN_PASSWORD = $tohumSifre
    $env:DEPOWISE_SEED_ADMIN_PASSWORD = $tohumSifre
    $env:DEPOWISE_MFA_ANAHTARI = "dGVzdC1vcnRhbWktbWZhLWFuYWh0YXJpLTMyLWJ5dGU="   # yalnız yerel test anahtarı
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:DEPOWISE_CORS_ORIGINS = $webAdres
    $env:ASPNETCORE_URLS = $apiAdres
    Start-Process dotnet -ArgumentList "`"$(Join-Path $apiKlasor 'DepoWise.Api.dll')`"" -WorkingDirectory $apiKlasor -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $kok "api.log") -RedirectStandardError (Join-Path $kok "api.err") | Out-Null
    $env:ASPNETCORE_URLS = $webAdres
    $env:Api__BaseUrl = $apiAdres
    Start-Process dotnet -ArgumentList "`"$(Join-Path $webKlasor 'DepoWise.Web.dll')`"" -WorkingDirectory $webKlasor -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $kok "web.log") -RedirectStandardError (Join-Path $kok "web.err") | Out-Null
    foreach ($adres in @("$apiAdres/health", "$webAdres/login")) {
        $hazir = $false
        for ($i = 0; $i -lt 90 -and -not $hazir; $i++) { $hazir = Ayakta $adres; if (-not $hazir) { Start-Sleep -Seconds 1 } }
        if (-not $hazir) { throw "Test sunucusu açılamadı ($adres) — günlük: $kok" }
    }
    Yaz "Test sunucusu + web açık ($apiAdres, $webAdres)"
}

function Istek($yol, $govde, $jeton) {
    $h = @{}; if ($jeton) { $h.Authorization = "Bearer $jeton" }
    $p = @{ Uri = "$apiAdres$yol"; Headers = $h; ContentType = "application/json; charset=utf-8"; UseBasicParsing = $true }
    if ($null -ne $govde) { $p.Method = "Post"; $p.Body = [Text.Encoding]::UTF8.GetBytes(($govde | ConvertTo-Json -Compress)) } else { $p.Method = "Get" }
    Invoke-RestMethod @p
}

function DenemeKullanicilari {
    # Kurulu mu? deneme1 şifresiyle giriş deneyerek bak.
    try { $null = Istek "/api/auth/login" @{ username = "deneme1"; password = $denemeSifre; companyId = "DEPOWISE" }; return } catch { }
    $admin = (Istek "/api/auth/login" @{ username = "admin"; password = $tohumSifre; companyId = "DEPOWISE" }).token
    # Şube listesi düz dizi ya da { items: [...] } olarak gelebilir.
    function Subeler { $r = Istek "/api/branches" $null $admin; if ($r.items) { @($r.items) } else { @($r) | Where-Object { $_.id } } }
    $subeler = @(Subeler)
    if ($subeler.Count -eq 0) { $null = Istek "/api/branches" @{ name = "Merkez" } $admin; $subeler = @(Subeler) }
    $sube = $subeler[0].id
    if (-not $sube) { throw "Test ortamında şube bulunamadı" }
    foreach ($k in @(@("deneme1", "Deneme Masaüstü"), @("deneme2", "Deneme Web"))) {
        $null = Istek "/api/users" @{ username = $k[0]; password = "Gecici2026!"; fullName = $k[1]; roleKeys = @("role-company-admin"); branchId = $sube } $admin
        $g = Istek "/api/auth/login" @{ username = $k[0]; password = "Gecici2026!"; companyId = "DEPOWISE" }
        if ($g.mustChangePassword) { $null = Istek "/api/auth/change-initial-password" @{ newPassword = $denemeSifre } $g.token }
    }
    Yaz "Deneme kullanıcıları kuruldu (deneme1, deneme2)"
}

function EnYeniMasaustu {
    Get-ChildItem $kok -Directory -Filter "masaustu_*" | Sort-Object Name -Descending |
        Where-Object { Test-Path (Join-Path $_.FullName "DepoWise.Desktop.exe") } | Select-Object -First 1
}

function SimgeKur {
    $hedef = Join-Path $kok "baslat.ps1"
    Copy-Item $PSCommandPath $hedef -Force
    $masaustu = [Environment]::GetFolderPath("Desktop")
    $lnk = Join-Path $masaustu "DepoWise Test.lnk"
    $kabuk = New-Object -ComObject WScript.Shell
    $s = $kabuk.CreateShortcut($lnk)
    $s.TargetPath = Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe"
    $s.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$hedef`" -Baslat"
    $s.WorkingDirectory = $kok
    $s.Description = "DepoWise TEST ortamı — canlıya yayınlanmamış son çalışmalar (yerel test sunucusu, gerçek veriye dokunmaz)"
    $son = EnYeniMasaustu
    if ($son) { $s.IconLocation = (Join-Path $son.FullName "DepoWise.Desktop.exe") + ",0" }
    $s.Save()
}

if ($Derle) {
    $repo = Split-Path $PSScriptRoot -Parent
    $surum = Get-Date -Format "yyyyMMdd_HHmmss"
    $masaKlasor = Join-Path $kok "masaustu_$surum"
    $commit = (& git -C $repo log --oneline -1 2>$null)
    Yaz "Derleniyor: $commit"
    & dotnet publish (Join-Path $repo "src/DepoWise.Desktop/DepoWise.Desktop.csproj") -c Release -r win-x64 --self-contained false -p:UseAppHost=true -o $masaKlasor -v q -nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { Yaz "HATA: masaüstü derlenemedi"; exit 2 }
    Set-Content -Path (Join-Path $masaKlasor "serverurl.txt") -Value $apiAdres -Encoding ascii
    Set-Content -Path (Join-Path $masaKlasor "TEST_SURUMU.txt") -Value "$surum`r`n$commit" -Encoding utf8
    SunuculariDurdur
    foreach ($p in @(@("src/DepoWise.Api/DepoWise.Api.csproj", $apiKlasor), @("src/DepoWise.Web/DepoWise.Web.csproj", $webKlasor))) {
        & dotnet publish (Join-Path $repo $p[0]) -c Release -o $p[1] -v q -nologo | Out-Null
        if ($LASTEXITCODE -ne 0) { Yaz "HATA: $($p[0]) derlenemedi"; exit 2 }
    }
    SunuculariBaslat
    DenemeKullanicilari
    # Eski masaüstü sürümlerini temizle (en yeni 2 kalır; açık olan kilitliyse silinemez, sonra silinir).
    Get-ChildItem $kok -Directory -Filter "masaustu_*" | Sort-Object Name -Descending | Select-Object -Skip 2 |
        ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    SimgeKur
    Yaz "TAMAM: test sürümü hazır ($surum) — masaüstündeki 'DepoWise Test' simgesiyle açılır"
}

if ($Baslat) {
    try {
        SunuculariBaslat
        DenemeKullanicilari
        $son = EnYeniMasaustu
        if (-not $son) { throw "Test ortamı henüz derlenmemiş" }
        $env:DEPOWISE_ENVIRONMENT = "TestOrtami"
        Start-Process (Join-Path $son.FullName "DepoWise.Desktop.exe") -WorkingDirectory $son.FullName | Out-Null
        Yaz "Test uygulaması açıldı ($($son.Name))"
    }
    catch {
        Yaz "HATA: $($_.Exception.Message)"
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show("DepoWise Test açılamadı:`n$($_.Exception.Message)`n`nGünlük: $gunluk", "DepoWise Test") | Out-Null
    }
}
