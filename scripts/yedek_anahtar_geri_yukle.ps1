# ⭐ M-08 (AR-GE 2026-10-17) — YEDEK ANAHTARINI YENİ BİLGİSAYARA YÜKLE
# Bilgisayar değiştiğinde ya da anahtar silindiğinde: özel GitHub hafıza deposundaki anahtar dosyasını bu bilgisayarın
# Windows korumalı (DPAPI) anahtar dosyasına yazar. Anahtar ekrana yazılmaz.
# Elle: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\yedek_anahtar_geri_yukle.ps1 [-Kaynak <dosya>]
param([string]$Kaynak = (Join-Path $env:USERPROFILE ".claude-yedek\anahtarlar\DepoWise_yedek_anahtari.txt"))
$ErrorActionPreference = "Stop"
if (-not (Test-Path $Kaynak)) { throw "anahtar dosyasi bulunamadi: $Kaynak (once geri-yukle.ps1 ile hafiza deposunu indirin)" }
$satir = Get-Content $Kaynak -Encoding UTF8 | Where-Object { $_ -and -not $_.StartsWith("#") } | Select-Object -First 1
if (-not $satir -or $satir.Trim().Length -lt 32) { throw "anahtar dosyasinda gecerli anahtar satiri yok" }
$hedef = Join-Path $env:LOCALAPPDATA "DepoWise\yedek_anahtar.dpapi"
New-Item -ItemType Directory -Force (Split-Path $hedef) | Out-Null
if (Test-Path $hedef) { Copy-Item $hedef "$hedef.eski_$(Get-Date -Format yyyyMMddHHmmss)" }
(ConvertTo-SecureString $satir.Trim() -AsPlainText -Force | ConvertFrom-SecureString) | Set-Content -Path $hedef -Encoding ascii
"Yedek anahtari yuklendi: $hedef"
