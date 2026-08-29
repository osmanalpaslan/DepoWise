# ARA İŞ 3 — TARİH DÖNÜŞÜM HATALARI (TARİH KAYMASI) · 00 ANALİZ

> **DURUM: FAZ 0 ✅ · FAZ 1 ✅ · FAZ 2 ✅ KARARLAR ONAYLANDI (2026-08-29, ADR-184) ·
> FAZ 3 ⏸️ "UYGULAMA BAŞLASIN" ONAYI BEKLİYOR**
> **KOD YAZILMADI · TEST KOŞULMADI · MIGRATION OLUŞTURULMADI · PRODUCTION'A BAĞLANILMADI (SELECT dahil).**
> Onaylanan kararlar: **PK-TAR-01=A · 02=A · 03=A · 04=A · 05=A · 06=B · 07=A** (ayrıntı §9).
> Tarih: 2026-08-29 · Kaynak talep: kullanıcının "TARİH / TARİH-SÜRE DÖNÜŞÜM HATALARININ AYRIŞTIRILMASI"
> ara iş talimatı + ARA İŞ 2'de bırakılan S1d bulgusu.

---

## 1. Amaç

Kullanıcının seçtiği **takvim/iş günü** tarihinin, yerel saat dilimi (TR = UTC+3) yüzünden veritabanına
**bir gün erken** yazılması hatasını tüm ekranlarda tespit etmek, gerçek hataları kanıtla ayırmak,
hata OLMAYAN noktaları da açıkça belirtmek ve düzeltme için karar paketi hazırlamaktır.

**Bu tur analiz turudur.** Düzeltme, kullanıcı kararlarından ve açık "UYGULAMA BAŞLASIN" onayından sonra.

## 2. Başlangıç durumu (repository'den doğrulandı)

| Alan | Değer |
|---|---|
| HEAD | `c244508` · origin ile eşit · ağaç temiz (yalnız kullanıcının 2 takip-dışı dosyası) |
| Son kod commit'i | `7cbb52b` · yayın kaydı `e5583c4` |
| Masaüstü sürüm | **1.0.162** (canlıda) |
| Canlı şema | **81** · katalog azamisi `Migration081_Announcements()` · **Migration082 master'da YOK** |
| Yayın havuzu | **BOŞ** (her şey yayınlandı) |
| Production | Bu turda **dokunulmadı** |

## 3. Ana roadmap'teki konum

**ANA ROADMAP: AŞAMA 3 — FINAL KARAR PAKETİ.** Bu ara iş roadmap'i **DEĞİŞTİRMEZ** ve ana aşamayı
ilerletmiş SAYILMAZ. Ara iş yayınlandıktan sonra dönülecek nokta:
**AŞAMA 3 → FIN-B1 / Migration082 ayrı onay süreci.**

## 4. Önceki tamamlanmış/yayınlanmış işler (yalnız BAĞLAM — tekrar açılmaz)

ARA İŞ 2 PAKET-1 ✅ yayınlandı · ADR-181 ✅ · ADR-183 ✅ · M — Excel Merkezi ✅ · O — Barkod/QR ✅ ·
FIN düzeltmeleri ✅ (**Migration082 HARİÇ**) · N/Mobil ⏭️ ATLANDI ·
FIN-B1/Migration082 ⏸️ ayrı onay · Custom Rapor ⏸️ ayrı faz · Ekip+Hiyerarşi+Onay ⏸️ ayrı faz.

## 5. Yeni ara iş kapsamı

**İŞ-1:** Kullanıcının seçtiği takvim tarihinin doğru güne yazılmasını sağlamak (tarih kayması hatası).
Kapsam: aşağıda doğrulanan yazım noktaları — **masaüstü VE web ayrı ayrı**.

### Kapsam DIŞI (kendiliğinden dahil edilmez)
Custom Rapor · Ekip/Hiyerarşi/Onay · FIN-B1/Migration082 · Mobil · yeni özellik ·
**gerçek zaman damgası (`created_at`, audit) alanları** · rapor OKUMA yolu (RPR-06 ile zaten doğru) ·
kapsam dışı refactor.

---

## 6. FAZ TAKİP TABLOSU

