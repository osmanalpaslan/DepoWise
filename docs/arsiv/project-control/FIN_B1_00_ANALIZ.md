# FIN-B1 / Migration082 — FAZ 1 ANALİZ + FAZ 2 KARAR PAKETİ

> Tarih: **2026-08-29** · Aşama: **AŞAMA 3 — FINAL KARAR PAKETİ** · Durum: **FAZ 1 ✅ · FAZ 2 ✅ (ADR-185) · FAZ 3 ✅ UYGULAMA + TEST TAMAM · YAYIN ⏸️ "YAYINLA" BEKLİYOR**
>
> **ONAYLANAN KARARLAR: PK-FIN-01=A · PK-FIN-02=B · PK-FIN-03=C · PK-FIN-04=A · PK-FIN-05=A**
> → `sync_inbox` **FIN-B1 kapsamına ALINDI** (7. hedef) · normal UNIQUE index (CONCURRENTLY yok) ·
> FIN5 yeni sözleşmeye çevrilecek · **tek yayın**: Migration082 + kod + masaüstü **1.0.164**
> ⛔ **KOD YOK · MIGRATION YOK · TEST DEĞİŞİKLİĞİ YOK · PRODUCTION'A BAĞLANILMADI (SELECT dahil) · DEPLOY YOK**
> Tasarım temeli: `35d7bce` (ADR-179) · Geri çekme: ADR-180 · Bu analiz ARA İŞ 3'ü (kapalı) **açmaz**.

---

## 1. FAZ 0 — Durum doğrulaması

| Kontrol | Beklenen | Bulunan | Sonuç |
|---|---|---|---|
| Son yayın commit | `14f705b` | `14f705b` | ✅ |
| ARA İŞ 3 kod commit | `ab0d0d4` | `ab0d0d4` | ✅ |
| Çalışma ağacı | temiz | temiz (yalnız kullanıcının takip dışı dosyaları) | ✅ |
| Migration katalog azamisi | 81 | **81** (`Migration077…081`) | ✅ |
| `Migration082` kod dosyası | yok | **yok** (yalnız 6 test yorumunda metin) | ✅ |
| Aktif ara iş | yok | yok | ✅ |
| Ana devam noktası | AŞAMA 3 / FIN-B1 | aynı | ✅ |

---

## 2. FIN-B1 — iş problemi (A)

**Bulgu (FINAL simülasyonu):** 6 *eski* tabloda `operation_id` **tüm firmalar genelinde** benzersiz.
İdempotency (tekrar koruması) kontrolleri de firma süzgeçsiz. Sonuç: **B firmasının meşru işlemi,
A firmasında aynı `operation_id` kullanılmışsa sessizce atlanır** — hata verilmez, kayıt oluşmaz.

Bu davranış bugün testle **belgelenmiş durumda**:
`FinalStabilizasyonTests.FIN5_FarkliFirma_AyniOperationId_Bugun_Sessiz_Atlanir`
([FinalStabilizasyonTests.cs:151](tests/DepoWise.Tests/FinalStabilizasyonTests.cs:151)) — B firmasının
yakıt depo girişi `""` döner ve **0 kayıt** oluşur.

---

## 3. Mevcut davranış (B) — repository kanıtları

### 3.1 Küresel benzersiz 6 indeks (bugün)

| İndeks | Tablo | Tanım yeri |
|---|---|---|
| `ux_stock_movements_operation` | `stock_movements` | [Migration005_Materials.cs:123](src/DepoWise.Infrastructure/Database/Migrations/Migration005_Materials.cs:123) |
| `ux_vehicle_maintenances_op` | `vehicle_maintenances` | [Migration008_Maintenance.cs:63](src/DepoWise.Infrastructure/Database/Migrations/Migration008_Maintenance.cs:63) |
| `ux_fuel_depot_op` | `fuel_depot_entries` | [Migration009_FuelDailyActivity.cs:35](src/DepoWise.Infrastructure/Database/Migrations/Migration009_FuelDailyActivity.cs:35) |
| `ux_fuel_dist_op` | `fuel_distributions` | [Migration009_FuelDailyActivity.cs:56](src/DepoWise.Infrastructure/Database/Migrations/Migration009_FuelDailyActivity.cs:56) |
| `ux_daily_activities_op` | `daily_activities` | [Migration009_FuelDailyActivity.cs:81](src/DepoWise.Infrastructure/Database/Migrations/Migration009_FuelDailyActivity.cs:81) |
| `ux_assign_operation` | `assignment_movements` | [Migration076_Assignments.cs:57](src/DepoWise.Infrastructure/Database/Migrations/Migration076_Assignments.cs:57) |

Hepsi tek kolon: `ON <tablo>(operation_id)`.

### 3.2 Zaten doğru desendeki tablolar (karşılaştırma)

Yeni muhasebe tabloları **firma kapsamlı** kurulmuş — hedef desen zaten projede var:
- [Migration066_Parties.cs:91](src/DepoWise.Infrastructure/Database/Migrations/Migration066_Parties.cs:91) `ux_party_ledger_op ON party_ledger(company_id, operation_id)`
- [Migration067_Invoices.cs:142](src/DepoWise.Infrastructure/Database/Migrations/Migration067_Invoices.cs:142) `ux_invoices_op`
- [Migration068_Finance.cs:146](src/DepoWise.Infrastructure/Database/Migrations/Migration068_Finance.cs:146) `ux_finance_txn_op` · [:169](src/DepoWise.Infrastructure/Database/Migrations/Migration068_Finance.cs:169) `ux_invoice_alloc_op`

Servis tarafı da bunlarda firma süzgeçli: [FinanceService.cs:829](src/DepoWise.Infrastructure/Accounting/FinanceService.cs:829) ·
[InvoiceService.cs:554](src/DepoWise.Infrastructure/Accounting/InvoiceService.cs:554) ·
[PartyLedgerService.cs:181](src/DepoWise.Infrastructure/Accounting/PartyLedgerService.cs:181).

**Yani FIN-B1, yeni tabloların desenini eski tablolara yaymaktır — yeni bir desen icat etmez.**

### 3.3 Firma süzgeçsiz idempotency kontrolleri (bugün)

