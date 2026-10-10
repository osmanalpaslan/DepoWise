# Faz S — Eşitleme Performansı · Foreign Key · Benzersizlik (İş #11)

Tarih: **2026-08-09** · Tür: **analiz** (+ küçük ve risksiz bir düzeltme)
Migration: **UYGULANMADI** — gerekli görülenler aşağıda onayınıza sunuldu
Production: **yazma yok** · Deploy: **yok**

---

## Özet

| Bölüm | Sonuç |
|---|---|
| **#11-A Performans** | 1 gerçek N+1 bulundu ve **düzeltildi**; diğerleri incelendi, düzeltilmedi (gerekçeli) |
| **#11-B Foreign Key** | 62 tablo çıkarıldı. Eksik FK'ler **migration gerektirir → DURULDU** |
| **#11-C Benzersizlik** | Firma sınırları **zaten doğru**. Eksik index'ler migration gerektirir → **DURULDU** |

---

# #11-A — Eşitleme / sorgu performansı

## Düzeltilen: `/api/materials` bakiye N+1 (eski Y-6)

**Sorun.** Uç, dönen her malzeme için ayrı bir `GetBalance` sorgusu atıyordu:

```
sayfa = 200 malzeme  →  1 liste sorgusu + 200 bakiye sorgusu
```

**Neden önemli.** Üç etken birleşiyor:
1. Sunucu veritabanı artık **PostgreSQL** (Neon, ağ üzerinden) — her sorgu bir gidiş-dönüş.
   SQLite'ta (yerel dosya) aynı desen ucuzdu; taşınma bunu pahalı hâle getirdi.
2. Bu uç yalnız bir liste değil; **Stok, Talep ve Bakım ekranlarının hızlı-arama seçicisi**.
   Kullanıcı yazdıkça çağrılır.
