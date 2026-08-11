# AÇIK SORULAR — Kullanıcı Kararı Bekleyenler

> Son güncelleme: **2026-08-11** · Karar verilince ilgili görevin önündeki engel kalkar.
> **Karar verildiğinde** buraya sonucu yaz + `docs/DECISIONS.md`'ye ADR olarak geçir.

---

## ✅ KARAR-7 — **KARARA BAĞLANDI: A** *(2026-08-11, kullanıcı)*

> **Malzeme kartı firma geneli, stok depo/lokasyon bazlı.**

**Gerekçe:** Ürünün hedef sektörü (çok şantiye/şube/depo) bunu gerektirir. Tek malzeme kartı +
lokasyon bazlı bakiye; depolar arası transfer, şantiye maliyeti, stok raporları, ön muhasebe ve
gelecekteki maliyet hesapları ancak bu temelle doğru çalışır. B seçeneği (şube bazlı malzeme kartı)
aynı malzemenin her şubede yeniden tanımlanmasına, transferin anlamsızlaşmasına ve raporların
bölünmesine yol açardı.

**Sonuçları:**
- `stock_balances` birincil anahtarı `(company_id, material_id, location_id)` olur.
- Stok lokasyonu = `branches` (yeni kavram üretilmez); ileride çoklu depo gerekirse `stock_locations`
  eklenir ve tasarım bunu kırmadan taşır.
- `materials.branch_id` **stok lokasyonu DEĞİLDİR**; malzeme kartının organizasyonel şubesidir
  (bugün 2461 kayıttan yalnız 2'sinde dolu — fiilen kullanılmıyor).

**Açan görevler:** FAZ C (`STK-00…08`, `TRF-01`) → ardından FAZ D (`MUH-01`).
**Ayrıntılı tasarım:** [`FAZ_C_DEPO_BAZLI_STOK_TASARIM.md`](FAZ_C_DEPO_BAZLI_STOK_TASARIM.md)

---

## 🟠 KARAR-8 — "Atanmamış" stok kovası nasıl dağıtılacak? *(YENİ — FAZ C'den doğdu)*

Canlı veride **667 hareketin 666'sı lokasyonsuz** (664'ü açılış). Bu yüzden migration sonrası
stoğun **neredeyse tamamı "ATANMAMIŞ"** kovasında görünecek (8953 birim / 664 malzeme satırı).

Bu bir hata değil, geçmişte lokasyon girilmemiş olmasının dürüst yansımasıdır. Veri **uydurulmayacak**.

| Seçenek | Ne demek |
|---|---|
| **A — Kullanıcı transferle dağıtır** *(önerilen)* | Migration sonrası "Atanmamış → Depo/Şantiye" transferi yapılır. Gerçek iş işlemi: audit'e yazılır, geri alınabilir, geçmiş bozulmaz |
| B — Tümü tek bir varsayılan depoya yazılsın | Hızlı ama **varsayım**; yanlış depoya yazarsa düzeltmesi daha zor |

**Öneri: A** + `STK-08` (toplu dağıtım yardımcı ekranı) ile kolaylaştırma.

---

## 🟠 KARAR-4 — Bakımda stok düşümü

Bakım kaydı malzeme tüketirken stok yetersizse ne olacak?
**A)** Negatif stoka izin ver (saha gerçeği) · **B)** Onay kapısı koy (stok yoksa bakım kaydedilemez).
**Engellediği:** `BKM-01…03`.

## 🟠 YET-01 — Rol değişince yetkiler ne olsun?

Kullanıcı Personel→Admin veya Admin→Personel olunca mevcut `user_permissions` satırları:
**A)** korunur · **B)** temizlenir · **C)** kullanıcıya sorulur.
**Engellediği:** `TMZ-02`, `BRM-01`, `YTK-01…04`.

## 🟠 SNK-05 — Çevrimdışı onay çakışması

Masaüstünde çevrimdışı verilen talep onayı, sunucuda o sırada değişmişse ne olacak?
**A)** Sunucu kazanır · **B)** İstemci kazanır · **C)** Çakışma kuyruğuna düşer (kullanıcı çözer).
Not: Onayda **LWW yasak** (CLAUDE.md §4) → **C** mimariye en uygun görünüyor.

## 🟡 Ön muhasebe kapsam sınırı

Öneri (onayınızla ADR olur): Alpnex **yasal muhasebe/beyanname yazılımı olmayacak**; yalnız
**operasyonel ön muhasebe** (cari, borç/alacak, tahsilat/ödeme, kasa/banka, şantiye gider dağıtımı).
e-Fatura/e-Arşiv/beyanname **kapsam dışı** (D sınıfı).

## 🟡 Masaüstü paketi ne zaman yayınlanacak?

Grup 6'nın masaüstü düzeltmeleri (Çöp Kutusu parolası, sunucu-otoriteli şablonlar) **kullanıcılara
ulaşmadı** — yeni paket yayınlanana kadar yalnız web tarafı güncel.

## 🟡 Branch `master`'a birleştirilsin mi?

32 commit `feature/mlz-01-malzeme-silme-korumasi` dalında; canlıya bu daldan deploy edildi.

---

## ✅ Karara bağlanmış olanlar (kayıt)

| Konu | Karar | Tarih |
|---|---|---|
| PostgreSQL yedeği yalnız Neon'a bırakılmasın | Kabul — `pg_dump` prosedürü eklendi | 2026-08-11 |
| PG17→PG17 restore provası yapılamaması | **Bilinçli risk kabulü** | 2026-08-11 |
| Şablon rolü davranışı (KARAR-G6-B) | Masaüstü davranışı doğru; web hizalandı | 2026-08-11 |
| Silinen kullanıcı (KARAR-G6-A) | Çöp Kutusu + koşullu benzersizlik | 2026-08-11 |
| Şube silme (KARAR-G6-C) | Bağlı araç/personel varsa engelle | 2026-08-11 |
