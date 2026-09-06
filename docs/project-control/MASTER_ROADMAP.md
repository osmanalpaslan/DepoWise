# Alpnex — MASTER ROADMAP (Yeni Özellik Yol Haritası)

> Son güncelleme: **2026-09-03** (ADR-195…199 dalgaları **YAYINLANDI** — rapor bazlı yetki · kategorize
> yetki ağacı · Alan Ayarları ekranı (**Migration087 → canlı şema 87**) · Günlük Faaliyet kayıt tipi
> yetkisi · Tanımlar'a Araç Modelleri · Excel şube şifresi · sekmeler · fotoğraf otomatik taşıma.
> Masaüstü **1.0.171**, API **v185**, Web **v209**. Ayrıntı: `CURRENT_PHASE.md`.) · Kaynak: yeni
> özellik teknik analizi (2026-08-27) + kullanıcının "Canlı Veri Koruma Odaklı Geliştirme Protokolü".
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
| | 7b | Bakım-Ekipman genişletmesi (PK-F9 ayrı işi) | Mevcut modüle küçük ekleme | ✅ **TAMAMLANDI + YAYINLANDI (2026-09-02, canlı şema 86, masaüstü 1.0.168)** (2026-08-30, **ADR-191**) · FAZ 2 kararı **SEÇENEK B**: ekipman hattı **ayrı tablolarla** (A elendi — `vehicle_maintenances`'a 2 gelen FK var, SQLite'ta transaction içinde FK kapatılamadığı için rebuild güvenli değil; içtihatların üçü de FK'siz tablolarda). **Migration086_EquipmentMaintenance** (4 tablo, **hiç ALTER yok**, backfill yok; katalog azamisi **86**, canlı şema **85**). `EquipmentMaintenanceService` + `EquipmentInspectionService` (mevcut `StockBalanceWriter`/`AlertRules` ortak; `MaintenanceService` **hiç değişmedi**); tanım↔ekipman eşlemesi; 9 API ucu; iş emri `entity_type='equipment_maintenance'` (4 nokta, araç bağı korundu); 4 tablo senkronda (ebeveyn sırası doğru); UI'da **yeni ekran YOK** — masaüstünde sekme, web'de hedef seçimi. Yeni yetki modülü **YOK** (`maintenance`/`inspection`). Test: 7b **24/24** · izole PG **56/56** · tam süit **3.196/0/48** · Debug+Release+test build 0 hata. Commit `db49f29` push edildi (2026-08-30). **YAYIN: 2026-09-02** — Migration086 canlıya uygulandı (**şema 85 → 86**), API v181 + Web v206 dağıtıldı, masaüstü **1.0.168**; yayın öncesi yedek alındı ve doğrulandı, canlı veri birebir korundu (`equipment_maintenances` 0 satır). |
| | 8 | H — Takvim | Yeni ana menü (tek ekran) | ✅ **TAMAMLANDI** (2026-08-28, ADR-171 · YAYINLANDI 2026-08-28) — [H_TAKVIM_01.md](H_TAKVIM_01.md) |
| **FAZ 4 — Bilgilendirme/UX** | 9 | I — Bildirim Merkezi | Uyarılar genişletmesi | ✅ **TAMAMLANDI** (2026-08-28, ADR-172 · MIGRATION YOK · YAYINLANDI 2026-08-28) — [I_BILDIRIM_01.md](I_BILDIRIM_01.md) |
| | 10 | J — Duyuru | Yeni ana menü | ✅ **TAMAMLANDI** (2026-08-28, ADR-173 · YAYINLANDI 2026-08-28) — [J_DUYURU_01.md](J_DUYURU_01.md) |
| | 11 | K — Global Arama | Üst bar ortak özelliği (menü DEĞİL) | ✅ **TAMAMLANDI** (2026-08-28, ADR-174 · MIGRATION YOK · YAYINLANDI 2026-08-28) — [K_ARAMA_01.md](K_ARAMA_01.md) |
| | 12 | L — Dashboard | Mevcut ekran dönüşümü | ✅ **TAMAMLANDI** (2026-08-28, ADR-175 · MIGRATION YOK · YAYINLANDI 2026-08-28) — [L_DASHBOARD_01.md](L_DASHBOARD_01.md) — **FAZ 4 BİTTİ** |
| **FAZ 5 — Verimlilik/Mobil** | 13 | M — Excel Merkezi | Import/Export genişletmesi | ✅ **TAMAMLANDI** (2026-08-28, ADR-176 · MIGRATION YOK · ✅ **YAYINLANDI 2026-08-29**) — [M_EXCEL_01.md](M_EXCEL_01.md) |
| | 14 | O — Barkod / QR | Ortak özellik + alanlar | ✅ **TAMAMLANDI** (2026-08-29, ADR-177 · MIGRATION YOK · ✅ **YAYINLANDI 2026-08-29**) — [O_BARKOD_QR_01.md](O_BARKOD_QR_01.md) |
| | 15 | ~~N — Mobil (ayrı uygulama)~~ | — | ❌ **TAMAMEN KALDIRILDI** (kullanıcı kararı **2026-09-04**). Ayrı bir mobil uygulama **yapılmayacak** — bakım/yayın/mağaza yükü isteniyor değil. Yerine **`MOB-W`** geldi: kullanıcı telefonun tarayıcısından web'e girip işi oradan yönetecek. Bu satır yeniden açılmaz; mobil ihtiyacı `MOB-W` ile karşılanır. |
| | **15b** | **`MOB-W` — Mobil tarayıcı uyumluluğu (responsive web)** | Mevcut web'in dar ekran davranışı | ✅ **TAMAMLANDI + YAYINLANDI** (2026-09-04, ADR-204 · Web v212 · migration YOK) — yeni ekran/özellik YOK, **mevcut ekranların telefonda kullanılabilir hâle getirilmesi**; 62 sayfaya dokunulmadı, masaüstü etkilenmedi. Ayrıntı: [MOB_W_01_MOBIL_WEB.md](MOB_W_01_MOBIL_WEB.md) |
| **FİNAL** | — | Kullanıcı Simülasyonu ve Stabilizasyon | Ayrı faz | ✅ **TAMAMLANDI** (2026-08-29, ADR-178 · production'a BAĞLANILMADI) — [FINAL_STABILIZASYON_01.md](FINAL_STABILIZASYON_01.md) · **KARAR PAKETİ de UYGULANDI** (ADR-179): FIN-B1 → **⚠️ ADR-180 (2026-08-29, PK-R4=B) ile MASTER'DAN GERİ ÇEKİLDİ — Migration082 AYRI ONAY BEKLİYOR (tasarım `35d7bce`; canlı şema 81; katalog azamisi yine 81; tamamlanmış SAYILMAZ)** · 🟢 **2026-08-29: FAZ 1 ANALİZ + FAZ 2 KARARLAR TAMAM (ADR-185)** — [FIN_B1_00_ANALIZ.md](FIN_B1_00_ANALIZ.md); **PK-FIN-01=A · 02=B · 03=C · 04=A · 05=A**; ⭐ yeni bulgu üzerine **`sync_inbox` FIN-B1 kapsamına ALINDI** (7. hedef; `company_id` sütunu zaten var → yeni sütun/backfill YOK); normal UNIQUE index (CONCURRENTLY yok); FIN5 yeni sözleşmeye çevrilecek; **tek yayın**: Migration082 + kod + masaüstü **1.0.164**. ✅ **TAMAMLANDI ve YAYINLANDI (2026-08-29)** — kod `d9fc350`; Migration082 (**7 hedef**: 6 operasyon tablosu + `sync_inbox`), 9 idempotency sorgusu + `SyncServer.InboxHas` firma kapsamına alındı, FIN5 yeni sözleşmeye çevrildi + 10 yeni kilit. **CANLI ŞEMA 81 → 82** (`operation_id_company_scope`, 2026-08-29 19:42 UTC); 7 indeks `UNIQUE (company_id, operation_id)`, adlar korundu; **hiçbir kayıt değişmedi** (683/220/3 birebir aynı); masaüstü **1.0.164** (checksum `DA127644…947A789B`), API + Web yeniden dağıtıldı. Yayın öncesi pg_dump yedeği alındı ve doğrulandı. Doğrulama: tam süit **3.036/0**, izole PG **53/53**, 3 Release 0 hata · YET-01 kaldırıldı · ARC-01(a)/RPR-02 zaten çözülmüş çıktı · STK-B2 hayır · SNK-05(a) sözleşme kilitlendi · MAK-01/b korundu — [FINAL_KARAR_PAKETI.md](FINAL_KARAR_PAKETI.md) |

| **ARA İŞ** | — | Rapor Günlük Kırılım + Rapor Türü Yetkileri | Kullanıcı talebi (2026-08-29) | ✅ **TAMAMLANDI ve YAYINLANDI 2026-08-29** (ADR-181 · PK-R1..R4=A·A·A·B · MIGRATION YOK) — [RAPOR_ARA_IS_01.md](RAPOR_ARA_IS_01.md). Ön koşul ADR-180: FIN-B1/Migration082 master'dan geri çekildi (katalog max 81 = canlı şema). Yayın sonrası SONRAKİ ANA İŞ: **AŞAMA 3 — FINAL karar paketi** (FIN-B1/082 AYRI onay; diğer 6 madde ADR-179'da kapandı ve korunuyor). |
| **ARA İŞ 3** | — | Tarih dönüşüm hataları (tarih kayması) — S1d bulgusunun ayrı ara işi | Kullanıcı talebi (2026-08-29) | ✅ **TAMAMLANDI VE YAYINLANDI (2026-08-29)** — kod `ab0d0d4`, kayıt `ARA_IS_3_01_YAYIN_ONCESI.md`; masaüstü **1.0.163** (checksum `27ED96C7…7A81B339`), API + Web yeniden dağıtıldı; tam süit 3026/0, izole PG 52/52, 3 Release 0 hata; **MIGRATION YOK, canlı şema 81 kaldı** — kararlar ADR-184 (PK-TAR-01=A · 02=A · 03=A · 04=A · 05=A · 06=B · 07=A); 20 tarih yazım noktası tek kaynağa bağlandı (`IsGunuTarihi`), web `Stock.razor` düzeltildi · **MIGRATION YOK, canlı şema 81 kalır** · production'a dokunulmadı — [ARA_IS_3_00_ANALIZ.md](ARA_IS_3_00_ANALIZ.md). Yeniden sayım: **11 ekran / 19 masaüstü noktası + web'de 1 gerçek hata** (`Stock.razor:258`); web'in kalan 10 noktası DOĞRU. Bekleyen: **PK-TAR-01…07**. ⚠️ **Bu ara iş ana roadmap'i DEĞİŞTİRMEZ**; yayınlandıktan sonra dönülecek nokta: **AŞAMA 3 — FIN-B1/Migration082 ayrı onay süreci**. |
| **ARA İŞ 4** | — | **Custom Rapor (Rapor Tasarımcısı)** — AŞAMA 3'ün kalan iki ana işinden birincisi | Kullanıcı talebi (2026-08-29) | ✅ **TAMAMLANDI ve YAYINLANDI (2026-08-30)** — kod `2669176`; ADR-186 (PK-CR-01…10 = A); **canlı şema 82 → 83** (`custom_reports`, 07:52:37 UTC), masaüstü **1.0.165**, API + Web yeniden dağıtıldı; v1 kaynakları: Malzeme · Araç · Günlük Faaliyet; ham SQL/JOIN YOK; 6 güvenlik kapısı; tanımlar senkronda (çevrimdışı çalışır); **mevcut iş verisi DEĞİŞMEDİ** — [ARA_IS_4_00_ANALIZ.md](ARA_IS_4_00_ANALIZ.md). Onaylanan çerçeve: ham SQL/serbest JOIN **YOK**, merkezî beyaz-liste; tanım tablosu **senkronda** (masaüstü çevrimdışı çalışır); mevcut rapor motoru **genişletilir** (ikinci motor yok); rapor başına **dinamik yetki anahtarı** (yetki için migration yok); `maxRows` **SQL'e iner** + tarih filtresi zorunlu; **tek yayın** (FIN-B1 emsali); FAZ 3→7 sırası. FAZ 3'ün ilk işi **14 teknik noktanın yeniden doğrulanması**. Kanıtlanan kapsam: Custom Rapor kodu **hiç yok** (0 dosya); `TableModel` altı jenerik (kolaylaştırıcı) ama `ReportCatalog.All` sabit 25 kayıt + `Dispatch` **kapalı switch** (ana engel); `Run` **4 güvenlik kapısı** (manager · DataModule/RPR-15 · kategori/ADR-181 · katalog) dinamik raporda da korunmalı; **masaüstü raporu ÇEVRİMDIŞI yerel çalıştırıyor** (`ReportsViewModel:594`) → tanım tablosu senkrona girmezse masaüstü çevrimdışı custom rapor çalıştıramaz; web **online**, kendi motoru yok; web'in **proje referanssız** mimari sınırı korunacak. **MIGRATION GEREKİR** (yeni tanım tablosu → Migration083; bu turda OLUŞTURULMADI), ancak **yetki için migration gerekmez** (`module_key` serbest metin). **KOD YOK · TEST YOK · PRODUCTION'A BAĞLANILMADI.** Ana roadmap sırası DEĞİŞMEDİ; Ekip+Hiyerarşi+Onay ⏸️ başlanmadı. |
| **ARA İŞ 5** | — | **Ekip + Hiyerarşi + Onay** — AŞAMA 3'ün kalan ikinci ana işi | Roadmap (AŞAMA 3) | 🟢 **FAZ 0 ✅ · FAZ 1 ✅ · FAZ 2 ✅ KARARLAR KESİNLEŞTİ (ADR-187, 2026-08-30)** — [ARA_IS_5_00_ANALIZ.md](ARA_IS_5_00_ANALIZ.md). Kararlar: **PK-EK-01=C** (Malzeme Talebi **+ Satın Alma**; İş Emri kapsam dışı) · **02=B** (kullanıcı tabanlı hiyerarşi + `/api/lookups/sync` aynası; `users` tablosuna sütun EKLENMEZ) · **03=B** (ayrı `approval_instance`/`approval_step`) · **04=A** (zincir snapshot) · **05=A** (yalnız çevrimiçi onay) · **06=A** (3 alt faz) · **07=B** (ekip yetkisi mevcut **`users`** modülünde; yeni `teams` modülü YOK). İş kuralları: çoklu üyelik **Evet** · derinlik **4** · zincir **opsiyonel** · reddedilen yeniden gönderilemez · self-approval **yalnız admin** · ekip yöneticisi **üye yönetir + onaylar** · ekipler arası görünürlük **açık** · ekip **firma bazlı** · çevrimdışı onay **kesin yasak** (UI'da engelle + uyarı) · ret gerekçesi **herkese açık**. **FAZ 3 🔄 AKTİF (2026-08-30, ADR-188): §9'un 6 açık noktası KESİNLEŞTİ + ALT FAZ 1 (Ekip Tanımı) ✅ TAMAMLANDI** — **Migration084_Teams** (`teams` + `team_members`, katalog azamisi **84**), `TeamService`, `/api/teams` uçları, web `/teams` ekranı, `/api/lookups/sync` ekip aynası, masaüstü **salt okunur** ekran. Satın Alma kararı: **zincir başlatıldıysa onay tamamlanmadan mal kabul YOK** (kapı servis/API'da); `purchase_orders.status` **değişmez**; onaycı **kullanıcı hiyerarşisinden** (ekip lideri otomatik onaycı **değil**); çevrimdışı onay **kesin yasak**. **ALT FAZ 2 ✅ TAMAMLANDI (ADR-189):** Migration085 (`user_hierarchy` + `approval_instance` + `approval_step`, katalog azamisi **85**), hiyerarşi motoru (**4 düğüm**, döngüsüz, N+1'siz), **tek onay motoru** (snapshot değişmez, adım sahipliği, self-approval yalnız admin, eşzamanlılık güvenli), Malzeme Talebi **opsiyonel** zinciri + eski akış korundu, **Satın Alma'da onaysız mal kabul engeli** (servis kapısı — eski istemci bypass edemez), **çevrimdışı onay imkânsız** (motor yalnız sunucuda; masaüstü onayı `OnlineApprovalClient` ile sunucuya gider, `sync_outbox`'a onay yazılmaz), hiyerarşi `/api/lookups/sync` aynasında. **ALT FAZ 3 ✅ TAMAMLANDI (ADR-190):** "Onaylamalarım" ekranı (masaüstü + web, rota `/approvals`, `AppScreens` kaydı **`request_approval`** modülünde — yeni yetki modülü YOK). Liste **tek sorgu** ile üretilir (önceki N+1 düzeltildi, sorgu sayan test eklendi); başkasının kuyruğu istenemez; **çevrimdışıyken liste de karar da yok, uyarı var, hiçbir yerel/`sync_outbox` kaydı oluşmaz**; ret gerekçesi zorunlu; eşzamanlı ikinci karar sunucuda reddedilir. **ALT FAZ 3 için Migration086 GEREKMEDİ** (mevcut şema yeterli — katalog **85** kaldı). **ARA İŞ 5 kod seviyesinde TAMAMLANDI.** **Canlı şema 83 — production'a DOKUNULMADI** · commit/push **yapılmadı** · **yayın yapılmadı**. Ana roadmap sırası DEĞİŞMEDİ. |
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
| **FAZ C** | **Depo bazlı stok** (`STK-00…08`, `TRF-01`, `STK-12`) | **Projenin 1 numaralı mimari borcu**; ön muhasebe ve şantiye maliyeti buna bağlı. **KARAR-7=A ile açıldı** | ✅ **TAMAM** (2026-09-04) — kalan `STK-B2`/`RPR-02` karar bekliyor |
| **FAZ A** | Kullanıcı bug'ları + yetki tamamlama (`YTK-05`, `UIX-01`, `YTK-06`, `YTK-08`) | Küçük, bağımsız, düşük riskli. **Silinmedi** — stok altyapısı mimari öncelik olduğu için sonraya alındı; FAZ C sonrasında yapıldı | ✅ TAMAM (2026-09-04, ADR-209) |
| **FAZ B** | Ekran görünürlük yönetimi (`GRN-01`) | Yetki sistemine dokunur; yeni stok ekranları doğduğunda hazır olması iyi olur | ✅ **TAMAM** — G5/MNU-B2 turlarında yapılmış, yol haritası güncellenmemişti (2026-09-04'te ölçülüp doğrulandı) |
| **FAZ D** | Ön muhasebe **alan hazırlığı** (`MUH-01`) | FAZ C ile aynı migration ailesinde yapılmalı; sonra eklenirse geçmiş veri boş kalır | ✅ **TAMAM** (2026-09-04, ADR-210/211/212) — Migration089+090, ikisi de yalnız ekleme |
| **FAZ E** | Senkron ölçeklenme (`SNK-06…10`) | FAZ C şemayı büyütür; senkron optimizasyonu ondan sonra anlamlı | ✅ **TAMAM** (2026-09-04, ADR-214) — SNK-06 zaten yapılmıştı · SNK-08 gzip eklendi · **SNK-09'da sessiz veri kaybı bulundu ve düzeltildi** · SNK-10 kilitlendi · SNK-07 ölçüm olmadan yapılmadı |
| **FAZ F** | Güncelleme + sürüm uyumu (`GNC-01…03`) | Çok makineli kullanım öncesi | ✅ **TAMAM** (2026-09-04, ADR-215) — mekanizmalar vardı ama kullanıcıya ULAŞMIYORDU: sürüm uyumsuzluğu artık görünür, disk doluluğunun eşiği var |
| **FAZ G** | Kalan parite + rapor envanteri (`PRT-02`, `P-1`) | Çekirdek oturduktan sonra | ✅ **TAMAM** (2026-09-04, ADR-216) — `P-1` ve `RPR-01` zaten yapılmıştı; gerçek eksik Personel/Muayene Excel dışa aktarımıydı (projenin kendi liste ekranı kuralı çiğneniyordu) |
| **FAZ H** | Ön muhasebe **modülü** (`MUH-02…05`) | Alan hazırlığı + depo stoku bitmeden başlanmaz | ✅ **TAMAM** (2026-09-04, ADR-217) — MUH-02/03/05 zaten kuruluydu (6 rapor); gerçek eksik MUH-04'tü: maliyet merkezi özeti ekranda vardı ama RAPOR değildi |
| **FAZ I** | Test/veri bütünlüğü + performans (`TST-01`, index, N+1) | Özellikler bitince | ✅ **TAMAM** (2026-09-05, ADR-218) — atlanan testler ölü değil **PG lehçe kapsamıymış**; bu gecenin 3 migration'ı izole PG'de doğrulandı · **liste sorgularının indeksi yoktu** (Migration091) · yeni grid yollarına N+1 sayacı |
| **FAZ J** | Canlıya geçiş: güvenlik sertleştirme, API sürümleme | Özellik fazlarının sonuncusu | ✅ **TAMAM** (2026-09-05, ADR-218) — sertleştirmenin çoğu zaten vardı; boşluk **tarayıcı güvenlik başlıklarıydı**. CSP ve canlı yük testi bilinçli olarak YAPILMADI (gerekçeler kayıtlı) · API sürümleme: **sürüm öneki yok** kararı teyit edildi |
| **FAZ K** | **UÇTAN UCA DOĞRULAMA VE ONARIM** (`UUD-01`) — 33 bölümlük protokol: gerçek kullanıcı simülasyonu · her alan/buton · CRUD · API · DB · tenant izolasyonu · yetki · 10.000+ kayıt · concurrency · network failure · masaüstü+web · sync · audit · performans · erişilebilirlik · regresyon. **Bulunan hatalar kök nedeniyle DÜZELTİLİR**, yalnız raporlanmaz | **En son.** Kullanıcı yazılımcı değil; "çalışıyordur" denip geçilen katmanlar burada kanıtlanır. Protokol boyunca **production'a dokunulmaz** | ✅ **TAMAM** (2026-09-05, ADR-219) — **dört sessiz kusur** bulundu ve düzeltildi (belge no sınırı yok · dışa aktarım 200'de kesiliyor · "yüklenemedi" ≠ "kayıt yok" · iki farklı sayfa tavanı) · 25.000 kayıt ölçüldü · **37 yeni test** · üretime dokunulmadı — [FAZ_K_UCTAN_UCA_DOGRULAMA.md](FAZ_K_UCTAN_UCA_DOGRULAMA.md) |
| **LST-01** | **Tavanlı listelerin sayfalanması** — `Stock` · `Maintenance` · `StockMovements` · `Personnel` · `Audit` · `StockChangeLog` (+ tavansız `Inspection`/`Purchasing`). Hepsinde ARA İŞ 6'daki kusurun aynısı var: kayıt var ama tavan yüzünden **sessizce görünmüyor**. Desen kurulu (`SearchDistributions` + `/api/fuel/grid` + iki arayüz), risk sırasıyla uygulanacak | 🟢 **BÜYÜK ÖLÇÜDE TAMAM** (2026-09-07) —  ·  ·  düzeltildi (gerçek toplam + "en yenisinden N tanesi gösteriliyor" uyarısı);  zaten sayfalanmış uç kullanıyordu; /// ölçüldü, **tavanları yok**. Kalan tek yer: araç detayındaki bakım alt listesi (tek araç için 200). |
| **MOB-W** | **Mobil tarayıcı uyumluluğu** (responsive web) | Kullanıcı telefondan yönetmek istiyor; ayrı mobil uygulama **iptal edildi** (2026-09-04). Yeni özellik doğurmaz, mevcut ekranları dar ekranda kullanılabilir kılar → bağımlılığı yok, hemen yapılabilir | ✅ **TAMAM + YAYINLANDI** (2026-09-04, ADR-204 · Web v212) |
| FAZ 9+ | Backlog: BI, e-Fatura, lastik ömrü, puantaj | Gelir sonrası | ERTELENDİ |
| ~~Mobil uygulama~~ | ~~Ayrı iOS/Android uygulaması~~ | **KAPSAM DIŞI** — kullanıcı kararı 2026-09-04. İhtiyaç `MOB-W` (mobil tarayıcı) ile karşılanıyor | ❌ KALDIRILDI |

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

✅ **FAZ A TAMAM** (2026-09-04, ADR-209) — ayrıntı: [FAZ_A_KULLANICI_BUGLARI_YETKI.md](FAZ_A_KULLANICI_BUGLARI_YETKI.md)

| ID | İş | Ortam | Durum |
|---|---|---|---|
| `YTK-05` | Yetkiler ekranına **"Tümünü Temizle / Sıfırla"** + seçili kullanıcının yetkisini toptan güncelleme | Web + Masaüstü | ✅ **TAMAM** — toptan yazma ZATEN vardı (tek çağrıda full-replace); eksik olan **tüm ağacı** kapsayan "Tümünü Temizle" eklendi (sunucuya yazmaz, Sıfırla'dan ayrı) |
| `UIX-01` | **Tablo satır seçimi** — yazıya tıklayınca seçilmeme sorunu; ortak bileşen düzeyinde çöz | Web + Masaüstü | ✅ **TAMAM** — kök neden G3'te çözülmüştü ama ortak stili kullanmayan **3 ekran** dışarıda kalmıştı (Onaylar · Ekipler · Ekipman Bakım) → düzeltildi + **kapsam kilidi testi**. Web ölçüldü: kusur YOK |
| `YTK-06` | Yeni ekranın **yetki kataloğuna otomatik dâhil olması** — kaçırmayı imkânsız kılan mekanizma | Ortak | ✅ **TAMAM** — mekanizma vardı ama kilit **tek yönlüydü**: masaüstü kapalı, web AÇIK. `S9b_Webde_Yetim_Ekran_Yok` eklendi |
| `YTK-08` | Delegasyon tavanı **regresyon testi** (kendinde olmayan yetkiyi verememe) | API testi | ✅ **TAMAM** — ölçüldü: 7 regresyon testi (`G1b_*`) zaten mevcut ve servis katmanında zorunlu. **Kod değişikliği gerekmedi**, yalnız kayıt güncellendi |

## FAZ B — Ekran görünürlük yönetimi

| ID | İş |
|---|---|
| `GRN-01` | Ekranın **web/masaüstü görünürlüğünü** yönetim ekranından açıp kapatma. Yetki sisteminden **ayrı** eksen: yetki "kim görebilir", görünürlük "nerede görünür" | ✅ **TAMAM** — G5/MNU-B2 turlarında yapılmış; 2026-09-04'te ölçülerek doğrulandı: `Migration065_ScreenPlatformVisibility` (`screen_platform_visibility` tablosu) · `ScreenVisibility` çözümleyicisi (**yalnız daraltır, genişletmez**) · yönetim ekranı `ScreenVisibility.razor` · menü kurucu iki platformda da uygular (`MenuLayoutService.cs:175`, `ShellViewModel.cs:869/897`) · masaüstünde gezinme kapısı da var (`ShellViewModel.cs:990`) · kilitlenmeye karşı `AppScreens.Protected` (bu ekran + `users` + `permissions` her platformda birden kapatılamaz) · `ScreenPlatformVisibilityTests` |

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
| `TRF-01` | Transfer **kodu zaten var** — UI paritesi + bakiyeye yansıma doğrulaması | ✅ **TAMAM** (2026-09-04, ADR-205) — servis olgun çıktı; **maliyet merkezi transferde sessizce yutuluyordu** (iki platformda, düzeltildi) · hedef listesinden kaynak dışlandı · onayda hedefin adı · `TransferPariteTests` — [TRF_01_TRANSFER_PARITE.md](TRF_01_TRANSFER_PARITE.md) |
| `STK-12` | **Masaüstünde "Tüm Şubeler" modunda stok işlemi** — web'de STK-04 ile açık (depo açıkça seçilirse), masaüstünde `BranchGuard` tümünü engelliyor. TRF-01 analizinde bulundu ama **transfer'e özel değil, Stok ekranının TAMAMINI** ilgilendiriyor (her işlem türü oturum şubesine yazıyor) → kendi analiz + test turunu hak ediyor, TRF-01'e sıkıştırılmadı | ✅ **TAMAM** (2026-09-04, ADR-208 — koruma kaldırılmadı, **yeri değişti**: depo açıkça seçilir; şubesiz kayıt hâlâ imkânsız) |
| `STK-10a` | **"Stok Hareketleri" raporu** — katalog + Kaynak/Hedef + `Date`+`Location` + **gerçek XLSX doğrulaması** | ✅ **TAMAM** (41 senaryo · izole PG sorgu planı · Web/masaüstünde kod değişmedi) |
| `STK-10b-1` | **Hareket Türü filtresi** — 6/6 katman · seçenekler MovementTypeOptions'tan · fail-closed | ✅ **TAMAM** (28 senaryo · RPR-01 yeşil · izole PG) |
| `STK-10b-2` | **Serbest metin arama** — 6/6 katman · semantik mevcut ekrandan birebir | ✅ **TAMAM** (41 senaryo · RPR-01 yeşil · izole PG) |
| `STK-10b-3` | **Malzeme filtresi + autocomplete** — 6/6 katman · scope BÜYÜMEDİ · mevcut indeks yetti | ✅ **TAMAM** (32 senaryo · RPR-01 yeşil · izole PG) |
| `STK-10b-4` | **2 hareket ekranı + B-1** — filtreler tek SQL kaynağına bağlandı, lokasyon süzmesi sunucuya indi (ADR-105) | ✅ **TAMAM** (36 senaryo · ekran=rapor=XLSX · izole PG) → **STK-10 BİTTİ** |
| `STK-B2` | Arama `stock_documents.note`'u da kapsasın mı? | ✅ **KARAR VERİLDİ: HAYIR** (2026-08-29, ADR-179) — arama kapsamı değişmedi, `FinalStabilizasyonTests.FIN8` kilitledi |
| `RPR-02` | Web rapor isteği oturumun ŞUBESİNİ taşımıyor (JWT'de yok) | ✅ **KAPANDI** (2026-08-29, ADR-179) — fiilen RPR-07 (2026-08-25) ile çözülmüştü: web operasyon kipinde `operatingBranchId` gönderiyor, sunucu `BranchAccess.Require` ile doğruluyor. Kod gerekmedi; eskimiş kayıttı |
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
| `MUH-01` | Para hareketi doğuran her kayda **cari + maliyet merkezi + belge** alanları | ✅ **TAMAM** (2026-09-04) — üç eksen ayrı ayrı ölçüldü: maliyet merkezinde eksik olan **kapsam**tı (ekipman bakımı), belgede **üç tablo** (`Migration089`), caride yalnız **bakımlar** (`Migration090`). Yakıt/satın almaya ve stok belgesine kolon **eklenmedi** — karşı taraf oralarda zaten ulaşılabilir; eklemek ikinci gerçeklik üretirdi. Ayrıntı: [FAZ_D_MUH_01_ON_MUHASEBE_ALANLARI.md](FAZ_D_MUH_01_ON_MUHASEBE_ALANLARI.md) |

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

## FAZ K — Uçtan uca doğrulama ve onarım (`UUD-01`) — **SON AŞAMA**

Tüm ekran ve özellikler bittikten sonra çalışan **33 bölümlük doğrulama ve onarım protokolü**.
Bir "test raporu yaz" görevi değildir: uygulama gerçek bir Alpnex kullanıcısı gibi kullanılır,
görünen ve görünmeyen tüm katmanlar doğrulanır, hatalar **kök nedeniyle düzeltilir** ve düzeltmeler
tekrar test edilir.

Kapsam başlıkları: proje/pattern analizi · test matrisi · gerçek kullanıcı simülasyonu ·
her alan · her buton · **10.000+ kayıt (zorunlu)** · DB doğrulaması · **tenant/company güvenliği
(zorunlu)** · yetki · API · concurrency/double submit · network failure · masaüstü · web ·
offline/sync · UI/UX/erişilebilirlik · setup/installer · log/audit · performans ·
hata önceliklendirme (P0–P3) · regresyon · test verisi temizliği · testlerin kendisini doğrulama ·
build ve tam test · MCP politikası · kabul kriterleri · tam kullanıcı senaryosu · son rapor.

**Protokol boyunca production'a dokunulmaz** (SELECT dâhil). Testler local/test ortamında yapılır.

### ✅ Sonuç (2026-09-05, ADR-219)

**Dört sessiz kusur bulundu ve düzeltildi** — hiçbiri hata vermiyordu, hepsi kullanıcıya yanlış ama
inandırıcı bir sonuç gösteriyordu:

1. Belge/fatura numarası alanlarında **uzunluk sınırı yoktu** → ortak `BelgeNo` (100 karakter).
2. Personel dışa aktarımı **200 satırda kesiliyordu** (bu turda eklenen kusur) → `ListAllForExport`.
3. Web listeleri **"yüklenemedi" ile "kayıt yok"u karıştırıyordu** → hata + "Tekrar dene" (4 ekran).
   Masaüstünde bu kusur yoktu (ölçüldü), dokunulmadı.
4. İki farklı sayfa tavanı (imleçli 200 / ızgara 500) → teste yazıldı.

**Ölçüm:** 25.000 kayıtta ilk sayfa 58 ms, son sayfa 78 ms — sayfalama ve indeks çalışıyor.
Arama doğrusal büyüyor (942 ms): "içerir" araması indeks kullanamaz; ölçüldü, kayda geçti,
ölçülmemiş optimizasyon eklenmedi.

**37 yeni test.** Üretim veritabanına hiç dokunulmadı.
**Yapılmayan:** tarayıcıda oturum açılarak tam kullanıcı yürüyüşü — giriş formuna parola
yazılmadığı için; kimlik doğrulamalı ekranlar gerçek HTTP hattı (`ApiTestHost`) ile sınandı.

Tam metin ve rapor: [FAZ_K_UCTAN_UCA_DOGRULAMA.md](FAZ_K_UCTAN_UCA_DOGRULAMA.md)

> **Bitiş sırası:** `FAZ K` biter → **tek yayın** → bilgisayar uykuya alınır.

---

## Devredilen teknik borçlar (fazlanmamış, kapanmadı)

`G6-10…G6-19` · `G6-21/22/24` · `H-6` (masaüstü sunucu adresi 7 dosyada tekrar) · `H-7` · `GRP3-JOIN` ·
`brands/vehicle_models JOIN` · `500→400` · `WEB-01b` · `GUV-01b` · `TLP-B5` · `MUA-01/02` · `G2-08` ·
`TMZ-01/03` · Personel 200 kayıt tavanı · `SNK-05` ✅ karar verildi (ADR-179: online ilk-kazanır / offline LWW, FIN9-FIN10 kilitli) · `WEB-02` · `YET-01` ✅ kapandı (YET-05, 2026-08-26: `btn-reverse` kapısı web Stok+Yakıt ekranlarında uygulanıyor)

Ayrıntı: [`TASK_BACKLOG.md`](TASK_BACKLOG.md).

---

## 2026-09-07 — A/B GRUBU ÖLÇÜM DÜZELTMESİ

Tam analiz yapıldı (ayrıntı: [A_B_GRUBU_ANALIZ.md](A_B_GRUBU_ANALIZ.md)). İki madde yanlış
kayıtlıydı:

- **B4 — Araç zimmeti geçmişi: ZATEN YAPILMIŞ.** `/api/assignments/history` ucu var; web
  `Assignments.razor` satıra tıklayınca geçmişi açıyor, masaüstü `AssignmentsViewModel` seçim
  değişince yüklüyor. Yapılacak iş yok.
- **A2 — Cari yaşlandırma: migration GEREKTİRMEZ.** `invoices.due_date` zaten var ve ekranda
  gösteriliyor; eksik olan yalnız raporun kendisi.

Ayrıca **B6 — Puantaj** kapsam dışıdır (`Migration079` kaydındaki `PK-F4 puantaj YOK` kararı).

**Önerilen sıra (risk/değer):** A1 (liste toplamları) → A2 (yaşlandırma) → B3 (trafik cezası/HGS) →
A4 (favoriler) → A3 (toplu işlem) → B2 (e-posta) → B1 (çek/senet) → B5 (lastik).
