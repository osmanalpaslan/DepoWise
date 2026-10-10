# MOB-W — Mobil tarayıcı uyumluluğu (responsive web)

> **Durum:** 🔵 AKTİF · başlangıç **2026-09-04** · kullanıcı kararı
> **Yerini aldığı iş:** ~~N — Mobil (ayrı uygulama)~~ ❌ tamamen kaldırıldı

---

## 1. Karar ve gerekçe

**Kullanıcı kararı (2026-09-04):** Ayrı bir mobil uygulama **yapılmayacak**; bu satır yol haritasından
tamamen çıkarıldı. Kullanıcı, telefonun **tarayıcısından** web'e girip işi oradan yönetmek istiyor.

Gerekçe (kullanıcının kendi ifadesi): ayrı bir mobil uygulamanın yükünü taşımak istemiyor.
Teknik olarak da doğru karar:

- Ayrı uygulama = **üçüncü bir istemci** demektir. Bugün iki istemci (masaüstü + web) zaten
  `AppScreens` kataloğu, yetki kapıları ve senkron sözleşmesiyle hizada tutuluyor; üçüncüsü bu
  hizalama maliyetini **1,5 katına** çıkarırdı (bkz. `PARITY_MATRIX.md`).
- Web zaten **Blazor Server**: sunucuda çalışır, istemciye HTML gönderir. Yani telefonda çalışması
  için yeni bir mimari gerekmez — **yalnız dar ekran davranışı** gerekir.
- Mağaza yayını, imzalama, sürüm uyumu ve ayrı güncelleme hattı da ortadan kalkar
  (`GNC-02` yükü büyümez).

## 2. Kapsam

**KAPSAMDA:** mevcut web ekranlarının telefon tarayıcısında (~390 px) kullanılabilir olması.

**KAPSAM DIŞI — bilinçli:**
- Yeni ekran, yeni özellik, yeni yetki, yeni API ucu, migration → **hiçbiri yok**.
- **Masaüstü uygulaması bu işten etkilenmez.** Ortak servis/model/API'ye dokunulmaz;
  değişiklik yalnız web'in sunum katmanındadır (`.razor` düzeni + `app.css`).
- Çevrimdışı mobil çalışma yok (web zaten çevrimiçidir; çevrimdışı iş masaüstünün işidir).
- Dokunmatik jestler (kaydırarak silme vb.) yok — mevcut etkileşim korunur.

## 3. Yaklaşım — 62 sayfaya tek tek dokunulMAZ

Web'de **62 Razor sayfası / katalogda 70 ekran** var. Her birine ayrı mobil düzeni yazmak hem
haftalar sürer hem de her yeni ekranda tekrar unutulur. Bunun yerine **tek bir mobil katmanı**
kurulur:

1. **Genel CSS katmanı** (`app.css` içinde tek bir mobil bölümü) — uygulamanın ortak yapılarını
   (üst bar, menü, tablo, filtre satırı, form, dialog, sekme şeridi) dar ekranda düzeltir.
   Bu katman **bütün ekranlara aynı anda** uygulanır; yeni eklenen ekran da kendiliğinden alır.
2. **Düzen (layout) değişiklikleri** — yalnız `MainLayout.razar`/`NavMenu.razor` gibi **ortak**
   dosyalarda; tek yerde yapılır, her ekranda geçerli olur.
3. **Tekil sayfa dokunuşu** yalnız genel katmanın çözemediği yerde yapılır ve gerekçesi yazılır.

Bu, projenin mevcut deseniyle de tutarlıdır: kolon kataloğu, yetki ağacı ve menü nasıl tek
kaynaktan üretiliyorsa, mobil davranış da tek kaynaktan gelir.

## 4. Bulgular (analiz)

| # | Bulgu | Nerede | Telefonda ne oluyordu |
|---|---|---|---|
| 1 | Menü `DrawerVariant.Persistent` | `MainLayout.razor` | Menü açıkken içeriği **yana itiyordu**: 375 px ekranda 240 px menü → içeriğe ~135 px kalıyor, ekran kullanılamaz |
| 2 | Üst barda 6+ öğe, ikisi sabit genişlikte | firma seçici `min-width:210px`, arama `min-width:180px` | Bu ikisi tek başına ekranı dolduruyor; bildirim, araçlar, kullanıcı ve **çıkış düğmesi ekran dışında** kalıyordu |
| 3 | Arama sonuç paneli `width:380px; right:0` | `MainLayout.razor` | 375 px ekranda kenardan taşıp **sayfayı yana kaydırıyordu** |
| 4 | 40 dosyada 102 tablo, **hiçbirinde yatay kaydırma yok** | `Components/Pages/*` | Kolonlar okunmaz hâle sıkışıyor ya da sayfa yana kayıyordu |
| 5 | Filtre alanlarında satır içi `min-width:110–200px` | `WorkOrders.razor` ve benzerleri | Yan yana 4 filtre ≈ 600 px → taşma |
| 6 | 351 yerde `Size.Small` | proje geneli | Dokunma hedefi ~30 px; parmakla ıskalanıyor (eşik 44 px) |
| 7 | Mevcut 3 kırılım noktası **yalnız kozmetik** | `app.css` | 600 px altı için toplam 4 satır kural vardı; menü/tablo/form hiç ele alınmamıştı |
| 8 | `.dw-col-grip` (kolon genişletme tutamağı) `touch-action:none` | `app.css` | Dokunmatikte hem hedeflenemiyor hem o bölgede parmakla kaydırmayı engelliyordu |

