# J — Duyuru · ANALİZ RAPORU (kod yazılmadı)

> Tarih: **2026-08-28** · Roadmap: FAZ 4 / SIRA 10 (MASTER_ROADMAP §1 — "Yeni ana menü")
> Bu belge SALT ANALİZDİR: kod / migration / deploy / canlı veri değişikliği YOKTUR.
> Uygulama, kullanıcının PK-J kararlarından SONRA ayrı turda yapılır.

---

## 1. Mevcut altyapı (kod taraması, 2026-08-28) — yeniden kullanılacak parçalar

**Duyuru/mesajlaşma altyapısı YOK** ("duyuru/announcement/broadcast" kod tabanında hiç geçmiyor).
Sıfırdan ama TAMAMI bu oturumda kanıtlanmış desenlerle kurulur — paralel hiçbir sistem gerekmez:

| Parça | Emsal | Duyuruda kullanımı |
|---|---|---|
| Firma-içi içerik tablosu (soft delete + version + sync) | `calendar_events` (TKV-01) | `announcements` tablosu birebir aynı iskelet |
| Kullanıcı-bazlı OKUNDU işareti (imzalı) | `alert_reads` (#18) — anahtar SERBEST METİN | **Migration'sız okundu**: anahtar `Announcement\|id\|başlık`, imza=güncelleme damgası → duyuru DÜZENLENİNCE herkes için yeniden okunmamış olur |
| Çan + sayaç + Uyarılar ekranı + "tümünü okundu" | BLD-01 | Aktif duyuru, bildirime KALEM olarak düşer (`AlertKind`'a SONA eklenen `Announcement` değeri) — çan/sayaç/okundu BEDAVAYA gelir |
| Deny-by-default modül + katalog kablolaması | EMR/TKV | Yeni `announcements` modülü + AppScreens + parite |
| Senkron (FK sıralı ekleme, LWW) | `calendar_events` | 1 tablo eklenir → masaüstü ÇEVRİMDIŞI okur/yazar |
| Çöp Kutusu / audit / ekran logu | standart | `["announcements"]="title"` + `ScreenAuditMap` |

## 2. Önerilen veri modeli (Migration081 — yalnız CREATE, 1 tablo; ALTER YOK)

`announcements(id, company_id, branch_id NULL, title NOT NULL, body NULL, importance 'normal'|'important',
publish_start NULL, publish_end NULL, created_by, created_at, updated_at, version, is_deleted)` + indeks
`(company_id, is_deleted)`. **Mevcut hiçbir tabloya ALTER gerekmez** (okundu için `alert_reads` zaten
yeterli — bit-bit + statik kanıt testleri standart). Yayın tarihleri PLAN anlamındadır (ADR-162:
geri-tarih kapısına girmez; `created_at` audit'te korunur).

## 3. Ekranlar ve akış

- **Tek "Duyurular" ekranı** iki platformda (EMR/TKV tek-ekran deseni): üstte form (yetkili için:
  başlık · metin · önem · hedef şube · yayın başlangıç/bitiş), altında liste (aktif + gelecek + biten;
  yetkisiz kullanıcı yalnız AKTİF duyuruları okur) + detay metni + okundu durumu. Excel liste kuralı 2.
- **Bildirim entegrasyonu:** yayın penceresi İÇİNDEKİ duyurular `DashboardService.GetSummary`'ye kalem
  olarak eklenir (önemli=kritik rozet) → çan sayacı, Uyarılar ekranında "Duyuru" kategorisi, okundu/
  tümünü-okundu otomatik çalışır. `NavigateKey="announcements"` → tıkla, Duyurular ekranı.
- Takvim/Evrak entegrasyonu v1'de YOK (duyuru tarih-planı değildir; dosya eki ileride
  `DocumentService.Entities`'e 1 satırla eklenebilir — yeniden yazım riski yok).

## 4. Yetki modeli

Yeni **`announcements`** modülü: **yazma (Create/Edit/Delete) kapalı gelir** — rollere açılmalı
(yönetici işi). **Okuma** ürün kararıdır (PK-J1): duyurunun doğası "herkese yayın" → önerim okumanın
yetki ARAMAMASI (Uyarılar ekranı `WebPermOverride:""` emsali); alternatif katı View kapısı.

## 5. Tenant / BranchAccess

Her sorgu `company_id`'li (tenant testi standart). Hedefli duyuru: `branch_id` doluysa yalnız o şube
KAPSAMINDAKİLER görür (BranchAccess.Allowed süzgeci — iş emri/takvim ile aynı satırlar); şubesiz duyuru
herkese (sınıf kuralı). **Yan kapı testi:** kapsam dışı şubenin duyurusu ne ekranda ne bildirimde görünür.

## 6. Web / masaüstü / offline

`announcements` BusinessSync'e girer (yerel tablo) → masaüstü duyuruları **ÇEVRİMDIŞI okur**; yetkili
çevrimdışı duyuru da açabilir (senkronla yayılır — calendar_events emsali). Sunucu-otoriteli model
(projeler deseni) BİLİNÇLİ SEÇİLMEDİ: çevrimdışı şantiye makinesi duyuruyu görememezdi. Okundu işaretleri
cihaz-yereldir (PK-I4 ile tutarlı — davranış değişikliği yok).

## 7. Senkron

`Tables` sonuna 1 satır + `TableModule` → "announcements" (FK yalnız companies/branches — sıra sorunu yok).
Uçtan uca + tekrar-kopyasızlık + silme taşınması testleri standart (TKV13 emsali). Bildirim kalemi
SENKRONLANMAZ (türetilmiş — BLD-01 mimarisi aynen).

## 8. Canlı veriye etki / mevcut davranış değişikliği

Canlıya yazma YOK · mevcut tablolara ALTER YOK · mevcut davranış değişikliği YOK (tek dokunuş:
`AlertKind`'a SONA eklenen değer + GetSummary'ye eklemeli blok — BLD-01'de kanıtlanan güvenli desen).
Migration081 deploy'a kadar canlıda çalışmaz.

## 9. Test planı (kod yazılmadan)

`DuyuruTests` (~12-14): CRUD + doğrulama (başlık zorunlu, bitiş<başlangıç red) · yayın penceresi
(başlamamış/bitmiş bildirime GİRMEZ; yetkili ekranda görür) · **okundu-imza döngüsü** (okundu →
düzenlenince yeniden okunmamış) · **yazma yetkisi kapalı** + okuma modeli (PK-J1'e göre) ·
**BranchAccess hedef izolasyonu (yan kapı)** · **tenant** · soft delete + Çöp Kutusu ·
**senkron uçtan uca idempotent + silme taşınır** · **Migration081 bit-bit + statik CREATE-only** ·
bildirim entegrasyonu (çan sayacına girer/pencere dışı girmez) · kaynak kayıtlar bit-bit (okuma salt-okunur)
· mevcut kaynakların davranışının değişmediği (BLD regresyonu). Parite 55→56 / 62→63 güncellenir.

## 10. Bilinen sınırlar (v1 kapsam dışı — bilinçli)

Yorum/onay akışı · kişi-bazlı hedefleme (yalnız şube) · dosya eki · zorunlu "okudum onayı" ·
zamanlanmış e-posta/push (I kararıyla tutarlı) · zengin metin (düz metin yeter).

## 11. Teknik maliyet / yeniden yazım riski

**Büyüklük: DÜŞÜK-ORTA** (Takvim'den küçük — ay ızgarası gibi görsel bileşen yok; asıl iş 2 platform
ekranı + testler). Yeniden yazım riski YOK: kişi hedefleme ileride eklemeli tablo, dosya eki 1 satır,
zengin metin aynı kolonda.

## 12. Roadmap ilişkisi

J sonrası: **K — Global Arama** (FAZ 4/SIRA 11) · 7b hâlâ serbest · yayın bekleyenler Migration073..080
(+J'de 081). L — Dashboard, duyuru panelini ileride ana ekrana taşıyabilir (bugün karar gerektirmez).

---

## PK-J SORULARI — kullanıcı kararı bekleniyor

Karar bekleyen 5 soru ana rapordadır (PK-J1 okuma yetkisi · PK-J2 şube hedefleme · PK-J3 yayın
penceresi · PK-J4 gösterim yüzeyi · PK-J5 önem seviyesi). Kararlar gelmeden UYGULAMA BAŞLAMAZ.