| # | Yer | Sorgu |
|---|---|---|
| 1 | [AssignmentService.cs:229](src/DepoWise.Infrastructure/Assignments/AssignmentService.cs:229) | `WHERE operation_id IN (@o,@o2,@o3)` |
| 2 | [MaintenanceService.cs:601](src/DepoWise.Infrastructure/Maintenance/MaintenanceService.cs:601) | `WHERE operation_id=@op` |
| 3 | [OpeningStockService.cs:99](src/DepoWise.Infrastructure/Materials/OpeningStockService.cs:99) | `WHERE operation_id=@op` |
| 4 | [DailyActivityService.cs:610](src/DepoWise.Infrastructure/Operations/DailyActivityService.cs:610) | `WHERE operation_id=@op` |
| 5–6 | `FuelService` `OperationExists` / `FindDistribution` | firma süzgeçsiz |
| 7–8 | `StockService` `FindDocumentByOperation`, `PurchaseOrderService`/`WorkOrderService` tüketim yolu | firma süzgeçsiz |

**Tutarsızlık kanıtı:** aynı dosyada içe-aktarım ön kontrolü **zaten firma kapsamlı** —
[FuelService.cs:171-181](src/DepoWise.Infrastructure/Operations/FuelService.cs:171) `OperationApplied(...)`
`WHERE operation_id=@op AND company_id=@c`. Yani yazma yolu firma-kör, önizleme yolu firma-kapsamlı.

### 3.4 `operation_id` gerçekte nasıl üretiliyor (risk için belirleyici)

| Yol | Üretim | Firmalar arası çakışma |
|---|---|---|
| Masaüstü/web normal işlem | `Guid.NewGuid().ToString("N")` (ör. [Stock.razor:541](src/DepoWise.Web/Components/Pages/Stock.razor:541)) | Pratikte **imkânsız** |
| Yakıt Excel içe aktarım | SHA-256(`fuel-import\|companyId\|satır\|…`) — [FuelImportService.cs:176-180](src/DepoWise.Infrastructure/Reporting/FuelImportService.cs:176) | **Hash'e companyId dahil → imkânsız** |
| Yakıt depo içe aktarım | SHA-256(`fuel-depot-import\|companyId\|…`) — [FuelDepotImportService.cs:116-120](src/DepoWise.Infrastructure/Reporting/FuelDepotImportService.cs:116) | **imkânsız** |
| **API — istemci gönderimi** | `d.OperationId` **istemciden kabul ediliyor** (Program.cs:1289, 1412, 1468-1474, 1720, 1749, 2137-2183, 2290, 2373, 2551) | **Serbest metin — kasıtlı çakışma mümkün** |

**Sonuç:** kazara çakışma olasılığı ≈ 0. Gerçek maruziyet: **istemciden gelen serbest `operation_id`**
(kimliği doğrulanmış bir kullanıcının kasıtlı/kopyalanmış id göndermesi), veri kopyalama/geri yükleme
ve gelecekte firma bilgisi içermeyen deterministik bir id üreticisi eklenmesi.

---

## 4. Hedef davranış (C)

- Aynı firma içinde tekrar (retry) → **bugünkü gibi** idempotent, ikinci kayıt yok.
- Farklı firmalarda aynı `operation_id` → **birbirini engellemez**; her firma kendi kaydını oluşturur.
- Hiçbir kayıt silinmez/dönüştürülmez; benzersizlik **gevşer**, sıkılaşmaz.

---

## 5. Masaüstü analizi (D) — ayrı incelendi

Masaüstü, `DepoWise.Infrastructure` servislerini **yerel SQLite** üzerinde doğrudan çalıştırır; yani
yukarıdaki 8 firma-kör kontrol masaüstünde de aynen çalışır. Yerel şema aynı migration kataloğuyla
yürür → Migration082 masaüstüne **uygulama güncellemesiyle** iner.

**Pratik etki:** bir masaüstü kurulumunun yerel veritabanı normalde **tek firma** barındırır (cihaz
token'ı tek `company_id`'ye bağlıdır — [SyncServer.cs:124-133](src/DepoWise.Infrastructure/Sync/SyncServer.cs:124)),
bu yüzden FIN-B1 hatası yerelde **tetiklenemez**. Migration082 masaüstünde zararsız bir indeks
değişimidir; asıl kazanç sunucu tarafındadır.

## 6. Web analizi (E) — ayrı incelendi

