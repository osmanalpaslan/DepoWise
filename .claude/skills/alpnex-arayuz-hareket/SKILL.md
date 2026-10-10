---
name: alpnex-arayuz-hareket
description: Alpnex arayüzünde hareket/animasyon (geçiş, açılma, vurgulama, yükleniyor durumu) eklerken veya düzeltirken kullanılır. MudBlazor (web) ve Avalonia (masaüstü) için somut kurallar, süre/eğri değerleri ve yapılmayacaklar listesi içerir. Yeni ekran, dialog, sekme, bildirim veya liste yenileme davranışı tasarlanırken çağrılır.
---

# Alpnex arayüz hareketi

Alpnex bir **veri girişi ERP'sidir**: depo görevlisi, şantiye şefi ve muhasebeci gün boyu
aynı ekranlarda yüzlerce kayıt girer. Buradaki hareket **süs değil, geri bildirimdir**.
Ölçüt tektir: *kullanıcı ne olduğunu anlamakta zorlanıyorsa hareket ekle; zaten anlıyorsa ekleme.*

> ⚠️ Bu projede **React / Framer Motion / GSAP yoktur.** İnternetteki animasyon örneklerinin
> çoğu bu kütüphaneler içindir ve buraya uymaz. Yığın: **MudBlazor 9.6 (web, Blazor Server)** +
> **Avalonia 12 (masaüstü, XAML)**.

## Karar sırası — önce bunu sor

1. **Zaten var olan bileşen bunu yapıyor mu?** MudBlazor'da `MudDialog`, `MudExpansionPanel`,
   `MudSnackbar`, `MudMenu`, `MudTabs` kendi geçişlerini getirir. Avalonia'da `Expander`,
   `Flyout`, `TabControl` aynı şekilde. **Elle animasyon yazmadan önce bileşenin kendi
   davranışını kullan.**
2. **Hareket bir soruyu cevaplıyor mu?** Geçerli üç soru: *"tıklamam işledi mi?"*,
   *"bu değer neden değişti?"*, *"sistem çalışıyor mu yoksa dondu mu?"*. Bu üçünden birine
   cevap vermiyorsa animasyon **eklenmez**.
3. **Blazor Server gecikmesi** — web tarafında her etkileşim sunucuya gider. Kullanıcının
   gördüğü gecikme ağdan gelir; üstüne 300 ms animasyon koymak yavaşlık hissini **artırır**.

## Süre ve eğri (her iki platformda aynı)

| Amaç | Süre | Eğri |
|---|---|---|
| Vurgulama / hover / odak | 120 ms | ease-out |
| Açılır panel, sekme geçişi | 180 ms | ease-out |
| Dialog / modal giriş | 200 ms | ease-out |
| Çıkış (kapanma) | girişin ~%70'i | ease-in |
| Kaydedildi/hata vurgusu | 400 ms sönümlenen | ease-out |

Kural: **girişler hızlı biter (ease-out), çıkışlar hızlı başlar (ease-in).** 250 ms'yi aşan
hiçbir arayüz geçişi olmamalı — 300 ms üstü bu üründe "takıldı" olarak algılanır.

## Yapılmayacaklar (bu üründe sert kurallar)

- **Grid/liste satırlarına animasyon yok.** Malzemeler 2.500+, araç 167, stok hareketi 700+
  satır. Satır bazlı geçiş büyük listede kekemeleşir. Yenilenen listede yalnız **değişen tek
  satır** kısa bir arka plan vurgusu alabilir; tüm liste asla.
- **Sayfa/ekran geçişinde tam ekran animasyon yok.** Sekme sistemi zaten bağlam koruyor.
- **Kaydırma (scroll) tetikli efekt yok.** Bu bir pazarlama sayfası değil.
- **Sonsuz dönen ikon yalnız gerçekten beklenirken.** 400 ms'den kısa işlemde spinner
  gösterme — titreşim etkisi yapar, daha yavaş hissettirir.
- **Renk tek başına anlam taşımaz.** Hareket veya renkle verilen her bilgi metinle de verilir
  (Alpnex'te yaşlı/deneyimsiz kullanıcı var).

## Web (MudBlazor / Blazor Server)

- Geçişleri **CSS ile** yaz, JavaScript ile değil (Blazor Server'da JS interop her seferinde
  ağ turu demektir).
- Tema değişkenlerini kullan; sabit renk gömme. Koyu/açık tema ikisinde de test et.
- Zorunlu erişilebilirlik bloğu — **her yeni animasyonlu CSS sınıfında**:

```css
.alpnex-vurgu { transition: background-color 180ms ease-out; }

@media (prefers-reduced-motion: reduce) {
  .alpnex-vurgu { transition: none; }
}
```

- `MudSnackbar` bildirim için hazır ve doğru araçtır; elle "toast" yazma.

## Masaüstü (Avalonia 12 / XAML)

- Projede stil dili **`Classes="..."`** üzerinden yürüyor (örn. `Classes="aktifSekme"`,
  `Classes="Primary"`, `Classes="Ghost"`). Yeni hareket de **Classes tabanlı stil** olarak
  yazılır, code-behind'da değil (`.claude/rules/desktop.md`: code-behind iş kuralı içermez).
- Basit geçişler için `Transitions`, tek seferlik olanlar için `Animation`:

```xml
<Style Selector="Border.kartVurgu">
  <Setter Property="Transitions">
    <Transitions>
      <BrushTransition Property="Background" Duration="0:0:0.18"/>
    </Transitions>
  </Setter>
</Style>
```

- **UI thread'i bloklama.** Animasyon sırasında DB/ağ çağrısı yapılmaz; veri önce `async`
  gelir, sonra görsel değişir.
- Avalonia'da `RenderTransform` (opacity/scale/translate) ucuzdur; `Width`/`Height`/`Margin`
  animasyonu **pahalıdır** — düzen yeniden hesaplanır. Boyut yerine dönüşüm kullan.

## İki platform paritesi

`.claude/rules/platform-priority.md` gereği: **önce masaüstü** yapılır ve test edilir, **web
hemen ardından tamamlanır**. Aynı etkileşim iki tarafta *aynı anlamı* taşımalı — piksel
eşitliği aranmaz, ama "kaydedildi" geri bildirimi bir tarafta varsa diğerinde de olmalı.

## Bitmeden kontrol et

- [ ] Süre 250 ms altında mı?
- [ ] `prefers-reduced-motion` (web) karşılığı var mı?
- [ ] Büyük listede satır bazlı animasyon eklenmedi, değil mi?
- [ ] Bilgi yalnız hareketle değil, metinle de veriliyor mu?
- [ ] Masaüstü + web ikisi de yapıldı mı?
