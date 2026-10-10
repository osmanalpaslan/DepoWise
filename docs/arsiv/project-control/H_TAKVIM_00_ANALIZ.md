# H — Takvim · ANALİZ RAPORU (kod yazılmadı)

> Tarih: **2026-08-28** · Roadmap: FAZ 3 / SIRA 8 (MASTER_ROADMAP §1 — "Yeni ana menü, tek ekran")
> Bu belge SALT ANALİZDİR: kod / migration / deploy / canlı veri değişikliği YOKTUR.
> Uygulama, kullanıcının PK-H kararlarından SONRA ayrı turda yapılır.

---

## 1. Mevcut altyapı envanteri (kod taraması, 2026-08-28)

**Takvim/planlama altyapısı YOK** — "takvim/calendar/reminder" kod tabanında yalnız alakasız yerlerde
(bootstrap css, doğrulama metinleri) geçiyor. Sıfırdan ama MEVCUT desenlerle kurulacak.

**Uyarı sistemi VAR ama tarihsel değil, eşik bazlı:** `MaintenanceService.GetAlerts` +
`InspectionService.GetAlerts` + `AlertRules` (%85 yaklaşıyor / %95 kritik / %100 gecikti) türetilmiş
(veritabanında uyarı satırı YOK, her seferinde hesaplanır); `alert_reads` (Migration031) yalnız
"okundu" işareti tutar. Dashboard + Uyarılar ekranı bunları gösterir. **Takvim bunun "tarih eksenli"
kardeşidir; I — Bildirim Merkezi (sıra 9) ise bu uyarı sisteminin genişletmesi olacaktır.**

**Tarih taşıyan mevcut kaynaklar** (hepsi BIGINT Unix ms — mevcut standart):

| Kaynak | Alan | Anlamı | Masaüstü çevrimdışı? |
|---|---|---|---|
| `work_orders` | `planned_start/planned_end` (+`actual_*`) | PLAN (ADR-170) | ✅ (BusinessSync) |
| `vehicle_inspections` | `next_date` | muayene/sigorta/kasko/kalibrasyon sonraki tarih | ✅ |
| bakım tanımları (gün bazlı) | son bakım + `interval` (Day) | hedef tarih TÜRETİLİR; km/saat bazlılar TARİHSİZDİR → takvime giremez | ✅ |
| `file_records` | `valid_until` (Migration074) | evrak geçerlilik sonu | ❌ sunucu-otoriteli |
| `projects` | `start_date/end_date` (Migration073) | proje plan tarihleri | ❌ sunucu-otoriteli |
| `daily_activities` | `activity_date` | GEÇMİŞ kaydı (plan değil) | ✅ |
| `purchase_orders` | `order_date` | sipariş iş günü (beklenen teslim alanı YOK) | ✅ |
| `assignment_movements` | `doc_date` | zimmet defteri (geçmiş) | ✅ |

## 2. Önerilen model: İKİ KATMAN

- **Katman 1 — TÜRETİLMİŞ (salt-okunur):** mevcut kayıtlar ay/hafta penceresine göre SELECT ile
  toplanır; **hiçbir bağ kaydı/tablo/ALTER gerekmez** (kopya gerçeklik yasağı — üçlü gerçeklik
  yaratılmaz). Öğeye tıklanınca ilgili ekrana gidilir. İş emri ilişkisi (madde 5) böyle kurulur:
  `work_orders.planned_*` doğrudan okunur.
- **Katman 2 — EL İLE KAYIT:** yeni `calendar_events` tablosu — toplantı/teslimat/not gibi plan
  kayıtları; opsiyonel iş emri bağı (PK-H5). PK-H1 "yalnız türetilmiş" seçilirse bu katman ve
  **migration tamamen atlanır**.

## 3–4. Yeni tablo / ALTER gereksinimi

- Hibrit seçilirse: **Migration080_CalendarEvents — yalnız CREATE, 1 tablo.** Mevcut tablolara
  **ALTER dahi YOK** (7 modül deseninin devamı; bit-bit + statik kanıt testleri aynen).
