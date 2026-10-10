# ARA İŞ 4 — CUSTOM RAPOR — FAZ 0 + FAZ 1 ANALİZ

> Tarih: **2026-08-29** · Aşama: **AŞAMA 3 — FINAL KARAR PAKETİ** · Durum: **FAZ 0 ✅ · FAZ 1 ✅ · FAZ 2 ✅ KARARLAR ONAYLANDI (ADR-186) · FAZ 3 ⏸️ "UYGULAMA BAŞLASIN" BEKLİYOR**
>
> **ONAYLANAN KARARLAR: PK-CR-01…08 = A (sekizinin tamamı)** — ayrıntı §11.5
> ⛔ **KOD YOK · MIGRATION YOK · TEST DEĞİŞTİRİLMEDİ/ÇALIŞTIRILMADI · PRODUCTION'A BAĞLANILMADI (SELECT dahil) · DEPLOY YOK**
> Bu analiz ARA İŞ 3'ü ve FIN-B1'i **açmaz**; ana roadmap sırası **değişmez**.

---

## 1. İŞ ADI VE AMAÇ

**Custom Rapor (Rapor Tasarımcısı).** Kullanıcının, geliştirici müdahalesi olmadan kendi raporunu
tanımlayabilmesi: kaynak seç → kolonları seç/sırala → filtre ver → çalıştır → kaydet → katalogda görün.

**Amaç (bu tur):** kapsamı repository kanıtıyla çıkarmak ve uygulamadan önce karar gerektiren
noktaları saptamak.

---

## 2. FAZ 0 — DURUM DOĞRULAMA

| Kontrol | Sonuç |
|---|---|
| HEAD | **`17c552f`** · origin/master ile **birebir eşit** |
| Çalışma ağacı | **temiz** (takip edilen kirli dosya: 0) |
| Takip dışı | `SECURITY_CREDENTIAL_ROTATION_PLAN.md`, `kilavuzlar/` — **dokunulmadı** |
| Migration katalog azamisi | **82** (`Migration082_OperationIdCompanyScope`); 083+ **YOK** |
| Custom Rapor için mevcut migration | **YOK** |
| Geri çekilmiş Custom Rapor migration'ı | **YOK** (yorumlarda geçen `Migration082` metinleri FIN-B1'e aittir, Custom Rapor'la ilgisizdir) |
| **Custom Rapor kodu** | **HİÇ YOK** — `custom_report` / `CustomReport` deseni `src/` ve `tests/` altında **0 dosya** |
| Aktif ara iş (öncesi) | YOK · yayın havuzu BOŞ |

**Kapsam kaynağı (uydurulmadı):** `ARA_IS_2_00_ANALIZ.md` **§İŞ 6 — CUSTOM RAPOR TASARIMCISI**
(satır 278–311), fizibilite bulguları ve taslak karar noktalarıyla birlikte. Orada "AYRI FAZ" olarak
işaretlenmiş; `MASTER_ROADMAP.md` ve `CURRENT_PHASE.md` de bunu "başlanmadı" olarak taşıyor.

⚠️ ARA_IS_2'deki fizibilite bulguları **PAKET-1, ARA İŞ 3 ve FIN-B1'den ÖNCE** yazılmıştı. Aşağıdaki
FAZ 1, o iddiaların tamamını **bugünkü kodla yeniden doğrular**.

---

## 3. MEVCUT DURUM — KANITLAR

### 3.1 Rapor altyapısı: `TableModel`'in altı gerçekten jenerik ✅ (doğrulandı)

```
TableModel(Title, Headers, Rows, Numeric?, TotalRow?)
```
[ReportModels.cs:15-20](src/DepoWise.Application/Reports/ReportModels.cs:15) — kolon adları ve satırlar
**veri**; hiçbir rapora özel tip yok. Dinamik üretilen bir rapor, masaüstü grid'i · web tablosu · Excel
dışa aktarımı ve API yanıtını **sıfır UI değişikliğiyle** besler. **Bu, işin en büyük kolaylaştırıcısıdır.**

### 3.2 Katalog ve dağıtım: kapalı yapı ❌ (ana engel — doğrulandı)

- `ReportCatalog.All` = **25 sabit `ReportDescriptor`** ([ReportCatalog.cs:164](src/DepoWise.Application/Reports/ReportCatalog.cs:164))
- `ReportCatalog.ByKey(key)` = dizide arama ([:360](src/DepoWise.Application/Reports/ReportCatalog.cs:360))
- `ReportService.Run` bilinmeyen anahtarda **istisna atar**:
  `ReportCatalog.ByKey(key) ?? throw new ArgumentException("Bilinmeyen rapor tipi: " + key)`
  ([ReportService.cs:1946](src/DepoWise.Infrastructure/Reporting/ReportService.cs:1946))
- `Dispatch` = **kapalı `key switch`** ([ReportService.cs:2017](src/DepoWise.Infrastructure/Reporting/ReportService.cs:2017))

→ Dinamik rapor için **hem katalog çözümleyicisi hem dağıtıcı** genişletilmelidir.

### 3.3 `ReportService.Run` — 4 güvenlik kapısı (Custom Rapor'un uyması ZORUNLU)

| # | Kapı | Kanıt | Custom Rapor'a etkisi |
|---|---|---|---|
| 1 | Katalog çözümlemesi | [:1946](src/DepoWise.Infrastructure/Reporting/ReportService.cs:1946) | Dinamik tanım katalog nesnesine dönüşmeli |
| 2 | **Yönetici raporu kapısı** (RPR-07) `desc.IsManager && !IsAdmin` → 403 | [:1960](src/DepoWise.Infrastructure/Reporting/ReportService.cs:1960) | Tanım "yönetici raporu mu" bilgisini taşımalı |
| 3 | **Veri modülü kapısı** (RPR-15) `desc.DataModule` role kapatılmışsa → 403 | [:1979](src/DepoWise.Infrastructure/Reporting/ReportService.cs:1979) | ⭐ Tanım, **okuduğu ekranın modülünü** bildirmeli; yoksa "kapalı ekranın verisi rapordan okunamaz" güvencesi DELİNİR |
| 4 | **Kategori yetkisi** (ADR-181/RPT-YETKI) `AccessControl.Require(s, CategoryModule(desc.Category), View)` | [:1994](src/DepoWise.Infrastructure/Reporting/ReportService.cs:1994) | Tanımın kategorisi olmalı |

`CategoryModule` **kapalı switch, 9 kategori**; tanımsız kategoride **istisna atar**
([ReportCatalog.cs:149-162](src/DepoWise.Application/Reports/ReportCatalog.cs:149)).

Ayrıca: `RequiresDate` → sunucu tarafında "Bu Ay" varsayılanı zorlanır ([:1997](src/DepoWise.Infrastructure/Reporting/ReportService.cs:1997));
`maxRows` tavanı **bellekte** kesilir ([:2009](src/DepoWise.Infrastructure/Reporting/ReportService.cs:2009)).

### 3.4 `ReportDescriptor` alanları

`Key · Name · Description · Category · Group · Filters · RequiresDate · ExportButton · InfoNote? · DataModule?`
([ReportCatalog.cs:83-99](src/DepoWise.Application/Reports/ReportCatalog.cs:83)) — bir custom rapor
tanımının **en az bu alanları üretebilmesi** gerekir.

