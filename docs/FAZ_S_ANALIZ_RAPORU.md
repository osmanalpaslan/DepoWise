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

## 🛑 DURULDU — migration gerektiriyor

Eksik FK eklemek ya da yukarıdaki tablolara `company_id` eklemek **şema değişikliğidir**.
Talimatınız gereği uygulanmadı. Onay isterseniz gereken bilgiler:

1. **Neden gerekli?** Yalnız derinlemesine savunma ve veri tutarlılığı — bilinen aktif bir hata yok.
2. **Hangi tablolar?** Yukarıdaki ❌ satırları (M-S1b kapsamı dahil).
3. **Ne değişir?** Yeni `company_id` kolonu + index; bazı yerlerde yeni FK.
4. **Risk?** **Önce canlıda öksüz (orphan) kayıt taraması ZORUNLU.** Ebeveyni silinmiş bir çocuk
   satır varsa FK eklenemez; migration yarıda kalır.
5. **Öksüz/duplicate veri var mı?** **BİLİNMİYOR** — canlı veritabanına salt-okuma sorgusu gerekiyor.
   Bu rapor hazırlanırken canlıya bağlanılmadı.
6. **Büyüklük?** M-S1a'ya benzer (2 tablo) değil; burada 6–7 tablo → **daha büyük**.
7. **Geri alınabilir mi?** Evet — M-S1a'daki gibi kolon düşürme betiği yazılabilir (SQLite'ta önce
   index düşürülmeli; bu ders M-S1a'da alındı).
8. **SQLite/PostgreSQL uyumu?** M-S1a deseni: PG'de `ADD COLUMN → backfill → SET NOT NULL`,
   SQLite'ta tablo yeniden kurulur. Kanıtlanmış yöntem.
9. **Kesinti?** Beklenmiyor (migration API açılışında koşar), ama tablo sayısı arttığı için süre uzar.
10. **Yayın sırası?** Önce API (migration onunla koşar), sonra web, sonra masaüstü paketi.

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

# Sonuç ve öneri

**Şu an uygulanan:** yalnız #11-A'daki N+1 düzeltmesi (küçük, testli, geri alınabilir, migration yok).

**Onayınızı bekleyen (hiçbiri uygulanmadı):**
1. Canlıda **salt-okuma öksüz/duplicate taraması** — FK kararı bunsuz verilemez.
2. Kalan çocuk tablolara `company_id` (M-S1b dahil) — derinlemesine savunma.
3. Eksik FK index'leri.

**Önerim:** önce (1) — salt-okuma, risksiz, ve (2)/(3)'ün gerçekten gerekip gerekmediğini
belirleyecek tek şey o. Öksüz kayıt yoksa migration basit; varsa önce veri onarımı gerekir.
