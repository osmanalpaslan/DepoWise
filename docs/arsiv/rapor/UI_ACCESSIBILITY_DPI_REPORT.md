# DepoWise — Erişilebilirlik & DPI Dayanıklılık Raporu (Faz 9)

**Tarih:** 2026-06-27 · **Kapsam:** Yeni özellik yok; mevcut modern arayüzün dayanıklılık/erişilebilirlik sertleştirmesi.
**Kısıt:** COMODO nedeniyle asistan imzasız EXE'yi çalıştıramaz → runtime/DPI/görsel doğrulama **kullanıcı** tarafından
(dotnet host) yapılır. Aşağıda kod düzeyinde tespit + uygulanan düzeltmeler + kullanıcı smoke listesi.

## 1–2. Ekran/ölçek & layout dayanıklılığı (kullanıcı doğrulayacak)
Tasarım, ölçek bağımsızlığı için **göreli/esnek** kurgulandı; sabit piksel yerleşimden kaçınıldı:
- **Pencere:** `MinWidth=900, MinHeight=560` → küçük ekranda kırılmaz; `Width/Height=1180×720` başlangıç.
- **İçerik:** modüller `Grid` + `*`/`Auto` + `WrapPanel` (KPI/aksiyon) + `ScrollViewer` → %125/%150'de dikey/yatay scroll devreye girer; sabit yükseklik yığını yok.
- **Tablolar:** `ListBox.Table` + `ScrollViewer.Horizontal/Vertical=Auto` + kolon `MinWidth` → daralınca yatay scroll, içerik kaybolmaz.
- **Metin kesilmesi:** uzun alanlarda `TextTrimming=CharacterEllipsis` + `ToolTip.Tip` (tam metin) — menü grupları, sayfa başlığı/bağlam, tablo Ad/Tür/Açıklama, kullanıcı adı (`MaxWidth=160`).
- **Diyaloglar:** onay panelleri pencere içi (`Border.Dialog` `MaxWidth`, ortalı) → ekran dışına taşmaz; ayrı OS penceresi yok.
- **Kartlar:** KPI kart `MinWidth=188` + `WrapPanel` → aşırı daralmadan alt satıra geçer.

**Kullanıcı smoke (1366×768/%100, 1920×1080/%100-%125-%150, mümkünse 2560×1440/%150):** her modülde metin kesilmesi/buton taşması/menü kayması/kart daralması/dialog konumu/tablo scroll kontrolü → `docs/ui-evidence/phase-09-hardening/`.

## 3. Klavye
- **Tab/Shift+Tab:** doğal XAML sırası; formlarda kritik alanlara açık `TabIndex` (Malzeme/Araç yeni kayıt).
- **Enter/Escape:** yeni kayıt panellerinde `KeyBinding` (Enter=Kaydet, Escape=İptal); kapsam panele bağlı (global çakışma yok).
- **Menü/dialog odağı:** menü düğmeleri Button/ToggleButton (Tab ile gezilebilir); onay panelleri içerik akışında.

## 4. Yalnız-ikon butonlar → tooltip + erişilebilir ad (UYGULANDI)
- İkon rayı (Ana Ekran 🏠 + modül grupları), üst bar ☰ → `ToolTip.Tip` **ve** `AutomationProperties.Name` eklendi (ekran okuyucu adı). Avatar rozeti `ToolTip.Tip=DisplayName` (etkileşimsiz Border).

## 5. Focus göstergesi (UYGULANDI)
- Koyu temada belirgin **beyaz 2px halka**: tüm sınıf butonlarında (`Primary/Secondary/Ghost/Danger/Icon`) `:focus-visible`.
- Giriş alanlarında `:focus-visible` → accent kenar **2px**'e kalınlaştırıldı. Menü (NavLink/NavRail) focus-visible zaten vardı (korundu).

## 6. Durum yalnız renkle değil (DOĞRULANDI)
- Tüm durumlar **metin + badge**: stok (Düşük/Yeterli), araç (Aktif/Pasif/Bakımda), bakım (Gecikti/Kritik/Yaklaşıyor/Güncel), talep (Beklemede/Onaylı/Reddedildi/...). Grafiklerde dilim/eksen **metin etiketi** (renk tek gösterge değil).

## 7. Kontrast (DÜZELTİLDİ)
- `TextMuted` **#7C8696 → #929DAD** (koyu zeminde ~4.5:1 sınırından ~5.6:1'e; yardımcı/helper metinler). `TextSecondary #AEB7C4` (~7:1) ve `TextPrimary #F5F7FA` zaten AA üstü.

## 8. Türkçe karakter & uzun etiket (DOĞRULANDI)
- Sistem fontu `Segoe UI Variable, Segoe UI, Aptos, Inter` tam Türkçe kapsar (İ/ı/ş/ç/ğ/ü/ö). Uzun etiketler ellipsis+tooltip; başlıklar/menü `TextTrimming`.

## 9. Görsel ağaç sadeleştirme
- Mevcut ağaç zaten ölçülü (sınıf-tabanlı opt-in stiller, tekrar eden converter yok — durumlar pseudo-class/`StringConverters` built-in ile). Bu fazda **riskli refactor yapılmadı** (kural #13: yalnız tespit edilen sorunlar). Gereksiz nested panel tespit edilmedi.

## 10. UI thread'de bekleme
- **Dosya/ağ:** yok. **DB:** modül VM'leri açılışta **senkron** yerel SQLite okur (WAL, hızlı, ms düzeyi) — mevcut mimari deseni. Yeni bir bloklama eklenmedi. **Not (gelecek iyileştirme, bu fazın kapsamı dışı/riskli):** navigasyon yüklemeleri async'e taşınabilir; şu an yerel ve hızlı olduğundan donma gözlenmedi.

## 11. Memory leak / abonelik
- Desktop'ta **event aboneliği / static event yok** (grep ile doğrulandı: `+=`/`static event` bulunamadı). Navigasyon her modülde yeni VM üretir, eskisi GC'ye bırakılır. Grafikler statik koleksiyona bağlı, timer/abonelik yok → View kapanışında elle temizlik gerekmez. **Leak riski tespit edilmedi.**

## 12. Grafik animasyonu / büyük liste
- Grafik animasyonu **kapalı** (`EasingFunction=null`); bar **MaxBars=20**; listeler `Limit=200` + sanallaştıran `ListBox`. Ek optimizasyon gerekmedi.

## 13. Kapsam
- Yalnız tespit edilen erişilebilirlik/kontrast/odak/ad sorunları düzeltildi; **yeni özellik eklenmedi**.

## 15. Build / test
- `dotnet build DepoWise.sln` → **0 hata** · `dotnet test` → **191/191**. Ana akış smoke (liste/arama/form/validation/grafik) kod düzeyinde korunur; görsel/DPI smoke kullanıcı.

## Uygulanan dosya değişiklikleri
- `Themes/Palette.axaml` (TextMuted kontrast), `Themes/Components.axaml` (focus-visible halka), `Views/MainWindow.axaml` (AutomationProperties.Name).
