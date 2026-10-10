# GENEL PROJE DURUM RAPORU

- **Tarih:** 2026-08-09
- **Yöntem:** salt-okuma. Canlı veriye **hiçbir yazma yapılmadı**, migration çalıştırılmadı, deploy yapılmadı.
- **Kapsam dışı (kullanıcı sınırı):** Claude Code ayarları/izinleri ve o konuyla ilişkili her şey;
  kapsamlı güvenlik denetimi/pentest. Firma izolasyonu bulguları **yalnız raporlanmıştır, düzeltilmemiştir.**

---

## 1. GENEL DURUM

Proje **çalışan, canlı kullanımda olan bir ürün** — ancak *tek gerçek kullanıcıyla* (baban) ve çok küçük
veriyle. Olgunluk seviyesi:

| Alan | Seviye |
|---|---|
| Çekirdek iş akışları (stok, araç, bakım, yakıt, faaliyet, talep) | **Olgun** — uçtan uca çalışıyor |
| Veri bütünlüğü (transaction, ters kayıt, eşzamanlılık) | **Olgun** — CAS + retry, atomik iptal, defter-tabanlı stok |
| Çok firmalı (multi-tenant) izolasyon | **Kısmen** — ana yollar kapalı, **6 nokta açık** (bkz. §7) |
| Web/masaüstü paritesi | **Kısmen** — listeleme/oluşturma eşit, **düzenleme ve Excel eksik** |
| Test | **Güçlü** — 839 test, kritik senaryolar kapsanmış |
| Dokümantasyon | **Kısmen yanlış** — CLAUDE.md mimari bölümü gerçeği yansıtmıyor (bkz. §2) |

**Kod büyüklüğü:** Masaüstü 25.437 satır · Infrastructure 19.444 · Test 17.418 · Web 13.697 ·
API 3.408 · Application 2.355. Toplam ~82.000 satır, 551 dosya.

---

## 2. DOKÜMANTASYON ↔ GERÇEK UYUŞMAZLIĞI (önemli)

**`CLAUDE.md` satır 53-54 YANLIŞ:**

> "API/sunucu veritabanı: **SQLite** (`depowise-server.db`, Fly.io kalıcı disk `/data`) — planlanan
> PostgreSQL/Drizzle hiç üretime alınmadı, gerçek çalışan sistem uçtan uca SQLite"

**Gerçek:** Sunucu **2026-07-24'ten beri PostgreSQL** üzerinde (Neon, `depowise_prod`, PostgreSQL 17.10).
Bu raporun tüm canlı ölçümleri PostgreSQL'den alındı. `DEVAM.md` ve `docs/GOREV_PANOSU.md` doğru;
yalnız **CLAUDE.md'nin "Mimari değişmezler" bölümü eski.** Bu bölüm gelecekteki kararları yanlış
yönlendirebileceği için düzeltilmeli (P1).

Diğer belgeler (`DEVAM.md`, `YARIM_KALAN_ISLER.md`, `GOREV_PANOSU.md`) gerçekle **uyumlu** bulundu.

---

## 3. MİMARİ (gerçek durum)

```
Masaüstü (Avalonia/.NET 8)        Web (Blazor Server/MudBlazor)
  ├─ yerel SQLite                   └─ kendi veritabanı YOK
  └─ Infrastructure (ortak)              └─ HTTP → API
        │                                        │
        └──────── /sync/* + /api/sync/* ─────────┤
                                                 ▼
                              API (Fly.io depowise-erp) → PostgreSQL (Neon)
```

- **Ortak katman:** `DepoWise.Infrastructure` hem masaüstü hem API tarafından kullanılıyor →
  iş kuralı **tek yerde**. Web iş kuralı taşımıyor, yalnız API'yi tüketiyor.
- **Lehçe soyutlaması:** `DbConnection` tabanlı; farklar `SqlDialect`, `DbIntrospect`, `DialectPurge`
  içinde toplanmış.
- **Modüller:** Materials, Vehicles, Maintenance, Operations (fuel + daily activity), Requests,
  Reporting, Security, Sync, Org/Organization, Settings, Update, Files.

---

## 4. DATABASE / ŞEMA DURUMU

**Şema sürümü: 62** (canlı PostgreSQL) — 62 migration, **çakışma yok, boşluk yok, dosya adı ↔ sürüm
tutarlı, katalogda eksik yok**. Toplam **56 tablo**.

