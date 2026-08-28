# TKV-01 — Takvim · KALICI KONTROL BELGESİ

> Oluşturma: **2026-08-28** · Karar: **ADR-171** · Roadmap: FAZ 3 / SIRA 8 (MASTER_ROADMAP §1)
> Analiz: [H_TAKVIM_00_ANALIZ.md](H_TAKVIM_00_ANALIZ.md) — PK-H1..H5 kullanıcı tarafından KESİNLEŞTİRİLDİ ve AYNEN uygulandı.

## 1. Uygulanan ürün kararları

| Karar | Uygulama |
|---|---|
| PK-H1 | HİBRİT — türetilmiş katman (salt-okunur SELECT, kopya kayıt YOK) + el ile plan kayıtları (calendar_events CRUD). |
| PK-H2 | Beş türetilmiş kaynak: iş emri planları · muayene/sigorta `next_date` · evrak `valid_until` · proje `start/end` · gün-bazlı bakım hedefi (son bakım + aralık günü; km/saat bazlı tanımlar TARİHSİZ → takvime giremez). Kaynak servislerin KENDİ list metotları çağrılır → yetki/BranchAccess/tenant otomatik aynen (TKV4–TKV8). |
| PK-H3 | Kaynak planlama + çakışma denetimi YOK; el ile kayıtta tek opsiyonel sorumlu personel. |
| PK-H4 | Gün bazlı, çok günlü aralık; SAAT YOK. Tarih PLAN tarihidir — ADR-162 geri-tarih kapısına GİRMEZ, `created_at` audit'te korunur. ms kolonu saati ileride eklemeli taşır (yeniden yazım yok). |
| PK-H5 | İş emri bağı YALNIZ gezinme: `CalendarService`'te iş emri durumu/stok/iş mantığı çağıran YOL YOK; bağ create/update/delete döngüsünden sonra work_orders satırı **bit-bit aynı** (TKV3). |

## 2. Veri modeli — Migration080 (şema v80, yalnız CREATE)

`calendar_events(id · company_id · branch_id? · title · note? · start_date · end_date? ·
responsible_personnel_id? · work_order_id? · created_by · created_at/updated_at/version/is_deleted)` +
2 indeks. Mevcut tablolara **ALTER dahi yok** — TKV14 (bit-bit) + TKV15 (statik) kanıtlı.
⚠️ Canlıya UYGULANMADI. Rollback: tek DROP + schema_migrations satırı.
Türetilmiş öğeler bu tabloya YAZILMAZ (her açılışta kaynağından okunur → hep güncel, çift gerçeklik yok).

## 3. Mimari / entegrasyonlar

- **Yetki:** yeni `calendar` modülü (kapalı gelir — rollere AÇILMALI). **ÇİFT KAPI:** türetilmiş öğe
  yalnız kullanıcının O KAYNAĞIN modülünde View yetkisi varsa görünür — bakım yetkisi olmayan takvimden
  bakım tarihlerini OKUYAMAZ (TKV9 yan kapı testi). BranchAccess: el ile kayıtta okuma filtresi + yazma
  Require; türetilmişte kaynak servisin kendi kapsamı (TKV10). Ekran logu `calendar_event`; audit
  create/update/delete; türetilmiş GÖRÜNTÜLEME audit doğurmaz (salt okuma).
- **Senkron:** yalnız `calendar_events` — `Tables`'ta work_orders SONRASI (FK: work_order_id); push
  kapısı `calendar`. Uçtan uca taşıma + tekrar-kopyasızlık + silmenin taşınması kanıtlı (TKV13).
- **Masaüstü offline:** el ile kayıt + iş emri + muayene + bakım YEREL → çevrimdışı tam işlevli.
  **Evrak + Proje sunucu-otoriteli** → çevrimiçiyse `OrgServerClient.ListCalendarAsync` ile eklenir,
  çevrimdışıysa "çevrimiçi gerekli" notu (Projeler ekranı emsali; veri uydurulmaz). CalendarService
  masaüstünde `documents=null` ile kurulur (TKV6'da kanıtlı: kaynak sessizce boş, hata yok).
- **Silme:** soft delete + Çöp Kutusu (`calendar_events`→title) — fiziksel silme YOK (TKV2).
- **Ekranlar:** tek ekran iki platformda — AY IZGARASI (Pzt başlangıçlı; ★ = el ile kayıt, türetilmişler
  ayrık rozet) + AJANDA listesi (gün seçimi süzer) + kaynak/şantiye filtresi + arama + Excel (liste
  kuralı 2). Web `Calendar.razor` (/calendar) + masaüstü `CalendarView`. "Kayda Git" kaynak ekranına
  götürür. Parite 55/62.
- **Bildirim YOK** (I — Bildirim Merkezi'nin konusu) · **tekrarlayan iş YOK** (ileride Takvim üzerinden,
  eklemeli — PK-F7; bugünkü şema yeniden yazım gerektirmez).

## 4. Testler

`TakvimTests` **16/16**: el ile CRUD+pencere+kilit (TKV1) · **soft delete+Çöp Kutusu (TKV2)** ·
**İE bağı bit-bit (TKV3)** · türetilmiş 5 kaynak (TKV4–8, tarihsiz kayıtların girmediği dahil) ·
**yan kapı yok (TKV9)** · **kapsam (TKV10)** · **tenant (TKV11)** · senkron sıra/kapı (TKV12) ·
**uçtan uca idempotent+silme (TKV13)** · **migration bit-bit (TKV14)** · statik CREATE-only (TKV15) ·
Excel modeli (TKV16).
Hedefli regresyon (iş emri/evrak/proje/ekipman/zimmet/satın alma/maliyet/bakım/muayene/senkron/parite/
çöp kutusu): **451 geçti / 0 başarısız / 2 atlanan** (koşullu atlamalar — bu turda eklenmedi).
Üç Release build **0 hata**. Parite 55 ekran / 62 web bağlantısı.

## 5. Canlı veri güvenliği

Canlıya yazma YOK · mevcut kayıt değişimi YOK (TKV14 bit-bit; TKV3 iş emri bit-bit; türetilmiş katman
yalnız SELECT) · fiziksel silme YOK · production migration/deploy YOK. Migration080 yalnız CREATE.

## 6. Bilinen sınırlar / elle test

Saat bilgisi · kaynak planlama/çakışma uyarısı · tekrarlayan iş · bildirim/hatırlatma · takvim kaydına
dosya ekleme bilinçli KAPSAM DIŞI (analiz + PK kararları). İki platformda gözle doğrulama size kaldı
(Avalonia otomasyonu yok; ay ızgarası bu turun tek yeni görsel bileşeni — işlevi kodda tam, görünümü
elle kontrol edilmeli). **"Takvim" yetkisi kapalı gelir** — rollere açılmalı; türetilmiş kaynaklar için
kullanıcıda ilgili modül yetkisi de olmalı. Masaüstünde Evrak+Proje öğeleri yalnız çevrimiçiyken görünür.

## 7. Canlıya alınma durumu

⛔ **Yayınlanmadı.** Yayın bekleyenler: **Migration073..080** (C+A+E+B+D+P+F+H — 8 modül birlikte).

## 8. Sonraki roadmap işi

**FAZ 4 / SIRA 9 — I: Bildirim Merkezi** (uyarılar genişletmesi; takvim/hatırlatma bildirimleri doğal
olarak oraya bağlanır). Ayrıca **7b Bakım-Ekipman genişletmesi** hâlâ bekliyor (sırası serbest).
