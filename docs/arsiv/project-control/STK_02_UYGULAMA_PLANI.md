# STK-02 — `stock_balances` Lokasyon Farkındalığı: Uygulama Planı

> Tarih: **2026-08-11** · Faz: **FAZ C** · Ön koşul: `STK-01` (Migration064 yazıldı, **inert**)
> Bu belge **koddan doğrulanmış envanterdir**. Uygulama bu sıraya göre yapılacaktır.
> ⚠️ **STK-02 atomiktir:** çağrı noktaları + Migration064 etkinleştirme **aynı iş biriminde** iner.
> Yarısı inerse stok değerleri sessizce yanlış görünür.

---

## 1. Tam envanter (repo geneli tarama sonucu)

Önceki tahmin 15'ti; gerçek sayı **16 üretim noktası**dır (`StockService:279` toplu okuma ayrı sayıldı).
Ayrıca **sync 0 değişiklik** gerektiriyor (§3).

### A) YAZMA — CAS ve recompute (4 nokta)

| # | Yer | Bugün | Yeni davranış |
|---|---|---|---|
| 1 | `StockBalanceWriter.cs:86` | `INSERT ... ON CONFLICT(material_id) DO NOTHING` | `ON CONFLICT(company_id, material_id, location_id)` |
| 2 | `StockBalanceWriter.cs:94` | `UPDATE ... WHERE material_id=@m AND quantity=@expected` | `WHERE company_id=@c AND material_id=@m AND location_id=@l AND quantity=@expected` |
| 3 | `StockBalanceWriter.cs:117` (`ReadRaw`) | `WHERE material_id=@m` | `WHERE company_id=@c AND material_id=@m AND location_id=@l` |
| 4 | `StockService.cs:519` (recompute yazma) | `ON CONFLICT(material_id)` | Lokasyon bazlı grupla + `ON CONFLICT(company_id, material_id, location_id)` |

> `ApplyDelta` imzasına `locationId` eklenecek. **Çağıranlar hangi lokasyonu yazacağını
> ZORUNLU olarak vermeli** — varsayılan parametre KOYULMAYACAK, aksi halde sessizce ATANMAMIŞ'a yazılır.
> Lokasyon gerçekten bilinmiyorsa çağıran açıkça `""` geçer (ATANMAMIŞ). Rastgele şubeye **asla** yazılmaz.

### B) OKUMA — tek satır varsayan skaler sorgular (3 nokta)

| # | Yer | İş amacı (§3 sınıfı) | Yeni davranış |
|---|---|---|---|
| 5 | `StockService.cs:246` `Balance()` | **A — genel toplam** | `SELECT SUM(...)`; ayrıca yeni `BalanceAt(material, location)` eklenecek (**B**) |
| 6 | `MaterialService.cs:482` | **A — malzeme kartı toplam stok** | `SUM` |
| 7 | `OpeningStockService.cs:87` | **A — açılış öncesi mevcut toplam kontrolü** | `SUM` (açılışın hangi lokasyona yazılacağı ayrı: §2) |

> Toplama **SQL'de değil**: `quantity` TEXT içinde decimal. SQLite'ta `SUM(CAST(... AS REAL))`
> kayan nokta hatası üretir (`Money` kuralı: float kullanılmaz). Bu yüzden satırlar okunup
> **C#'ta `Money.Parse` ile decimal toplanacak** — `StockService`'in mevcut recompute deseninin aynısı.

### C) OKUMA — toplu (1 nokta)

| # | Yer | Yeni davranış |
|---|---|---|
| 8 | `StockService.cs:279` `BalancesFor()` | `material_id IN (...)` sorgusu artık N×lokasyon satır döner → **C#'ta malzeme bazında toplanacak** (tek sorgu korunur, N+1 üretilmez) |

### D) JOIN — satır çoğaltma riski (8 nokta) ⚠️ **en tehlikeli grup**

| # | Yer | Ekranın amacı | Yeni davranış |
|---|---|---|---|
| 9 | `MaterialService.cs:300` | Malzeme listesi — **bir malzeme = bir satır** | Toplayan alt sorgu (`GROUP BY material_id`) ile JOIN |
| 10 | `MaterialService.cs:332` | Malzeme grid (filtre/sıralama/sayfalama) | Aynı — **sayfalama ve COUNT bozulmamalı** |
| 11 | `MaterialService.cs:621` | Malzeme detay/liste | Aynı |
| 12 | `DashboardService.cs:146` | Kart metriği | Aynı — **çift sayım olmamalı** |
| 13 | `DashboardService.cs:177` | Kart metriği | Aynı |
| 14 | `ReportService.cs:34` | Stok raporu | Aynı |
| 15 | `ReportService.cs:62` | Rapor | Aynı |
| 16 | `ReportService.cs:92` | Rapor | Aynı |

> **`DISTINCT` ile düzeltme YASAK** — gerçek modelleme hatasını gizler (kullanıcı talimatı §5).
> Doğru yöntem: toplayan alt sorgu. Alt sorgudaki toplama da precision nedeniyle dikkat ister;
> **sıralama/filtre için** SQL toplaması kabul edilebilir, **gösterilen değer** C# tarafında
> `Money.Parse` ile üretilecekse tutarlılık korunur. Her JOIN için karar ayrı verilecek.

