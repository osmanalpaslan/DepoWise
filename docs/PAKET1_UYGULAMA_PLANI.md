# PAKET 1 — UYGULAMA PLANI (KD-1 + T-1…T-6 + API çok-firmalı testler)

- **Tarih:** 2026-08-09
- **Durum:** ⏸️ **YALNIZ PLAN** — kod değiştirilmedi, migration çalıştırılmadı, deploy yapılmadı,
  canlı veriye yazılmadı.
- **Yöntem:** salt-okuma kod incelemesi + canlı salt-okuma HTTP kontrolleri.

---

## 0. ÖNEMLİ DÜZELTMELER (önceki rapora göre)

Bu derin inceleme, önceki genel raporun **iki noktasını düzeltti**:

1. **KD-1 kapsamı 2 değil 3 uç.** Üçüncü kırık uç canlıda doğrulandı:
   `GET /api/materials/{id}/movements` → **500**.
2. **T-2 önceki raporda olduğundan daha az ve daha çok.** Ayrıntı §2'de — "başka firmanın
   tanımı değiştirilebilir" iddiası **mevcut uç üzerinden yanlış**; ama **fark edilmemiş ikinci bir
   açık** (yabancı araç bağlama) gerçek ve sömürülebilir.

---

## 1. KD-1 — `rowid` PostgreSQL'de yok

### Zincir
| Katman | Konum |
|---|---|
| Uç 1 | `GET /api/stock` — `Program.cs:961` |
| Uç 2 | `GET /api/stock/movements` — `Program.cs:963` |
| Uç 3 | `GET /api/materials/{id}/movements` — `Program.cs:842` |
| Servis 1-2 | `StockService.RecentMovements` (`:221`) → `SearchMovements` (`:227`) |
| Servis 3 | `StockService.RecentForMaterial` (`:269`) |
| Kırılma noktası | `StockService.cs:246` ve `:284` → `ORDER BY sm.created_at DESC, sm.rowid DESC` |
| Hata | `Npgsql 42703: column sm.rowid does not exist` |

**Canlı doğrulama (bugün, salt-okuma):** üç uç da **500**.

### Neden `rowid` var?
`created_at` Unix **milisaniye**; aynı milisaniyede birden çok hareket oluşabilir (toplu işlem,
bakım malzemeleri döngüsü). `rowid` SQLite'ta **ekleme sırasını** verir → kararlı sıralama.
PostgreSQL'de `rowid` yoktur; `ctid` fiziksel konumdur ve VACUUM ile değişir → **kullanılamaz**.

### Önerilen çözüm (SQLite davranışı korunur)
`SqlDialect`'e yeni bir yardımcı eklenir:

```
MovementTieBreaker(conn) => IsSqlite(conn) ? "sm.rowid" : "sm.id"
```

- **SQLite:** `sm.rowid` — mevcut davranış **birebir korunur** (ekleme sırası).
- **PostgreSQL:** `sm.id` — `stock_movements.id TEXT PRIMARY KEY` (doğrulandı,
  `Migration005_Materials.cs:108`). GUID olduğu için "ekleme sırası" vermez ama **deterministik ve
  kararlıdır**: aynı sorgu her zaman aynı sırayı döndürür, sayfalama/karşılaştırma tutarlı olur.

**Alternatif (önerilmiyor):** her iki lehçede de `sm.id` — daha basit ama SQLite'ta bugünkü
"en son eklenen en üstte" davranışını bozar; senin şartın "SQLite davranışı bozulmamalı" olduğu için
lehçe-ayrımlı çözüm seçildi.

**Not:** `src/DepoWise.Desktop/CompanySyncService.cs:162` de `rowid` kullanıyor — **dokunulmayacak**;
orası yalnız masaüstü (SQLite) kuyruğudur, PostgreSQL'e gitmez.

---

## 2. T-2 — `SetVehicles` yazma izolasyonu (tam zincir)

