# ═══ ALPNEX — TESTLERİ ÇALIŞTIRMANIN TEK YOLU (2026-09-04) ═══
#
# NEDEN VAR:
#   2026-09-04'te iki test koşusu farkında olmadan aynı anda çalıştı. Birincisi test ikili
#   dosyalarını kilitledi, ikincisinin DERLEMESİ BAŞARISIZ OLDU — ama koşu devam edip
#   ESKİ ikili dosyaları test etti ve "hepsi geçti" raporladı. Yeşil ama anlamsız bir sonuç.
#
#   İki ayrı kusur vardı:
#     1) Aynı anda iki koşu engellenmiyordu.
#     2) `dotnet build ... | tail -n 3 && dotnet test --no-build` kalıbında boru (pipe)
#        yüzünden çıkış kodu `tail`'inki oluyor (hep 0) → `&&` derleme çökse bile devam ediyor.
#        Bu, sessizce eski kodu test etmeye yol açan asıl tehlikeydi.
#
# BU BETİK İKİSİNİ DE KAPATIR:
#   • Sistem genelinde kilit (mutex) alır → ikinci koşu SESSİZCE değil, AÇIKÇA reddedilir.
#   • Derlemeyi ayrı çalıştırır ve çıkış kodunu GERÇEKTEN kontrol eder; derleme çökerse test
#     ÇALIŞTIRILMAZ.
#   • `--no-build` kullanmaz; test edilen ikili dosyanın az önce derlenmiş olduğu garantidir.
#
# KULLANIM:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run_tests.ps1          # tam süit
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run_tests.ps1 -Filter "KUR"
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run_tests.ps1 -Bekle

param(
  [string]$Filter = "",
  [switch]$Bekle,
  [int]$BeklemeDakika = 45
)

$ErrorActionPreference = "Stop"
$proje = Join-Path $PSScriptRoot "..\tests\DepoWise.Tests\DepoWise.Tests.csproj"
$kilitAdi = "Local\AlpnexTestKosusu"

$mutex = New-Object System.Threading.Mutex($false, $kilitAdi)
$alindi = $false
try {
  try {
    $sure = if ($Bekle) { $BeklemeDakika * 60 * 1000 } else { 0 }
    $alindi = $mutex.WaitOne($sure)
  } catch [System.Threading.AbandonedMutexException] {
    # Önceki koşu çökmüş; kilit bize devredildi.
    $alindi = $true
  }

  if (-not $alindi) {
    Write-Output "REDDEDILDI: baska bir test kosusu zaten calisiyor."
    Write-Output "  Ayni anda iki kosu, ikincisinin ESKI ikili dosyalari test etmesine yol acar."
    Write-Output "  Bitmesini beklemek icin: -Bekle"
    exit 2
  }

  # ── 1) DERLEME — cikis kodu GERCEKTEN kontrol edilir ──
  Write-Output "[1/2] Derleniyor..."
  & dotnet build $proje -v q --nologo
  if ($LASTEXITCODE -ne 0) {
    Write-Output ""
    Write-Output "DERLEME BASARISIZ (cikis kodu $LASTEXITCODE). Testler CALISTIRILMADI."
    Write-Output "  Eski ikili dosyalarla yanlis bir 'gecti' sonucu uretmemek icin burada duruldu."
    exit $LASTEXITCODE
  }

  # ── 2) TESTLER — --no-build YOK; derlenen ikili test edilir ──
  Write-Output "[2/2] Testler calisiyor..."
  # -v q ciktisi SADECE "[FAIL] TestAdi" yazar; iddia mesajini (beklenen/gelen) GIZLER. Bir tam kosu
  # 24 dakika surdugu icin "hata neydi" diye tekrar kosturmak pahalidir. TRX kaydi her kosuda
  # tutulur ve basarisizlikta ayrinti otomatik ekrana yazilir -> tek kosuda teshis.
  $trx = Join-Path $PSScriptRoot "..\artifacts\test-sonuc.trx"
  if ($Filter) { & dotnet test $proje --no-build --filter $Filter -v q --nologo --logger "trx;LogFileName=$trx" }
  else         { & dotnet test $proje --no-build -v q --nologo --logger "trx;LogFileName=$trx" }
  $testKodu = $LASTEXITCODE

  Write-Output ""
  if ($testKodu -eq 0) { Write-Output "SONUC: TUM TESTLER GECTI" }
  else {
    Write-Output "SONUC: TEST BASARISIZ (cikis kodu $testKodu)"
    # Basarisiz testlerin IDDIA MESAJINI ekrana yaz — tekrar kosturmaya gerek kalmasin.
    if (Test-Path $trx) {
      try {
        [xml]$x = Get-Content $trx -Encoding UTF8
        $hatalar = $x.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq 'Failed' }
        foreach ($h in $hatalar) {
          Write-Output ""
          Write-Output "--- BASARISIZ: $($h.testName)"
          if ($h.Output.ErrorInfo.Message)    { Write-Output $h.Output.ErrorInfo.Message }
          if ($h.Output.ErrorInfo.StackTrace) { Write-Output ($h.Output.ErrorInfo.StackTrace -split "`n" | Select-Object -First 4) }
        }
      } catch { Write-Output "(TRX okunamadi: $_)" }
    }
  }
  exit $testKodu
}
finally {
  if ($alindi) { $mutex.ReleaseMutex() }
  $mutex.Dispose()
}