| İş | FAZ 0 | FAZ 1 | FAZ 2 | Karar | Uygulama | Test | Yayın | Durum |
|---|---|---|---|---|---|---|---|---|
| İŞ-1 Tarih kayması | ✅ | ✅ | ✅ | ✅ **ONAYLANDI** (ADR-184) | ⏸️ | ⏸️ | ⏸️ | **FAZ 3 — "UYGULAMA BAŞLASIN" ONAYI BEKLİYOR** |

> FAZ 2 kullanıcı tarafından **kesin olarak onaylandı** (2026-08-29). Uygulama/test/yayın fazları
> kullanıcının ayrıca vereceği **"UYGULAMA BAŞLASIN"** onayı olmadan başlatılmaz.

---

## 7. FAZ 0 — DURUM DOĞRULAMA ✅

`CURRENT_PHASE.md` · `MASTER_ROADMAP.md` · `DECISIONS.md` (ADR-180…183) · `KNOWN_ISSUES.md` ·
migration kataloğu · git durumu okundu. **Kullanıcının bildirdiği durumla repository ARASINDA
ÇELİŞKİ BULUNMADI** (§2 tablosu). Kod/test/migration değiştirilmedi, production'a erişilmedi.

---

## 8. FAZ 1 — ANALİZ (yazım yolları doğrulandı)

### 8.0 Hatanın tanımı ve DOĞRU desen

Sistem tarihleri **unix ms (UTC)** olarak saklar. Kullanıcının seçtiği gün, `Kind=Local` bir
`DateTime`/`DateTimeOffset`'ten HAM olarak ms'e çevrilirse yerel ofset uygulanır:
`2 Ağustos 00:00 (UTC+3)` → **`1 Ağustos 21:00 UTC`** → tarih filtreli her raporda **bir gün erken**.

**DOĞRU dönüşüm (projede zaten var, belgeli):**
- Web: `DepoWise.Web.Services.FieldChecks.ToUnixMs` — `FieldChecks.cs:33-39`; doc yorumu bu hatayı
  birebir anlatıyor: *"o, yerel saat dilimini uygular (TR = UTC+3) → 00:00 yerel = 21:00 UTC ÖNCEKİ GÜN;
  tarih BİR GÜN KAYARDI"* (`FieldChecks.cs:28-29`).
- Ortak/masaüstü: `DepoWise.Application.Reports.ReportDateRange.ToMs` (RPR-06) — aynı kural.
- Masaüstü satır-içi doğru desen: `new DateTimeOffset(DateTime.SpecifyKind(d.Value.Date, DateTimeKind.Utc)).ToUnixTimeMilliseconds()`
  (Duyurular · Zimmet · Takvim · Evrak · Proje · Satın Alma · İş Emri · Maliyet Merkezi).
- **Emsal düzeltme:** Yakıt ekranı ADR-182/S1a ile bu kurala geçirildi (`FuelViewModel.IsGunuMs`).

### 8.1 ⚠️ ÖNCEKİ SAYIM DÜZELTİLDİ