### Zincir
| Katman | Konum | Bulgu |
|---|---|---|
| Uç | `PUT /api/maintenance/definitions/{id}` — `Program.cs:1789-1796` | `id` doğrulanmadan servise geçiyor |
| API içi sıra | önce `Update(s, id, …)`, sonra `SetVehicles(s, id, d.VehicleIds)` | — |
| Servis A | `MaintenanceDefinitionService.Update` (`:98`) | ✅ `WHERE id=@id AND company_id=@c`; 0 satırda **`ForbiddenException`** |
| Servis B | `MaintenanceDefinitionService.SetVehicles` (`:165`) | ⚠️ yalnız `AccessControl.Require(Edit)`; **tanım ve araç sahipliği hiç doğrulanmıyor** |
| Sorgu B1 | `DELETE FROM maintenance_definition_vehicles WHERE definition_id=@d` | firma filtresi yok |
| Sorgu B2 | `INSERT INTO maintenance_definition_vehicles(definition_id, vehicle_id)` | iki id de doğrulanmıyor |

### Gerçek sömürülebilirlik (düzeltme)
- **T-2a — yabancı `defId`:** Mevcut uçta `Update` **önce** çalışıyor ve başka firmanın id'sinde
  `ForbiddenException` fırlatıyor → `SetVehicles` hiç çalışmıyor. Masaüstünde de aynı sıra var
  (`MaintenanceViewModel.cs:167-168`). → **bugün sömürülemez, ama servis savunmasız (latent).**
  Yeni bir uç/çağrı eklendiğinde gerçek açığa dönüşür.
- **T-2b — yabancı `vehicleIds`:** ⚠️ **GERÇEK ve sömürülebilir.** Kullanıcı **kendi** tanımını
  güncellerken gövdeye **başka firmanın araç id'lerini** koyabilir. `Update` başarılı olur,
  `SetVehicles` yabancı araçları bağlar (FK sağlanır, çünkü araç gerçekten var).
  **Aynı açık `Create`'te de var** (`MaintenanceDefinitionService.cs:60-67`) — orada da araç
  sahipliği doğrulanmıyor.
- **Etkisi sınırlı ama gerçek:** yabancı araç id'leri `GetVehicleIds` ile geri okunabilir (T-3).
  Bakım uyarıları etkilenmez — uyarı sorgusu firma filtreli
  (`MaintenanceService.cs:271` → `JOIN vehicles v ... AND v.company_id = @c` ✅).

### Kapatılacak en doğru katman: **SERVİS**
Gerekçe: aynı servis metodu hem API'den hem **masaüstünden doğrudan** çağrılıyor
(`MaintenanceViewModel.cs:168`). API katmanında kapatmak masaüstünü korumaz. Arayüz katmanı zaten
yeterli değil (senin şartın). Uygulanacak desen, projede **zaten var olan** yardımcılar:
`EnsureVehicleOwned` (`MaintenanceService.cs:518`), `TenantAccessGuard.EnsureOwnership`
(`AccessControl.cs:125`).

**Yapılacak:** `SetVehicles` ve `Create` içinde (a) tanımın firmasını doğrula, (b) her `vehicleId`
için araç sahipliğini doğrula; ihlalde `ForbiddenException`.

---

## 3. T-1, T-3, T-4, T-5, T-6 — tek tek

