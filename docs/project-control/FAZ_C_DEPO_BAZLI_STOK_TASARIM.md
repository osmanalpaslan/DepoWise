# FAZ C — Depo Bazlı Stok: Analiz, Tasarım ve Migration Planı

> Tarih: **2026-08-11** · Karar: **KARAR-7 = A** (malzeme kartı firma geneli, stok depo bazlı)
> Bu belgede **kod değiştirilmemiştir**. Uygulama, §8'deki sıraya göre yapılacaktır.
> Kısıt: **Masaüstü offline çalışma mimarisi korunur** (sorgulanmaz).

---

## 1. Mevcut durumun gerçeği (koddan + canlı veriden)

### 1.1 İyi haber — defter zaten lokasyon taşıyor

`stock_movements` şeması **baştan lokasyon farkındadır**:

| Kolon | Kaynak | Anlam |
|---|---|---|
| `branch_id` | Migration005:111 | Hareketin gerçekleştiği lokasyon |
| `branch_from_id` | Migration006:42 | Transferde **kaynak** lokasyon |
| `document_id`, `is_reversed`, `reverses_movement_id` | Migration006 | Belge + ters kayıt |
| `operation_id` (UNIQUE) | Migration005 | **Idempotency** |

**Transfer zaten uygulanmış** (`StockService.Transfer`, çok malzemeli, tek transaction, negatif stok guard'lı,
satır bazlı idempotency anahtarlı). Kaynak çıkış + hedef giriş olmak üzere **2 hareket** yazıyor.
Kodun kendi yorumu durumu net anlatıyor:

> *"Kaynak çıkış (negatif guard) + hedef giriş — **net bakiye değişmez** ama hareketler kayıtlı"*

### 1.2 Asıl sorun — bakiye önbelleğinde lokasyon yok

```sql
CREATE TABLE stock_balances (
    company_id, material_id, quantity, updated_at,
    PRIMARY KEY (material_id)          -- ⬅️ LOKASYON BOYUTU YOK
);
```

`stock_balances` **türetilmiş bir önbellektir** — ana kaynak defterdir ve `StockService` içinde
**defterden yeniden hesaplama** yordamı zaten vardır (`:495-527`, yarış koruması + defter imzası ile).
Yani sorun "veri kayıp" değil, **"bakiye tek havuzda toplanıyor"**dur.

**Sonuç:** Transfer bugün hareket olarak doğru yazılıyor ama bakiyede **görünmüyor**.

### 1.3 ⚠️ Kritik gerçek — canlı defterde lokasyon bilgisi YOK

Canlı veritabanı (salt-okuma ölçüm, 2026-08-11):