ARA İŞ 2'nin S1d notu **"10 ekran / 17 nokta"** diyordu. Güncel kodda yeniden sayıldığında
**11 ekran / 19 masaüstü yazım noktası** bulundu (+ web'de 1 nokta). Sayım düzeltildi; eksik iki nokta
`InvoicesViewModel` ve `PartiesViewModel`'ın ikinci tarih alanlarıdır (vade tarihleri).

### 8.2 MASAÜSTÜ bulguları (19 nokta / 11 ekran) — `dosya:satır` kanıtlı

**🔴 SINIF A-1 — HER ZAMAN kayar** (alan `new DateTimeOffset(DateTime.Today)` ile başlar → kullanıcı
hiçbir şeye dokunmasa bile yerel gece yarısı gönderilir):

| # | Ekran | Alan | Yazım noktası | Alan tanımı |
|---|---|---|---|---|
| 1-3 | Stok Girişi | `docDate` (3 akış: giriş/çıkış/transfer) | `StockEntryViewModel.cs:422, 457, 470` | `:173` |
| 4 | Stok Sayım | `docDate` | `StockCountViewModel.cs:235` | `:26` |
| 5 | Stok Dağıtım | `docDate` | `StockDistributeViewModel.cs:171` | `:36` |

**🟠 SINIF A-2 — kullanıcı gün SEÇİNCE kayar** (alan `DateTimeOffset.Now` ile başlar ya da boştur;
DatePicker seçilen günü yerel gece yarısı verir. Ayrıca yerel saat 00:00–03:00 arasında kayıt açılırsa
"bugün" bile bir gün geriye düşer):

| # | Ekran | Alan | Yazım noktası | Alan tanımı |
|---|---|---|---|---|
| 6 | Fatura | `InvoiceDate` | `InvoicesViewModel.cs:344` | `:237` (`Now`) |
| 7 | Fatura | `DueDate` (vade) | `InvoicesViewModel.cs:345` | `:238` (boş → seçilir) |
| 8 | Finans | `TxnDate` | `FinanceViewModel.cs:328` | `:302` (`Now`) |
| 9 | Finans | transfer `TDate` | `FinanceViewModel.cs:383` | `:351` (`Now`) |
| 10 | Muayene | `NextDate`/`PostponeDate` | `InspectionViewModel.cs:144` | `:49` (boş) |
| 11 | Muayene | `LastDate` | `InspectionViewModel.cs:147` | `:48` (boş) |
| 12 | Bakım | `PerformedDate` | `MaintenanceViewModel.cs:625` | `:385` (boş) |
| 13-14 | Günlük Faaliyet | `PerformedDate` ×2 | `DailyActivityViewModel.cs:530, 560` | `:299` (`Now`) |
| 15 | Günlük Faaliyet | `ActivityDate` | `DailyActivityViewModel.cs:583` | `:299` (`Now`) |
| 16 | Cari | `EntryDate` | `PartiesViewModel.cs:356` | `:321` (`Now`) |
| 17 | Cari | `DueDate` (vade) | `PartiesViewModel.cs:358` | `:322` (boş → seçilir) |
| 18 | Ödeme/Tahsilat | `TxnDate` | `PaymentsViewModel.cs:305` | `:175` (`Now`) |
| 19 | Talep | `RequestDate` | `RequestsViewModel.cs:352` | `:171` (`Now`) |

### 8.3 WEB bulguları — ⚠️ **AYRI İNCELENDİ, MASAÜSTÜYLE AYNI DEĞİL**

**✅ SINIF C — hata YOK** (doğru dönüşüm; `FieldChecks.ToUnixMs` = UTC gece yarısı):
`Inspection.razor:138` · `Daily.razor:475` · `Invoices.razor:608` · `Parties.razor:516-517` ·
`Maintenance.razor:614` · `Finance.razor:556` · `Payments.razor:491` · `Requests.razor:302`.
**✅ SINIF C — hata YOK** (satır-içi doğru desen, `SpecifyKind(Utc)`):
`StockCount.razor:191` · `StockDistribute.razor:254`.

**🔴 SINIF A — WEB'DE DE GERÇEK KAYMA (YENİ BULGU, S1d'de yoktu — S1d yalnız masaüstünü taramıştı):**

| Ekran | Alan | Kanıt | Neden hatalı |
|---|---|---|---|
| **Stok Girişi/Çıkışı/Transfer (web)** | `docDate` | `Stock.razor:258` → `new DateTimeOffset(_docDate.Value.Date).ToUnixTimeMilliseconds()` (alan `:255` `DateTime.Today`) | `TimeSpan.Zero`/`SpecifyKind(Utc)` YOK → yerel ofset uygulanır. Yorumu "masaüstüyle aynı" diyor; ne yazık ki **ikisi de aynı şekilde yanlış**. |

➡️ **Sonuç:** "web zaten doğru" demek YANLIŞ olurdu. Web'de **1 gerçek hata** var, kalan 10 nokta doğru.

### 8.4 Ortak katman / API / DB bulguları

- **Semantik tek kaynak:** `DateEntryPolicy` (`src/DepoWise.Application/Security/DateEntryPolicy.cs:8-16`)
  iki tarihi açıkça ayırır: **işlem tarihi (iş günü — `doc_date`, `entry_date`, `performed_date`…)** ve
  **kayıt anı (`created_at`)**. Bu ara işin kapsamındaki alanların TAMAMI birinci gruptadır (takvim
  tarihi); `created_at`/audit alanlarına **DOKUNULMAYACAK**.