3. Sorgu sayısı veri büyüdükçe artar (sayfa dolduğunda sabit 200'e oturur).

**Düzeltme.** `StockService.GetBalances(session, ids)` — tek sorgu, parametreli `IN` listesi.
Uç artık bir kez çağırıyor. **Kod değişikliği küçük ve tersine çevrilebilir; SQL semantiği aynı.**

**Doğrulama (yanlış bakiye göstermemek asıl risk):** `BulkBalanceTests` 6/6 —
toplu okuma tek-tek okumayla **birebir aynı** sonucu veriyor · hareketi olmayan malzeme 0 sayılıyor ·
**başka firmanın bakiyesi dönmüyor** · karışık istekte yalnız kendi firması dönüyor · boş liste sorgu atmıyor.

## İncelenip DÜZELTİLMEYENLER (gerekçeli)

| Yer | Durum | Neden dokunulmadı |
|---|---|---|
| `MaterialService.GetDetail` → muadiller | Muadil başına 1 sorgu | N = bir malzemenin muadil sayısı (tipik 0–5), yalnız **tek kayıt** açılırken. Ölçülebilir etki yok. |
| `TrashService.List` | Tablo başına 1 sorgu | Sabit tablo listesi (~10) — veri büyüklüğüyle artmıyor, N+1 değil |
| `/api/personnel` | Görünürde döngü | Hesap eşlemesi döngü **öncesinde tek sorguyla** alınıyor — zaten doğru yapılmış |
| Servislerdeki `foreach` + INSERT | Yazma yolları | Hepsi **tek transaction** içinde ve boyutu kullanıcı girdisiyle sınırlı (talep kalemleri vb.) |
| Migration döngüleri | Tek seferlik | Sürüm yükseltmesinde bir kez çalışır |

## Eşitleme (sync) tarafı — bulgu yok

`BusinessSyncService` snapshot/upsert yolu, `SyncGate`, idempotency (`operation_id` benzersiz),
`sync_inbox`/`sync_outbox` incelendi. Kayıt başına ek sorgu deseni **görülmedi**; `operation_id`
üzerindeki benzersiz index'ler tekrar (replay) korumasını sağlıyor. Bu bölümde değişiklik yapılmadı.

---

# #11-B — Foreign Key analizi

**62 tablo** ve ilişkileri çıkarıldı; **58 FOREIGN KEY** tanımı mevcut.

## Firma kolonu (`company_id`) taşımayan çocuk tablolar

| Tablo | Firma kolonu | Not |
|---|---|---|
| `material_request_items` | ✅ **var** | Migration 062 (M-S1a) ile eklendi |
| `maintenance_materials` | ✅ **var** | Migration 062 (M-S1a) ile eklendi |
| `request_status_history` | ❌ yok | **M-S1b** — kullanıcı tarafından ertelendi |
| `material_equivalents` | ❌ yok | ebeveyn üzerinden çözülüyor |
| `material_compatible_vehicles` | ❌ yok | ebeveyn üzerinden çözülüyor |
| `maintenance_definition_vehicles` | ❌ yok | ebeveyn üzerinden çözülüyor |
| `stock_count_lines` | ❌ yok | ebeveyn üzerinden çözülüyor |
| `vehicle_template_materials` | ❌ yok | ebeveyn üzerinden çözülüyor |
| `user_roles` | ❌ yok | kullanıcı üzerinden çözülüyor |

Bu tablolara yazan servisler **ebeveyn sahipliğini doğruluyor** (Paket 1'de T-1…T-6, Y-1, Y-2 ile
kapatıldı). Yani bilinen aktif bir sızıntı yok; kolon eklemek **derinlemesine savunma** olur.

## ✅ CANLI SALT-OKUMA TARAMASI YAPILDI (2026-08-09)

`depowise_prod` üzerinde **yalnız SELECT**. Kanıt: oturum `SET TRANSACTION READ ONLY` ile açıldı ve
bir yazma denemesi **SqlState 25006 ile reddedildi**; ardından transaction geri alındı.
Hiçbir INSERT/UPDATE/DELETE/ALTER/VACUUM/migration çalıştırılmadı.

### Sonuç: canlı veri TAMAMEN TEMİZ

| Tarama | Sonuç |
|---|---|
| Öksüz (orphan) kayıt — 15 ebeveyn-çocuk bağı | **0** |
| Firmalar arası bağ (bir çocuğun iki ebeveyni farklı firmada) — 7 kontrol | **0** |
| `company_id` geri doldurulamayan satır — 7 kontrol | **0** |

### Canlıda GERÇEKTEN VAR OLAN FK sayısı: **54**

Analizin ilk hâlinde "eksik FK" sanılanların büyük kısmı **zaten mevcut**:
`material_equivalents`, `maintenance_definition_vehicles`, `stock_count_lines`,
`vehicle_template_materials`, `request_status_history`, `user_roles` — hepsinin FK'leri kurulu.

**Tek gerçek eksik FK:** `material_compatible_vehicles.vehicle_id → vehicles.id`
(kodda zaten "Faz 08'de eklenecek" notu var, bilinçli ertelenmiş).

### 🔎 Migration'ın GEREKÇESİ ÇÜRÜDÜ — hazırlanmadı

M-S1a'da `company_id` eklemenin somut gerekçesi vardı: `BusinessSyncService` snapshot'ı firma
filtresini **yalnız company_id kolonu olan tablolara** uygular; o iki tablo filtresiz gidiyordu.

Bu tur kontrol edildi: **`BusinessSyncService.Tables` listesi 22 tablodur ve aday çocuk tabloların
HİÇBİRİ bu listede değildir.** Yani bu tablolar snapshot'a hiç girmiyor → M-S1a'daki sızıntı yolu
bunlar için **yok**.

Buna ek olarak:

| Tablo | Canlı satır sayısı |
|---|---|
| `material_equivalents` · `material_compatible_vehicles` · `maintenance_definition_vehicles` | **0** |
| `stock_count_lines` · `vehicle_template_materials` · `request_status_history` · `maintenance_materials` | **0** |
| `user_roles` | 8 |

Ve servis katmanı ebeveyn sahipliğini zaten doğruluyor (Paket 1: T-1…T-6, Y-1, Y-2).

**Üç şey birden doğru:** sızıntı yolu yok · tablolar boş · FK'ler zaten kurulu.
Bu durumda `company_id` migration'ı **gösterilmiş bir problemi çözmüyor**. Bu yüzden
migration dosyası **hazırlanmadı** — "çalışan şemayı gerekçesiz değiştirme" kuralı gereği.

### Kararınıza kalan tek şey (küçük)

`material_compatible_vehicles.vehicle_id` FK'si. Tablo **boş** olduğu için eklenmesi risksiz;
faydası, ileride silinmiş bir araca işaret eden satırı şemanın engellemesi. Yine de **şema
değişikliğidir** → onayınız olmadan hazırlanmadı/çalıştırılmadı.

> ℹ️ Önemli işletim notu: bu projede migration'lar **API açılışında otomatik koşar**
> (`MigrationRunner`). Yani bir migration dosyasını kataloğa eklemek, **bir sonraki API deploy'unda
> çalışacağı** anlamına gelir. Bu yüzden onaysız migration dosyası eklenmedi.

---

# #11-C — Benzersizlik (unique) ve index analizi

## İyi haber: firma sınırları ZATEN doğru

Endişe ettiğiniz nokta (`company_id + code` mi, global `code` mi) **doğru kurulmuş**:

```
ux_materials_code        ON materials(company_id, code)
ux_users_username        ON users(company_id, username)
ux_vehicles_internal_code ON vehicles(company_id, internal_code)
ux_material_requests_no  ON material_requests(company_id, doc_no)
ux_stock_documents_no    ON stock_documents(company_id, doc_type, doc_no)
ux_brands / ux_units / ux_suppliers / ux_vehicle_types / ux_vehicle_categories  → hepsi company_id ile
```

İki firma **aynı malzeme kodunu** kullanabilir — iş kuralı budur ve şema bunu doğru uyguluyor.

## Bilinçli olarak GLOBAL olanlar — doğru

`operation_id` üzerindeki benzersiz index'ler (`stock_movements`, `daily_activities`,
`vehicle_maintenances`, `fuel_*`, `sync_inbox/outbox`) firma bazlı **değildir** ve olmamalıdır:
bunlar GUID idempotency anahtarıdır; tekrar gönderimde ikinci hareketi bu index engeller.

