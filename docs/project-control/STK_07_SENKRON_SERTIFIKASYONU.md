# STK-07 — Senkron Sertifikasyonu (lokasyon bazlı stok) · ✅ TAMAMLANDI

> 2026-08-11 · FAZ C · Ön koşul: `STK-00…06` ✅
> Amaç: **yeni senkron tasarlamak DEĞİL** — mevcut senkronun depo bazlı stok modeliyle doğru,
> idempotent ve çevrimdışı uyumlu çalıştığını **uçtan uca kanıtlamak**.

---

## 1. MEVCUT MİMARİ — koddan doğrulandı (varsayım değil)

| İddia (STK-02'den) | Kod kanıtı | Sonuç |
|---|---|---|
| `stock_movements` lokasyonu zaten taşıyor | `branch_id` + `branch_from_id` kolonları | ✅ doğru |
| Şema değişmedi | Migration064 yalnız `stock_balances`'ı değiştirdi | ✅ doğru |
| Bileşik PK otomatik çözülüyor | `DbIntrospect.PrimaryKey` → `BusinessSyncService:571` `conflictTarget` | ✅ doğru |
| `stock_balances` türetilmiş | `/api/sync/business-push` içinde `RecomputeBalances` çağrılıyor (`Program.cs:384`) | ✅ doğru |
| Bakiye **otoriter değil** | Senaryo 8 ile KANITLANDI (aşağıda) | ✅ doğru |
| Lokasyon senkronda kaybolmuyor | Senaryo 1-7, 9 | ✅ doğru |

**Gerçek senkron yolu:** masaüstü → yerel SQLite (çevrimdışı yazma) → bağlantı gelince
`POST /api/sync/business-push` → sunucu `Apply` + **`RecomputeBalances`** → `GET /api/sync/business-pull?since=`
(delta) → yerele uygula (**`stock_balances` HARİÇ**).

## 2. 🔍 BULGU — `branches` iş-senkronunda YOK

`BusinessSyncService.Tables` listesinde **`branches` bilinçli olarak yoktur**
(*"web-otoriteli; kod/şifre taşır — sunucuda zaten var"*). Masaüstü şubeleri **ayrı org uçlarından** alır.

**Depo bazlı stok için anlamı:** masaüstü, **yerel veritabanında bilmediği bir depoya stok yazamaz**
(`EnsureLocationOwned` reddeder). Yani web'de yeni açılan bir depo, masaüstüne org senkronu inmeden
kullanılamaz. **Bu bir hata değildir** — yeni depo zaten çevrimdışı bilinemez ve uydurulmamalıdır.
Ancak kullanıcıya "depo listem eksik" dedirtebilecek bir durumdur → **`SNK-12`** olarak kaydedildi
(org senkronu sonrası depo listesinin tazelenmesi ve kullanıcıya görünür olması).

## 3. SERTİFİKASYON TESTLERİ (11 senaryo, GERÇEK HTTP senkron uçları)

Dosya: `tests/DepoWise.Tests/SyncStockLocationCertificationTests.cs`

| # | Senaryo | Kanıtladığı |
|---|---|---|
| 1-2 | Çevrimdışı giriş + çıkış | Doğru depoya yazılıyor, diğer depo etkilenmiyor, senkron sonrası sunucu aynı |
| 3 | Çevrimdışı transfer | **İki bacak da** senkronlanıyor; `branch_id` / `branch_from_id` birebir korunuyor |
| 4 | Çevrimdışı sayım | Sayılan deponun bakiyesiyle karşılaştırılıyor; fark ATANMAMIŞ'a **yazılmıyor** |
| 5 | offline→online→offline→online | **Kopya hareket yok**; yerel ve sunucu hareket sayısı eşit |
| 6 | Idempotency | Aynı paket **3 kez** gönderildi → hareket sayısı ve bakiye değişmedi |
| 7 | Çoklu lokasyon + ATANMAMIŞ | A=10 · B=20 · ATANMAMIŞ=5 → toplam 35, kırılım korunuyor |
| 8 | **Bakiyenin otoritesi DEFTER** | Yerel bakiye kasten 999 yapıldı → senkron sonrası **10** (defter kazandı) |
| 9 | Yakınsama | Giriş+çıkış+transfer+sayım+ters kayıt sonrası **hareket kimlikleri dahil** iki taraf aynı |
| 10 | **Delta pull + sürüm ilerlemesi** | `?since=` yalnız değişeni getiriyor; eski kayıt **tekrar inmiyor**; güncel sürümden sonrası **boş** |
| 11 | Şirket izolasyonu | Yabancı depoya yazma çevrimdışı da reddediliyor; paket yalnız kendi firmasını taşıyor |
| 12 | Bakiye tablosu temizliği | (malzeme, lokasyon) başına **tek satır**; **hayalet lokasyon satırı yok** |

## 4. DOĞRULAMALAR

| Doğrulama | Sonuç |
|---|---|
| Başlangıç testi | 1281 · 1248 geçti · 0 kaldı · 33 atlandı |
| **Bitiş testi** | **1292 · 1259 geçti · 0 kaldı · 33 atlandı** (**11 yeni senaryo**) |
| Build | **0 hata** |
| Senkron kodu | **DEĞİŞTİRİLMEDİ** (tek satır bile) |
| Offline mimari | **DOKUNULMADI** — stok yazma yerel SQLite transaction'ı, API çağrısı yok |

## 5. PERFORMANS ÖLÇÜMÜ (mevcut sistem — refactor YOK)

- **Delta pull çalışıyor:** güncel sürümden sonrası **boş paket** dönüyor → ikinci senkronda değişmemiş
  kayıtlar **tekrar inmiyor** (senaryo 10). "Her açılışta tüm veriyi indirme" riski **yok**.
- **Sürüm (cursor) ilerliyor:** `business-version` ucu iş verisi değişince artıyor → istemci ucuz
  yoklama ile değişikliği fark ediyor, tam snapshot çekmiyor.
- **Push tek pakettir**; hareket başına ayrı istek yok. Idempotency `operation_id` ile hareket düzeyinde.
- **`SNK-11` teyit edildi:** `stock_balances` push paketinde taşınıyor (tablo listesinde) ama sunucu
  onu **kullanmıyor** (recompute eziyor) → **saf gereksiz yük**. Kaldırılması bu fazda YAPILMADI
  (senkron mimarisi değişikliği); ölçüm `SNK-11`'e işlendi.

## 6. YENİ DEVREDİLEN İŞ
| Kod | İş |
|---|---|
| `SNK-12` | Masaüstünde depo listesi tazeleme — `branches` iş-senkronunda olmadığı için web'de açılan yeni depo masaüstüne org senkronu inmeden görünmüyor |

**Silinmeyen açık işler:** `BKM-04` · `SNK-11` · `RPR-01` · `STK-09` · `STK-10` · `STK-11` ·
**KARAR-8 / STK-08**.
