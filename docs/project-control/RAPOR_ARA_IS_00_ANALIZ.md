# ARA İŞ — RAPOR GELİŞTİRMELERİ · 00 ANALİZ (2026-08-29, KOD YOK)

> Kapsam: (1) Araç raporlarında GÜN BAZLI kırılım · (2) Rapor türlerine AYRI YETKİ.
> Bu belge yalnız analizdir — **hiçbir kod yazılmadı, production'a bağlanılmadı**.
> Kararlar (PK-R1..R4) kullanıcıdan bekleniyor; onay gelmeden uygulamaya geçilmez.
> Bu ara iş tamamlanınca YAYINLANACAK; ardından ana plan **AŞAMA 3 — FINAL KARAR PAKETİ**'ne dönülecek
> (7 madde zaten UYGULANDI — ADR-179; bekleyen tek şey yayın penceresi).

---

## A. MEVCUT DURUM TESPİTİ (koddan, varsayımsız)

### A1. Araç Raporu bugün nasıl çalışıyor?
- `ReportService.VehicleReport` (`src/DepoWise.Infrastructure/Reporting/ReportService.cs:555-657`).
- **Tek sorgu**, araç başına TEK satır: `vehicles` + 3 türetilmiş tablo (yakıt `fuel_distributions`,
  bakım malzemesi `vehicle_maintenances`+`maintenance_materials`, doğrudan parça
  `stock_documents`+`stock_movements`) — hepsi `GROUP BY vehicle_id`. **Gün kırılımı HİÇBİR raporda yok**
  (`GROUP BY` gün deseni tüm `src/` altında 0 eşleşme; `daily_activities` tablosu araç raporunda kullanılmıyor).
- 14 kolon: İç Kod · Plaka · Araç Adı · Şube · Sayaç Birimi · Dönem Sayaç Mesafesi · Toplam Yakıt (L) ·
  Ort. Yakıt Fiyatı · Yakıt Maliyeti · Ort. Yakıt Tüketimi · Bakım Malzeme · Doğrudan Parça ·
  Toplam Maliyet · Birim Başına Maliyet.
- "Dönem Sayaç Mesafesi" = yakıt fişlerindeki `current_meter - prev_meter` farklarının toplamı
  (dönem başı/sonu farkı DEĞİL) → her fark bir fişe, dolayısıyla bir GÜNE aittir → **günlük toplanabilir
  ve günlük toplamlar dönem toplamına birebir eşittir.**
- Maliyeti olmayan araç 0'larla yine listelenir (test kilidi: `VehicleReportTests` "HicMaliyetiOlmayanArac...").
- TotalRow: yalnız litre+para toplamları; mesafe/tüketim/birim-maliyet BOŞ (km↔saat karışımı toplanmaz).

### A2. Tarih aralığı nasıl uygulanıyor?
- Tüm tarihler **BIGINT unix milisaniye (UTC)**. Rapor filtresi `DateFilter` (ReportService.cs:1179):
  `col >= @from AND col <= @to` — **iki uç DAHİL**. Lehçe farkı YOK (düz sayı karşılaştırması).
- İstemci sınırları `ReportDateRange.ToMs` (RPR-06): başlangıç = günün 00:00.000 **UTC**, bitiş =
  23:59:59.999 **UTC**; web aynası `FieldChecks.cs` birebir aynı, parite testli (RPR06h).
- Tarih araçlara değil yalnız 3 maliyet alt-sorgusuna uygulanır (araç her durumda listelenir).
- ⚠️ Bilinen tutarsızlık (mevcut, bu işte DEĞİŞTİRİLMEYECEK): sunucu varsayılan aralığı
  `CurrentMonthRange()` YEREL offset kullanır; UI her zaman açık tarih gönderdiği için pratikte etkisiz.

### A3. Rapor türü envanteri (21 tür — tek ortak katalog)
`src/DepoWise.Application/Reports/ReportCatalog.cs` — masaüstü doğrudan derler, web `/api/reports/catalog`
ile aynı listeyi alır. **İki platformda ayrı liste YOKTUR.** Kategoriler (mevcut `ReportCategory` alanı —
bugün yalnız görsel grup başlığı, yetki anlamı YOK):

