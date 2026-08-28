# PAN-01 — Dashboard · KALICI KONTROL BELGESİ

> Oluşturma: **2026-08-28** · Karar: **ADR-175** · Roadmap: FAZ 4 / SIRA 12 (MASTER_ROADMAP §1)
> Analiz: [L_DASHBOARD_00_ANALIZ.md](L_DASHBOARD_00_ANALIZ.md) — PK-L1..L4 kullanıcı tarafından KESİNLEŞTİRİLDİ ve AYNEN uygulandı.

## 1. Uygulanan ürün kararları

| Karar | Uygulama |
|---|---|
| PK-L1 | Yeni özet alanları: **Açık İş Emri** (geciken sayısı vurgulu; tıkla→İş Emirleri) · **Açık Sipariş** (tıkla→Satın Alma) · **Bugünün Takvimi** şeridi (bugünle kesişen öğeler; tıkla→Takvim) · **Aktif Duyurular** şeridi (önemliler kırmızı rozetli; tıkla→Duyurular). Ekipman/zimmet EKLENMEDİ. |
| PK-L2 | Uyarı kategori kartları **4→8, HEP görünür** (Evrak · İş Emri · Talep · Duyuru eklendi) — iki platformda; "kategori seçilmeden liste yok" (2026-07-26), tıkla-filtrele ve "okundu" davranışları AYNEN. Ana ekran, çan ve Uyarılar ekranı artık HİZALI. |
| PK-L3 / PK-L4 | Kişiselleştirme YOK · grafik kütüphanesi YOK. |

## 2. Mimari — mevcut GetSummary'nin EKLEMELİ genişletilmesi (paralel sistem YOK)

- `DashboardSummary`'ye SONA, default'lu, **NULLABLE** alanlar eklendi (`OpenWorkOrderCount`,
  `OverdueWorkOrderCount`, `OpenPurchaseOrderCount`, `TodayCalendar`, `ActiveAnnouncements`) —
  **null = kullanıcının o kaynağa yetkisi yok → kart/şerit HİÇ gösterilmez** (yan kapı yok, PAN3);
  eski imzayla kurulan özet aynen derlenir (eklemeli kanıtı PAN8'de assert'li).
- Veriler MEVCUT servislerden salt-okunur türetilir: iş emri sayıları geciken-uyarı bloğunun TEK
  listesinden (ikinci sorgu yok; BranchAccess içeride — PAN4) · sipariş `PurchaseOrderService.List(s,
  null, "open")` (teslim şubesi kapsamı içeride) · takvim `CalendarService.Items` bugün penceresi
  (masaüstünde documents=null → evrak öğesi şeritte de tutarlı atlanır) · duyuru şeridi bildirim
  kalemleriyle AYNI tek `AnnouncementService.List` çağrısından.
- `/api/dashboard`: summary'ye eklemeli sayı alanları + `todayCalendar[]` + `activeAnnouncements[]`
  (null=yetki yok) — eski istemciler bozulmaz.
- **MIGRATION YOK — şema 81'de kaldı**; grafik kütüphanesi/cache/paralel veri sistemi kurulmadı.

## 3. Ekranlar

- **Web Home:** 8 kategori kartı (4'lü iki sıra) + kategori kartlarının altında Açık İş Emri
  ("n gecikmiş" alt notu kırmızı) ve Açık Sipariş mini kartları + sağ kolonda Bugünün Takvimi
  (boşsa "Bugün için kayıt yok") ve Aktif Duyurular (boşsa gizli) şeritleri + kurulum kartı/senkron
  çakışmaları YERİNDE. `Open()` dönüşümüne `_`→`-` eklendi (bildirimlerle aynı; mevcut anahtarlar etkilenmez).
- **Masaüstü Genel Özet:** ÖZET kart şeridine koşullu 2 yeni kart (Açık İş Emri — gecikmişse
  etiketle+turuncu; Açık Sipariş) + 4 yeni kategori butonu + uyarıların altında iki şerit paneli +
  sürüm/güncelleme kartı YERİNDE.

## 4. Testler

`PanoTests` **9/9**: iş emri sayıları (terminal sayılmaz) (PAN1) · sipariş sayısı (iptal sayılmaz)
(PAN2) · **yetkisiz alan NULL — kart sızmaz (PAN3)** · **kapsam (PAN4)** · **tenant (PAN5)** ·
bugün penceresi (dün/yarın girmez, çok günlü kesişir) (PAN6) · duyuru şeridi + önem (PAN7) ·
**eski davranış korunumu + eklemeli imza kanıtı (PAN8)** · **salt-okunur bit-bit (PAN9)**.
Hedefli regresyon (pano/bildirim/duyuru/takvim/iş emri/satın alma/arama/rapor/yetki/menü/parite):
**848 geçti / 0 başarısız / 4 atlanan** (atlananlar PostgreSQL gerektiren testler — yerel ortamda PG yok).
Üç Release build **0 hata**. Parite sayıları DEĞİŞMEDİ (yeni ekran yok).

## 5. Canlı veri güvenliği

Canlıya yazma YOK · mevcut kayıt değişimi YOK (PAN9 bit-bit; tamamen salt-okunur türetme) ·
fiziksel silme YOK · **MIGRATION YOK (şema 81)** · deploy YOK.

## 6. Bilinen sınırlar / elle test

Kişiselleştirme · grafik · ekipman/zimmet özetleri bilinçli kapsam dışı (PK-L1/L3/L4).
Ana ekran her gün görülen ekran — iki platformda GÖZLE doğrulama size kaldı (8 kartın yerleşimi,
mini kartlar, şeritler; Avalonia otomasyonu yok). Masaüstünde takvim şeridi evrak öğelerini
göstermez (evrak sunucu-otoriteli — Takvim ekranındaki kuralla aynı).

## 7. Canlıya alınma durumu

⛔ **Yayınlanmadı.** Yayın bekleyenler DEĞİŞMEDİ: **Migration073..081**.

## 8. Sonraki roadmap işi

**FAZ 4 BİTTİ.** Sıradaki: **M — Excel Merkezi** (FAZ 5/SIRA 13). 7b Bakım-Ekipman genişletmesi
hâlâ serbest sırada. ⚠️ 10 modüllük yayın birikimi (073..081 + 3 migration'sız iş) — FAZ 5'e
geçmeden toplu yayın önerilir.
