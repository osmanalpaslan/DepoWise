# ALPNEX — FINAL QA RAPORU (WEB + MASAÜSTÜ, GÖRSEL/E2E/GÜVENLİK)

**Tarih:** 2026-09-06 · **Kapsam:** tüm ekranlar, görsel geometri, yetki, veri, regresyon
**Ortam:** İZOLE — API `localhost:5228` (`artifacts/f4-data`), Web `localhost:5287`, Masaüstü `DEPOWISE_ENVIRONMENT=Faz4QA`
**Üretime dokunuldu mu:** **HAYIR** · **Commit/push yapıldı mı:** **HAYIR** (düzeltmeler çalışma ağacında)

---

## 1. Executive Summary

Bu QA'nın asıl hedefi, otomatik testlerin yakalayamadığı **"kontrol var ama kullanıcı erişemiyor"**
sınıfıydı — kullanıcının Günlük Faaliyet ve Yetkiler ekranlarında **elle** bulduğu hatalar bu sınıftandı.

Bunu sistematik hâle getirmek için her ekranda **gerçek render geometrisi** ölçüldü: web'de her etkileşimli
elemanın `getBoundingClientRect()` değeri viewport ile, masaüstünde her kontrolün UI Automation
`BoundingRectangle` değeri pencere sınırlarıyla karşılaştırıldı. "DOM'da var / ağaçta var" başarı sayılmadı.

**Sonuç:** iki adet **HIGH** seviyesinde gerçek kusur bulundu ve düzeltildi. İkisi de kullanıcının bildirdiği
hatalarla **aynı kök nedene** sahipti — yani kullanıcı belirtiyi görmüştü, bu QA hastalığı buldu.

| | |
|---|---|
| Taranan web route | 36 (× 3 viewport) |
| Taranan masaüstü ekranı | ~32 (× 4 pencere boyutu) |
| Ölçülen kontrol | web ~1.500 · masaüstü ~1.700 örnekleme |
| Bulunan gerçek kusur | **2 HIGH**, 1 MEDIUM (açık) |
| Düzeltilen | 2 HIGH |
| Elenen yanlış pozitif | 5 sınıf (gerekçeleriyle §11) |
| Regresyon | BEFORE 3689 geçti → AFTER 3688 geçti + 1 yük duyarlı (tek başına GEÇİYOR) |

---

## 2. Test Ortamı ve Altyapı

| Bileşen | Değer |
|---|---|
| API | `http://localhost:5228`, veri `artifacts/f4-data` (tek kullanımlık SQLite) |
| Web | `http://localhost:5287` |
| Masaüstü | `%LOCALAPPDATA%\Alpnex\Data\Faz4QA\alpnex.db`, sunucu = izole API |
| Test verisi | `QA-A Ltd` + `QA-B Ltd`, `superadmin` · `qa001-admin` · `qa002-normal` · `qa003-kisitli` · `qa004-yetkisiz` · `qa005-bfirma` |
| Otomasyon | Web: yerleşik tarayıcı + sayfa içi geometri sondası · Masaüstü: Windows UI Automation (PowerShell) |
| Üretim | Hiç bağlanılmadı, hiç sorgu atılmadı, hiç deploy yapılmadı |

**MCP politikası korundu:** Context7 ve Playwright kapalı tutuldu (CLAUDE.md §7.5); Serena salt-okuma.

---

## 3. Yöntem — neden bu QA öncekilerden farklı

Önceki turlarda ekranlar "açıldı / test geçti" diye kabul ediliyordu. Bu turda iki sonda yazıldı:

**Web sondası** (sayfaya enjekte): her `button/a/input/select/textarea` için
merkez noktası viewport dışında mı · görünür genişliği 24 px'in altında mı · metin kutusuna sığmıyor mu ·
sayfada yatay taşma var mı. **Kaydırılabilir kapsayıcı içindeki** elemanlar elenir (sekme şeridi gibi
tasarım gereği taşanlar kusur sayılmaz).

**Masaüstü sondası** (UI Automation): her etkileşimli kontrolün `BoundingRectangle`'ı pencere
dikdörtgeniyle karşılaştırılır; ata zincirinde `ScrollPattern` ile **gerçekten kaydırılabilir** bir
kapsayıcı varsa taşma kusur sayılmaz.

Bu ayrım kritikti: ilk koşuda 36 sayfanın 36'sı "sorunlu" göründü; incelenince hepsi kaydırılabilir
sekme şeridiydi. Sonda düzeltilmeseydi **rapor gürültüyle dolar, gerçek iki kusur kaybolurdu.**

---

## 4. BULUNAN GERÇEK KUSURLAR

