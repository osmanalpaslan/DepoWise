# ⭐ 2026-10-10 — YEREL PostgreSQL TEST SUNUCUSU (yalnız bu bilgisayar: localhost:54329)
#
# Neden: PostgreSQL testleri eskiden Neon'daki bir deneme veritabanını kullanıyordu. Neon'un bağlantı bilgisi
# herkese açık depo geçmişinde sızmıştı → Neon kaldırıldı, testler bu YEREL sunucuya taşındı (ağ yok, ücret yok).
# Yönetici izni GEREKMEZ: taşınabilir PostgreSQL 17 %LOCALAPPDATA%\DepoWise\pgsql17 altında, veri pgtest altında.
#
# Ne yapar: veri klasörü yoksa kurar (rastgele şifre; şifre yalnız %LOCALAPPDATA%\DepoWise\pgtest_sifre.txt ve
# .env.pgtest.local/.env.test.local'da durur), sunucu kapalıysa başlatır. run_tests.ps1 her koşuda bunu çağırır. Tekrar çalıştırmak zararsız.
$ErrorActionPreference = "Stop"
$kok   = Join-Path $env:LOCALAPPDATA "DepoWise"
$bin   = Join-Path $kok "pgsql17\pgsql\bin"
$data  = Join-Path $kok "pgtest"
$sifreDosya = Join-Path $kok "pgtest_sifre.txt"
$port  = 54329
$envDosyalari = @(".env.pgtest.local", ".env.test.local") | ForEach-Object { Join-Path (Split-Path $PSScriptRoot -Parent) $_ }

if (-not (Test-Path (Join-Path $bin "postgres.exe"))) {
    Write-Output "[pg] Tasinabilir PostgreSQL yok ($bin) - PG testleri atlanir."
    exit 0
}

if (-not (Test-Path (Join-Path $data "PG_VERSION"))) {
    $sifre = -join ((48..57 + 65..90 + 97..122) | Get-Random -Count 24 | ForEach-Object { [char]$_ })
    Set-Content -Path $sifreDosya -Value $sifre -NoNewline -Encoding ascii
    $p = Start-Process -FilePath (Join-Path $bin "initdb.exe") -NoNewWindow -Wait -PassThru `
        -ArgumentList @("-D", "`"$data`"", "-U", "postgres", "--pwfile=`"$sifreDosya`"", "-A", "scram-sha-256", "-E", "UTF8", "--locale=C") `
        -RedirectStandardOutput (Join-Path $kok "pgtest_initdb.log") -RedirectStandardError (Join-Path $kok "pgtest_initdb.err")
    if ($p.ExitCode -ne 0) { throw "initdb basarisiz (kod $($p.ExitCode)) - bkz. $kok\pgtest_initdb.err" }
    Add-Content (Join-Path $data "postgresql.conf") "`nport = $port`nlisten_addresses = 'localhost'`nmax_connections = 100`n"
    Write-Output "[pg] Yerel test sunucusu kuruldu."
}

# Calisiyor mu? (pg_ctl status: 0 = calisiyor)
$durum = Start-Process -FilePath (Join-Path $bin "pg_ctl.exe") -ArgumentList @("-D", "`"$data`"", "status") -NoNewWindow -Wait -PassThru `
    -RedirectStandardOutput (Join-Path $kok "pgtest_status.log")
if ($durum.ExitCode -ne 0) {
    # Borusuz baslat (cikti boruya baglanirsa pg_ctl sunucu kapanana kadar bekler).
    Start-Process -FilePath (Join-Path $bin "pg_ctl.exe") -WindowStyle Hidden `
        -ArgumentList @("-D", "`"$data`"", "-l", "`"$data\server.log`"", "start") | Out-Null
    $hazir = $false
    for ($i = 0; $i -lt 30 -and -not $hazir; $i++) {
        Start-Sleep -Milliseconds 500
        $c = Start-Process -FilePath (Join-Path $bin "pg_isready.exe") -ArgumentList @("-h", "localhost", "-p", "$port") -NoNewWindow -Wait -PassThru `
            -RedirectStandardOutput (Join-Path $kok "pgtest_ready.log")
        $hazir = ($c.ExitCode -eq 0)
    }
    if (-not $hazir) { throw "yerel PG sunucusu baslamadi - bkz. $data\server.log" }
    Write-Output "[pg] Yerel test sunucusu baslatildi (localhost:$port)."
}

# Test veritabani + .env.test.local
$sifre = Get-Content $sifreDosya -Raw
$env:PGPASSWORD = $sifre
$var = & (Join-Path $bin "psql.exe") -h localhost -p $port -U postgres -Atc "SELECT 1 FROM pg_database WHERE datname='depowise_test';"
if ($var -eq "1") {
    # 2026-10-10: her test şemayı silip yeniden kurduğu için veritabanı zamanla şişer; 40 MB'ı geçince
    # PostgresTestGuard'ın 50 MB güvenlik sınırına takılmadan YENİDEN oluşturulur (yalnız bu bilgisayardaki test DB'si).
    $mb = & (Join-Path $bin "psql.exe") -h localhost -p $port -U postgres -Atc "SELECT pg_database_size('depowise_test')/1048576;"
    if ([int]$mb -gt 40) {
        & (Join-Path $bin "psql.exe") -h localhost -p $port -U postgres -c "DROP DATABASE depowise_test WITH (FORCE);" | Out-Null
        $var = ""
        Write-Output "[pg] Test veritabani $mb MB'a sismisti; yeniden olusturuluyor."
    }
}
if ($var -ne "1") { & (Join-Path $bin "psql.exe") -h localhost -p $port -U postgres -c "CREATE DATABASE depowise_test;" | Out-Null }
Remove-Item Env:PGPASSWORD

$hedef = "DEPOWISE_PG_URL=Host=localhost;Port=$port;Database=depowise_test;Username=postgres;Password=$sifre;Maximum Pool Size=20"
foreach ($envDosya in $envDosyalari) { if (-not (Test-Path $envDosya)) { Set-Content -Path $envDosya -Value "# Yerel PG test sunucusu (scripts/pg_test_sunucu.ps1)" -Encoding utf8 }
    $satirlar = Get-Content $envDosya -Encoding UTF8   # 2026-10-10: kodlama verilmezse PS 5.1 ANSI okur → Türkçe değerler (ör. "Oze İnşaat") bozuluyordu
    if (-not ($satirlar -contains $hedef)) {
        $yeni = $satirlar | Where-Object { $_ -notlike "DEPOWISE_PG_URL=*" }
        $yeni += $hedef
        Set-Content -Path $envDosya -Value $yeni -Encoding utf8
        Write-Output "[pg] $(Split-Path $envDosya -Leaf) yerel test sunucusuna yonlendirildi."
    }
}