### 3.5 Güvenli yapı taşları (hazır)

- `GridQuery` — parametreli filtre üretici ([Infrastructure/Database/GridQuery.cs](src/DepoWise.Infrastructure/Database/GridQuery.cs))
- `ListColumns` — Türkçe etiketli **kolon beyaz-listesi**: 3 katalog / **36 kolon**
  ([Application/Ui/ListColumns.cs](src/DepoWise.Application/Ui/ListColumns.cs))
- `ExcelCenterService` — "ham SQL yazmaz, veri sahibi servisten geçer" doktrini
- `ReportLimits.DefaultMaxRows = 50_000`, ayarla değiştirilebilir, 1000 altına inmez
  ([ReportLimits.cs:11-22](src/DepoWise.Application/Reports/ReportLimits.cs:11))

---

## 4. PLATFORM ANALİZİ (ayrı ayrı — varsayım YOK)

### 4.1 Masaüstü — ÇEVRİMDIŞI, yerel yürütme ⭐

`ReportsViewModel` raporu **yerel** çalıştırır:
`DesktopServices.Reports.Run(_session, SelectedReport.Key, req, maxRows)`
([ReportsViewModel.cs:594](src/DepoWise.Desktop/ViewModels/ReportsViewModel.cs:594)).
Yani rapor motoru masaüstünde **SQLite üzerinde, internetsiz** çalışır.

**Sonuç:** custom rapor **tanımı** masaüstünün yerel veritabanında bulunmazsa, masaüstü o raporu
çevrimdışı **çalıştıramaz**. Bu, senkron kararını (PK-CR-02) doğrudan belirler.

### 4.2 Web — ÇEVRİMİÇİ, API üzerinden

`Reports.razor` `ApiClient` ile `/api/reports/catalog` ve `/api/reports/{type}` uçlarını çağırır
([Reports.razor:5,12](src/DepoWise.Web/Components/Pages/Reports.razor:5)). Web'in kendi rapor motoru
**yoktur** → sunucudaki tek motor değişince web otomatik alır.

### 4.3 Mimari sınır — KORUNACAK

`DepoWise.Web.csproj` içinde **`ProjectReference` YOK**; ortak kod **dosya paylaşımıyla** gelir
(`<Compile Include="..\DepoWise.Application\...">` — 8 dosya, ör. `ListColumns.cs:27`,
`AppScreens.cs:54`). Custom Rapor için web'e iş katmanı referansı **eklenmemelidir**; gereken tip
varsa aynı `<Compile Include>` desenine eklenir.

### 4.4 API uçları (mevcut)

`/api/reports/catalog` · `/api/reports/{type}` · `/api/reports/{type}/export`
([Program.cs:2977, 3016, 3033](src/DepoWise.Api/Program.cs:2977)).
**Sözleşme değişikliği gerekmeyebilir** — custom rapor anahtarları aynı uçlardan akabilir (PK-CR-03).

---

## 5. VERİTABANI VE MIGRATION ANALİZİ

- **Custom rapor tanımı için tablo YOK** → tanımların saklanacağı **yeni tablo gerekir** ⇒ **MIGRATION GEREKİR**.
  Bu, varsayım değil: `custom_report` deseni kodda ve migration'larda **0 sonuç**.
- Migration azamisi **82**; yeni tablo **Migration083** olur (bu turda **oluşturulmadı**).
- **Yetki tarafında migration GEREKMEZ:** `user_permissions.module_key TEXT NOT NULL` **serbest metindir**
  ve benzersizlik `(user_id, module_key)` üzerindedir
  ([Migration001_CoreSchema.cs:83,93](src/DepoWise.Infrastructure/Database/Migrations/Migration001_CoreSchema.cs:83))
  → **rapor başına dinamik yetki anahtarı çalışma zamanında, migration'sız** verilebilir.
- **Yetki AĞACI statiktir:** `AppScreens.Sections/Groups/All` sabit diziler
  ([AppScreens.cs:90,101,140](src/DepoWise.Application/Security/AppScreens.cs:90)) → dinamik rapor
  anahtarlarının ağaçta görünmesi için **ekleme noktası** gerekir (kod, migration değil).

---

## 6. SENKRON ANALİZİ

`BusinessSyncService` tablo listesi **FK sırasına göre dizilmiş 63 kayıttır**; her tablo ayrıca
**push yetki kapısı** haritasına girer (ör. `["announcements"] = "announcements"` —
[BusinessSyncService.cs:119,186](src/DepoWise.Infrastructure/Sync/BusinessSyncService.cs:119)).

**Duyurular deseni (emsal):** FK yalnız `companies`/`branches` → sıra bağımlılığı yok; masaüstü
çevrimdışı okur/yazar. Custom rapor tanım tablosu aynı desene uyarsa **düşük riskli** eklenir.

**Eski istemci davranışı — DOĞRULANMASI GEREKEN NOKTA:** ARA_IS_2 "eski istemciler bilinmeyen tabloyu
SESSİZCE YOK SAYAR — kanıtlı" diyor. Bu iddia duyurular (ADR-173) turunda kanıtlanmıştı; **FAZ 3
başında bugünkü senkron kodu üzerinde yeniden doğrulanmalıdır** (bu turda kod okundu, davranış
testle doğrulanmadı — test çalıştırma yasağı).

**Senkron protokolü:** değişmesi **gerekmez** — yalnız tablo listesine ekleme yapılır (ADR-185'te
`sync_inbox` için yapıldığı gibi protokol dokunulmadan kapsam değişikliği mümkündür).

---

## 7. YETKİ / TENANT / BRANCHACCESS / EXPORT