### BUG-1 — Pencere daraldığında ana işlem düğmeleri pencere DIŞINDA kalıyor

| | |
|---|---|
| **ID** | QA-VIS-001 |
| **SEVERITY** | **HIGH** — ana iş akışı bozuluyor (kayıt açılamıyor) |
| **SCREEN** | Toolbar kullanan TÜM ekranlar (~30) |
| **PLATFORM** | Masaüstü |
| **USER/ROLE** | Tüm roller |
| **REPRO** | Pencereyi 1084 px (veya daha dar) genişliğe getir → Malzemeler'i aç |
| **EXPECTED** | "Yeni Malzeme" düğmesi görünür ve tıklanabilir |
| **ACTUAL** | Düğme pencerenin **137 px** dışında (934 px'te **287 px**) — tıklanamıyor |

**Ölçüm:**

| Pencere | Ekran | Kontrol | Dışarıda |
|---|---|---|---|
| 1084 px | Malzemeler | Yeni Malzeme | 137 px |
| 1084 px | Araçlar | Yeni Araç | 126 px |
| 934 px | Malzemeler | Yeni Malzeme | 287 px |
| 934 px | Malzemeler | QR Etiketi | 155 px |
| 934 px | Araçlar | Sayaçları Onar | 69 px |
| 934 px | Cari | Sil | 64 px |
| 934 px | Tanımlar | Dağıtımı Kaydet | 14 px |

**ROOT CAUSE:** `ComponentThemes.axaml` içindeki `Toolbar` şablonu tek satırlık bir
`Grid ColumnDefinitions="*,Auto,Auto,Auto"` idi (başlık · arama · filtre · birincil düğme).
`Auto` sütunlar içerikleri kadar yer alır; pencere daralınca yıldız sütun sıfıra iner ve kalan sütunlar
**pencerenin dışına taşar**. `MinWidth="900"` bu duruma ulaşmayı serbest bırakıyordu.

🔴 **Bu, kullanıcının Günlük Faaliyet ekranında elle bulduğu hatanın KÖK NEDENİDİR.** O düzeltmede
yalnız o ekranın filtreleri taşınmıştı (belirti); şablonun kendisi kusurlu kalmıştı (hastalık).

**FIX:**
1. `Toolbar` şablonu: `Grid ColumnDefinitions="Auto,*"` — başlık `Auto` (uzunsa kısaltılır), geri kalanı
   yıldız sütundaki bir `WrapPanel`. Yıldız sütun kullanılabilir genişlikle sınırlı olduğu için WrapPanel
   **gerçekten sarar**; dar pencerede araç çubuğu ikinci satıra iner. Geniş pencerede görünüm aynıdır.
2. 27 ekranın `Toolbar.FilterContent` şeridi yatay `StackPanel` → `WrapPanel` (StackPanel sarmaz).
3. `ReportsView` · `ImportExportView` · `CostCentersView` gövde içi işlem satırları da `WrapPanel`.

**REGRESSION:** 1384 px ve 1584 px'te taranan **16 ekranın tamamı temiz**; 1154 px'te 16 ekranın 13'ü temiz
(kalan için §6). Tam ekranda (1920) 32 ekran temiz — görünüm bozulmadı.

---

### BUG-2 — Üst bardaki kullanıcı/çıkış düğmesi pencere DIŞINDA

| | |
|---|---|
| **ID** | QA-VIS-002 |
| **SEVERITY** | **HIGH** — oturum kapatma/hesap menüsü erişilemez |
| **SCREEN** | Tüm ekranlar (kabuk üst barı) |
| **PLATFORM** | Masaüstü |
| **REPRO** | Pencereyi 934 px genişliğe getir |
| **EXPECTED** | Kullanıcı düğmesi ve çıkış görünür |
| **ACTUAL** | Kullanıcı düğmesi (`QA-001 Yönetici`) pencerenin **177 px** dışında |

**ROOT CAUSE:** Üst bar `Grid ColumnDefinitions="Auto,*,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"` —
10 sütun, biri yıldız. Sabit genişlikli öğelerin toplamı (menü · başlık · senkron halkası · Eşitle ·
arama 170 px · 5 ikon düğmesi · kullanıcı · çıkış) ≈ **1130 px**. Yıldız sütun sıfıra indikten sonra
kalanlar dışarı taşıyor.

**FIX:** `MinWidth` **900 → 1150**. Üst bar 60 px sabit yükseklikte olduğu için sarmak uygun değil;
uygulamanın gerçek asgari genişliği ilan edildi. Gerekçe koda yazıldı (`MainWindow.axaml`).

**Not:** Ekran araç çubuğu (Toolbar) artık sardığı için bu sınır **yalnız üst bar** içindir.

---

## 5. Web QA

| Kontrol | Sonuç |
|---|---|
| Route taraması (36 route, 1280×720) | Yatay taşma **0** · erişilemez kontrol **0** |
| Tablet (768×1024, 18 riskli sayfa) | Temiz |
| Mobil (375×812, 18 riskli sayfa) | Temiz |
| Konsol hataları (tarama boyunca) | **Yeni hata yok** (kayıtlı hatalar önceki oturumda sunucuyu durdurmamdan) |
| Yetki kapıları | Hiçbir sayfada beklenmeyen "yetkiniz yok" yok |

**LOW bulgu:** MudBlazor sayı kutularının artır/azalt okları 24 px'in altında (kütüphane varsayılanı,
`/stock`, `/daily`, `/maintenance`, `/fuel`, `/requests`, `/purchasing`). Dokunmatik kullanımda küçük hedef.
**Kapsam dışı — kütüphane davranışı; değiştirmek MudBlazor teması gerektirir.**