| # | Uç | Metod | Servis + satır | Sorgu | Eksik olan | Önerilen düzeltme | Sömürülebilir? |
|---|---|---|---|---|---|---|---|
| **T-1** | `/api/stock/balance/{materialId}` | GET | `StockService.GetBalance` `:214` | `SELECT quantity FROM stock_balances WHERE material_id=@m` | **`SessionContext` parametresi bile yok** | İmzaya `SessionContext` ekle + sorguya **`AND company_id=@c`** (`stock_balances`'ta kolon **var** — doğrulandı). ⚠️ `EnsureMaterialOwned` **kullanılMAMALI**: ek sorgu getirir ve aşağıdaki N+1'i ikiye katlar | ✅ **Evet** — malzeme id'si bilinen başka firmanın stok miktarı okunur |
| **T-3** | `/api/maintenance/definitions/{id}/vehicles` | GET | `MaintenanceDefinitionService.GetVehicleIds` `:151` | `SELECT vehicle_id FROM maintenance_definition_vehicles WHERE definition_id=@d` | Üst tanımın firması doğrulanmıyor | Tanım sahipliğini doğrula (T-2 ile aynı yardımcı) | ✅ **Evet** — başka firmanın araç id'leri okunur |
| **T-4** | `/api/requests/{id}/history` | GET | `RequestService.GetHistory` `:299` | `SELECT … FROM request_status_history WHERE request_id=@r` | **`SessionContext` parametresi yok** | İmzaya session ekle + `LoadStatus(s, requestId)` tenant guard'ı (aynı sınıfta `:341` `GetItems`'te kullanılan desen) | ✅ **Evet** — başka firmanın talep durum geçmişi okunur |
| **T-5** | `/api/request-ops/{id}/history` | GET | `RequestOperationsService.GetHistory` `:201` | aynı tablo, `WHERE h.request_id=@r AND h.kind='operation'` | Üst talebin firması doğrulanmıyor (yetki kontrolü var, tenant yok) | `LoadOperationStatus(s, requestId)` (`:225`, **zaten firma filtreli**) ile önce doğrula | ✅ **Evet** — operasyon geçmişi (kim/ne zaman/gerekçe/şube) okunur |
| **T-6** | `/api/users/{id}/roles` | GET | `UserService.GetRoleKeys` `:333` | `SELECT r.role_key FROM user_roles ur JOIN roles r … WHERE ur.user_id=@u` | `actor` alınıyor ama **hiç kullanılmıyor**; yetki kontrolü de yok | `AccessControl.Require(actor,"users",View)` + `EnsureUserOwned` deseni (`PermissionService.cs:234` zaten var) | ✅ **Evet** — başka firmanın kullanıcı rolleri okunur |

**Aynı problem başka uçlarda var mı?** T-1…T-6 için tarandı; bu altısı dışında **aynı uçlarda tekrar yok**.
Ancak genişletilmiş tarama başka yerlerde benzer desen buldu → §4.

### T-1'in gizli maliyeti: `GetBalance` 6 yerden çağrılıyor
| Çağrı yeri | Not |
|---|---|
| `Program.cs:1492` | T-1'in kendisi (`/api/stock/balance/{materialId}`) |
| `Program.cs:795` | `/api/materials` içinde **her malzeme için ayrı çağrı** — 200 malzeme = **200 sorgu (N+1)**. Sızıntı yok (malzemeler zaten firma filtreli listeden geliyor) ama **performans borcu**. |
| `DailyActivityViewModel.cs:431`, `MaintenanceViewModel.cs:481`, `StockCountViewModel.cs:86`, `StockEntryViewModel.cs:243` | Masaüstü — imza değişince derleyici zorlar |

**Sonuç:** imza `GetBalance(SessionContext s, string materialId)` olacak ve **6 çağrı yeri**
güncellenecek. Sahiplik kontrolü **ek sorguyla değil, aynı sorgudaki `AND company_id=@c` ile**
yapılacak → N+1 daha da kötüleşmez.

> **Yeni bulgu (pakete DAHİL DEĞİL):** `/api/materials` ucundaki N+1 (`Program.cs:793-798`).
> 200 malzemelik sayfada 200 ek sorgu. Tek `JOIN`/`IN` sorgusuyla çözülür. → **P3 (performans)**, Y-6.

---

## 4. GENİŞLETİLMİŞ TARAMA — YENİ BULGULAR (pakete DAHİL EDİLMEDİ)

Yöntem: `DepoWise.Infrastructure` içindeki tüm `public`/`internal` metodlar; SQL içeren + id ile
hedefleyen + firma filtresi/guard'ı görünmeyenler (26 aday) → elle ayıklandı.

