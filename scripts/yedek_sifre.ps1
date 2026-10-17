# ⭐ M-08 (AR-GE 2026-10-17) — YEDEK ŞİFRELEME YARDIMCILARI (yedek_al.ps1 ve yedek_prova.ps1 içeri alır: . .\yedek_sifre.ps1)
#
# Yöntem: standart OpenSSL (Git for Windows ile gelir) — AES-256-CBC, PBKDF2 (200.000 tur, SHA-256), rastgele tuz.
#         Yedek, bu programlar olmadan da her bilgisayarda aynı komutla açılabilir (anahtar dosyasındaki açıklamaya bakın).
# Anahtar: bu bilgisayarda %LOCALAPPDATA%\DepoWise\yedek_anahtar.dpapi (Windows DPAPI — yalnız bu kullanıcı açabilir);
#          bilgisayar dışı kopya kullanıcı kararıyla özel GitHub hafıza deposunda (claude-ortak-hafiza/anahtarlar).
#          Anahtar hiçbir zaman ekrana, log'a ya da komut satırına yazılmaz (openssl'e ortam değişkeniyle verilir).
$script:YedekAnahtarDosyasi = Join-Path $env:LOCALAPPDATA "DepoWise\yedek_anahtar.dpapi"

function Get-OpenSsl {
    $o = (Get-Command openssl -ErrorAction SilentlyContinue).Source
    if (-not $o) { $o = "C:\Program Files\Git\usr\bin\openssl.exe" }
    if (-not (Test-Path $o)) { throw "openssl bulunamadi (Git for Windows kurulu olmali)" }
    return $o
}

function Test-YedekAnahtar { return (Test-Path $script:YedekAnahtarDosyasi) }

function Invoke-YedekOpenSsl([string]$Girdi, [string]$Cikti, [switch]$Coz) {
    if (-not (Test-YedekAnahtar)) { throw "yedek anahtari yok ($script:YedekAnahtarDosyasi)" }
    $ss = Get-Content $script:YedekAnahtarDosyasi | ConvertTo-SecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($ss)
    try {
        $env:DW_YEDEK_ANAHTAR = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        $arg = @("enc") + $(if ($Coz) { @("-d") } else { @() }) + @("-aes-256-cbc", "-pbkdf2", "-iter", "200000", "-md", "sha256",
               "-pass", "env:DW_YEDEK_ANAHTAR", "-in", $Girdi, "-out", $Cikti)
        & (Get-OpenSsl) @arg
        if ($LASTEXITCODE -ne 0) { throw "openssl $(if ($Coz) { 'cozme' } else { 'sifreleme' }) basarisiz (kod $LASTEXITCODE)" }
    }
    finally {
        Remove-Item Env:DW_YEDEK_ANAHTAR -ErrorAction SilentlyContinue
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

# Şifreler, hemen geri açıp özgün dosyayla SHA-256 karşılaştırarak DOĞRULAR; sonra düz dosyayı siler. Döner: .enc yolu.
function Protect-YedekDosyasi([string]$Dosya) {
    $enc = "$Dosya.enc"
    $dogrula = "$Dosya.dogrula"
    Invoke-YedekOpenSsl -Girdi $Dosya -Cikti $enc
    Invoke-YedekOpenSsl -Girdi $enc -Cikti $dogrula -Coz
    try {
        if ((Get-FileHash $Dosya -Algorithm SHA256).Hash -ne (Get-FileHash $dogrula -Algorithm SHA256).Hash) {
            Remove-Item $enc -Force -ErrorAction SilentlyContinue
            throw "sifreli yedek dogrulanamadi (acilan dosya ozgunuyle ayni degil)"
        }
    }
    finally { Remove-Item $dogrula -Force -ErrorAction SilentlyContinue }
    Remove-Item $Dosya -Force
    return $enc
}

# .enc dosyasını açar; döner: açılmış dosyanın yolu (.enc uzantısı atılmış).
function Unprotect-YedekDosyasi([string]$EncDosya) {
    $cikti = $EncDosya.Substring(0, $EncDosya.Length - 4)
    Invoke-YedekOpenSsl -Girdi $EncDosya -Cikti $cikti -Coz
    return $cikti
}
