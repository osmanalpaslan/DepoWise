# ═══════════════════════════════════════════════════════════════════════════════
#  Alpnex — YEREL VERİYİ TAMAMEN TEMİZLE  (marka geçişi dahil)
#  Bu makinedeki TÜM Alpnex yerel verisini + varsa ESKİ "DepoWise" kalıntılarını KALICI siler:
#    • Yeni  : %LOCALAPPDATA%\Alpnex\            + Belgeler\Alpnex_Yedekler\
#    • Eski  : %LOCALAPPDATA%\DepoWise\          + Belgeler\DepoWise_Yedekler\   (marka öncesi)
#    • Eski masaüstü kısayolu: DepoWise.lnk (varsa kaldırılır)
#  (Veritabanı/malzeme/araç/stok + oturum önbelleği, makine kimliği, "beni hatırla",
#   güncelleme önbelleği, loglar ve yerel yedekler dahil.)
#  Sunucudaki veriye DOKUNMAZ. Geri alınamaz. Uygulama sonraki açılışta sıfırdan başlar.
# ═══════════════════════════════════════════════════════════════════════════════
$ErrorActionPreference = 'Continue'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$local     = $env:LOCALAPPDATA
$belgeler  = [Environment]::GetFolderPath('MyDocuments')
$masaustu  = [Environment]::GetFolderPath('DesktopDirectory')

# Silinecek klasörler: yeni (Alpnex) + eski (DepoWise) kalıntıları
$hedefler = @(
    (Join-Path $local    'Alpnex'),
    (Join-Path $local    'DepoWise'),
    (Join-Path $belgeler 'Alpnex_Yedekler'),
    (Join-Path $belgeler 'DepoWise_Yedekler')
)
# Kaldırılacak eski kısayol (marka öncesi)
$eskiKisayol = Join-Path $masaustu 'DepoWise.lnk'

function KlasorBoyutu($p) {
    if (-not (Test-Path $p)) { return $null }
    try {
        $b = (Get-ChildItem $p -Recurse -Force -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
        return [math]::Round(($b/1MB), 1)
    } catch { return $null }
}

Write-Host ''
Write-Host '  ================================================================'
Write-Host '   Alpnex - YEREL VERIYI TAMAMEN TEMIZLE (marka gecisi dahil)'
Write-Host '  ================================================================'
Write-Host ''
Write-Host '  Bu makinedeki TUM yerel veri (Alpnex + eski DepoWise) KALICI silinecek:'
Write-Host ''
$varMi = $false
foreach ($h in $hedefler) {
    if (Test-Path $h) {
        $varMi = $true
        $sz = KlasorBoyutu $h
        $szTxt = if ($sz -ne $null) { "$sz MB" } else { '' }
        Write-Host ("   [SILINECEK] {0}   {1}" -f $h, $szTxt)
    } else {
        Write-Host ("   [zaten yok] {0}" -f $h)
    }
}
if (Test-Path $eskiKisayol) { $varMi = $true; Write-Host ("   [SILINECEK] {0}" -f $eskiKisayol) }
Write-Host ''
Write-Host '  ! SUNUCUDAKI veriye DOKUNULMAZ. Bu islem GERI ALINAMAZ.'
Write-Host '  ! Uygulama bir sonraki acilista sifirdan (bos) baslar.'
Write-Host ''

if (-not $varMi) {
    Write-Host '  Silinecek yerel veri bulunamadi. Bu makine zaten temiz.'
    Write-Host ''
    Read-Host '  Kapatmak icin Enter'
    return
}

$onay = Read-Host '  Devam etmek icin buyuk harflerle  EVET  yazip Enter (iptal: bos birak)'
if ($onay -ne 'EVET') {
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
Start-Sleep -Milliseconds 800

# 2) Sil
$silinen = 0
foreach ($h in $hedefler) {
    if (Test-Path $h) {
        try {
            Remove-Item $h -Recurse -Force -ErrorAction Stop
            Write-Host ("   silindi : {0}" -f $h)
            $silinen++
        } catch {
            Write-Host ("   HATA    : {0}  ->  {1}" -f $h, $_.Exception.Message)
            Write-Host '             (Uygulama hala acik olabilir; kapatip tekrar deneyin.)'
        }
    }
}
if (Test-Path $eskiKisayol) {
    try { Remove-Item $eskiKisayol -Force -ErrorAction Stop; Write-Host ("   silindi : {0}" -f $eskiKisayol); $silinen++ } catch {}
}

Write-Host ''
Write-Host ("  Tamamlandi. {0} konum temizlendi." -f $silinen)
Write-Host '  Artik Alpnex kurup giris yaptiginizda bos, temiz bir yerelle baslayacaksiniz.'
Write-Host ''
Read-Host '  Kapatmak icin Enter'
