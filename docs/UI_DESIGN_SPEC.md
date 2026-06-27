# DepoWise UI Tasarım Spesifikasyonu (Hedef koyu tema)

**Referanslar:** `Tasarım Paketi/Referanslar/DepoWise-Hedef.png` (2816×1536, hedef) ve `DepoWise-Mevcut.png` (1181×748, mevcut).
Tüm renkler **merkezi tema kaynaklarında** (`Brand.*`) tutulur; ekranlara sabit yazılmaz. Ürün adı her yerde **DepoWise**. **ALPDEP/ALPDEPO/ALPDEP-CLOUD kullanılmaz** (hedef mockup'taki "ALPDEP-CLOUD" yazısı taşınmaz).

## 1. Karar Tablosu (zorunlu)
| Konu | Karar | Gerekçe |
|---|---|---|
| **Wpf.Ui** | **Kullanılmaz** (bağımlılık değil). Avalonia native tema/stil uygulanır. | Proje Avalonia (kanıt: AUDIT §1); WPF assembly bağlanamaz. wpfui yalnız görsel referans (MIT). |
| **LiveCharts2** | Yalnız **anlamlı rapor/veri görselleştirmelerinde**; Avalonia paketi `LiveChartsCore.SkiaSharpView.Avalonia` (MIT). | Faz 0'da eklenmez; Raporlar fazında değerlendirilecek. KPI kartları grafik değildir. |
| **Lucide ikonları** | **Merkezi + minimum set**; yalnız kullanılan SVG'ler, ISC lisans notu korunarak. Avalonia'da `StreamGeometry`/`PathIcon` kaynak sözlüğü olarak. | Emoji yerine tutarlı vektör; tüm repo kopyalanmaz. |
| **Emoji ikonlar** | **Kaldırılır** → Lucide. | Kural: emoji kullanma. |
| **Tema** | Tek kaynak `ThemeTokens` + `ThemeApplier`/`Brand.*`. Yeni anahtar gerekiyorsa türetilir (Panel/Border/Hover gibi). | Merkezi renk yönetimi korunur. |

## 2. Hedef ↔ Mevcut farkları (ölçülebilir kurallar)
> Değerler 1280×800 baseline; px. Hedef mockup açılı olduğundan oranlar referanstan türetilmiştir.

### 2.1 Sol menü — çift katman (ikon rayı + açıklamalı menü)
- **Hedef:** ince **ikon rayı** (yalnız ikon) + **açıklamalı menü** kolonu (grup başlıkları + alt öğeler).
- **Mevcut:** tek geniş sidebar (ikon rayı yok).
- **Spec:** İkon rayı **56 px** (yalnız Lucide ikon, dikey hizalı) + açıklamalı menü **204 px** → toplam **260 px**. Açıklamalı menü daraltılabilir (rail-only) hedefe yakınlık için opsiyon.
- Grup satırı yüksekliği **40 px**; alt öğe **32 px**, sol girinti **16 px**.

### 2.2 Üst bar
- **Hedef:** ince, koyu üst bar; solda başlık/breadcrumb, sağda **Eşitle** + kullanıcı menüsü.
- **Spec:** yükseklik **56 px**, alt kenarlık `Brand.Border`. Başlık 15–16px SemiBold; breadcrumb/karşılama 12px, %60 opaklık. "Eşitle" düğmesi `Brand.Accent`, radius `Brand.CornerRadius`, padding 14×8.

### 2.3 İçerik boşlukları
- Dış padding **24 px**; bölümler arası dikey boşluk **20 px**; kart arası **16 px**.

### 2.4 Kartlar (KPI)
- **Hedef:** kompakt, doygun **mavi** istatistik hücreleri; üstte değer, altta etiket; ikon küçük.
- **Spec:** kart **min 180×96 px**, köşe yarıçapı **`Brand.CornerRadius` (10)**, arka plan `Brand.Accent`, metin beyaz. Değer **28–30px Bold**, etiket **12px** %90 opak. Vurgu: dolu mavi yüzey (gölge yerine düz).
- Boş veride değer "0" gösterilir (sahte veri yok).

### 2.5 Yazı hiyerarşisi
- Sayfa başlığı 17px SemiBold (`Brand.OnSurface`).
- Bölüm etiketi (KRİTİK UYARILAR/GENEL ÖZET) 12px Bold, harf aralığı geniş, %60 opak.
- Gövde 13px; ikincil 11–12px %60 opak.
- Font: Inter (mevcut `WithInterFont`).

### 2.6 Uyarı renkleri (kritik uyarılar)
- **Gecikti/Bitti (expired):** `Brand.Danger` (#DC2626).
- **Kritik (95–100):** turuncu (#EA580C) — gerekirse `Brand.Warning` tonu.
- **Yaklaşıyor (85–95):** `Brand.Warning` (amber #F59E0B).
- Bar: tam genişlik, radius 8, sol ikon (Lucide alert-triangle), beyaz metin.

### 2.7 Seçili menü & hover
- **Seçili (active):** sol **3px `Brand.Accent` vurgu çubuğu** + hafif `#22FFFFFF` zemin + ikon `Brand.Accent`.
- **Hover:** `#16FFFFFF` zemin geçişi (150ms).
- **Açık grup:** chevron 90° döner (mevcut davranış korunur).

### 2.8 Empty-state
- Liste/panel boşsa: ortalı Lucide ikon (ör. inbox) + tek satır açıklama + (varsa) birincil eylem. Sahte veri YOK.
- Mevcut "Aktif kritik uyarı yok ✓" empty-state'i ikon + metin olarak standartlaştırılacak.

## 3. Token eşlemesi (mevcut → hedef)
Mevcut palet hedefe uygun (koyu). İnce ayarlar Faz'larda:
- `Brand.Panel` (kart/panel yüzeyi), `Brand.Border`, `Brand.Hover` korunur.
- Gerekirse `Brand.IconRail` (rayın hafif farklı koyusu) türetilir — sabit yazılmaz, ThemeApplier'da.

## 4. Erişilebilirlik / kalite
- Klavye ile menü gezinme + görünür odak göstergesi.
- Kontrast: koyu zeminde metin ≥ WCAG AA.
- Yüksek DPI: vektör (Lucide) ikon; bitmap ikon kaçınılır.
- UI thread'de ağır iş yok; veri çağrıları servis katmanında.

## 5. Kapsam dışı (Faz 0)
- View/VM/resource/paket değişikliği yok (Faz 0 salt okunur).
- LiveCharts/Lucide entegrasyonu ilgili fazda; şimdi yalnız karar.

---

## 6. Tasarım Sistemi — uygulanan altyapı (Faz 2)
Avalonia ayrı tema kaynakları olarak eklendi; App.axaml'e merge edildi. **Renkler yalnız bu kaynaklarda; ekranlara sabit yazılmaz.** Mevcut çalışma-zamanı `Brand.*` (ThemeApplier) sistemi **korundu**; bu yeni semantik katman onunla **çakışmaz** (ayrı anahtarlar).

### Dosyalar
- `src/DepoWise.Desktop/Themes/Palette.axaml` — semantik renk token'ları (Color + Brush).
- `src/DepoWise.Desktop/Themes/Scales.axaml` — boşluk, köşe, tipografi boyutları, font, elevation.
- `src/DepoWise.Desktop/Themes/Controls.axaml` — **sınıf-tabanlı** temel stiller (opt-in).

### Semantik renk token'ları (koyu palet)
| Token (Brush) | Hex | Kullanım |
|---|---|---|
| AppBackgroundBrush | #12161E | Uygulama zemini |
| SidebarBackgroundBrush | #1B212C | Sol menü |
| TopBarBackgroundBrush | #1B212C | Üst bar |
| SurfaceBrush | #282F3A | Kart yüzeyi |
| SurfaceElevatedBrush | #303743 | Yükseltilmiş yüzey |
| SurfaceHoverBrush | #2E3744 | Yüzey hover |
| BorderSubtleBrush | #394250 | İnce kenarlık |
| AccentBrush / AccentHoverBrush | #2F6FD5 / #3D7FE5 | Vurgu (buton/aktif) |
| TextPrimaryBrush | #F5F7FA | Ana metin |
| TextSecondaryBrush | #AEB7C4 | İkincil metin |
| TextMutedBrush | #7C8696 | Yardımcı metin |
| SuccessBrush / WarningBrush / DangerBrush | #2CBF6D / #D8A617 / #E24A3B | Durum |
| InfoBrush | #3AA0E0 | Bilgi |
| OverlayHoverBrush / OverlaySelectedBrush | #16FFFFFF / #22FFFFFF | Koyu yüzey hover/seçili |

### Ölçekler
- **Boşluk:** Space4/8/12/16/20/24/32 (+ PadCard=16, PadPage=24, PadTopBar=24,14).
- **Köşe:** Radius4/6/8/12.
- **Tipografi (boyut):** FontPageTitle=20, FontSectionTitle=13, FontCardLabel=12, FontMetric=28, FontBody=13, FontHelper=11.
- **Font:** `AppFontFamily = "Segoe UI Variable, Segoe UI, Aptos, Inter, sans-serif"` — **harici font dosyası eklenmedi**; sistem fontu + güvenli fallback.
- **Elevation:** ShadowSm/ShadowMd (hafif; ağır blur yok).

### Temel stiller (sınıf-tabanlı, opt-in)
- Tipografi: `TextBlock.PageTitle/.SectionTitle/.CardLabel/.Metric/.Body/.Helper`.
- `Button.Primary` (accent + hover), `Border.Card`, `Border.SurfaceElevated`, `Border.Divider`.
- **Hepsi `Classes` ile kapsanır** → mevcut kontroller (Classes'sız) etkilenmez; global `Button`/`TextBlock` seçici yok.

### İlişki: yeni semantik tokenlar ↔ Brand.* (geçiş planı)
- Şu an ekranlar `Brand.*` kullanıyor (eski palet, çalışmaya devam). Yeni semantik tokenlar gelecek faz ekranlarında kullanılacak; ilerleyen fazda `Brand.*` → semantik tokenlara köprülenip tek kaynağa indirgenecek (bu faz kapsamı dışı).

### Tema önizleme view'ı (Faz 2 kararı)
- **Eklenmedi.** Gerekçe: mevcut yapı ayrı bir "geliştirici önizleme" barınağı içermiyor; bir önizleme ya üretim navigasyonuna sahte ekran ekler (yasak) ya da ölü kod olur. Token'lar build ile doğrulandı; görsel doğrulama ilgili ekran fazında yapılacak.
