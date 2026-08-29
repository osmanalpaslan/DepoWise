# ARA İŞ 2 — PAKET-1 UYGULAMA KAYDI (2026-08-29, ADR-182) — 🔨 SÜRÜYOR

> Plan: [ARA_IS_2_01_PLAN.md](ARA_IS_2_01_PLAN.md) · Analiz: [ARA_IS_2_00_ANALIZ.md](ARA_IS_2_00_ANALIZ.md)
> Kararlar: PK-F1=A·F2·F3·F4=A·F5=A · T1=A·T2·T3=A·T4=A · V1=A · G1=A·G2=A · D1=A.
> **Production'a HİÇBİR aşamada bağlanılmadı · MIGRATION YOK (katalog azamisi 81 = canlı şema).**

| Adım | Konu | Durum |
|---|---|---|
| S1 | Yakıt: tarih yazım hatası + rapor kapsam sözleşmesi | ✅ **TAMAM** |
| S2 | "Yakıtı Veren" son seçim | ✅ **TAMAM** |
| S3 | Yakıt-Günlük + Stok Hareketleri-Günlük | ✅ **TAMAM** |
| S4 | Günlük Faaliyet — Detay | ✅ **TAMAM** |
| S5 | Fotoğraf sunucu-otoriteli + silme kapısı | ⏳ |
| S6 | Kapanış doğrulaması + yayın öncesi rapor | ⏳ |

---

## S1 — YAKIT (✅ tamamlandı)

