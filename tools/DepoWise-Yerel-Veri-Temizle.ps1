# ═══════════════════════════════════════════════════════════════════════════════
#  Alpnex — YEREL VERİYİ TAMAMEN TEMİZLE
#  Bu makinedeki TÜM Alpnex yerel verisini KALICI siler:
#    • Veritabanı (malzeme/araç/stok/… + oturum önbelleği)   %LOCALAPPDATA%\Alpnex\
#    • Makine kimliği (firma/şube), "beni hatırla", güncelleme önbelleği, loglar
#    • Yedekler                                               Belgeler\Alpnex_Yedekler\
#  Sunucudaki veriye DOKUNMAZ. Geri alınamaz. Uygulama sonraki açılışta sıfırdan başlar.
# ═══════════════════════════════════════════════════════════════════════════════
$ErrorActionPreference = 'Continue'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$appData  = Join-Path $env:LOCALAPPDATA 'Alpnex'
$yedekler = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Alpnex_Yedekler'
$hedefler = @($appData, $yedekler)

function KlasorBoyutu($p) {
    if (-not (Test-Path $p)) { return $null }
    try {
        $b = (Get-ChildItem $p -Recurse -Force -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
        return [math]::Round(($b/1MB), 1)
    } catch { return $null }
}

Write-Host ''
Write-Host '  ================================================================'
Write-Host '   Alpnex - YEREL VERIYI TAMAMEN TEMIZLE'
Write-Host '  ================================================================'
Write-Host ''
Write-Host '  Bu makinedeki TUM Alpnex yerel verisi KALICI silinecek:'
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

# 1) Calisan Alpnex'i kapat (dosyalar kilitli kalmasin)
Write-Host ''
Write-Host '  Alpnex kapatiliyor (aciksa)...'
try { Get-Process 'DepoWise.Desktop' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 800 } catch {}

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

Write-Host ''
Write-Host ("  Tamamlandi. {0} konum temizlendi." -f $silinen)
Write-Host '  Artik Alpnex''i acip giris yaptiginizda bos, temiz bir yerelle baslayacaksiniz.'
Write-Host ''
Read-Host '  Kapatmak icin Enter'