- Yalnız-türetilmiş seçilirse: **migration HİÇ GEREKMEZ** (şema 79'da kalır).

Taslak şema (hibritte): `calendar_events(id, company_id, branch_id NULL, title, event_type,
start_date NOT NULL, end_date NULL, responsible_personnel_id NULL, entity_type NULL, entity_id NULL,
note, created_by, created_at, updated_at, version, is_deleted)` + `(company_id, start_date, is_deleted)`
indeksi. FK'sız polymorphic bağ (file_records/cost_center_links emsali).

## 5 + 19. İş Emri ilişkisi ve durum eşleme

- Türetilmiş gösterim OTOMATİK (plan tarihi olan her iş emri takvimde; durum rengi/rozeti gösterilir).
- **Otomatik durum eşleme ÖNERİLMEZ ve yapılmamalı:** iş emri durumu YALNIZ `WorkOrderService`
  geçiş matrisinden değişir (PK-F1/F2 kilidi). Takvim iş emri durumunu ASLA değiştirmez — değiştirse
  yetki/geçiş matrisine YAN KAPI olurdu. Ters yön de gereksiz: iş emri tamamlanınca takvimde zaten
  "tamamlandı" renginde görünür (türetilmiş olduğu için kendiliğinden günceldir).

## 6 + 21. Tekrarlayan işler / gelecek temeli (yeniden yazımsız büyüme kanıtı)

PK-F7 gereği tekrarlayan iş emri v1'de YOK; ileride Takvim üzerinden gelecek. Temel şöyle hazırlanır
(v1'de kod YOK, yalnız şema uygunluğu):
- `calendar_events`'e ileride **ADD COLUMN** ile tekrar alanları (kural/aralık/bitiş/seri kimliği)
  eklenir — eklemeli, mevcut kayıtlar bozulmaz.
- Üretici mantık child iş emirlerini **MEVCUT** `WorkOrderService.Create` ile açar → iş emri şeması
  ve akışı DEĞİŞMEZ.
- Saat bilgisi, puantaj, kaynak planlama: ms kolonu saati zaten taşıyabilir; katılımcı tablosu
  ileride CREATE ile gelir. **Hiçbir gelecek senaryosu bugünkü şemayı yeniden yazdırmaz.**

## 7 + 18. Kaynak planlama ve çakışma

- v1 önerisi: personel/araç/ekipman PLANLAMASI YOK; el ile kayıtta tek opsiyonel "sorumlu personel".
  İş emri atamaları zaten `work_order_assignments`'ta — takvim türetilmiş katmanda yalnız okur.
- Çakışma denetimi (aynı personel aynı gün iki planda): kaynak planlaması olmadan anlamsız; mevcut
  sistemde iş emri atamaları da çakışma denetlemiyor — v1'de engel koymak davranış değişikliği olur.
  İleride "uyarı (engel değil)" olarak eklemeli gelir.

## 8 + 9. Tarih/saat ve TRH-01 (ADR-162) uyumu

- v1 **GÜN BAZLI** (tüm-gün), çok günlü aralık (start/end) destekli. Mevcut sistemde HİÇ saat girişi
  yok (tüm ekranlar gün bazlı DatePicker); saat eklemek iki platformda yeni kontrol + format işidir
  ve ms kolonu saati sonradan taşıyabilir → saat ertelenebilir (PK-H4).
- Takvim tarihi **PLAN tarihidir** → Projeler + İş Emri plan tarihleri EMSALİ: `DateEntryPolicy` /
  `btn-backdate` kapısına GİRMEZ (plan geçmişe/geleceğe serbest — bilinçli, ADR-170'te gerekçeli).
  `created_at` kayıt anı audit'te aynen korunur. İş günü/kayıt anı ayrımı bozulmaz.

## 10. Şantiye/Saha/Proje gösterimi

Her türetilmiş öğe kaynağının `branch_id`'sini taşır → takvimde şube/şantiye filtresi. Proje,
şantiyeden TÜRETİLİR (PK-F5 ile tutarlı — ayrı proje bağı tutulmaz). `calendar_events.branch_id`
opsiyonel; BranchAccess kapsamı buradan (şubesiz kayıt herkese görünür — mevcut kural).

## 11. Yetki

- Yeni modül **`calendar`** — KAPALI gelir (deny-by-default), rollere açılmalı.
- **ÇİFT KAPI (LOG-01/EVR deseni):** türetilmiş öğe yalnız kullanıcının O KAYNAĞIN modülünde
  Read yetkisi varsa görünür — bakım yetkisi olmayan, takvimden bakım tarihlerini OKUYAMAZ
  (yan kapı testi zorunlu). BranchAccess okuma filtresi her kaynakta uygulanır.
- El ile kayıt: calendar Create/Edit/Delete; silme = soft delete + Çöp Kutusu.

## 12. Offline (masaüstü)

İş emri + muayene/sigorta + bakım YEREL → çevrimdışı görünür. **Evrak geçerlilik + proje tarihleri
sunucu-otoriteli** → çevrimdışıyken bu iki kaynak "çevrimiçi gerekli" notuyla boş kalır (Projeler
ekranı emsali; veri uydurulmaz). `calendar_events` yerel tablo + BusinessSync → çevrimdışı CRUD tam.

## 13. Web/Desktop paritesi

Tek ekran iki platformda. Ay ızgarası web'de ucuz (CSS grid); **Avalonia'da özel panel = turun en
büyük maliyet kalemi.** İki görünüm önerisi: Ay ızgarası + Ajanda listesi (tarihe göre gruplu) —
liste Excel'e aktarılır. Piksel eşitliği zorunlu değil (CLAUDE.md §4). Parite testleri (S13/S14/S14b)
güncellenir: 54→55 / 61→62.

## 14. Senkron

Yalnız `calendar_events` için: `BusinessSyncService.Tables` sonuna 1 tablo + `TableModule` →
"calendar". FK bağımlılığı yok (bağ FK'sız). Türetilmiş katman senkron GEREKTİRMEZ — kaynaklar
zaten kendi yollarıyla taşınıyor. Uçtan uca + tekrar-kopyasızlık testi standart.

## 15. Evrak bağı

Takvim kaydına dosya ekleme v1'de GEREKSİZ (hafif kayıt). Evrak zaten `valid_until` ile takvimde
görünür. İleride gerekirse `DocumentService.Entities`'e "calendar_event" 1 satırla eklenir.

## 16. Audit + ekran logu

Standart: `ScreenAuditMap["calendar"]={"calendar_event"}` · create/update/delete audit ·
web `LogModules` kaydı · Ekran Araçları (ADR-163) otomatik. Türetilmiş GÖRÜNTÜLEME audit doğurmaz
(salt okuma — mevcut rapor/dashboard davranışıyla aynı).

## 17. Excel

Ajanda LİSTE görünümü Excel'e aktarılır (liste kuralı 2; `dwDownload` / `SaveExcelAsync`).
Ay ızgarası aktarılmaz.

## 20. Bildirim/hatırlatma

Roadmap SIRA 9 = **I — Bildirim Merkezi** ("Uyarılar genişletmesi") zaten bu iş için ayrılmış →
v1 takvim yalnız GÖSTERİR, bildirim/hatırlatma üretmez. Ürün sorusu YOK (roadmap belirlemiş).

## 22. Mevcut davranışa riskler

- Türetilmiş katman SALT-OKUNUR SELECT → mevcut modüllere SIFIR yazma, davranış değişikliği yok.
- Ortak dosyalara dokunuş yalnız katalog/kablolama (AppModules/AppScreens/sync listesi/parite) —
  7 modülde kanıtlanmış, düşük riskli desen.
- Performans: sorgular ay penceresiyle sınırlı; mevcut indeksler büyük oranda yeter — ölçmeden
  indeks eklenmez (protokol §8). Canlı veri hacmi küçük (tek firma).
- Ana maliyet/risk: Avalonia ay ızgarası bileşeni (yeni görsel iş; işlevsel riski düşük).

---

## ÖZET — önerilen kapsam ve plan

- **Kapsam:** tek "Takvim" ekranı = türetilmiş katman (iş emri planları · muayene/sigorta ·
  evrak geçerlilik · proje tarihleri · gün-bazlı bakım hedefleri) + el ile plan kayıtları (hibritte).
- **Veri modeli:** en fazla 1 yeni tablo (`calendar_events`, yalnız CREATE); ALTER yok; türetilmişte hiç tablo yok.
- **Ekranlar:** web `/calendar` + masaüstü `CalendarView` — Ay ızgarası + Ajanda listesi + kaynak/şube filtreleri + Excel (liste).
- **Yetki:** yeni `calendar` modülü kapalı gelir; türetilmişte çift kapı (kaynak modül Read) + BranchAccess.
- **Senkron/Offline:** yalnız calendar_events senkronlanır; masaüstü çevrimdışı (evrak+proje kaynağı hariç) tam çalışır.
- **Test:** migration bit-bit+statik · türetilmiş doğruluk (kaynak başına) · yan kapı (kaynak yetkisi) ·
  BranchAccess · tenant · CRUD+çöp kutusu · senkron uçtan uca idempotent · parite · Excel modeli.
- **Fazlar:** H1 migration+kanıt → H2 CalendarService → H3 API → H4 web → H5 masaüstü →
  H6 katalog/senkron/parite → H7 testler+hedefli regresyon → H8 belge+commit (yayın YOK).
- **Sonraki roadmap:** 7b Bakım-Ekipman genişletmesi (sırası serbest) → I — Bildirim Merkezi.
  Yayın bekleyen: Migration073..079 (+H hibritse 080).

## PK-H SORULARI — kullanıcı kararı bekleniyor

Karar bekleyen 5 soru ana rapordadır (PK-H1 içerik modeli · PK-H2 türetilmiş kaynak seti ·
PK-H3 kaynak planlama/çakışma · PK-H4 saat bilgisi · PK-H5 el ile kayıtta iş emri bağı).
Kararlar gelmeden UYGULAMA BAŞLAMAZ.
