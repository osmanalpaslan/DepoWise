# ⭐ 2026-10-10 — HAFTALIK ÜRETİM VERİTABANI YEDEĞİ → Google Drive (osmanalpaslan0101, klasör DepoWise_Yedekler)
#
# Ne yapar:  Supabase'deki canlı veritabanının tam yedeğini (pg_dump, sıkıştırılmış) alır, yedeğin açılabildiğini
#            doğrular (pg_restore -l), Google Drive'a yükler. Eski yedekleri temizler:
#            Drive'da 120 günden eski, bu bilgisayarda 30 günden eski yedekler silinir.
# Gizli bilgi: bağlantı bilgisi .env.supabase.local'dan, Drive yetkisi rclone ayarından okunur. Hiçbiri
#            ekrana/log'a yazılmaz, hiçbir yere gönderilmez. Canlı veritabanına YALNIZ OKUMA yapılır.
# Çalıştıran: Windows Görev Zamanlayıcısı "DepoWise Haftalik Yedek" (Pazar 03:00; kaçarsa açılışta).
# Elle:      powershell -NoProfile -ExecutionPolicy Bypass -File scripts\yedek_al.ps1
$ErrorActionPreference = "Stop"
$kok = Split-Path $PSScriptRoot -Parent
$log = Join-Path $env:LOCALAPPDATA "DepoWise\yedek_log.txt"
$yerel = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "DepoWise_Yedekler"
New-Item -ItemType Directory -Force -Path (Split-Path $log), $yerel | Out-Null
function Yaz($m) { "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $m" | Add-Content -Path $log -Encoding utf8 }

try {
    $pgbin = "C:\Program Files\PostgreSQL\17\bin"
    $rclone = (Get-Command rclone -ErrorAction SilentlyContinue).Source
    if (-not $rclone) { $rclone = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter rclone.exe | Select-Object -First 1 -ExpandProperty FullName }
    if (-not $rclone) { throw "rclone bulunamadi" }

    # Bağlantı: .env.supabase.local → DEPOWISE_PG_URL_SUPABASE (Host=..;Port=..;Database=..;Username=..;Password=..)
    $satir = Get-Content (Join-Path $kok ".env.supabase.local") | Where-Object { $_ -like "DEPOWISE_PG_URL_SUPABASE=*" } | Select-Object -First 1
    if (-not $satir) { throw ".env.supabase.local icinde DEPOWISE_PG_URL_SUPABASE yok" }
    $kv = @{}
    foreach ($p in ($satir.Substring($satir.IndexOf("=") + 1).Trim('"') -split ";")) {
        $i = $p.IndexOf("="); if ($i -gt 0) { $kv[$p.Substring(0, $i).Trim().ToLower()] = $p.Substring($i + 1).Trim() }
    }
    $env:PGHOST = $kv["host"]; $env:PGPORT = $(if ($kv["port"]) { $kv["port"] } else { "5432" })
    $env:PGDATABASE = $kv["database"]; $env:PGUSER = $kv["username"]; $env:PGPASSWORD = $kv["password"]; $env:PGSSLMODE = "require"

    $dosya = Join-Path $yerel ("DepoWise_prod_{0}.dump" -f (Get-Date -Format "yyyy-MM-dd_HHmm"))
    & "$pgbin\pg_dump.exe" -Fc --no-owner --no-privileges -f $dosya
    if ($LASTEXITCODE -ne 0) { throw "pg_dump basarisiz (kod $LASTEXITCODE)" }
    $tablo = (& "$pgbin\pg_restore.exe" -l $dosya | Select-String "TABLE DATA").Count
    if ($tablo -lt 50) { throw "yedek supheli: yalniz $tablo tablo verisi" }

    & $rclone copy $dosya "gdrive:DepoWise_Yedekler" --log-level ERROR
    if ($LASTEXITCODE -ne 0) { throw "Drive yuklemesi basarisiz (kod $LASTEXITCODE)" }
    & $rclone delete "gdrive:DepoWise_Yedekler" --min-age 120d --log-level ERROR
    Get-ChildItem $yerel -Filter "DepoWise_prod_*.dump" | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } | Remove-Item -Force

    Yaz ("BASARILI  {0}  {1:N0} KB  {2} tablo  -> Drive" -f (Split-Path $dosya -Leaf), ((Get-Item $dosya).Length / 1KB), $tablo)

    # FOTOĞRAFLAR: veritabanında değil, sunucu diskinde (/data/files). Sunucuda geçici arşiv → indir → Drive.
    # Bu adım başarısız olursa veritabanı yedeği YİNE geçerlidir; yalnız log'a uyarı düşer.
    try {
        # flyctl bilgi mesajlarını ("Connecting…") stderr'e yazar; PS 5.1 bunu hata sanmasın.
        $ErrorActionPreference = "Continue"
        $foto = Join-Path $yerel ("DepoWise_fotolar_{0}.tgz" -f (Get-Date -Format "yyyy-MM-dd_HHmm"))
        flyctl ssh console -a depowise-erp -C "tar czf /tmp/dw_fotolar.tgz -C /data files" 2>$null | Out-Null
        flyctl ssh sftp get /tmp/dw_fotolar.tgz $foto -a depowise-erp 2>$null | Out-Null
        flyctl ssh console -a depowise-erp -C "rm -f /tmp/dw_fotolar.tgz" 2>$null | Out-Null
        if (-not (Test-Path $foto) -or (Get-Item $foto).Length -lt 1KB) { throw "foto arsivi inmedi" }
        & $rclone copy $foto "gdrive:DepoWise_Yedekler" --log-level ERROR
        if ($LASTEXITCODE -ne 0) { throw "foto Drive yuklemesi basarisiz" }
        Get-ChildItem $yerel -Filter "DepoWise_fotolar_*.tgz" | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } | Remove-Item -Force
        Yaz ("BASARILI  {0}  {1:N0} KB  -> Drive" -f (Split-Path $foto -Leaf), ((Get-Item $foto).Length / 1KB))
    }
    catch { Yaz "UYARI  fotograf yedegi alinamadi: $($_.Exception.Message)" }
}
catch { Yaz "HATA  $($_.Exception.Message)"; exit 1 }
finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }
