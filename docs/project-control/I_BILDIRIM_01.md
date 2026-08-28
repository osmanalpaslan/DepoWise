# BLD-01 — Bildirim Merkezi · KALICI KONTROL BELGESİ

> Oluşturma: **2026-08-28** · Karar: **ADR-172** · Roadmap: FAZ 4 / SIRA 9 (MASTER_ROADMAP §1 — "Uyarılar genişletmesi")
> Analiz: [I_BILDIRIM_00_ANALIZ.md](I_BILDIRIM_00_ANALIZ.md) — PK-I1..I4 kullanıcı tarafından KESİNLEŞTİRİLDİ ve AYNEN uygulandı.

## 1. Uygulanan ürün kararları

| Karar | Uygulama |
|---|---|
| PK-I1 | v1 yeni kaynaklar: **① Evrak geçerlilik** (≤30 gün "yaklaşıyor" / geçmiş "süresi doldu"+KRİTİK — muayene eşiği `InspectionService.ApproachingDays` ile AYNI sabit) · **② Geciken iş emri** (plan bitişi geçmiş VE terminal değil; KRİTİK) · **③ Bekleyen talep** (status='pending', kalem bazlı; KPI sayacı değişmedi). ④ Satın alma ve ⑤ takvim kaydı ALINMADI. |
| PK-I2 | Tam UI paketi: **üst bar çan 🔔 + okunmamış sayacı** (web MainLayout + masaüstü MainWindow/Shell) · Uyarılar ekranında yeni 3 kategori + **"Tümü"** görünümü + okundu/okunmadı ayrımı (soluk satır + rozet) + **"Okundu işaretle"** + **"Tümünü Okundu Yap"** · "kaynağa git" korunur. İlk açılış "kategori seçin" davranışı (2026-07-26 isteği) KORUNDU. |
| PK-I3 | Mevcut **`alerts`** modülüyle devam — yeni yetki modülü YOK. Asıl güvence çift kapı: her kaynak kendi modül yetkisiyle sarılı. |
| PK-I4 | Okundu işaretleri **CİHAZ-YEREL** (mevcut `alert_reads` aynen; senkronlanmaz). **MIGRATION YOK — şema 80'de kaldı; `alert_reads`'e dokunulmadı.** |

## 2. Mimari — TÜRETİLMİŞ bildirim (fiziksel kayıt YOK)

Mevcut uyarı mimarisi genişletildi: `DashboardService.GetSummary` mevcut 4 kaynağa (bakım/muayene/
stok/yakıt — davranışları DEĞİŞMEDİ) 3 yeni blok ekledi; `AlertKind`'a değerler SONA eklendi
(Document/WorkOrder/Request — mevcut serileştirme değişmez). Bildirim her çağrıda kaynaktan hesaplanır →
**kopya bildirim yapısal olarak imkânsız** (BLD8), kaynak düzelince bildirim kendiliğinden düşer,
kaynak kayıtlar bit-bit değişmez (BLD9). Paralel NotificationService KURULMADI.

- **Okundu:** mevcut imzalı `alert_reads` modeli — `UNIQUE(user_id,key)` upsert (kopya satır imkânsız);
  hal kötüleşince (imza değişir) okundu OTOMATİK düşer (BLD7). Yeni: `MarkAllAlertsRead` + `UnreadAlertCount`
  + `ApplyReads` (masaüstünün uzaktan aldığı bildirimlere yerel işaret uygulanır — BLD11).
- **API:** `/api/dashboard` alerts çıktısına eklemeli `entityId` · yeni `/api/alerts/read-all` +
  `/api/alerts/count`. Mevcut `/api/alerts/read` aynen.
- **Gezinme:** NavigateKey — evrak `documents` · iş emri `work_orders` · talep `requests:approve`.
  Web Open() dönüşümüne `_`→`-` eklendi (work-orders rotası; mevcut anahtarlarda alt çizgi yok →
  eski davranış birebir).

## 3. Yetki / kapsam / tenant

Çift kapı: evrak `DocumentService.List` (files + bağlı modül + şube/proje kapsamı İÇİNDE) · iş emri
`WorkOrderService.List` (BranchAccess İÇİNDE) · talep `requests` View + şube kapsam filtresi (şubesiz
talep gizlenmez). Kaynak yetkisi olmayan oturuma o kategori SIZMAZ (BLD4); kapsam (BLD5); tenant (BLD6).
Mevcut 4 kaynağın firma-geneli davranışına DOKUNULMADI.