| Kategori | Raporlar (anahtar) |
|---|---|
| Araç (Vehicle) | vehicle · inspection · vehicles-template(Y) · vehicles-nontemplate(Y) |
| Stok (Stock) | stock · stock-movements · stock-count |
| Yakıt (Fuel) | fuel · fuel-depot |
| Bakım (Maintenance) | maintenance |
| Talep (Requests) | requests |
| Yönetim (Management) | personnel · status(Y) |
| Malzeme (Material) | materials-template(Y) · materials-nontemplate(Y) |
| Ön Muhasebe (Accounting) | acc-statement · acc-balances · acc-invoices · acc-open-invoices · acc-payments · acc-cash |

(Y) = Manager grubu — zaten yalnız admin görür/çalıştırır (RPR-07 kapısı).

### A4. Mevcut rapor yetki modeli
- Tek modül anahtarı: **`("reports","Raporlar")`** (`AppModules.cs:77`); her rapor metodu başında
  `AccessControl.Require(s,"reports",View)`. Yetki ağacında raporlar TEK satırdır.
- Kısmi tür-bazlı kapı ZATEN VAR: `ReportDescriptor.RequiredModule` (6 raporda: inspection→inspection,
  personnel→personnel, acc-*→parties/invoices/finance) + `DataModule` (role kapatılan ekranın raporu
  gizlenir/engellenir, RPR-15). Katalog süzmesi hem API (`Program.cs:2975`) hem masaüstü
  (`ReportsViewModel.cs:208`) aynı kuralla.
- Export: `btn-export-reports` / `btn-export-mgr-reports` özel butonları (değişmeyecek).
- **Yeni modül anahtarı eklemek MIGRATION GEREKTİRMEZ**: `user_permissions.module_key` serbest TEXT,
  katalog tamamen kod-tabanlı, bilinmeyen anahtar deny-by-default (satır yoksa erişim yok, ağaçta
  işaretsiz görünür). Emsal: Migration056 yalnız VERİ backfill'i içindi (export ayrışması).
- Tavan (ceiling), rol-blok, firma paketi, yetki şablonları — hepsi `AppModules.All`'dan otomatik beslenir;
  yeni anahtarlar kendiliğinden akar.

