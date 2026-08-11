# AKTİF DURUM

> Son güncelleme: **2026-08-11** · Bu dosya **her iş sonunda** güncellenir.

---

## 🔵 MEVCUT FAZ: **FAZ C — Depo bazlı stok altyapısı**

**KARAR-7 = A** (malzeme kartı firma geneli, stok depo bazlı) — 2026-08-11 kesinleşti.
Tasarım: [`FAZ_C_DEPO_BAZLI_STOK_TASARIM.md`](FAZ_C_DEPO_BAZLI_STOK_TASARIM.md)

## ✅ SON TAMAMLANAN
**`STK-00` — Migration güvenlik kanıtı.** Gerçek production yedeğinin izole kopyası üzerinde,
defterden lokasyon bazlı bakiye üretiminin **toplamları koruduğu kanıtlandı**:

| Ölçüm | Sonuç |
|---|---|
| Mevcut bakiye satırı | 664 |
| Lokasyona bölünmüş satır | 665 |
| **Toplamı uyuşmayan malzeme** | **0** ✅ |
| Defterde olmayan / bakiyede olmayan | 0 / 0 ✅ |
| Migration'ın ürettiği YENİ negatif | **1** (66 negatif zaten bugün de var) |

→ Migration **kayıpsız ve deterministik**. Veri uydurma gerekmiyor.

## 🟡 `STK-01` — MIGRATION YAZILDI, **ETKİNLEŞTİRİLMEDİ** (2026-08-11)

