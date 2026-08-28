# L — Dashboard · ANALİZ RAPORU (kod yazılmadı)

> Tarih: **2026-08-28** · Roadmap: FAZ 4 / SIRA 12 (MASTER_ROADMAP §1 — "Mevcut ekran dönüşümü")
> Bu belge SALT ANALİZDİR: kod / migration / deploy / canlı veri değişikliği YOKTUR.
> Yayın bekleyen Migration073..081 durumu değişmez; L için MIGRATION ÖNGÖRÜLMÜYOR.

---

## 1. Mevcut durum (kod taraması, 2026-08-28)

**Tek veri kaynağı `DashboardService.GetSummary`** (KPI sayıları + birleşik uyarılar — BLD/DYR ile
artık 8 uyarı kaynağı üretiyor; yetki/kapsam/tenant kapıları İÇERİDE). `/api/dashboard` bunu döndürür.

| Yüzey | Bugün gösterdiği |
|---|---|
| **Web Home** (Aurora v4) | 4 uyarı-kategori KPI/filtre kartı (**yalnız ESKİ 4 tür**: Malzeme·Bakım·Muayene·Yakıt) + "kategori seçilmeden liste yok" (2026-07-26 kuralı) + kritik uyarı listesi (okundu butonlu) + senkron çakışmaları + masaüstü kurulum kartı |
| **Masaüstü Genel Özet** | 5 sayı kartı (Araç·Malzeme·Düşük Stok·Bekleyen Talep·Personel; tıkla→ekran) + **yalnız ESKİ 4 kategorili** uyarı butonları + makine/şube bilgisi + sürüm/güncelleme kartı |

**Boşluk (L'nin doğal işi):** BLD-01/DYR-01'in 4 YENİ uyarı türü (Evrak · İş Emri · Talep · Duyuru)
ana ekran kartlarında YOK (yalnız çan + Uyarılar ekranında) → ana ekran ile bildirim sistemi ayrıştı.
Yeni modüllerin (iş emri, sipariş, takvim, duyuru, ekipman, zimmet) ana ekranda HİÇBİR özeti yok.
Roadmap'in "L neredeyse hepsine bağlı — erken yapılırsa yeniden yazım" uyarısının nedeni buydu;
artık tüm modüller hazır → L şimdi doğru zamanda.

## 2. Önerilen dönüşüm — TAMAMEN EKLEMELİ (mevcut davranış korunur)

1. **Uyarı kategori kartları 4→8** iki platformda (Evrak · İş Emri · Talep · Duyuru eklenir) —
   "kategori seçilmeden liste yok", "okundu", tıkla-filtrele davranışları AYNEN (PK-L2 yerleşim sorusu).
2. **Yeni özet kartları/şeritleri** (PK-L1) — hepsi `GetSummary`'nin MEVCUT desenleriyle, kaynak modül
   View yetkisi yoksa kart GÖRÜNMEZ (bildirim deseni):
   - **Açık İş Emri** sayısı (geciken sayısı vurgulu; tıkla→İş Emirleri),
   - **Açık Sipariş** sayısı (tıkla→Satın Alma),
   - **Bugünün Takvimi** şeridi (bugünün öğeleri — CalendarService.Items, salt-okunur; tıkla→Takvim),
   - **Aktif Duyurular** şeridi (önemliler vurgulu; tıkla→Duyurular).
3. Veri: `DashboardSummary`'ye EKLEMELİ alanlar (+`/api/dashboard` summary'ye eklemeli JSON alanları —
   eski istemciler bozulmaz); takvim/duyuru şeritleri mevcut servislerden okunur (kopya yok).
4. Masaüstü kurulum kartı (web) ve sürüm/güncelleme kartı (masaüstü) YERİNDE kalır; senkron
   çakışmaları bölümü DEĞİŞMEZ.
5. **Grafik kütüphanesi KURULMAZ** (PK-L4) · kişiselleştirme YOK (PK-L3) · **MIGRATION YOK** ·
   performans: mevcut GetSummary çağrısına birkaç hafif COUNT + bugünkü takvim penceresi eklenir
   (ekran başına tek yükleme — mevcut model).

## 3. Test planı

`DashboardTests` genişletmesi (~10): yeni sayıların doğruluğu (açık/geciken İE, açık sipariş) ·
kart-yetki görünürlüğü (kaynak View yoksa alan/şerit yok — yan kapı) · takvim şeridi bugünün öğeleri ·
duyuru şeridi pencere kuralı · kapsam/tenant · eski 5 KPI + eski 4 kategorinin DEĞİŞMEDİĞİ (regresyon:
mevcut dashboard/rapor testleri) · salt-okunurluk bit-bit · API alanlarının eklemeli olduğu.

## 4. Riskler / maliyet

**Büyüklük: ORTA** (iki platform ana ekran düzeni + testler; veri tarafı hafif). Ana ekran tek firma
canlı kullanımda her gün görülen ekran → görsel doğrulama önemli (Avalonia otomasyonu yok — elle).
Yeniden yazım riski YOK: kartlar eklemeli; ileride kart aç/kapa istenirse üstüne gelir.

---

## PK-L SORULARI — kullanıcı kararı bekleniyor

Karar bekleyen 4 soru ana rapordadır (PK-L1 yeni kart seti · PK-L2 kategori kartlarının yerleşimi ·
PK-L3 kişiselleştirme · PK-L4 grafik). Kararlar gelmeden UYGULAMA BAŞLAMAZ.