## Benzersizliği OLMAYAN ve olmaması doğru olan

`personnel` — aynı isimde iki personel gerçek hayatta olabilir. Benzersizlik eklenmemeli.

## 🛑 DURULDU — eksik index'ler migration gerektiriyor

Bazı foreign key kolonlarında index yok (ör. çocuk tablolarda `parent_id` benzeri kolonlar).
PostgreSQL'de FK kolonuna index yoksa ebeveyn silme/güncelleme tablo taraması yapabilir.
Ancak **index eklemek de migration'dır** → uygulanmadı, onayınıza bırakıldı.

Gereksiz index bulunmadı.

---

# Sonuç ve öneri (2026-08-09 canlı tarama sonrası güncellendi)

**Uygulanan:** yalnız #11-A'daki N+1 düzeltmesi (küçük, testli, geri alınabilir, migration yok).
Doğrulama: SQLite 964 yeşil · **PostgreSQL 42 yeşil, 0 atlandı** · toplu okuma tek-tek okumayla birebir aynı.

**Yapılan ama uygulanmayan:** canlı salt-okuma tarama (yukarıda) — **veri temiz çıktı**.

**Migration hazırlanmadı** çünkü tarama gerekçeyi çürüttü: aday tablolar eşitleme kapsamında değil,
canlıda boş, FK'leri zaten kurulu ve servis katmanı sahipliği doğruluyor.

**Kararınıza kalan tek madde:** `material_compatible_vehicles.vehicle_id` FK'si — küçük, risksiz,
ama yine de şema değişikliği. "Ekle" derseniz migration'ı hazırlarım; **çalıştırmak yine ayrı onay
ister** (API deploy'u ile otomatik koşacağı için).

**M-S1b / M-S1d hakkında:** M-S1b'nin gerekçesi de aynı taramayla zayıfladı
(`request_status_history` eşitlenmiyor, canlıda 0 satır, FK'si var). Yeniden değerlendirilmeli;
"migration gerektiren bekleyen iş" olarak listede tutmak artık yanıltıcı.
