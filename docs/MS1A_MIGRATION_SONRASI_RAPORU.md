# M-S1a — CANLI MIGRATION SONRASI RAPOR

- **Tarih:** 2026-08-09
- **Durum:** ✅ **BAŞARILI** — canlı migration uygulandı, doğrulandı, yayınlar tamamlandı
- **Onay:** kullanıcı (2026-08-09), Bölüm 4 seçeneği **A — DUR**
- Bu raporda hiçbir bağlantı adresi, kullanıcı adı, parola veya API anahtarı yer almaz.

---

## 1. Migration başarı durumu

| | |
|---|---|
| Migration | `Migration062_ChildTableCompanyId` |
| Şema sürümü | **61 → 62** ✅ |
| Çalıştığı yer | Canlı PostgreSQL (`depowise_prod`), API açılışında |
| Sonuç | Başarılı — durma/geri alma **gerekmedi** |
| Çözülemeyen kayıt | **0** (durma koşulu hiç oluşmadı) |
| Silinen kayıt | **0** |

---

## 2. Önce / sonra veri sayımları (salt-okuma, `SET TRANSACTION READ ONLY`)

Her iki ölçümde de yazma denemesi bilerek yapıldı ve PostgreSQL **SqlState 25006** ile reddetti.

| Tablo | ÖNCE | SONRA | Beklenen | Sonuç |
|---|---|---|---|---|
| `material_request_items` | 2 | **2** | 2 | ✅ |
| `maintenance_materials` | 0 | **0** | 0 | ✅ |
| boş `company_id` | — | **0** | 0 | ✅ |
| yetim kayıt | 0 | **0** | 0 | ✅ |
| yanlış firma eşleşmesi | — | **0** | 0 | ✅ |
| çözülemeyen kayıt | 0 | **0** | 0 | ✅ |
| şema sürümü | 61 | **62** | 62 | ✅ |

### Dokunulmaması gereken tablolar — hepsi AYNI

| Tablo | Önce | Sonra |
|---|---|---|
| `material_requests` | 2 | 2 |
| `vehicle_maintenances` | 0 | 0 |
| `materials` | 2463 | 2463 |
| `vehicles` | 94 | 94 |
| `stock_movements` | 667 | 667 |
| `personnel` | 3 | 3 |
| `users` | 8 | 8 |
| `branches` | 6 | 6 |
| `companies` | 3 | 3 |

### Taşınan kayıtlar (canlıdaki tam liste)

| Kalem | → `company_id` | Doğru mu? |
|---|---|---|
| `808fed13…` | `ed271d0c…` (Oze İnşaat) | ✅ üst talebinin firmasıyla aynı |
| `0e7c4a0c…` | `ed271d0c…` (Oze İnşaat) | ✅ üst talebinin firmasıyla aynı |

Firma dağılımı (sonra): `material_request_items` → `ed271d0ca2b04a73b97f5025a53a04b4 = 2`. Başka firma yok.

---

## 3. Migration 062 doğrulaması

| Kontrol | `material_request_items` | `maintenance_materials` |
|---|---|---|
| `company_id` kolonu | VAR ✅ | VAR ✅ |
| NOT NULL | **EVET** ✅ | **EVET** ✅ |
| Varsayılan (DEFAULT) | **yok** ✅ | **yok** ✅ |
| Firma indeksi | `ix_material_request_items_company` ✅ | `ix_maintenance_materials_company` ✅ |
| Eski indeksler korundu | `ix_material_request_items`, `_pkey` ✅ | `ix_maintenance_materials`, `_pkey` ✅ |

`schema_migrations` içinde sürüm 62 tam **1 kez** kayıtlı.

---

## 4. Firma izolasyonu doğrulaması (canlı, salt-okuma)

Asıl amacın canlıda çalıştığının doğrudan kanıtı — **DEPOWISE** firmasıyla giriş yapılıp eşitleme paketi alındı:

| | |
|---|---|
| `/api/sync/business-pull` | 200 |
| Paketteki `material_request_items` | **0 satır** |
| Paketteki `maintenance_materials` | **0 satır** |
| Yabancı firma satırı | **0** ✅ |