- **Sunucu kapısı:** `DateEntryPolicy.Uygula(s, istenen)` — `btn-backdate` yetkisi yoksa istenen tarih
  yok sayılır ve "şimdi" yazılır (`:35-38`). Bu kapı **DEĞİŞMEYECEK**.
- **API sözleşmesi:** istemciler zaten **unix ms** gönderiyor; düzeltme yalnız istemcinin ÜRETTİĞİ
  değeri düzeltir → **API sözleşmesi değişmez, DTO/alan eklenmez.**
- **DB:** ilgili sütunlar BIGINT unix ms; **şema değişikliği gerekmez.**

### 8.5 Senkronizasyon

Etkilenen tablolar iş senkronunda mevcut; ancak düzeltme **yalnız üretilen DEĞERİ** düzeltir —
tablo listesi, LWW kuralları, çakışma sözleşmesi ve SNK-13 **değişmez**. Çevrimdışı masaüstünde
açılan kayıt da düzeltilmiş değerle yazılır; senkron paketi büyümez.
🔎 *Doğrulanacak (karar sonrası):* offline→server→web→offline turunda gün korunumu testi.

### 8.6 Yetki / Tenant / BranchAccess / Export

Hiçbiri etkilenmiyor: değişiklik yalnız istemcinin tarih→ms dönüşümüdür. `btn-backdate` kapısı,
tenant süzmesi, `BranchAccess` ve export yetkileri **aynen kalır**.

### 8.7 Performans

Sıfır etki: aynı sayıda alan, aynı sorgular. N+1 yok, senkron paketi büyümüyor.

### 8.8 Geriye uyumluluk / eski istemciler

⚠️ **Önemli:** düzeltme yayınlansa bile **1.0.162 ve öncesi masaüstüler kaymalı değer yazmaya devam
eder** (istemci tarafı dönüşüm). Yani doğru veri ancak güncellenen istemcilerden gelir. Bu, PK-TAR-05'in
konusudur. API/DB sözleşmesi değişmediği için eski istemciler **bozulmaz**.

### 8.9 Production veri etkisi (production'a BAĞLANMADAN çıkarımlar)

- Geçmişte masaüstünden girilen **stok belgeleri** (giriş/çıkış/transfer/sayım/dağıtım) ve web'den
  girilen **stok belgeleri** büyük olasılıkla **bir gün erken** kayıtlıdır (her ikisi de A-1/A sınıfı).
- Diğer alanlarda (fatura, cari, ödeme, finans, bakım, muayene, faaliyet, talep) kayma **yalnız
  kullanıcı farklı bir gün seçtiyse veya gece 00:00–03:00 arasında kayıt açtıysa** oluşmuştur.
- 📌 **PRODUCTION DOĞRULAMASI GEREKTİREN NOKTA:** hangi kayıtların gerçekten kaydığı ancak canlı veriye
  salt-okunur bakışla ölçülebilir (ör. `doc_date % 86400000 == 75_600_000` → 21:00 UTC deseni).
  **Bu turda YAPILMADI** ve kullanıcı açık izin vermeden yapılmayacaktır.
- **Geçmiş veri düzeltmesi bu ara işin kapsamında DEĞİLDİR** — PK-TAR-02 ile ayrı karara bağlanır.

### 8.10 Mevcut testler / kilitler

`ReportDateRangeTests` (RPR-06 okuma yolu paritesi) · `IslemTarihiTests` (TRH-01: iş günü ↔ kayıt anı
ayrımı, geri tarih yetkisi) · `YakitTarihGunTests` (ADR-182 ile eklenen yazım-yolu kilidi + kaynak-düzeyi
kilit). Yeni düzeltmeler için benzer **yazım yolu** kilitleri gerekecek (§11).

### 8.11 FAZ 1'de HENÜZ TAMAMLANMAYANLAR (karar sonrası derinleştirilecek)

1. Her alan için servis yazım satırı ve DB sütununun tek tek doğrulanması (semantik örneklemle teyit edildi).
2. Her ekranın XAML/Razor tarih kontrolünün tipi (DatePicker ↔ DateTime/DateTimeOffset) tek tek.
3. Muayene `NextDate` gibi "gelecek tarih" alanlarının rapor/uyarı eşiklerine etkisi.
4. Offline→sync→web gün korunumu senaryosunun mevcut testlerde karşılığı.

