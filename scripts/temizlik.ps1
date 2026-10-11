# ⭐ 2026-10-11 — BİTMİŞ TEST / GEÇİCİ İŞ VERİLERİNİ OTOMATİK TEMİZLEME (kullanıcı kalıcı kuralı)
#
# Neden: test koşuları, yayın hazırlıkları ve Claude oturumları zamanla disk üzerinde GB'larca artık bırakıyor
#        (2026-10-11 ölçümü: ~10 GB). Kullanıcı: "işimizin tamamen bittiği test verilerini otomatik olarak sil".
#
# GÜVENLİK İLKESİ — YALNIZ İZİN LİSTESİ: aşağıda adı geçen klasör/desenler dışında HİÇBİR ŞEYE dokunulmaz.
#   ASLA silinmez: gerçek uygulama verisi (%LOCALAPPDATA%\Alpnex, DepoWise\Data — UiTest_* hariç), uygulama yedekleri
#   (DepoWise\backup), Drive yedeklerinin yerel kopyası (Belgeler\DepoWise_Yedekler), yerel PG test sunucusu
#   (pgsql17, pgtest), NuGet/Playwright önbellekleri, bin/obj, kaynak kod, git, .env dosyaları.
#   "Bitmiş" = belirtilen günden ESKİ (en yeni dosyasına göre). Test koşusu sürerken temizlik YAPILMAZ.
#
# Çalıştıran: Görev Zamanlayıcı "DepoWise Haftalik Temizlik" (Pazar 05:00) + Claude her yayın/iş sonunda.
# Elle:      powershell -NoProfile -ExecutionPolicy Bypass -File scripts\temizlik.ps1 [-Kuru]   (-Kuru: yalnız listeler)
param([switch]$Kuru, [int]$Gun = 3)
$ErrorActionPreference = "Continue"
$kok = Split-Path $PSScriptRoot -Parent
$log = Join-Path $env:LOCALAPPDATA "DepoWise\temizlik_log.txt"
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
$sinir = (Get-Date).AddDays(-$Gun)
$toplam = 0L; $adet = 0
$liste = New-Object System.Collections.Generic.List[string]

function EnYeni($yol) {
    $i = Get-Item $yol -Force -ErrorAction SilentlyContinue
    if (-not $i) { return [datetime]::MaxValue }
    if (-not $i.PSIsContainer) { return $i.LastWriteTime }
    $f = Get-ChildItem $yol -Recurse -Force -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($f) { return $f.LastWriteTime } else { return $i.LastWriteTime }
}
function Boyut($yol) {
    $i = Get-Item $yol -Force -ErrorAction SilentlyContinue
    if (-not $i) { return 0L }
    if (-not $i.PSIsContainer) { return $i.Length }
    $s = (Get-ChildItem $yol -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    if ($s) { return [long]$s } else { return 0L }
}
function Sil($yol, [datetime]$esik = $sinir) {
    if (-not (Test-Path $yol)) { return }
    if ((EnYeni $yol) -gt $esik) { return }           # henüz bitmemiş / yeni
    $b = Boyut $yol
    $script:toplam += $b; $script:adet++
    $script:liste.Add(("{0,8:N0} MB  {1}" -f ($b / 1MB), $yol))
    if (-not $Kuru) { Remove-Item $yol -Recurse -Force -ErrorAction SilentlyContinue }
}

# Test koşusu sürüyorsa dokunma (run_tests.ps1 kilidi).
$m = New-Object System.Threading.Mutex($false, "Local\AlpnexTestKosusu")
if (-not $m.WaitOne(0)) { "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  TEMIZLIK ATLANDI: test kosusu suruyor" | Add-Content $log -Encoding utf8; exit 0 }
try {
    $t = $env:TEMP
    # 1) Test koşularının geçici veritabanları / klasörleri + yayın/ses/prova artıkları
    foreach ($desen in @("depowise_*", "dw_*", "dw-ses-*", "dw_prova_*", "supabase_windows_*.tar.gz")) {
        # Test koşusu sürmüyorsa (kilit alındı) bu artıklar BİTMİŞTİR → 1 saatten eski olan silinir.
        Get-ChildItem $t -Filter $desen -Force -ErrorAction SilentlyContinue | ForEach-Object { Sil $_.FullName (Get-Date).AddHours(-1) }
    }
    # 2) Bitmiş Claude oturumlarının çalışma klasörleri (7 günden eski; açık oturum hep yeni olduğu için korunur)
    Get-ChildItem (Join-Path $t "claude") -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "C--Users-*" } | ForEach-Object {
            Get-ChildItem $_.FullName -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object { Sil $_.FullName (Get-Date).AddDays(-7) }
        }
    # 3) artifacts/ — eski QA/test verileri ve gevşek görüntü/log dosyaları (yedek klasörleri, rc ve ui-ekran HARİÇ)
    $art = Join-Path $kok "artifacts"
    $koru = @("rc", "backups", "prod-backup", "yerel-db-yedek", "db-onarim", "ui-ekran", "test-sonuc.trx")
    Get-ChildItem $art -Force -ErrorAction SilentlyContinue | Where-Object { $koru -notcontains $_.Name } | ForEach-Object { Sil $_.FullName }
    # 3b) artifacts/rc — yalnız SON 3 masaüstü sürümünün zip'i kalır; açılmış klasörlerden yalnız en yenisi
    $rc = Join-Path $art "rc"
    $zipler = Get-ChildItem $rc -Filter "DepoWise-desktop-*.zip" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
    $zipler | Select-Object -Skip 3 | ForEach-Object { Sil $_.FullName ([datetime]::MaxValue) }
    $klasorler = Get-ChildItem $rc -Directory -Filter "desktop*" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
    $klasorler | Select-Object -Skip 1 | ForEach-Object { Sil $_.FullName ([datetime]::MaxValue) }
    # 4) publish/ — eski elle yayın paketleri (artık artifacts/rc kullanılıyor)
    Get-ChildItem (Join-Path $kok "publish") -Force -ErrorAction SilentlyContinue | ForEach-Object { Sil $_.FullName }
    # 5) Playwright sonuç/rapor klasörleri
    foreach ($k in @("tests\web-e2e\test-results", "tests\web-e2e\playwright-report")) { Sil (Join-Path $kok $k) }
    # 6) Masaüstü arayüz testlerinin ayrı test ortamları (gerçek Data klasörü DEĞİL)
    Get-ChildItem (Join-Path $env:LOCALAPPDATA "DepoWise\Data") -Directory -Filter "UiTest_*" -ErrorAction SilentlyContinue | ForEach-Object { Sil $_.FullName }

    $ozet = "{0}  TEMIZLIK{1}  {2} oge  {3:N0} MB" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $(if ($Kuru) { " (KURU DENEME)" } else { "" }), $adet, ($toplam / 1MB)
    $ozet | Add-Content $log -Encoding utf8
    $ozet
    $liste | Sort-Object -Descending | Select-Object -First 40
}
finally { $m.ReleaseMutex(); $m.Dispose() }
