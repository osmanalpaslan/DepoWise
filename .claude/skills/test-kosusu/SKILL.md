---
name: test-kosusu
description: DepoWise testlerini çalıştırırken ve sonucunu yorumlarken kullan — dar/ilgili/tam takım seçimi, PostgreSQL testleri, masaüstü arayüz (headless) testleri, canlı web (Playwright) testi, uzun koşuyu arka planda yürütme, başarısızlıkları nedene göre gruplama ve kota/kesinti sonrası yarım kalan koşuyu kontrollü yeniden başlatma. "test et", "testleri çalıştır", "tam test", "." (kota sonrası devam) durumlarında çağrılır.
---

# Test koşusu (DepoWise)

**Tek yol:** `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run_tests.ps1 [-Filter "..."] [-PostgresAtla]`
(kilit `Local\AlpnexTestKosusu` + gerçek derleme/çıkış kodu kontrolü). Elle `dotnet test` YAZMA. Test tipine Claude
karar verir, raporda tek satır gerekçe yazar.

## 1. Seçim
| Durum | Koşu |
|---|---|
| Tek dosya/servis değişti | `-Filter "FullyQualifiedName~<Sınıf>|FullyQualifiedName~<İlgili>"` (`-PostgresAtla` yalnız SQL'e dokunulmadıysa) |
| Rapor/ekran/filtre/menü | + nöbetçiler: `ReportArchitecture|CustomRapor|ReportFilterParity|StockReportLocation|AppScreens|MenuRenk|IslemOnaylari` |
| Yayın öncesi (YALNIZ kullanıcı "yayınlayalım" deyince) | **Tam takım** (~4.630 test, ~2 sa 50 dk) — arka planda |
| Geniş etki / tenant-yetki-senkron (yayın dışı) | Genişletilmiş riskli alan paketi — tam takım YOK (2026-10-28 toplu yayın kuralı) |
| Masaüstü görünüm değişti | `scripts/run_ui_tests.ps1` (Avalonia Headless + Skia) → `artifacts/ui-ekran/*.png`'yi Read ile GÖZLE kontrol |
| Web arayüzü değişti | Yayından SONRA `cd tests/web-e2e && npx playwright test --workers=1` (canlıda yalnız okur; tek koşu) |

## 2. Tam takım (arka plan)
```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/run_tests.ps1 > "<scratchpad>/tam_testN.log" 2>&1
```
`run_in_background: true` ile başlat; bildirim gelince: `grep -E "\[FAIL\]|SONUC|Başarı" log`. Ara durum için yalnız
`grep -c "\[FAIL\]"` (tüm logu okuma — token). İki koşu aynı anda başlatılamaz (kilit).

## 3. Başarısızlık sınıflandırma (trx: `artifacts/test-sonuc.trx`)
```powershell
[xml]$x=Get-Content artifacts\test-sonuc.trx -Encoding UTF8; $x.TestRun.Results.UnitTestResult | ? outcome -eq 'Failed' | % { $_.testName; ($_.Output.ErrorInfo.Message -split "`n")[0..3] -join ' | ' }
```
| Belirti | Neden | Yapılacak |
|---|---|---|
| "GÜVENLİK: veritabanı 'depowise_test' boyutu NN MB (> 50 MB)" | yerel PG test DB şişti (ortam) | 2026-10-11'den beri `ResetSchema` sonrası VACUUM büyümeyi durdurur; yine olursa `scripts/pg_test_sunucu.ps1` (20 MB+ ise yeniden kurar) + PG testlerini tekrar koş |
| `Assert.Equal(N, …Count)` sayı farkı | bilinçli ekleme (rapor/ekran/filtre) | sayıyı + gerekçe yorumunu güncelle — körlemesine değil |
| `FormatException ''` / yanlış değer, rapor testi | araya sütun eklendi, indeks kaydı | testteki `[n]` indekslerini kaydır |
| Nöbetçi (`65536`, "yalnız X raporunda") | yeni bayrak/rapor bilinçli | listeyi güncelle, nöbetçiyi bir sonraki boş değere taşı |
| Kaynak sözleşmesi (`Assert.Contains("...", razor)`) | metin/ad değişti | testi yeni sözleşmeye güncelle (davranış aynıysa) |
| Gerçek davranış hatası | kod | düzelt; düzeltme sonrası o sınıfı + ilgilileri tekrar koş |
Ortam hatası (PG boyutu, kilit, ağ) "geçti" sayılmaz → ortamı düzelt, o testleri YENİDEN koş.

## 4. Kesinti / kota sonrası ("." mesajı) — kontrollü yeniden koşu
1. Askıda süreç: `Get-Process | ? ProcessName -match 'testhost|vstest'` → varsa yalnız bunları sonlandır.
2. Kilit boş mu: `[Threading.Mutex]::new($false,'Local\AlpnexTestKosusu').WaitOne(0)` (true ise hemen ReleaseMutex).
3. Son log "SONUC: TUM TESTLER GECTI" ile bitmediyse koşu GEÇMEMİŞTİR: kırılanları düzelt → dar koşu → yarım kalan
   koşuyu (tam ise tam) BAŞTAN başlat. `docs/project-control/TALEP_*.md` faz tablosuna durumu yaz.
4. Yayın yalnız eksiksiz biten + geçen koşudan sonra (`yayin` skill'i).

## Test DB'sine elle bakmak (boyut vb.)
PowerShell aracıyla çalıştır (Bash içinden kaçışlı PowerShell `$` değişkenlerini bozar → psql parola bekleyip ASILI kalır):
`$kok="$env:LOCALAPPDATA\DepoWise"`, `scripts/pg_test_sunucu.ps1`'deki `$sifreDosya` satırını çalıştır, `$env:PGPASSWORD`
ata, `pgsql17\pgsql\bin\psql.exe -h localhost -p 54329 -U postgres -Atc "..."`, sonra `Remove-Item Env:PGPASSWORD`.
Askıda `psql` kalırsa `Stop-Process`.

## 5. Rapor
Kullanıcıya: kaç test, kaç geçti, hangi bulgular nasıl düzeldi (sade dil), tekrar koşu sonucu. Bitince `scripts/temizlik.ps1`.
