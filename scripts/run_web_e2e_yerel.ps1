# ⭐ M-69 (AR-GE 2026-10-26, F-022) — WEB KAYIT AKIŞLARI, YEREL ORTAMDA GERÇEK TARAYICIYLA.
# Canlı web testleri (tests/web-e2e, varsayılan yapılandırma) YALNIZ OKUR; hiçbir kayıt akışı tarayıcıda denenmiyordu.
# Bu betik BOŞ, GEÇİCİ bir SQLite veritabanıyla yerel API + web başlatır ve kayıt yazan testleri koşar:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run_web_e2e_yerel.ps1 [-Filtre "stok"]
# GÜVENLİK: DEPOWISE_PG_URL bu süreçlerde BOŞALTILIR (canlı/test PG'ye bağlanamaz); .env.test.local OKUNMAZ; veri klasörü
# %TEMP%\dw_e2e_* altında, koşu sonunda silinir. Portlar 5391 (API) / 5392 (web) — yalnız localhost.
# -SadeceSunucu: testleri koşmadan sunucuları açık tutar (yeni test yazarken ekranı keşfetmek için; Ctrl+C / süreç sonlandırma).
param([string]$Filtre = "", [switch]$SadeceSunucu)
$ErrorActionPreference = "Stop"
$kok = Split-Path $PSScriptRoot -Parent
$veri = Join-Path $env:TEMP ("dw_e2e_" + (Get-Date -Format "yyyyMMdd_HHmmss"))
New-Item -ItemType Directory -Force $veri | Out-Null
$apiAdres = "http://localhost:5391"; $webAdres = "http://localhost:5392"
$sifre = "E2eYerel!2026"

Write-Output "[e2e] derleniyor..."
& dotnet build (Join-Path $kok "src/DepoWise.Api/DepoWise.Api.csproj") -v q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Output "SONUC: DERLEME HATASI (API)"; exit 2 }
& dotnet build (Join-Path $kok "src/DepoWise.Web/DepoWise.Web.csproj") -v q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Output "SONUC: DERLEME HATASI (web)"; exit 2 }

# Süreç ortamı: yalnız bu betiğin başlattığı süreçlere geçer.
$env:DEPOWISE_PG_URL = $null
[Environment]::SetEnvironmentVariable("DEPOWISE_PG_URL", $null, "Process")
$env:DEPOWISE_SERVER_DATA = $veri
$env:DEPOWISE_JWT_KEY = "e2e-yerel-jwt-anahtari-0123456789-0123456789"
$env:DEPOWISE_SEED_SUPERADMIN_PASSWORD = $sifre
$env:DEPOWISE_SEED_ADMIN_PASSWORD = $sifre
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:DEPOWISE_CORS_ORIGINS = $webAdres

$api = $null; $web = $null; $basarili = $false
try {
    $env:ASPNETCORE_URLS = $apiAdres
    $api = Start-Process dotnet -ArgumentList @("run", "--no-build", "--no-launch-profile", "--project", "`"$(Join-Path $kok 'src/DepoWise.Api/DepoWise.Api.csproj')`"") `
        -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $veri "api.log") -RedirectStandardError (Join-Path $veri "api.err")
    $env:ASPNETCORE_URLS = $webAdres
    $env:Api__BaseUrl = $apiAdres
    $web = Start-Process dotnet -ArgumentList @("run", "--no-build", "--no-launch-profile", "--project", "`"$(Join-Path $kok 'src/DepoWise.Web/DepoWise.Web.csproj')`"") `
        -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $veri "web.log") -RedirectStandardError (Join-Path $veri "web.err")

    foreach ($adres in @("$apiAdres/health", "$webAdres/login")) {
        $hazir = $false
        for ($i = 0; $i -lt 90 -and -not $hazir; $i++) {
            try { $r = Invoke-WebRequest $adres -UseBasicParsing -TimeoutSec 3; $hazir = $r.StatusCode -eq 200 } catch { Start-Sleep -Seconds 1 }
        }
        if (-not $hazir) { Write-Output "SONUC: SUNUCU ACILMADI ($adres) — günlük: $veri"; exit 2 }
    }
    Write-Output "[e2e] API + web hazır (veri: $veri)"

    if ($SadeceSunucu) { Write-Output "[e2e] SADECE SUNUCU modu — açık"; while ($true) { Start-Sleep -Seconds 5; if ($api.HasExited -or $web.HasExited) { Write-Output "[e2e] sunucu kapandı"; exit 2 } } }
    $env:DEPOWISE_E2E_API = $apiAdres; $env:DEPOWISE_E2E_WEB = $webAdres; $env:DEPOWISE_E2E_SIFRE = $sifre
    Push-Location (Join-Path $kok "tests/web-e2e")
    try {
        $args2 = @("playwright", "test", "-c", "playwright.yerel.config.js")
        if ($Filtre) { $args2 += $Filtre }
        & npx @args2
        $kod = $LASTEXITCODE
    } finally { Pop-Location }
    if ($kod -eq 0) { Write-Output "SONUC: TUM YEREL WEB TESTLERI GECTI" } else { Write-Output "SONUC: YEREL WEB TESTI BASARISIZ (cikis $kod) — günlük: $veri" }
    $basarili = ($kod -eq 0)
    exit $kod
}
finally {
    foreach ($p in @($web, $api)) { if ($p -and -not $p.HasExited) { try { Stop-Process -Id $p.Id -Force } catch {} } }
    # dotnet run alt süreci (asıl sunucu) ayrı süreçtir → portu dinleyen süreci de kapat.
    foreach ($port in 5391, 5392) {
        Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
            ForEach-Object { try { Stop-Process -Id $_.OwningProcess -Force } catch {} }
    }
    Start-Sleep -Seconds 1
    if ($basarili) { Remove-Item $veri -Recurse -Force -ErrorAction SilentlyContinue }
}