Web'in **kendi idempotency kopyası yoktur**; `operation_id` yalnız iki yorum satırında geçer
(Invoices.razor:307, Payments.razor:215 — çift gönderim notu). Web tüm işlemleri uzak API'ye yaptırır.
→ **FIN-B1 için web tarafında kod değişikliği gerekmez**; düzeltme API üzerinden otomatik gelir.
"İki platform aynıdır" varsayımı kullanılmadı: web ayrı tarandı, sonuç *farklı* çıktı (web'de değişiklik yok).

## 7. Ortak servis / API analizi (F)

Düzeltme tamamen `DepoWise.Infrastructure` servislerinde toplanır; API sözleşmesi (uç adları, alanlar,
tipler) **değişmez**. `operationId` istemciden gelmeye devam eder. Masaüstü ve sunucu aynı servis
kodunu paylaştığı için düzeltme **tek yerde** yapılır.

## 8. Domain (G) / Infrastructure (H)

Domain modelinde değişiklik yok. Infrastructure'da: 8 metot imzasına `companyId` parametresi + SQL'e
`company_id=@c` süzgeci (tasarım `35d7bce`'de mevcut).

## 9. Veritabanı (I)

`company_id` sütunu **6 tabloda da zaten var** (indeks bu sütunu kullanacak). Yeni sütun/tablo/ilişki
gerekmez. Yalnız 6 indeksin kolon listesi değişir.

---

## 10. Migration analizi (J) — derin

### 10.1 Neden gerekli?

Servis süzgeci **tek başına yetmez**: firma süzgeci eklenirse B firmasının kaydı artık "zaten var"
sayılmaz ve INSERT denenir → **küresel UNIQUE indeks ihlali → hata**. Yani sessiz atlama, sert hataya
dönüşür. Bu yüzden ADR-179 kod+migration'ı **çift** tasarlamıştır.

### 10.2 Tam olarak ne değişecek?

`35d7bce`'deki tasarım, 6 indeks için:
`DROP INDEX IF EXISTS <ad>; CREATE UNIQUE INDEX <ad> ON <tablo>(company_id, operation_id);`
**Aynı adlar korunur** — bu bilinçlidir: `StockBalanceWriter.IsDocumentNumberRace` indeks *adına*
bakarak yarış sınıflandırması yapar.

### 10.3 Cevaplar

| Soru | Cevap |
|---|---|
| Yeni tablo/sütun/constraint? | **Hayır** — yalnız 6 indeks yeniden kurulur |
| Şema 81 neden yetmez? | Küresel UNIQUE, firma kapsamlı benzersizliği ifade edemez |
| Mevcut kayıtlar dönüştürülür mü? | **Hayır** — hiçbir satıra dokunulmaz |
| Yeni alan nullable mı? | Yeni alan yok |
| Backfill? | **Gerekmez** — küresel benzersizliği sağlayan her veri, firma kapsamlı benzersizliği de otomatik sağlar (kısıt gevşiyor) |
| Duplicate riski? | **Yapısal olarak sıfır** (aynı sebeple) |
| Transaction sınırı | Runner **migration başına tek transaction** ([MigrationRunner.cs:33-45](src/DepoWise.Infrastructure/Database/Migrations/MigrationRunner.cs:33)); PostgreSQL'de DDL transaction'lıdır → başarısızlıkta **tam geri alma** |
| Migration başarısız olursa DB durumu | Transaction geri alınır; `schema_migrations` yazılmaz → şema **81'de kalır**, API açılışta yeniden dener |
| Lock riski | ⚠️ **VAR** — `CREATE UNIQUE INDEX` (CONCURRENTLY değil) PostgreSQL'de tabloya **ACCESS EXCLUSIVE** kilit alır; süre tablo boyutuyla orantılıdır. Canlı boyutlar **ölçülmedi** (production'a bağlanılmadı) |
| `CONCURRENTLY` kullanılabilir mi? | ⚠️ **Mevcut runner ile HAYIR** — `CREATE INDEX CONCURRENTLY` transaction içinde çalışamaz; runner her migration'ı transaction'a sarar. Kullanılacaksa **runner değişikliği** gerekir (kapsam büyür) |
| Rollback mümkün mü? | Evet — aynı adlarla tek kolonlu indeksleri geri kurmak. ⚠️ Ancak geri alma, arada oluşmuş firmalar-arası aynı-id kayıtları varsa **başarısız olur** (küresel UNIQUE yeniden kurulamaz) |
| Eski istemciler yeni şema ile çalışır mı? | **Evet** — indeks yapısı istemciye görünmez; API sözleşmesi değişmez |
| Migration yeni uygulama olmadan uygulanırsa? | Zararsız: benzersizlik gevşer, eski kod firma-kör kontrolüyle çalışmaya devam eder (bugünkü davranış sürer) |
| Yeni uygulama eski şema (81) ile çalışır mı? | ⚠️ **HAYIR (güvenli değil)** — firma süzgeçli kod + küresel indeks = farklı firma aynı id → **UNIQUE ihlali/hata** |
| **Doğru dağıtım sırası** | **Önce migration (82), sonra yeni uygulama.** Runner API açılışında çalıştığı için tek deploy'da sıra doğaldır; ancak birden çok API makinesi varsa geçiş anında eski kod yeni şemayla çalışır — bu **güvenli yön**dür |

---

## 11. Senkron analizi (K) — ⚠️ EN ÖNEMLİ BULGU

### 11.1 `sync_inbox` firma-kördür ve ADR-179 kapsamı DIŞINDADIR

- Küresel benzersiz indeks: `CREATE UNIQUE INDEX ux_inbox_operation ON sync_inbox(operation_id);`
  ([Migration001_CoreSchema.cs:166](src/DepoWise.Infrastructure/Database/Migrations/Migration001_CoreSchema.cs:166))
- Firma süzgeçsiz kontrol: [SyncServer.cs:145-152](src/DepoWise.Infrastructure/Sync/SyncServer.cs:145)
  `SELECT COUNT(*) FROM sync_inbox WHERE operation_id=@op;`
- `sync_inbox` tablosunda `company_id` **sütunu vardır** ve yazılır
  ([SyncServer.cs:154-169](src/DepoWise.Infrastructure/Sync/SyncServer.cs:154)) — yalnız **okumada ve
  benzersizlikte kullanılmaz**.

### 11.2 Sıralama sorunu — düzeltme senkron yolunda ETKİSİZ kalır

`Push` akışında inbox kontrolü **en başta**, servis katmanına ulaşmadan çalışır
([SyncServer.cs:39](src/DepoWise.Infrastructure/Sync/SyncServer.cs:39)):

```
Push → AuthDevice → [InboxHas: FİRMA-KÖR] → kritik doğrulama → servis → 6 tablo
                          ↑ burada "AlreadyApplied" dönerse alt katmana HİÇ inilmez
```

Ve senkronun **kritik** entity tipleri tam da FIN-B1 tablolarıdır:
`stock_movement`, `vehicle_maintenance`, `fuel_distribution`
([SyncModels.cs:20-23](src/DepoWise.Application/Sync/SyncModels.cs:20)).

**Sonuç:** ADR-179 tasarımı (6 indeks + 8 servis süzgeci) uygulansa bile, **çevrimdışı masaüstünden
senkronla gelen** işlemler için hata kapanmaz — firma-kör `sync_inbox` daha önce devreye girer.
Masaüstü bu projenin **birincil istemcisi** olduğundan bu, tasarımın etkinliğini doğrudan sınırlar.

ADR-179 `sync_inbox`'ı "senkron sözleşmesi değişmesin" gerekçesiyle **bilinçli** dışarıda bırakmıştır;
bu analiz o kararın **sonucunu** ölçüp karara sunar (PK-FIN-02).

### 11.3 Etkilenmeyenler

`server_changes`, pull/cursor, BranchMirror, LWW ve çakışma çözümü **etkilenmez**; push/pull
satırları `id` üzerinden upsert eder, bu indeksler senkron tekilleştirmesinde kullanılmaz.

---

## 12. Yetki (L) / Tenant (M) / BranchAccess (N) / Export (O)

- **Yeni yetki anahtarı GEREKMEZ** — kullanıcıya görünen yeni bir ekran/işlem yok; yetki ağacı, kategori,
  rol kalıtımı, admin bypass **değişmez**.
- **Tenant izolasyonu:** değişiklik izolasyonu yalnız **güçlendirir** (firma süzgeci ekler); hiçbir yetki
  tavanı gevşemez. `company_id` yine yalnız güvenilir session'dan gelir.
- **BranchAccess:** dokunulmaz — `operation_id` benzersizliği şube kavramından bağımsızdır.
- **Export:** etkilenmez.

---

## 13. UI / UX (P)

Kullanıcıya görünen değişiklik yok. Tek dolaylı etki: bugün **sessizce atlanan** bir işlem, düzeltme
sonrası normal şekilde **kaydedilir** (doğru davranış). Yeni ekran/alan/mesaj gerekmez.

---

## 14. Test mimarisi ve mevcut kilitler (Q)

**Bugün yürürlükte olan ve DEĞİŞMESİ gerekecek kilit:**
- `FIN5_FarkliFirma_AyniOperationId_Bugun_Sessiz_Atlanir` — **bugünkü hatalı davranışı kilitliyor**.
  FIN-B1 uygulanırsa bu test **tersine çevrilmelidir** (sözleşme değişikliği; testi "gevşetme" değil).

**Korunacak kilitler:** FIN1–FIN4 (aynı firma retry idempotent) · FIN8 · FIN9/FIN10 (SNK-05 sözleşmesi) ·
`BarkodQrTests` katalog-max bağlaması (**81 → 82 güncellenmeli**) · `PostgresMigrationTests`.

**Uygulanırsa zorunlu testler (öneri — bu turda YAZILMADI):**

| Kod | Test | Neden |
|---|---|---|
| FIN5' | Farklı firma aynı id → **ikisi de kaydolur** | Yeni sözleşme |
| FIN11 | Aynı firma retry hâlâ idempotent (6 tablo × ayrı) | Regresyon |
| FIN12 | Migration082 yalnız-indeks (kolon/satır dokunmadı) | Veri güvenliği |
| FIN13 | Hedef dışı indeks envanteri değişmedi | Yan etki |
| FIN14 | İndeks adları korundu (`IsDocumentNumberRace` bağımlılığı) | Yarış sınıflandırması |
| FIN15 | Migration idempotent (iki kez çalışınca bozulmaz) | Runner |
| PG082 | İzole PostgreSQL'de 6 indeks `(company_id, operation_id)` UNIQUE | İki lehçe |
| SNK-INBOX | (PK-FIN-02=B ise) farklı firma aynı id → inbox engellemez | Senkron |
| ESK-01 | Eski istemci (şema 82 + eski sözleşme) bozulmuyor | Geriye uyum |
| ROLL-01 | Migration hatasında şema 81'de kalıyor | Rollback |

Tasarım örneği git geçmişinde: `35d7bce:tests/DepoWise.Tests/PostgresMigration082Tests.cs`.

---

## 15. Performans (R)

- İndeks **tek kolondan iki kolona** çıkar → boyut marjinal artar; `company_id` önde olduğu için
  firma-içi sorgular **iyileşir veya aynı kalır**.
- Tek maliyet: migration anındaki **indeks yeniden kurma** süresi ve kilidi (bkz. §10.3).
- Canlı tablo boyutları **ölçülmedi** (production yasağı) → süre tahmini yapılamaz; yayın öncesi
  salt-okunur ölçüm planlanmalıdır.

## 16. Geriye uyumluluk (S) ve eski istemciler (T)

- API sözleşmesi değişmediği için **eski masaüstü istemciler (≤1.0.163) bozulmaz**.
- Eski istemciler, güncellenene kadar kendi **yerel** veritabanlarında eski (firma-kör) davranışı
  sürdürür — yerel DB tek firmalı olduğu için pratik etkisi yoktur.
- ⚠️ Ters yön riskli: **yeni kod + eski şema (81)** güvenli değildir (§10.3). Bu yüzden migration ve
  uygulama **birlikte** yayınlanmalıdır.

## 17. Production etkisi (U)

- Veri **dönüştürülmez**, satır silinmez, backfill yok.
- Tek gerçek etki: migration sırasındaki **kısa yazma kilidi**.
- Bu turda production'a **bağlanılmadı**; aşağıdakiler yalnız *yayın sırasında/sonrasında* yapılacak
  salt-okunur kontroller olarak planlanır:
  1. (Yayın öncesi) 6 tablonun satır sayısı ve indeks boyutu — kilit süresi tahmini için
  2. (Yayın öncesi) `pg_dump` yedeği alınmış mı
  3. (Yayın sonrası) 6 indeksin `(company_id, operation_id)` UNIQUE olduğu — `pg_index`'ten
  4. (Yayın sonrası) `schema_migrations` azamisi = 82
  5. (Yayın sonrası) `/health` 200 ve senkron akışının çalıştığı

## 18. Rollback (V)

| Senaryo | Sonuç |
|---|---|
| Migration sırasında hata | Transaction geri alınır → şema **81**, veri değişmemiş |
| Yayın sonrası geri dönüş isteği | Kod revert + **ters migration** (tek kolonlu indeksleri geri kur) |
| ⚠️ Ters migration riski | Arada **firmalar arası aynı `operation_id`** kayıt oluştuysa küresel UNIQUE **yeniden kurulamaz** → geri dönüş engellenir. Pratikte GUID üretimi nedeniyle olasılık ≈ 0, ama sıfır değil |

## 19. Güvenlik / veri izolasyonu (W) + regresyon riski (Y)

- Değişiklik izolasyonu **güçlendirir**; yetki tavanı gevşemez.
- Regresyon riski **düşük ve dar**: 8 servis metodu + 6 indeks. Rapor, export, senkron sözleşmesi,
  yetki ağacı, BranchAccess, tarih altyapısı (ARA İŞ 3) **etkilenmez**.
- ARA İŞ 3'ün `IsGunuTarihi` altyapısı bu işle **ilgisizdir**; yeniden tasarlanmayacaktır.

## 20. Yayın stratejisi (X)

Migration'lı ilk yayın olacağı için ARA İŞ 3'ten farklıdır: **yedek + tek deploy + doğrulama**.
Masaüstü sürümü 1.0.163 → 1.0.164 ile aynı pakette dağıtılır (yerel şema da 82'ye çıkar).

---

## 21. Risk matrisi

| # | Risk | Olasılık | Etki | Azaltma |
|---|---|---|---|---|
| R1 | Migration sırasında yazma kilidi | Orta | Orta (kısa kesinti) | Düşük trafikte yayın · tablo boyutu önceden ölçülür |
| R2 | Senkron yolunda düzeltme etkisiz kalır (§11) | **Yüksek** (tasarım gereği) | **Yüksek** (birincil istemci) | PK-FIN-02 |
| R3 | Yeni kod + eski şema karışımı | Düşük | Yüksek (hata) | Migration ve uygulama birlikte yayınlanır |
| R4 | Ters migration engellenir | Çok düşük | Orta | Geri dönüş penceresi kısa tutulur |
| R5 | FIN5 kilidinin tersine çevrilmesi yanlış anlaşılır | Düşük | Düşük | ADR'de sözleşme değişikliği olarak yazılır |
| R6 | Canlı tablo büyükse kilit uzar | Bilinmiyor (ölçülmedi) | Orta | Yayın öncesi salt-okunur ölçüm |

---

## 22. PK-FIN KARAR PAKETİ

> ✅ **KARARLAR ONAYLANDI (ADR-185, 2026-08-29):** PK-FIN-01=**A** · PK-FIN-02=**B** · PK-FIN-03=**C** ·
> PK-FIN-04=**A** · PK-FIN-05=**A**. Aşağıdaki seçenekler **kayıt amaçlıdır**, yeniden sorulmayacaktır.

### PK-FIN-01 — FIN-B1 uygulanacak mı, hangi biçimde? → **KARAR: A**

| | Seçenek |
|---|---|
| **A** | **ADR-179 tasarımı aynen** (6 indeks Migration082 + 8 servis firma süzgeci) |
| **B** | **Migration'sız savunma:** yalnız servis süzgeci eklenir; küresel indeks kalır → sessiz atlama yerine **açık hata** verilir (şema 81 korunur) |
| **C** | **Yapma / ertele:** bugünkü davranış korunur, FIN5 belgeleyici kilit olarak kalır |

- **Öneri: A** · **Gerekçe:** hatanın sınıfı gerçek ve veri **kaybına** yol açıyor (sessiz atlama);
  tasarım hazır ve kanıtlanmış (`35d7bce`); yeni tablolarda zaten aynı desen kullanılıyor; kısıt
  gevşediği için veri riski yapısal olarak sıfır. B, veri kaybını hataya çevirir ama **meşru işlemi
  yine engeller**; C hatayı canlıda bırakır.
- Kullanıcı etkisi: yok (görünmez düzeltme) · Veri etkisi: yok · Migration: **A→gerekli**, B/C→yok
- Sync etkisi: PK-FIN-02'ye bağlı · Eski istemci: bozulmaz · Rollback: §18 · Risk: R1, R3

### PK-FIN-02 — `sync_inbox` firma kapsamına alınacak mı? ⭐ → **KARAR: B (EVET, kapsama alındı)**

| | Seçenek |
|---|---|
| **A** | **Hayır** — ADR-179 kapsamı aynen; senkron sözleşmesine dokunulmaz (düzeltme senkron yolunda etkisiz kalır) |
| **B** | **Evet** — `ux_inbox_operation` da `(company_id, operation_id)` yapılır + `InboxHas` firma süzgeçli olur (aynı migration içinde) |
| **C** | **Ayrı iş** — FIN-B1 A ile yayınlanır, senkron kapsamı sonraki bir işe bırakılır |

- **Öneri: B** · **Gerekçe:** çevrimdışı masaüstü birincil istemcidir ve kritik senkron tipleri
  (`stock_movement`, `vehicle_maintenance`, `fuel_distribution`) tam da FIN-B1 tablolarıdır. A seçilirse
  FIN-B1 **yarım** kalır: hata yalnız doğrudan API çağrılarında kapanır, senkronla gelen işlemlerde
  açık kalır. B, aynı sınıftaki tek boşluğu kapatır ve **protokolü değiştirmez** (istek/yanıt biçimi,
  cursor, çakışma mantığı aynı; yalnız yinelenme kontrolü firma kapsamına girer).
- ⚠️ B'nin maliyeti: `sync_inbox` tablosu büyük olabilir → indeks yeniden kurma süresi daha uzun (R1/R6 artar).
- Migration: B → Migration082'ye 7. hedef eklenir · Sync sözleşmesi: **değişmez** (yalnız kapsam daralır)

### PK-FIN-03 — İndeks kurulum yöntemi (kilit süresi) → **KARAR: C**

| | Seçenek |
|---|---|
| **A** | **Normal** `CREATE UNIQUE INDEX` — transaction içinde, kısa ACCESS EXCLUSIVE kilit |
| **B** | **`CONCURRENTLY`** — kilitsiz, ama transaction dışı çalışmalı → **MigrationRunner değiştirilmeli** |
| **C** | Normal, ama **yayın öncesi tablo boyutu ölçülüp** düşük trafik saatinde uygulanır |

- **Öneri: C** · **Gerekçe:** B, runner'ın "migration başına tek transaction + tam rollback" garantisini
  bozar ve kapsamı mimari düzeye taşır (`.claude/rules` gereği dokunulmaması gereken alan). C, A'nın
  güvenliğini korur ve tek gerçek bilinmezliği (tablo boyutu) yayın öncesi ölçümle kapatır.
- Risk: R1, R6

### PK-FIN-04 — `FIN5` kilidinin tersine çevrilmesi → **KARAR: A**

| | Seçenek |
|---|---|
| **A** | FIN5 **yeni sözleşmeye** çevrilir (farklı firma → ikisi de kaydolur), eski hâli ADR'de kayda geçer |
| **B** | FIN5 korunur, yanına yeni test eklenir (çelişkili iki kilit — **uygulanamaz**) |

- **Öneri: A** · Bu bir **sözleşme değişikliğidir**, test gevşetmesi değildir; ADR'de açıkça yazılır.

### PK-FIN-05 — Yayın biçimi → **KARAR: A (tek yayın, masaüstü 1.0.164)**

| | Seçenek |
|---|---|
| **A** | **Tek yayın:** migration + kod + masaüstü 1.0.164 birlikte (yedek → deploy → doğrulama) |
| **B** | İki aşamalı: önce migration, doğrulandıktan sonra kod |
| **C** | Custom Rapor / Ekip fazlarıyla **birleştirilmiş** yayın |

- **Öneri: A** · **Gerekçe:** yeni kod eski şemayla güvenli değil (§10.3); B, aradaki pencerede eski
  kodu yeni şemayla çalıştırır (güvenli yön) ama iki deploy + iki doğrulama maliyeti getirir ve
  ARA İŞ 3'te kanıtlanan tek-deploy akışı yeterlidir. C, kapsamı büyütür (kullanıcı kuralı: ayrı faz).
- ⚠️ Ön koşul (her seçenekte): **`pg_dump` yedeği**.

---

## 23. Önerilen FAZ 3 sıra planı (onay gelirse)

| Sıra | Adım | Not |
|---|---|---|
| S1 | `35d7bce`'den Migration082 + katalog kaydı geri getirilir | PK-FIN-02=B ise 7. hedef (`sync_inbox`) eklenir |
| S2 | 8 servis idempotency kontrolüne `company_id` süzgeci | `35d7bce` aynen |
| S3 | (PK-FIN-02=B ise) `SyncServer.InboxHas` firma kapsamlı | Protokol değişmez |
| S4 | FIN5 yeni sözleşmeye çevrilir + FIN11–FIN15 eklenir | PK-FIN-04=A |
| S5 | `PostgresMigration082Tests` geri getirilir | İzole PG, guard'lı |
| S6 | `BarkodQrTests` katalog-max 81 → 82 | Kilit güncellemesi |
| S7 | Tam süit + izole PG + 3 Release build | Gevşetme yok |
| S8 | Yayın öncesi rapor + **DUR** | Ayrı `YAYINLA` onayı |

---

## 23.1 ⭐ PK-FIN-02 SONRASI: `sync_inbox`'ın fiziksel biçimi — KANITLA ÇÖZÜLDÜ

Kullanıcı §6'da haklı olarak sordu: *"`sync_inbox` için yalnız indeks değişikliği yeterli mi, yoksa
yeni sütun mu gerekiyor — bu 'yeni sütun yok' sınırıyla çelişir mi?"* **Cevap kanıtla nettir: çelişki yok.**

| Soru | Kanıt | Cevap |
|---|---|---|
| `company_id` inbox tablosunda var mı? | [Migration001_CoreSchema.cs:156-165](src/DepoWise.Infrastructure/Database/Migrations/Migration001_CoreSchema.cs:156) → `company_id TEXT NOT NULL` | ✅ **VAR ve NOT NULL** |
| Dolduruluyor mu? | [SyncServer.cs:154-169](src/DepoWise.Infrastructure/Sync/SyncServer.cs:154) `InsertInbox(... @c ...)` | ✅ **Her kayıtta yazılıyor** |
| Yeni sütun gerekiyor mu? | — | ❌ **HAYIR** — "yeni sütun yok" sınırı korunur |
| Mevcut kayıtlarda duplicate riski? | Bugün `operation_id` **küresel** benzersiz → `(company_id, operation_id)` çiftleri kendiliğinden benzersiz | ❌ **YOK** (kısıt gevşiyor) |
| Backfill gerekiyor mu? | `company_id` zaten dolu | ❌ **HAYIR** |
| Yalnız indeks değişikliği yeterli mi? | — | ✅ **EVET** — `ux_inbox_operation` → `(company_id, operation_id)` |
| Kod tarafında ne gerekiyor? | [SyncServer.cs:145-152](src/DepoWise.Infrastructure/Sync/SyncServer.cs:145) | `InboxHas` firma süzgeçli olacak (FAZ 3) |

**Sonuç:** `sync_inbox` diğer 6 hedefle **birebir aynı biçimdedir** → Migration082'nin **7. hedefi**
olur. Sync protokolü değişmez.

⚠️ **FAZ 3 başlangıcında yeniden doğrulanacak tek teknik nokta:** `sync_inbox` **büyüklüğü**. Bu tablo
her push işleminde birikir, dolayısıyla 6 operasyon tablosundan büyük olabilir → indeks yeniden kurma
süresi ve ACCESS EXCLUSIVE kilidi daha uzun sürebilir. PK-FIN-03=C gereği **yayın öncesi ölçülecektir**
(bu turda production'a bağlanılmadığı için ölçüm YAPILMADI).

## 23.2 FAZ 3'te analiz edilecek eski istemci senaryoları (karar gereği)

1. Eski desktop (≤1.0.163) + yeni şema (82)
2. Yeni desktop (1.0.164) + yeni şema (82)
3. Eski desktop + yeni API
4. Senkronla gelen eski `operation_id` davranışı
5. Migration sonrası eski istemcinin insert/update davranışı
6. Rollback sonrası eski istemci davranışı

## 23.3 FAZ 3 UYGULAMA KAYDI (2026-08-29)

### S1 — Fiziksel model yeniden doğrulandı (FAZ 1 körlemesine kabul edilmedi)

| Doğrulama | Sonuç |
|---|---|
| 7 hedef indeksin tamamı küresel tek kolon | ✅ doğrulandı (Migration001:166 · 005:123 · 008:63 · 009:35/56/81 · 076:57) |
| 7 tabloda `company_id` sütunu | ✅ **hepsinde `TEXT NOT NULL`** |
| `sync_inbox.company_id` | ✅ **var, NOT NULL, `InsertInbox`'ta dolduruluyor** → yeni sütun gerekmedi |
| Katalog azamisi (uygulama öncesi) | 81 |
| Runner transaction modeli | migration başına tek transaction + rollback |

**Servis noktaları yeniden sayıldı: 9 sorgu** (FAZ 1'de "8" olarak yazılmıştı; `FuelService`'te iki ayrı
yardımcı olduğu için gerçek sayı 9'dur). Ayrıca `FuelService.OperationApplied` (önizleme) **zaten firma
kapsamlıydı** — dokunulmadı.

### S2 — Migration082 oluşturuldu

`Migration082_OperationIdCompanyScope` — **7 hedef**, aynı adlarla `DROP INDEX IF EXISTS` +
`CREATE UNIQUE INDEX ... (company_id, operation_id)`. Yeni tablo/sütun/backfill/veri dönüşümü **YOK**.
`CONCURRENTLY` **kullanılmadı** (PK-FIN-03=C). Katalog azamisi **81 → 82**.

### S3 — 9 idempotency sorgusu firma kapsamına alındı

| # | Yer | Değişiklik |
|---|---|---|
| 1 | `AssignmentService.Idempotent` | `company_id=@c AND operation_id IN (@o,@o2,@o3)` (devir çifti dahil) |
| 2 | `MaintenanceService.FindByOperation` | `company_id=@c AND operation_id=@op` |
| 3 | `OpeningStockService.OperationApplied` | `company_id=@c AND operation_id=@op` |
| 4 | `DailyActivityService.FindActivity` | `company_id=@c AND operation_id=@op` |
| 5 | `FuelService.OperationExists` | `company_id=@c AND operation_id=@op` |
| 6 | `FuelService.FindDistribution` | `company_id=@c AND operation_id=@op` |
| 7 | `StockService.FindDocumentByOperation` | `mv.company_id=@c AND mv.operation_id LIKE @op` |
| 8 | `PurchaseOrderService` (mal kabul) | `company_id=@c AND operation_id LIKE @op` |
| 9 | `WorkOrderService` (İE tüketim) | `company_id=@c AND operation_id LIKE @op` |

`company_id` her zaman `s.CompanyId`'den (güvenilir oturum) gelir — istemciden alınmaz.

### S4 — `sync_inbox` düzeltmesi (PK-FIN-02=B)

- **İndeks:** Migration082'nin 7. hedefi.
- **Kod:** `SyncServer.InboxHas` artık `company_id=@c AND operation_id=@op`; çağrı `Push` içinde
  `AuthDevice`'tan gelen `companyId` ile yapılır (**istemci gönderemez**).
- **Senkron protokolü DEĞİŞMEDİ:** istek/yanıt biçimi, cursor, çakışma çözümü, SNK-05(a) aynen; yalnız
  yinelenme kontrolünün kapsamı firmaya daraldı.

### S5 — Test sözleşmeleri

- **FIN5 yeni sözleşmeye çevrildi** (PK-FIN-04=A): `FIN5_FarkliFirma_AyniOperationId_Iki_Ayri_Kayit_Olusur`.
  Eski ad/gövde hatalı davranışı kilitliyordu; **silinmedi, tersine çevrildi** ve tarihçesi test
  belgesine yazıldı. Aynı-firma retry kilidi test içinde ayrıca korundu.
- **Yeni kilitler:** FIN11 (açılış/stok çapraz-firma) · FIN12 (zimmet) · FIN13 (bakım — yabancı kayıt
  id'si döndürmez) · **FIN16 (sync_inbox aynı firma idempotent)** · **FIN17 (sync_inbox çapraz-firma
  engellenmez)** · FIN18 (7 indeks UNIQUE + kolon sırası + ad korundu) · FIN19 (yalnız-indeks: kolonlara
  dokunulmadı) · FIN20 (katalog azamisi 82) · **FIN21 (gerçek 81→82 yükseltme: mevcut veri korunur)** ·
  **FIN22 (rollback: migration patlarsa şema 81'de kalır)**.
- `PostgresMigration082Tests` geri getirildi (izole PG, guard'lı; 7 hedefi `Targets` listesinden doğrular).
- `BarkodQrTests.BAR15` kataloğa bağlı olduğu için **sabit güncellemesi gerekmedi**; yalnız açıklaması güncellendi.
- **Hiçbir test silinmedi veya gevşetilmedi.**

### Platform ayrımı (karar gereği korundu)

- **Web:** kendi idempotency kopyası olmadığı için **web'de kod değişikliği YAPILMADI** (gereksiz paralel
  mantık üretilmedi).
- **Masaüstü:** aynı ortak servisleri yerel SQLite'ta çalıştırır → düzeltme oraya da iner; yerel DB tek
  firmalı olduğu için davranış değişmez. Release derlemesiyle doğrulandı.

### S6–S7 — Doğrulama sonuçları (2026-08-29)

| Doğrulama | Geçen | Başarısız | Atlanan |
|---|---|---|---|
| **Tam test süiti** | **3.036** | **0** | 40 |
| **İzole PostgreSQL** (127.0.0.1:5544) | **53** | **0** | **0** |
| API Release | 0 hata | | |
| Web Release | 0 hata | | |
| Masaüstü Release | 0 hata | | |

Önceki tur: 3.026 / 0 / 39 → **+10 yeni test** (FIN11–FIN13, FIN16–FIN22 ve PG 082 testi).
PG turunda atlanan **0** → guard gevşetilmedi, testler gerçekten koştu.

> ⚠️ **Şeffaflık notu — İLK TEŞHİS YANLIŞTI, KÖK NEDEN BULUNDU (yayın öncesi kontrolde):**
> PG turunda 40 test başarısız oldu. İlk açıklamam "eşzamanlı derleme çakışması" idi; yayın öncesi
> kontrolde tur **tek başına** tekrarlanınca hata **yeniden üretildi** → o açıklama YANLIŞTI.
>
> **Gerçek kök neden:** `PostgresTestGuard` **K4 kuralı — test veritabanı 50 MB'ı aşarsa guard
> istisna fırlatır** (`MaxDbSizeMb = 50`). Yerel scratch PostgreSQL'i tekrarlanan koşumlarla şişmişti
> (koşum başlangıcında **36,5 MB**); tur ilerledikçe DB 50 MB'ı aşıyor ve kalan testler guard'da
> patlıyordu — bu yüzden tekil sınıflar geçiyor, toplu tur düşüyordu (13 geçti / 40 düştü / 3 sn).
>
> **Çözüm:** izole test veritabanı boşaltılıp yeniden oluşturuldu (7,7 MB) → tur **53/53**.
> **Guard GEVŞETİLMEDİ** — tersine, guard tasarlandığı gibi çalıştı ve kendi scratch ortamımın
> bakımsızlığını yakaladı. Bu bir **ürün kusuru DEĞİLDİR**; Migration082 veya FIN-B1 koduyla ilgisi
> yoktur (tüm PG testleri temiz DB'de geçiyor). Gizlenmedi, düzeltilerek kayda geçirildi.

## 23.4 YAYIN ÖNCESİ SON KONTROL (2026-08-29) — kod/migration/test DEĞİŞTİRİLMEDİ

### A) Depo ve migration doğrulaması

| Kontrol | Sonuç |
|---|---|
| HEAD | **`d9fc350`** · origin/master ile **birebir eşit** |
| Çalışma ağacı | **temiz** (takip edilen kirli dosya: 0) |
| Takip dışı dosyalar | `SECURITY_CREDENTIAL_ROTATION_PLAN.md`, `kilavuzlar/` — **dokunulmadı, commit edilmedi** |
| Katalog sırası | `…080 → 081 → **082**` (son kayıt) |
| Migration082 kimliği | `Version => 82` · `Name => "operation_id_company_scope"` |
| Hedef sayısı | **7** (6 operasyon tablosu + `sync_inbox`) |
| Yasaklı DDL/DML (çalışan kodda) | `CREATE TABLE` 0 · `ALTER TABLE` 0 · `ADD COLUMN` 0 · `INSERT` 0 · `UPDATE` 0 · `DELETE` 0 · `DROP TABLE` 0 |
| `CONCURRENTLY` | çalışan SQL'de **YOK** (yalnız 2 açıklama satırında geçiyor) |
| Firma-kör kalan idempotency sorgusu | **0** |
| Firma kapsamlı sorgu | 13 (9 FIN-B1 + `sync_inbox` + zaten doğru olan 3 muhasebe sorgusu) |

### B) İzole PostgreSQL'de indeks doğrulaması (production'a bağlanılmadan)

`pg_indexes` çıktısı — **7/7 doğru**, adlar korundu, hepsi `UNIQUE … (company_id, operation_id)`;
`schema_migrations` azamisi **82**.

### D) Boyut ölçümü — PK-FIN-03=C

⚠️ **PRODUCTION BOYUTU ÖLÇÜLMEDİ** (production erişimi yasak). Aşağıdaki rakamlar **yalnız izole test
veritabanına** aittir ve **canlı boyut için gösterge DEĞİLDİR** (test DB'sinde veri yok denecek kadar az):

| Tablo | Satır | Toplam | operation indeksi |
|---|---|---|---|
| `fuel_distributions` | 5 | 80 kB | 16 kB |
| `assignment_movements` | 0 | 48 kB | 8 kB |
| `daily_activities` | 0 | 40 kB | 8 kB |
| `stock_movements` | 0 | 40 kB | 8 kB |
| `fuel_depot_entries` | 0 | 32 kB | 8 kB |
| `vehicle_maintenances` | 0 | 32 kB | 8 kB |
| **`sync_inbox`** | 0 | 24 kB | 8 kB |

**`sync_inbox` canlı boyutu BİLİNMİYOR** ve tahmin edilmeyecektir. Yayın anında (yedekten sonra,
migration'dan önce) salt-okunur olarak ölçülmesi **zorunlu adımdır**.

### E) Yayın riski — yeniden değerlendirme

| Konu | Değerlendirme |
|---|---|
| `CREATE UNIQUE INDEX` kilidi | PostgreSQL'de tabloya kısa **ACCESS EXCLUSIVE** kilit; süre tablo boyutuyla orantılı → boyut ölçülmeden yayın yapılmamalı |
| `sync_inbox` büyüklüğü | **Bilinmiyor** — her push'ta biriktiği için en büyük hedef olabilir; ana kilit riski budur |
| Runner davranışı | migration başına tek transaction; DDL transaction'lı → hatada **tam geri alma** |
| Migration başarısız | şema **81**'de kalır, veri değişmez (FIN22 ile kanıtlandı) |
| Migration başarılı | şema **82** |
| Kod + migration birlikte | **ZORUNLU** — yeni kod + eski şema 81 güvenli DEĞİL (firma süzgeçli kod + küresel indeks = UNIQUE ihlali) |
| Eski kod + yeni şema 82 | **Güvenli yön** (benzersizlik gevşemiş; eski kod çalışmaya devam eder) |
| Ters migration | Mümkün, ancak arada **firmalar arası aynı `operation_id`** kayıt oluşmuşsa küresel UNIQUE yeniden kurulamaz → geri dönüş engellenir |
| Bu riskin olasılığı | **Çok düşük**: normal işlemde `operation_id` **GUID**; Excel içe aktarımda hash'e `companyId` dahil. **Ancak API `operationId`'yi istemciden kabul ettiği için teorik olarak mümkündür** → rollback penceresi kısa tutulmalı |

### F) Yayın paketi ve önerilen sıra (BU TURDA UYGULANMADI — yalnız plan)

**Paket:** Migration082 · 9 idempotency düzeltmesi · `sync_inbox` düzeltmesi · güncellenmiş FIN testleri ·
API · Web · Masaüstü **1.0.164**. (ARA İŞ 3'ün 1.0.163 yayını **değiştirilmez**.)

1. **`pg_dump` yedeği** (zorunlu ön koşul)
2. Yedek doğrulaması
3. **Salt-okunur boyut ölçümü** (7 tablo + indeksler, özellikle `sync_inbox`) — PK-FIN-03=C
4. API + Web deploy (Migration082 API açılışında runner ile uygulanır)
5. Migration sonucu doğrulama: `schema_migrations` azamisi **82** + 7 indeksin kolonları
6. API/Web sağlık kontrolü
7. Kritik salt-okunur doğrulamalar
8. Masaüstü **1.0.164** paketi + yayını
9. Yayın sonrası doğrulama

## 24. Faz takip tablosu

| Faz | Durum |
|---|---|
| FAZ 0 — durum doğrulama | ✅ TAMAM |
| FAZ 1 — analiz | ✅ TAMAM (bu belge) |
| FAZ 2 — karar paketi | ✅ **KARARLAR ONAYLANDI (ADR-185)** — PK-FIN-01=A · 02=B · 03=C · 04=A · 05=A |
| FAZ 3 — uygulama | ✅ **TAMAMLANDI** — Migration082 (7 hedef) + 9 idempotency sorgusu + `InboxHas` + 10 yeni test |
| TEST | ✅ **TAMAM** — tam süit 3.036/0 · izole PG 53/53 · 3 Release 0 hata |
| YAYIN | ⏸️ **"YAYINLA" onayı bekliyor** (tek yayın: Migration082 + kod + masaüstü **1.0.164**) |

## 25. Git durumu ve production teyidi

- Analiz turunda **kod/migration/test değişikliği YAPILMADI**; yalnız bu belge + durum belgeleri.
- **Production'a hiçbir istek gönderilmedi — SELECT dahil.** Canlı ölçüm yapılmadı, deploy yapılmadı.
- ARA İŞ 3 kapalı ve yayınlanmış durumda; bu analiz onu **açmadı**. Ana roadmap sırası değişmedi.