### Canlı veri hacmi (salt-okuma)
| Tablo | Satır | | Tablo | Satır |
|---|---|---|---|---|
| materials | 2463 | | stock_movements | 667 |
| stock_balances | 664 | | login_attempts / sessions | 321 / 321 |
| material_categories | 269 | | vehicles | 94 |
| vehicle_models | 55 | | app_releases | 47 |
| vehicle_categories | 29 | | units | 13 |
| users | 8 | | branches | 6 |
| companies | **3** | | personnel | 3 |
| material_requests / _items | 2 / 2 | | vehicle_maintenances | **0** |

### company_id taşımayan 13 tablo
| Tablo | Firmalı ebeveyni | Eşitlemede mi? | Değerlendirme |
|---|---|---|---|
| `request_status_history` | VAR (material_requests) | Hayır | **M-S1b adayı** |
| `maintenance_definition_vehicles` | VAR | Hayır | **M-S1b adayı** |
| `material_compatible_vehicles` | VAR (materials) | Hayır | M-S1b adayı |
| `material_equivalents` | VAR (materials) | Hayır | M-S1b adayı |
| `stock_count_lines` | VAR (stock_documents) | Hayır | M-S1b adayı |
| `vehicle_template_materials` | VAR (vehicle_templates) | Hayır | M-S1b adayı |
| `user_roles` | VAR (users) | Hayır | kabul edilebilir |
| `user_list_preferences` | YOK | Hayır | kullanıcı tercihi — firma bağımsız |
| `companies`, `schema_migrations`, `app_releases`, `machine_resets`, `role_grant_limits` | — | Hayır | sistem tabloları, doğru |

**Kritik tespit:** company_id'siz tabloların **hiçbiri eşitleme listesinde değil** → M-S1a ile kapatılan
sızıntı sınıfı **kapalı kalmaya devam ediyor**. Ancak bu tablolar **API üzerinden** okunabiliyor (bkz. §7).

### SQLite ↔ PostgreSQL farkları
- Lehçe farkı olan 7 migration `SqlDialect`/`IsSqlite` ile ayrılmış; yalnız `Migration053` PG'ye özel
  (Türkçe collation), `Migration062` iki yollu (PG: SET NOT NULL · SQLite: tablo yeniden kurma).
- Genel kod SQLite'a özel yapıları `SqlDialect.PortableSql` ile çeviriyor.
- **Kaçak var:** `rowid` (bkz. §7 KD-1).

---

## 5. SERVİS VE İŞ KURALLARI (doğrulandı)

| Servis | Transaction | Atomiklik | Idempotency | Ters kayıt | Audit |
|---|---|---|---|---|---|
| `StockService` | `BeginImmediate` | ✅ | operation_id | ✅ ters belge | ✅ |
| `StockBalanceWriter` | CAS + 3 tekrar | ✅ | ✅ | — | log |
| `MaintenanceService` | `BeginImmediate` | ✅ | operation_id + `FindByOperation` | ✅ `usage_reverse` | ✅ |
| `DailyActivityService` | `BeginImmediate` | ✅ **tek işlem** | erken çıkış (zaten iptal) | bakım üzerinden | ✅ `reverse` |
| `FuelService` | transaction | ✅ | zaten-iptal kontrolü | bakiye guard'ı | ✅ `reverse` |
| `RequestService` | `BeginImmediate` | ✅ | durum makinesi | — | ✅ |
| `BusinessSyncService` | satır bazlı | kısmi (satır izole) | PK upsert + LWW | — | çakışma kaydı |

**Bakiye yazımı tek noktada** (`StockBalanceWriter`) toplanmış; `StockService`, `MaintenanceService`,
`DailyActivityService`, `OpeningStockService` aynı korumayı kullanıyor. **Fiziksel silme yok**
(tek istisna ADR-083 firma kalıcı silme).

---

## 6. MODÜLLER ARASI İLİŞKİLER (FK ile doğrulandı)

```
daily_activities ──maintenance_id──> vehicle_maintenances ──> maintenance_materials ──> materials
       └──vehicle_id──> vehicles                                      └── stock_movements (usage/usage_reverse)
material_requests ──> material_request_items ──> materials
stock_documents ──> stock_movements ──> materials ──> stock_balances
fuel_depot_entries / fuel_distributions ──vehicle_id──> vehicles
```

**Rapor tutarlılığı kontrol edildi:** Araç maliyet raporu bakım malzemesini `maintenance_materials`'tan
(`vm.is_cancelled=0` filtreli), parça çıkışını `stock_documents`+`stock_movements`'tan okuyor —
**çifte sayım yok**, iptal edilen bakım maliyete girmiyor. Bakım raporundaki alt sorgu da dış sorgunun
`is_cancelled=0` filtresi altında çalışıyor. ✅ Bu alanda tutarsızlık bulunmadı.

