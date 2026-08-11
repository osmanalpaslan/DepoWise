# AKTİF DURUM

> Son güncelleme: **2026-08-11** · Bu dosya **her iş sonunda** güncellenir.

---

## 🔵 MEVCUT FAZ: **FAZ C — Depo bazlı stok altyapısı**

**KARAR-7 = A** (malzeme kartı firma geneli, stok depo bazlı) — 2026-08-11 kesinleşti.
Tasarım: [`FAZ_C_DEPO_BAZLI_STOK_TASARIM.md`](FAZ_C_DEPO_BAZLI_STOK_TASARIM.md)

## ✅ SON TAMAMLANAN — `STK-01` + `STK-02` (tek iş birimi, 2026-08-11)

**Stok bakiyesi artık depo bazlı.** `stock_balances` anahtarı `(company_id, material_id, location_id)`
oldu; `location_id=''` = **ATANMAMIŞ**. Transfer bundan böyle bakiyede **görünür** (eskiden tek havuzda
toplandığı için görünmüyordu).

Migration ve kod **aynı iş biriminde** verildi — bilinçli: yalnız migration açılsaydı stok değerleri
**sessizce yanlış** görünürdü (en tehlikeli hata türü).

### Dönüştürülen 16 üretim noktası
| Tür | Adet | Ne yapıldı |
|---|---|---|
| CAS yazma | 4 | `ON CONFLICT(company_id, material_id, location_id)` · `locationId` **zorunlu parametre** (varsayılan yok — çağıran bilinçli seçsin) |
| Skaler + toplu okuma | 4 | `StockBalanceWriter.ReadTotal` / C#'ta `decimal` toplama (SQL `SUM` **yok** — SQLite'ta float hatası verir) |
| JOIN | 8 | `SqlDialect.StockTotalSubquery` → malzeme başına **tek satır**. `DISTINCT` ile gizleme **yok** |

**Yanında bulunan gerçek hata:** sayım, sistem miktarını **firma genelinden** okuyup düzeltmeyi
**şubeye** yazıyordu → "genelden oku, lokasyona yaz" tutarsızlığı. Düzeltildi ve test edildi.

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Tam test takımı | **1223 / 1190 geçti / 0 kaldı / 33 atlandı** (taban 1206'ydı; **17 yeni senaryo**) |
| Çözüm derlemesi | **0 hata** |
| İzole PG provası (üretim yedeğinin kopyası) | 667 hareket · 664 → **665** bakiye satırı · **uyuşmayan 0** · toplam korundu (8952,3) · süre 173 ms |
| Dolu SQLite v63 → v64 yükseltmesi | 3 bakiye → 5 satır · toplam korundu · ondalıklar tam (0.1 / 0.2) |
| Doğrulama kapısı (kasten bozuk bakiye) | Migration **durdu**, şema 63'te kaldı, **hiçbir şey yazılmadı** |
| Dönüştürülen sorgular PostgreSQL'de | Liste **2459 satır = malzeme sayısı** → satır çoğaltma **yok**; detay/kırılım/servis toplamı **tutarlı** |

Yeni testler: [`tests/DepoWise.Tests/StockLocationTests.cs`](../../tests/DepoWise.Tests/StockLocationTests.cs)
QA raporu: [`docs/tests/Stok_Lokasyon_Test_Report.md`](../tests/Stok_Lokasyon_Test_Report.md)

> 🔒 **Masaüstü çevrimdışı mimarisine dokunulmadı.** Senkron **0 değişiklik** gerektirdi:
> `DbIntrospect.PrimaryKey` bileşik anahtarı okuyor, `BusinessSyncService` `ON CONFLICT` hedefini
> ondan kuruyor → üç kolonlu anahtar **otomatik** üretiliyor. `stock_movements` şeması değişmedi.

## ▶️ SIRADAKİ İŞ
**`STK-03` — API uçlarına lokasyon boyutu.** `/api/stock/...` giriş/çıkış/sayım/transfer uçları
lokasyonu açıkça alıp döndürmeli; malzeme uçlarına lokasyon kırılımı eklenmeli
(`StockService.GetBalanceAt` / `GetBalancesByLocation` zaten hazır).
Ardından `STK-04` (Web) → `STK-05` (Masaüstü + offline).

## ⛔ Karar bekleyenler
| İş | Neyi bekliyor |
|---|---|
| `STK-08` | **KARAR-8** — "Atanmamış" stok nasıl dağıtılacak (öneri: kullanıcı transferle) |
| `BKM-01…03` | KARAR-4 (bakımda negatif stok mu, onay kapısı mı) |
| `TMZ-02`, `BRM-01`, `YTK-01…04` | YET-01 (rol değişince yetkiler) |
| `SNK-05` | Çevrimdışı onay çakışması |

## 📌 Canlı ortam
API `depowise-erp` v149 · Web `depowise-web` v175 · Neon PG **17.10** · **canlı şema 63**
(64 henüz **deploy edilmedi** — dalda duruyor) · 3 firma · 8 kullanıcı · 6 lokasyon · 2461 malzeme ·
667 stok hareketi · Test 1223/1190/0/33 · Build 0 hata

## ⚠️ Açık riskler
- **Deploy edilince** stoğun neredeyse tamamı **"ATANMAMIŞ"** görünecek (666/667 hareket lokasyonsuz;
  8953,3 birim) → KARAR-8 alınmadan kullanıcıya sürpriz olur.
- Migration, bakiyesi defterle **uyuşmayan** bir veritabanında **bilinçli olarak durur** (sessiz bozulma
  yerine açık hata). Böyle bir **masaüstü** veritabanı varsa güncelleme başlamaz → önce yeniden hesaplama
  gerekir. Üretim PG kopyasında uyuşmazlık **yok**.
- Masaüstü **paketi yayınlanmadı** — Grup 6 masaüstü düzeltmeleri kullanıcıya ulaşmadı.
- Branch **`master`'a birleştirilmedi**.
- **66 malzemede negatif stok** zaten mevcut (ADR-086 devralınan eksik stok); migration **1 yeni** negatif
  üretir (defterin söylediği) — toplam 67.
