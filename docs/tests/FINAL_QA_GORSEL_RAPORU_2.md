# Görsel QA — 2. tur (ölçeklendirme odaklı) · 2026-09-06

**Kullanıcı talebi:** "ekran, tablo ve buton ölçeklendirmesi… her şey pencere boyutuna göre ölçeklensin",
"her ekran için çalışmayan ve hatalı çalışan alan, buton ve çalışma mantığı hatası var mı kontrol et",
"kendi eklemek istediğin maddeleri de ekle", "tespit ettiğin hataların önerilen düzeltmelerini uygula".

**Ortam:** izole QA (`DEPOWISE_ENVIRONMENT=Faz4QA`, yerel API 5228 / web 5287). **Üretim verisine
DOKUNULMADI.** Eklenen 5 örnek malzeme yalnız izole QA veritabanındadır.

---

## 0. Önce: bir önceki turun ölçümleri neden yanlıştı

İlk turda `MainWindow.axaml` içine XML yorumunu **öznitelik listesinin ortasına** koymuşum. Dosya
geçersiz XML olduğu için **masaüstü derlemesi 11:38'den beri sessizce başarısızdı**; "BUILD-DONE"
görülüyor ama üretilen ikili eskiydi. Bu yüzden birinci turda yapılan tüm düzeltmeler ölçüme hiç
yansımadı ve "düzelttim ama ölçüm değişmedi" çelişkisi doğdu.

**Alınan ders (yönteme eklendi):** derleme sonrası "hata yok" yetmez — **ikilinin zaman damgası**
doğrulanır. Bu tur boyunca her derlemeden sonra `DepoWise.Desktop.dll` damgası kontrol edildi.

---

## 1. Yöntem (ve kendi eklediğim maddeler)

| # | Madde | Kaynak |
|---|---|---|
| 1 | 53 masaüstü ekranı × 5 pencere genişliği — tıklanamaz/kırpık denetim ölçümü (UI Automation) | kullanıcı |
| 2 | 68 web rotası × 3 genişlik — aynı ölçüm (getBoundingClientRect + kaydırılabilir ata elemesi) | kullanıcı |
| 3 | **Windows %125/%150 ölçekleme senaryosu** — 1366@%125 = 1092 mantıksal px | **eklendi** |
| 4 | **Tablo başlık↔satır hizalaması** ve **uzun metin taşması** (uzun adlı örnek kayıtlarla) | **eklendi** |
| 5 | **Ölü buton statik denetimi** — XAML'de bağlanan komut ViewModel'de var mı | **eklendi** |
| 6 | **Türkçe yerelleştirme** — alanlarda İngilizce metin kaldı mı | **eklendi** |
| 7 | İş mantığı: tam test süiti + 38 maddelik API bataryası | kullanıcı |
| 8 | Web/masaüstü paritesi — her bulgu iki platformda da soruldu | kural |

**Gerçekçi boyutlar seçildi.** Kullanıcı "kullanmadığım kadar küçük boyutlar" dediği için ölçüm
1920 · 1600 · 1366 üzerine kuruldu; 1100 ve 980 yalnız **asgari sınırı ölçmek** için kullanıldı
(bkz. §2.1 — sınırın kendisi bir hataydı).

---

## 2. Bulgular ve uygulanan düzeltmeler

### 2.1 🔴 Asgari pencere genişliği hatalıydı — %125 ölçeklemede pencere ekrana sığmıyordu
- **Ölçüm:** üst bar tek satırlık, sarılamaz bir Grid. 1120 px'te kullanıcı düğmesi 7 px, 1060'ta 67 px,
  900'de 6 öğe pencere dışında → hesap menüsü/çıkış **tıklanamıyordu**.
- **İlk (yetersiz) çözüm:** sınırı 1150/1180'e çekmek. Ama 1366 px'lik bir dizüstü **%125 Windows
  ölçeklemesinde** 1092 mantıksal px olur; 1180'lik pencere o ekrana sığmazdı. Sorun başka kullanıcıya
  taşınıyordu.
- **Uygulanan çözüm:** üst bar **uyarlanabilir** yapıldı (`GenislikEsikConverter`). Pencere daraldıkça
  sırayla global arama (1300 altı · 178 px), kullanıcı adı (1200 altı · 148 px) ve "Ekran" etiketi
  (1120 altı · 45 px) gizlenir. **Hiçbir işlev kaybolmaz:** menü araması, baş harf dairesi ve ikon
  yerinde kalır; kullanıcı adı düğmenin ipucunda zaten var.
- **Sonuç:** asgari genişlik **980**'e indi. 1366@%125 (1092) ve 1280@%125 (1024) senaryolarının
  ikisinin de altında.

### 2.2 🔴 Ekran çubuğu ve filtre satırları sarmıyordu → düğmeler ekran dışında
- **Ölçüm (1180 px):** Giriş-Çıkış'ta "+" 161 px, Stok Hareketleri'nde "Temizle" 89 px, Stok Sayım'da
  bir düğme 151 px pencere dışında. **1100 px:** Zimmet'te "Kaydet" 109 px dışarıda.
- **Düzeltme:** ilgili satırlar `StackPanel` → `WrapPanel`. Ayrıca `AssignmentsView`'de dış kapsayıcının
  `HorizontalAlignment="Left"` niteliği kaldırıldı: sola yaslanmış kapsayıcı çocuğuna **sınırsız
  genişlik** verdiği için içindeki WrapPanel hiç sarmıyordu (sessiz tuzak).