---

## 7. TESPİT EDİLEN PROBLEMLER

### 🔴 KD-1 · Stok Hareketleri sunucuda ÇALIŞMIYOR (mevcut, açık)
- `GET /api/stock` ve `/api/stock/movements` → **500** · `42703: column sm.rowid does not exist`
- Neden: `StockService.cs:246` ve `:284` → `ORDER BY sm.created_at DESC, sm.rowid DESC`.
  `rowid` **SQLite'a özel**; PostgreSQL'de yok.
- **2026-08-05 (`8a644fe`) tarihinden beri var.** Masaüstü etkilenmiyor (SQLite).
- Etki: web'de "Stok Hareketleri" ekranı hiç açılmıyor.

### 🟠 Firma izolasyonu — API üzerinden 6 açık nokta
Statik analiz + elle kod doğrulaması ile bulundu. **Hiçbiri düzeltilmedi.**

| # | Uç | Servis | Sorun | Etki |
|---|---|---|---|---|
| T-1 | `GET /api/stock/balance/{materialId}` | `StockService.GetBalance(materialId)` | **Session parametresi bile yok**, firma filtresi yok | Başka firmanın malzeme **stok miktarı okunur** |
| T-2 | `PUT /api/maintenance/definitions/{id}` | `MaintenanceDefinitionService.SetVehicles` | Üst tanımın firması doğrulanmıyor | Başka firmanın bakım tanımı-araç bağlantıları **değiştirilebilir (YAZMA)** |
| T-3 | `GET /api/maintenance/definitions/{id}/vehicles` | `GetVehicleIds` | aynı | Başka firmanın araç id'leri okunur |
| T-4 | `GET /api/requests/{id}/history` | `RequestService.GetHistory(requestId)` | **Session parametresi yok** | Başka firmanın talep durum geçmişi okunur |
| T-5 | `GET /api/request-ops/{id}/history` | `RequestOperationsService.GetHistory` | Üst talebin firması doğrulanmıyor | Operasyon geçmişi (kim/ne zaman/gerekçe) okunur |
| T-6 | `GET /api/users/{id}/roles` | `UserService.GetRoleKeys(actor, userId)` | `actor` alınıyor ama **kullanılmıyor**; yetki kontrolü de yok | Başka firmanın kullanıcı rolleri okunur |

**Bugünkü gerçek risk düşük:** sistemde tek aktif firma var ve id'ler 32 haneli rastgele GUID
(tahmin edilemez). Ama ikinci firma aktifleştiğinde bunlar **gerçek sızıntıya** dönüşür.
T-2 tek **yazma** açığı olduğu için en önceliklisi.

