# ARA İŞ 4 — CUSTOM RAPOR — FAZ 0 + FAZ 1 ANALİZ

> Tarih: **2026-08-29** · Aşama: **AŞAMA 3 — FINAL KARAR PAKETİ** · Durum: **FAZ 0 ✅ · FAZ 1 ✅ · FAZ 2 ⏸️ KARAR BEKLİYOR**
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

## 12. PK-CR KARAR PAKETİ (kararları kullanıcı verir)

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

## 13. FAZ TAKİP TABLOSU

| Faz | Durum |
|---|---|
| FAZ 0 — durum doğrulama | ✅ TAMAM |
| FAZ 1 — analiz | ✅ TAMAM (bu belge) |
| FAZ 2 — karar paketi | ⏸️ **PK-CR-01…08 KARAR BEKLİYOR** |
| FAZ 3 — uygulama | ⛔ "UYGULAMA BAŞLASIN" olmadan başlamaz |
| YAYIN | ⛔ "YAYINLA" olmadan yapılmaz |

## 14. PRODUCTION DOĞRULAMASI GEREKTİREN NOKTALAR

1. Rapor kaynaklarının canlı veri hacmi (satır sayıları) — tavan ve indeks kararı için
2. Eşzamanlı kullanıcı yükü / timeout davranışı
3. Migration083 öncesi tablo boyutları (yayın öncesi adım)

**Bu turda hiçbiri ölçülmedi; tahmin yazılmadı.**

## 15. CHATGPT DEVAM NOKTASI

**ARA İŞ 4 — CUSTOM RAPOR · FAZ 0 ✅ · FAZ 1 ✅ · FAZ 2 ⏸️ PK-CR-01…08 karar bekliyor.**
Kod/migration/test **yok**; production'a **bağlanılmadı**. Migration katalog azamisi **82** (canlı şema 82).
ARA İŞ 3 ve FIN-B1 **kapalı + yayınlanmış** olarak korunuyor; ana roadmap sırası **değişmedi**.
Kararlar verilince: ADR + `CURRENT_PHASE`/`MASTER_ROADMAP` güncellenir, sonra "UYGULAMA BAŞLASIN" beklenir.
