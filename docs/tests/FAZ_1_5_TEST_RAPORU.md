# FAZ 1–5 test raporu · 2026-09-06

**Kapsam:** kullanıcının bildirdiği yakıt hatası · kompakt tarih alanı · kullanıcı iletişim alanları ·
liste yazdırma (PDF) · uygulama içi sohbet. Her biri **masaüstü ve web** için ayrı ayrı.

**Ortam:** izole QA (`DEPOWISE_ENVIRONMENT=Faz4QA`, yerel API 5228 / web 5287).
**Üretim verisine dokunulmadı**; tüm kayıtlar izole QA veritabanındadır.

---

## 1. Ne yapıldı

| Faz | İş | Sonuç |
|---|---|---|
| 1 | Yakıt Dağıtımları'nda LİTRE kolonu boş | **Düzeltildi** — kök neden ölçüldü |
| 2 | Tarih alanları "çirkin ve büyük" | **Düzeltildi** — 305 px → 112 px, 43 alan |
| 2 | "Yeni Sekme" alanı kaldırılsın | **Kaldırıldı** — iki platformdan da |
| 3 | Kullanıcı formunda eksik alanlar | **Eklendi** — e-posta · telefon · unvan · not + düzenleme yolu |
| 4 | Liste yazdırma (PDF) | **Eklendi** — 11 masaüstü ekranı, 13 API ucu, 6 web sayfası |
| 5 | Uygulama içi sohbet | **Eklendi** — iki platformda, senkron dışı, yalnız çevrimiçi |

---

## 2. Kök nedenler (tahmin değil, ölçüm)

**Yakıt litre.** Veri yolu sağlamdı (SQL `fd.liters`'ı seçiyor, eşleme doğru). Sorun gösterimdeydi:
DURUM hücresi satır bazında gizleniyor (`IsVisible=IsCancelled`) ve o kolonun **ortak boyut grubu
yoktu**; görünmeyince istediği genişlik 0 oluyor, satır ızgarası o ~102 px'i yıldız kolona geri
veriyordu. Sonuç: LİTRE'den sonraki tüm değerler başlığa göre **bir kolon sağa** kayıyordu.
Aynı desen tüm ekranlarda tarandı — başka örneği yok.

**Tarih alanı.** Avalonia kutusu dar kalınca yıl bölmesini tamamen düşürüyor: 150/200 px'te yıl
**hiç yok**, 250'de kırpık, 280'de tam. Bir önceki turda alanları 280'e çıkarmak yılı kurtarmış ama
formları şişirmişti; doğru çözüm kutuyu değiştirmekti.

---

## 3. Test sonuçları

| Ölçüm | Sonuç |
|---|---|
| Yeni birim/entegrasyon testleri | **151 geçti** (TarihMetni 35 · İletişimDoğrulama 26 · KullanıcıAlanları 20 · PDF 12 · Sohbet 22 · mevcut migration testleri 36) |
| Yeni özellik API bataryası | **32 / 32** — 8'i doğrudan güvenlik (firma sızıntısı, yetkisiz erişim) |
| Mevcut API bataryası (gerileme) | **38 / 38** |
| Masaüstü görsel tarama: 53 ekran × 1920·1600·1366·1100·980 | tıklanamaz/kırpık öğe **yok** |
| Web tarama: 45 rota | bulgu **yok** |
| Derleme: Desktop · Web · Api | **0 hata** |

**Menüde açılmayan 6 ekran** (Firma Tanım · Makine Yönetimi · Güncelleme Yönetimi · Sunucu Yedekleri ·
Yetki Şablonları · Geliştirici Modu): QA kullanıcısı süper admin olmadığı için görünmüyor —
deny-by-default doğru çalışıyor, hata değil.

---

## 4. Tam süitin yakaladığı üç GERÇEK kusur

Bu tur en çok değeri **mevcut testlerin** üretti; üçü de gerçek kusurdu, testler zayıflatılmadı:

1. **Parite kırılması.** "Yeni Sekme" düğmesini yalnız masaüstünden kaldırmıştım; web'de duruyordu.
   `SekmeSeridiTests.SEK4` (iki şerit aynı parçaları taşır) bunu yakaladı → web'den de kaldırıldı.
2. **Onay kuralı atlanmıştı.** Yeni "Bilgileri Düzenle" düğmeleri, projenin "her Düzenle işlemden
   önce onay sorar" kuralına uymuyordu (`IslemOnaylariTests`). İki platforma da onay eklendi —
   test gevşetilmedi.
3. **Yetki ağacı paritesi.** Yeni `chat` modülü hiçbir menüde görünmüyordu (`ScreenTreeParityTests`).
   Sohbet bir ekran değil, her ekranın üstünde duran bir katman olduğu için testin "menüsüz ama
   gerçek yetki" listesine **gerekçesiyle** eklendi.

Ayrıca yol boyunca iki kusur daha bulundu ve düzeltildi:
- Web'de yetki paketi girişten SONRA geliyor; bileşen yetkiye bir kez bakınca sohbet ancak sayfa
  yenilenince görünüyordu → `AuthState.Changed` aboneliği.
- Yeni izole CSS, tarayıcı önbelleğindeki eski dosyayla eziliyordu; bileşen doğru çizilip **stilsiz**
  kalıyordu → stil dosyaları artık derleme damgasıyla sürümleniyor (ADR-233).

---

## 5. Güvenlik kontrolleri (yeni yazma yüzeyleri)

Bu turda iki yeni yazma ucu açıldı (kullanıcı profili, sohbet). Her ikisi de ayrıca sınandı:

| Kontrol | Sonuç |
|---|---|
| Yetkisiz kullanıcı başkasının profilini düzenleyemez | ✅ 403 |
| Başka firmanın kullanıcısına dokunulamaz | ✅ engellendi |
| Başka firmaya mesaj gönderilemez | ✅ 403 |
| Başka firmanın konuşması okunamaz | ✅ boş döner |
| Başka firma okundu işaretleyemez | ✅ etkisiz |
| Sohbet yetkisi olmayan kişi listesini alamaz | ✅ 403 |
| Tokensiz sohbet ucu | ✅ 401 |
| Dışa aktarım yetkisi olmayan PDF alamaz | ✅ 403 |

---

## 6. Kapsam dışı bırakılanlar (yapılmadı, gizlenmedi)

- **A grubu kalanı:** ekran içi liste toplamları, cari yaşlandırma (vade) raporu, toplu işlem,
  favori ekranlar. (Yazdırma çıktısında toplam satırı **var**; ekran içi toplam ayrı iş.)
- **B grubu:** çek/senet portföyü, e-posta/SMS uyarısı, trafik cezası + HGS/OGS, araç zimmeti
  geçmişi, lastik yaşam döngüsü, puantaj.
- Bunlar bir sonraki turda yapılacak; bu yayın FAZ 1–5'i kapsar.

---

## 7. Görsel tarama — son durum (UI Automation ölçümü)

| Genişlik | Sonuç |
|---|---|
| 1920 | temiz |
| 1600 | temiz |
| 1366 | temiz |
| 1100 | temiz |
| 980 (asgari) | temiz — tek satır kaydırma çubuğu oku (16×3 px), kullanıcı denetimi değil |

Tarama sırasında **bir gerçek kusur daha** bulundu ve düzeltildi: Yakıt Dağıtımları'nın filtre satırı
yatay `StackPanel`'di; 1100 px'te "Temizle", 980 px'te "Sorgula" da pencere dışında kalıyordu
(90 / 101 / 194 px ölçüldü). `WrapPanel`'e çevrildi, iki genişlik de yeniden ölçüldü.