## 4. Offline / senkron

- **Masaüstü:** iş emri + talep + bakım + muayene + stok + yakıt bildirimleri YERELDEN → çevrimdışı tam.
  **Evrak** sunucu-otoriteli → yalnız ÇEVRİMİÇİYKEN `OrgServerClient.ListDocumentAlertsAsync` ile alınır
  (sunucu files yetkisini kendi süzer); çevrimdışıyken ekranda "çevrimiçi gerekli" notu (BLD10 temsilci).
  Tek akış noktası: `AlertFeed` (Shell çanı + Uyarılar ekranı aynı kaynağı okur).
- **Senkron:** bildirim senkronu YOK (tablo yok); kaynak veri taşınınca her cihaz kendi üretir.
  `BusinessSyncService`'e DOKUNULMADI. Okundu işaretleri cihaz-yerel (PK-I4).
- **Sayaç performansı:** web'de oturum başına bir kez + Uyarılar ekranından dönünce; masaüstünde girişte
  bir kez + Uyarılar ekranı etkileşimlerinde. Her sayfa geçişinde HESAPLANMAZ. Cache/queue kurulmadı.

## 5. Testler

`BildirimTests` **12/12**: evrak eşikleri (BLD1) · geciken iş emri + terminal muafiyeti (BLD2) ·
bekleyen talep + KPI korunumu (BLD3) · **yan kapı yok (BLD4)** · **kapsam (BLD5)** · **tenant (BLD6)** ·
**okundu-imza döngüsü (BLD7)** · **tümünü-okundu + idempotency/kopyasızlık (BLD8)** ·
**kaynaklar bit-bit (BLD9)** · offline evrak sessiz-boş (BLD10) · ApplyReads cihaz-yerel (BLD11) ·
boş kurulumda yeni kategori yok (BLD12).
Hedefli regresyon (uyarı-okundu / evrak / iş emri / takvim / talep / rapor / parite):
**648 geçti / 0 başarısız / 4 atlanan** (atlananlar = PostgreSQL gerektiren rapor testleri — yerel
ortamda PG yok; bu turda test atlanır hale GETİRİLMEDİ). Parite 55/62 (ekran eklenmedi — alerts mevcuttu).
Üç Release build **0 hata**.

## 6. Canlı veri güvenliği

Canlıya yazma YOK · mevcut kayıt değişimi YOK (BLD9 bit-bit; türetilmiş katman salt-okunur; okundu
yalnız kullanıcı-yerel `alert_reads` upsert'üdür) · fiziksel silme YOK · **migration YOK (şema 80)** ·
production deploy YOK.

## 7. Bilinen sınırlar / elle test

E-posta/SMS/push · ertele/kapat · kullanıcı tercihleri · üçlü öncelik · zimmet (vade alanı yok) ·
olay-bazlı durum-değişim bildirimi · eşik ayarları (30 gün sabit) bilinçli KAPSAM DIŞI (analiz §13).
Okundu işaretleri cihazlar arası TAŞINMAZ (PK-I4 — istenirse ileride eklemeli ALTER+sync ile).
İki platformda gözle doğrulama size kaldı (çan/rozet yerleşimi + Uyarılar ekranı yeni butonları).
Masaüstünde evrak bildirimi yalnız çevrimiçiyken görünür.

## 8. Canlıya alınma durumu

✅ **YAYINLANDI — 2026-08-28 toplu yayın** (kullanıcı onayı; Migration073..081 canlıda birlikte uygulandı).
API **v174** · Web **v199** · Masaüstü **1.0.160** (SHA-256 EA688F2F…59CAE2). Kanıtlar:
[TOPLU_YAYIN_2026-08-28.md](TOPLU_YAYIN_2026-08-28.md) — deploy öncesi/sonrası canlı salt-okunur sayım/karma
karşılaştırması: mevcut TÜM tabloların satır içerikleri BİT-BİT AYNI; yeni tablolar BOŞ; şema 72→81.
Yeni yetkiler hiçbir role otomatik AÇILMADI — rollere kontrollü açılacak durumda.
## 9. Sonraki roadmap işi

**J — Duyuru** (FAZ 4/SIRA 10). 7b Bakım-Ekipman genişletmesi hâlâ serbest sırada.