| Kod | Yer | Bulgu | Sömürülebilir? | Sınıf |
|---|---|---|---|---|
| **Y-1** | `MaterialService.RemoveEquivalent` `:167` | `DELETE FROM material_equivalents WHERE …` — malzemelerin firması doğrulanmıyor. **`AddEquivalent` doğruluyor** (`:159-160` `EnsureOwned`) → asimetri | ✅ Evet (**YAZMA**) — başka firmanın muadil ilişkisi silinebilir | **P1** |
| **Y-2** | `MaintenanceDefinitionService.Create` `:60-67` | Araç sahipliği doğrulanmıyor (T-2b'nin ikizi) | ✅ Evet (**YAZMA**) | **P1** (T-2 ile birlikte kapanmalı) |
| **Y-3** | `VehicleTemplateService.GetMaterials` `:93` | `SessionContext` yok, firma filtresi yok | ⚠️ Hayır — **API ucu yok**, yalnız masaüstünden çağrılıyor (yerel tek-firma DB) → latent | **P2** |
| **Y-4** | `OpeningStockService.GetBalance` `:83` | Session alıyor ama kullanmıyor; `stock_balances` filtresiz | ⚠️ Hayır — **hiçbir yerden çağrılmıyor (ölü kod)** | **P3** (ölü kod temizliği) |
| **Y-5** | `BranchRepository.SoftDelete` `:49` | Firma bağlamı yok | ⚠️ Hayır — **çağıranı yok (ölü kod)** | **P3** |
| **Y-6** | `Program.cs:793-798` (`/api/materials`) | Her malzeme için ayrı `GetBalance` → **N+1** (200 malzeme = 200 sorgu) | — (güvenlik değil, **performans**) | **P3** |

**Yanlış alarm olduğu doğrulananlar:** `FileService.DeletePhoto` (`companyId != s.CompanyId` kontrolü
var ✅), `CompanyService.Update` (süper admin kısıtlı modül ✅), `UserListPreferenceService.*`
(kullanıcı tercihi, firma bağımsız — tablo zaten `company_id` taşımıyor), `StockBalanceWriter.*`
(iç yardımcı; çağıran firma doğruluyor), `AuthService.*` (kimlik doğrulama akışı, firma kavramından önce),
`UserService.ChangeOwnPassword` (kendi parolası), `SpecialCodeService.HasCode` / `CompanyService.GetName`
/ `CompanyPurgeService.FindName` (sistem düzeyi).

> **Senin talimatın gereği Y-1…Y-5 bu paketin kapsamına ALINMADI.** Y-1 ve Y-2 yazma açığı olduğu
> için P1'dir; istersen pakete eklenebilir (Y-2 zaten T-2 ile aynı dosyada, marjinal maliyet).

---

## 5. POSTGRESQL'DE ATLANAN TEST — KESİNLEŞTİ

### Hangi test atlandı? (trx raporuyla kanıtlandı)

```
[NotExecuted] DepoWise.Tests.PostgresConnectionTests.PostgreSQL_Sunucusuna_Baglanip_Surum_Okunabiliyor
toplam 30 · geçen 29 · başarısız 0
```

**Bu sonuç aşağıdaki kök nedeni birebir doğruluyor:** atlanan test, `[Collection]` taşımayan —
yani **paralel çalışan** — iki sınıftan biri olan `PostgresConnectionTests` içinde.
Diğer paralel sınıf (`PostgresTestGuardTests`) ise ortam değişkenlerini bozan sınıftır.
Koleksiyona bağlı 8 sınıfın **hiçbirinde** atlama olmadı.

### Sorulara cevaplar

**Gerçek bir problem mi?** **Evet — ama üründe değil, TEST ALTYAPISINDA.** Bir yarış koşulu (race
condition). Ürün kodu etkilenmiyor.

**Neden atlandı?** `PostgresTestGuardTests.WithEnv` (`:24-40`) **süreç geneli ortam değişkenlerini**
geçici olarak değiştiriyor:
```
Environment.SetEnvironmentVariable("DEPOWISE_PG_URL", url);              // null veya "postgres://sahte/deneme"
Environment.SetEnvironmentVariable(PostgresTestGuard.ConfirmVar, confirm); // null veya "evet"
```
`PostgresTestGuard.SkipReason()` **tam da bu iki değişkeni** okuyor (`PostgresTestGuard.cs:80-87`).

**Neden yarış oluşuyor?** xUnit farklı **koleksiyonları paralel** çalıştırır:

| Test sınıfı | `[Collection("PostgresSchema")]` | Sonuç |
|---|---|---|
| `PostgresCompanyIdMigration`, `DataCopy`, `EndToEnd`, `Migration`, `Purge`, `ServerHealth`, `StockConcurrency`, `SyncRecovery`, `TurkishSearch` | ✅ var | kendi aralarında **seri** |
| **`PostgresTestGuardTests`** | ❌ **YOK** | **paralel** — env'i bozan sınıf |
| **`PostgresConnectionTests`** | ❌ **YOK** | **paralel** — env'e bağımlı kurban |

`PostgresTestGuardTests` env'i "sahte/boş" değerlere çevirdiği **milisaniyeler içinde**, paralel
çalışan başka bir PG testi `SkipUnlessSafe()` çağırırsa `SkipReason()` bozulmuş env'i görür ve
test **atlanır**. `finally` bloğu env'i geri koyar — ama iş işten geçmiştir.

**Önceki koşuda neden geçiyordu?** Zamanlama. Yarış koşulları çalıştırmadan çalıştırmaya değişir;
30/30 ile 29/1 arasındaki fark **tam olarak budur**. Yani test **flaky**.

**Test altyapısı değişti mi?** ❌ Hayır. `PostgresTestGuardTests` ve `PostgresConnectionTests`
bu oturumda **hiç değiştirilmedi**. Kusur **baştan beri** vardı; yalnız bugüne kadar tetiklenmemişti.

**Tekrar çalıştırıldığında geçiyor mu?** **Hayır — bu oturumda İKİ ardışık koşuda da atlandı**
(29/1 ve 29/1), üçüncü bir daha önceki koşuda ise 30/30 geçmişti. Yani kararsız (flaky):
yeniden çalıştırmak **çözmüyor**, kalıcı düzeltme gerekiyor.

**Elenen alternatif hipotezler (ölçülerek):** test veritabanı `depowise_test` = **14,3 MB**
(guard sınırı 50 MB → aşılmadı), `dw_test_marker` şeması **var**, replica **değil**, ad "test"
içeriyor → guard'ın **hiçbir** kalıcı ölçütü ihlal edilmiyor. Yani atlama nedeni veritabanının
durumu **değil**, env'in o anki değeri.

### Önerilen çözüm (pakete DAHİL)
**A (tercih edilen):** `PostgresTestGuardTests`'e `[Collection("PostgresSchema")]` ekle → env'i
bozan sınıf diğer PG testleriyle **seri** çalışır, yarış biter. **Tek satır**, davranış değişmez.
*(Not: `PostgresConnectionTests`'in de aynı koleksiyona alınması tutarlılık sağlar.)*

**B (daha temiz, daha kapsamlı):** `SkipReason()`/`AssertSafeTestDatabase` için env okumayan saf
overload (`SkipReason(string? url, string? confirm)`) ekle; guard testleri env'e hiç dokunmadan
o overload'ı test etsin. Ürün davranışı değişmez.

Plan: **A uygulanacak** (asgari, riski en düşük); B ayrı iyileştirme olarak not edilir.

---

## 6. API ÇOK-FİRMALI TEST PAKETİ (tasarım)

### Ne ile koşacak: **gerçek HTTP hattı**
`Microsoft.AspNetCore.Mvc.Testing` + `WebApplicationFactory<Program>` ile API **kendi sürecinde
bellek-içi** ayağa kalkar; testler `HttpClient` ile **gerçek uçlara** istek atar. Böylece
kimlik doğrulama (JWT), yetkilendirme, model bağlama ve hata yönetimi dahil **tüm hat** kapsanır.

**Gerekli altyapı değişiklikleri:**
1. `tests/DepoWise.Tests.csproj` → `Microsoft.AspNetCore.Mvc.Testing` paketi (API'ye ProjectReference **zaten var**).
2. `src/DepoWise.Api/Program.cs` sonuna `public partial class Program { }` — top-level statements
   kullanan bir uygulamayı `WebApplicationFactory` ile ayağa kaldırmanın standart yolu. **Davranış değişmez.**
3. Test fabrikası: `DEPOWISE_SERVER_DATA` → geçici klasör, `DEPOWISE_JWT_KEY` → test anahtarı,
   `DEPOWISE_PG_URL` → **tanımsız** (SQLite) veya PostgreSQL varyantında tanımlı.

**Servis düzeyi testin gerekli/uygun olduğu yerler (ayrıca yazılacak):**
- **T-2b / Y-2 (yabancı araç bağlama):** API testi de yazılabilir, ancak asıl koruma servis
  katmanında olduğu için **servis düzeyi test zorunlu** — masaüstü aynı metodu doğrudan çağırıyor.
- **`RecentForMaterial` sıralaması (KD-1):** HTTP üzerinden sıra doğrulanabilir ama lehçe farkını
  görmek için **servis düzeyi + iki lehçe** testi daha net.
- **Masaüstü yolu:** HTTP hattı masaüstünü kapsamaz; servis testleri onu temsil eder.

### Senaryo matrisi
| # | Senaryo | Beklenen |
|---|---|---|
| S1 | Firma A token'ı → A'nın kaydı (GET) | **200 + veri** |
| S2 | Firma A token'ı → B'nin kaydı (GET) | **403/404 — veri DÖNMEMELİ** |
| S3 | Firma A token'ı → B'nin kaydı (PUT/POST — yazma) | **403/404 + B'nin verisi DEĞİŞMEMİŞ** |
| S4 | Firma A token'ı → B'nin **child** kaydı (talep kalemi, bakım malzemesi, geçmiş) | **403/404** |
| S5 | Gövdede yabancı id (kendi kaydına B'nin aracını bağlama) | **403 — bağlantı OLUŞMAMALI** |
| S6 | Kimlik doğrulamasız aynı uçlar | **401** |
| S7 | Yetkisi olmayan kullanıcı, kendi firmasının kaydı | **403** (yetki ile tenant ayrımı korunuyor mu) |
| S8 | Her düzeltilen uç için A→A **regresyon** | **200** (düzeltme meşru kullanımı bozmadı) |

### Kapsanacak uçlar (en az)
`GET /api/stock/balance/{materialId}` · `GET /api/maintenance/definitions/{id}/vehicles` ·
`PUT /api/maintenance/definitions/{id}` (gövdede yabancı araç) · `GET /api/requests/{id}/history` ·
`GET /api/request-ops/{id}/history` · `GET /api/users/{id}/roles` ·
+ **kontrol grubu** (zaten korumalı olduğu doğrulananlar, regresyon için):
`DELETE /api/materials/{id}`, `POST /api/requests/{id}/approve`, `GET /api/permissions/{userId}`

### Lehçe kapsamı
- **SQLite:** varsayılan koşu (her CI/yerel çalıştırmada).
- **PostgreSQL:** aynı test sınıfı `[SkippableFact]` + `PostgresTestGuard` ile; yalnız doğrulanmış
  **boş test veritabanında** koşar. **KD-1 için kritik** — `rowid` hatası ancak PostgreSQL'de görünür.

---

## 7. DEĞİŞECEK DOSYALAR (önceden liste)

### Kaynak kod (5 dosya)
| Dosya | Değişiklik | Yaklaşık kapsam |
|---|---|---|
| `src/DepoWise.Infrastructure/Database/SqlDialect.cs` | Yeni `MovementTieBreaker(conn)` yardımcısı | +6 satır |
| `src/DepoWise.Infrastructure/Materials/StockService.cs` | 2 sıralama ifadesi lehçe-duyarlı (`:246`, `:284`); `GetBalance` imzasına session + sahiplik (T-1) | ~12 satır |
| `src/DepoWise.Infrastructure/Maintenance/MaintenanceDefinitionService.cs` | `SetVehicles` + `Create` + `GetVehicleIds`: tanım ve araç sahipliği (T-2, T-3) | ~25 satır |
| `src/DepoWise.Infrastructure/Requests/RequestService.cs` | `GetHistory` imzasına session + tenant guard (T-4) | ~6 satır |
| `src/DepoWise.Infrastructure/Requests/RequestOperationsService.cs` | `GetHistory` tenant doğrulaması (T-5) | ~4 satır |
| `src/DepoWise.Infrastructure/Security/UserService.cs` | `GetRoleKeys`: yetki + sahiplik (T-6) | ~5 satır |

### API (1 dosya)
| Dosya | Değişiklik |
|---|---|
| `src/DepoWise.Api/Program.cs` | `/api/stock/balance/{materialId}` ve `/api/requests/{id}/history` çağrılarına session geçir (imza değişikliği gereği); sona `public partial class Program { }` |

### Çağrı yerleri (imza değişikliğinden etkilenen — **taranarak kesinleştirildi**)
| Dosya | Neden | Satır |
|---|---|---|
| `src/DepoWise.Desktop/ViewModels/DailyActivityViewModel.cs` | `Stock.GetBalance` | 431 |
| `src/DepoWise.Desktop/ViewModels/MaintenanceViewModel.cs` | `Stock.GetBalance` | 481 |
| `src/DepoWise.Desktop/ViewModels/StockCountViewModel.cs` | `Stock.GetBalance` | 86 |
| `src/DepoWise.Desktop/ViewModels/StockEntryViewModel.cs` | `Stock.GetBalance` | 243 |
| `src/DepoWise.Desktop/ViewModels/RequestsViewModel.cs` | `Requests.GetHistory` | 153 |
| `src/DepoWise.Api/Program.cs` | `GetBalance` ×2 (795, 1492), `GetHistory` ×1 (2075) | — |
| `src/DepoWise.Web/Components/Pages/*` | **Değişmeyecek** — web API'yi HTTP ile çağırıyor, servis imzası görmüyor (doğrulandı) | — |

`GetRoleKeys` (T-6) ve `GetVehicleIds` (T-3) **zaten `SessionContext` alıyor** → imza değişmez,
yalnız gövdeye kontrol eklenir; çağrı yerleri etkilenmez.

### Test (3 yeni + 1 proje dosyası)
| Dosya | İçerik |
|---|---|
| `tests/DepoWise.Tests/ApiMultiCompanyTests.cs` (**yeni**) | S1–S8, gerçek HTTP hattı, SQLite |
| `tests/DepoWise.Tests/PostgresApiMultiCompanyTests.cs` (**yeni**) | aynı matris, PostgreSQL (`PostgresTestGuard`) |
| `tests/DepoWise.Tests/StockMovementOrderingTests.cs` (**yeni**) | KD-1: iki lehçede deterministik sıralama + regresyon |
| `tests/DepoWise.Tests/DepoWise.Tests.csproj` | `Microsoft.AspNetCore.Mvc.Testing` paketi |
| `tests/DepoWise.Tests/PostgresTestGuardTests.cs` | **+1 satır:** `[Collection("PostgresSchema")]` — flaky atlama düzeltmesi (§5) |
| `tests/DepoWise.Tests/PostgresConnectionTests.cs` | **+1 satır:** aynı koleksiyon (tutarlılık) |

**Toplam: 8 kaynak/test dosyası + 1 proje dosyası. Migration YOK.**

---

## 8. ÖZET TABLO

| İş | Dosyalar | Migration | Canlı veri etkisi | Testler | Risk |
|---|---|---|---|---|---|
| **KD-1** sıralama düzeltmesi | `SqlDialect.cs`, `StockService.cs` | ❌ | ❌ yok (yalnız SELECT) | `StockMovementOrderingTests` (SQLite+PG) | **Düşük** |
| **T-1** stok bakiyesi | `StockService.cs`, `Program.cs` (+ masaüstü çağrısı) | ❌ | ❌ | API testi S2 + regresyon S1 | **Düşük** |
| **T-2 + T-3** bakım tanımı/araç | `MaintenanceDefinitionService.cs` | ❌ | ❌ | API S3/S5 + **servis testi** | **Düşük-Orta** (masaüstü akışına dokunur) |
| **T-4** talep geçmişi | `RequestService.cs`, `Program.cs` | ❌ | ❌ | API S4 | **Düşük** |
| **T-5** operasyon geçmişi | `RequestOperationsService.cs` | ❌ | ❌ | API S4 | **Düşük** |
| **T-6** kullanıcı rolleri | `UserService.cs` | ❌ | ❌ | API S2 + S7 | **Düşük** |
| **API test paketi** | 3 yeni test + csproj + `Program.cs` partial | ❌ | ❌ | kendisi test | **Düşük** |
| **PG flaky atlama** | `PostgresTestGuardTests.cs`, `PostgresConnectionTests.cs` (+1'er satır) | ❌ | ❌ | tekrarlı PG koşusu | **Düşük** |

**Paketin tamamı: migration YOK · canlı veriye yazma YOK · şema değişikliği YOK.**
Yayınlanması için yalnız normal deploy onayın gerekir.

---

## 9. KAPSAM DIŞI BIRAKILANLAR

| # | İş | Neden dışarıda |
|---|---|---|
| D-1 | `CLAUDE.md` PostgreSQL/SQLite bilgisinin düzeltilmesi | **Ayrı küçük doküman işi** (senin talimatın) — 2 satırlık düzeltme, kod etkisi yok |
| D-2 | Y-1 `RemoveEquivalent` yazma açığı | Yeni bulgu — **P1**, onayınla ayrı ele alınacak |
| D-3 | Y-2 `Create` yabancı araç | Yeni bulgu — **P1**; T-2 ile aynı dosyada olduğu için istersen pakete eklenebilir |
| D-4 | Y-3 `VehicleTemplateService.GetMaterials` | Yeni bulgu — **P2**, latent (API ucu yok) |
| D-5 | Y-4 / Y-5 ölü kod | Yeni bulgu — **P3** temizlik |
| D-6 | M-S1b, M-S1d, düzenleme altyapısı, Excel→Web vb. | Önceki raporun P1-P3'ü, bu pakette değil |

---

## 10. UYGULAMA SIRASI (onay verilirse)

1. **PG'de atlanan test kesinleştirilir** (§5) — belirsizlikle başlanmaz.
2. Test altyapısı: `Mvc.Testing` paketi + `Program` partial + fabrika. **Önce testler kırmızı**
   (T-1…T-6 açıkları kanıtlanır, KD-1 PostgreSQL'de 500 verir).
3. KD-1 düzeltmesi → PG testi yeşile döner.
4. T-1, T-3, T-4, T-5, T-6 düzeltmeleri (küçük, bağımsız).
5. T-2 düzeltmesi (servis katmanı, masaüstü akışı doğrulanır).
6. Tüm takım + PostgreSQL takımı çalıştırılır; **hiçbir test yeşil görünsün diye değiştirilmez**.
7. Build + web/masaüstü doğrulama → **deploy onayın istenir**.
