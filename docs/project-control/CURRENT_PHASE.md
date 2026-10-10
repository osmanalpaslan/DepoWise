# AKTİF DURUM

> Yalnız son ~10 günün girdileri burada durur (her oturumda okunur → kısa kalmalı). Daha eskisi:
> `../arsiv/project-control/CURRENT_PHASE_GECMIS_2026-08-11_2026-09-30.md`.

**Özet (2026-10-10):** canlıda API+Web, masaüstü 1.0.195, şema 98, üretim DB Supabase. Sıradaki: kullanıcının seçtiği öneriler (yakıt sapma uyarısı, kiralık araç uyarı+maliyet raporu, yedek geri yükleme provası).

## ✅ 2026-10-10 YAYINDA — API+Web 41f47a7 · masaüstü 1.0.195 · şema 98 · Talep çoklu araç · Kiralık Araçlar · Yakıt Özeti · yeni sesler
- **Talep Formu (4e0cf18):** kalemde birden fazla araç (`material_request_items.vehicle_ids`, Migration097; `vehicle_id` = ilk araç,
  geriye uyumlu); arama sonucunda + kalem tablosunda + standart PDF'te **malzeme kodu**. Araç kimliği artık firmaya göre doğrulanıyor.
- **Kiralık Araçlar (c3101f5):** ayrı ekran (`vehicles.rental`, masaüstü `vehicles:rental`, web `/vehicles/rental`); `vehicles.is_rental`
  + kira sütunları (Migration098). Araç Listesi kiralıkları göstermez; tüm araç seçicileri ikisini "(Kiralık)" etiketiyle listeler.
  Değişim kaydı: yeni kiralık araç açılırken giden araç aynı işlemde PASİF + kira bitişi yazılır. "Kiralamayı Bitir" (iade).
  Kiralık araç şablon zorunluluğundan muaf. Ayrı fiziksel tablo AÇILMADI (gerekçe Migration098 başlığında).
- **Yakıt Özeti (e599cf1):** iOS tarzı kartlar; haftalar mavi oran çubuğu, günler yeşil takvim kutucuğu; bu ay açık, boş aylar soluk.
- **Sesler (494193c):** matematiksel tonlar → GitHub **Octave** (ücretsiz iOS arayüz sesleri); `scripts/ses_guncelle.mjs`.
- **QA (41de313):** Avalonia Headless artık Skia ile çizer + ekran görüntüsü; UIT10-12. Web: `tests/web-e2e/tests/yeni-ekranlar.spec.js`.
- **Doğrulama:** tam takım 4008 (9 bulgu düzeltildi, ilgili 155 test yeniden geçti) · masaüstü arayüz 6/6 · canlı web Playwright 7/7 (salt okuma).
  Canlı web 256 MB: art arda çok Playwright koşusu Blazor oturumlarıyla makineyi yavaşlatır → koşular arası ~3 dk bekle.
- **Not:** `TASK_BACKLOG.md`'deki birçok "SIRADA" madde kodda zaten kapalı (GUV-A1/A2, DEN-D1, DEN-E2, DEN-F1 doğrulandı) → liste temizlenmeli.

## ✅ 2026-10-04 (2. tur) YAYINDA — API+Web a6294ac · masaüstü 1.0.194 · sayaç kuralı · tablo başlıkları · rapor sütun genişliği · ANLIK EŞİTLEME
- **Bakım sayacı (e44e46a):** formlarda tek alan, türü aracın sayacı; bakımda zorunlu (masaüstü + web + sunucu `RequireMeter`).
  Güncelden küçük/eşit değer kabul. Tamir/İlave Yağ/Filtre'de tek alan ama opsiyonel.
- **Tablolar (72fd044):** tüm başlıklar yatay+dikey ortada (rapor dahil); rapor tablosu tutamağı Thumb → işaretçi yakalamalı Border.
- **Anlık eşitleme (d7acce4):** `/api/sync/wait` uzun yoklama + `SyncNotifier`; masaüstü gelen için bekler, giden için 3 sn'de bir YEREL kontrol.
  Zamanlayıcı: 30 sn güvenlik kontrolleri + 60 sn güvenlik ağı turu. Geri sayım halkası kalktı; aktarımda dönen yay + ↑giden/↓gelen.
- **Kalıcı kurallar (hafıza):** test tipine Claude karar verir; tablo istekleri rapor dahil TÜM tablolara uygulanır.
- **Yayın (2026-10-04 19:00):** tam takım 3.988/3.990 (2 hata = yeni test sınıflarında seri koleksiyon eksikti, düzeltildi, yeniden geçti) → API + Web deploy + masaüstü 1.0.194. Sıradaki: kullanıcı rapor tablosunda sütun sürüklemeyi ekranda doğrulasın.

## ✅ 2026-10-04 — uyarılar 2. tur · fotoğraflar cihaza iner · muadil düzenleme (1.0.194 ile YAYINDA)

