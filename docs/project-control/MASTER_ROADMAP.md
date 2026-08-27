# Alpnex — MASTER ROADMAP (Yeni Özellik Yol Haritası)

> Son güncelleme: **2026-08-27** · Kaynak: yeni özellik teknik analizi (aynı gün) + kullanıcının
> "Canlı Veri Koruma Odaklı Geliştirme Protokolü".
> **Bu dosya yeni özellik geliştirmenin ANA KONTROL BELGESİDİR** — her özellik başlamadan önce ilgili
> bölüm okunur, bittikten sonra güncellenir. Sıra kullanıcı onayı olmadan DEĞİŞTİRİLMEZ.

---

## 0. VERİ GÜVENLİĞİ KURALLARI (her özellikte geçerli — 2026-08-27 protokolü)

- Alpnex **canlı veriyle** kullanılıyor (2026-08-27'den beri). Canlıda DELETE/UPDATE/INSERT/migration/
  DDL/seed/test verisi **YOK**; canlı yalnız salt-okunur incelenebilir.
- Mevcut kayıtların ID, company_id, branch_id, tarih, version, operation_id, fiyat/kur, ilişki ve
  audit değerleri **DEĞİŞMEZ**. Otomatik backfill/normalizasyon/taşıma **YOK** — gerekirse dur + onay.
- Migration yalnız gerçekten gerekliyse, yalnız **eklemeli** (additive); izole ortamda iki lehçede
  (SQLite + PostgreSQL) test edilir; **canlıya uygulama ayrı açık onay ister**.
- Yeni özellik eski kayıtlarla çalışmalı; yeni alanlar mevcut kayıtları geçersiz kılmamalı.
- Test kademeli: Seviye 1 (build + ilgili testler) her işte · Seviye 2 (yetki/tenant/şube/senkron)
  gerekince · Seviye 3 (tam süit) yalnız gerçek risk varsa.
- Nihai büyük kullanıcı simülasyonu (5–10k kayıt, tüm kombinasyonlar) roadmap BİTİNCE ayrı
  **"Final Kullanıcı Simülasyonu ve Stabilizasyon"** fazıdır — şimdi yapılmaz.

## 0.1 DOKUNULMAMASI GEREKEN SİSTEMLER (görev doğrudan gerektirmedikçe)

Tenant izolasyonu · BranchAccess/BranchService kapsam mantığı · yetki mimarisi · AppScreens ·
rapor dispatch · senkron firma kapıları ve `BusinessSyncService.Tables` sırası · idempotency ·
update/checksum/release · migration runner/kataloğu · stok defteri (`stock_movements` ana kaynak) ·
tarih semantiği (iş günü `doc_date` vb. ↔ kayıt anı `created_at`, ADR-162) · audit.

---

## 1. ROADMAP SIRASI (kullanıcı onayı: 2026-08-27)

| Faz | # | Özellik | Tür | Durum |
|---|---|---|---|---|
| **FAZ 1 — Temel veri modeli** | 1 | **C — Proje / Şantiye (+ G Saha)** | Yeni ana menü | ✅ **TAMAMLANDI** (2026-08-27, ADR-164 · yayın bekliyor) |
| | 2 | A — Evrak / Belge Yönetimi | Yeni ana menü | ✅ **TAMAMLANDI** (2026-08-28, ADR-165 · yayın bekliyor) |
| | 3 | E — Varlık / Ekipman | Yeni ana menü | ✅ **TAMAMLANDI** (2026-08-28, ADR-166 · yayın bekliyor) |
| **FAZ 2 — Operasyon** | 4 | B — Zimmet | Yeni ana menü | 🔵 **SIRADAKİ** (önce ürün soruları: stok ilişkisi · devir) |
| | 5 | D — Maliyet Merkezi | Alt menü (Finans) + raporlar | BEKLİYOR |
| | 6 | P — Satın Alma | Yeni ana menü | BEKLİYOR |
| **FAZ 3 — İş yönetimi** | 7 | F — İş Emri | Yeni ana menü | BEKLİYOR |
| | 8 | H — Takvim | Yeni ana menü (tek ekran) | BEKLİYOR |
| **FAZ 4 — Bilgilendirme/UX** | 9 | I — Bildirim Merkezi | Uyarılar genişletmesi | BEKLİYOR |
| | 10 | J — Duyuru | Yeni ana menü | BEKLİYOR |
| | 11 | K — Global Arama | Üst bar ortak özelliği (menü DEĞİL) | BEKLİYOR |
| | 12 | L — Dashboard | Mevcut ekran dönüşümü | BEKLİYOR |
| **FAZ 5 — Verimlilik/Mobil** | 13 | M — Excel Merkezi | Import/Export genişletmesi | BEKLİYOR |
| | 14 | O — Barkod / QR | Ortak özellik + alanlar | BEKLİYOR |
| | 15 | N — Mobil | Önce responsive web | BEKLİYOR |
| **FİNAL** | — | Kullanıcı Simülasyonu ve Stabilizasyon | Ayrı faz | BEKLİYOR |

**Kilit bağımlılıklar:** F, (C+E+B+D)'ye · P, (C+D)'ye · B, E'ye · D, C'ye · H/I tam değeri F'ye ·
L neredeyse hepsine bağlı. G Saha ayrı modül DEĞİL, C'nin içindedir. Erken yapılırsa yeniden yazım
doğuranlar: F (C/E'siz), P (C/D'siz), L (erken pano).

---

## 2. AKTİF İŞ — C: PROJE / ŞANTİYE (+ SAHA)

### 2.1 Analiz bulguları (2026-08-27, kod incelemesi)

- `branches` tablosu: `id, company_id, parent_id, name, kind('branch'|'site'), code, password(M024),
  created_at/updated_at/version/is_deleted`. **Şantiye kavramı ve hiyerarşi ZATEN VAR.**
- `BranchAccess.Expand` (ŞB-04): kullanıcı kapsamı şube + **altındaki tüm alt şubeleri** otomatik
  kapsar → şantiyeye bağlanan alt noktalar (saha) kapsama bedavaya girer.
- Tüm hareket tabloları (`stock_movements/documents`, `fuel_*`, `daily_activities`, `invoices`,
  `finance_*`) `branch_id` taşır → şantiye/proje bazlı tüketim mevcut veriyle raporlanabilir;
  **hiçbir mevcut kayda dokunmak gerekmez.**
- **Şubeler SUNUCU-OTORİTELİDİR** (BusinessSync'te DEĞİL): masaüstü CRUD'u çevrimiçi API ile yapar
  (`OrgServerClient`), yerel kopya `BranchMirror`/`BranchMirrorApply` ile `/api/public/branches`
  uçtan aynalanır (Id, Name, Code, Kind, ParentId). → Proje meta katmanı da aynı deseni izlerse
  **`BusinessSyncService.Tables` değişikliği GEREKMEZ**; yalnız aynaya alan eklenir (masaüstü
  çevrimdışı görüntüleyecekse).
- Ekranlar küçük: `Branches.razor` 216 satır · `BranchesViewModel` 211 · `BranchesView.axaml` 128.
  `AppScreens`'te tek kayıt ("Şube / Şantiye", Both).

### 2.2 Ürün kararları — durum

| # | Soru | Karar | Tarih |
|---|---|---|---|
| PK-C1 | Bir proje birden fazla şubeye/şantiyeye yayılabilir mi? | ✅ **"Şimdilik tek, ileride çok"** — model çokluya hazır, UI tek seçim | 2026-08-27 |
| PK-C2 | Saha modeli | ✅ **branches.kind üçüncü değer "Saha"** (ayrı tablo/kapsam sistemi YOK, mevcut hiyerarşi) | 2026-08-27 |
| PK-C3 | Proje kartı alanları | ✅ **Tüm alanlar** (ad · durum · başlangıç/bitiş · sorumlu · konum · açıklama · şantiye); ad dışında opsiyonel | 2026-08-27 |
| PK-C4 | Yetki | ✅ **Ayrı kapı YOK** — branches modülü + BranchAccess kapsamı | 2026-08-27 |

### 2.3 Teknik kararlar (2026-08-27, uygulandı)

- Veri modeli: **Seçenek 2** — `projects` + `project_branches` ilişki tablosu (çokluya hazır);
  mevcut tablolara SIFIR dokunuş, hareket tablolarına project_id EKLENMEDİ.
- Migration: **Migration073_Projects** (şema v73, yalnız CREATE — PRJ13/PRJ14 kanıtlı). ⚠️ CANLIYA
  HENÜZ UYGULANMADI; deploy anında MigrationRunner koşacak → yayın onayı migration onayını içerir.
- Sunucu-otoriteli (şubeler deseni); BusinessSync değişikliği YOK. Ayrıntı: [PRJ_01_PROJE_SANTIYE.md](PRJ_01_PROJE_SANTIYE.md)

### 2.4 Model seçenekleri (PK-C1 cevabına göre)

- **Seçenek 1 (proje = şantiye üstü katman, 1:1):** yeni eklemeli tablo `branch_projects`
  (branch'e 1:1 meta: tarih/durum/sorumlu/açıklama). En az iş; çok-şubeli proje OLMAZ.
- **Seçenek 2 (proje ayrı varlık, N şube : 1 proje):** yeni `projects` tablosu + `branches`'a
  eklemeli `project_id` kolonu. Bir proje birden çok şantiyeyi kapsar; hareket tablolarına
  DOKUNULMAZ (proje → şube kümesi → branch_id ile çözülür). Orta iş.
- **Seçenek 3 (N:N ya da harekete project_id):** hareket tablolarına kolon = büyük migration
  ailesi + tüm servisler. ÖNERİLMEZ (yeniden yazım riski en yüksek).

### 2.5 Yapılan değişiklikler (2026-08-27 — tamamlandı, yayın bekliyor)

- Migration073 + ProjectService + API uçları (/api/projects) + web Projects.razor + masaüstü
  ProjectsView/ViewModel + menü kaydı + Saha türü + Çöp Kutusu/ekran logu katalogları.
- Testler: ProjeTests 15/15 (kapsam/tenant/migration-güvenliği dahil) · parite 19/19 · ilgili 69/69 ·
  üç Release derleme 0 hata. Canlıya yazma YOK. Tam kayıt: [PRJ_01_PROJE_SANTIYE.md](PRJ_01_PROJE_SANTIYE.md)

---

## 3. TAMAMLANAN İŞLER (yeni roadmap kapsamında)

- 2026-08-27 · Yol haritası teknik analizi (15 özellik, bağımlılık grafiği, sıra) — kod değişikliği yok.
- 2026-08-27 · ADR-162 (işlem tarihi/kayıt anı) + ADR-163 (Ekran Araçları/log) — roadmap ÖNCESİ
  "eksik alanlar" listesinin 1-2. maddeleri; yayınlandı (API v173 · web v198 · masaüstü 1.0.159).

## 4. MIGRATION KAYITLARI (yeni roadmap kapsamında)

| Tarih | Migration | Özellik | Canlıya uygulandı mı |
|---|---|---|---|
| 2026-08-27 | Migration073_Projects (v73 — yalnız CREATE, 2 yeni tablo) | C — Proje/Şantiye | **HAYIR** (yayın onayıyla uygulanacak) |
| 2026-08-28 | Migration074_DocumentFields (v74 — yalnız ADD COLUMN + indeks) | A — Evrak | **HAYIR** (yayın onayıyla uygulanacak) |
| 2026-08-28 | Migration075_Equipment (v75 — yalnız CREATE, 2 yeni tablo) | E — Ekipman | **HAYIR** (yayın onayıyla uygulanacak) |

---
---

# ARŞİV — ESKİ MASTER ROADMAP (2026-08-11)

> Aşağıdaki bölüm 2026-08-11 tarihli önceki yol haritasıdır; **tarihsel kayıt olarak korunur,
> güncellenmez.** İçindeki fazların büyük kısmı (depo bazlı stok, ön muhasebe G4, ekran görünürlüğü
> G5, senkron ölçeklenme) tamamlanmış ve canlıya alınmıştır (bkz. CURRENT_PHASE.md geçmişi).

# Alpnex — MASTER ROADMAP

> Son güncelleme: **2026-08-11** · Kaynak: [`AUDIT_2026-08-11.md`](AUDIT_2026-08-11.md)
> **Hedef:** satılabilir ilk sürüm (MVP+). "Güzel olur" fikirleri FAZ 9+'a.

---

## Faz sırası ve gerekçesi

Sıra **bağımlılığa** göredir, isteğe göre değil. Bir faz, öncekinin çıktısına dayanır.

| Faz | Ad | Neden bu sırada | Durum |
|---|---|---|---|
| **FAZ C** | **Depo bazlı stok** (`STK-00…08`, `TRF-01`) | **Projenin 1 numaralı mimari borcu**; ön muhasebe ve şantiye maliyeti buna bağlı. **KARAR-7=A ile açıldı** | 🔵 **AKTİF** — `STK-00…07` ✅ |
| **FAZ A** | Kullanıcı bug'ları + yetki tamamlama (`YTK-05`, `UIX-01`, `YTK-06`, `YTK-08`) | Küçük, bağımsız, düşük riskli. **Silinmedi** — stok altyapısı mimari öncelik olduğu için sonraya alındı; FAZ C içinde uygun boşlukta veya FAZ C sonrası yapılır | BEKLEMEDE |
| **FAZ B** | Ekran görünürlük yönetimi (`GRN-01`) | Yetki sistemine dokunur; yeni stok ekranları doğduğunda hazır olması iyi olur | BEKLEMEDE |
| **FAZ D** | Ön muhasebe **alan hazırlığı** (`MUH-01`) | FAZ C ile **aynı migration ailesinde** yapılmalı; sonra eklenirse geçmiş veri boş kalır | FAZ C'ye bağlı |
| **FAZ E** | Senkron ölçeklenme (`SNK-06…10`) | FAZ C şemayı büyütür; senkron optimizasyonu ondan sonra anlamlı | FAZ C'ye bağlı |
| **FAZ F** | Güncelleme + sürüm uyumu (`GNC-01…03`) | Çok makineli kullanım öncesi | BEKLEMEDE |
| **FAZ G** | Kalan parite + rapor envanteri (`PRT-02`, `P-1`) — **`RPR-01` ✅ erken tamamlandı** | Çekirdek oturduktan sonra | BEKLEMEDE |
| **FAZ H** | Ön muhasebe **modülü** (`MUH-02…05`) | Alan hazırlığı + depo stoku bitmeden başlanmaz | BEKLEMEDE |
| **FAZ I** | Test/veri bütünlüğü + performans (`TST-01`, index, N+1) | Özellikler bitince | BEKLEMEDE |
| **FAZ J** | Canlıya geçiş: güvenlik sertleştirme, API sürümleme | En son | BEKLEMEDE |
| FAZ 9+ | Backlog: mobil, BI, e-Fatura, lastik ömrü, puantaj | Gelir sonrası | ERTELENDİ |

---

## Bağımlılık ağacı

```
KARAR-7 (malzeme kartı: firma geneli mi şube bazlı mı?)
   │
   ▼
FAZ C — STK-01 (stock_balances'a depo boyutu) ──┬──▶ STK-02..07 (UI/API/rapor)
                                                 ├──▶ TRF-01 (depo→depo transfer)
                                                 └──▶ MUH-01 (cari + maliyet merkezi alanları)
                                                            │
FAZ A (YTK-05, YTK-06, YTK-08, UIX-01) ── bağımsız ─────────┤
FAZ B (GRN-01) ── yetki sistemine dokunur ──────────────────┤
                                                            ▼
                                        FAZ E (SNK-06..10)  →  FAZ H (MUH-02..05)
                                                            →  FAZ I (test/perf)
                                                            →  FAZ J (deploy/güvenlik)
```

**Kural:** Aynı özelliğin web ve masaüstü tarafı **aynı faz içinde** bitirilir. Biri diğerini bekleyemez.

---

## FAZ A — Kullanıcı bug'ları + yetki tamamlama *(A sınıfı, maliyetsiz)*

| ID | İş | Ortam | Bağımlılık |
|---|---|---|---|
| `YTK-05` | Yetkiler ekranına **"Tümünü Temizle / Sıfırla"** + seçili kullanıcının yetkisini toptan güncelleme | Web + Masaüstü | — |
| `UIX-01` | **Tablo satır seçimi** — yazıya tıklayınca seçilmeme sorunu; ortak bileşen düzeyinde çöz | Web + Masaüstü | — |
| `YTK-06` | Yeni ekranın **yetki kataloğuna otomatik dâhil olması** — kaçırmayı imkânsız kılan mekanizma (rota/menü ↔ `AppModules.All` eşleşmesini doğrulayan test) | Ortak | — |
| `YTK-08` | Delegasyon tavanı **regresyon testi** (kendinde olmayan yetkiyi verememe — zaten çalışıyor, kilitlenecek) | API testi | — |

## FAZ B — Ekran görünürlük yönetimi

| ID | İş |
|---|---|
| `GRN-01` | Ekranın **web/masaüstü görünürlüğünü** yönetim ekranından açıp kapatma. Yetki sisteminden **ayrı** eksen: yetki "kim görebilir", görünürlük "nerede görünür". `AppModules` yanına `screen_platforms` tablosu; menü kurucu ikisini birden uygular |

## FAZ C — Depo bazlı stok 🔵 **AKTİF** *(KARAR-7 = A)*

Tasarım + migration planı: [`FAZ_C_DEPO_BAZLI_STOK_TASARIM.md`](FAZ_C_DEPO_BAZLI_STOK_TASARIM.md)

| ID | İş | Durum |
|---|---|---|
| `STK-00` | Migration güvenlik kanıtı — production kopyasında toplam korunumu | ✅ **TAMAM** (uyuşmayan 0) |
| `STK-01` | `stock_balances` → `(company_id, material_id, location_id)` + defterden yeniden hesaplama + **migration içi doğrulama** (iki lehçe) | ✅ **TAMAM** (Migration064 etkin) |
| `STK-02` | Tüm okuma/yazma yollarını (16 nokta) lokasyon bazlı yap — giriş/çıkış/sayım/transfer/ters kayıt + liste/rapor/dashboard | ✅ **TAMAM** (17 yeni test · PG+SQLite provası) |
| `STK-03` | API uçları + DTO (lokasyon parametresi) + **lokasyon sahiplik doğrulaması** | ✅ **TAMAM** (17 yeni senaryo · 2 yeni bakiye ucu) |
| `STK-04` | Web: giriş/çıkış/sayım/transfer lokasyonu · malzeme kartı kırılımı · hareket lokasyonu + filtre · açılış deposu | ✅ **TAMAM** (14 yeni senaryo · 3 hata düzeltildi) |
| `STK-05` | Masaüstü: lokasyonlu giriş/çıkış/sayım/açılış + kart kırılımı + hareket lokasyonu · **çevrimdışı korundu** | ✅ **TAMAM** (13 yeni senaryo · 4 hata düzeltildi) |
| `STK-06` | Rapor lokasyon boyutu: Stok Durumu (kırılım+filtre) · Stok Sayım (sayılan depo) | ✅ **TAMAM** (14 yeni senaryo) |
| `STK-07` | Senkron sertifikasyonu — 11 senaryo, gerçek HTTP senkron uçları · **kod değiştirilmedi** | ✅ **TAMAM** |
| `STK-08` | "Atanmamış → depo" toplu dağıtım ekranı (Web + masaüstü + çevrimdışı) | ✅ **TAMAM** (17 senaryo · gerçek veriyle doğrulandı) |
| `BKM-04` | **Bakım malzemesinin çıktığı depo** — oturum şubesi varsayılan + kullanıcı seçebilir (**KARAR-9 / ADR-103**) | ✅ **TAMAM** (44 senaryo · izole PG · iptal simetrisi kilitli) |
| `RPR-01` | Web ↔ masaüstü rapor filtre paritesi (koruma testi) | ✅ **TAMAM** (18 senaryo) |
| `SNK-11` | Türetilmiş bakiye senkron paketinden çıkarıldı (~86 KB/tur) | ✅ **TAMAM** |
| `SNK-12` | Masaüstünde depo listesi senkron turunda tazeleniyor | ✅ **TAMAM** (8 senaryo) |
| `STK-B1` | `movement_type` gösterim kataloğu — 8 tür tek kaynağa bağlandı, ham İngilizce kaçağı ve Web↔masaüstü ıraksaması kapatıldı | ✅ **TAMAM** (24 senaryo · STK-10 adım 0) |
| `TRF-01` | Transfer **kodu zaten var** — UI paritesi + bakiyeye yansıma doğrulaması | BEKLEMEDE |
| `STK-10a` | **"Stok Hareketleri" raporu** — katalog + Kaynak/Hedef + `Date`+`Location` + **gerçek XLSX doğrulaması** | ✅ **TAMAM** (41 senaryo · izole PG sorgu planı · Web/masaüstünde kod değişmedi) |
| `STK-10b-1` | **Hareket Türü filtresi** — 6/6 katman · seçenekler MovementTypeOptions'tan · fail-closed | ✅ **TAMAM** (28 senaryo · RPR-01 yeşil · izole PG) |
| `STK-10b-2` | **Serbest metin arama** — 6/6 katman · semantik mevcut ekrandan birebir | ✅ **TAMAM** (41 senaryo · RPR-01 yeşil · izole PG) |
| `STK-10b-3` | **Malzeme filtresi + autocomplete** — 6/6 katman · scope BÜYÜMEDİ · mevcut indeks yetti | ✅ **TAMAM** (32 senaryo · RPR-01 yeşil · izole PG) |
| `STK-10b-4` | **2 hareket ekranı + B-1** — filtreler tek SQL kaynağına bağlandı, lokasyon süzmesi sunucuya indi (ADR-105) | ✅ **TAMAM** (36 senaryo · ekran=rapor=XLSX · izole PG) → **STK-10 BİTTİ** |
| `STK-B2` | Arama `stock_documents.note`'u da kapsasın mı? Davranış değişikliği | ⛔ **KARAR BEKLİYOR** |
| `RPR-02` | Web rapor isteği oturumun ŞUBESİNİ taşımıyor (JWT'de yok) — tüm raporları etkiler | ⛔ **KARAR BEKLİYOR** |
| `STK-09` · `STK-11` | Lokasyon bazlı dashboard · eski float artığı temizliği | BEKLEMEDE |

> **Önemli:** `StockService.Transfer` çok malzemeli, tek transaction, idempotent ve negatif-guard'lı olarak
> **zaten uygulanmış**; hareketler kaynak/hedef lokasyonla yazılıyor. Bugün yalnız **bakiyeye yansımıyor**
> çünkü bakiye lokasyonsuz. `STK-01` bunu kökten çözer.
>
> **Offline kısıtı (değişmez):** Bakiye türetilmiş bir önbellektir ve **LWW ile senkronlanmaz**;
> iki tarafta da defterden yeniden hesaplanır (CLAUDE.md §4 — stokta LWW yasak).

## FAZ D — Ön muhasebe alan hazırlığı

| ID | İş |
|---|---|
| `MUH-01` | Para hareketi doğuran her kayda **cari + maliyet merkezi (şantiye) + belge** alanları (malzeme alışı, yakıt, bakım, şantiye gideri). FAZ C migration'ları ile **birlikte** |

## FAZ E — Senkron ölçeklenme

| ID | İş |
|---|---|
| `SNK-06` | Girişte tam pull yerine **kalıcı imleçle delta pull** |
| `SNK-07` | Snapshot'ı **sayfala** (batch/chunk) |
| `SNK-08` | Yanıt **sıkıştırma** (gzip) |
| `SNK-09` | Delta ölçütünü **monoton sunucu sırasına** taşı (saat kaymasına karşı) |
| `SNK-10` | Silinen kayıtların delta ile taşındığını **test et** |

## FAZ F — Güncelleme + sürüm uyumu

| ID | İş |
|---|---|
| `GNC-01` | Otomatik güncelleme davranış denetimi (mevcut plandan devir) |
| `GNC-02` | **API ↔ istemci sürüm uyumu** (eski masaüstü / yeni API) |
| `GNC-03` | Sunucu disk politikası — paket saklama tavanı, `/data` doluluk uyarısı |

## FAZ G — Kalan parite + rapor

`PRT-02` (ekran adı eşleme) · `P-1` (masaüstü "Bağı Kaldır") · Personel/Muayene filtre+export

✅ `RPR-01` (rapor filtre paritesi) **2026-08-11'de tamamlandı** — FAZ C içinde erken alındı, çünkü
STK-06 aynı riski canlı olarak gösterdi. Kayıt: [`RPR_01_FILTRE_PARITESI.md`](RPR_01_FILTRE_PARITESI.md)

## FAZ H — Ön muhasebe modülü

`MUH-02` cari hesap · `MUH-03` kasa/banka + tahsilat/ödeme · `MUH-04` gider dağıtımı (şantiye maliyeti) · `MUH-05` ön muhasebe raporları

## FAZ I — Test / performans

`TST-01` (33 atlanan test) · index denetimi · N+1 taraması · büyük liste sayfalama

## FAZ J — Canlıya geçiş

Güvenlik sertleştirme · API sürümleme kararı · yük testi

---

## Devredilen teknik borçlar (fazlanmamış, kapanmadı)

`G6-10…G6-19` · `G6-21/22/24` · `H-6` (masaüstü sunucu adresi 7 dosyada tekrar) · `H-7` · `GRP3-JOIN` ·
`brands/vehicle_models JOIN` · `500→400` · `WEB-01b` · `GUV-01b` · `TLP-B5` · `MUA-01/02` · `G2-08` ·
`TMZ-01/03` · Personel 200 kayıt tavanı · `SNK-05` (karar bekliyor) · `WEB-02` · `YET-01` (karar bekliyor)

Ayrıntı: [`TASK_BACKLOG.md`](TASK_BACKLOG.md).