### A5. Çift kapı bugünkü hâli
UI kapısı (katalog süzmesi iki platformda) + servis kapısı (`ReportService.Run`: reports + manager +
DataModule; RequiredModule'lü raporlarda metot içi ek `Require`) + API export kapısı. Tenant
(`TenantAccessGuard`/`company_id`) ve `BranchAccess`/`ReportSession` (şube yalnız DARALIR) sağlam ve
bu işte DOKUNULMAYACAK.

---

## B. TASARIM — ÖZELLİK 1: GÜN BAZLI ARAÇ RAPORU

### B1. Yaklaşım (önerilen: yeni katalog satırı)
- **Yeni rapor türü `vehicle-daily` — "Araç Raporu — Günlük"** aynı Raporlar ekranının mevcut sol
  listesine eklenir (YENİ EKRAN/MENÜ YOK — 21 raporun tamamı zaten bu listede yaşıyor; iki platform,
  filtreler, tarih varsayılanı, Excel export ve yetki kapıları katalogdan OTOMATİK gelir; iki düz grid
  hiçbir değişiklik istemez).
- Mevcut `vehicle` raporuna **tek satır dokunulmaz** → toplam rapor davranışı yapısal olarak korunur.
- Filtreler mevcut araç raporuyla aynı: Tarih (zorunlu, ay varsayılanı) · Şube · Araç · Araç Türü.

### B2. Satır şekli
`Tarih (GG.AA.YYYY) · İç Kod · Plaka · Araç Adı · Şube · Sayaç Birimi` + aynı 9 sayısal metrik,
o günün değerleriyle. Ek öneri: **"Gün İçi Son Sayaç"** kolonu (o günkü son yakıt fişindeki
`current_meter`; fiş yoksa boş) — "günlük tüketim" ile "gün sonu sayaç" ayrımını netleştirir ve hatalı
sayaç girişini görünür kılar. Sıralama: Tarih → Şube → Araç. TotalRow = DÖNEM toplamları (mevcut
`vehicle` raporunun toplamlarıyla birebir tutarlılık TESTLE kilitlenir); oran kolonları mevcut kuralla boş.

### B3. Gün gruplama — lehçe-bağımsız
Gün anahtarı = `tarih_kolonu / 86400000` (tam sayı bölmesi). BIGINT unix ms iki lehçede de aynı sonucu
verir (`strftime`/`to_char` GEREKMEZ, SqlDialect'e ekleme GEREKMEZ), ve RPR-06'nın UTC gün sınırıyla
BİREBİR örtüşür (StartMs tam gün başına denk gelir). 3 alt-sorguya `GROUP BY vehicle_id, gün` eklenir;
birleştirme + boş gün doldurma bellekte yapılır. **Tek veri çekimi, gün başına sorgu YOK.**

### B4. Boş günler
Kullanıcı beklentisi gereği aralıktaki TÜM günler gösterilir (veri yoksa 0 satırı) — "veri girilmemiş gün"
açıkça görünür. Satır sayısı matematiği: gün × araç. Örn. 31 gün × 160 araç ≈ 4.960 satır (ay varsayılanı);
1 yıl × 160 araç ≈ 58.400 satır → web ekranı ilk 1.000 satırı çizer ve MEVCUT kesme uyarısını gösterir
(DwDataGrid davranışı), Excel export `reports.max_rows` (50.000) sınırına tabidir. Bu bir mevcut-mimari
sınırıdır; yeni mekanizma eklenmeyecek, rapor açıklamasına "uzun aralıkta araç filtresi önerilir" notu konur.

### B5. Günlük toplanabilirlik değerlendirmesi
- Doğrudan toplanabilir: litre, yakıt maliyeti, bakım malzeme, doğrudan parça, toplam maliyet,
  sayaç mesafesi (fiş-farkı tanımı sayesinde). Günlük toplamlar = dönem toplamı, birebir.
- Oran kolonları (ort. fiyat, ort. tüketim, birim maliyet): o GÜNÜN değerlerinden aynı formülle hesaplanır
  (toplama YAPILMAZ — mevcut iş anlamı korunur); veri yoksa boş ("-").
- Anomali motoru/alarm/eşik/otomatik düzeltme YOK — yalnız görünürlük.

---

## C. TASARIM — ÖZELLİK 2: RAPOR TÜRÜ YETKİLERİ

### C1. Önerilen granülerlik: KATEGORİ bazlı 8 yeni modül anahtarı
Kullanıcının örnekleri ("Araç Raporları / Yakıt Raporları / Stok Raporları") kategori düzeyine birebir
oturuyor; 21 ayrı anahtar düz yetki ağacını şişirir. Öneri — `AppModules.All`'a `reports`'un hemen
altına 8 anahtar:

`report_vehicle "Rapor: Araç"` · `report_stock "Rapor: Stok"` · `report_fuel "Rapor: Yakıt"` ·
`report_maintenance "Rapor: Bakım"` · `report_requests "Rapor: Talep"` · `report_management "Rapor: Yönetim"` ·
`report_material "Rapor: Malzeme"` · `report_accounting "Rapor: Ön Muhasebe"`

Eşleme kod içinde tek yerde: `ReportCatalog.CategoryModule(ReportCategory)` — descriptor'a alan bile
gerekmez; `vehicle-daily` otomatik `report_vehicle` altına düşer.

### C2. Çift kapı (dört katman)
1) Katalog süzmesi API (`/api/reports/catalog`) + masaüstü (`ReportsViewModel`) → yetkisiz tür listede
   GÖRÜNMEZ (menü zaten `reports` ile kapılı, değişmez).
2) `ReportService.Run` içinde merkezî kapı: `Require(s, CategoryModule(desc.Category), View)` → masaüstü
   servis çağrısı da aynı kapıdan geçer (paylaşılan servis).
3) API `/api/reports/{type}` ve `/export` aynı `Run`/`BuildReport` yolunu kullandığı için otomatik kapsanır;
   tür adı değiştirerek atlama imkânsız (kapı tür→kategori eşlemesinden türetilir).
4) Mevcut kapılar AYNEN kalır: `reports` üst kapısı, Manager/IsAdmin, RequiredModule (inspection/personnel/
   acc-*), DataModule/rol-blok, tenant, BranchAccess/ReportSession, export butonları. Yeni kapı hiçbirini
   bypass ETMEZ, yalnız EKLENİR. Admin bypass'ı mevcut kurala göre işler (firma admini tüm kategorileri görür).

### C3. `reports` üst yetkisi — ÖNERİ: (A) üst kapı olarak KALIR
`reports` = Raporlar ekranına giriş (menü + ortak altyapı + rapor filtre uçları `ListForReportFilter`);
kategori anahtarı = ikinci kapı. Kaldırma (B) seçeneği menü/ekran kapısını, RPR ailesi testlerini ve tavan/
rol-blok akışlarını gereksiz yere kırar — ÖNERİLMEZ.