`Migration064_StockBalanceLocation` yazıldı: `(company_id, material_id, location_id)` PK ·
defterden C#/decimal ile yeniden hesaplama (SQL CAST hassasiyeti bozmasın) ·
**migration içi doğrulama** (malzeme toplamı eşleşmezse istisna → runner transaction'ı geri alır) ·
`location_id=''` = ATANMAMIŞ · tablo yeniden kurma (iki lehçede de çalışır) · lokasyon indeksi.

⚠️ **`MigrationCatalog`'a KAYITLI DEĞİL — bilinçli.** Şema değişince `stock_balances` malzeme başına
çok satır döner ve bugünkü **15 üretim çağrı noktası** eski tek-satır varsayımına dayanıyor:

| Sorun | Adet | Etkisi |
|---|---|---|
| `SELECT quantity ... WHERE material_id=@m` (`ExecuteScalar`) | 4 | **İlk** lokasyonu alır, toplamı değil |
| `LEFT JOIN stock_balances` | 8 | Satır çoğaltır → malzeme listesi / rapor / dashboard **yanlış** |
| CAS yazma (`ON CONFLICT(material_id)`) | 3 | Yazma hedefi belirsiz |
| Eski şemayla satır yazan test dosyası | 5 | Test kırılır |

Tek başına etkinleştirilirse stok değerleri **sessizce yanlış** görünür — en tehlikeli hata türü.
Bu yüzden etkinleştirme `STK-02` ile **aynı iş biriminde** yapılacaktır.

## 🟡 `STK-02` — ENVANTER + PLAN HAZIR, **KOD DEĞİŞİKLİĞİ BAŞLAMADI**

Tam repo taraması yapıldı. Plan: [`STK_02_UYGULAMA_PLANI.md`](STK_02_UYGULAMA_PLANI.md)

**Gerçek envanter: 16 üretim noktası** (tahmin 15'ti; `StockService:279` toplu okuma ayrı çıktı):
4 yazma (CAS + recompute) · 3 skaler okuma · 1 toplu okuma · **8 JOIN** (satır çoğaltma riski).

✅ **De-risk bulgusu — senkron 0 değişiklik gerektiriyor:** `DbIntrospect.PrimaryKey` PK kolonlarını
sırayla okuyor ve `BusinessSyncService:571` `conflictTarget`'ı ondan kuruyor →
`ON CONFLICT(company_id, material_id, location_id)` **otomatik** üretilecek. Generic upsert bileşik
PK'yi zaten destekliyor. `stock_movements` şeması değişmediği için push/pull/idempotency aynen çalışır.

⚠️ **Kod değişikliği bilinçli olarak başlatılmadı.** STK-02 **atomiktir**: writer değişip JOIN'ler
değişmemiş bir ara durum hem 1206 testi kırar hem de stok değerlerini **sessizce yanlış** gösterir.
Tek oturumda kesintisiz yapılmalı.

## 🟠 STK-02 KOD DENEMESİ — YAZMA YOLLARI BİTTİ, GERİ ALINDI (2026-08-11)

Adım 1-2 (yazma yolları) **tamamlandı ve derlendi**, ancak 8 JOIN'e sıra gelmeden oturum kapasitesi
doldu. Yarım hâlde **çalışma ağacı tutarsızdı** (yazma yolları `location_id` bekliyor, migration inert
→ çalışma zamanı SQL hatası). Bu yüzden `src/` son sağlam commit'e **geri alındı**.

**Yapılan iş KAYBOLMADI** → [`STK_02_YAZMA_YOLLARI.patch`](STK_02_YAZMA_YOLLARI.patch) (372 satır).
Uygulamak için: `git apply docs/project-control/STK_02_YAZMA_YOLLARI.patch`

### Yamanın içeriği (bitmiş kısım)
| Dosya | Ne yapıldı |
|---|---|
| `StockBalanceWriter` | `Unassigned` sabiti · `ApplyDelta`/`ReadRaw`/`ReadBalance` **zorunlu `locationId`** (varsayılan YOK — çağıran bilinçli seçsin) · CAS `ON CONFLICT(company_id, material_id, location_id)` · yeni `ReadTotal` (C#'ta decimal toplama) |
| `StockService` | `GetBalance` = firma toplamı · **yeni** `GetBalanceAt` (lokasyon) · **yeni** `GetBalancesByLocation` · `GetBalances` C#'ta toplama (N+1 yok) · `ApplyLine` bakiyeyi `branchId`'ye yazıyor · ters kayıt **orijinal lokasyona** · sayım **sayılan lokasyonla** karşılaştırıyor · `RecomputeBalances` (malzeme+lokasyon) + hayalet satır temizliği |
| `OpeningStockService` | Bakiye açılış hareketinin lokasyonuna |
| `MaintenanceService` | Tüketim hareketi `branch_id=NULL` yazdığı için bakiye de ATANMAMIŞ'a (defterle birebir) |
| `SqlDialect` | **yeni** `StockTotalSubquery(conn)` — JOIN'lerde satır çoğaltmayı önleyen, lehçe-güvenli toplayan alt sorgu |

### Kalan (sıradaki oturumun işi)
1. Yamayı uygula (`git apply`)
2. **8 JOIN'i dönüştür** — `StockTotalSubquery` ile: `MaterialService` :300/:332/:621 · `DashboardService` :146/:177 · `ReportService` :34/:62/:92
3. `OpeningStockService.GetBalance` (:87) → toplam
4. `MaterialService` :482 → toplam
5. 5 test dosyası + 16 yeni senaryo
6. `MigrationCatalog`'a **Migration064**'ü ekle
7. Tam test → SQLite doğrulaması → izole PostgreSQL provası

## ▶️ SIRADAKİ İŞ
**`STK-02` — yamayı uygula, 8 JOIN'i `SqlDialect.StockTotalSubquery` ile dönüştür, testleri güncelle,
Migration064'ü etkinleştir, doğrula.**
**Kural:** Genel toplam ile lokasyon toplamı asla kopmayacak · `DISTINCT` ile düzeltme yasak ·
lokasyon bilinmiyorsa `''` (ATANMAMIŞ), asla rastgele şube · yarım bırakma (tutarsız ağaç).

## ⛔ Karar bekleyenler
| İş | Neyi bekliyor |
|---|---|
| `STK-08` | **KARAR-8** — "Atanmamış" stok nasıl dağıtılacak (öneri: kullanıcı transferle) |
| `BKM-01…03` | KARAR-4 (bakımda negatif stok mu, onay kapısı mı) |
| `TMZ-02`, `BRM-01`, `YTK-01…04` | YET-01 (rol değişince yetkiler) |
| `SNK-05` | Çevrimdışı onay çakışması |

## 📌 Canlı ortam
API `depowise-erp` v149 · Web `depowise-web` v175 · Neon PG **17.10** · şema **63** ·
3 firma · 8 kullanıcı · 6 lokasyon (1 şube + 5 şantiye) · 2461 malzeme · **667 stok hareketi** ·
disk %31 · Git `5813424` senkron · Test 1206/1173/0/33 · Build 0 hata

## ⚠️ Açık riskler
- Migration sonrası stoğun **neredeyse tamamı "ATANMAMIŞ"** görünecek (666/667 hareket lokasyonsuz) → KARAR-8.
- Masaüstü **paketi yayınlanmadı** — Grup 6 masaüstü düzeltmeleri kullanıcıya ulaşmadı.
- Branch **`master`'a birleştirilmedi** (33 commit feature dalında).
- **66 malzemede negatif stok** zaten mevcut (ADR-086 devralınan eksik stok) — migration bunu değiştirmiyor.
