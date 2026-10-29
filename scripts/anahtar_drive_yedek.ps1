# Kullanim: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\anahtar_drive_yedek.ps1  (yeni sifre/anahtar olusunca CALISTIR)
# 2026-10-29 (kullanıcı isteği: "ileride saklamam gereken bir şifre/anahtar var ise google drive hesabıma kaydet. bu bilgisayara
# ileride erişimim olmayabilir"). Gizli dosyalar geçici klasörde toplanır → rclone ile Drive "DepoWise_Anahtarlar" klasörüne kopyalanır →
# geçici klasör silinir. HİÇBİR değer ekrana yazılmaz; yalnız dosya adları ve sonuç yazılır.
$ErrorActionPreference = "Stop"
$kok = "C:\Users\Osman Alpaslan\Projects\DepoWise"
$gecici = Join-Path $env:TEMP ("dw_anahtar_" + [guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Path $gecici | Out-Null
try {
    $u = [Environment]::GetEnvironmentVariable("DEPOWISE_ADMIN_USER", "User")
    $p = [Environment]::GetEnvironmentVariable("DEPOWISE_ADMIN_PASS", "User")
    $t = [Environment]::GetEnvironmentVariable("DEPOWISE_ADMIN_TOTP", "User")
    $satirlar = @("# DepoWise YAYIN yönetici hesabı (masaüstü paket yayını: scripts/publish_release.mjs)",
                  "# Yeni bilgisayarda Windows kullanıcı ortam değişkeni olarak tanımlayın (setx).",
                  "DEPOWISE_ADMIN_USER=$u", "DEPOWISE_ADMIN_PASS=$p")
    if ($t) { $satirlar += "DEPOWISE_ADMIN_TOTP=$t" }
    Set-Content -Path (Join-Path $gecici "yayin_yonetici_hesabi.txt") -Value $satirlar -Encoding utf8

    foreach ($ad in ".env.test.local", ".env.supabase.local", ".env.pgtest.local") {
        $kaynak = Join-Path $kok $ad
        if (Test-Path $kaynak) { Copy-Item $kaynak (Join-Path $gecici ($ad.TrimStart('.') + ".txt")) }
    }
    $anahtarKlasoru = Join-Path $env:USERPROFILE ".claude-yedek\anahtarlar"
    if (Test-Path $anahtarKlasoru) { Get-ChildItem $anahtarKlasoru -File | ForEach-Object { Copy-Item $_.FullName (Join-Path $gecici $_.Name) } }

    $oku = @"
DepoWise — ŞİFRE VE ANAHTAR YEDEĞİ ($(Get-Date -Format 'yyyy-MM-dd HH:mm'))
==============================================================
Bu klasörü KİMSEYLE PAYLAŞMAYIN. Google hesabınızda iki adımlı doğrulama açık olmalı.
Yeni bir bilgisayarda Claude'a "Drive'daki DepoWise_Anahtarlar klasörünü kullanarak kurulumu yap" demeniz yeterli.

Dosyalar:
- yayin_yonetici_hesabi.txt     : Masaüstü sürümünü yayınlayan "superadmin" hesabı (kullanıcı adı + şifre).
- env.test.local.txt            : Otomatik testlerin hesapları + canlı duman testi hesabı (iki adımlı giriş anahtarı varsa içinde).
                                  Proje klasörüne ".env.test.local" adıyla konur.
- env.supabase.local.txt        : Canlı (üretim) veritabanı bağlantısı — ÇOK HASSAS. Proje klasörüne ".env.supabase.local" adıyla konur.
- env.pgtest.local.txt          : Bu bilgisayardaki test veritabanı ayarı (yeni bilgisayarda yeniden kurulabilir).
- DepoWise_yedek_anahtari.txt   : Drive'daki "DepoWise_Yedekler" şifreli veritabanı yedeklerini AÇMAK için anahtar.
                                  Bu olmadan yedekler açılamaz. Kurulum: scripts\yedek_anahtar_geri_yukle.ps1 -Kaynak <bu dosya>
- DepoWise_paket_imza_ozel.pem  : Masaüstü güncellemelerini İMZALAYAN gizli anahtar. Bu olmadan yeni sürüm yayınlanamaz
                                  (bilgisayarlar imzasız güncellemeyi kurmaz). Yeni bilgisayarda %USERPROFILE%\.claude-yedek\anahtarlar\ altına konur.
- DepoWise_MFA_ANAHTARI.txt     : Sunucunun iki adımlı giriş bilgilerini şifreleyen anahtar (Fly.io sunucu ayarında da kayıtlı).
                                  Kaybolursa iki adımlı girişler yeniden kurulmak zorunda kalır.
- DepoWise_ISTEMCI_IP_ANAHTARI.txt : Sunucu ile web arasındaki istemci adresi doğrulama anahtarı (Fly.io sunucu ayarında da kayıtlı).

Not: Fly.io, GitHub ve Supabase hesaplarınıza tarayıcıdan kendi Google/GitHub girişinizle girersiniz; onların ayrı şifresi burada yok.
"@
    Set-Content -Path (Join-Path $gecici "OKUBENI.txt") -Value $oku -Encoding utf8

    $rclone = (Get-Command rclone -ErrorAction SilentlyContinue).Source
    if (-not $rclone) { $rclone = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter rclone.exe | Select-Object -First 1 -ExpandProperty FullName }
    & $rclone copy $gecici "gdrive:DepoWise_Anahtarlar" --log-level ERROR
    if ($LASTEXITCODE -ne 0) { throw "rclone kopyalama hatası $LASTEXITCODE" }
    "Drive'a yüklendi:"
    & $rclone lsf "gdrive:DepoWise_Anahtarlar"
}
finally {
    Remove-Item -Recurse -Force $gecici -ErrorAction SilentlyContinue
}