### C4. Geriye uyumluluk — KARAR NOKTASI (aşağıda PK-R3)
Deny-by-default gereği yeni anahtarlar HERKESTE işaretsiz başlar → yayın anında, admin OLMAYAN mevcut
`reports` yetkili kullanıcılar kategori tanımlanana dek rapor listesini boş görür. Seçenekler:
- **(a) Migration YOK — yayın sonrası elle tanımlama ⭐:** Yetkiler ekranından ilgili kullanıcılara
  kategoriler açılır (mevcut ölçek: babanın firması; admin kullanıcılar zaten etkilenmez; 2026-08-28
  toplu yayınında aynı yöntem kullanıldı — "yeni yetkiler rollere elle açıldı" emsali). Kısa, kontrollü,
  migration'sız.
- **(b) Migration083 veri-backfill:** `reports.can_view=1` olan her kullanıcıya 8 kategori satırı
  INSERT (idempotent, Migration056 emsali). Otomatik ama ⚠️ **runner sıralı çalıştığı için yayında
  Migration082'nin de çalışmasını zorunlu kılar** (082 kataloğa kayıtlı, prod şema 81).
- (c) 082↔083 numara takası: FINAL teslimatlarına (test/belge) dokunur — ÖNERİLMEZ.

### C5. Yetki ağacı görünümü
`PermMatrix.razor` (web) ve `PermissionsViewModel` (masaüstü) katalogdan OTOMATİK beslenir — 8 satır
"Rapor: X" adıyla, `reports`'un hemen altında görünür (kod değişikliği: yalnız katalog sırası).
Yalnız View kutusu anlamlıdır (raporlar salt-okunur; Create/Edit/Delete `reports`'ta da bugün ölüdür —
mevcut düzen korunur, matris yapısı DEĞİŞTİRİLMEZ). Tavan/şablon/rol-blok/firma paketi otomatik.

---

## D. YAYIN GERÇEĞİ — ⚠️ MASTER'DAKİ BEKLEYEN HAVUZ (KARAR NOKTASI PK-R4)
Master'da yayınlanmamış işler var: **M (Excel Merkezi) + O (Barkod/QR) + FIN düzeltmeleri + Migration082**.
Yayın master'dan yapılır (yeni repo/branch cerrahisi yok — kural) → **bu ara işin yayını havuzdaki her
şeyi birlikte canlıya taşır ve API açılışında Migration082 production'da ÇALIŞIR** (runner bekleyenleri
sırayla uygular; 082 kataloğa kayıtlı). FIN-B1 kod+082 çifti bilinçli olarak ayrılamaz ("yarım sözleşme
bırakma" kuralı). Dolayısıyla yayın onayı = 082'nin de kontrollü onayı olmalı (önkoşullar hazır:
pg_dump yedeği + kısa ACCESS EXCLUSIVE indeks kilidi; iki lehçede bit-bit kanıt testleri mevcut).

## E. MIGRATION CEVABI
- Özellik 1 (günlük rapor): **MIGRATION YOK** (salt-okunur sorgu genişletmesi).
- Özellik 2 (yetkiler): **ŞEMA migration'ı YOK** hiçbir seçenekte; yalnız PK-R3=(b) seçilirse
  Migration083 VERİ backfill'i gerekir (yukarıda bildirildi; onaysız yazılmayacak).

## F. ETKİLENECEK DOSYALAR (uygulama onaylanırsa)
Kod: `ReportCatalog.cs` (+1 tür, +CategoryModule) · `ReportService.cs` (+VehicleDaily metodu, +dispatch
satırı, +Run'da kategori kapısı) · `AppModules.cs` (+8 anahtar) · `Program.cs` (/api/reports/catalog
süzmesine kategori) · `ReportsViewModel.cs` (katalog süzmesine kategori). UI dosyalarına dokunma YOK
(Reports.razor/axaml katalogdan beslenir). SqlDialect'e dokunma YOK.
Test: yeni `VehicleDailyReportTests` + yeni `ReportTypePermissionTests` · güncelleme:
`ReportArchitectureTests` (21→22 sayacı) · `ScreenTreeParityTests.A10 menusuz` (+8 anahtar) ·
`RaporKapaliModulBypassTests.RPR15d` (sözleşme bilinçli değişiyor: "yalnız reports yeter" →
"reports + kategori"; yeni sözleşme testle kilitlenir — gevşetme değil, yeni kuralın kanıtı) ·
`NewReportsTests` RPR12 ailesi (kategori kapısı tutarlılığı) · `RaporKapsamliTaramaTests` (otomatik kapsar).