Canlıda 2 talep kalemi var ama bunlar **Oze İnşaat**'a ait; DEPOWISE'ın paketine **girmiyor**.
Migration öncesi (kolon yokken) bu 2 satır süzgeçten geçemediği için DEPOWISE'ın paketine de giriyordu.
Açık artık kapalı.

---

## 5. Rollback / geri dönüş noktası

| | |
|---|---|
| Neon dalı | **`pre-ms1a`** (`br-red-breeze-a2ov3bqm`) |
| Durum | **ready** — duruyor, silinmedi |
| Oluşturma | 2026-08-09T10:21:24Z (migration'dan ~6 dk önce) |
| Ana veritabanına etkisi | **Yok** (kopya-üzerine-yazma) |

Geri alma betiği (gerekirse; her iki veritabanında da aynı, testle doğrulandı):
```sql
DROP INDEX IF EXISTS ix_material_request_items_company;
DROP INDEX IF EXISTS ix_maintenance_materials_company;
ALTER TABLE material_request_items DROP COLUMN company_id;
ALTER TABLE maintenance_materials  DROP COLUMN company_id;
DELETE FROM schema_migrations WHERE version = 62;
```

---

## 6. Deploy durumu

| Bileşen | Durum | Doğrulama |
|---|---|---|
| **API** `depowise-erp` | ✅ yayında | `/health` 200 · açılış logu temiz · migration açılışta uygulandı |
| **Web** `depowise-web` | ✅ yayında | `/`, `/login`, `/requests`, `/maintenance`, `/daily` → hepsi **200** |
| **Masaüstü 1.0.133** | ✅ yayında | sunucudaki "en güncel sürüm = 1.0.133" |

### Canlı sağlık kontrolleri (giriş yapılmış, salt-okuma)

| Uç | Sonuç |
|---|---|
| `/health` | 200 |
| `/api/materials` | 200 |
| `/api/vehicles` | 200 |
| `/api/requests` | 200 |
| `/api/maintenance` | 200 |
| `/api/daily/grid` | 200 |
| `/api/fuel` | 200 |
| `/api/stock/change-log` | 200 |
| `/api/sync/business-pull` | 200 |
| `/api/stock` · `/api/stock/movements` | **500 — kapsam dışı, önceden var olan hata (bkz. Bölüm 9)** |

### Masaüstü paketi doğrulaması

| | Manifest (sunucu) | Yerel dosya | Sonuç |
|---|---|---|---|
| Sürüm | 1.0.133 | 1.0.133 | ✅ |
| Boyut | 89.550.279 | 89.550.279 | **eşleşti** ✅ |
| SHA-256 | `aa12a6038fa55807…` | `aa12a6038fa55807…` | **eşleşti** ✅ |

---

## 7. Masaüstü migration davranışı

⚠️ **Babanın makinesindeki yerel veritabanına erişimim YOK.** Oradaki dosyayı göremiyorum, ölçemiyorum;
onun hakkında hiçbir varsayım yapmıyorum. Aşağıdaki kanıt **bu makinedeki gerçek bir masaüstü
veritabanı dosyasının KOPYASI** üzerinde alınmıştır (orijinal dosyaya dokunulmadı).

| Adım | Sonuç |
|---|---|
| Kopyanın başlangıç sürümü | **56** (eski sürüm — zincirin tamamı sınandı) |
| Uygulanan migration'lar | 57, 58, 59, 60, 61, **62** |
| Bitiş sürümü | **62** ✅ |
| Talep kalemi | 1 → **1** (kaybolmadı) · `company_id` = `ed271d0c…` (üst talebinden) ✅ |
| Bakım malzemesi | 1 → **1** (kaybolmadı) · `company_id` = `ed271d0c…` (üst bakımından) ✅ |
| Diğer değerler | `quantity`, `unit_price` aynen korundu ✅ |
| NOT NULL / varsayılan | notnull=1 · varsayılan **yok** ✅ |
| İndeksler | eski + yeni, dördü de yerinde ✅ |
| `PRAGMA foreign_key_check` | temiz ✅ |
| `PRAGMA integrity_check` | **ok** ✅ |

Bu, SQLite'ta tablo yeniden kurma yolunun **gerçek bir masaüstü dosyasında** veri kaybı olmadan
çalıştığını gösterir. Baban 1.0.133'e güncellediğinde aynı zincir onun dosyasında da çalışacaktır;
firması belirlenemeyen bir satırla karşılaşırsa (olasılığı çok düşük — FK'ler bunu zaten engelliyor)
karar A gereği migration **duracak** ve uygulama açılmayacaktır. Böyle bir durumda haber verilmesi yeterli.

---

## 8. Canlı veride yapılan yazma işlemlerinin özeti

Migration'ın gerektirdikleri **dışında hiçbir veri müdahalesi yapılmadı.**

| İşlem | Kapsam |
|---|---|
| `ALTER TABLE material_request_items ADD COLUMN company_id TEXT` | şema |
| `UPDATE material_request_items … FROM material_requests` | **2 satır** — firma üst kayıttan yazıldı |
| `ALTER TABLE material_request_items ALTER COLUMN company_id SET NOT NULL` | şema |
| `CREATE INDEX ix_material_request_items_company` | şema |
| `ALTER TABLE maintenance_materials ADD COLUMN company_id TEXT` | şema |
| `UPDATE maintenance_materials …` | **0 satır** (tablo boş) |
| `ALTER TABLE maintenance_materials ALTER COLUMN company_id SET NOT NULL` | şema |
| `CREATE INDEX ix_maintenance_materials_company` | şema |
| `INSERT INTO schema_migrations(62)` | sürüm kaydı |

**Silinen kayıt: 0. Değiştirilen iş verisi: yok** (yalnız yeni kolon dolduruldu).
Tüm doğrulama sorguları salt-okunur transaction içinde çalıştı; yazma denemeleri 25006 ile reddedildi.

---

## 9. Kapsam dışı tespit edilen yeni sorun (uygulanmadı — bildiriliyor)

**KD-1 · `/api/stock` ve `/api/stock/movements` canlıda 500 veriyor.**
- **Hata:** `42703: column sm.rowid does not exist`
- **Kök neden:** `StockService.SearchMovements` sıralamada `sm.rowid` kullanıyor. `rowid` **SQLite'a özel**;
  PostgreSQL'de yoktur (`StockService.cs` satır 246 ve 284).
- **M-S1a ile ilgisi YOK:** bu satırlar **2026-08-05** tarihli "Aşama 1 (Aurora)" commit'inden beri var;
  `StockService.cs` bu işte hiç değiştirilmedi (git ile doğrulandı). Yani hata **migration'dan önce de
  vardı**, migration onu ortaya çıkarmadı/oluşturmadı — sadece bu denetimde fark edildi.
- **Etkisi:** sunucu/web tarafında "Stok Hareketleri" listesi çalışmıyor. Masaüstü SQLite'ta sorunsuz.
- **Yapılmadı:** kullanıcı talimatı gereği kapsam sessizce büyütülmedi. Ayrı iş olarak ele alınmalı.

Önceki raporda listelenen ve hâlâ açık olan kapsam dışı maddeler: **M-S1b** (`request_status_history`,
`maintenance_definition_vehicles` firma kolonu), **M-S1c** (yeni tabloda firma kolonunun unutulmasını
engelleyecek otomatik kontrol), **M-S1d** (eşitlemede üst kaydın firmasının ayrıca doğrulanması).

---

## 10. Test özeti (deploy öncesi)

| Küme | Sonuç |
|---|---|
| `CompanyIdMigrationTests` (SQLite) | 14 / 14 ✅ |
| `PostgresCompanyIdMigrationTests` | 6 / 6 ✅ |
| Tüm takım (SQLite) | **839 geçti · 0 başarısız · 20 atlandı** |
| Tüm PostgreSQL testleri | **30 / 30 ✅ · 0 atlandı** |
| Derleme | 0 hata |

Hiçbir test "yeşil görünsün diye" değiştirilmedi; yalnız 3 rapor testinin veri hazırlama satırı yeni
kolonu dolduruyor (iddialar değişmedi).
