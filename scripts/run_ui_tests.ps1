# ⭐ 2026-10-10 — MASAÜSTÜ ARAYÜZ TESTLERİ (Avalonia Headless = görünmez ekran)
#
# Normal test koşusu (scripts/run_tests.ps1) bu testleri ÇALIŞTIRMAZ — araç varsayılan olarak "kapalı".
# Ne zaman açılır (Claude karar verir): masaüstünde GÖRÜNÜM / FARE / KLAVYE davranışı değişen işler
# (tablo, sürükleme, form alanı görünürlüğü, hizalama, animasyon tetikleme vb.).
#
# Kullanım:  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run_ui_tests.ps1 [-Filter "UIT1"]
param([string]$Filter = "")

$ErrorActionPreference = "Stop"
$proje = Join-Path $PSScriptRoot "..\tests\DepoWise.Desktop.UiTests\DepoWise.Desktop.UiTests.csproj"

Write-Host "[1/2] Arayuz testleri derleniyor..."
& dotnet build $proje -v q --nologo
if ($LASTEXITCODE -ne 0) { Write-Host "SONUC: DERLEME BASARISIZ — testler calistirilmadi."; exit 1 }

Write-Host "[2/2] Arayuz testleri calisiyor..."
if ($Filter) { & dotnet test $proje --no-build --nologo --filter $Filter }
else         { & dotnet test $proje --no-build --nologo }
if ($LASTEXITCODE -ne 0) { Write-Host "SONUC: ARAYUZ TESTI BASARISIZ"; exit 1 }
Write-Host "SONUC: TUM ARAYUZ TESTLERI GECTI"