- **Uyarı kök nedenleri (canlı veride doğrulandı, salt-okuma):**
  - Bakım: 1.0.193 tanımın biriminde sayaca göre seçiyordu; GREY 010'da saat tanımının yeni kaydı sayacı **km alanına** girilmiş → eski kayıt seçildi.
    Artık: **yapıldığı tarih** önce, sayaç = km ?? saat (89215f7).
  - Muayene: uyarı doğruydu; **liste ekranı** yenilenmiş eski belgeyi "Süresi geçti" gösteriyordu → artık "Yenilendi".
  - Bilgilendirme notu (masaüstü + web): sayaçsız son bakım (5 kalem "Kontrol gerekli"), şüpheli araç sayacı (EKS-P 004: 122045 vs 12404),
    esas alınmayan sonradan girilen kayıt, bitiş tarihsiz / geçmiş bitişli yeni belge.
  - Evrak modülü canlıda boş (uyarı üretmiyor); diğer kaynaklar (stok/yakıt/iş emri/talep/duyuru) kayıt-bazlı, "eski kayıt" sorunu yok.
- **Fotoğraflar (8248789):** sunucu diski tam (251 kayıt → hepsinin dosyası var). Taşıma günündeki erişimsizlik havuz tükenmesi (500 → "çevrimdışı" sanıldı).
  Masaüstü artık fotoğrafları `%LOCALAPPDATA%\DepoWise\FotoOnbellek` altına indirir (30 dk'da bir arka planda, yeni uç `/api/photos/index`); çevrimdışıyken buradan gösterir.
- **Muadil (b192462):** sağ panelde KOD — Ad; çift tık penceresinde (masaüstü + web) liste + Düzelt modunda ekle/çıkar.
- **Sıradaki:** tam test takımı → kullanıcı onayıyla API deploy + masaüstü 1.0.194 yayını. Önce API (yeni uç), sonra masaüstü.

## ✅ 2026-10-01 akşam — uyarı düzeltmesi + tablo güncellemeleri YAYINDA (API bf5e20e · masaüstü 1.0.193)

- **Uyarı:** bakım/muayene "en son kayıt" giriş zamanına değil yapıldığı noktaya göre (canlıda 21+1 grup eski uyarı tutuyordu).
- **Tablolar:** ekran geneli sağ tık, belirgin sütun çizgisi (ColumnRuleBrush), başlık–filtre hizası, tasarım tüm tablolarda, rapor tablosunda kayıtlı genişlik.
- **DB bağlantısı:** API Supabase **doğrudan** bağlantıya alındı (IPv6, `Maximum Pool Size=25`, DB max_connections 60).
  Sebep: pooler adresinde 10'luk havuz yoğun eşitlemede tükendi → `/api/dashboard` 500. Yerel araçlar pooler adresini kullanır (PC'de IPv6 yok).
- **Test:** tam takım 3.885/3.885 (58 dk) + sonraki değişiklikler için ilgili 821 test.


## ✅ ÜRETİM VERİTABANI TAŞINDI: Neon → Supabase (2026-10-01) · API 512 MB · masaüstü 1.0.191 sunucuda

- **Neden:** Neon ücretsiz compute kotası ~17.09'da doldu (masaüstü 15 sn yoklaması DB'yi hiç uyutmuyordu) → API çöktü.
- **Yapılan:** Neon `depowise_prod` pg_dump (yedek: `Documents/DepoWise_Yedekler/neon_depowise_prod_2026-10-01.dump`, 1,1 MB) →
  Supabase `Alpnex` / `depowise_prod` (eu-central-1, PG17, session pooler :5432, `Maximum Pool Size=10`; Data API kapalı).
  Doğrulama: 107/107 tablo satır sayısı birebir (24.419 satır); `updated_at` olan 70 tablonun son güncelleme zamanı birebir;
  Neon yedekten sonra değişmedi (veri kaybı yok). Fly secret `DEPOWISE_PG_URL` → Supabase; API + Web temiz HEAD (f428464)
  kopyasından deploy; API `shared-cpu-1x` 512 MB; masaüstü **1.0.191** `publish_release.mjs` ile yayınlandı (otomatik güncelleme).
- **Ders:** bu Npgsql sürümü `Max Pool Size` takma adını tanımıyor → `Maximum Pool Size` (ilk deploy bu yüzden çöktü, hemen düzeltildi).
- **Geri dönüş:** Neon `depowise_prod` silinmedi (yedek). Bağlantı bilgisi Neon API'den alınır (.env'deki eski şifre geçersizdi).
- **Açık:** CLAUDE.md §4 hâlâ "Neon" diyor (dosyada kullanıcının commitlenmemiş değişiklikleri var, dokunulmadı). SNK-15: birkaç gün
  sonra Supabase aylık trafik ölçümü.