## G. RİSK DEĞERLENDİRMESİ
- Günlük rapor: **DÜŞÜK** — tamamen eklemeli, mevcut rapora dokunmuyor, salt-okunur, migration yok.
- Rapor yetkileri: **ORTA** — bilinçli sözleşme değişikliği (RPR15d) + yayın anı geriye-uyum penceresi
  (PK-R3) + canlıda kullanıcı görünür etkisi. Tenant/BranchAccess/senkron DOKUNULMUYOR.
- Yayın: **ORTA-YÜKSEK dikkat** — havuz birlikte çıkar, Migration082 çalışır (PK-R4).
- Satır patlaması: uzun aralık × büyük filo → mevcut kesme mekanizmaları devrede (B4).

## H. TEST PLANI (kullanıcının 25 maddesi birebir karşılanır)
Günlük: tek/çok gün, uç günler dahil, boş günler, aynı günde çok kayıt, çok araç/çok gün,
**günlük≡toplam tutarlılığı (mevcut `vehicle` sonucuyla karşılaştırmalı)**, gece yarısı sınırı (RPR-06
uçları), NULL sayaç, tenant, BranchAccess, yetkisiz, PG paritesi (izole yerel PG'de aynı senaryo).
Yetki: yetkili görür/yetkisiz UI-endpoint-servis üç katmanda alamaz, kategori çapraz sızmaz, tenant/
BranchAccess korunur, eski kombinasyonlar (RequiredModule/DataModule/manager) bozulmaz, ağaç görünümü,
platform paritesi. Regresyon: mevcut rapor test aileleri (VehicleReport/ReportBranchScope/ApiReportScope/
ReportFilterParity/RaporKapsamliTarama/Accounting…) + Excel + arama + dashboard → tam süit + izole PG süiti.

## I. YAYIN ÖNCESİ DOĞRULAMALAR (uygulama bitince, onay öncesi rapor edilecek)
3 Release build (API/Web/Masaüstü) · hedefli testler + TAM süit + izole PG süiti · git diff/temiz ağaç ·
production bağlantısı OLMADIĞININ teyidi · Migration082 (+ varsa 083) izole kanıtlarının özeti ·
yayın adımları ve geri dönüş planı (pg_dump; `flyctl` sürüm dönüşü) · yayın sonrası salt-okunur kontrol
listesi (health/API/web/rapor uçları/yetki/günlük+toplam araç raporu/tenant-BranchAccess).

---

## KARARLAR (PK-R) — KULLANICI BEKLENIYOR

- **PK-R1 — Günlük görünümün biçimi:** (a)⭐ Yeni katalog satırı "Araç Raporu — Günlük" (aynı ekran içi
  liste; sıfır UI kodu; iki platform otomatik) · (b) Mevcut Araç Raporu'na "Günlük" anahtarı/toggle
  (iki platformda UI kodu + istek alanı; daha çok dokunuş, aynı sonuç).
- **PK-R2 — Yetki granülerliği:** (a)⭐ Kategori bazlı 8 anahtar (örnekleriniz bu düzeye oturuyor) ·
  (b) Rapor türü bazlı 21 anahtar (çok uzun düz liste).
- **PK-R3 — Geriye uyumluluk:** (a)⭐ Migration YOK — yayın sonrası kategorileri Yetkiler ekranından elle
  tanımlarız (admin olmayan rapor kullanıcıları için kısa pencere; 2026-08-28 emsali) · (b) Migration083
  backfill (otomatik ama 082'yi de yayına zorlar) · (c) numara takası (önerilmez).
- **PK-R4 — Yayın kapsamı onayı:** Bu ara işin yayını master'daki havuzu (M+O+FIN+**Migration082**) da
  canlıya taşır ve 082 production'da çalışır. (a)⭐ Evet — yayın penceresinde 082 dahil onaylıyorum
  (pg_dump + kısa kilit önkoşullarıyla) · (b) Hayır — yayın stratejisini ayrıca konuşalım (bu durumda
  ara iş yayını bloklanır, çünkü branch cerrahisi kural dışı).

> Not: RPR15d test sözleşmesinin "reports + kategori" olarak güncellenmesi Özellik 2'nin doğal sonucudur;
> PK-R2 onayı bunu da kapsar. Boş günlerin 0 satırıyla gösterimi talimat gereği sabittir (karar değil).