**Sorun ÇIKMAYAN yerler:** viewport etiketi zaten doğru (`App.razor`), giriş ekranı zaten duyarlı
(900 px'te marka paneli gizleniyor, 480 px'te kart daralıyor) → **dokunulmadı**.

## 5. Yapılanlar

**Tek dosyada, tek katman:** `app.css` §18 (MOB-W). Ayrıca yalnız iki ortak dosyaya dokunuldu:
`MainLayout.razor` (menü türü + aramanın ikinci kopyası). **Hiçbir ekran sayfası değiştirilmedi.**

| Bölüm | Ne yapıldı |
|---|---|
| 18.0/18.3 | **Global arama telefonda menüye taşındı** — üst bardaki kopya gizlenir, menüdeki (215 px, tam kullanılabilir) görünür. Logo/başlık gizlenir, firma seçici 92 px'e iner, "Çıkış" yalnız ikon olur. Üst bar artık **375/375 px, taşma yok** |
| 18.1 | Tablolar **kendi içinde** yatay kayar (`:has()` ile sarmalayıcı hedeflendi — sınıfı yoktu). Kaydırma çubuğu telefonda **görünür** yapıldı ki kullanıcı devam ettiğini anlasın |
| 18.2 | Filtre alanlarının satır içi `min-width`'leri sıfırlanır, alt alta iner |
| 18.5 | Dokunma hedefleri 40–44 px (`pointer: coarse` ile — ekran genişliğine göre değil, **girdi türüne** göre) · dokunmatikte kolon tutamağı devre dışı |
| 18.6 | Güvenlik ağı: gövde asla yana kaymaz; gözden kaçan geniş öğe kendi kabında kalır |
| 18.7/18.8 | Dialoglar neredeyse tam ekran · sekme şeridi 36 px'e iner, "Yeni Sekme" sağ kenara **yapıştırılır** (çok sekmede ekran dışında kalıyordu) |

### Uygulama sırasında bulunup düzeltilenler
- **`MudHidden` geri alındı.** Aramanın iki kopyası önce `MudHidden` ile ayrılmıştı; denemede
  **geniş ekranda arama kutusunu tamamen kaybettirdi** (kırılım bilgisini JavaScript'ten alıyor,
  güncellenmeyince "gizli" varsayıyor). Görünürlük CSS medya sorgusuna alındı — tarayıcının kendi
  ölçüsüdür, şaşmaz. `MOB4` bu geri dönüşü yasaklıyor.
- **Arama kutusunu "dokununca açılan" 44 px'lik kutuya çevirme denemesi terk edildi**: `flex`
  kısayolu içindeki `min()` bazı motorlarda tüm bildirimi geçersiz kılıyor; ayrıca 44 px'lik bir
  kutuya yazmak zaten kullanışlı değil.
- Bildirim rozeti yanındaki düğmenin üstüne biniyordu → telefonda küçültülüp içeri çekildi.
- `.dw-grid-wrap` diye bir sınıf **yok**; sarmalayıcılar satır içi stille yazılmış → `:has()` ile
  yapıya göre hedeflendi.

## 6. Doğrulama

**Genişlik taraması — 11 ölçü:** 320 · 375 · 600 · 768 · 960 · 1000 · 1100 · 1101 · 1280 · 1440 ·
1920 px. **Hiçbirinde üst bar taşması ve sayfa yatay kayması yok.**

> ⚠ Tarama sırasında **MOB-W'den önce de var olan bir kusur** bulundu: tam masaüstü üst barının
> öğe toplamı ~1060 px'tir, yani **1000 px genişliğindeki bir tarayıcı penceresinde zaten taşıyordu**.
> Üst bar sınırı bu yüzden 960 değil **1100 px** seçildi (ölçülen asgarinin güvenli üstü).
> İlk denemede sınır 600 px'ti ve 601–960 px arasında bozuk bir aralık kalıyordu (768 px tablette
> görüldü): menü çoktan üste binen katmana dönüyor ama üst bar geniş ekran ölçüleriyle çiziliyordu.

**Canlı ölçüm (izole QA sunucusu, 375×812 telefon):** 8 ekranda (`/materials`, `/vehicles`,
`/daily`, `/permissions`, `/work-orders`, `/invoices`, `/reports`, `/screen-visibility`)
**sayfa yatay kayması YOK** ve **gerçek taşma YOK**. Bu ölçüm §18.6 güvenlik ağı GEÇİCİ OLARAK
KAPATILARAK yapıldı — aksi hâlde ağ gerçek taşmaları gizler ve test kendini kandırırdı.

- Üst bar: 375/375 px, taşma yok · menü içeriğin üstüne açılıyor ve seçimden sonra kapanıyor
- Tablo: 1120 px içerik, 321 px kapta **kendi içinde** kayıyor
- Geniş ekran (1440 px) **birebir eskisi gibi**: logo, başlık, 210 px firma seçici, "Çıkış" yazısı,
  42 px sekme şeridi, üst barda arama — hiçbiri değişmedi

**Test:** `MobilWebTests` (MOB1–MOB6) — en kritiği **MOB3**: mobil katmandaki her kuralın medya
sorgusu İÇİNDE olduğunu süslü parantez derinliği sayarak doğrular. Bu, "telefonu düzeltirken
bilgisayarı bozma" riskini kalıcı olarak kapatır.

**İzolasyon:** tüm denemeler `artifacts/qa-data` altındaki ayrı veritabanıyla yapıldı
(`.claude/launch.json` → `api-qa`/`web-qa`). Geliştiricinin kendi yerel verisine ve **üretime
hiç dokunulmadı**.
