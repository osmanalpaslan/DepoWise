# ⭐ 2026-10-10 — AYLIK YEDEKTEN GERİ YÜKLEME PROVASI (kullanıcının seçtiği öneri 5)
#
# Neden: haftalık yedek alınıyordu (yedek_al.ps1) ama hiç GERİ AÇILIP denenmemişti. Açılamayan yedek,
#        yedek değildir. Bu betik ayda bir, gerçek bir felaket anındaki adımların aynısını yapar.
# Ne yapar:
#   1. Google Drive'daki EN YENİ veritabanı yedeğini (+ yanındaki sayım listesini) indirir — Drive kopyası sınanır.
#   2. Bu bilgisayardaki YEREL test PostgreSQL'ine (localhost:54329) boş bir veritabanı olarak geri yükler.
#      Canlı veritabanına HİÇ bağlanmaz; gerçek veriye dokunmaz.
#   3. Kontroller: geri yükleme hatasız mı · tablo sayısı · şema sürümü · her tablonun satır sayısı yedek
#      anındaki sayım listesiyle tutarlı mı (yedekteki ≥ listedeki ve en fazla +50) · firma ve kullanıcı var mı.
#   4. Sonucu yedek_log.txt'ye ve Belgeler\DepoWise_Yedekler\prova_son.json'a yazar; deneme veritabanını siler.
#      Başarısızsa Windows bildirimi gösterir.
# Çalıştıran: Görev Zamanlayıcı "DepoWise Aylik Yedek Provasi" (her ayın ilk Pazarı 04:00; kaçarsa açılışta).
# Elle:      powershell -NoProfile -ExecutionPolicy Bypass -File scripts\yedek_prova.ps1
# -SayimListesi: YALNIZ provanın kendisini sınamak için (bozuk liste verip hatayı yakaladığını görmek). Normal çalışmada boş.
param([string]$SayimListesi = "")
$ErrorActionPreference = "Stop"
$log = Join-Path $env:LOCALAPPDATA "DepoWise\yedek_log.txt"
$yerel = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "DepoWise_Yedekler"
$gecici = Join-Path $env:TEMP ("dw_prova_" + [guid]::NewGuid().ToString("N").Substring(0, 8))
$bin = Join-Path $env:LOCALAPPDATA "DepoWise\pgsql17\pgsql\bin"
$db = "depowise_restore_drill"
New-Item -ItemType Directory -Force -Path $gecici, $yerel, (Split-Path $log) | Out-Null
function Yaz($m) { "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $m" | Add-Content -Path $log -Encoding utf8 }
function Bildir($baslik, $metin) {
    try {
        [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
        $x = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
        $t = $x.GetElementsByTagName("text"); $t.Item(0).AppendChild($x.CreateTextNode($baslik)) | Out-Null; $t.Item(1).AppendChild($x.CreateTextNode($metin)) | Out-Null
        [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier("DepoWise").Show([Windows.UI.Notifications.ToastNotification]::new($x))
    } catch { }
}
function Psql([string]$sorgu, [string]$veritabani = $db) { & "$bin\psql.exe" -d $veritabani -At -F "|" -c $sorgu }

$sonuc = [ordered]@{ tarih = (Get-Date).ToString("s"); yedek = $null; basarili = $false; sema = $null; tablo = 0; uyumsuz = @(); not = $null }
try {
    $rclone = (Get-Command rclone -ErrorAction SilentlyContinue).Source
    if (-not $rclone) { $rclone = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter rclone.exe | Select-Object -First 1 -ExpandProperty FullName }
    if (-not $rclone) { throw "rclone bulunamadi" }
    if (-not (Test-Path "$bin\pg_restore.exe")) { throw "yerel PostgreSQL araclari yok ($bin)" }

    # 1) Drive'daki en yeni yedek (adlar tarih sıralıdır: DepoWise_prod_yyyy-MM-dd_HHmm.dump)
    $ad = & $rclone lsf "gdrive:DepoWise_Yedekler" --include "DepoWise_prod_*.dump" | Sort-Object | Select-Object -Last 1
    if (-not $ad) { throw "Drive'da veritabani yedegi bulunamadi" }
    $sonuc.yedek = $ad
    & $rclone copy "gdrive:DepoWise_Yedekler/$ad" $gecici --log-level ERROR
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $gecici $ad))) { throw "yedek Drive'dan indirilemedi" }
    $listeAd = [IO.Path]::ChangeExtension($ad, ".counts.json")
    & $rclone copy "gdrive:DepoWise_Yedekler/$listeAd" $gecici --log-level ERROR 2>$null
    $listeYol = Join-Path $gecici $listeAd
    if ($SayimListesi) { Copy-Item $SayimListesi $listeYol -Force }

    # 2) Yerel test sunucusu + boş deneme veritabanı
    & (Join-Path $PSScriptRoot "pg_test_sunucu.ps1") | Out-Null
    $env:PGPASSWORD = Get-Content (Join-Path $env:LOCALAPPDATA "DepoWise\pgtest_sifre.txt") -Raw
    $env:PGHOST = "localhost"; $env:PGPORT = "54329"; $env:PGUSER = "postgres"
    Remove-Item Env:PGDATABASE, Env:PGSSLMODE -ErrorAction SilentlyContinue
    Psql "DROP DATABASE IF EXISTS $db;" "postgres" | Out-Null
    Psql "CREATE DATABASE $db;" "postgres" | Out-Null
    $hata = Join-Path $gecici "restore_err.txt"
    $p = Start-Process -FilePath "$bin\pg_restore.exe" -NoNewWindow -Wait -PassThru -RedirectStandardError $hata `
        -ArgumentList @("--no-owner", "--no-privileges", "-d", $db, "`"$(Join-Path $gecici $ad)`"")
    if ($p.ExitCode -ne 0) { throw "geri yukleme hatali (kod $($p.ExitCode)): " + ((Get-Content $hata -TotalCount 3) -join " ") }

    # 3) Kontroller
    $sonuc.tablo = [int](Psql "SELECT count(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE';")
    if ($sonuc.tablo -lt 50) { throw "yalniz $($sonuc.tablo) tablo geri geldi" }
    $sonuc.sema = [int](Psql "SELECT MAX(version) FROM schema_migrations;")
    if ([int](Psql "SELECT count(*) FROM companies;") -lt 1) { throw "firma tablosu bos" }
    if ([int](Psql "SELECT count(*) FROM users;") -lt 1) { throw "kullanici tablosu bos" }

    if (Test-Path $listeYol) {
        $liste = Get-Content $listeYol -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([int]$liste.sema -ne $sonuc.sema) { $sonuc.uyumsuz += "sema: liste $($liste.sema) / yedek $($sonuc.sema)" }
        $sorgu = Psql "SELECT string_agg(format('SELECT %L, count(*) FROM public.%I', table_name, table_name), ' UNION ALL ') FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE';"
        $gelen = @{}; foreach ($s in (Psql $sorgu)) { $x = $s -split "\|"; if ($x.Count -eq 2) { $gelen[$x[0]] = [int64]$x[1] } }
        foreach ($t in $liste.tablolar.PSObject.Properties) {
            $beklenen = [int64]$t.Value
            if (-not $gelen.ContainsKey($t.Name)) { $sonuc.uyumsuz += "$($t.Name): yedekte yok"; continue }
            $n = $gelen[$t.Name]
            if ($n -lt $beklenen -or $n -gt $beklenen + 50) { $sonuc.uyumsuz += "$($t.Name): liste $beklenen / yedek $n" }
        }
        if ($sonuc.uyumsuz.Count -gt 0) { throw "sayim uyumsuz: " + ($sonuc.uyumsuz -join "; ") }
        $sonuc.not = "$($liste.tablolar.PSObject.Properties.Name.Count) tablonun satir sayisi yedek anindaki listeyle tutarli"
    }
    else { $sonuc.not = "bu yedegin sayim listesi yok (liste ozelligi 2026-10-10'dan sonraki yedeklerde var); yapi kontrolleri gecti" }

    $sonuc.basarili = $true
    Yaz ("PROVA BASARILI  {0}  sema {1}  {2} tablo  — {3}" -f $ad, $sonuc.sema, $sonuc.tablo, $sonuc.not)
}
catch {
    $sonuc.not = $_.Exception.Message
    Yaz "PROVA HATA  $($sonuc.yedek)  $($_.Exception.Message)"
    Bildir "DepoWise yedek provası BAŞARISIZ" "Yedek geri açılamadı ya da eksik. Ayrıntı: yedek_log.txt"
}
finally {
    try { Psql "DROP DATABASE IF EXISTS $db;" "postgres" | Out-Null } catch { }
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    Remove-Item $gecici -Recurse -Force -ErrorAction SilentlyContinue
    [IO.File]::WriteAllText((Join-Path $yerel "prova_son.json"), ($sonuc | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
}
if (-not $sonuc.basarili) { exit 1 }
