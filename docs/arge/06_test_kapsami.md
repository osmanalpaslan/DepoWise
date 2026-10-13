# 06 — Test kapsamı ve doğrulama raporu (AR-GE A4)

> Tarih: 2026-10-13 · Dal: `arge/2026-10-kapsamli` · Yöntem: **salt okuma** (kod ve test dosyaları okundu; test
> KOŞULMADI). Bilinen son sonuçlar: tam takım **4.103 / 4.103 geçti** (`artifacts/test-sonuc.trx`, 2026-10-12
> 19:36–20:15, atlanan 0 → PostgreSQL testleri de koştu), masaüstü arayüz testleri **47/47**, canlı web Playwright **14/14**.
> İlke: **kapsam yüzdesi hedef değildir; kritik iş kurallarının (CLAUDE.md §3) negatif senaryoyla kilitli olması hedeftir.**

---

## 0. Kısa sonuç (yönetici özeti)

- Test sayısı yüksek ve disiplin iyi: geçici DB, PostgreSQL güvenlik kapısı, tek koşu betiği, retry yasağı, kararsız
  testlerin kök neden düzeltmesi (ör. `SyncBalancePayloadTests` "777" vakası, `TestDbTemizlik` ClearAllPools vakası).
- **En tehlikeli boşluklar teste değil, test edilen sözleşmeye dair:** bazı testler CLAUDE.md §3'le **çelişen**
  davranışı "doğru" diye kilitliyor (onayda LWW, web'den bakım/yakıt kaydında idempotency yok, bakımda negatif stok).
- **Hiç test edilmeyenler:** iki çevrimdışı cihazın aynı stoğu düşüp senkronda birleşmesi (oversell), yakıt/bakım
  satırlarının senkronda LWW ile ezilmesi, gerçek boyutlu eski veride migration, masaüstü ↔ web ↔ API **sonuç** paritesi
  (çoğu parite testi kaynak kodu metin olarak tarıyor), web arayüzünde **yazma** akışı (Playwright yalnız okur).

---

## 1. Test envanteri

### 1.1 Projeler

| Proje | Araç | Test sayısı (son koşu) | Ne test eder | Varsayılan koşuda mı? |
|---|---|---|---|---|
| `tests/DepoWise.Tests` | xUnit (+ `Xunit.SkippableFact`) | **4.103** vaka (381 sınıf, 3.536 `[Fact]/[Theory]` özniteliği; Theory'ler açılınca 4.103) | Servis/iş kuralı, SQLite entegrasyon, PostgreSQL, gerçek HTTP (API host), kaynak (XAML/Razor/CSS) taraması | Evet — `scripts/run_tests.ps1` (~40 dk) |
| `tests/DepoWise.Desktop.UiTests` | Avalonia Headless + Skia | **47** (`GorselTaramaTests` 32, `Talep1011ArayuzTests` 5, `TabloArayuzTests` 3, `YeniEkranlarArayuzTests` 3 + Theory açılımı) | Masaüstü ekranlarının görünmez pencerede çizimi, yerleşim ölçümü, birkaç etkileşim | **Hayır** — `scripts/run_ui_tests.ps1`, elle açılır |
| `tests/web-e2e` | Playwright (chromium, `workers=1`, `retries=0`) | **14** (`duman.spec.js` 2, `talep-1011.spec.js` 6, `yeni-ekranlar.spec.js` 4, `yetki.spec.js` 2) | **Canlı** sitede (`depowise-web.fly.dev`) giriş + ekran açılışı + 1 yetki reddi | **Hayır** — elle; canlıda **yalnız okur** |

### 1.2 Modüllere göre dağılım (yaklaşık — sınıf adına göre sınıflandırıldı, son TRX'ten)

| Alan | Vaka | Sınıf | Örnek sınıflar |
|---|---|---|---|
| Güvenlik: tenant / şube / yetki | ~909 | 87 | `AuthPermissionTests`, `BranchIsolationMatrixTests`, `ApiTenantSweepTests`, `ApiYazmaTenantSweepTests`, `ApiTenantOkumaSilmeSweepTests`, `AlanYetkisiTests`, `PermissionGrantCeilingTests`, `ServiceTenantIsolationTests` |
| Arayüz / ortak ekran (çoğu kaynak taraması) | ~751 | 63 | `MasaustuTabloKolonHizasiTests`, `MenuRenkTests`, `AppScreensParityTests`, `TarihKaymasiTests`, `ScreenPlatformVisibilityTests` |
| Rapor / dışa aktarım | ~467 | 30 | `RaporKapsamliTaramaTests` (121), `CustomRaporTests`, `StockMovementsReportTests`, `ApiReportScopeTests` |
| Stok / malzeme | ~439 | 34 | `StockOperationTests`, `StockConcurrencyTests`, `MultiBranchStockScenarioTests`, `StockLocationTests`, `MaintenanceStockLocationTests` |
| Senkron / makine | ~220 | 27 | `SyncTests`, `BusinessSyncTests`, `SenkronSayacGeriGitmeTests`, `SyncCoverageTests`, `ReversalSyncStampTests` |
| Fatura / finans / cari | ~166 | 8 | `FinanceTests` (45), `InvoiceTests`, `PartyAccountingTests` |
| Talep / onay / satın alma | ~151 | 12 | `RequestOperationsPhase1/2Tests`, `RequestOperationsConcurrencyTests`, `OnayZinciriTests`, `SatinAlmaTests` |
| Personel / kullanıcı / ekip | ~147 | 11 | `OrgPersonnelTests`, `PersonnelImportTests`, `EkipTanimiTests` |
| Bakım / ekipman / iş emri | ~145 | 14 | `MaintenanceTests`, `EkipmanBakimTests`, `IsEmriTests`, `ActivityMaintenanceEditTests` |
| Yakıt | ~109 | 10 | `FuelConsumptionTests`, `FuelCancelTests`, `FuelImportTests`, `YakitTarihGunTests` |
| Filo / araç | ~96 | 8 | `VehicleTests`, `AracSayaciDuzeltmeTests`, `KiralikAracTests` |
| İçe aktarım | ~64 | 2 | `ImportFullFieldsTests`, `ApiImportTests` |
| PostgreSQL'e özel | ~59 | 25 | `PostgresStockConcurrencyTests`, `PostgresEndToEndTests`, `PostgresMigration08xTests` |
| Günlük faaliyet | ~41 | 4 | `DailyActivityCancelTests`, `DailyActivityExtraTests`, `GunlukFaaliyetSuzgecTests` |
| Migration / şema | ~31 | 7 | `Migration029/030/035/037Tests`, `CompanyIdMigrationTests`, `AcilisBozukIndeksOnarimiTests` |
| Zimmet | ~27 | 3 | `ZimmetTests`, `ZimmetTopluTeslimTests`, `ApiZimmetTopluTeslimTests` |
| Güncelleme / yedek / kurulum / diğer | ~280 | 36 | `UpdateIntegrityTests`, `FileTrashBackupTests`, `KurulumAraciTests`, `PaketSaklamaPolitikasiTests` |

> Not: Güvenlik testlerinin ağırlığı (≈%22) olumlu. Buna karşılık **Günlük Faaliyet (41) ve Zimmet (27)** iş hacmine
> göre zayıf; Arayüz kümesinin büyük kısmı davranış değil **kaynak metni** doğrular (bkz. §3).

### 1.3 Test türleri

| Tür | Yaklaşık hacim | Nasıl | Kanıt |
|---|---|---|---|
| Birim (saf fonksiyon) | az | DB'siz karar fonksiyonları | `PostgresTestGuard.SchemaAcceptable`, `MeterRule.ShouldAdvance` (`AracSayaciDuzeltmeTests.SY7_Supheli_Sicrama_Uyarisi`), `MoneyPrecisionTests`, `TarihMetniTests` |
| **SQLite entegrasyon** (çoğunluk) | ~3.000+ | Her sınıf `%TEMP%\dw_*.db` / `depowise_*.db` geçici dosya; gerçek migration + gerçek servis | `StockOperationTests`, `FinalStabilizasyonTests` (`dw_fin_*.db`) |
| **PostgreSQL** | 26 sınıf (`[Collection("PostgresSchema")]` + `PostgresTestGuard.SkipUnlessSafe()`) | Yerel PG `localhost:54329`, şema her testte sıfırlanır | `PostgresStockConcurrencyTests`, `PostgresEndToEndTests`, `PostgresSyncRecoveryTests` |
| **Gerçek HTTP / API host** | 60 sınıf | `ApiTestHost : WebApplicationFactory<Program>` — JWT + yetki + model bağlama dahil; ayrı geçici veri klasörü, `DEPOWISE_PG_URL` silinir | `ApiTenantSweepTests`, `FazKUctanUcaApiTests`, `ApiSecurityGateTests` |
| **Kaynak taraması** (XAML/Razor/CSS/C# metni) | ~70–90 sınıf içeriyor | `File.ReadAllText` + `Assert.Contains("...")` | `MenuRenkTests`, `TersKayitKapisiParityTests`, `AppScreensParityTests`, `MasaustuTabloKolonHizasiTests` |
| Görünmez masaüstü arayüz | 47 | Avalonia Headless, gerçek `App` stilleri | `Talep1011ArayuzTests`, `GorselTaramaTests` |
| Canlı web e2e | 14 | Playwright, canlıda salt okuma | `tests/web-e2e/tests/*.spec.js` |
| Performans / hacim | birkaç | Kademeli ölçüm, gevşek eşik | `BuyukVeriOlcumTests.BV1/BV2` (25.000 satır), `ImportFullFieldsTests.Hacim_3000Arac_*`, `AlanYetkiPerformansTests` |

### 1.4 İzolasyon nasıl sağlanıyor (ve nerede sızıyor)

- **İyi:** her test sınıfı kendi geçici SQLite dosyasını kurar; `TestDbTemizlik.Bitir` yalnız kendi havuzunu temizler
  (eskiden süreç geneli `ClearAllPools` paralel testleri düşürüyordu — düzeltilmiş). Koşu başında `dw_`/`depowise_`
  artıkları süpürülür. Saat sahte (`FakeClock.Advance`) → zamana bağlı testler deterministik.
- **İyi:** `PostgresTestGuard` 5 kapılı, fail-closed: onay değişkeni, DB adında "test", şema boş ya da işaretli,
  < 50 MB, salt-okunur değil. Canlı DB'ye yanlışlıkla bağlanma riski düşük. `PostgresTestGuardTests` (11) kapıyı test ediyor.
- **Sızıntı 1 — süreç geneli ortam değişkenleri:** `ApiTestHost.ConfigureWebHost` `DEPOWISE_SERVER_DATA`,
  `DEPOWISE_JWT_KEY`, `DEPOWISE_PG_URL` değişkenlerini **süreç genelinde** yazıyor/siliyor. Bu yüzden API sınıfları
  `PostgresSchema` koleksiyonuna alınıp sıraya sokulmuş; PG adresi `PgTestOrtami` ile süreç başında "fotoğraflanıyor".
  Çalışıyor ama kırılgan: yeni bir API test sınıfı koleksiyon özniteliğini unutursa paralel sınıflar birbirinin veri
  klasörünü görebilir.
- **Sızıntı 2 — masaüstü arayüz testlerinde ortak ortam:** `GorselTaramaTests.Ortam()` ve `Talep1011ArayuzTests.Ortam()`
  süreç genelinde **tek** `DesktopServices` örneği ve tek `UiTest_*` veritabanı kullanıyor. `GorselTaramaTests.cs:53-54`
  yorumu açıkça yazıyor: araç tohumu eklemek, araç listesini birebir sayan UIT10 testini **koşu sırasına göre**
  düşürüyordu (2026-10-12). Yani UI testleri **sıraya bağımlı**; şimdilik tohum azaltılarak susturulmuş.
- **Sızıntı 3 — atlanan test "geçti" sayılır:** `run_tests.ps1` PG adresi bulamazsa uyarı yazıp devam eder; xUnit
  Skip'leri çıkış kodunu bozmaz. Son koşuda atlanan 0, ama betik "PG testleri atlandıysa kırmızı" demiyor.

---

## 2. İzlenebilirlik matrisi — kural → kod → test

Durum: **TAM** = olumlu + olumsuz senaryo kilitli · **KISMİ** = yalnız bir katman/yol · **YOK** = test yok ·
**ÇELİŞKİ** = test, kuralın tersini kilitliyor.

### 2.1 Kritik iş kuralları (CLAUDE.md §3)

| # | Kural | Kod | Test kanıtı | Durum |
|---|---|---|---|---|
| K1 | Tenant izolasyonu (`company_id` yalnız oturumdan) | servisler + `TenantGuard`; senkron `Apply` `values["company_id"]=companyId` | `DatabaseFoundationTests` (fail-closed), `ServiceTenantIsolationTests`, `ApiTenantSweepTests`/`ApiYazmaTenantSweepTests`/`ApiTenantOkumaSilmeSweepTests` (gerçek HTTP okuma/yazma/silme), `BusinessSyncTests.Apply_CompanyId_OturumdanZorlanir`, `Snapshot_BaskaFirmaninVerisini_Sizdirmaz`, `FazKUctanUcaApiTests.UUA1/UUA2`, `EndToEndTests.UctanUca_TamAkis_TenantIzolasyonu` | **TAM** (zayıf nokta: `IsDenied` 500'ü de "ret" sayar, §3.1) |
| K2 | Şube erişimi (BranchScope) | `BranchScope`, `StockMovementFilterSql` | `BranchIsolationMatrixTests` (27), `BranchScopeParityTests`, `SyncBranchScopeTests`, `BranchScopeSyncGrantTests`, `StockOperationTests.Cikis_SubeyeBagliKullanici_BaskaSubeden_Reddedilir`, `ApiReportScopeTests` | **TAM** |
| K3 | Deny-by-default, UI ve API'de aynı yetki | `PermissionSet`, API `RequireAuthorization` | `StockOperationTests.DenyByDefault_YetkisizStokIslemi`, `AuthPermissionTests`, `AlanYetkiApiTests`, `RolIzniApiTests`, `RaporKapaliModulBypassTests`, `BusinessSyncTests.Apply_YetkisizModul_TablosuUygulanmaz`; web: `yetki.spec.js` (2) | **KISMİ** — API ve servis tam; "UI = API" eşitliği büyük ölçüde **kaynak taraması** (`ButtonPermissionCatalogTests`, `TersKayitKapisiParityTests`). Her API ucu için otomatik "yetkisiz rol → 403" taraması yok |
| K4 | Stok defteri ana kaynak; bakiye doğrudan değişmez | `StockService`, `StockBalanceWriter`, `RecomputeBalances` | `StockOperationTests.RecomputeBalances_HareketDefterindenDogruHesaplar_BozukSnapshotEzmez`, `StockConcurrencyTests.RecomputeBalances_Defterden_Dogru_Bakiyeyi_Yeniden_Kurar`, `BusinessSyncTests.SNK11_Bakiye_Senkron_Paketinde_TASINMAZ_Hareketler_Tasinir`, `SyncBalancePayloadTests` | **TAM** |
| K5 | Negatif stok yok | `NegativeStockException`; CAS koşullu güncelleme | Tek cihaz: `StockOperationTests.Cikis_NegatifStok_Engellenir_BakiyeDegismez`, `Transfer_YetersizStok_Engellenir`, `EsZamanli_IkiCikis_NegatifStokOlusturamaz`; PG: `PostgresStockConcurrencyTests.Eszamanli_Iki_Cikis_Oversell_Ve_Kayip_Dusum_Uretmez`, `Yuksek_Cekismede_Bakiye_Negatife_Dusmez…`; `MultiBranchStockScenarioTests.S13_…`; `FuelCancelTests.Depo_girisi_bakiyeyi_EKSIYE_dusurecekse_IPTAL_EDILEMEZ` | **KISMİ + ÇELİŞKİ**: (a) `StockConcurrencyTests.Bakim_Malzemesi_Ortak_Yaziciyi_Kullanir_Ve_Negatife_Izin_Verir` (satır 272) bakımda **-4 bakiyeyi doğru** sayıyor (ADR 2026-08-06 istisnası, §3'te yazmıyor); (b) **senkron yolunda** negatif bakiye kontrolü yok (`BusinessSyncService.cs:247-252` bilinçli) ve iki çevrimdışı cihazın aynı stoğu düşmesi **test edilmemiş** |
| K6 | Sayaç geriye gidemez | `MeterRule.ShouldAdvance`, `MeterBackwardException`, senkron `BusinessSyncService.cs:1513-1536` | `VehicleTests` (MeterBackward), `SenkronSayacGeriGitmeTests.SNK01a–e`, `AracSayaciDuzeltmeTests.SY1–SY9` (iptal/düzeltmede geri çekme, negatif sayaç reddi), `FuelCancelTests.Dagitim_iptali_ARAC_SAYACINI_…` | **TAM** (yalnız istemci→sunucu; sunucu→istemci pull bilinçli olarak sunucu-otoriter, testi yok) |
| K7 | Idempotency / operation id | `operation_id` UNIQUE, `sync_inbox (company_id, operation_id)` | `StockOperationTests.Idempotency_AyniOperation_IkinciHareketUretmez`, `Iptal_Idempotent_IkinciKezNoOp`, `SyncTests.Push_AyniOperation_IkinciKez_AlreadyApplied`, `FinalStabilizasyonTests.FIN16`, `FazKUctanUcaApiTests.UUA6` (servis yolu) | **KISMİ + ÇELİŞKİ**: web/API'den **bakım, yakıt, günlük faaliyet, açılış stoğu** kayıtlarında sunucu her istekte yeni `Guid` üretiyor (`Program.cs:4187, 4231, 4337, 4352, 4380, 4389, 4398, 4412, 1920`). `UUA6_Ayni_Bakim_Iki_Kez_Gonderilirse_Davranis_Kayitlidir` **çift kaydı (2 bakım) beklenen sonuç** olarak kilitliyor — bakım malzeme düşüyorsa stok da iki kez düşer |
| K8 | Stok/sayaç/yakıt/bakım/onayda **LWW yasak** | Senkron genel upsert: `excluded.updated_at >= tablo.updated_at` (`BusinessSyncService.cs` ~1550) — `fuel_distributions`, `fuel_depot_entries`, `vehicle_maintenances`, `maintenance_materials`, `material_requests` hepsi bu yoldan | Stok: hareketler değişmez satır + bakiye taşınmıyor (SNK11) → uygun. Sayaç: SNK01 → uygun. Talep düzenleme: `RequestOperationsConcurrencyTests` (sürüm kontrolü, online). **Onay: `FinalStabilizasyonTests` FIN10 (satır ~327-333) "approved"ı daha yeni "rejected"ın ezmesini `// LWW: son yazan kazanır` diye kilitliyor.** Yakıt/bakım senkron çakışması: **test yok** | **ÇELİŞKİ (onay) + YOK (yakıt, bakım)** |
| K9 | Operasyonel kayıt fiziksel silinmez (iptal/ters kayıt + audit) | `is_deleted`, `is_cancelled`, ters hareket; istisna ADR-083 kalıcı silme | `DatabaseFoundationTests` (soft-delete), `StockOperationTests.Iptal_TersHareket_BakiyeyiGeriAlir_BelgeIptal`, `FuelCancelTests.Iptal_audit_kaydi_olusturur`, `DailyActivityCancelTests` (14), `ApiInspectionCancelTests`, `BusinessSyncTests.Sunucuda_Silinen_Kayit_Cihaz_Pushuyla_Diriltilemez`, `AuditCoverageTests`, `CompanyPurgeTests`/`PostgresPurgeTests` (istisna) | **TAM** (genel bir "hiçbir operasyonel tabloda DELETE yok" kaynak taraması yok; modül modül) |
| K10 | Çevrimdışı kalıcılık | outbox, yerel SQLite | `SyncTests.Offline_Kalicilik_YenidenAcilis`, `Outbox_YerelWriteIleAtomik_RollbackHicbiriniBirakmaz`, `DesktopOfflineLocationTests` (13), `PostgresSyncRecoveryTests` | **KISMİ** — tek cihaz/servis düzeyinde; **masaüstü outbox → gerçek API push → başka cihazda pull** uçtan uca HTTP testi yok |
| K11 | Eski şema toleransı | migration'lar, `OnayMigrationVeEskiIstemciTests`, eski istemci paketleri | `Migration029/030/035/037Tests` (1'er), `CompanyIdMigrationTests` (14), `PostgresMigration082/084/085/086Tests`, `OnayMigrationVeEskiIstemciTests` (5), `EkipMigrationVeAynaTests`, `GuncellemeSurumUyumuTests`, `AcilisBozukIndeksOnarimiTests` | **KISMİ** — elle kurulan birkaç satırla; gerçek boyutlu/anonimleştirilmiş eski DB ile yükseltme yok; her migration'ın testi yok |
| K12 | Tarih semantiği (UTC gün başı, `IsGunuTarihi`) | `IsGunuTarihi`, web tarih dönüşümü | `TarihKaymasiTests` (49), `YakitTarihGunTests` (14), `IslemTarihiTests` (15), `WebDateConversionTests` (13), `ReportDateRangeTests` | **TAM** (bir kısmı kaynak taraması; saat dilimi gece yarısı sınırı ayrıca PG'de denenmemiş) |
| K13 | Kayıt işlemleri `ConfirmService` onayı ister | ConfirmService | `IslemOnaylariTests` (3), `OnayPenceresiTests` (3) — **kaynak taraması** | **KISMİ** |

### 2.2 Ana modüller

| Modül | Olumlu | Olumsuz / kural | Eşzamanlılık | API (HTTP) | Web UI | Durum |
|---|---|---|---|---|---|---|
| Stok (giriş/çıkış/transfer/sayım/iptal) | `StockOperationTests`, `StockDistributeTests`, `StockCountTests`, `MultiMaterialStockTests` | negatif, yetersiz transfer, aynı depoya transfer (`DesktopOfflineLocationTests.Ayni_Depoya_Transfer_Engellenir`), gerekçesiz sayım | SQLite + PG | `ApiStockLocationTests`, `ApiStockMovementsReportTests`, `ApiMultiMaterialTests` | yok (yalnız okuma) | **TAM** (senkron oversell hariç) |
| Filo / araç | `VehicleTests`, `KiralikAracTests` | sayaç geri, negatif sayaç | yok | `ApiVehicleTenantRefTests`, `ApiVehicleBrandLookupTests` | ekran açılışı | **KISMİ** |
| Günlük faaliyet | `DailyActivityExtraTests`, `GunlukFaaliyetSuzgecTests` | `DailyActivityCancelTests`, `GunlukFaaliyetTipYetkisiTests` | **yok** | dolaylı | yok | **KISMİ** (41 vaka; API ucu sunucu-tarafı op id, idempotent değil) |
| Bakım | `MaintenanceTests`, `EkipmanBakimTests`, `MaintenanceStockLocationTests` | iptal, yetki, konum | `MaintenanceDefinitionConcurrencyTests` (yalnız tanım) | `ApiMaintenanceLocationTests`, `FazKUctanUcaApiTests.UUA6` | yok | **KISMİ + ÇELİŞKİ** (çift POST = çift kayıt; negatif stok izinli) |
| Yakıt | `FuelConsumptionTests`, `FuelDailyActivityTests`, `FuelDepotReportTests` | `FuelCancelTests` (eksiye düşürecek iptal reddi, tekrar iptal reddi, başka firma) | **yok** | dolaylı | ekran açılışı | **KISMİ** (senkron LWW, API idempotency yok) |
| Talep / onay | `RequestTests`, `RequestOperationsPhase1/2Tests`, `OnayZinciriTests` | rejected tekrar gönderilemez, self-approval yalnız admin | `RequestOperationsConcurrencyTests` (sürüm), FIN9 (online ilk onay kazanır) | `OnayApiTests` | form alanı (okuma) | **KISMİ + ÇELİŞKİ** (çevrimdışı LWW) |
| Fatura / finans / cari | `InvoiceTests`, `FinanceTests`, `PartyAccountingTests` | pasif hesap, iptal, para hassasiyeti (`MoneyPrecisionTests`) | `PartyAccountingTests` içinde sınırlı | dolaylı | yok | **KISMİ** (API'de tenant taraması var; eşzamanlı ödeme/fatura yok) |
| Zimmet | `ZimmetTests`, `ZimmetTopluTeslimTests` | sınırlı | **yok** | `ApiZimmetTopluTeslimTests` | 1 okuma testi | **KISMİ** (27 vaka — en zayıf modül) |
| Yetkiler | çok güçlü | çok güçlü | `PermissionConcurrencyTests` | güçlü | 2 test | **TAM** (UI eşitliği hariç) |
| Senkron | `SyncTests`, `BusinessSyncTests`, `SenkronDeltaTests`, `SyncCoverageTests` | diriltme yasağı, negatif miktar reddi, yetkisiz modül | yok | `SyncTests` (servis), `AnlikEsitlemeTests` | — | **KISMİ** (çok cihazlı gerçek HTTP turu yok) |

---

## 3. Zayıf / gevşek testler (somut örnekler)

### 3.1 "Ret" tanımı çok geniş — çökme de geçiyor
- `tests/DepoWise.Tests/ApiTestHost.cs` (son satırlar): `IsDenied(r) => (int)r.StatusCode is 400 or 401 or 403 or 404 or 500;`
  **500 (sunucu çöktü) "erişim reddedildi" sayılıyor.** 16 dosyada 56 kullanım (ör. `ApiTenantSweepTests.cs:93, 127, 134`,
  `BranchIsolationMatrixTests`, `ApiSecurityGateTests`, `YetkiZinciriTests`). Bir uç başka firma isteğinde NullReference ile
  çökse test yeşil kalır; bu hem bir hata hem de olası bilgi sızıntısı (hata gövdesi) olarak gizlenir.
  `FazKUctanUcaApiTests.UUA4_Bozuk_Parametreler_500_Uretmez` bunu yalnız birkaç uç için ayrıca deniyor.

### 3.2 Hiç iddia (assert) içermeyen arayüz testleri
- `tests/DepoWise.Desktop.UiTests/GorselTaramaTests.cs:258-292` — **G01–G30 (30 test)** yalnız `Tara(...)` çağırıyor;
  `Tara` (satır 228-254) ölçüm bulgularını `bulgular.txt`'ye yazıp döndürüyor ama **hiçbir test sonucu kontrol etmiyor**.
  Sınıf açıklaması da "taşma ölçümü şimdilik RAPOR amaçlıdır" diyor. Yani "47/47 geçti"nin 30'u fiilen
  "ekran istisna atmadan açıldı" demek. Ekran görüntüsü kaydı da `try { … } catch { }` ile sessizce yutuluyor (satır 243).
- `SubeAgaciKapsamTutarliligiTests.cs:152-153`, `TanimTablosuSemaTests.cs:70-71` — `Assert.Null(Record.Exception(...))`
  biçimi: yalnız "patlamadı" kontrolü; dönen değer doğrulanmıyor (bağlama göre kabul edilebilir ama zayıf).

### 3.3 Kaynak metni tarayan testler (kolay geçer / kolay kırılır)
- `MenuRenkTests.cs:367-379` — `Assert.Contains("Opacity=\"0.85\"", pencere)`, `Assert.Contains(".dw-fam-group::before   { width: 2px; opacity: .85;", css)`:
  dosyada bu metin **herhangi bir yerde** geçerse geçer (yorum satırı dahil); boşluk değişince davranış aynıyken kırılır.
- `MenuRenkTests.cs:406-408` — `Assert.DoesNotContain("AccessControl", kod)`: yalnız belirli adlara bakar; aynı işi
  farklı adla yapan kod geçer.
- Parite testlerinin büyük bölümü (`TersKayitKapisiParityTests`, `TransferPariteTests`, `TumSubelerStokPariteTests`,
  `ParitePRT02Tests`, `IslemOnaylariTests`, `OnayPenceresiTests`, `ButtonPermissionCatalogTests`) masaüstü ve web
  **kaynağında** aynı çağrının/düğmenin geçtiğini kontrol eder, **çıktının aynı olduğunu değil**. İstisna (iyi örnek):
  `StockMovementsScreenReportParityTests` ekran = rapor = XLSX'i gerçek veriyle hücre hücre karşılaştırıyor.
- 90 kadar dosya kaynak okuması içeriyor (§1.3). Bunlar "unutulmuş bağlantı" yakalamada değerli; ama davranış testinin
  yerine sayılmamalı.

### 3.4 Beklentisi bir hatayı/kural çelişkisini kilitleyen testler
| Test | Kilitlediği davranış | Çelişki |
|---|---|---|
| `FinalStabilizasyonTests` FIN10 (satır ~327-333) | çevrimdışı "approved" sonrası daha yeni zaman damgalı "rejected" **kazanır** (`// LWW: son yazan kazanır`) | §3: "onayda LWW yasak" |
| `FazKUctanUcaApiTests.UUA6_Ayni_Bakim_Iki_Kez_Gonderilirse_Davranis_Kayitlidir` (satır 228) | aynı bakım iki kez POST → **2 kayıt** | §3: "operation id + idempotency" (web çift tık / ağ tekrarında çift kayıt + çift stok düşümü) |
| `StockConcurrencyTests.Bakim_Malzemesi_Ortak_Yaziciyi_Kullanir_Ve_Negatife_Izin_Verir` (satır 272) | bakım malzemesiyle bakiye **-4** | §3: "negatif stok yok" — ADR istisnası §3'e yazılmamış |
| `BusinessSyncTests.SNK11_Negatif_Acilis_Defter_Uzerinden_Tasinir` (satır 453) | negatif açılış bakiyesi (-9) senkronla taşınır | ADR-086 istisnası; §3'te yok |
| `BusinessSyncTests.GeriCekmede_SilinmemisKayitta_LWW_Korunur`, `Apply_LWW_EskiYazmaYeniyiEzmez` | personel tablosunda LWW | Uygun (tanımsal veri) — ama aynı genel yol yakıt/bakım/talep tablolarına da uygulanıyor ve onlar için karşı test yok |

Bu satırlar "test yanlış" demek değil: ya **§3 istisnalarla güncellenmeli** ya da **kod ve test kurala çekilmeli**. Karar ürün sahibinde.

### 3.5 Sıraya / ortak duruma bağımlı testler
- Masaüstü UI: ortak `UiTest_*` DB'si — `GorselTaramaTests.cs:53-54` (UIT10 araç sayımı vakası). `Talep1011ArayuzTests.Ortam()` aynı kalıpta.
- API: süreç geneli ortam değişkenleri (§1.4) — koleksiyon sıralamasıyla güvence altında.
- `PaketSaklamaPolitikasiTests.PKT3_Sinir_Asilinca_Disk_Buyumez` **12 dk** sürüyor (tam koşunun ~%30'u); bir süre/disk
  testi tüm takımın geri bildirim süresini belirliyor.

### 3.6 Regresyon yakalama gücü düşük olanlar
- `BuyukVeriOlcumTests.BV1` eşiği bilinçli olarak gevşek (`sonKademeIlkSayfa < 3000 ms`, `≤ ilk×50`): büyük gerilemeyi yakalar, %3–5'lik yavaşlamayı yakalamaz.
- `Migration029/030/035/037Tests` birer vaka, elle eklenmiş tek satırla.
- `EndToEndTests.UctanUca_TamAkis_TenantIzolasyonu` tek uzun test (13 assert): kırılınca hangi adımın bozulduğu zor okunur.
- Web e2e'de 5 yerde `test.skip(...)` koşulu var (`talep-1011.spec.js:82, 93`, `yeni-ekranlar.spec.js:63` vb.): test kullanıcısının yetkisi değişirse testler **sessizce atlanır**, rapor yine "geçti" görünür.

---

## 4. Boşluklar

| # | Boşluk | Mevcut durum | Risk |
|---|---|---|---|
| B1 | **İki çevrimdışı cihaz aynı stoğu düşer → senkron** | Tek süreçte eşzamanlılık var (SQLite/PG). Senkronda hareketler değişmez satır olarak birleşiyor, sunucu `RecomputeBalances` ile **negatif** bakiye üretebilir; kural bilinçli (`BusinessSyncService.cs:247`) ama ne tespit/uyarı ne test var | **Yüksek** — §3 "negatif stok yok" sahada aşılabilir, kimse fark etmez |
| B2 | **Yakıt / bakım satırlarının senkron çakışması** | `fuel_distributions`, `vehicle_maintenances`, `maintenance_materials` genel LWW upsert ile uygulanıyor; çakışma testi yok | **Yüksek** — §3 "yakıt/bakımda LWW yasak" ihlali test edilmemiş |
| B3 | **Web/API'den bakım-yakıt-faaliyet kaydında idempotency** | Sunucu her istekte yeni op id üretiyor; UUA6 çift kaydı kilitliyor | **Yüksek** — çift tık / zayıf mobil ağ tekrarında çift kayıt, çift stok düşümü |
| B4 | Çevrimdışı → senkron **uçtan uca** (masaüstü outbox → gerçek API → ikinci cihaz pull) | Parçalar ayrı ayrı test ediliyor (`SyncTests`, `BusinessSyncTests`, `ApiTestHost`), zincir yok | Orta-yüksek |
| B5 | Masaüstü ↔ web ↔ API **sonuç** paritesi | Çoğu kaynak taraması; tek gerçek-veri örneği stok hareketleri (`StockMovementsScreenReportParityTests`) | Orta — §3 "işlevsel eşit" kuralı rapor/stok dışında kanıtsız |
| B6 | Gerçek boyutlu eski veride migration | Elle birkaç satırlık senaryolar; PG'de 08x için birer test | Orta-yüksek — yayında süre aşımı / kilit / bozuk satır riski |
| B7 | Büyük veri performansı | `BuyukVeriOlcumTests` (25.000 hareket, SQLite), `ImportFullFieldsTests.Hacim_3000*`, `AlanYetkiPerformansTests`; **PG'de hacim testi ve rapor/senkron delta hacim testi yok**; üretim PG | Orta |
| B8 | Güvenlik olumsuz testleri | Tenant tarama (okuma/yazma/silme) iyi; **eksik:** her API ucunu otomatik listeleyip yetkisiz rolle çağıran "uç kataloğu" testi, 500'ün ret sayılması (§3.1), JWT süresi dolmuş/başka firma token'ı ile senkron push, cihaz token'ı iptali sonrası pull | Orta |
| B9 | Web arayüz e2e | 14 test, canlı sitede yalnız **okuma**; web'de hiçbir kayıt akışı (stok çıkışı, bakım, onay) tarayıcıdan denenmiyor; yerel/izole web ortamına karşı koşu yok | Orta-yüksek — Blazor form hataları (bağlama, onay penceresi, çift tık) yakalanmıyor |
| B10 | Masaüstü arayüz testi varsayılan koşuda değil, 30/47'si iddiasız | Elle açılıyor | Orta |
| B11 | Eşzamanlılık modül boşlukları | Fatura/ödeme, zimmet, günlük faaliyet, yakıt deposu için eşzamanlı yazma testi yok | Orta |
| B12 | Belgelerin güncelliği | `docs/TEST_LISTESI.md` 2026-06-28'den kalma (elle test listesi, eski menü yapısı); `docs/TEST_ORTAMI.md` SQLite/Hetzner dönemini anlatıyor (üretim artık Fly.io + Supabase PG) | Düşük — ama yanıltıcı |

---

## 5. Öneriler (öncelik sırasıyla)

Efor: **S** ≤ 1 gün · **M** 2–4 gün · **L** 1 hafta+.

| Öncelik | Öneri | Katman / araç | Efor |
|---|---|---|---|
| **P0** | **Kural kararı:** onay LWW (FIN10), bakımda negatif stok, açılışta negatif bakiye, web bakım/yakıt çift kaydı (UUA6) için ürün sahibiyle karar; ya §3'e "istisna" olarak yaz ya da kodu kurala çek. Testler karara göre güncellenir (gevşetme değil, sözleşme değişikliği olarak ADR'li) | Belge + ADR | S |
| **P0** | `ApiTestHost.IsDenied`'dan **500'ü çıkar**; çöken uçları ayrı hata olarak raporla (önce ölçüm: hangi testler 500 alıyor) | xUnit, API host | S |
| **P0** | **Web/API idempotency testi**: bakım, yakıt dağıtım/giriş, günlük faaliyet, açılış stoğu uçlarına aynı `operationId` ile iki POST → tek kayıt, tek stok düşümü (kod düzeltmesiyle birlikte: istemci op id'si zorunlu) | xUnit + `ApiTestHost` | M |
| **P1** | **İki cihaz oversell senaryosu**: A ve B cihazı çevrimdışı 5'lik stoktan 5'er çıkış → ikisi push → beklenen davranış (ret / çakışma kaydı / negatif uyarı) kilitlensin | xUnit, iki SQLite + sunucu (SQLite ve PG) | M |
| **P1** | **Yakıt/bakım/onay senkron çakışma testleri**: aynı satır iki cihazda farklı değiştirilir → LWW olmadığını (veya bilinçli kuralı) kanıtla; `data_conflicts`'e düştüğünü doğrula | xUnit (`BusinessSyncService`) | M |
| **P1** | **Uçtan uca çevrimdışı tur**: masaüstü servisleriyle yaz → outbox → `ApiTestHost` push → ikinci yerel DB pull → bakiye/sayaç/iptal aynı | xUnit + API host | M |
| **P1** | **API uç kataloğu güvenlik taraması**: `EndpointDataSource`'tan tüm uçları listele; her uç için anonim, yetkisiz rol, başka firma token'ı → 401/403/404 (500 yasak). Yeni uç eklenince otomatik kapsama girer | xUnit + API host | M |
| **P2** | `GorselTaramaTests` G01–G30'a eşik ekle: düzeltilmiş ekranlarda bulgu sayısı 0 kilitlensin; diğerleri için "bulgu sayısı artmasın" tabanı | Avalonia Headless | S |
| **P2** | UI testlerinde ortak ortamı kaldır: her sınıf kendi `UiTest_*` ortamı (ya da sayım testleri göreli sayım yapsın) | Avalonia Headless | S |
| **P2** | **Web yazma e2e** — yerel API + yerel web + boş test DB'ye karşı (canlıya değil): stok çıkışı, bakım kaydı, talep onayı, iptal; çift tıkta tek kayıt | Playwright, yerel ortam | L |
| **P2** | **Sonuç paritesi**: aynı tohum veride masaüstü servis sonucu = API yanıtı = web tablo (stok bakiye, yakıt özeti, bakım listesi, cari bakiye) — `StockMovementsScreenReportParityTests` kalıbını genişlet | xUnit + API host | M |
| **P2** | **Gerçek boyutlu migration provası**: anonimleştirilmiş üretim yedeğinden (ya da 100k+ sentetik satır) PG'de tüm migration zinciri; süre ve satır sayısı denetimi | PG test sunucusu | M |
| **P3** | PG'de hacim ölçümü (stok hareketi raporu, senkron delta, yetki filtresi) — gevşek eşik + trend kaydı | xUnit PG | M |
| **P3** | `run_tests.ps1`: PG testleri atlandıysa (ya da e2e'de `test.skip` olduysa) sonucu **sarı/kırmızı** göster; TRX'ten `NotExecuted` sayısını oku | betik | S |
| **P3** | `PKT3_Sinir_Asilinca_Disk_Buyumez` (12 dk) küçültülsün ya da "kapsamlı" kategoriye alınsın | xUnit trait | S |
| **P3** | `docs/TEST_LISTESI.md` ve `docs/TEST_ORTAMI.md` güncellensin (ya da arşive) | belge | S |
| **P3** | Zimmet, günlük faaliyet, fatura/ödeme için eşzamanlı yazma testleri | xUnit | M |

**Not:** Kaynak taraması testleri silinmemeli — "bağlantı unutuldu" türü hataları ucuza yakalıyorlar. Öneri, kritik
kurallarda bunların **yanına** davranış testi koymak.