**Yanlış alarm olduğu doğrulananlar** (guard'ı var): materials/vehicles/personnel silme,
talep onay/ret/iptal (`TenantAccessGuard.EnsureOwnership`), `PermissionService.GetForUser`
(`EnsureUserOwned`), lookup ekleme (`EnsureWritableTable`), firma uçları (süper admin kısıtlı).

### 🟡 Orta
- **Doküman-gerçek uyuşmazlığı** (CLAUDE.md, §2) — yanlış mimari kararlara yol açabilir.
- **Eşitleme yazma yolunda üst kayıt doğrulaması yok (M-S1d):** sunucu gelen satıra kendi firmasını
  zorluyor, ama satırın **üst kaydının** aynı firmaya ait olduğunu ayrıca doğrulamıyor. FK sağlam
  olduğu için "olmayan üst kayıt" imkânsız; ama çok firmalı senaryoda sıkılaştırılmalı.
- **Excel içe aktarma web'de yok** — yalnız masaüstünde (`ImportExportView`).

### 🟢 Düşük / teknik borç
- Migration'ların çoğunda dosya-içi idempotency kontrolü yok; koruma yalnız `schema_migrations`
  kaydında. Kayıt kaybolursa migration tekrar çalışıp hata verir. (Runner tasarımı gereği kabul edilebilir.)
- `Program.cs` 3.408 satır ve 243 uç tek dosyada — bakım zorluğu.
- 20 test atlanıyor (PostgreSQL testleri, ortam değişkeni yoksa) — bilinçli.

---

## 8. WEB / MASAÜSTÜ PARİTESİ

**Web'de olup masaüstünde olmayan (bilinçli, süper admin ekranları):** Firma Yetki Kontrol,
Rol Yetki Kontrol, Kalıcı Silme, Firma İş Verisini Sıfırla, Makine Yedekleri, Kota İzleme, Canlı Sunucu.

**Masaüstünde olup web'de olmayan:** **Excel içe/dışa aktarma ekranı (`ImportExportView`)** ← gerçek eksik.

**Düzenleme (edit) paritesi:**

| Ekran | Web | Masaüstü | Durum |
|---|---|---|---|
| Malzemeler | ✅ | ✅ | eşit |
| Araçlar | ✅ | ✅ | eşit |
| Personel | ✅ güçlü | ⚠️ zayıf | **masaüstü eksik** (iş #4) |
| Talepler | ✅ | ⚠️ zayıf | **masaüstü eksik** (iş #4) |
| Günlük Faaliyet | ❌ | ❌ | **iki tarafta da yok** (iş #5) |
| Bakım | ⚠️ | ⚠️ | **iki tarafta da yok** (iş #5) |
| Yakıt | ❌ | ❌ | **iki tarafta da yok** (iş #5) |

**Son iki işin paritesi doğrulandı:** Yakıt iptali ve Günlük Faaliyet iptali her iki tarafta da var
("İptal Et" butonu, "İptal edilenleri göster" kutusu, etki gösteren onay penceresi).

---

## 9. TEST DURUMU (gerçek çalıştırma)

| Küme | Sonuç |
|---|---|
| Tüm takım (SQLite) | **839 geçti · 0 başarısız · 20 atlandı** (2 dk 59 sn) |
| PostgreSQL testleri (boş test veritabanı) | **29 geçti · 0 başarısız · 1 atlandı** (5 dk 51 sn) |
| Derleme | 0 hata, 3 önceden var olan uyarı |
| Flaky | Gözlenmedi (aynı takım bu oturumda 3 kez aynı sonucu verdi) |

⚠️ **Doğrulanamadı:** PostgreSQL koşusunda **1 test atlandı**. Aynı takım bu oturumda daha önce
30/30 geçmişti. Atlamanın `PostgresTestGuard` güvenlik kontrolünden (test veritabanının o anki
durumu) kaynaklandığı değerlendiriliyor, ancak **hangi test olduğu bu koşuda tespit edilemedi**
(çıktı sade kipte alındı). Başarısız test yok; yine de takip edilmeli.

**Test kapsamındaki boşluklar:**
| Alan | Durum |
|---|---|
| Multi-company izolasyon | ⚠️ **API düzeyinde test YOK** — T-1…T-6 bu yüzden fark edilmemiş |
| Transaction rollback | ✅ var (İş 2 rollback kanıt testi) |
| Eşzamanlılık | ✅ var (PostgreSQL gerçek paralel testler) |
| İptal/ters kayıt | ✅ var (yakıt 14, günlük faaliyet 14) |
| Sync | ⚠️ kısmi — snapshot izolasyonu test edildi, **apply/upsert tenant testi yok** |
| Yetkilendirme | ✅ var, ama **uç (endpoint) düzeyinde değil, servis düzeyinde** |
| SQLite/PostgreSQL parite | ⚠️ kısmi — yalnız migration ve eşzamanlılık; **sorgu paritesi test edilmiyor** (KD-1 bu yüzden kaçtı) |
| Migration | ✅ güçlü (M-S1a 14+6 test) |
| API/masaüstü parite | ❌ yok |

---

## 10. CANLI SİSTEM (salt-okuma)

| Kontrol | Sonuç |
|---|---|
| API `/health` | **200** |
| API kritik uçlar (materials, vehicles, requests, maintenance, daily/grid, fuel, stock/change-log) | hepsi **200** |
| `GET /api/stock`, `/api/stock/movements` | **500** ← KD-1 |
| Web sayfaları (`/`, `/login`, `/materials`, `/vehicles`, `/daily`, `/maintenance`, `/fuel`, `/requests`, `/reports`, `/stock`) | hepsi **200** |
| Şema sürümü | **62** |
| Firma izolasyonu (eşitleme paketi) | DEPOWISE paketinde yabancı firma satırı **0** ✅ |
| Masaüstü yayın sürümü | **1.0.133** |
| Neon geri dönüş noktası | `pre-ms1a` dalı **ready** |

---

## 11. GİT / ÇALIŞMA DURUMU

- Dal: **master** · origin ile **tam senkron** (0 ileri / 0 geri) · bekleyen push **yok**
- Commit edilmemiş kendi değişikliğim **yok**
- Son 6 commit M-S1a ve güvenlik işlerini kapsıyor, hepsi push edilmiş

---

## 12. ÖNCEKİ İŞLERİN KOD ÜZERİNDEN DOĞRULANMASI

| İş | İddia | Kod doğrulaması |
|---|---|---|
| Günlük Faaliyet iptal akışı | tek atomik işlem | ✅ `DailyActivityService.cs:449` `BeginImmediate` + `:468` `CancelInTransaction` + `:474` `is_deleted=1` — **doğru** |
| MaintenanceService transaction | ortak transaction desteği | ✅ `:169` `CancelInTransaction`, `:174` `CancelCore` — **doğru** |
| Stok ters hareketleri | `usage_reverse` yazılıyor | ✅ `:190` `reverse: true` + `:406` `usage_reverse` — **doğru** |
| Yakıt iptali (İş 1) | iptal + prev_meter taşıma | ✅ `CancelDepotEntry`, `CancelDistribution`, `GetCancelledPrevMeter` — **doğru** |
| Eşzamanlılık (Faz 3-Ön) | CAS + 3 tekrar, 4 serviste | ✅ `StockBalanceWriter` + 4 servis kullanıyor — **doğru** |
| `company_id` migration (M-S1a) | NOT NULL, varsayılansız, indeksli | ✅ canlıda doğrulandı: NOT NULL ✓, varsayılan yok ✓, 2 indeks ✓, yanlış firma 0 ✓ |
| Sync izolasyonu | yabancı firma satırı gelmiyor | ✅ canlı `business-pull` ile doğrulandı |
| 1.0.132 / 1.0.133 yayınları | yayınlandı | ✅ sunucudaki en güncel sürüm **1.0.133** |
| Web/masaüstü paritesi | "eşit" | ⚠️ **kısmen doğru** — son işler eşit, ama düzenleme ve Excel'de gerçek fark var (§8) |

**Sonuç: "tamamlandı" denen işlerin tamamı kodda gerçekten mevcut.** Yalnız "web/masaüstü paritesi"
ifadesi fazla iyimser — son işler için doğru, genel olarak değil.

---

## 13. ÖNCELİKLENDİRME

### P0 — Önce yapılmalı

**P0-1 · KD-1: Stok Hareketleri sunucuda 500 veriyor**
- **Sorun:** `rowid` PostgreSQL'de yok → `/api/stock`, `/api/stock/movements` çöküyor.
- **Neden önemli:** Canlıda bir ekran **hiç çalışmıyor**; kullanıcı veriye erişemiyor.
- **Dosyalar:** `StockService.cs:246`, `:284`
- **Bağımlılık:** yok
- **Çözüm:** ikincil sıralama anahtarını iki lehçede de çalışan bir kolonla değiştir (örn. `sm.id`)
  veya `SqlDialect` üzerinden üret.
- **Migration:** ❌ gerekmez · **Canlı veri etkisi:** ❌ yok (yalnız SELECT)
- **Test:** SQLite + PostgreSQL sıralama testi (bu sınıf hatayı kalıcı olarak yakalar)
- **Risk:** **Düşük**

**P0-2 · T-2: Bakım tanımı-araç bağlantısında firma doğrulaması yok (YAZMA)**
- **Sorun:** `SetVehicles` üst tanımın firmasını doğrulamıyor.
- **Neden önemli:** Tek **yazma** izolasyon açığı — başka firmanın verisi değiştirilebilir.
- **Dosyalar:** `MaintenanceDefinitionService.cs:165`, `:151`
- **Çözüm:** tanımın firmasını doğrulayan guard (`LoadDefinition` deseni zaten var).
- **Migration:** ❌ · **Canlı veri etkisi:** ❌ · **Test:** iki firmalı yetki testi
- **Risk:** **Düşük** (düzeltme küçük) / açık kalırsa **Orta-Yüksek**

### P1 — Sıradaki geliştirme

**P1-1 · T-1, T-3, T-4, T-5, T-6: kalan 5 okuma izolasyonu açığı** — aynı desende guard ekleme.
Migration ❌, canlı etki ❌, test: iki firmalı API testi. **Risk: Düşük**

**P1-2 · API düzeyinde çok-firmalı izolasyon test takımı** — bugün böyle bir test **yok**; T-1…T-6
bu yüzden fark edilmedi. Yeni uç eklendiğinde otomatik yakalayacak koruma. **Risk: Düşük**

**P1-3 · CLAUDE.md mimari bölümünü gerçekle eşitle** (§2). **Risk: Düşük**

**P1-4 · Onaylı sıranın 4. maddesi: ortak düzenleme altyapısı + Personel/Talepler çift tık** —
masaüstü paritesi. **Risk: Orta** (birçok ekrana dokunur)

### P2 — Normal geliştirme
- **P2-1** Günlük Faaliyet + Bakım + Yakıt kaydı **düzenleme** (iş #5) — iki tarafta da yok. **Risk: Orta**
- **P2-2** Düzenleme kilitleri (iş #6) — aynı kaydı iki kişi. **Risk: Orta**
- **P2-3** Excel içe aktarma → Web (iş #7). **Risk: Düşük**
- **P2-4** M-S1b: `request_status_history` + `maintenance_definition_vehicles` (+4 tablo) firma kolonu.
  **Migration GEREKİR** · **Canlı veri etkisi: VAR** · **Risk: Orta** (M-S1a deseni hazır)
- **P2-5** M-S1d: eşitleme yazma yolunda üst kayıt firma doğrulaması. **Risk: Orta**

### P3 — İyileştirme
- **P3-1** M-S1c: yeni tabloda firma kolonu unutulmasını engelleyen otomatik test. **Risk: Düşük**
- **P3-2** `Program.cs` (3.408 satır, 243 uç) modüllere bölme. **Risk: Orta** (geniş refactor)
- **P3-3** Kalan işler: çoklu malzeme + şube sürüm kontrolü, LookupBox, kolon kataloğu, Faz S. **Risk: Düşük-Orta**

---

## 14. ÖNERİLEN GELİŞTİRME SIRASI

1. **P0-1 KD-1 stok hareketleri düzeltmesi** — canlıda kırık ekran · Risk: Düşük
2. **P0-2 T-2 yazma izolasyonu** — tek yazma açığı · Risk: Düşük
3. **P1-1 kalan 5 okuma izolasyonu açığı** · Risk: Düşük
4. **P1-2 API çok-firmalı izolasyon test takımı** — 1-3'ü kalıcı kılar · Risk: Düşük
5. **P1-3 CLAUDE.md düzeltmesi** · Risk: Düşük
6. **P1-4 ortak düzenleme altyapısı + Personel/Talepler** (onaylı sıranın 4. maddesi) · Risk: Orta
7. **P2-1 Günlük Faaliyet/Bakım/Yakıt düzenleme** · Risk: Orta
8. **P2-2 düzenleme kilitleri** · Risk: Orta
9. **P2-3 Excel → Web** · Risk: Düşük
10. **P2-4 M-S1b migration** · Risk: Orta — **onay gerektirir**
11. **P2-5 M-S1d** · Risk: Orta
12. **P3** iyileştirmeler

**1–5 arası hepsi migration'sız, canlı veriye dokunmayan, düşük riskli işlerdir** ve toplamda
tek bir yayın turuna sığar.

---

## 15. ONAYINI GEREKTİREN İŞLER

| İş | Neden onay gerekir |
|---|---|
| **P2-4 M-S1b** (`request_status_history` + 5 tablo firma kolonu) | **Veri taşıyan migration** — canlı veritabanına yazar |
| Herhangi bir **deploy** (API/web) | API deploy'u bekleyen migration'ları çalıştırır |
| **Masaüstü paket yayını** | Kullanıcıların yerel veritabanı migrate olur |
| **Git geçmişi temizliği** | Force-push gerektirir, tüm klonları etkiler |
| P3-2 `Program.cs` bölme | Geniş refactor, geri dönüşü zahmetli |

**Onay gerektirmeyenler:** P0-1, P0-2, P1-1, P1-2, P1-3 — hepsi kod değişikliği + test; migration yok,
canlı veriye yazma yok. (Yayınlanmaları için yine deploy onayın gerekir.)

---

## 16. SIRADAKİ İŞ ÖNERİM

**P0-1 (KD-1) + P0-2 (T-2) + P1-1 (kalan 5 izolasyon açığı) + P1-2 (izolasyon test takımı)
tek iş paketi olarak.**

Gerekçe: dördü de küçük, migration'sız, canlı veriye dokunmayan değişiklikler; aynı test altyapısını
paylaşıyorlar ve tek yayın turunda çıkabilirler. KD-1 şu anda canlıda kırık bir ekran olduğu için
en acil olanı; izolasyon açıkları ise ikinci firma aktifleşmeden kapatılmalı.