### S1a · Masaüstü tarih yazım hatası (PK-T2)
**Kök neden:** `FuelViewModel` seçilen günü HAM `DateTimeOffset.ToUnixTimeMilliseconds()` ile
gönderiyordu. Avalonia DatePicker günü YEREL ofsetle verir (TR +03:00) → kullanıcının seçtiği
**2 Ağustos, veritabanına 1 Ağustos 21:00 UTC** yazılıyordu; fiş tarih-filtreli tüm raporlarda
(fuel · vehicle · vehicle-daily · fuel-depot) **bir gün erken** görünüyordu. Web (`Fuel.razor`) bu
hatayı taşımıyordu (doğrulandı — web'e DOKUNULMADI).

**Düzeltme:** yeni `FuelViewModel.IsGunuMs(DateTimeOffset?)` — seçilen günün **UTC 00:00**'ı.
Kural, rapor tarih sınırı (`ReportDateRange`) ve web (`FieldChecks.ToUnixMs`) ile birebir aynıdır;
masaüstünün Duyuru/Zimmet/Takvim/Evrak/Proje/Satın Alma/İş Emri ekranlarının zaten kullandığı desen.
İki çağrı noktası: dağıtım `DistributionDate`, depo girişi `EntryDate`.
**PK-T3=A:** mevcut canlı kayıtlara DOKUNULMADI (eski fişler bir gün erken görünmeye devam eder —
bilinçli kabul; istenirse ileride ayrı onaylı düzeltme işi).

### S1b · Rapor kapsam sözleşmesi (PK-T1=A)
`FuelConsumption` içindeki türetilmiş yakıt tablosuna **INNER JOIN** (eskiden LEFT) → **yalnız seçilen
aralıkta yakıt fişi olan araçlar** listelenir. InfoNote kullanıcıya yeni davranışı ve "tüm filoyu görmek
için Araç Raporu / Araç Raporu — Günlük" yönlendirmesini söyler. **Yalnız bu rapor değişti.**
Sözleşme değişikliği testte açıkça belgelendi: eski kilit `YakitAlmayanArac_TamFilo_...` →
yeni kilit `YakitAlmayanArac_ARTIK_Listelenmez` (gevşetme DEĞİL — yeni kuralın kanıtı).

### S1d · Diğer masaüstü tarih alanları — SALT TARAMA (PK-T4=A, düzeltme YAPILMADI)
Aynı hata sınıfı (yerel ofsetli `DateTimeOffset` → ham ms) **10 ekranda / 17 yazım noktasında** daha var:

| Şiddet | Ekran (dosya:satır) | Alan |
|---|---|---|
| 🔴 **Her seferinde 1 gün kayar** (`new DateTimeOffset(DateTime.Today)`) | StockEntryViewModel:422,457,470 · StockCountViewModel:235 · StockDistributeViewModel:171 | stok belge tarihi (`docDate`) |
| 🟠 **Kullanıcı gün seçince kayar** (picker yerel gece yarısı verir) | MaintenanceViewModel:625 · InspectionViewModel:144,147 · InvoicesViewModel:344,345 · PartiesViewModel:356,358 · PaymentsViewModel:305 · FinanceViewModel:328,383 · DailyActivityViewModel:530,560,583 · RequestsViewModel:352 | bakım/muayene/fatura/cari/ödeme/finans/faaliyet/talep iş günü |

**DOĞRU desen zaten kullanan ekranlar** (referans): Duyurular · Zimmet · Takvim · Evrak · Proje ·
Satın Alma · İş Emri · Maliyet Merkezi (+ okuma filtreleri: Sistem Logu · Stok Değişiklik · Stok Hareketleri).
➡️ **Karar kullanıcıya bırakıldı** (kapsam dışı; bu pakette düzeltilmedi). Düzeltilirse davranış
değişikliği olacağından ayrı iş + ayrı test turu gerektirir.

### Testler (S1)
- **YENİ** `YakitTarihGunTests` **11 test**: UTC gün başı kuralı 4 saat diliminde (rapor filtresiyle
  parite) · eski ham dönüşümün bir gün erkene düştüğünün belgesi · **kaynak-düzeyi kilit** (FuelViewModel
  ham dönüşüme geri dönemez) · uçtan uca yazım→rapor (2 Ağustos'ta VAR, 1 Ağustos'ta YOK) ·
  **1 Ağustos ≠ 2 Ağustos araç listesi** (kullanıcının senaryosu) · gün sınırı iki uç dahil
  (00:00:00.000 + 23:59:59.999) · aralık dışı fişi olan araç listelenmez · hiç fişi olmayan listelenmez ·
  **REGRESYON: `vehicle` tam filo KORUNDU · `vehicle-daily` tam filo × tüm günler KORUNDU**.
- **GÜNCELLENEN** `FuelConsumptionTests`: satır sayısı 4→3 + tam-filo kilidi yeni sözleşmeye çevrildi.
- Koşu: hedefli aile **84/85** (1 atlanan = PG sınıfı) → geniş regresyon (Report/Rapor/Excel/Vehicle/
  Fuel/Yakit/BranchIsolation) **679 geçti / 0 başarısız / 2 atlanan**.

### Değişen dosyalar (S1)
`src/DepoWise.Desktop/ViewModels/FuelViewModel.cs` · `src/DepoWise.Infrastructure/Reporting/ReportService.cs` ·
`src/DepoWise.Application/Reports/ReportCatalog.cs` · `tests/DepoWise.Tests/FuelConsumptionTests.cs` ·
**yeni** `tests/DepoWise.Tests/YakitTarihGunTests.cs`. **Web'e dokunulmadı** (hata web'de yoktu).

---

## S2 — "YAKITI VEREN" SON SEÇİMİ (✅ tamamlandı · PK-V1=A)

**Davranış:** Yakıt Dağıtımı formunda en son seçilen "Yakıtı Veren" kişi, sonraki kayıtta otomatik
ön-seçili gelir; kullanıcı değiştirirse yeni seçim son seçim olur. **"Yakıtı Alan" KAPSAM DIŞI** —
her işlemde boş/değişken kalır (kullanıcı kuralı, testle kilitli).

