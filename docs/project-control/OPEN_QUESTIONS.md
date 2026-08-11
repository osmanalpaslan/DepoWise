# AÇIK SORULAR — Kullanıcı Kararı Bekleyenler

> Son güncelleme: **2026-08-11** · Karar verilince ilgili görevin önündeki engel kalkar.
> **Karar verildiğinde** buraya sonucu yaz + `docs/DECISIONS.md`'ye ADR olarak geçir.

---

## 🔴 KARAR-7 — Malzeme kartı: firma geneli mi, şube bazlı mı? *(EN KRİTİK)*

**Neyi engelliyor:** FAZ C'nin tamamı (`STK-01…07`, `TRF-01`) → dolayısıyla FAZ D (ön muhasebe altyapısı)
ve şantiye maliyeti. **Projenin en büyük mimari borcunun önündeki tek engel budur.**

**Bugünkü karışık durum:** `materials.branch_id` var (malzeme kartının şubesi) ama `stock_balances`
firma başına **tek bakiye** tutuyor. Yani "malzeme bir şubeye ait" ile "stok depoda tutulur" karışmış.

| Seçenek | Ne demek | Artı | Eksi |
|---|---|---|---|
| **A — Malzeme firma geneli, stok depo bazlı** *(önerilen)* | Malzeme kartı tek; aynı malzemenin her depoda ayrı bakiyesi olur | Sektör standardı · transfer doğal · maliyet çıkar · SaaS'a uygun | `stock_balances` migration'ı (veri taşıma) |
| B — Malzeme şube bazlı | Her şube kendi malzeme kartını açar | Basit | Aynı vida 6 kez tanımlanır · transfer anlamsız · rapor bölünür |

**Öneri: A.** Ürünün hedef sektörü (çok şantiye/depo) bunu gerektiriyor.

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