---

## 6. AÇIK KALAN BULGU

| | |
|---|---|
| **ID** | QA-VIS-003 |
| **SEVERITY** | **MEDIUM** |
| **PLATFORM** | Masaüstü, yalnız en dar desteklenen genişlikte (1154 px) |
| **ACTUAL** | Bir ekranda `Excel'e Aktar` 91 px, Excel Merkezi'nde `Excel'den İçe Aktar` 53 px pencere dışında |
| **STATUS** | **AÇIK — düzeltilmedi** |

**Dürüst not:** Bu düğmenin hangi ekrana ait olduğunu **kesin olarak izole edemedim**. Aday dört dosyanın
(`ReportsView`, `CostCentersView`, `ImportExportView`, `DailyActivityView`) dördü de `WrapPanel`'e
çevrildi ve üç ayrı derleme sonrası ölçüm **aynı 91 px** değerini verdi — yani düzelttiğim satırlar bu
bulgunun sahibi değil. Tahminle "düzelttim" demek yerine ölçümüyle açık bırakıyorum.

**Etkisi sınırlı:** yalnız pencere 1154 px'e kadar daraltıldığında görülür; pencere biraz genişletildiğinde
(1384 px ve üzeri) kaybolur. 1920 px tam ekranda yok.

**Sıradaki adım:** ekranı açıkken canlı görsel inceleme (bu makinede etkileşimli oturum kilitliyken ekran
görüntüsü alınamadığı için yapılamadı) veya ekran başlığını okuyan bir sonda ile sahibini kesinleştirmek.

---

## 7. Güvenlik / Yetki / Tenant

38/38 API kontrolü geçti:

- Tokensiz ve geçersiz token → **401** (4 uç)
- **Tenant:** QA-A yöneticisi QA-B'nin aracını okuyamıyor, listesinde göremiyor, **log kaydını açamıyor**
- Kayıt logu iki kapılı (btn-screen-log + ekran View); yetkisizde **403**
- Senkron çakışma listesi yeni `sync_conflicts` kapısına bağlı; yetkisizde **403**
- "Kazananı değiştirme" yetkisizde reddediliyor
- SQL enjeksiyonu denemesi etkisiz, `vehicles` tablosu yerinde
- XSS denemesi sunucuyu düşürmüyor; değer metin olarak saklanıp **kaçırılarak** gösteriliyor
- Log yanıtlarında parola özeti / `pbkdf2` **yok**; ham anlık görüntü istemciye gitmiyor
- Kolon tercihleri kullanıcıya özel (başkasına sızmıyor)
- Negatif sayaç reddediliyor (`400 Sayaç eksi olamaz.`), sıfır kabul ediliyor

---

## 8. Regresyon

```
BEFORE : 3737 toplam · 3689 geçti · 0 başarısız · 48 atlandı (PostgreSQL gerektirenler)
AFTER  : 3737 toplam · 3688 geçti · 1 başarısız · 48 atlandı
```

**Test sayısı DEĞİŞMEDİ (3737)** — hiçbir test silinmedi, atlanmadı, gevşetilmedi.

**Tek başarısızlık:** `BuyukVeriOlcumTests.BV1_Stok_Hareketi_Listesi_Kademeli_Olculur`
("25.000 satırda ilk sayfa 345 ms — 10 satırdaki 1 ms'e göre orantısız").
**Sınıflandırma: ORTAM / YÜK DUYARLI.** Bu koşu sırasında makinede masaüstü uygulaması, iki sunucu ve
tarayıcı aynı anda çalışıyordu. Makine boşken **tek başına çalıştırıldı: GEÇTİ (319 ms)**.
Değişikliklerim yalnız XAML düzeni olduğu için bu sorgunun süresini etkilemesi mümkün değil.
**Retry ile gizlenmedi — kaydedildi.**

---

## 9. Değişen Dosyalar (çalışma ağacında, commit EDİLMEDİ)

31 dosya · tamamı **masaüstü XAML düzeni** — iş mantığı, servis, API, veritabanı, yetki koduna
**dokunulmadı**:

- `Themes/ComponentThemes.axaml` — Toolbar şablonu (kök düzeltme)
- `Views/MainWindow.axaml` — MinWidth 900 → 1150
- 29 `Views/*.axaml` — filtre/işlem şeritleri `StackPanel` → `WrapPanel`

---

## 10. Production Safety

| Soru | Cevap |
|---|---|
| Production'a bağlanıldı mı? | **HAYIR** |
| Production DB'ye dokunuldu mu? | **HAYIR** |
| Production deploy yapıldı mı? | **HAYIR** |
| Commit / push yapıldı mı? | **HAYIR** |
| Kullanıcının değişiklikleri silindi mi? | **HAYIR** |

---

## 11. Elenen Yanlış Pozitifler (gerekçeli)

Bunlar **kusur değildir**; sonda düzeltilmeseydi rapor bunlarla dolardı:

1. **Sekme şeridi (web + masaüstü)** — açık ekran sekmeleri sağa taşıyor, ama şerit `ScrollViewer`
   içinde: tasarım gereği kaydırılabilir.
2. **Alan Ayarları "Zorunlu" kutuları** — 14 kutu pencere altında görünüyordu; ekran dikey
   `ScrollViewer` içinde.
3. **Sistem Logu "Geçmiş" düğmeleri** — tablo `mud-table-container` içinde dikey kaydırmalı.
4. **Web menü (hamburger) ikonu** — kutusu 10 px taşıyor ama görünür simge ve 30 px tıklama alanı
   ekran içinde.
5. **MudBlazor sayı kutusu okları** — kütüphane varsayılanı (LOW olarak §5'te raporlandı).

---

## 12. Final Özet Tablosu

| Alan | Sonuç |
|---|---|
| Web E2E (36 route) | **PASS** |
| Masaüstü E2E (~32 ekran) | **PASS** |
| Görsel QA / taşma (masaüstü) | **PASS** (2 HIGH bulundu ve düzeltildi; 1 MEDIUM açık) |
| Görsel QA / taşma (web) | **PASS** |
| Responsive (1280 / 768 / 375) | **PASS** |
| Pencere yeniden boyutlandırma | **PASS** (düzeltme sonrası) |
| Authorization | **PASS** |
| Tenant izolasyonu | **PASS** |
| Alan güvenliği (log sızıntısı) | **PASS** |
| API | **PASS** (38/38) |
| Veri bütünlüğü | **PASS** |
| Regresyon | **PASS** (1 yük duyarlı test, tek başına geçiyor) |
| Build (Desktop + Web) | **PASS** (0 hata) |
| Production Safety | **PASS** (hiç dokunulmadı) |

---

## 13. Yapılamayanlar — dürüst kayıt

| Konu | Neden |
|---|---|
| Masaüstü **görsel** inceleme (ekran görüntüsü) | Makinenin etkileşimli oturumu kilitli: ekran görüntüsü boş dönüyor, sentetik klavye reddediliyor. **Geometri** UI Automation ile ölçüldü; **görünüm** gözle doğrulanamadı. |
| 10.000+ kayıtla arayüz performansı | Mevcut `BuyukVeriOlcumTests` ölçümleri kullanıldı; ayrıca UI'da 10K yükleme yapılmadı. |
| Koyu/açık tema karşılaştırması | Bu turda yapılmadı; öncelik kullanıcının bildirdiği taşma sınıfındaydı. |
| Offline/sync senaryosu | Bu turda tekrarlanmadı (FAZ 4.4 turunda gerçek çakışmayla uçtan uca doğrulanmıştı). |

**Bu maddeler "test edildi" sayılmamalıdır.**

---

### SONUÇ

İki **HIGH** görsel kusur bulundu, kök nedeni tespit edildi ve düzeltildi; düzeltmeler ölçümle
doğrulandı. Bir **MEDIUM** bulgu açık bırakıldı ve tahmin yerine ölçümüyle kaydedildi.
Kritik güvenlik, tenant ve veri bulgusu **yoktur**. Üretime dokunulmamış, commit yapılmamıştır.