**Mimari (MIGRATION YOK):** değer mevcut `user_list_preferences` tablosunda, **ayrılmış bir anahtar**
altında tek elemanlı liste olarak saklanır. Anahtar iki platformda **paylaşımlı** dosyadan gelir
(`UserPrefKeys.FuelGiver`) → web/masaüstü anahtar sürüklenmesi imkânsız. Yeni servis yardımcıları:
`UserListPreferenceService.GetLastChoice / SaveLastChoice` (mevcut `GetColumns/SaveColumns` üzerine
ince sarmalayıcı → web mevcut `/api/me/list-columns/{key}` ucuyla AYNI biçimi yazar; **yeni API ucu
GEREKMEDİ**). Tercih kişiseldir (`user_id` daima oturumdan) ve senkron listesine dokunulmaz.

- **Masaüstü:** form açılışında (`ToggleDist`) ön-seçim; **yalnız başarılı kayıttan sonra** hatırlama.
  Kayıtlı kişi listede yoksa (silinmiş/pasif) sessizce boş bırakılır.
- **Web:** `OnInitializedAsync`'te ön-seçim; başarılı kayıttan sonra hatırlama + form temizlendikten
  sonra "veren" ön-seçili kalır (Yakıtı Alan kalmaz).

**Testler:** **yeni** `YakitVerenTercihTests` **8 test** — kayıt yoksa ön-seçim yok · son seçim geri
okunur · yeni seçim öncekini ezer · boş değer tercihi temizler · **kullanıcılar arası taşmaz** ·
liste ekranı kolon tercihiyle çakışmaz · **web biçimi ↔ masaüstü biçimi paritesi** · **"Yakıtı Alan"
hatırlanmaz** (kaynak-düzeyi kilit + katalogda tek anahtar). Koşu: 91/91 (grid/liste tercihi
regresyonu dahil). Web + Masaüstü Debug derlemeleri **0 hata**.

### Değişen dosyalar (S2 — aşağıda; S3 kaydı bu bölümün ardındadır)
**yeni** `src/DepoWise.Application/Ui/UserPrefKeys.cs` · `src/DepoWise.Web/DepoWise.Web.csproj`
(paylaşımlı dosya satırı) · `src/DepoWise.Infrastructure/Settings/UserListPreferenceService.cs` ·
`src/DepoWise.Desktop/ViewModels/FuelViewModel.cs` · `src/DepoWise.Web/Components/Pages/Fuel.razor` ·
**yeni** `tests/DepoWise.Tests/YakitVerenTercihTests.cs`.

---

## S3 — GÜN BAZLI YENİ RAPORLAR (✅ tamamlandı · PK-G1=A · PK-G2=A)

İki yeni KATALOG raporu (yeni ekran/menü YOK — iki platform aynı katalogdan besleniyor; Excel mevcut
merkezi mekanizmadan; kategori yetkileri ADR-181 çift kapısıyla otomatik):

**1) `fuel-daily` — "Yakıt Tüketim — Günlük"** (kategori Yakıt → `report_fuel`)
Her satır bir (ARAÇ, GÜN). **PK-G1=A: yalnız o gün fişi OLAN araçlar** — boş gün satırı üretilmez
(tüm filo × tüm gün görünümü bilinçli olarak `vehicle-daily`'de kaldı). Kolonlar dönem raporuyla
hizalı (Tarih + Şube · İç Kod · Plaka · Araç Adı · Araç Türü · Sayaç Birimi · İşlem · Mesafe · Litre ·
Ort. Tüketim · Ort. Fiyat · Maliyet · Birim Maliyet). Oranlar **günün** değerlerinden yeniden
hesaplanır (toplanmaz); TOPLAM satırı **dönemin tamamından** gelir (satır sınırına takılsa bile) →
**günlerin toplamı = dönem raporu** güvencesi testle kilitli. TEK sorgu + GROUP BY (N+1 yok).

**2) `stock-movements-daily` — "Stok Hareketleri — Günlük"** (kategori Stok → `report_stock`)
**PK-G2=A: gün × hareket türü ÖZETİ** (Tarih · Tür · İşlem Sayısı · Giriş Miktarı · Çıkış Miktarı).
Mevcut **detay** rapor aynen korundu (regresyonla kilitli). Filtreler (lokasyon/tür/arama/malzeme)
mevcut **tek kaynaktan** (`StockMovementFilterSql`) gelir → ekran = detay = özet. Tarih yine tek
kaynak `IslemTarihiSql` (`doc_date`, yoksa `created_at`). Miktar toplamları `SqlDialect.ExactSumText`
ile toplanır → PG'de tam kesinlik, SQLite'ta temiz ondalık (10,5 + 4,5 = **15**, kayan nokta artığı yok).
Transfer defterde iki bacaktır ve öyle sayılır; farklı birimlerin aynı toplamda birleştiği sınırlama
InfoNote'ta kullanıcıya açıkça yazıldı.

