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
| **FAZ 1 — Temel veri modeli** | 1 | **C — Proje / Şantiye (+ G Saha)** | Yeni ana menü | ✅ **TAMAMLANDI** (2026-08-27, ADR-164 · YAYINLANDI 2026-08-28) |
| | 2 | A — Evrak / Belge Yönetimi | Yeni ana menü | ✅ **TAMAMLANDI** (2026-08-28, ADR-165 · YAYINLANDI 2026-08-28) |
| | 3 | E — Varlık / Ekipman | Yeni ana menü | ✅ **TAMAMLANDI** (2026-08-28, ADR-166 · YAYINLANDI 2026-08-28) |
| **FAZ 2 — Operasyon** | 4 | B — Zimmet | Yeni ana menü | ✅ **TAMAMLANDI** (2026-08-28, ADR-167 · YAYINLANDI 2026-08-28) |
| | 5 | D — Maliyet Merkezi | Alt menü (Finans) | ✅ **TAMAMLANDI** (2026-08-28, ADR-168 · YAYINLANDI 2026-08-28) |
| | 6 | P — Satın Alma | Yeni ana menü | ✅ **TAMAMLANDI** (2026-08-28, ADR-169 · YAYINLANDI 2026-08-28) — **FAZ 2 BİTTİ** |
| **FAZ 3 — İş yönetimi** | 7 | F — İş Emri | Yeni ana menü | ✅ **TAMAMLANDI** (2026-08-28, ADR-170 · YAYINLANDI 2026-08-28) — [F_ISEMRI_01.md](F_ISEMRI_01.md) |
| | 7b | Bakım-Ekipman genişletmesi (PK-F9 ayrı işi) | Mevcut modüle küçük ekleme | BEKLİYOR (sırası serbest — teknik bağımlılığı yok) |
| | 8 | H — Takvim | Yeni ana menü (tek ekran) | ✅ **TAMAMLANDI** (2026-08-28, ADR-171 · YAYINLANDI 2026-08-28) — [H_TAKVIM_01.md](H_TAKVIM_01.md) |
| **FAZ 4 — Bilgilendirme/UX** | 9 | I — Bildirim Merkezi | Uyarılar genişletmesi | ✅ **TAMAMLANDI** (2026-08-28, ADR-172 · MIGRATION YOK · YAYINLANDI 2026-08-28) — [I_BILDIRIM_01.md](I_BILDIRIM_01.md) |
| | 10 | J — Duyuru | Yeni ana menü | ✅ **TAMAMLANDI** (2026-08-28, ADR-173 · YAYINLANDI 2026-08-28) — [J_DUYURU_01.md](J_DUYURU_01.md) |
| | 11 | K — Global Arama | Üst bar ortak özelliği (menü DEĞİL) | ✅ **TAMAMLANDI** (2026-08-28, ADR-174 · MIGRATION YOK · YAYINLANDI 2026-08-28) — [K_ARAMA_01.md](K_ARAMA_01.md) |
| | 12 | L — Dashboard | Mevcut ekran dönüşümü | ✅ **TAMAMLANDI** (2026-08-28, ADR-175 · MIGRATION YOK · YAYINLANDI 2026-08-28) — [L_DASHBOARD_01.md](L_DASHBOARD_01.md) — **FAZ 4 BİTTİ** |
| **FAZ 5 — Verimlilik/Mobil** | 13 | M — Excel Merkezi | Import/Export genişletmesi | ✅ **TAMAMLANDI** (2026-08-28, ADR-176 · MIGRATION YOK · ✅ **YAYINLANDI 2026-08-29**) — [M_EXCEL_01.md](M_EXCEL_01.md) |
| | 14 | O — Barkod / QR | Ortak özellik + alanlar | ✅ **TAMAMLANDI** (2026-08-29, ADR-177 · MIGRATION YOK · ✅ **YAYINLANDI 2026-08-29**) — [O_BARKOD_QR_01.md](O_BARKOD_QR_01.md) |
| | 15 | N — Mobil | Önce responsive web | ⏭️ **ATLANDI** (kullanıcı kararı 2026-08-29 — bu geliştirme döngüsünde UYGULANMAYACAK; kod/analiz uygulaması yapılmadı) |
| **FİNAL** | — | Kullanıcı Simülasyonu ve Stabilizasyon | Ayrı faz | ✅ **TAMAMLANDI** (2026-08-29, ADR-178 · production'a BAĞLANILMADI) — [FINAL_STABILIZASYON_01.md](FINAL_STABILIZASYON_01.md) · **KARAR PAKETİ de UYGULANDI** (ADR-179): FIN-B1 → **⚠️ ADR-180 (2026-08-29, PK-R4=B) ile MASTER'DAN GERİ ÇEKİLDİ — Migration082 AYRI ONAY BEKLİYOR (tasarım `35d7bce`; canlı şema 81; katalog azamisi yine 81; tamamlanmış SAYILMAZ)** · 🟢 **2026-08-29: FAZ 1 ANALİZ + FAZ 2 KARARLAR TAMAM (ADR-185)** — [FIN_B1_00_ANALIZ.md](FIN_B1_00_ANALIZ.md); **PK-FIN-01=A · 02=B · 03=C · 04=A · 05=A**; ⭐ yeni bulgu üzerine **`sync_inbox` FIN-B1 kapsamına ALINDI** (7. hedef; `company_id` sütunu zaten var → yeni sütun/backfill YOK); normal UNIQUE index (CONCURRENTLY yok); FIN5 yeni sözleşmeye çevrilecek; **tek yayın**: Migration082 + kod + masaüstü **1.0.164**. ✅ **TAMAMLANDI ve YAYINLANDI (2026-08-29)** — kod `d9fc350`; Migration082 (**7 hedef**: 6 operasyon tablosu + `sync_inbox`), 9 idempotency sorgusu + `SyncServer.InboxHas` firma kapsamına alındı, FIN5 yeni sözleşmeye çevrildi + 10 yeni kilit. **CANLI ŞEMA 81 → 82** (`operation_id_company_scope`, 2026-08-29 19:42 UTC); 7 indeks `UNIQUE (company_id, operation_id)`, adlar korundu; **hiçbir kayıt değişmedi** (683/220/3 birebir aynı); masaüstü **1.0.164** (checksum `DA127644…947A789B`), API + Web yeniden dağıtıldı. Yayın öncesi pg_dump yedeği alındı ve doğrulandı. Doğrulama: tam süit **3.036/0**, izole PG **53/53**, 3 Release 0 hata · YET-01 kaldırıldı · ARC-01(a)/RPR-02 zaten çözülmüş çıktı · STK-B2 hayır · SNK-05(a) sözleşme kilitlendi · MAK-01/b korundu — [FINAL_KARAR_PAKETI.md](FINAL_KARAR_PAKETI.md) |

| **ARA İŞ** | — | Rapor Günlük Kırılım + Rapor Türü Yetkileri | Kullanıcı talebi (2026-08-29) | ✅ **TAMAMLANDI ve YAYINLANDI 2026-08-29** (ADR-181 · PK-R1..R4=A·A·A·B · MIGRATION YOK) — [RAPOR_ARA_IS_01.md](RAPOR_ARA_IS_01.md). Ön koşul ADR-180: FIN-B1/Migration082 master'dan geri çekildi (katalog max 81 = canlı şema). Yayın sonrası SONRAKİ ANA İŞ: **AŞAMA 3 — FINAL karar paketi** (FIN-B1/082 AYRI onay; diğer 6 madde ADR-179'da kapandı ve korunuyor). |
| **ARA İŞ 3** | — | Tarih dönüşüm hataları (tarih kayması) — S1d bulgusunun ayrı ara işi | Kullanıcı talebi (2026-08-29) | ✅ **TAMAMLANDI VE YAYINLANDI (2026-08-29)** — kod `ab0d0d4`, kayıt `ARA_IS_3_01_YAYIN_ONCESI.md`; masaüstü **1.0.163** (checksum `27ED96C7…7A81B339`), API + Web yeniden dağıtıldı; tam süit 3026/0, izole PG 52/52, 3 Release 0 hata; **MIGRATION YOK, canlı şema 81 kaldı** — kararlar ADR-184 (PK-TAR-01=A · 02=A · 03=A · 04=A · 05=A · 06=B · 07=A); 20 tarih yazım noktası tek kaynağa bağlandı (`IsGunuTarihi`), web `Stock.razor` düzeltildi · **MIGRATION YOK, canlı şema 81 kalır** · production'a dokunulmadı — [ARA_IS_3_00_ANALIZ.md](ARA_IS_3_00_ANALIZ.md). Yeniden sayım: **11 ekran / 19 masaüstü noktası + web'de 1 gerçek hata** (`Stock.razor:258`); web'in kalan 10 noktası DOĞRU. Bekleyen: **PK-TAR-01…07**. ⚠️ **Bu ara iş ana roadmap'i DEĞİŞTİRMEZ**; yayınlandıktan sonra dönülecek nokta: **AŞAMA 3 — FIN-B1/Migration082 ayrı onay süreci**. |
| **ARA İŞ 4** | — | **Custom Rapor (Rapor Tasarımcısı)** — AŞAMA 3'ün kalan iki ana işinden birincisi | Kullanıcı talebi (2026-08-29) | 🟡 **FAZ 0–2 ✅ (ADR-186: PK-CR-01…08 = A) · FAZ 3/S1 ✅ 14 teknik nokta doğrulandı (nokta 3 GERÇEK TESTLE 5/5) · S2 ⛔ DURDURULDU — PK-CR-09 karar bekliyor (v1 veri kaynağı kümesi)** — [ARA_IS_4_00_ANALIZ.md](ARA_IS_4_00_ANALIZ.md). Onaylanan çerçeve: ham SQL/serbest JOIN **YOK**, merkezî beyaz-liste; tanım tablosu **senkronda** (masaüstü çevrimdışı çalışır); mevcut rapor motoru **genişletilir** (ikinci motor yok); rapor başına **dinamik yetki anahtarı** (yetki için migration yok); `maxRows` **SQL'e iner** + tarih filtresi zorunlu; **tek yayın** (FIN-B1 emsali); FAZ 3→7 sırası. FAZ 3'ün ilk işi **14 teknik noktanın yeniden doğrulanması**. Kanıtlanan kapsam: Custom Rapor kodu **hiç yok** (0 dosya); `TableModel` altı jenerik (kolaylaştırıcı) ama `ReportCatalog.All` sabit 25 kayıt + `Dispatch` **kapalı switch** (ana engel); `Run` **4 güvenlik kapısı** (manager · DataModule/RPR-15 · kategori/ADR-181 · katalog) dinamik raporda da korunmalı; **masaüstü raporu ÇEVRİMDIŞI yerel çalıştırıyor** (`ReportsViewModel:594`) → tanım tablosu senkrona girmezse masaüstü çevrimdışı custom rapor çalıştıramaz; web **online**, kendi motoru yok; web'in **proje referanssız** mimari sınırı korunacak. **MIGRATION GEREKİR** (yeni tanım tablosu → Migration083; bu turda OLUŞTURULMADI), ancak **yetki için migration gerekmez** (`module_key` serbest metin). **KOD YOK · TEST YOK · PRODUCTION'A BAĞLANILMADI.** Ana roadmap sırası DEĞİŞMEDİ; Ekip+Hiyerarşi+Onay ⏸️ başlanmadı. |
| **ARA İŞ 2** | — | PAKET-1: Yakıt tarih/kapsam · Yakıtı Veren son seçim · Yakıt-Günlük · Stok Hareketleri-Günlük · Günlük Faaliyet Detay · Fotoğraf sunucu-otoriteli | Kullanıcı talebi (2026-08-29) | ✅ **TAMAMLANDI ve YAYINLANDI 2026-08-29** (ADR-182 · PK-F/T/V/G/D kararları aynen · **MIGRATION YOK — canlı şema 81 KALDI** · yayın commit'i `386b22d` → masaüstü 1.0.161). ⭐ **ADR-183 DÜZELTMESİ AYNI GÜN YAYINLANDI** (`7cbb52b` → masaüstü **1.0.162**): kullanıcı bildirimi üzerine Araç Raporu—Günlük artık verisi olmayan satırları listelemez (canlıda 1.972→195 satır) ve Stok Hareketleri—Günlük o günün her hareketini malzemesiyle tek tek listeler (1 özet→20 satır). Dönem raporu `vehicle` tam filo KORUNDU. Her iki yayında da 28/28 salt-okunur kontrol. — analiz: [ARA_IS_2_00_ANALIZ.md](ARA_IS_2_00_ANALIZ.md) · plan: [ARA_IS_2_01_PLAN.md](ARA_IS_2_01_PLAN.md) · uygulama: [ARA_IS_2_02_UYGULAMA.md](ARA_IS_2_02_UYGULAMA.md). Commit zinciri `fc3e2fd`→`f2d7daf`→`142b2b5`→`77805cd`→`a638c51`. **İş 6 Custom Rapor ve İş 7 Ekip+Onay: AYRI FAZLAR — henüz uygulanmadı** (migration/senkron gerektirir). Yayın havuzu: M+O+FIN(**082 HARİÇ**)+ADR-181+PAKET-1; **canlı şema 81 KALIR**. CHATGPT DEVAM NOKTASI: CURRENT_PHASE.md. |

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
| 2026-08-27 | Migration073_Projects (v73 — yalnız CREATE, 2 yeni tablo) | C — Proje/Şantiye | **EVET — 2026-08-28 toplu yayın** ([kanıt](TOPLU_YAYIN_2026-08-28.md)) |
| 2026-08-28 | Migration074_DocumentFields (v74 — yalnız ADD COLUMN + indeks) | A — Evrak | **EVET — 2026-08-28 toplu yayın** ([kanıt](TOPLU_YAYIN_2026-08-28.md)) |
| 2026-08-28 | Migration075_Equipment (v75 — yalnız CREATE, 2 yeni tablo) | E — Ekipman | **EVET — 2026-08-28 toplu yayın** ([kanıt](TOPLU_YAYIN_2026-08-28.md)) |
| 2026-08-28 | Migration076_Assignments (v76 — yalnız CREATE, 1 yeni tablo) | B — Zimmet | **EVET — 2026-08-28 toplu yayın** ([kanıt](TOPLU_YAYIN_2026-08-28.md)) |
| 2026-08-28 | Migration077_CostCenters (v77 — yalnız CREATE, 2 yeni tablo; ALTER dahi yok) | D — Maliyet Merkezi | **EVET — 2026-08-28 toplu yayın** ([kanıt](TOPLU_YAYIN_2026-08-28.md)) |
| 2026-08-28 | Migration078_PurchaseOrders (v78 — yalnız CREATE, 2 yeni tablo) | P — Satın Alma | **EVET — 2026-08-28 toplu yayın** ([kanıt](TOPLU_YAYIN_2026-08-28.md)) |
| 2026-08-28 | Migration079_WorkOrders (v79 — yalnız CREATE, 4 yeni tablo) | F — İş Emri | **EVET — 2026-08-28 toplu yayın** ([kanıt](TOPLU_YAYIN_2026-08-28.md)) |
| 2026-08-28 | Migration080_CalendarEvents (v80 — yalnız CREATE, 1 yeni tablo) | H — Takvim | **EVET — 2026-08-28 toplu yayın** ([kanıt](TOPLU_YAYIN_2026-08-28.md)) |
| 2026-08-28 | — (I Bildirim Merkezi MIGRATION GEREKTİRMEDİ; şema 80'de kaldı, alert_reads'e dokunulmadı) | I — Bildirim Merkezi | — |
| 2026-08-28 | Migration081_Announcements (v81 — yalnız CREATE, 1 yeni tablo) | J — Duyuru | **EVET — 2026-08-28 toplu yayın** ([kanıt](TOPLU_YAYIN_2026-08-28.md)) |
| 2026-08-28 | — (K Global Arama MIGRATION GEREKTİRMEDİ; şema 81'de kaldı, indeks de eklenmedi) | K — Global Arama | — |
| 2026-08-28 | — (L Dashboard MIGRATION GEREKTİRMEDİ; şema 81'de kaldı) | L — Dashboard | — |

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
