# ═══════════════════════════════════════════════════════════════════════════════
#  Alpnex — UYGULAMAYI TAMAMEN KALDIR
#  Bu araç Alpnex masaüstü uygulamasını ve tüm yerel izlerini bu makineden siler.
#  (Uygulama hafif kurulumla geldiği için Denetim Masası'nda görünmez; kaldırma buradan yapılır.)
#
#  Silinenler:
#    • Uygulama + yerel veri + güncelleme önbelleği + loglar + makine kimliği
#        %LOCALAPPDATA%\Alpnex\   (ve varsa eski %LOCALAPPDATA%\DepoWise\)
#    • Yedekler   Belgeler\Alpnex_Yedekler\   (ve varsa Belgeler\DepoWise_Yedekler\)
#    • Masaüstü kısayolları   Alpnex.lnk / DepoWise.lnk  (kullanıcı + ortak masaüstü)
#
#  Sunucudaki veriye DOKUNMAZ. GERİ ALINAMAZ. Yönetici gerekmez.
#  Sonra: kurulum aracını (AlpnexSetup.exe) yeniden çalıştırıp TEMİZ kurulum yapabilirsiniz.
# ═══════════════════════════════════════════════════════════════════════════════
$ErrorActionPreference = 'Continue'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$local    = $env:LOCALAPPDATA
$belgeler = [Environment]::GetFolderPath('MyDocuments')
$masaustu = [Environment]::GetFolderPath('DesktopDirectory')
$ortakMas = [Environment]::GetFolderPath('CommonDesktopDirectory')

# Silinecek KLASÖRLER (uygulama + veri + yedekler; yeni Alpnex + eski DepoWise)
$klasorler = @(
    (Join-Path $local    'Alpnex'),
    (Join-Path $local    'DepoWise'),
    (Join-Path $belgeler 'Alpnex_Yedekler'),
    (Join-Path $belgeler 'DepoWise_Yedekler')
)
# Silinecek KISAYOLLAR (kullanıcı + ortak masaüstü, yeni + eski)
$kisayollar = @(
    (Join-Path $masaustu 'Alpnex.lnk'),
    (Join-Path $masaustu 'DepoWise.lnk'),
    (Join-Path $ortakMas 'Alpnex.lnk'),
    (Join-Path $ortakMas 'DepoWise.lnk')
)

function KlasorBoyutu($p) {
    if (-not (Test-Path $p)) { return $null }
    try {
        $b = (Get-ChildItem $p -Recurse -Force -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
        return [math]::Round(($b/1MB), 1)
    } catch { return $null }
}

Write-Host ''
Write-Host '  ================================================================'
Write-Host '   Alpnex - UYGULAMAYI TAMAMEN KALDIR'
Write-Host '  ================================================================'
Write-Host ''
Write-Host '  Bu makineden Alpnex uygulamasi ve TUM yerel izleri silinecek:'
Write-Host ''
$varMi = $false
foreach ($h in $klasorler) {
    if (Test-Path $h) {
        $varMi = $true
        $sz = KlasorBoyutu $h
        $szTxt = if ($sz -ne $null) { "$sz MB" } else { '' }
        Write-Host ("   [SILINECEK] {0}   {1}" -f $h, $szTxt)
    } else {
        Write-Host ("   [zaten yok] {0}" -f $h)
    }
}
foreach ($k in $kisayollar) {
    if (Test-Path $k) { $varMi = $true; Write-Host ("   [SILINECEK] {0}" -f $k) }
}
Write-Host ''
Write-Host '  ! SUNUCUDAKI veriye DOKUNULMAZ. Bu islem GERI ALINAMAZ.'
Write-Host '  ! Sonra kurulum aracini (AlpnexSetup.exe) calistirip temiz kurabilirsiniz.'
Write-Host ''

if (-not $varMi) {
    Write-Host '  Kaldirilacak bir sey bulunamadi. Bu makinede Alpnex zaten kurulu degil.'
    Write-Host ''
    Read-Host '  Kapatmak icin Enter'
    return
}

$onay = Read-Host '  Kaldirmak icin buyuk harflerle  KALDIR  yazip Enter (iptal: bos birak)'
if ($onay -ne 'KALDIR') {
    Write-Host ''
    Write-Host '  Iptal edildi. Hicbir sey silinmedi.'
    Read-Host '  Kapatmak icin Enter'
    return
}

# 1) Calisan uygulamayi kapat (dosyalar kilitli kalmasin). Exe adi ic kod adiyla ayni: DepoWise.Desktop
Write-Host ''
Write-Host '  Uygulama kapatiliyor (aciksa)...'
foreach ($proc in @('DepoWise.Desktop', 'Alpnex.Desktop')) {
    try { Get-Process $proc -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue } catch {}
}
Start-Sleep -Milliseconds 900

# 2) Sil
$silinen = 0
foreach ($h in $klasorler) {
    if (Test-Path $h) {
        try {
            Remove-Item $h -Recurse -Force -ErrorAction Stop
            Write-Host ("   silindi : {0}" -f $h); $silinen++
        } catch {
            Write-Host ("   HATA    : {0}  ->  {1}" -f $h, $_.Exception.Message)
            Write-Host '             (Uygulama hala acik olabilir; kapatip tekrar deneyin.)'
        }
    }
}
foreach ($k in $kisayollar) {
    if (Test-Path $k) {
        try { Remove-Item $k -Force -ErrorAction Stop; Write-Host ("   silindi : {0}" -f $k); $silinen++ } catch {}
    }
}

Write-Host ''
Write-Host ("  Tamamlandi. {0} konum kaldirildi. Alpnex bu makineden silindi." -f $silinen)
Write-Host '  Temiz kurulum icin: web -> Kurulum Aracini Indir (AlpnexSetup.exe) -> calistir.'
Write-Host ''
Read-Host '  Kapatmak icin Enter'