---

## 2. Açılış stoğu (OpeningStock) — lokasyon kararı

Bugün açılış lokasyonsuz yazılıyor (üretimde 664 açılış hareketi, hepsi `branch_id` NULL).
STK-02'de davranış: **çağıran lokasyon verirse ona, vermezse ATANMAMIŞ**. Kullanıcıdan lokasyon
istenmesi bir **UX gereksinimidir** → `STK-03` (API) ve `STK-04`/`STK-05` (Web/Desktop) görevlerine aktarıldı.
STK-02 bunu zorunlu hale getirmez (mevcut iş kuralı değişmez).

---

## 3. Senkronizasyon — **DEĞİŞİKLİK GEREKMİYOR** ✅

Koddan doğrulandı:
- `BusinessSyncService:340` → `PrimaryKey(conn, table)` → `DbIntrospect.PrimaryKey`
- `DbIntrospect.PrimaryKey` PK kolonlarını **sırayla** okur (SQLite `PRAGMA table_info` pk indeksi;
  PostgreSQL `information_schema`)
- `BusinessSyncService:571` → `conflictTarget = string.Join(", ", pk)`

Yani yeni bileşik PK ile üretilecek ifade **otomatik olarak**
`ON CONFLICT(company_id, material_id, location_id)` olur. **Generic upsert bileşik PK'yi zaten destekliyor.**

Tercih edilen model (`movement → sync → local recompute`) korunuyor; `stock_balances` senkron
kapsamında kalmaya devam eder (mevcut mimari), ek bir veri kaynağı hâline getirilmez.
`stock_movements` şeması **değişmiyor** → push/pull/idempotency aynen çalışır.

---

## 4. Testler

**Güncellenecek (eski şemayla satır yazan 5 dosya):**
`BusinessSyncTests` (:233, :300, :416) · `ImportFullFieldsTests` (:407) · `StockConcurrencyTests` (:63,:72,:90,:97) ·
`StockOperationTests` (:52) · `PostgresPurgeTests` (:129 — yalnız tablo adı listesi, değişiklik gerekmeyebilir)

**Eklenecek yeni senaryolar (kullanıcı §18):**
1. Malzeme A + Lokasyon 1 = 100
2. İki lokasyon → genel toplam 150
3. Transfer 25 → 75 / 75, genel 150 **değişmez**
4. Aynı transfer tekrar → **stok değişmez** (idempotency)
5. ATANMAMIŞ 100 + Lokasyon 50 → genel 150
6. Malzeme listesi **satır çoğaltmıyor** (1 malzeme = 1 satır)
7. Dashboard toplamı doğru
8. Rapor toplamı doğru
9. Offline işlem sonrası yerel bakiye doğru
10. Sync sonrası Web ve Desktop aynı sonucu üretiyor

---

## 5. Uygulama sırası (atomik blok)

```
1. StockBalanceWriter (imzaya locationId; CAS anahtarı)
2. StockService  — ApplyLine çağrıları, Balance (SUM), BalanceAt (yeni), BalancesFor (C# toplama), recompute (lokasyonlu)
3. MaterialService (3 JOIN + 1 skaler)
4. DashboardService (2 JOIN)
5. ReportService (3 JOIN)
6. OpeningStockService (1 skaler)
7. 5 test dosyası + 10 yeni senaryo
8. MigrationCatalog'a Migration064 EKLE           ← ancak 1-7 bittikten sonra
9. Tam test (hedef: 1206+ yeşil, 0 kırmızı)
10. SQLite doğrulaması
11. İzole PostgreSQL provası (production kopyası): migration → toplam/lokasyon/ATANMAMIŞ/negatif karşılaştırması
```

### Etkinleştirme ön koşulları (hepsi ✅ olmadan katalog kaydı YAPILMAZ)
- [ ] StockBalanceWriter · StockService · MaterialService · Dashboard · Report · OpeningStock doğru
- [ ] Hiçbir aktif üretim kodunda "material başına tek satır" varsayımı kalmadı
- [ ] Testler yeşil (mevcut 1206 korunuyor)
- [ ] SQLite doğrulandı
- [ ] İzole PostgreSQL provası başarılı
- [ ] Migration sonrası toplamlar eşleşiyor (beklenen: 667 hareket · 666 lokasyonsuz · 8953 ATANMAMIŞ · 66 mevcut negatif · +1 yeni negatif)
- [ ] Hareket defteri ile bakiye tutarlı

---

## 6. Durum

| Adım | Durum |
|---|---|
| Envanter + sınıflandırma | ✅ **TAMAM** (bu belge) |
| Sync etkisi analizi | ✅ **TAMAM** — değişiklik gerekmiyor |
| Kod değişiklikleri (1-7) | ⬜ **BAŞLANMADI** |
| Migration etkinleştirme (8) | ⬜ Ön koşullar bekliyor |
| Doğrulama (9-11) | ⬜ |

> **Not:** Kod değişiklikleri bilinçli olarak başlatılmadı. STK-02 atomiktir; yarım bırakılan bir
> düzenleme (örn. writer değişip JOIN'ler değişmemiş hâli) hem testleri kırar hem de yanlış stok
> gösterir. Bu belge, işin tek oturumda kesintisiz yapılabilmesi için hazırlanmıştır.