---

## 9. FAZ 2 — RİSK VE KARAR PAKETİ ✅ **KARARLAR ONAYLANDI (2026-08-29 · ADR-184)**

### 9.0 ONAYLANAN KARARLAR — ÖZET (bağlayıcı)

| PK | Karar | Bağlayıcı sonuç |
|---|---|---|
| **PK-TAR-01** | **A** | **20 noktanın TAMAMI** düzeltilir (masaüstü 19 / 11 ekran + web `Stock.razor:258`). Web'in doğru 10 noktasına DOKUNULMAZ. Her nokta iki platformda AYRI doğrulanır; "diğerinde de aynıdır" varsayımı YASAK. |
| **PK-TAR-02** | **A** | **Yalnız ileriye dönük.** Geçmiş canlı kayıtlar değiştirilmez, otomatik data-fix yok. Geçmiş düzeltmesi gerekirse **AYRI iş/karar**. |
| **PK-TAR-03** | **A** | **Tek kaynaklı dönüşüm.** Ortak/masaüstü tarafında tek yardımcı; web'de mevcut doğru `FieldChecks.ToUnixMs` tek kaynak kalır ve `Stock.razor` ona bağlanır. Web'in mimari sınırı korunur (proje referansı yok). İki platform paritesi + ham dönüşüme dönüşü engelleyen kaynak kilitleri testle kanıtlanır. |
| **PK-TAR-04** | **A** | **Zaman damgalarına dokunulmaz** (`created_at`, `updated_at`, audit, gerçek an). Yalnız iş günü/takvim alanları; `DateEntryPolicy` ayrımı korunur. |
| **PK-TAR-05** | **A** | **Eski istemciler kabul.** ≤1.0.162 kaymalı yazmaya devam edebilir; sunucuda telafi yuvarlaması YOK; API/DB sözleşmesi değişmez; kullanıcılar 1.0.163+'a yönlendirilir; yayın öncesi raporda açıkça yazılır. |
| **PK-TAR-06** | **B** | **Production ölçümü YAPILMAZ.** Bu ara iş boyunca canlı API/DB'ye erişilmez (SELECT dahil). Gerekirse ileride ayrı karar. |
| **PK-TAR-07** | **A** | **Tek başına, migration'sız yayın.** Migration oluşturulmaz, Migration082 dahil edilmez, **şema 81 kalır**. API + Web + masaüstü paketi (1.0.162 → uygun artış). FIN-B1 · Custom Rapor · Ekip+Onay · N/Mobil durumları DEĞİŞMEZ. |

> Aşağıdaki alt bölümler kararların dayandığı seçenek analizidir; **seçilen seçenekler yukarıdaki
> tabloda kesinleşmiştir** ve tekrar sorulmaz.

### PK-TAR-01 — Kapsam: hangi noktalar düzeltilsin?
- **A (ÖNERİLEN):** **20 noktanın tamamı** (19 masaüstü + 1 web `Stock.razor`). Tek ve tutarlı kural;
  aynı hata sınıfı ikinci kez geri gelmez.
- **B:** Yalnız 🔴 A-1 sınıfı (stok belgeleri: masaüstü 5 + web 1 = 6 nokta). En dar dokunuş; ama
  fatura/cari/ödeme gibi alanlarda kullanıcı gün seçtiğinde hata DEVAM EDER.