- Dosyalar: `StockEntryView` (6 satır), `StockCountView` (2), `StockMovementsView`, `AssignmentsView`,
  `AuditLogView`, `StockChangeLogView`, `StockDistributeView`, `CostCentersView`, `ReportsView`,
  `ImportExportView`.

### 2.3 🔴 Tarih alanlarında YIL hiç görünmüyordu
- **Ölçüm:** Avalonia tarih kutusu dar kaldığında yıl bölmesini **tamamen düşürüyor**. 150 px ve 200 px'te
  "7 Ağustos" (yıl yok), 250 px'te "202|" (kırpık), **280 px'te "7 Ağustos 2026" tam**.
- **Etki:** kullanıcı bir tarih süzgecinin hangi **yıla** ait olduğunu göremiyordu — rapor/filtre
  ekranlarında sessiz ama ciddi bir hata.
- **Düzeltme:** tarih alanı olan tüm `FormField`'lar 280 px'e sabitlendi — **20 ekranda 37 alan**.
- **Yan etki yakalandı:** genişleyen alanlar 3 ekranda taşmayı geri getirdi (Sistem Logu, Stok Değişiklik
  Kaydı, Atanmamış Stok Dağıtımı); onlar da sarmalanarak kapatıldı. *(Tarama ağı bu yüzden var.)*

### 2.4 🔴 Tarih alanlarında İngilizce yer tutucu ("day / month / year")
- Türkçe uygulamada boş tarih alanları İngilizce yazıyordu. Stil ile çözülemez: Avalonia bu metinleri
  şablon uygulandıktan sonra **yerel değer** olarak yazar, yerel değer stili yener.
- **Düzeltme:** `TarihYerTutucu` — açılışta bir kez kurulan sınıf düzeyinde işleyici; **43 kullanım,
  25 ekran** tek noktadan Türkçeleşti (`gün / ay / yıl`). Hiçbir görünüm dosyası değişmedi.
  Tarih seçiliyken gerçek değere dokunulmaz (görsel olarak doğrulandı).

### 2.5 🔴 Atanmamış Stok Dağıtımı — tabloda iki ayrı hata
1. MALZEME kolonu yıldız (*) genişlikteydi; uzun malzeme adı satırı büyütünce **"DAĞITILACAK" alanı ve
   "Tümü" düğmesi ekran dışına** itiliyordu (1366'da 106 ve 286 px) → dağıtım miktarı **girilemiyordu**.
2. Başlık ile satırlar **hizalanmıyordu** (ATANMAMIŞ boş görünüp sayılar KALAN'ın altına düşüyordu).
- **Düzeltme:** projedeki diğer tabloların kalıbı uygulandı — tüm kolonlar sabit (Min=Max), tablo yatay
  kaydırıcı içinde, başlık ve gövde aynı kaydırıcıyı paylaşıyor. Görsel olarak doğrulandı.

### 2.6 Üst bar başlığı üç nokta göstermeden kesiliyordu
- Dikey `StackPanel` çocuğunu daraltılmış genişliğe sıkıştırmadığı için `TextTrimming` devreye girmiyor,
  `ClipToBounds` metni ham kesiyordu ("Maliye|"). `Grid`'e çevrildi → gerçek "…" kısaltma + 14 px boşluk.

---

## 3. Doğrulama sonuçları

| Ölçüm | Sonuç |
|---|---|
| Masaüstü: 53 ekran × 1920 · 1600 · 1366 · 1100 · 980 | **tıklanamaz/kırpık öğe YOK** |
| Web: 68 rota × 1920 · 1366 · 1024 | **bulgu YOK** |
| Tam test süiti (`scripts/run_tests.ps1`) | **3689 geçti · 0 başarısız · 48 atlandı** |
| API bataryası (38 kontrol: kimlik · tenant · yetki · çakışma · sayaç · enjeksiyon) | **38/38 geçti** |
| Ölü buton statik denetimi | `ReflectionBinding` 0 · boş komut gövdesi 0 · derlenmiş bağlama açık → **ölü buton yapısal olarak imkânsız** |

**Menüde açılmayan 6 ekran** (Firma Tanım · Makine Yönetimi · Güncelleme Yönetimi · Sunucu Yedekleri ·
Yetki Şablonları · Geliştirici Modu) — QA kullanıcısı süper admin olmadığı için görünmüyor:
**deny-by-default doğru çalışıyor**, hata değil.

---

## 4. Kapsam dışı bırakılan gözlemler (değiştirilmedi)

- **Geniş ekranda tablonun sağında boş alan.** Tablolar bilerek "Excel benzeri"dir: kolonlar içeriğe göre
  sabit, sığmazsa yatay kaydırma (2026-07-18 kullanıcı isteği). Tabloyu esnetmek bu kararı bozardı.
  Kullanıcı "Kolonları Ayarla" ile kolon ekleyerek genişliği kendisi belirler.
- **`Evrak / Belgeler` — 7×16 px "Page right".** Kaydırma çubuğunun sayfa oku; kullanıcı denetimi değil.
  Ölçüm aracının bilinen yanlış pozitifi.

---

## 5. Web/masaüstü paritesi

Tarih kusurlarının ikisi de (yıl düşmesi · İngilizce yer tutucu) **Avalonia'ya özgüdür**. Web
`MudDatePicker` + `tr-TR` kültürü kullanır; her iki sorun web'de **yoktur** ve web ölçümleri temizdir.
Yerleşim düzeltmeleri de yalnız masaüstü XAML'ini ilgilendirir — web MudBlazor ızgarasıyla zaten
duyarlıdır. **Web'de yapılacak karşılık işi yoktur.**