| Ölçüm | Değer |
|---|---|
| Toplam hareket | **667** |
| `branch_id` **dolu** | **1** |
| `branch_id` **boş (NULL)** | **666** |
| Bunların 664'ü | `opening` (açılış yüklemesi) |
| Kullanılan farklı şube | 1 |
| `stock_balances` satırı | 664 |
| Malzeme kartı | 2461 (yalnız **2**'sinde `branch_id` dolu) |
| Şube/şantiye | 1 `branch` + 5 `site` |

> **Bu, migration'ın en önemli girdisidir.** Geçmiş stok verisinin **hangi depoda** olduğu
> **veriden çıkarılamıyor** — çünkü açılış kayıtları lokasyonsuz girilmiş.
> Kullanıcı talimatı gereği: **veri uydurulmayacak, rastgele depoya dağıtılmayacak, sahte geçmiş üretilmeyecek.**

### 1.4 Diğer bulgular

| ID | Bulgu |
|---|---|
| `STK-B1` | `movement_type` belgelenen küme `opening\|in\|out\|transfer\|adjustment` ama kodda ayrıca **`usage` / `usage_reverse`** üretiliyor (`MaintenanceService.cs:478`). Katalog eksik/tutarsız |
| `STK-B2` | `materials.branch_id` (Migration055) 2461 kayıttan yalnız 2'sinde dolu → **fiilen kullanılmıyor**. KARAR-7=A ile bu alanın anlamı "malzeme kartının ait olduğu şube"dir ve **stok lokasyonu DEĞİLDİR**; karıştırılmamalı |
| `STK-B3` | Ayrı `warehouses`/`depots` tablosu **yok** — lokasyon = `branches` |

---

## 2. Kavram netleştirmesi (mevcut modele sadık)

| Kavram | Bugünkü karşılığı | Rolü |
|---|---|---|
| **Şube** | `branches.kind='branch'` | Organizasyonel birim |
| **Şantiye** | `branches.kind='site'` | Operasyonel çalışma alanı |
| **Depo (stok lokasyonu)** | **Ayrı varlık YOK** | Bugün stok lokasyonu = `branches` satırı |

### Tasarım kararı — **yeni kavram üretilmeyecek**

Stok lokasyonu olarak **`branches` kullanılacaktır**. Gerekçe:
- Alan zaten `stock_movements.branch_id` olarak var ve transfer bunun üzerinde çalışıyor.
- Sektörde şantiyenin kendi deposu vardır; ayrı bir "depo" varlığı bugün karşılığı olmayan bir soyutlama olur.
- Kullanıcı talimatı: *"gereksiz yeni kavramlar üretme, mevcut modeli doğru kullan."*

**İleriye kapı açık:** Bir şubenin **birden fazla** deposu gerekirse, ileride `stock_locations(id, branch_id, name)`
eklenir ve `location_id` oraya işaret eder. Bugünkü tasarım bunu **kırmadan** kaldırır çünkü bakiye
`location_id` üzerinden anahtarlanır, doğrudan `branch_id` üzerinden değil.

> **Kural (§7'ye uyum):** Organizasyon birimi ile stok lokasyonu **kavramsal olarak ayrıdır**;
> bugün aynı tabloya denk gelmeleri bir **uygulama detayı**dır, kalıcı bir eşitleme değildir.

---

## 3. Hedef veri modeli

### 3.1 `stock_balances` — yeni anahtar

```sql
-- YENİ
PRIMARY KEY (company_id, material_id, location_id)
location_id TEXT NOT NULL          -- branches.id  |  '' = ATANMAMIŞ
```

**Neden `''` (boş metin), NULL değil:** PostgreSQL'de birincil anahtar kolonu NULL olamaz.
Boş metin **açık ve sorgulanabilir** bir "Atanmamış / Merkez" kovasıdır; ekranda böyle gösterilir.
Bu kova **geçicidir** — kullanıcı stoğu gerçek depolara dağıttıkça boşalır.

### 3.2 Bakiye artık nasıl hesaplanır

```sql
SELECT material_id, COALESCE(branch_id,'') AS location_id,
       SUM(direction * quantity) AS quantity
FROM stock_movements
WHERE company_id = @c
GROUP BY material_id, COALESCE(branch_id,'');
```

Defter **ana kaynak** olmaya devam eder; bakiye yalnız bu türetmenin önbelleğidir (mevcut mimari korunur).

### 3.3 Transfer modeli — **değişmiyor**

Bugünkü 2-hareketli (out+in), tek transaction, `group_id` ile bağlı yapı **doğrudur** ve korunur.
Tek fark: artık **bakiyeye de yansıyacak** (kaynak azalır, hedef artar).

---

## 4. Migration stratejisi (veri kaybı riski: **YOK**)

| Adım | İşlem | Güvenlik |
|---|---|---|
| 1 | `pg_dump -Fc` yedeği (prosedür: `POSTGRES_BACKUP_RESTORE.md`) | Zorunlu ön koşul |
| 2 | Yeni `stock_balances` yapısını kur (tablo yeniden oluştur — SQLite'ta PK değişimi ALTER ile yapılamaz) | Tek transaction |
| 3 | **Defterden yeniden hesapla** — `GROUP BY material_id, COALESCE(branch_id,'')` | **Veri uydurulmaz** |
| 4 | **Doğrulama:** her malzeme için `SUM(yeni lokasyon bakiyeleri) == eski tek bakiye` | Eşleşmezse migration **DURUR** (transaction geri alınır) |
| 5 | İndeksler | `(company_id, location_id, material_id)` |

**Neden veri kaybı yok:** Yeni bakiyeler **mevcut defterden** üretiliyor. Defter değiştirilmiyor.
Toplamlar korunuyor — yalnız lokasyona bölünüyor. Doğrulama adımı bunu **migration içinde** garanti eder.

### 4.1 ⚠️ Kullanıcının bilmesi gereken sonuç

666/667 hareket lokasyonsuz olduğu için, migration'dan sonra **stoğun neredeyse tamamı
"Atanmamış" kovasında görünecektir.** Bu bir hata değildir — geçmişte lokasyon girilmemiş olmasının
dürüst yansımasıdır.

**Çözüm (veri uydurmadan):** Kullanıcı, normal **transfer** ekranından "Atanmamış → Depo A/B/Şantiye C"
dağıtımını **bir kez** yapar. Bu gerçek bir iş işlemidir: audit'e yazılır, geri alınabilir, hareket geçmişi doğru olur.
`STK-08` bu dağıtım için toplu bir yardımcı ekran öngörür.

---

## 5. Etkilenen bileşenler

### Veritabanı
`stock_balances` (yeniden kurulur) · `stock_movements` (**değişmez**) · indeksler

### Backend / API
`StockService` — `Balance`, `BalancesFor`, `ApplyLine`, `RecomputeBalances`, negatif stok guard'ı, `Transfer`,
`ReceiveIn`, `IssueOut`, `Adjust`, sayım · `MaterialService` (liste bakiyesi, kritik stok) ·
`MaintenanceService` (`usage` hareketi) · `DashboardService` · `ReportService` · `/api/stock/*`, `/api/materials/*`

### Web
Malzemeler (liste + kart) · Stok Girişi/Çıkışı · Stok Hareketleri · Stok Sayımı · Transfer · Uyarılar · Raporlar · Dashboard

### Masaüstü
Aynı ekranların tamamı (`MaterialsViewModel`, `StockEntryViewModel`, `StockMovementsViewModel`,
`StockCountViewModel`, `DashboardViewModel`, `ReportsViewModel`) — **offline yollar dâhil**

### Senkron
`stock_balances` **zaten** `BusinessSyncService.Tables` içinde. Yeni birincil anahtar nedeniyle
upsert çakışma anahtarı `(company_id, material_id, location_id)` olarak güncellenmeli.
`stock_movements` şeması değişmediği için push/pull mantığı **aynen** çalışır.

---

## 6. Offline senaryolar (§18 karşılıkları)

| # | Senaryo | Tasarımdaki karşılık |
|---|---|---|
| 1 | Çevrimdışı stok giriş/çıkış | Yerel SQLite'a hareket + yerel bakiye (lokasyonlu). **Değişiklik yok** |
| 2 | Çevrimdışı transfer | 2 hareket + `group_id`, tek yerel transaction. **Değişiklik yok** |
| 3 | Bağlantı gelince gönderim | Mevcut watermark'lı push. Hareketler `operation_id` ile **idempotent** |
| 4 | Sunucuda değişiklik varsa | Mevcut delta pull (`?since=`) + kalıcı imleç |
| 5 | Senkron yarıda kesilirse | Bozuk sayfada rollback, cursor ilerlemez (`SyncServer.cs:94`) |
| 6 | Tekrar senkron | `ux_stock_movements_operation` UNIQUE → **aynı hareket iki kez uygulanamaz** |

**Sonuç: offline mimariye dokunulmuyor.** Bakiye türetilmiş olduğu için, senkron sonrası
her iki tarafta da defterden yeniden hesaplanarak yakınsar.

⚠️ **Tek yeni risk:** Aynı malzeme+lokasyon bakiyesi iki tarafta ayrı hesaplanır. Bakiye **LWW ile
senkronlanmamalıdır** (CLAUDE.md §4: stokta LWW yasak). Doğru davranış: **bakiyeyi senkronlamak yerine
defterden yeniden hesaplamak**. `STK-07` bunu doğrulayacak.

---

## 7. Ön muhasebe ile ilişki (FAZ D hazırlığı)

Depo bazlı stok kurulduğunda **maliyet merkezi = lokasyon** doğal olarak elde edilir:

| Ön muhasebe ihtiyacı | Depo bazlı stoktan gelen |
|---|---|
| Şantiye maliyeti | Lokasyon bazlı çıkış hareketleri × birim fiyat |
| Stok maliyeti | Lokasyon bazlı bakiye × fiyat |
| Gider dağıtımı | Hareketin `branch_id`'si = maliyet merkezi |

`MUH-01` (cari + belge alanları) bu fazın migration ailesiyle **birlikte** planlanacaktır — ayrı geçiş yapılmayacak.

---

## 8. Görev sırası (bağımlılıkla)

| ID | İş | Ön koşul |
|---|---|---|
| `STK-00` | **İzole provada** migration'ı gerçek production kopyası üzerinde çalıştır + doğrula | yedek |
| `STK-01` | `stock_balances` şema değişimi + defterden yeniden hesaplama + doğrulama adımı (iki lehçe) | STK-00 |
| `STK-02` | `StockService` tüm yolları lokasyon bazlı (guard + recompute + sorgular) | STK-01 |
| `STK-03` | API uçları + DTO'lar (lokasyon parametresi) | STK-02 |
| `STK-04` | Web ekranları (liste, kart, giriş/çıkış, sayım, transfer, uyarı) | STK-03 |
| `STK-05` | Masaüstü ekranları + **offline yollar** | STK-03 |
| `STK-06` | Raporlar + Dashboard lokasyon boyutu | STK-04, STK-05 |
| `STK-07` | Senkron doğrulaması (offline 6 senaryo + idempotency + bakiye yakınsaması) | STK-05 |
| `STK-08` | "Atanmamış → depo" toplu dağıtım yardımcısı (web + masaüstü) | STK-04, STK-05 |
| `STK-B1` | `movement_type` kataloğunu `usage`/`usage_reverse` ile tutarlı hale getir | — |
| `TRF-01` | Transfer ekranı/akışı doğrulama (kod var, UI paritesi kontrol) | STK-04, STK-05 |

**Testler (§25):** iki depoda aynı malzeme · transfer · sıfır/negatif stok · düzeltme · sayım ·
offline hareket + transfer · senkron sonrası bakiye · tekrar senkron (duplicate yok) · yarım senkron ·
yetki · firma izolasyonu · şube/şantiye ilişkisi.

---

## 9. Bu belgeye göre ilk somut adım

**`STK-00`** — Migration'ı yazmadan önce, alınmış production yedeğinin izole kopyası üzerinde
"defterden lokasyon bazlı bakiye üretimi"nin **toplamları koruduğunu** kanıtla.
664 malzeme × 667 hareketle bu kanıt hızlı ve risksizdir; migration'ı yazmak ancak bundan sonra doğrudur.