- **C:** Yalnız masaüstü (web'e dokunma). ❌ ÖNERİLMEZ — web'de kanıtlanmış bir hata var (`Stock.razor:258`).
- **Etki:** A/B/C hiçbirinde migration, senkron, yetki veya API sözleşmesi değişikliği YOK.

### PK-TAR-02 — Geçmiş (canlı) kayıtlar ne olacak?
- **A (ÖNERİLEN):** **Yalnız ileriye dönük düzeltme**; mevcut kayıtlara DOKUNULMAZ (ADR-182/PK-T3 ile
  aynı ilke, canlı veri koruma protokolüne uygun). Eski kayıtlar bir gün erken görünmeye devam eder.
- **B:** Ayrı, ayrıca onaylı **veri düzeltme işi** (pg_dump + kapsam listesi + geri alma planı) — bu ara
  işin DIŞINDA, ayrı faz.
- **C:** Otomatik migration/data-fix. ❌ ÖNERİLMEZ — canlı veriyi toplu değiştirir, geri dönüşü zordur.
- **Not:** A seçilse bile hangi kayıtların kaydığını görmek için **salt-okunur** canlı ölçüm ayrıca
  onaylanabilir (PK-TAR-06).

### PK-TAR-03 — Düzeltme biçimi
- **A (ÖNERİLEN):** **Tek paylaşımlı yardımcı**: masaüstü tarafında ortak bir "iş günü → UTC ms"
  yardımcısı (ör. `DepoWise.Application.Ui` içinde) ve web tarafında mevcut `FieldChecks.ToUnixMs`.
  Böylece kural TEK yerde kalır ve hata üçüncü kez doğmaz; kaynak-düzeyi test kilidiyle korunur.
- **B:** Her ekranda satır-içi düzeltme (mevcut 8 ekranın yaptığı gibi). Daha az dosya ama kural
  kopyalanmaya devam eder.
- **Etki:** A'da yeni paylaşımlı dosya + web csproj satırı (emsali var); davranış aynı.

### PK-TAR-04 — Zaman damgası alanlarına dokunulacak mı?
- **A (ÖNERİLEN):** **HAYIR.** `created_at`, audit ve gerçek an alanları AYNEN kalır; yalnız
  "iş günü" semantiğindeki alanlar düzeltilir (DateEntryPolicy ayrımı esas alınır).
- **B:** Tümünü tek tipe indir. ❌ ÖNERİLMEZ — denetim izini bozar.

### PK-TAR-05 — Eski istemci (≤1.0.162) davranışı
- **A (ÖNERİLEN):** Kabul et: eski masaüstüler güncellenene kadar kaymalı yazmaya devam eder; yayın
  notunda belirtilir, kullanıcılar 1.0.163'e yönlendirilir. Sunucu tarafında düzeltme YAPILMAZ.
- **B:** Sunucuda "gelen ms'i gün başına yuvarla" normalizasyonu. ❌ ÖNERİLMEZ — meşru saat-bazlı
  değerleri de bozar ve iki farklı doğruluk kaynağı yaratır.

### PK-TAR-06 — Canlıda kayma ölçümü (salt-okunur)
- **A:** Yayından sonra **salt-okunur** bir ölçüm yapılsın (hangi tablolarda kaç kayıt 21:00 UTC
  desenine sahip) — veri DEĞİŞTİRİLMEZ, yalnız rapor.
- **B (ÖNERİLEN):** Şimdilik yapılmasın; PK-TAR-02=B seçilirse o işin ilk adımı olarak yapılsın.

### PK-TAR-07 — Yayın stratejisi
- **A (ÖNERİLEN):** Bu ara iş **migration'sızdır** → tamamlanınca mevcut (boş) yayın havuzuna girer ve
  onay verildiğinde **tek başına yayınlanır**; FIN-B1/Migration082 beklenmez, canlı şema 81 kalır.
- **B:** Başka işlerle birlikte yayınlanmak üzere bekletilsin.

---

## 10. Uygulama planı (yalnız PLAN — henüz uygulanmadı)

1. Ortak "iş günü → UTC ms" kuralının tek kaynağa alınması (PK-TAR-03).
2. 🔴 A-1 noktaları: Stok Girişi (3) · Stok Sayım · Stok Dağıtım (masaüstü) + **web `Stock.razor`**.
3. 🟠 A-2 noktaları: Fatura(2) · Finans(2) · Muayene(2) · Bakım · Günlük Faaliyet(3) · Cari(2) ·
   Ödeme · Talep (masaüstü).
4. Testler (§11) · tam süit · izole PG · 3 Release build.
5. Belgeler + commit/push → **YAYIN ÖNCESİ RAPOR** → DUR.

## 11. Test planı (tasarım — kod yazılmadı)

- **Yazım yolu kilidi:** "1 Ağustos seç → DB'de 1 Ağustos", "2 Ağustos seç → DB'de 2 Ağustos"
  (birden çok saat dilimi ofsetiyle, makineden bağımsız).
- **Gün sınırı:** 00:00:00.000 ve 23:59:59.999; gece yarısı ve 00:00–03:00 kayıt senaryosu.
- **Uçtan uca:** yazım → rapor okuma aynı günde görünüyor mu (her modül için).
- **Regresyon:** `created_at`/audit değerleri değişmedi; `btn-backdate` kapısı aynı.
- **Kaynak-düzeyi kilit:** düzeltilen ekranlar ham dönüşüme geri dönemez.
- **İki lehçe:** SQLite + izole PostgreSQL.
- **Senkron:** offline yazım → sync → web okuma → tekrar offline; gün korunumu.
- **Web/masaüstü paritesi:** aynı gün seçimi iki platformda AYNI ms üretir.

## 12. Migration planı

**MIGRATION GEREKMİYOR.** Değişiklik yalnız istemcilerin ürettiği değerdedir; şema, sütun, indeks ve
tablo yapısı aynı kalır. **Canlı şema 81'de kalır.** Migration082 (FIN-B1) bu ara iş tarafından
kendiliğinden geri getirilmez. (PK-TAR-02=B seçilirse veri düzeltmesi AYRI faz + AYRI onay olur.)

## 13. Production politikası

Analiz ve uygulama aşamalarında production'a **bağlanılmaz** (SELECT dahil). Yalnız kullanıcı
"YAYINLA" dedikten sonra, izin verilen **salt-okunur** kontroller yapılır.

## 14. Yayın planı

Tamamlanınca yayın öncesi rapor → "YAYINLA" onayı → API + Web + masaüstü paketi (1.0.163) →
yayın sonrası salt-okunur kontroller → **AŞAMA 3'e dönüş**. Migration çalıştırılmaz.

## 15. Rollback

Kod düzeltmesi olduğundan geri dönüş = önceki imaja/sürüme dönüş; **şema geri alma gerekmez**
(migration yok). Yazılmış veriler geri alınmaz (yalnız ileriye dönük düzeltme — PK-TAR-02=A).

---

## 16. CHATGPT DEVAM NOKTASI

- **Ana roadmap aşaması:** AŞAMA 3 — FINAL KARAR PAKETİ (bu ara iş onu ilerletmez)
- **Aktif ara iş:** **ARA İŞ 3 — TARİH DÖNÜŞÜM HATALARI**
- **Aktif faz:** **FAZ 2 ✅ KARARLAR ONAYLANDI (ADR-184) → FAZ 3 ⏸️ "UYGULAMA BAŞLASIN" bekliyor**
- **Tamamlanan ara işler:** ARA İŞ 2 PAKET-1 (+ADR-183) · Rapor Ara İşi (ADR-181)
- **Yayınlanmış işler:** ARA İŞ 2 PAKET-1 · ADR-181 · ADR-183 · M · O · FIN (082 hariç)
- **Verilen kararlar:** **PK-TAR-01=A · 02=A · 03=A · 04=A · 05=A · 06=B · 07=A** (§9.0 · ADR-184) —
  bunlar TEKRAR SORULMAZ
- **Bekleyen kararlar:** ana roadmap'te **FIN-B1/Migration082** (bu ara işin dışında)
- **Migration durumu:** bu ara iş için **GEREKMİYOR**; canlı şema **81**; Migration082 master'da yok
- **Production durumu:** **DOKUNULMADI** (PK-TAR-06=B gereği uygulama boyunca da dokunulmayacak)
- **Kod yazıldı mı:** **HAYIR** · **Test koşuldu mu:** HAYIR · **Migration:** oluşturulmadı
- **Son commit:** ARA İŞ 3 karar kaydı (öncesi `832efb3` analiz · `e5583c4` yayın · `7cbb52b` son kod)
- **Son başarılı test:** tam süit **2.977/0/39** · izole PG **47/47** · 3 Release **0 hata** (`7cbb52b`)
- **Sıradaki TEK iş:** kullanıcının **"UYGULAMA BAŞLASIN"** onayı → FAZ 3 uygulama
- **Ara iş tamamlanınca dönülecek nokta:** **AŞAMA 3 — FIN-B1 / Migration082 ayrı onay süreci**
- **Ayrı fazlar (dokunulmadı):** Custom Rapor ⏸️ · Ekip+Hiyerarşi+Onay ⏸️ · N/Mobil ⏭️ ATLANDI