- **Tenant:** tanım satırı `company_id` taşımalı; **çalıştırma** her zaman sahibi servisin
  tenant süzgecinden geçer (custom rapor kendi SQL'ini yazmazsa izolasyon otomatik korunur).
- **BranchAccess:** mevcut raporlar şube kapsamını kendi sorgularında uygular; custom rapor mevcut
  servisleri çağırırsa **aynen korunur**. Ham SQL'e izin verilirse **korunmaz** → PK-CR-01'in güvenlik gerekçesi.
- **Dört kapı** (§3.3) custom raporlar için de **zorunlu** kalmalıdır.
- **Export:** `ExportButton` alanı zaten tanımda; Excel yolu `ExcelCenterService` beyaz-listesinden geçer.

---

## 8. PERFORMANS ANALİZİ

| Risk | Durum |
|---|---|
| Dinamik SQL / SQL injection | Ham SQL **verilmezse** yapısal olarak yok (PK-CR-01=A) |
| Tüm tabloyu çekme | `maxRows` tavanı **bellekte** kesiyor ([:2009](src/DepoWise.Infrastructure/Reporting/ReportService.cs:2009)) → büyük kaynaklarda tavan **SQL'e inmeli** (emsal: `stock-movements` `LIMIT @lim`) |
| Bellek | 50.000 satır × çok kolon → masaüstünde de aynı süreçte; export sırasında ikinci kopya |
| SQLite vs PostgreSQL | İki lehçe `SqlDialect` ile ayrışıyor; custom sorgu üreticisi **iki lehçede de** test edilmeli |
| İndeks ihtiyacı | Kullanıcı serbest filtre verebildiği için indekssiz kolonlarda tarama olur |
| N+1 | Mevcut servisler üzerinden gidilirse yok; yeni gevşek bir üretici yazılırsa risk doğar |
| Eşzamanlı yük / timeout | **BİLİNMİYOR — PRODUCTION ÖLÇÜMÜ GEREKTİRİR** |
| Canlı veri hacmi | **BİLİNMİYOR — PRODUCTION ÖLÇÜMÜ GEREKTİRİR** (bu turda ölçülmedi) |

---

## 9. TEST ANALİZİ

- **Custom Rapor testi YOK** (kod da yok).
- İlgili mevcut kilitler: `ReportFilterParityTests` (filtre 6 katman zinciri) · `ReportBranchScopeTests`
  (şube kapsamı) · `RaporYetkiTests`/kategori kilitleri (ADR-181) · `ReportCatalog` sayım kilitleri ·
  `Postgres*ReportTests` (iki lehçe paritesi).
- **Bu kilitler Custom Rapor'da da geçerli olmalı**; gevşetilmeyecek, gerekiyorsa **yeni test eklenecek**.
- Yeni gerekecek kilit aileleri (öneri): dinamik anahtar çözümleme · 4 kapının dinamik raporda da
  çalıştığı · kolon/filtre beyaz-listesi dışına çıkılamadığı · tenant izolasyonu · iki lehçe paritesi ·
  eski istemci (bilinmeyen tablo) davranışı · satır tavanı.

---

## 10. RİSK MATRİSİ

| # | Risk | Olasılık | Etki | Not |
|---|---|---|---|---|
| R1 | Ham SQL yüzeyi açılırsa injection + tenant/şube delinmesi | Orta | **Çok yüksek** | PK-CR-01 ile kapatılır |
| R2 | Dinamik rapor 4 kapıyı atlarsa yetki delinir | Orta | **Yüksek** | Tanım `DataModule` + `Category` + `IsManager` taşımalı |
| R3 | Masaüstü çevrimdışı çalışamaz | **Yüksek** (senkron yoksa) | Yüksek | PK-CR-02 |
| R4 | Büyük sonuçta bellek/performans | Orta | Orta | Tavan SQL'e indirilmeli |
| R5 | Migration083 canlı şemayı değiştirir | Kesin | Orta | Yalnız CREATE; FIN-B1 emsali |
| R6 | Eski istemci yeni tabloyu bilmiyor | Orta | Orta | Duyuru deseni; FAZ 3'te yeniden doğrulanacak |
| R7 | Web'e proje referansı eklenmesi cazibesi | Düşük | Yüksek (mimari bozulur) | `<Compile Include>` deseni korunacak |

---

## 11. KAPSAM DIŞI (bilinçli)

`sync_outbox` bulgusu · geçmiş veri düzeltmeleri · ARA İŞ 3 · FIN-B1 / Migration082 · N/Mobil ·
Ekip+Hiyerarşi+Onay. Hiçbiri bu işe **dahil edilmedi**.

**Ayrı iş olarak kaydedilmesi önerilir (bu turda dokunulmadı):** yok — Custom Rapor kapsamı dışında
yeni bir problem tespit edilmedi.

---

## 11.5 ⭐ FAZ 2 — KARARLAR ONAYLANDI (2026-08-29, ADR-186)

> Aşağıdaki §12'de yazan seçenekler artık **öneri DEĞİL**; kullanıcı **sekizinin tamamını (A)**
> onaylamıştır ve **BAĞLAYICIDIR**. FAZ 1 bulguları değiştirilmedi/silinmedi.
> ⛔ **FAZ 3 HENÜZ BAŞLAMADI** — kod/migration/test üretilmedi, production'a bağlanılmadı.

| Karar | Sonuç | Özet |
|---|---|---|
| **PK-CR-01** | **A** | Merkezî tanım modeli · **ham SQL YOK, serbest JOIN YOK** · kaynak+kolon beyaz-listeden · güvenli sorgu üretim katmanı · **dört güvenlik kapısı zorunlu** (yönetici · RPR-15 veri modülü · ADR-181 kategori · katalog çözümleme) |
| **PK-CR-02** | **A** | Yeni tanım tablosu + **`BusinessSyncService` ile senkron** → masaüstü **çevrimdışı** çalışır · senkron protokolü korunur · **eski istemci davranışı FAZ 3 başında GERÇEK TESTLE doğrulanacak** · migration gerekecek (şema 82 → sıradaki numara), **FAZ 3'ten önce oluşturulmayacak** |
| **PK-CR-03** | **A** | Mevcut motor **genişletilir**, ikinci motor kurulmaz · `ReportCatalog`/`Dispatch`/`Run`/`TableModel`/API uçları/masaüstü+web ekranları korunur · mevcut raporların çıktısı bozulmaz |
| **PK-CR-04** | **A** | Rapor başına **dinamik permission key** (`module_key` serbest metin → **migration yok**) · tanım **DataModule · Category · IsManager · key** taşır · `AppScreens` genişletmesi **kod tarafında** · paralel yetki sistemi icat edilmez |
| **PK-CR-05** | **A** | **Merkezî** kolon beyaz-listesi · kullanıcı tablo/kolon adı, SQL ifadesi, JOIN, ORDER BY veya aggregate parçası **veremez** · whitelist dışı alan çalışmaz · yeni kaynak/kolon kod tarafında tanımlanır |
| **PK-CR-06** | **A** | `maxRows` **SQL'e indirilecek** (yalnız bellek/UI limiti yetersiz sayılır) · **tarih filtresi zorunlu** · **PostgreSQL ve SQLite ayrı ayrı** ele alınacak |
| **PK-CR-07** | **A** | **Tek yayın**: yedek → migration → API → Web → masaüstü → doğrulama (FIN-B1 emsali) · migration ve kod aynı pakette · **pg_dump + salt-okunur boyut ölçümü** yayın ön koşulu · FAZ 2'de production erişimi YOK |
| **PK-CR-08** | **A** | FAZ 3 uygulama → FAZ 4 test → FAZ 5 yayın öncesi kontrol → FAZ 6 yayın → FAZ 7 yayın sonrası doğrulama |

### FAZ 3 başında yeniden doğrulanacak 14 teknik nokta (karar gereği)

1. Tanım tablosunun kesin şeması · 2. Senkron sırası · 3. **Eski istemcinin bilinmeyen tabloyu ele
alışı** · 4. FK bağımlılıkları · 5. PostgreSQL + SQLite uyumluluğu · 6. SQL üretim güvenliği ·
7. Whitelist modeli · 8. Zorunlu tarih filtresi · 9. SQL seviyesinde satır limiti · 10. Dispatch
entegrasyonu · 11. Yetki kapılarının Custom Rapor yolunda korunması · 12. Masaüstü çevrimdışı
davranışı · 13. API sözleşmesinin korunması · 14. `TableModel`'in yeniden kullanımı.

---

## 12. PK-CR KARAR PAKETİ — ✅ TAMAMI ONAYLANDI (ADR-186)

> Aşağıdaki A/B/C seçenekleri **kayıt amaçlıdır**; **hepsinde karar = A** (bkz. §11.5).
> Yeniden sorulmayacaktır.

### PK-CR-01 — v1 kapsamı ve sorgu güvenliği
- **A (ÖNERİLEN):** Tek kaynak (mevcut rapor/servis beyaz-listesinden) → kolon seç/sırala →
  mevcut `ReportFilters` yapı taşlarıyla filtre → çalıştır/kaydet. **Çapraz-kaynak join YOK. Ham SQL YOK.**
- **B:** Çok kaynak + join (tam vizyon).
- **C:** Yalnız "mevcut raporu kişiselleştir" (kolon gizle/sırala) — tanım tablosu yine gerekir.
- **Gerekçe (A):** ham SQL ve join, tenant/BranchAccess/soft-delete süzgeçlerini servis dışına taşır (R1).
  A'da izolasyon **yapısal olarak** korunur. · Veri etkisi: yok · Migration: 1 tablo · Senkron: PK-CR-02 ·
  Eski istemci: etkilenmez · Rollback: tablo kalır, özellik kapatılır · Kullanıcı etkisi: v1'de join beklentisi karşılanmaz

### PK-CR-02 — Tanımların saklanması ve masaüstü çevrimdışı davranışı ⭐
- **A (ÖNERİLEN):** Yeni `custom_report_defs` tablosu + **senkron listesine ekleme** (duyuru deseni)
  → masaüstü çevrimdışı da tanımı görür ve çalıştırır.
- **B:** Sunucu-otoriteli / yalnız çevrimiçi → **masaüstü çevrimdışıyken custom rapor YOK**.
- **C:** Makine-yerel (paylaşılamaz).
- **Gerekçe (A):** masaüstü rapor motoru **yerel** çalışıyor ([ReportsViewModel.cs:594](src/DepoWise.Desktop/ViewModels/ReportsViewModel.cs:594));
  B seçilirse masaüstü öncelik ilkesi zedelenir (R3). · Migration: A'da **iki lehçede** tablo ·
  Senkron: A'da tablo listesine ekleme (protokol değişmez) · Eski istemci: bilinmeyen tabloyu yok sayar
  (FAZ 3'te doğrulanacak) · Rollback: senkron listesinden çıkarma + özelliği kapatma

### PK-CR-03 — Dinamik raporun katalog/dağıtıma bağlanma biçimi
- **A (ÖNERİLEN):** `ReportCatalog.ByKey` ve `Dispatch` **çözümleyici ile genişletilir**: sabit
  anahtar bulunamazsa custom tanım aranır; tanım `ReportDescriptor`'a dönüştürülür ve **aynı 4 kapıdan** geçer.
  Mevcut `/api/reports/*` uçları ve `TableModel` **değişmez** → API sözleşmesi korunur.
- **B:** Ayrı `/api/custom-reports/*` uç ailesi (sözleşme büyür, kapılar ikinci kez yazılır).
- **C:** Yalnız masaüstünde (web'de yok) — parite bozulur.
- **Gerekçe (A):** `TableModel` altı zaten jenerik; tek dağıtım noktası korunursa güvenlik kapıları
  **tek yerde** kalır. · Migration: yok · Senkron: yok · Eski istemci: eski sürüm custom anahtarı
  bilmez, katalogda görmez → **bozulmaz**

### PK-CR-04 — Custom rapor yetkisi
- **A (ÖNERİLEN):** Rapor başına **dinamik yetki anahtarı** (`module_key` serbest metin olduğu için
  migration'sız) + sahibine otomatik yetki + `reports` üst kapısı + tanımın `DataModule`/`Category`
  kapıları aynen.
- **B:** Tek genel "custom_reports" anahtarı (kim görürse hepsini görür).
- **C:** Yalnız yönetici.
- **Gerekçe (A):** `user_permissions.module_key` serbest metin ([Migration001:83](src/DepoWise.Infrastructure/Database/Migrations/Migration001_CoreSchema.cs:83));
  deny-by-default korunur. ⚠️ **Ek iş:** yetki ağacı statik olduğu için ([AppScreens.cs:140](src/DepoWise.Application/Security/AppScreens.cs:140))
  dinamik anahtarların ağaçta görünmesi için ekleme noktası gerekir. · Migration: **yok**

### PK-CR-05 — Kaynak ve kolon beyaz-listesi
- **A (ÖNERİLEN):** Kaynaklar = mevcut rapor/servis yöntemleri; kolonlar = `ListColumns` (36 kolon,
  Türkçe etiketli) + rapor çıktısı başlıkları.
- **B:** Tablo/kolon adları doğrudan şemadan.
- **Gerekçe (A):** B, şema iç adlarını kullanıcıya açar ve yeniden adlandırmada kırılır; ayrıca
  yetkisiz kolon sızması riski doğar. · Migration: yok

### PK-CR-06 — Satır tavanı ve performans koruması
- **A (ÖNERİLEN):** Custom raporlarda tavan **SQL'e indirilir** (emsal: `stock-movements` `LIMIT @lim`)
  + `RequiresDate` **zorunlu** (tarih aralığı olmadan çalıştırma yok).
- **B:** Mevcut bellekte kesme yeterli sayılır.
- **Gerekçe (A):** bugünkü kesme bellekte yapılıyor ([:2009](src/DepoWise.Infrastructure/Reporting/ReportService.cs:2009));
  serbest tanımlı raporda bu yetersiz kalabilir (R4). · Performans etkisi: pozitif · Migration: yok

### PK-CR-07 — Yayın stratejisi
- **A (ÖNERİLEN):** Tek yayın: Migration083 + kod + masaüstü yeni sürüm (FIN-B1 emsali: yedek →
  boyut ölçümü → deploy → doğrulama).
- **B:** Önce migration, sonra kod (iki pencere).
- **Gerekçe (A):** FIN-B1'de kanıtlandı ve sorunsuz işledi. · **Production ölçümü yayın öncesi adımdır**, bu turda yapılmadı

### PK-CR-08 — Fazlama
- **A (ÖNERİLEN):** Tek FAZ 3'te uygula (v1 dar kapsam).
- **B:** İki alt faza böl (CR-a: tanım+saklama+senkron · CR-b: tasarımcı UI + katalog entegrasyonu).
- **Gerekçe:** kapsam PK-CR-01=A ile darsa A yeterli; B, riski böler ama iki yayın penceresi ister.

---

## 12.5 ⭐ FAZ 3 / S1 — 14 TEKNİK NOKTANIN YENİDEN DOĞRULANMASI (2026-08-29)

> Yalnız **doğrulama** yapıldı; **ürün kodu değiştirilmedi**, migration oluşturulmadı,
> production'a bağlanılmadı. Tek eklenen dosya bir **doğrulama testidir**.

| # | Nokta | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Tanım tablosunun şeması | **BELİRLENDİ** | Alan listesi §12.6'da; mevcut migration desenine uygun |
| 2 | Senkron sırası | **GEÇTİ** | `BusinessSyncService.Tables` FK sıralı 63 kayıt + `TableModule` yetki haritası ([:32, :152](src/DepoWise.Infrastructure/Sync/BusinessSyncService.cs:32)); duyuru emsali |
| 3 | **Eski istemci — bilinmeyen tablo** | ✅ **GERÇEK TESTLE KANITLANDI (5/5)** | `CustomRaporSenkronOnDogrulamaTests` — aşağıda |
| 4 | FK bağımlılıkları | **GEÇTİ** | Duyuru deseni: FK yalnız `companies`/`branches` → sıra bağımlılığı yok |
| 5 | PostgreSQL + SQLite | **GEÇTİ** | `SqlDialect` 11 yardımcı (`PortableSql`, `LikeTr`, `NowMs`, `NumericValue`…) ([SqlDialect.cs:17-147](src/DepoWise.Infrastructure/Database/SqlDialect.cs:17)) |
| 6 | SQL üretim güvenliği | **GEÇTİ (şartlı)** | `GridQuery.Build` → değer **parametreli** (`@gf0n`), kolon ifadesi **koddan** ([GridQuery.cs:57-95](src/DepoWise.Infrastructure/Database/GridQuery.cs:57)). ⚠️ `rawAlias` SQL'e **düz metin** giriyor → alias **yalnız** beyaz-listeden gelmeli |
| 7 | Whitelist modeli | ⚠️ **DEĞİŞTİ** | `ListColumns` yalnız **3 katalog / 36 kolon** (Malzeme · Araç · Günlük Faaliyet) — tüm rapor kaynaklarını kapsamıyor |
| 8 | Zorunlu tarih filtresi | **GEÇTİ** | `ReportFilters.Date = 1` bit bayrağı + `RequiresDate` sunucu-taraflı zorlama ([ReportService.cs:1997](src/DepoWise.Infrastructure/Reporting/ReportService.cs:1997)); ARA İŞ 3'ün `IsGunuTarihi` altyapısı **dokunulmadan** kullanılacak |
| 9 | SQL seviyesinde satır limiti | **GEÇTİ** | Emsal mevcut: "SQL'e inen satır tavanı" ([ReportService.cs:1223](src/DepoWise.Infrastructure/Reporting/ReportService.cs:1223)); genel yol hâlâ bellekte kesiyor ([:2009](src/DepoWise.Infrastructure/Reporting/ReportService.cs:2009)) → custom raporda SQL'e inecek |
| 10 | Dispatch entegrasyonu | **GEÇTİ** | `ByKey` ([:360](src/DepoWise.Application/Reports/ReportCatalog.cs:360)) + `Run` ([:1946](src/DepoWise.Infrastructure/Reporting/ReportService.cs:1946)) + kapalı `Dispatch` switch ([:2017](src/DepoWise.Infrastructure/Reporting/ReportService.cs:2017)) — çözümleyici ile genişletilebilir |
| 11 | Yetki kapıları | **GEÇTİ** | Dört kapı yerinde: yönetici ([:1960](src/DepoWise.Infrastructure/Reporting/ReportService.cs:1960)) · `DataModule` ([:1979](src/DepoWise.Infrastructure/Reporting/ReportService.cs:1979)) · kategori ([:1994](src/DepoWise.Infrastructure/Reporting/ReportService.cs:1994)) · katalog ([:1946](src/DepoWise.Infrastructure/Reporting/ReportService.cs:1946)). `CategoryModule` kapalı switch, tanımsızda **istisna** |
| 12 | Masaüstü çevrimdışı | **GEÇTİ** | `DesktopServices.Reports.Run(...)` yerel yürütme ([ReportsViewModel.cs:594](src/DepoWise.Desktop/ViewModels/ReportsViewModel.cs:594)) |
| 13 | API sözleşmesi | **GEÇTİ** | `/api/reports/catalog`, `/{type}`, `/{type}/export` ([Program.cs:2977](src/DepoWise.Api/Program.cs:2977)) — genişletme yeterli, kırılma gerekmiyor |
| 14 | `TableModel` yeniden kullanımı | **GEÇTİ** | `TableModel(Title, Headers, Rows, Numeric, TotalRow)` tamamen jenerik ([ReportModels.cs:15](src/DepoWise.Application/Reports/ReportModels.cs:15)) |

### Nokta 3 — GERÇEK TEST SONUCU (varsayımla kapatılmadı)

`tests/DepoWise.Tests/CustomRaporSenkronOnDogrulamaTests.cs` — **5/5 GEÇTİ**:

| Test | Kanıtlanan |
|---|---|
| **ESK-01** | Alıcının tanımadığı tablo içeren **pull** paketi: istisna YOK · bilinen tablolar normal uygulandı · bilinmeyen tablo için hata üretilmedi · yerelde tablo oluşmadı |
| **ESK-02** | Aynısı **push** yönünde: sunucu bozulmadı |
| **ESK-03** | **Gerçek eski şema provası:** şema 80'de kurulan DB'de `announcements` yok ama güncel `Tables` listesinde var → `TableExists` kapısı atladı, diğer tablolar uygulandı |
| **ESK-04** | Bilinmeyen tablo, 10 geçerli satırın transaction'ını **rollback ettirmedi** |
| **ESK-05** | **Senkron şema YARATMAZ** — tablo yalnız migration ile gelir (tasarım sınırı kilitlendi) |

**Mekanizma (kodda doğrulandı):** `ApplyCore` döngüsü **alıcının kendi** `Tables` dizisini gezer, paketin
tablolarını değil ([:915](src/DepoWise.Infrastructure/Sync/BusinessSyncService.cs:915)); ayrıca
`TableExists` ikinci kapıdır ([:919](src/DepoWise.Infrastructure/Sync/BusinessSyncService.cs:919)).
→ **PK-CR-02=A güvenle uygulanabilir.**

Regresyon: `Sync|Report|CustomRapor` aileleri **514 geçti / 0 başarısız / 3 atlandı** (atlananlar ayrı
ortam isteyen PG sınıflarıdır).

## 12.6 ⛔ YENİ KARAR NOKTASI — PK-CR-09 (FAZ 3 DURDURULDU)

**Bulgu (nokta 7'nin sonucu):** Güvenli, hazır ve beyaz-listeli veri kaynağı sayısı **3'tür**:
`MaterialService.SearchGrid` · `VehicleService.SearchGrid` · `DailyActivityService.SearchGrid`
([MaterialService.cs:685](src/DepoWise.Infrastructure/Materials/MaterialService.cs:685) ·
[VehicleService.cs:307](src/DepoWise.Infrastructure/Vehicles/VehicleService.cs:307) ·
[DailyActivityService.cs:349](src/DepoWise.Infrastructure/Operations/DailyActivityService.cs:349)) —
ve bunlar `ListColumns`'taki 3 katalogla **birebir** eşleşir.

Mevcut **25 rapor metodu** ise sabit kolonlu `TableModel` üretir; **rastgele kolon alt kümesi
projeksiyonu yapamaz** → doğrudan "custom rapor kaynağı" olamazlar.

**Neden karar gerekiyor:** ADR-186 kaynak *kümesini* belirlemedi ("yeni veri kaynağı kod tarafında
tanımlanır" dedi). İki okuma **maddi olarak farklı iş** üretir:

| | Seçenek | Sonuç |
|---|---|---|
| **A** | v1 = **mevcut 3 SearchGrid kaynağı** (Malzeme · Araç · Günlük Faaliyet) | En küçük güvenli kapsam; yeni sorgu yüzeyi yok; hızlı ve düşük riskli. Kullanıcı yakıt/bakım/stok hareketi/fatura üzerinden custom rapor **YAPAMAZ** |
| **B** | v1 = 3 kaynak + **seçilecek N kaynak için yeni beyaz-liste + grid katmanı** | Daha geniş fayda; her yeni kaynak için kolon kataloğu + güvenli sorgu üreticisi + testler ⇒ iş hacmi kaynak başına büyür |
| **C** | v1 = yalnız **mevcut raporu kişiselleştir** (kolon gizle/sırala/yeniden adlandır) | En küçük iş; ama "kendi raporunu tasarla" beklentisini karşılamaz |

**Öneri: A** (v1'i mevcut güvenli yüzeyle sınırla, sonraki sürümde kaynak ekle) — ancak bu **sizin
kararınızdır**; kapsamı kendiliğinden genişletmedim ve **kod yazmadan durdum**.

## 12.7 PK-CR-09 = A — KARAR KAYDEDİLDİ (2026-08-29)

**Karar:** Custom Rapor **v1 yalnızca 3 doğrulanmış kaynağı** destekler:
`MaterialService.SearchGrid` · `VehicleService.SearchGrid` · `DailyActivityService.SearchGrid`.
**B ve C seçilmedi.** Yakıt · Bakım · Stok Hareketleri · Faturalar **v1 kapsamı DIŞINDADIR**;
mevcut 25 rapor metoduna dinamik kolon projeksiyonu eklenmeyecek; kaynak sayısı kendiliğinden
artırılmayacak.

## 12.8 ⛔ S2 DURDURULDU — YENİ ÇELİŞKİ: PK-CR-10 (zorunlu tarih ↔ v1 kaynakları)

S2 uygulamasına başlamadan önceki son doğrulamada, **iki bağlayıcı karar arasında gerçek bir
çelişki** bulundu. Kullanıcı talimatı gereği (*"PK-CR kararlarıyla çelişen bir durum varsa
uygulamadan önce DUR"*) **kod yazılmadı**.

### Çelişki

**PK-CR-06 = A:** *"Custom raporlarda tarih filtresi ZORUNLU · bellekte değil, SQL seviyesinde
WHERE'e indirilecek · tarih aralığı olmadan rapor çalıştırılmayacak."*

**PK-CR-09 = A:** v1 kaynakları = Malzeme · Araç · Günlük Faaliyet.

**Kanıt — üç kaynağın tarih gerçeği:**

| Kaynak | Tabloda iş günü tarihi | Grid filtresinde tarih alanı | `SearchGrid` tarih parametresi |
|---|---|---|---|
| Malzeme | ❌ **YOK** — yalnız `created_at`/`updated_at` ([Migration005_Materials.cs:77](src/DepoWise.Infrastructure/Database/Migrations/Migration005_Materials.cs:77)) | ❌ yok (`MaterialGridFilter` 15 alan, tarih yok) | ❌ yok ([MaterialService.cs:685](src/DepoWise.Infrastructure/Materials/MaterialService.cs:685)) |
| Araç | ❌ **YOK** — yalnız `created_at`/`updated_at` ([Migration007_Vehicles.cs:85](src/DepoWise.Infrastructure/Database/Migrations/Migration007_Vehicles.cs:85)) | ❌ yok (`VehicleGridFilter` 14 alan) | ❌ yok ([VehicleService.cs:307](src/DepoWise.Infrastructure/Vehicles/VehicleService.cs:307)) |
| Günlük Faaliyet | ✅ **VAR** — `activity_date` ([Migration009:74](src/DepoWise.Infrastructure/Database/Migrations/Migration009_FuelDailyActivity.cs:74)) | ❌ yok (`DailyActivityGridFilter` 6 alan) | ❌ yok ([DailyActivityService.cs:349](src/DepoWise.Infrastructure/Operations/DailyActivityService.cs:349)) |

**Neden basit bir ekleme değil:**
1. **Malzeme ve Araç ANA VERİDİR** (katalog), olay verisi değil. Zorunlu tarih aralığı ancak
   `created_at` üzerinden kurulabilir — bu **kayıt anıdır**, iş günü değil. ARA İŞ 3 / ADR-184 bu iki
   semantiği **bilinçli olarak ayırmıştır** ve o altyapıya **dokunmam yasaktır**.
2. Üç `SearchGrid` metodunun **hiçbiri** tarih aralığı parametresi almıyor → tarihi SQL'e indirmek,
   **yayınlanmış ve testli servis kodunu** (ve API uçlarını) değiştirmeyi gerektirir; bu "rapor
   motorunu genişlet" kapsamının dışına çıkar.

### PK-CR-10 — Zorunlu tarih filtresi ile v1 kaynak kümesinin uyumu

| | Seçenek | Sonuç |
|---|---|---|
| **A (ÖNERİLEN)** | **Tarih zorunluluğu kaynak-bazlı olsun:** tarih filtresi yalnız **olay** verisi taşıyan kaynakta (Günlük Faaliyet) zorunlu; **ana veri** kaynaklarında (Malzeme, Araç) tarih filtresi **yok**, yerine **zorunlu SQL satır tavanı** + **en az bir filtre** şartı | PK-CR-06'nın asıl amacı (sınırsız/aşırı geniş sorguyu engellemek) korunur · semantik bozulmaz · yayınlanmış `SearchGrid` kodu **değişmez** · ADR-184 ayrımına dokunulmaz |
| **B** | **v1'i tek kaynağa indir:** yalnız Günlük Faaliyet | PK-CR-06 aynen korunur ama v1 çok dar; PK-CR-09=A'nın 3 kaynağı fiilen 1'e düşer |
| **C** | Üç `SearchGrid`'e tarih aralığı ekle; Malzeme/Araç'ta `created_at` kullan | Yayınlanmış servis kodu + API değişir; **kayıt anını iş günü gibi** kullandırır (ADR-184 ile çelişir) → **ÖNERİLMEZ** |

**Not:** Satır tavanı (PK-CR-06'nın diğer yarısı) her üç kaynakta da **sorunsuz uygulanabilir** —
`SearchGrid` zaten sayfalıdır (`page`, `pageSize`) ve tavan SQL'e iner.

## 12.9 FAZ 3 / S2 — UYGULAMA KAYDI (2026-08-29)

**PK-CR-10 = A onaylandı** (tarih zorunluluğu kaynak bazlı) → S2 uygulandı.

### Eklenen/değişen kod

| Katman | Dosya | İçerik |
|---|---|---|
| Application | `Reports/CustomReportSources.cs` **(YENİ)** | Merkezî kaynak+kolon beyaz listesi; **tam 3 kaynak** (PK-CR-09=A); her kaynak `DataModule`·`Category`·`IsManager`·`RequiresDate`·`RequiresFilter` taşır |
| Application | `Reports/CustomReportDefinition.cs` **(YENİ)** | Tanım modeli (ham SQL YOK) · `ReportKey` (`custom:` öneki) · `PermissionKey` (`report_custom_…`) · `CustomReportRules` doğrulayıcı (istisna atmaz, sonuç döner) |
| Infrastructure | `Database/Migrations/Migration083_CustomReports.cs` **(YENİ)** | `custom_report_defs` tablosu — tek CREATE, FK yalnız `companies`, backfill YOK |
| Infrastructure | `Database/Migrations/MigrationCatalog.cs` | 083 kaydı → katalog azamisi **83** |
| Infrastructure | `Reporting/CustomReportService.cs` **(YENİ)** | CRUD + çalıştırma; mevcut `SearchGrid`'lere bağlanır, `TableModel`'e projeksiyon yapar; sayfalı çekim (SQL LIMIT) |
| Infrastructure | `Reporting/ReportService.cs` | `Custom` bağlayıcısı + `Run` içinde custom çözümleme; **dört kapı + iki ek kapı** aynı yerde |
| Infrastructure | `Operations/DailyActivityService.cs` | `SearchGrid`'e **opsiyonel** `fromDateMs`/`toDateMs` → iş günü aralığı **SQL'e iner** (mevcut çağrılar ve API sözleşmesi birebir aynı) |
| Infrastructure | `Sync/BusinessSyncService.cs` | `custom_report_defs` tablo listesine (sona, FK bağımlılığı yok) + push yetki haritasına (`reports`) eklendi |
| API | `ServerServices.cs` | `CustomReports` bağlandı |
| Desktop | `DesktopServices.cs` | `CustomReports` bağlandı (çevrimdışı çalışır) |

### Güvenlik kapıları (custom rapor yolunda)

1. Katalog çözümleme · 2. Yönetici kapısı · 3. `DataModule` (RPR-15) · 4. Kategori (ADR-181) —
**dördü de aynen çalışır**; ayrıca custom yola özel iki kapı eklendi:
5. **`reports` üst kapısı** · 6. **rapor başına dinamik yetki anahtarı** (PK-CR-04=A, migration YOK).

> ⚠️ **Testle bulunan gerçek açık (düzeltildi):** custom rapor gövdesi alttaki `SearchGrid`
> servislerine gittiği için onlar yalnız kendi modüllerini (`materials`/`vehicles`/`daily_activity`)
> istiyordu → **`reports` üst kapısı boşta kalıyordu**. CR19 testi yakaladı; `Run` içinde açıkça
> istenerek kapatıldı.

### Tarih / filtre / limit (PK-CR-06 + PK-CR-10 = A)

- **Günlük Faaliyet:** iş günü aralığı **zorunlu** ve `da.activity_date` üzerinden **SQL'e iner**.
  Zorunluluk, motorun "Bu Ay varsayılanı" bloğundan **ÖNCE** doğrulanır — aksi hâlde varsayılan
  devreye girer ve kural fiilen uygulanmamış olurdu.
- **Malzeme / Araç:** tarih filtresi **YOK**; `created_at`/`updated_at` iş günü olarak **kullanılmaz**
  (CR17 kilidi bunu ayrıca kanıtlar). Yerine **en az bir beyaz-listeli filtre zorunludur**.
- **Satır tavanı:** her sorgu `SearchGrid`'in `LIMIT/OFFSET`'i ile sınırlı; toplam
  `CustomReportRules.MaxRows` (5.000) ile kesilir. "Önce hepsini çek sonra kes" YAPILMAZ.

### Kapsam dışı bırakılan (bilinçli)

Tasarımcı **UI** (masaüstü + web) ve **API uçları** bu turda yapılmadı — kullanıcı talimatı:
*"S2'de veri/motor/senkron/altyapı kurulumu öncelikli; UI kapsamını kendiliğinden büyütme."*
Web'e `<Compile Include>` eklenmedi (kullanılmayan kod olurdu); mimari sınır korundu, **`ProjectReference`
EKLENMEDİ**. `sync_outbox` · ARA İŞ 3 · FIN-B1 · geçmiş veri · yeni kaynaklar: **dokunulmadı**.

### S2 doğrulama sonuçları (2026-08-30)

| Doğrulama | Geçen | Başarısız | Atlanan |
|---|---|---|---|
| **Tam test süiti** | **3.079** | **0** | 40 |
| **İzole PostgreSQL** (127.0.0.1:5544, temiz DB) | **53** | **0** | **0** |
| Custom rapor aile testleri | **43** | 0 | 0 |
| API / Web / Masaüstü Release | 0 hata | | |

Önceki taban 3.036 → **3.079** (+43 yeni test). PG turunda atlanan **0** → guard gevşetilmedi,
**K4 (`MaxDbSizeMb=50`) kuralına dokunulmadı**; tur öncesi test veritabanı temizlendi.

**PostgreSQL'de Migration083 doğrulandı:** `schema_migrations` azamisi **83**; `custom_report_defs`
14 kolonla kuruldu (`information_schema` çıktısı). SQLite tarafı CR01/CR02/CR03 ile ayrıca kilitli.

> ⚠️ **Şeffaflık — ilk tam süitte 2 başarısızlık çıktı, gizlenmedi:**
> `FIN20_Katalog_Azamisi_82` ve `FIN21_Yukseltme_81den82ye…` (ikisi de FIN-B1 turunda benim yazdığım
> kilitler). **Kök neden:** bu testler kataloğun azami sürümünü **sabit "82" sayısına** bağlamıştı;
> Migration083 eklenince eskidiler — **ürün hatası değil, test sözleşmesinin eskimesi**.
> **Düzeltme (gevşetme DEĞİL):** FIN20 artık BAR15 ile aynı biçimde **kataloğun kendisine** bağlı ve
> ayrıca "082 uygulanmış mı" kontrolünü içeriyor (asıl güvence güçlendi); FIN21 ise adı gereği yalnız
> 81→82 adımını ölçmesi için koşumu `Version <= 82` ile sınırlandı. İkinci turda **0 başarısızlık**.

## 12.10 FAZ 4 — API + UI + KATALOG ENTEGRASYONU (2026-08-30)

### API (yeni uçlar — mevcut sözleşme KIRILMADI)

| Uç | İş |
|---|---|
| `GET /api/custom-reports/sources` | **Tasarımcı kataloğu**: 3 kaynak + beyaz-listeli kolonlar (anahtar · görünen ad · sayısal mı · tarih/filtre kuralı). **SQL ifadesi/tablo adı/alias İÇERMEZ.** |
| `GET /api/custom-reports` · `GET /{id}` | Tanım listesi / tekil |
| `POST` · `PUT /{id}` · `DELETE /{id}` | Oluştur · güncelle · **yumuşak sil** |

⚠️ **Çalıştırma için AYRI uç açılmadı**: custom raporlar mevcut `POST /api/reports/{type}` ucundan,
anahtar `custom-<id>` biçiminde geçerek çalışır (PK-CR-03=A). `CustomReportDto`'da **`CompanyId` alanı
YOKTUR** — firma daima oturumdan gelir.

### Rapor anahtarı biçimi düzeltildi (uygulama riski, yayın öncesi)

`custom:<id>` → **`custom-<id>`**. Gerekçe: anahtar `/api/reports/{type}` yolunda **URL segmenti**
olarak taşınıyor; iki nokta kodlama/yönlendirme sorunu çıkarabilirdi. Mevcut sabit anahtarlar da tire
kullanır (`vehicle-daily`). S2 yayınlanmadığı için değişiklik **bedelsiz**; KAT10 testi anahtarın
URL'de değişmeden taşındığını ve sabit anahtarlarla çakışmadığını kilitler.

### Katalog entegrasyonu (tek katalog, tek motor)

`CustomReportService.Catalog(session)` custom raporları `ReportDescriptor`'a çevirir; görünürlük
süzmeleri sabit raporlarla **birebir aynı**: `reports` üst yetkisi · kapatılmış modül (RPR-15) ·
kategori (ADR-181) · rapora özel dinamik anahtar. **API** ve **masaüstü `ReportsViewModel`** aynı
listeye ekler → kullanıcı tek yerde görür. Web listeyi API'den alır.

> ⚠️ **Parite kilidinin yakaladığı gerçek zayıflama (düzeltildi):** API kataloğunda projeksiyonu
> kopyalayınca `ReportFilterParityTests` "eksik API katalog alanı" kilidi **etkisizleşti** (alan iki
> yerde geçtiği için eksiklik yakalanamıyordu). Testi değiştirmek yerine **kopya kaldırıldı**:
> sabit + custom raporlar artık tek `ReportCatalogItem` projeksiyonundan geçiyor → kilit yeniden güçlü.

### Masaüstü UI

`CustomReportsViewModel` + `CustomReportsView.axaml` — kaynak seçimi · kolon işaretleme · kolon başına
filtre kutusu · kaydet/sil/yenile · kayıtlı raporlar listesi · kaynak kuralı açıklaması.
**Serbest metin yalnız rapor adı ve arama değeri içindir**; tablo/kolon adı, SQL, JOIN, ORDER BY
girilebilecek alan YOKTUR. Tanımlar yerel SQLite'tan okunur → **çevrimdışı çalışır**.

### Web UI

`Components/Pages/CustomReports.razor` (`/reports/designer`) — aynı sözleşme, MudBlazor desenleriyle.
Kaynak/kolon listesi **sunucudaki tek beyaz listeden** gelir. **`ProjectReference` EKLENMEDİ**; sayfa
yalnız API JSON'ı ile çalışır (mimari sınır korundu).

### Yetki ağacı

Yeni ekran `reports.designer` **mevcut `reports` modülüne** bağlandı → **yeni yetki modülü açılmadı,
migration gerekmedi**. Menü şeması ve ekran sayısı kilitleri (`AppScreensParityTests` S13/S14)
bilinçli olarak güncellendi (masaüstü 56→57, web 63→64) — bu, yeni ekranın **kayda geçirilmesidir**.

### FAZ 4 doğrulama sonuçları (2026-08-30)

| Doğrulama | Geçen | Başarısız | Atlanan |
|---|---|---|---|
| **Tam test süiti** | **3.091** | **0** | 40 |
| **İzole PostgreSQL** | **53** | **0** | **0** |
| Custom rapor aileleri | **55** | 0 | 0 |
| API / Web / Masaüstü Release | 0 hata | | |

Taban 3.079 → **3.091** (+12 yeni katalog/tasarımcı testi). PostgreSQL'de şema **83**,
`custom_report_defs` **14 kolon** doğrulandı. Guard **K4 (50 MB) gevşetilmedi**.

> ⚠️ **Ortam notu (ürün hatası DEĞİL):** PG turundan önce scratch PostgreSQL'in ikilileri ve
> veri dizinindeki boş klasörler geçici dosya temizliğinde silinmişti (`pgsql/` yok, `data/pg_notify`
> yok). Bozuk kümeyi onarmak yerine ikililer zip'ten yeniden çıkarıldı ve **temiz bir küme
> `initdb` ile kuruldu**; testler o kümede koştu. Ürün kodu veya guard **değiştirilmedi**.

> ⚠️ **Parite kilitlerinin yakaladığı iki gerçek bulgu (düzeltildi, gizlenmedi):**
> (1) API kataloğundaki projeksiyon kopyası `ReportFilterParityTests`'in "eksik alan" kilidini
> etkisizleştiriyordu → **kopya kaldırıldı**, tek `ReportCatalogItem` projeksiyonu kullanıldı.
> (2) `AppScreensParityTests` S13/S14 yeni ekranın menü şemasına ve ekran sayısına **bilinçli
> kaydını** istedi → masaüstü 56→57, web 63→64 olarak güncellendi (gevşetme değil, kayıt).

## 13. FAZ TAKİP TABLOSU

| Faz | Durum |
|---|---|
| FAZ 0 — durum doğrulama | ✅ TAMAM |
| FAZ 1 — analiz | ✅ TAMAM (bu belge) |
| FAZ 2 — karar paketi | ✅ **TAMAM — PK-CR-01…08 = A (ADR-186)** |
| FAZ 3 — uygulama | ✅ **S1 + S2 TAMAM** (§12.9): Migration083 · beyaz liste · CustomReportService · dispatch + 6 kapı · senkron · 43 test |
| FAZ 4 — API + UI | ✅ **TAMAM** (§12.10): tasarımcı katalog ucu + CRUD uçları · katalog entegrasyonu (tek katalog) · masaüstü tasarımcı ekranı · web tasarımcı sayfası · yetki ağacı kaydı · +12 test |
| FAZ 4 — test/doğrulama | ⛔ |
| FAZ 5 — yayın öncesi kontrol | ⛔ |
| FAZ 6 — production yayın | ⛔ "YAYINLA" olmadan yapılmaz |
| FAZ 7 — yayın sonrası doğrulama | ⛔ |

## 14. PRODUCTION DOĞRULAMASI GEREKTİREN NOKTALAR

1. Rapor kaynaklarının canlı veri hacmi (satır sayıları) — tavan ve indeks kararı için
2. Eşzamanlı kullanıcı yükü / timeout davranışı
3. Migration083 öncesi tablo boyutları (yayın öncesi adım)

**Bu turda hiçbiri ölçülmedi; tahmin yazılmadı.**

## 15. CHATGPT DEVAM NOKTASI

**ARA İŞ 4 — CUSTOM RAPOR · FAZ 0 ✅ · FAZ 1 ✅ · FAZ 2 ✅ KARARLAR ONAYLANDI (ADR-186) ·
FAZ 3 ⏸️ "UYGULAMA BAŞLASIN" bekliyor.**
Kararlar: **PK-CR-01…08 = A**. Kod/migration/test **yok**; production'a **bağlanılmadı (SELECT dahil)**.
Migration katalog azamisi **82** (canlı şema 82) — yeni migration FAZ 3'te açılacak.
ARA İŞ 3 ve FIN-B1 **kapalı + yayınlanmış** olarak korunuyor; ana roadmap sırası **değişmedi**.
FAZ 3'ün ilk işi: §11.5'teki **14 teknik noktanın yeniden doğrulanması** (özellikle eski istemcinin
bilinmeyen tabloyu ele alışı — gerçek testle).