**Gün anahtarı** her iki raporda `tarih_ms / 86400000` tam sayı bölmesi → SQLite = PostgreSQL birebir,
UTC gün sınırıyla (00:00:00.000 – 23:59:59.999, iki uç dahil) hizalı.

**Testler:** **yeni** `GunlukRaporlarTests` **20 test** (katalog tanımları · gün kırılımı · günlük oranlar ·
**günlük≡dönem birebir + TOPLAM eşitliği** · gün sınırı iki uç · boş gün satırı üretilmez · araç/şube
filtreleri · tenant · BranchAccess · `reports`+kategori çift kapısı (403'ler) · sıralama · gün×tür özeti ·
kesin ondalık toplam · transfer iki bacak · filtreler tek kaynaktan · TOPLAM dönemden · **regresyon:
detay stok raporu ve `vehicle-daily` tam filo DEĞİŞMEDİ**) — ilk koşuda 20/20.
**yeni** `PostgresGunlukRaporlarTests` (izole PG: gün bölmesi · sınırlar · yalnız-fişli · günlük≡dönem ·
numeric kesin toplam · transfer · detay regresyonu) — PG koşusu **2/2** (mevcut `vehicle-daily` PG
testiyle birlikte). Katalog sayaç kilidi 22→**24**; iki "filtre yayılmasın" nöbetçisi (lokasyon/tür/
arama/malzeme listeleri) yeni raporu **kapsayacak** şekilde güncellendi — listeler hâlâ tüketici
(gevşetme değil). Hedefli koşu **98/99** (1 atlanan = PG sınıfı).

### Değişen dosyalar (S3)
`src/DepoWise.Application/Reports/ReportCatalog.cs` (2 katalog satırı) ·
`src/DepoWise.Infrastructure/Reporting/ReportService.cs` (2 yeni metot + 2 dispatch satırı) ·
`tests/DepoWise.Tests/ReportArchitectureTests.cs` · `StockReportLocationTests.cs` ·
`StockMovementsMaterialFilterTests.cs` · **yeni** `GunlukRaporlarTests.cs` ·
**yeni** `PostgresGunlukRaporlarTests.cs`. **UI dosyalarına DOKUNULMADI** (katalogdan beslenirler).

---

## S4 — "GÜNLÜK FAALİYET — DETAY" RAPORU (✅ tamamlandı · PK-D1=A)

Yeni katalog raporu `daily-activity` — **yeni ekran/menü AÇILMADI**, mevcut Raporlar ekranının tür
listesinden çalışır. Tarih **ZORUNLU**; satırlar gün gün (en yeni üstte); silinmiş kayıtlar hariç;
kolonlar: Tarih · Kayıt Tipi · Şube · Araç · Nereden → Nereye · Operatör · Süre (gün) · Açıklama;
TOPLAM satırı kayıt sayısı + süre toplamı.

**Kayıt tipi — yeni ÇOKLU SEÇİM filtresi.** Tip veritabanında İKİ sütunla kodlanır
(`activity_type` + `movement_kind`): Bakım · İlave Yağ · İlave Filtre · Tamir · **Hareket**
(movement ∧ kind≠transfer) · **Transfer** (movement ∧ kind=transfer). Seçenekler ve Türkçe etiketler
artık **paylaşımlı tek kaynaktan** (`DailyActivityTypeOptions`, web csproj'a eklendi) gelir — etiket
daha önce iki yerde tekrarlanıyordu, üçüncü kopya üretilmedi. **Hiçbir tip seçilmezse TÜM tipler**
listelenir (boş liste = filtre yok). Bilinmeyen anahtar parametre olarak bağlanır ve hiçbir satırla
eşleşmez (fail-closed; enjeksiyon yüzeyi yok).

**Yeni filtre 6 katmanın hepsinde bağlandı** (parite testi bu zinciri makine ile denetler):
katalog bayrağı `ReportFilters.ActivityType` + `UsesActivityType` · `ReportRequest.ActivityTypes`
(**SONA** eklendi — API kaydı pozisyonel kuruyor) · API katalog alanı `usesActivityType` + `ReportReqDto`
alanı + **sorgu ve export uçlarında birebir aynı aktarım** · web filtre bloğu + `CatItem` + iki gövde ·
masaüstü `ShowActivityType` + picks + `BuildTable` · masaüstü XAML bloğu.

**Yeni kategori + yeni yetki (MIGRATION YOK):** 9. kategori `ReportCategory.DailyActivity`
("Günlük Faaliyet") ve yeni anahtar **`report_daily_activity`** — `reports` üst kapısı KORUNDU, kategori
ikinci kapı olarak üç noktada (API katalog süzmesi · masaüstü katalog süzmesi · ortak `Run`) aynı
merkezden uygulanır. Anahtar deny-by-default: **yayın sonrası Yetkiler ekranından elle açılacak.**
Ayrıca `DataModule: "daily_activity"` → Günlük Faaliyet ekranı role kapalıysa rapor da kapalıdır.

**Testler:** **yeni** `GunlukFaaliyetRaporuTests` **19 test** (katalog + yeni anahtarın varlığı · kolonlar ·
**tip seçilmezse TÜM tipler** · boş liste = tümü · tek/çoklu tip · **Hareket ↔ Transfer ayrışması** ·
bilinmeyen anahtar fail-closed · silinmiş kayıt görünmez · aralık dışı görünmez · gün sınırı + sıralama ·
tenant · BranchAccess · araç filtresi · **çift kapı 403 matrisi** · yeni kategori başka raporu açmaz ·
TOPLAM satırı · tip kataloğu tek kaynak) — 19/19. `PostgresGunlukRaporlarTests` genişletildi (PG'de tip
eşlemesi + çoklu seçim + süre toplamı) — **PG 1/1**. Kilit güncellemeleri: katalog sayacı 24→**25** ·
`ScreenTreeParity` menüsüz-modül listesine yeni anahtar · `ReportFilterParity` haritasına yeni satır ·
"kayıt tipi yalnız bu raporda" nöbetçisi + **sıradaki boş bayrak (32768) nöbetçisi bir sıra ileri alındı** ·
`RaporKapsamliTarama` sweep'ine muafiyet YAZILMADI, **gerçek faaliyet kaydı seed edildi** (raporun boş
dönmediği asıl kuralla kanıtlandı). Hedefli koşu **134/134**; 4 proje Debug derlemesi 0 hata.

### Değişen dosyalar (S4)
**yeni** `src/DepoWise.Application/Ui/DailyActivityTypeOptions.cs` · `ReportCatalog.cs` (bayrak+kategori+
etiket+CategoryModule+descriptor) · `ReportModels.cs` (ActivityTypes) · `AppModules.cs` (yeni anahtar) ·
`ReportService.cs` (rapor metodu + filtre SQL/bind + dispatch) · `Api/Program.cs` (katalog alanı + DTO +
2 uç) · `Web/Reports.razor` · `Web/DepoWise.Web.csproj` · `Desktop/ReportsViewModel.cs` ·
`Desktop/Views/ReportsView.axaml` · testler: **yeni** `GunlukFaaliyetRaporuTests.cs`,
`PostgresGunlukRaporlarTests.cs` (genişletildi), `ReportArchitectureTests.cs`, `ScreenTreeParityTests.cs`,
`ReportFilterParityTests.cs`, `StockReportLocationTests.cs`, `RaporKapsamliTaramaTests.cs`.
