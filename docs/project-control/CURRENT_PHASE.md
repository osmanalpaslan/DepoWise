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

## ▶️ SIRADAKİ İŞ
**`STK-02` — 15 çağrı noktasını lokasyon farkında hale getir + Migration064'ü katalogda etkinleştir.**
Sıra: `StockBalanceWriter` (CAS → lokasyon anahtarlı) → `StockService` (toplam = SUM, recompute lokasyonlu)
→ `MaterialService`/`DashboardService`/`ReportService`/`OpeningStockService` (JOIN'ler toplam alacak şekilde)
→ 5 test dosyası → katalog kaydı → tam test.
**Kural:** Genel toplam ile lokasyon toplamı asla kopmayacak.

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
