# 08 — Avalonia masaüstü arayüz araştırması ve Alpnex tasarım sistemi önerisi (A5)

> Dal: `arge/2026-10-kapsamli` · Tarih: 2026-10-12/13 · Kapsam: yalnız araştırma + öneri. **Kod, paket, proje değişikliği YOK.**
> Kaynaklar bu belgeye özgü `[T-nn]` kodlarıyla en sonda listelenir (erişim 2026-10-12/13). Doğrulanamayan bilgi **(doğrulanmadı)** diye işaretlidir.
> Kanıt biçimi: `dosya:satır` (kök: `src/DepoWise.Desktop/`) ve ekran görüntüsü adı (`artifacts/ui-ekran/…`, 2026-10-12 19:35 görsel tarama koşusu).
> Not: GitHub (`github.com`) bu ortamda açılamadı (DNS hatası), SAP Fiori sayfası 403 verdi; bu iki kaynaktan gelen bilgi ikincil/doğrulanmadı olarak işaretlendi.

---

## 0. Kısa özet (yönetici için)

1. **Mevcut tasarım altyapısı sağlam, sorun "kullanım tutarlılığında".** Renk/ölçek token'ları (`Themes/Palette.axaml`, `Themes/Scales.axaml`), ortak bileşenler (`ctrl:Toolbar` 51 ekranda, `ctrl:FormField` ~810 kullanım, `ctrl:StatePanel` 75 kullanım) ve ortak tablo deseni (`ListBox.Table`, 44 ekran) zaten var. Eksik olan: **sayfa şablonu** (liste/ayrıntı/form ekranlarının hep aynı iskeleti), **düğme yerleşim kuralı** ve **klavye/erişilebilirlik**.
2. **En büyük 5 sorun:** (a) her ekranda tüm düğmeler üst satırda yan yana, seçime göre değişmiyor; (b) aynı iş için 3 farklı sayfalama biçimi; (c) iki "birincil" (kehribar) düğme aynı satırda; (d) klavye kısayolu neredeyse yok (yalnız Ctrl+K), ekran okuyucu etiketi yalnız 4 yerde; (e) "yükleniyor" durumu ekranlarda hiç yok (`Mode="Loading"` yalnız galeride).
3. **Üçüncü taraf kütüphane önerisi: tam tema kütüphanesi BENİMSENMESİN.** Özel tasarım sistemi korunsun. Avalonia çekirdeği 12.0.4 → 12.1.x yükseltmesi ayrı, test edilmiş bir iş olarak planlansın; bu, ücretsiz (MIT) `DataGrid` ve çekirdekteki salt-okuma `TableView` kapılarını açar. Kullanılmayan `Semi.Avalonia` paket başvurusu kaldırılabilir.
4. Dönüşüm planı (bölüm 4) **küçük adımlı** ve geri alınabilir: önce ortak bileşenler (S/M), sonra ekran ekran şablona geçiş. Web (MudBlazor) tarafı aynı kuralları almalıdır (platform-öncelik kuralı).

---

## 1. Araştırma bulguları (kaynaklı)

### 1.1 Avalonia resmî belgeleri

| Konu | Bulgu | Bizim için anlamı | Kaynak |
|---|---|---|---|
| Sürüm durumu | Son kararlı çekirdek **12.1.4** (2026-10-09). Uygulama **12.0.4**'te (2026-05-28). 12.1.0 2026-07-09'da çıktı. | Ekosistem paketlerinin çoğu artık ≥12.1 istiyor (aşağıda). Yükseltme planlanmalı. | [T-01] |
| DataGrid (ücretsiz) | `Avalonia.Controls.DataGrid` 12.1.2, **MIT**, `Avalonia >= 12.1.0` ister (2026-08-03). | Mevcut çekirdekle kurulamaz (UI_DESIGN_SPEC §7.6'daki "12.0.5 ister" notunun güncel hâli). Çekirdek yükseltmesi şart. | [T-02], `docs/UI_DESIGN_SPEC.md` §7.6 |
| TreeDataGrid (ücretli) | Ekim 2025'ten beri Avalonia Accelerate kapsamında; belge sayfası "Avalonia Pro veya üstü" lisans anahtarı istiyor. Eski MIT sürümü (11.1.x) dondu. | **Kullanılmamalı** (ücret + bağımlılık). Mali tercih: minimum harcama. | [T-03], [T-04] |
| TableView (yeni, ücretsiz) | Avalonia **12.1**'den itibaren çekirdek `Avalonia.Controls` içinde; ek paket yok; **salt-okuma** (hücre düzenleme yok); `TableViewColumn` + `Binding`/`CellTemplate`. | Rapor tabloları ve salt-okuma listeler için gelecekte aday. Düzenleme gerektirmeyen ekranlar zaten "satırdan form/detay" mantığıyla çalışıyor → uyumlu. | [T-05] |
| Sanallaştırma (virtualization) | ListBox/TreeView/DataGrid varsayılan sanallaştırır; **sonsuz yükseklik veren kapsayıcıda (ör. StackPanel) sanallaştırma kapanır**. `VirtualizingStackPanel BufferFactor`, satırlara sabit `Height/MinHeight` önerilir. Avalonia 12'de derlenmiş bağlama (compiled binding) varsayılan. | Ana tablolar `Grid` satırı `*` + `DockPanel` doldurma alanında → sanallaştırma çalışıyor (`Views/MaterialsView.axaml:339-346,589`). Ama form içi küçük listeler `ScrollViewer` + `ItemsControl` (sanallaştırmasız) — küçük veri için sorun değil, büyüyen listelerde risk. | [T-06] |
| Tema varyantı | `ThemeDictionaries` (Light/Dark/Default), `RequestedThemeVariant`, `ThemeVariantScope`, özel adlı varyant tanımlanabilir. | Mevcut `Palette.axaml` doğru deseni kullanıyor. "Yüksek kontrast" veya "yoğun" varyant ileride aynı mekanizmayla eklenebilir. | [T-07] |
| Erişilebilirlik | `AutomationProperties.Name` en önemli özellik; yalnız ikonlu ya da metinsiz kontrollerde **zorunlu**; metinli Button/TextBlock kendi metnini kullanır. Avalonia, Windows'ta UI Automation'a eşler. | İkon düğmeleri (`Button.Icon`, `×`, `‹ ›`, takvim düğmesi) etiketsiz → ekran okuyucu "düğme" der, ne yaptığını söylemez. | [T-08] |
| Klavye kısayolları | `ICommandSource` kontrollerinde `HotKey`; `Window.KeyBindings` **öğeden bağımsız** genel kısayol; kontrol düzeyindeki `KeyBinding` o kontrol ve çocuklarında geçerli (ör. ListBox'ta `Delete`/`F2`). Gesture söz dizimi `Ctrl+Shift+N`, `F5`. Erişim tuşu (`_1`) kısayol kaydetmez. | Uygulamada pencere düzeyi yalnız Ctrl+K var (`Views/MainWindow.axaml.cs:19-31`); diğer 30 KeyBinding yalnız form/kutu içi Enter/Escape. | [T-09] |

### 1.2 Microsoft Fluent 2 / Windows tasarım rehberi

| Konu | Bulgu | Kaynak |
|---|---|---|
| Boşluk (spacing) | 4 px taban; rampa 0,2,4,6,8,10,12,16,20,24,28,32,36,40,48,52,56. Gruplamayı çizgiyle değil boşlukla yap; ilişkili öğeler sık, bölümler geniş aralıklı. Önemli içerik en büyük bölgede. | [T-10] |
| Tipografi | Windows rampası: Caption 12/16, Body 14/20, Body Strong 14/20 yarı kalın, Subtitle 20/28, Title 28/36. **Cümle düzeni (sentence case), BÜYÜK HARF kullanma.** Normal metin ≥4,5:1, büyük metin ≥3:1 kontrast. | [T-11] |
| Komut çubuğu (command bar) | Komutlar önem sırasıyla; dar pencerede taşanlar "…" taşma menüsüne (overflow) gider. Sayfalar arası sabit komut **hep aynı yerde** olmalı. Etiketler kısa, tercihen tek kelime. Geniş pencerede etiket ikonun sağında. | [T-12] |
| İletişim kutusu (dialog) | Her dialogda en az bir güvenli (yıkıcı olmayan) düğme. Düğme metni eylemi söylemeli ("Sil", "Vazgeç"; "Tamam" değil). Windows kuralı: "yap" düğmesi solda, güvenli düğme sağda; varsayılan düğme Enter'a yanıt verir, Esc güvenli düğmeyi çalıştırır. **Doğrulama hatası dialogla değil, alanın yanında satır içi gösterilir.** | [T-13] |

### 1.3 Veri yoğun iş uygulaması desenleri

| Desen | Bulgu | Kaynak |
|---|---|---|
| Tablo kullanıcı görevleri | 4 görev: kayıt bul (ara/filtrele/sırala), karşılaştır, tek satırı gör/düzenle/ekle, bir veya çok kayıtta eylem. Başlık (ve ilk kolon) sabit kalmalı; filtre etkinken açık gösterge; çok satır eylemi için onay kutusu + "tümünü seç"; satır içi çok düğme yığmaktan kaçın; **düzenleme için modal yerine modal olmayan yan panel** (komşu kayıtlar görünür kalır). | [T-14] |
| Liste raporu + nesne sayfası (list report / object page) | Kurumsal ERP'lerde standart iskelet: başlık → filtre çubuğu (+ kayıtlı görünüm / variant) → tablo araç çubuğu (seçime bağlı eylemler) → satırdan ayrıntı sayfasına geçiş. | [T-15] (sayfa 403 verdi — **doğrulanmadı**, genel bilgi) |

### 1.4 Kütüphaneler (lisans, bakım, Avalonia 12 uyumu, risk)

Uygulama: .NET 8 + Avalonia 12.0.4 (`DepoWise.Desktop.csproj`).

| Kütüphane | Son sürüm / tarih | Lisans | Avalonia / .NET şartı | Mevcut projeyle uyum | Değerlendirme | Kaynak |
|---|---|---|---|---|---|---|
| **FluentAvalonia** (FluentAvaloniaUI) | 3.1.0 · 2026-08-22 | MIT | Avalonia ≥12.1.0, **yalnız net10.0** | **Uyumsuz** (.NET 8 hedefi) | WinUI benzeri NavigationView, ContentDialog, CommandBar, InfoBar sunar; ama .NET 10'a geçiş + çekirdek yükseltmesi + tüm kabuğun yeniden yazımı gerekir. Getirisi düşük. | [T-16] |
| **Semi.Avalonia** | 12.1.0.1 · 2026-07-31 (projede 12.0.3 başvurulu) | MIT | ≥12.1.0 (son sürüm); net8+net10 | Paket var ama **kullanılmıyor**: "Semi (Modern)" seçeneği 2026-08-08'de kaldırıldı (`Theming/ThemeService.cs:45-47,68-69`). | Ölü bağımlılık → kaldırılabilir (S, düşük risk; test: tema açılışı). DataGrid teması ayrı paket (`Semi.Avalonia.DataGrid`). | [T-17] |
| **Ursa.Avalonia** (Irihi.Ursa) | 2.2.0 · 2026-07-31 | MIT | **≥12.0.2** · net8+net10 | Kurulabilir (tek kütüphane çekirdek yükseltmesi istemiyor). Görünüm için Semi teması ile eşleşmesi beklenir (`Semi.Ursa`) **(doğrulanmadı)**. | Zengin ek kontroller (çok seçimli kutu, sayfalama, form, dialog, tarih aralığı…). Ancak kendi tema dili Semi'dir → bizim Aurora/kehribar dilimize yeniden giydirme maliyeti. Yalnız tek bir eksik kontrol (ör. tarih aralığı) gerekirse **hedefli** değerlendirilir. | [T-18] |
| **SukiUI** | 7.0.1 kararlı (2026-05-19), gece sürümü 2026-10-02 | NuGet sayfasında yazmıyor (**doğrulanmadı**; GitHub'a erişilemedi) | ≥12.0.3 · net8 | Kurulabilir | Tam görünüm teması (kart/glassmorphism ağırlıklı, gösterişli). Veri yoğun, uzun süreli kullanım için fazla dekoratif; mevcut sistemi ezer. Lisansı netleşmeden kullanılmaz. | [T-19] |
| **Material.Avalonia** | 3.21.0 · 2026-09-25 | MIT | ≥12.1.1 · net8 | Çekirdek yükseltmesi gerekir | Material Design dili Windows iş uygulamasına ve web tarafına (MudBlazor da Material tabanlı, ama bizde özel Aurora teması var) uymaz; tüm ekranlar değişir. | [T-20] |
| **Avalonia DataGrid** | 12.1.2 · 2026-08-03 | MIT | ≥12.1.0 | Çekirdek yükseltmesi gerekir | Düzenlenebilir hücre, kolon yeniden boyutlandırma/sıralama hazır. Ama bizde bunların özel karşılıkları var (`Controls/KolonGenislik.cs` 336 satır, `Controls/TableHeaderSync.cs` 291, `Controls/HucreFiltre.cs`, `SortHeader.cs`, `Controls/DataGridView.axaml` + `GridController`). Geçiş maliyeti yüksek, kazanç sınırlı. | [T-02] |
| **TreeDataGrid** | Accelerate | Ticari | — | — | Kullanılmaz (ücret). | [T-03] [T-04] |
| **LiveCharts2** (`LiveChartsCore.SkiaSharpView.Avalonia` 2.0.5) | projede var | MIT | — | Kullanımda | Değişiklik önerilmez (UI_CHANGELOG Faz 8). | `docs/UI_CHANGELOG.md` |

> **Avalonia örnek uygulamaları / ControlCatalog / gerçek uygulamalar:** ControlCatalog ve vitrin (showcase) GitHub'da; bu ortamdan erişilemedi → karşılaştırma yapılmadı **(doğrulanmadı)**. Bu, öneriyi değiştirmez: önerilen desenler Fluent 2 + NN/g kaynaklarına dayanır.

---

## 2. Mevcut arayüzün değerlendirmesi (kanıtlı)

İncelenen görüntüler: `artifacts/ui-ekran/kiralik-liste.png`, `rapor-depo-cikislari.png`, `gorsel/malzeme-yeni-1126.png`, `gorsel/zimmet-1126.png`, `gorsel/depo-cikis-yeni-1126.png`, `gorsel/hiza-cari-1126.png`, `gorsel/sayim-1126.png` (+ aynı klasördeki 30 ekranın `bulgular.txt` özeti: hepsi "bulgu yok" = taşma/hiza testi geçiyor; bu değerlendirme taşma değil **düzen ve tutarlılık** üzerinedir). Görüntülerin tamamı **koyu temada**; açık tema görsel taraması yok (açık).

### 2.1 Güçlü yanlar (korunmalı)
- **Token disiplini:** renkler `DynamicResource` ile; Light/Dark `ThemeDictionaries` doğru kurulmuş (`Themes/Palette.axaml:13-130`). Menü aile renkleri kontrast testiyle ölçülüyor (`Palette.axaml:59-71`, `MenuRenkTests`).
- **Ortak bileşenler yaygın:** `ctrl:Toolbar` 51 view, `ctrl:FormField` ~810, `ctrl:LookupBox` ~108, `ctrl:TarihKutusu` 46, `ctrl:StatePanel` 75 kullanım (grep sayımı).
- **Tablo altyapısı olgun:** kolon başı filtre satırı, sıralama, sürükleyerek genişlik, kolon gizle/kaydet sağ tık menüsü (`Controls/KolonGenislik.cs:280-300`), ayrı başlık senkronu (`Controls/TableHeaderSync.cs`), yazıya tıklayınca satır seçme (`Themes/Components.axaml:424-428`).
- **Sanallaştırma doğru kurulmuş** ana tablolarda (`Views/MaterialsView.axaml:339-346,589`: `Grid` `*` satırı → `DockPanel` → `ListBox.Table`). Sayfalama + sayfa boyutu (25/50/100/200) kişisel tercih olarak saklanıyor (`ViewModels/DailyActivityViewModel.cs:92,155-158`).
- **Kabuk:** daraltılabilir açıklamalı menü (210 px, `Views/MainWindow.axaml:43-46`), menü içi arama, üst barda firma+şube bağlamı, eşitleme halkası, global arama (Ctrl+K), bildirim çanı, alt şeritte açık ekran sekmeleri + "Diğer Sayfalar" taşma menüsü (`MainWindow.axaml:496-740`). Bu yapı modern masaüstü uygulamalarıyla aynı düzeydedir.
- **Durum mesajları dürüst:** boş/hata panelleri var; çevrimdışı sohbet sahte liste göstermiyor (`MainWindow.axaml:868`); "Tüm Şubeler" modunda turuncu uyarı bandı (`gorsel/depo-cikis-yeni-1126.png`, `gorsel/sayim-1126.png`).

### 2.2 Sorunlar

| # | Alan | Bulgu | Kanıt | Etki |
|---|---|---|---|---|
| U1 | Düğme yerleşimi | Tüm eylemler başlık satırında yan yana, seçimden bağımsız: "Sayfa boyutu, Filtrele, Temizle, Excel'e Aktar, Yazdır, Sayaçları Onar, QR Etiketi, Yeni Kiralık Araç" = 7 düğme + 1 kutu. Seçime bağlı olan (QR Etiketi) yalnız sönük görünüyor. | `kiralik-liste.png`; `Views/MaterialsView.axaml:25-37` | Göz yorulur; hangi düğmenin seçili satıra uygulandığı belli değil. |
| U2 | İki birincil düğme | "Filtrele" ve "Yeni Kiralık Araç" ikisi de kehribar (Primary). | `kiralik-liste.png`; `MaterialsView.axaml:25` (`Filtrele` = `Classes="Primary"`) | Birincil eylem belirsizleşir. Fluent: sayfada tek vurgulu eylem. |
| U3 | Seçim eylemleri dağınık | Aynı tür eylem 3 farklı yerde: satır içinde (`Düzenle/İptal`, `depo-cikis-yeni-1126.png`), ayrıntı panelinin altında (`Düzenle/Sil`, `MaterialsView.axaml:738-741`), araç çubuğunda (`Yeni Cari / Düzenle / Hareket Ekle / Aktif-Pasif / Sil`, `hiza-cari-1126.png`). | ekran görüntüleri | Kullanıcı her ekranda "sil nerede?" diye arar. |
| U4 | Sayfalama 3 biçim | (a) numaralı düğmeler alt-solda (`kiralik-liste.png`, `MaterialsView.axaml:313-336`); (b) "◀ Önceki / Sonraki ▶ + Sayfa boyutu" tablonun içinde (`depo-cikis-yeni-1126.png`); (c) "Sayfa 1/1 · 0 cari ◀ ▶" araç çubuğunda (`hiza-cari-1126.png`). Sayfa boyutu bir ekranda başlıkta, diğerinde altta. | görüntüler | Öğrenme yükü; tutarsızlık. |
| U5 | Arama/filtre biçimleri | Cari'de çok satırlı yüksek arama kutusu + başlıkta şube onay kutuları (`hiza-cari-1126.png`); Zimmet'te "Ara / Yenile" düğmeli kutu (`zimmet-1126.png`); Malzemeler'de kolon filtresi + "Filtrele" düğmesi; Raporlar'da hem üst filtre paneli hem kolon filtresi (`rapor-depo-cikislari.png`). Etkin filtre göstergesi (kaç filtre açık) yok. | görüntüler | Aynı iş farklı yöntemle. |
| U6 | Form + liste aynı sayfada üst üste | Zimmet, Sayım, Giriş-Çıkış ekranlarında büyük form üstte sabit, liste aşağıda sıkışıyor (Giriş-Çıkış'ta tabloya ~40% kalıyor). | `zimmet-1126.png`, `sayim-1126.png`, `depo-cikis-yeni-1126.png` | Veri listesi küçük ekranda görünmez. |
| U7 | Form düğme sırası ve yeri tutarsız | Çoğu formda `İptal | Kaydet` sağda (`AnnouncementsView.axaml:60-61`, `BranchesView.axaml:54-55` …); Günlük Faaliyet'te `Kaydet | Vazgeç` (`DailyActivityView.axaml:98-99`); Zimmet'te tek "Kaydet" **solda** (`zimmet-1126.png`); Sayım'da sağda; Özel Raporlar'da Kaydet **Secondary** (`CustomReportsView.axaml:22`). Yalnız 16 yerde `IsDefault/IsCancel` (Enter/Esc) var. | grep + görüntüler | Kas hafızası oluşmaz; yanlış düğmeye basma riski. |
| U8 | Birincil düğme rengi kullanıcı aksanını izlemiyor | `Button.Primary` sabit kehribar gradyan (`#FBBF4A→#F0980D`) ve sabit `#241703` yazı (`Themes/Components.axaml:51-72`); oysa `AccentBrush` kullanıcı seçimiyle değişiyor (`Theming/ThemeService.cs:105`). Eşitle ikonunda da sabit renk (`MainWindow.axaml:322`). Toplam ~71 sabit renk satırı view'larda. | kod | Kullanıcı aksanı seçince arayüz yarı-yarıya değişir. |
| U9 | Tipografi | Bölüm başlıkları BÜYÜK HARF + harf aralıklı ("YENİ MALZEME", "ZİMMET GEÇMİŞİ"); Fluent büyük harf kullanılmamasını önerir [T-11]. Gövde 13 px (Windows önerisi 14). Yardımcı metin 11 px ve bazı yerlerde 11.5 (`Components.axaml:395`). Font karışık: token "Plus Jakarta Sans → Segoe UI Variable" (`Scales.axaml:185`, dosya gömülü değil), tablolar `fonts:Inter` (`Components.axaml:455,465,490`). 113 satırda doğrudan `FontSize=`. | kod + görüntüler | Okunabilirlik, özellikle yaşlı/az bilgisayar kullanan depo personeli için düşük; iki font karışımı. |
| U10 | Stil üst üste yazılmış | `ListBox.Table ListBoxItem` önce `MinHeight 40, Padding 12` sonra `48, 14` (`Components.axaml:430-456`); zebra önce açılıp sonra kapatılıyor (`:443-451`). | kod | Çalışıyor ama bakım zor; yoğunluk (density) ayarı eklemeyi zorlaştırır. |
| U11 | Boş alan yönetimi | Ayrıntı paneli seçim yokken de ekranın ~%25'ini kaplıyor, ortasında küçük yazı (`kiralik-liste.png`, `hiza-cari-1126.png`). Boş durum ikonu düz gri daire (yer tutucu) (`zimmet-1126.png`, `sayim-1126.png`). | görüntüler | Değerli tablo genişliği boşa gidiyor. |
| U12 | Yükleniyor durumu yok | `StatePanel Mode="Loading"` yalnız galeride (`ComponentGalleryView.axaml:120`); `IsLoading/IsBusy` yalnız 1 view'da; `Border.Skeleton` hiç kullanılmamış. | grep | Yavaş sorguda ekran "boş" görünür, kullanıcı tekrar tıklar. |
| U13 | Bildirim (toast) yok | `Border.Toast` stili var, altyapı yok (`UI_DESIGN_SPEC.md` §7.10); başarı mesajı ekran altındaki küçük `Status` yazısında (`MaterialsView.axaml:337-338`, `sayim-1126.png` sol alt "0 sayım düzeltmesi"). | kod + görüntü | "Kaydedildi mi?" belirsizliği. |
| U14 | Klavye | Genel kısayol yalnız Ctrl+K. Yeni (Ctrl+N), Kaydet (Ctrl+S), Yenile (F5), Ara (Ctrl+F), Sil (Del), Düzenle (F2/Enter), Kapat sekme (Ctrl+W) yok. `TabIndex` yalnız 15 yerde. Sağ tık menüsü yalnız kolon ayarı için (`KolonGenislik.cs:290`) — satır eylemleri sağ tıkta yok. | grep | Sık veri girişi yapan muhasebe/depo personeli fare bağımlı. |
| U15 | Ekran okuyucu | `AutomationProperties` yalnız 4 kez, hepsi `MainWindow.axaml` (215, 339, 402, 421). İkon düğmeleri (`×`, `‹`, `›`, `+`, takvim) etiketsiz. 342 `ToolTip.Tip` var ama ekran okuyucu adı değildir. | grep | Erişilebilirlik minimumunun altında. |
| U16 | Kontrast | Koyu tema: soluk metin 4,93:1, ikincil 5,91:1 (iyi). **Açık tema:** `WarningBrush #B4860F` beyazda 3,30:1, `SuccessBrush #12A85C` 3,10:1, `AccentBrush #C07C0C` 3,43:1 → küçük metin için 4,5:1 altında. Koyu `InfoBrush #6366F1` yüzeyde 3,84:1. Kenarlık `BorderSubtle` 1,25–1,27:1 (form alanı sınırı için WCAG 1.4.11 3:1 şartının altında). | hesap (WCAG bağıl parlaklık formülü) | Açık temada durum rozetleri ve vurgulu metin zor okunur; alan sınırları zor seçilir. |
| U17 | Yoğunluk (density) | Satır 48 px, form alanı ~40 px; tek yoğunluk. Muhasebe/rapor ekranında daha sık satır isteyen kullanıcı için seçenek yok. | `Components.axaml:452-463` | Büyük veride kaydırma artar. |
| U18 | Belge eskimiş | `docs/UI_DESIGN_SPEC.md` §6 tablosundaki renkler (#12161E, #2F6FD5 mavi aksan) güncel Aurora paletiyle (`Palette.axaml`: #0D1220, kehribar) uyuşmuyor; §2.1 ikon rayı 2026-08-19'da kaldırılmış (`MainWindow.axaml:38`). | belge vs kod | Yeni geliştirici yanlış referans alır. |

---

## 3. Önerilen Alpnex tasarım sistemi

İlke: **yeni sistem kurma, mevcut token ve kontrolleri sadeleştir + kurala bağla.** Anahtar adları değişmez (ekranlar bozulmaz); yeni anahtarlar ekleme yoluyla gelir.

### 3.1 Token'lar

**Renk** (`Themes/Palette.axaml` — mevcut anahtarlar korunur; düzeltme + ekleme):

| Token | Koyu | Açık | Değişiklik |
|---|---|---|---|
| `AccentBrush` | #E0951B | #C07C0C → **#9A6206** (≈5:1 beyazda) **veya** açık temada vurgulu metinde `AccentTextBrush` kullan | Açık temada metin olarak kullanılan aksan 4,5:1'i geçmeli |
| `AccentTextBrush` (yeni) | #F5B54A | #8A5A06 | Aksan renkli **yazı** için (bölüm başlıkları, bağlantılar) |
| `OnAccentBrush` (yeni) | #241703 | #241703 | Birincil düğme yazısı (sabit `#241703`'ü token'a taşır) |
| `PrimaryButtonBrush` (yeni, gradyan) | mevcut kehribar gradyan | aynı | `Button.Primary`'deki sabit gradyanı token'a taşır; `ApplyAccent` ister bunu da ezer (karar: ürün sahibi) |
| `SuccessBrush` / `WarningBrush` / `DangerBrush` / `InfoBrush` | aynı (Info: #818CF8'e açılabilir → ~5:1) | **#0F7A43 / #8A6508 / #B42318 / #4338CA** | Açık temada metin/rozet için ≥4,5:1 |
| `BorderStrongBrush` (yeni) | #3A4560 | #B8C0CE | Form alanı ve tablo dış sınırı için ≥3:1 (WCAG 1.4.11); `BorderSubtle` yalnız ayırıcı çizgide |
| `FocusRingBrush` (yeni) | #FFFFFF | #14171C | Klavye odağı halkası (mevcut "beyaz halka" açık temada görünmez) |
| `SuccessSoftBrush` … (yeni) | durum renginin %15'i | %12'si | `StatusBadge` zeminleri tek yerden |

> Ölçüm: değerler öneridir; uygulamada mevcut `MenuRenkTests` benzeri **kontrast testi** tüm metin/zemin çiftlerine genişletilir (otomatik, göz kararı değil).

**Tipografi** (`Themes/Scales.axaml`):

| Token | Şimdi | Öneri | Not |
|---|---|---|---|
| `AppFontFamily` | Plus Jakarta Sans (gömülü değil) → Segoe UI Variable | **Tek font:** `Inter` (zaten `Avalonia.Fonts.Inter` paketiyle gömülü) **veya** Segoe UI Variable | Tablolar zaten Inter; ikisini birleştir. Gömülü olan Inter her makinede aynı görünür → öneri Inter. |
| `FontCaption` (yeni) | 11 (`FontHelper`) | **12** | 11 px yaşlı kullanıcı için küçük |
| `FontBody` | 13 | **14** (Normal yoğunluk) / 13 (Sık) | Fluent Body 14/20 [T-11] |
| `FontBodyStrong` | — | 14 SemiBold | Alan etiketleri |
| `FontSectionTitle` | 13 Bold BÜYÜK HARF | **16 SemiBold, cümle düzeni** | "Yeni malzeme", "Zimmet geçmişi" |
| `FontPageTitle` | 20 | 20 (Subtitle) | aynı |
| `FontMetric` | 28 | 28 | aynı |
| `LineHeight*` (yeni) | — | 16/20/24/28 | Satır yüksekliği tutarlılığı |

**Boşluk** (mevcut `Space4…Space32` korunur, Fluent rampasıyla uyumlu): `Space2`, `Space6`, `Space40`, `Space48` eklenir. Kural: alan–etiket 4, alan–alan 12, grup–grup 24, sayfa kenarı 24 (`PadPage`), kart içi 16 (`PadCard`). Doğrudan `Margin="0,10,0,0"` gibi sayılar yeni kodda token'a bağlanır (758 `Margin=` satırı zamanla).

**Köşe yarıçapı:** mevcut ad–değer kayması düzeltilmeli: `Radius4`=6, `Radius8`=11 (ad ile değer farklı → kafa karıştırıcı). Öneri: anlamsal adlar eklenir — `RadiusControl`=6 (düğme/alan), `RadiusCard`=12 (panel/tablo), `RadiusDialog`=16, `RadiusPill`=999 (rozet/arama). Eski adlar geriye uyum için kalır.

**Yükselti (elevation):** `ShadowSm` (menü/flyout), `ShadowMd` (dialog/toast). Kartlar gölgesiz, kenarlıkla ayrılır (veri ekranında sakinlik).

**Yoğunluk (density) token'ları** (yeni, `ThemeDictionaries` benzeri ama ayrı sözlük: `Density.Normal.axaml` / `Density.Compact.axaml`, `ThemeService` ile değiştirilir):

| Token | Normal | Sık (compact) |
|---|---|---|
| `RowHeight` (tablo satırı) | 40 | 32 |
| `ControlHeight` (alan/düğme) | 36 | 30 |
| `TableCellPadding` | 12,0 | 8,0 |
| `FontBody` | 14 | 13 |

Varsayılan **Normal**; Sık yalnız kullanıcı seçerse (muhasebe/rapor). Satır yüksekliği sabit olduğu için sanallaştırma da iyileşir [T-06].

### 3.2 Bileşen kataloğu

Var olan → kullan; eksik → ekle. Hepsi `Controls/Components.cs` + `Themes/ComponentThemes.axaml` desenine uyar (yalnız sunum özelliği, iş mantığı yok — `Components.cs:9`).

| Bileşen | Durum | Tanım |
|---|---|---|
| **`ctrl:PageHeader`** (yeni; `Toolbar`'ın sadeleşmiş hâli) | eksik | Sol: sayfa başlığı + kayıt sayısı ("Malzemeler · 1.240 kayıt"). Sağ: **tek** birincil düğme ("Yeni malzeme") + "…" taşma menüsü (Excel'e aktar, Yazdır, Sayaçları onar, Ekran araçları). |
| **`ctrl:FilterBar`** (yeni) | kısmi (`Toolbar.FilterContent`) | Tek satır: arama kutusu (anında, 300 ms gecikmeli), en çok kullanılan 2–3 filtre (açılır), "Filtreler (n)" düğmesi → kalan filtreler açılır panelde; etkin filtreler **çip** (chip) olarak, her biri ✕ ile kaldırılır; "Temizle" bağlantısı. Kolon başı filtre satırı (`HucreFiltre`) kalır ama varsayılan **kapalı**, "Kolon filtreleri" anahtarıyla açılır. **Kayıtlı görünüm (saved view)**: filtre + kolon düzeni + sıralama adıyla kaydedilir (kolon ayarları zaten saklanıyor — `KolonGenislik.cs` "Kolon Ayarlarını Kaydet"). |
| **`ctrl:SelectionBar`** (yeni; bağlamsal komut çubuğu) | eksik | Tablonun hemen üstünde; seçim yokken gizli/silik, seçim varken "**2 kayıt seçili** · Düzenle · Zimmetle · QR etiketi · Pasife al · Sil · Seçimi kaldır". Tek seçimde tek-kayıt eylemleri, çoklu seçimde toplu (bulk) eylemler. Yıkıcı eylem (Sil/İptal) en sağda, ayraçla ayrılmış, kırmızı. |
| **`ctrl:DataTable`** (mevcut desenin şablonlaştırılması) | var (`Border.Table` + `ListBox.Table` + `DataGridView`/`GridController`) | Yeni ekranlar ve dönüştürülen ekranlar **`DataGridView` (GridController)** üzerinden; elle yazılmış başlık grid'i (`MaterialsView.axaml:346-588`, ~240 satır) azaltılır. İsteğe bağlı onay kutusu kolonu (çoklu seçim; şu an `SelectionMode="Multiple"` hiç yok). Sağ tık menüsü = SelectionBar eylemleri + "Kolonları ayarla". Sayılar sağa hizalı, tarih/kod sabit genişlik. Alt satır: toplam (rapor ekranındaki `TOPLAM` bandı deseni, `rapor-depo-cikislari.png`). |
| **`ctrl:Pager`** (yeni, tek biçim) | 3 farklı biçim | Tablonun **altında, solda** (uygulamada düzeltildi, M-64: kullanıcının 2026-07-19 "tüm tablolarda sol en altta" isteği önceliklidir): "1–50 / 1.240 · ‹ 1 2 3 … 25 › · Sayfa boyutu [50]". Tek bileşen, her ekranda aynı. |
| **`ctrl:DetailPane`** (master-detail) | kısmi (`Border.Panel` 360 px) | Seçim yokken **gizli** (tablo tam genişlik); seçilince sağdan açılır (360–480 px, sürükle-boyutlandır, genişlik hatırlanır). Üst: kayıt başlığı + durum rozeti; gövde: sekmeler (Bilgiler · Hareketler · Belgeler/Fotoğraflar · Geçmiş); alt: yalnız o kayda özgü 1–2 eylem. Esc kapatır. NN/g: modal yerine yan panel [T-14]. |
| **`ctrl:FormPage` / `FormSheet`** | kısmi (`ShowAdd` paneli) | Yeni/düzenle formu: (a) kısa formlar (≤8 alan) → sağ yan sayfa (sheet, 480–560 px), liste görünür kalır; (b) uzun formlar (Malzeme, Araç, Fatura) → tam sayfa, **üstte sabit başlık + altta sabit eylem çubuğu** ("İptal" · "Kaydet ve yeni" · "**Kaydet**"), bölümler sol kenarda küçük içindekiler listesiyle. İki kolonlu ızgara: ≥1100 px'de 2 kolon, altında 1 kolon. |
| **`ctrl:FormField`** | var | Aynen. Kural: etiket üstte, zorunlu `*`, yardım metni altta, hata metni yardım yerine kırmızı. `HasError` → alan kenarı `DangerBrush`. |
| **`ctrl:StatePanel`** | var | Modlar: Empty / Error / Loading + **yeni: Offline** ("Sunucuya bağlanılamıyor — yerel veriler gösteriliyor; değişiklikler bağlantı gelince gönderilecek") + **NoPermission**. Gerçek ikonlar (`Themes/Icons.axaml`'dan: inbox, alert-triangle, cloud-off, lock); gri daire yer tutucu kaldırılır. Empty iki türlü: "henüz kayıt yok" (birincil eylem düğmesiyle) ve "filtreye uyan kayıt yok" ("Filtreleri temizle" düğmesiyle). |
| **Yükleniyor** | stil var, kullanım yok | Liste ilk yüklemede **iskelet satırlar** (`Border.Skeleton`, 8 satır); yenilemede tablo üstünde ince belirsiz `ProgressBar.Loading`; 300 ms'den kısa işlerde hiçbir şey gösterilmez (titreme önlenir). |
| **`ctrl:InfoBar`** (sayfa içi uyarı bandı) | var ama elle (`"Tüm Şubeler" modundasınız…` bandı) | Bilgi/uyarı/hata/başarı türleri, isteğe bağlı eylem düğmesi ve kapatma; sayfa başlığının hemen altında. Çevrimdışı modda kabuk düzeyinde tek band. |
| **Toast / bildirim** | stil var, altyapı yok | Avalonia'nın yerleşik `WindowNotificationManager` (ek paket gerekmez) + `Border.Toast` görünümü; sağ alt, 4 sn, başarıda otomatik kapanır, hatada kalıcı + "Ayrıntı". Örnek: "Malzeme kaydedildi · Geri al" (geri al yalnız iş kuralı izin verirse). **(Not: `WindowNotificationManager`'ın 12.x API'si bu ortamda belgeden doğrulanmadı.)** |
| **Dialog** | var (`ConfirmWindow`, `ReasonWindow`, `ConfirmService`) | Tek şablon: başlık (soru), açıklama (sonuç), düğmeler eylem adlı ("Sil", "Vazgeç"), `IsDefault`/`IsCancel` **her dialogda zorunlu**. Yıkıcı onayda varsayılan (Enter) güvenli düğmedir. |
| **`ctrl:StatusBadge`** | var (21 kullanım) | Tüm durum metinleri (Aktif/Pasif/Güncel/Gecikti) bununla; renk + metin (yalnız renge dayanma). |
| **Düğmeler** | var (Primary/Secondary/Ghost/Danger/Icon) | `Button.Icon` için `AutomationProperties.Name` + `ToolTip` **zorunlu**. |

### 3.3 Sayfa şablonları

**A. Liste sayfası (List page)** — Malzemeler, Araçlar, Cari, Personel, Faturalar…
```
┌ PageHeader:  Malzemeler · 1.240 kayıt                     [Yeni malzeme] [⋯] ┐
├ FilterBar:   [🔍 Ara…] [Kategori ▾] [Depo ▾] [Filtreler (2)]  çip✕ çip✕  Temizle │
├ SelectionBar (seçim varsa): 3 seçili · Düzenle · QR etiketi · Zimmetle | Sil   │
├ DataTable (sanallaştırılmış, başlık sabit, toplam satırı)      │ DetailPane     │
│                                                              │ (seçimde açık) │
└ Pager: 1–50 / 1.240  ‹ 1 2 3 … ›  Sayfa boyutu [50]                            ┘
```
**B. Ana-ayrıntı (master-detail)** — Cari Hesaplar, Araç kartı, Talepler: A şablonu + DetailPane sekmeleri; çift tık / Enter → tam kayıt sayfası (sekmede açılır, alt şeritteki sekme sistemi zaten var).

**C. İşlem girişi (transaction entry)** — Giriş-Çıkış, Zimmet, Sayım, Yakıt dağıtım: üstte **başlık alanları** (tarih, depo, personel — tek satır), ortada **kalem tablosu** (satır ekle: barkod/kod yaz → Enter; satırı Del ile sil), altta sabit **özet + eylem çubuğu** ("3 kalem · toplam 52 adet" · İptal · **Kaydet**). Geçmiş listesi ayrı sekme/ekran ("Son işlemler") ya da sayfa altında daraltılabilir bölüm. Böylece form ile liste aynı anda büyük yer kaplamaz (U6).

**D. Rapor sayfası** — Raporlar: sol/üst filtre paneli daraltılabilir; "Sorgula" birincil; sonuç tablosu + toplam satırı + "Excel'e aktar" taşma menüsünde; açıklama metni (şimdi tablonun üstünde uzun paragraf) `ⓘ` ipucuna taşınır.

### 3.4 Etkileşim kuralları

1. **Sayfa başına tek birincil (kehribar) düğme.** Filtrele/Sorgula birincil değil — arama anında uygulanır; rapor ekranında "Sorgula" birincil olabilir çünkü "Yeni" yoktur.
2. **Düğme sırası (form/dialog alt çubuğu): `[İkincil/İptal] … [Birincil]` — birincil EN SAĞDA.** Gerekçe: kod tabanındaki baskın düzen bu (≈20 form, `ConfirmWindow.axaml:13-14`) ve web (MudBlazor) diyalogları da bu sırada; kullanıcı alışkanlığını bozmamak, Windows'un "yap düğmesi solda" önerisinden [T-13] daha değerli. **Bilinçli sapma** olarak ADR'ye yazılmalı. Aykırılar düzeltilir (`DailyActivityView.axaml:98-99`, Zimmet sol "Kaydet", `CustomReportsView.axaml:22`).
3. **Eylem yeri:** sayfa geneli → PageHeader; seçili kayda → SelectionBar + sağ tık + DetailPane altı (aynı komut, aynı ad); satır içine düğme **konmaz** (NN/g [T-14]; Giriş-Çıkış'taki satır içi `Düzenle/İptal` kaldırılır).
4. **Onay politikası** (`ConfirmService` mevcut kural korunur): yıkıcı/geri alınamaz (Sil, İptal kaydı, Pasife al, Toplu işlem) → dialog; dialog "N kayıt etkilenecek" der. Geri alınabilir işlemler (filtre, kolon, sekme kapat) → onaysız. Form içi kaydet → mevcut muaf liste. Toplu yıkıcı işlemde kayıt sayısı yazılmadan Enter ile onaylanmaz (varsayılan düğme "Vazgeç").
5. **Doğrulama:** satır içi (alanın altında), dialogla değil [T-13]; Kaydet'e basınca ilk hatalı alana odak.
6. **Klavye kısayolları** (pencere düzeyi `Window.KeyBindings` / `HotKey` [T-09]; metin kutusu odaktayken metin kısayollarıyla çakışmayanlar):

| Kısayol | Eylem | Kısayol | Eylem |
|---|---|---|---|
| Ctrl+K | Global arama (var) | Ctrl+F | Sayfa içi arama kutusuna odak |
| Ctrl+N | Yeni kayıt (sayfanın birincil eylemi) | Ctrl+S | Formu kaydet |
| Esc | Formu/paneli kapat (değişiklik varsa sor) | F5 | Yenile |
| Enter / F2 | Seçili kaydı aç / düzenle | Del | Seçili kaydı sil/iptal (onaylı) |
| Ctrl+A (tabloda) | Tümünü seç | Ctrl+E | Excel'e aktar |
| Ctrl+P | Yazdır | Ctrl+W / Ctrl+Tab | Sekmeyi kapat / sonraki sekme |
| Alt+1…9 | Alt şeritteki n. sekme | F1 | Ekran bilgisi (`ScreenInfoWindow` var) |

   Kısayollar düğme ipucunda gösterilir ("Kaydet (Ctrl+S)") ve "Klavye kısayolları" yardım penceresi (F1 içinde) eklenir.
7. **Odak sırası:** formlarda yukarıdan aşağı, soldan sağa doğal sıra; `TabIndex` yalnız istisnada. Form açılınca ilk alana odak; barkod alanlarında Enter → sonraki satır.

### 3.5 Erişilebilirlik minimumları (kabul ölçütü)

- Metin kontrastı ≥4,5:1, büyük metin ve ikon/kenarlık ≥3:1 — **iki temada**, otomatik testle (U16).
- Her `Button.Icon`, yalnız ikonlu düğme, `TextBox` (etiketsizse) için `AutomationProperties.Name`; form alanları `FormField` etiketiyle `AutomationProperties.LabeledBy` ya da `Name` [T-08]. Hedef: ikon düğmelerinde %100.
- Görünür odak halkası her etkileşimli öğede (şu an yalnız 2 seçicide `focus-visible`, `Components.axaml:719-723`).
- Renk tek başına anlam taşımaz: durum = renk + metin/ikon.
- Tıklama hedefi ≥ 28×28 px (Sık), ≥ 32×32 (Normal); `×` foto silme düğmesi 24×24 (`MaterialsView.axaml:253`) büyütülür.
- Uygulama Windows metin ölçeklemesi %125–150'de taşmasız (mevcut görsel tarama 940/1126 px genişlikte; ölçek testi eklenmeli).

---

## 4. Ekran ekran dönüşüm planı

Efor: S ≤1 gün · M 2–4 gün · L 1–2 hafta (masaüstü; web eşleniği ayrıca ~%60–80 ek). Risk: D düşük · O orta · Y yüksek. **Sıra önemlidir: önce ortak bileşenler (F0), sonra ekranlar.**

### F0 — Altyapı (ekran görünümü değişmeden)

| # | İş | Efor | Risk | Gerekçe |
|---|---|---|---|---|
| F0.1 | Token eklemeleri (AccentText, OnAccent, BorderStrong, FocusRing, açık tema durum renkleri) + kontrast testi | S | D | U8, U16; ekleme olduğu için ekran bozulmaz |
| F0.2 | `Button.Primary` sabit renk → token; `ListBox.Table` çift stilleri tek tanıma indir; font tekleştir (Inter) | S | O (görsel değişir → görsel tarama karşılaştırması) | U8, U9, U10 |
| F0.3 | `Pager`, `SelectionBar`, `PageHeader` (+ taşma menüsü), `FilterBar` (çipli), `InfoBar`, `StatePanel` Offline/NoPermission + gerçek ikonlar | M | D (yeni, opt-in) | U1–U5, U11 |
| F0.4 | Pencere düzeyi kısayol altyapısı: ekran VM'leri için ortak arayüz (`IHasPrimaryAction`, `IRefreshable`, `ISelectionActions`) + `MainWindow` `KeyBindings` | M | O (komut çakışması; metin kutusunda Del/Ctrl+A korunmalı) | U14 |
| F0.5 | Toast servisi (`WindowNotificationManager` + `Border.Toast`) ve `ConfirmService` ile birlikte kullanım | S–M | D | U13 |
| F0.6 | Yükleniyor durumu: ortak VM tabanında `IsLoading` + iskelet satır | M | D | U12 |
| F0.7 | Yoğunluk sözlükleri (Normal/Sık) + Ayarlar → Tema'da seçim | M | O (tüm tablolar etkilenir; görsel tarama şart) | U17 |
| F0.8 | `AutomationProperties.Name` taraması (ikon düğmeleri) + statik test ("ikon düğmesinde Name zorunlu", mevcut `MasaustuTabloHizaTests` gibi kaynak taramalı test) | S–M | D | U15 |
| F0.9 | `docs/UI_DESIGN_SPEC.md` güncel Aurora gerçeğine göre yeniden yazımı (bu belge temel alınarak) | S | D | U18 |

### F1+ — Ekranlar

| Ekran (view) | Şimdi | Dönüşüm | Efor | Risk |
|---|---|---|---|---|
| **Kabuk** (`MainWindow.axaml`) | Menü + üst bar + alt sekme şeridi; tek kısayol | Yapı **korunur**. Ekle: kısayol altyapısı (F0.4), çevrimdışı/eşitleme durumu için kabuk düzeyi `InfoBar` (üst bar altında, yalnız sorun varken), "Klavye kısayolları" yardımı. Eşitle düğmesindeki sabit ikon rengi token'a. | S–M | D |
| **Malzemeler** (`MaterialsView.axaml`, 854 satır) | 6 düğme + kolon filtre satırı + sağda sabit 360 px detay; ShowAdd ile tam form | PageHeader ("Yeni malzeme" + ⋯: Excel, Yazdır); FilterBar (ara + Kategori + Depo + çipler; kolon filtresi isteğe bağlı); SelectionBar (Düzenle, QR etiketi, Pasife al, Sil); DetailPane seçimde açılır, sekmeli (Bilgi · Stok/depo · Uyumlu araçlar · Fotoğraf); Pager altta. Form tam sayfa + sabit alt eylem çubuğu; "Uyumlu araçlar / Muadil" listeleri form içinde arama + çip seçimine dönüşür (şu an 160 px kutu, `MaterialsView.axaml:181-230`). Başlık grid'i `DataGridView`'a taşınır. | L | O (en çok kullanılan ekran; E2E + görsel tarama) |
| **Araçlar / Kiralık araçlar** (`VehiclesView.axaml`, 1009 satır) | 7 düğme + sayfa boyutu başlıkta (`kiralik-liste.png`) | Malzemeler şablonu. "Sayaçları onar" → ⋯ menüsü (bakım aracı, nadir). "Filtrele" kaldırılır (anında filtre). DetailPane sekmeleri: Bilgi · Sayaç · Bakım/Muayene · Zimmet · Belgeler. | L | O |
| **Malzeme Giriş-Çıkış** (`StockEntryView.axaml`) | Büyük form üstte, liste altta sıkışık, satır içi Düzenle/İptal (`depo-cikis-yeni-1126.png`) | Şablon C: başlık alanları tek satır; kalem tablosu (kod/barkod yaz → Enter ekle); alt sabit özet + Kaydet. Geçmiş hareketler ayrı sekme "Son hareketler" (SelectionBar: Düzenle · İptal kaydı). "Tüm Şubeler" uyarısı `InfoBar`. | L | Y (stok defteri; iş kuralı **değişmez**, yalnız yerleşim — idempotency/onay akışı korunmalı, kapsamlı test) |
| **Zimmet** (`AssignmentsView.axaml`) | Form üstte, "Kaydet" solda, "+ Listeye Ekle" ortada yüzüyor (`zimmet-1126.png`) | Şablon C (giriş-çıkışla aynı iskelet → öğrenme bir kez). Açık zimmetler listesi + sağda "Zimmet geçmişi" DetailPane (zaten benzer). Toplu teslim/iade = SelectionBar toplu eylemi. | M | O |
| **Stok Sayım** (`StockCountView.axaml`) | Form + liste (`sayim-1126.png`) | Şablon C; sayım satırlarında "sistem stoğu / sayılan / fark" kolonları; fark ≠ 0 satırları vurgulu; Kaydet öncesi özet dialog ("12 kalem, 3 farklı"). | M | O |
| **Cari Hesaplar** (`PartiesView.axaml`) | Çok satırlı arama kutusu, şube onay kutuları başlıkta, sayfalama araç çubuğunda, 5 eylem düğmesi (`hiza-cari-1126.png`) | Şablon B: FilterBar (tek satır arama + Tip + Şube çoklu seçimli açılır + "Yalnız aktif" anahtarı); SelectionBar (Düzenle · Hareket ekle · Aktif/Pasif · Sil); DetailPane sekmeleri: Kart · Hesap hareketleri · Bakiye. Pager altta. | M | D |
| **Raporlar** (`ReportsView.axaml`) | Üst filtre paneli + uzun açıklama + kolon filtresi + toplam bandı (`rapor-depo-cikislari.png`) | Şablon D: filtre paneli daraltılabilir; açıklama `ⓘ`; "Sorgula" birincil; "Excel'e aktar" ⋯ menüsü + Ctrl+E; kayıtlı görünüm (rapor + filtre seti adıyla). İleride (çekirdek 12.1 sonrası) salt-okuma `TableView` pilotu için en uygun ekran. | M | D |
| **Yakıt** (`FuelView.axaml`, 668) / **Bakım** (`MaintenanceView.axaml`, 654) | KPI + liste + çoklu form | Yakıt dağıtım = Şablon C; Bakım listesi = Şablon A/B (uyarı rozetleri); KPI kartları PageHeader altında tek satır. | M + M | O (sayaç geriye gitme / yakıt kuralları — yalnız yerleşim) |
| **Talepler / Onaylar** (`RequestsView`, `ApprovalsView`) | Liste + detay + onay/ret | Şablon B; Onayla/Reddet SelectionBar'da (toplu onay dahil — yetki kuralları aynen), ret gerekçesi mevcut `ReasonWindow`. | M | O (yetki) |
| **Faturalar / Ödemeler / Finans** | Liste + form | Şablon A/B + uzun form tam sayfa; tutar kolonları sağa hizalı, toplam satırı. | M ×3 | O |
| **Günlük Faaliyet** (`DailyActivityView.axaml`) | Modern tablo + form; düğme sırası ters (`:98-99`) | Şablon A; düğme sırası kurala; sayfa boyutu `Pager`'a. | S–M | D |
| **Tanım ekranları** (Şubeler, Maliyet merkezleri, Firmalar, Ekipler, Şablonlar, Duyurular, Takvim) | Basit liste + form, `İptal | Kaydet` zaten doğru | Kısa form → sağ yan sayfa (sheet); liste görünür kalır. Ortak şablon sayesinde her biri küçük iş. | S ×7 | D |
| **Dashboard** (`DashboardView.axaml`) | KPI + uyarılar | Değişiklik az: boş durum ikonları, kontrast, kısayol "Hızlı işlemler" (Yeni çıkış, Yeni zimmet, Sayım). | S | D |
| **Ayarlar → Tema** (`ThemeSettingsView`) | Tema + aksan | Yoğunluk seçimi + yazı boyutu (Normal/Büyük) eklenir. | S | D |

**Toplam kaba tahmin:** F0 ≈ 2–3 hafta; ana ekranlar ≈ 6–8 hafta (masaüstü) + web eşleniği. Her ekran ayrı, geri alınabilir iş birimi; her biri sonrası görsel tarama (`scripts/run_ui_tests.ps1 -Filter "FullyQualifiedName~GorselTarama"`) + ilgili E2E.

**Birleştirme/sadeleştirme önerileri:** (1) Zimmet, Giriş-Çıkış, Sayım, Yakıt dağıtım **aynı Şablon C iskeleti** → tek ortak `IslemGirisSablonu` kontrolü (L, ama sonraki her ekranı S yapar). (2) "Filtrele" ve "Temizle" düğmeleri kaldırılır, yerine anında filtre + çip ✕ + "Temizle" bağlantısı. (3) "Excel'e aktar / Yazdır / QR / Sayaçları onar" gibi ikincil eylemler ⋯ menüsünde birleşir; QR gibi seçime bağlı olanlar SelectionBar'a.

---

## 5. Üçüncü taraf kütüphane önerisi

| Seçenek | Karar | Neden |
|---|---|---|
| FluentAvalonia | **Benimseme** | .NET 10 + Avalonia ≥12.1 şartı [T-16]; kabuk yeniden yazımı; kazanç (NavigationView/ContentDialog) mevcut kabukta zaten karşılanıyor. |
| Semi.Avalonia | **Kaldır** (paket başvurusu) | Kullanılmıyor (`ThemeService.cs:45-47`); ölü bağımlılık güncelleme ve güvenlik yükü taşır. Ayrı küçük iş, tema açılış testiyle. |
| Ursa.Avalonia | **Şimdilik benimseme; hedefli izle** | Tek kütüphane ki 12.0.x ile kurulur [T-18]; ama Semi tasarım dili → bizim temaya giydirme maliyeti. Yalnız somut bir eksik kontrol (ör. tarih aralığı seçici, çoklu seçimli açılır) kendi yazımından pahalı çıkarsa o kontrol için değerlendirilir. |
| SukiUI | **Benimseme** | Dekoratif tam tema, lisans doğrulanamadı [T-19]; veri yoğun ekranlara uygun değil. |
| Material.Avalonia | **Benimseme** | Farklı tasarım dili, çekirdek yükseltmesi, tüm ekranlar değişir [T-20]. |
| TreeDataGrid | **Benimseme** | Ücretli (Accelerate) [T-03]. |
| Avalonia DataGrid (MIT) | **Şimdi değil** | Çekirdek ≥12.1 ister [T-02]; mevcut özel tablo altyapısı (filtre/sıralama/genişlik/kaydet) daha zengin; geçiş L/Y. |
| Avalonia **çekirdek 12.0.4 → 12.1.x** | **Planla (ayrı iş, onaylı)** | Ekosistem 12.1'e taşındı [T-01]; `TableView` (MIT, çekirdekte) [T-05] ve güncel DataGrid seçeneğini açar; güvenlik/performans düzeltmeleri. Risk: davranış farkları → tam test takımı + görsel tarama + iki ortam denetimi. `Avalonia.Fonts.Inter`, `Avalonia.Themes.Fluent`, `AvaloniaUI.DiagnosticsSupport`, `LiveChartsCore` sürüm uyumu kontrol edilmeli. |
| `TableView` (çekirdek, 12.1+) | **Yükseltme sonrası pilot** | Salt-okuma raporlar için doğal aday; ama bizim kolon filtresi/kaydet özelliklerimizi karşılayıp karşılamadığı denenmeli (pilot: Raporlar ekranı). |
| `WindowNotificationManager` (yerleşik) | **Kullan** | Toast için ek paket gerekmez (F0.5). |

**Genel karar:** Alpnex'in özel tasarım sistemi **korunur ve sadeleştirilir**. Popüler kütüphaneye geçiş, (a) ekranların büyük bölümünü yeniden yazma, (b) web ile görsel dil ayrışması, (c) kullanıcıların alıştığı düzenin bozulması maliyeti getirir; buna karşılık kazanılan kontrollerin çoğu bizde zaten var. Asıl değer **kurallar + şablonlar + klavye + erişilebilirlik** tarafındadır ve bunlar kütüphane gerektirmez.

---

## 6. Açık konular / doğrulanmayanlar

- Açık tema görsel taraması yok; U16 kontrast bulguları hesapla çıkarıldı, ekranda doğrulanmalı.
- `WindowNotificationManager` 12.x API ayrıntısı ve Ursa'nın tema bağımlılığı belgeden doğrulanmadı.
- SukiUI lisansı ve Avalonia ControlCatalog/vitrin uygulamaları GitHub erişimi olmadığından incelenemedi.
- Birincil düğmenin kullanıcı aksanını izleyip izlememesi ürün kararıdır (marka kehribarı sabit mi kalsın?).
- Düğme sırası kuralı (birincil sağda) Windows rehberinden bilinçli sapmadır → ADR gerekir.

---

## Kaynakça (erişim 2026-10-12/13)

- [T-01] NuGet — Avalonia sürüm geçmişi: https://www.nuget.org/packages/Avalonia
- [T-02] NuGet — Avalonia.Controls.DataGrid: https://www.nuget.org/packages/Avalonia.Controls.DataGrid
- [T-03] Avalonia belgeleri — TreeDataGrid: https://docs.avaloniaui.net/controls/data-display/structured-data/treedatagrid
- [T-04] Avalonia GitHub tartışması #19804 "Accelerate Licensing Changes" (arama özeti üzerinden; sayfa açılamadı — **ikincil**): https://github.com/AvaloniaUI/Avalonia/discussions/19804
- [T-05] Avalonia belgeleri — TableView: https://docs.avaloniaui.net/controls/data-display/structured-data/tableview
- [T-06] Avalonia belgeleri — Performance optimization: https://docs.avaloniaui.net/docs/app-development/performance
- [T-07] Avalonia belgeleri — Theme variants: https://docs.avaloniaui.net/docs/styling/theme-variants
- [T-08] Avalonia belgeleri — Accessibility: https://docs.avaloniaui.net/docs/app-development/accessibility
- [T-09] Avalonia belgeleri — Keyboard and hotkeys: https://docs.avaloniaui.net/docs/input/hotkeys ; Key/mouse binding: https://docs.avaloniaui.net/docs/concepts/input/binding-key-and-mouse
- [T-10] Microsoft Fluent 2 — Layout: https://fluent2.microsoft.design/layout
- [T-11] Microsoft Fluent 2 — Typography: https://fluent2.microsoft.design/typography
- [T-12] Microsoft Learn — Command bar: https://learn.microsoft.com/en-us/windows/apps/design/controls/command-bar
- [T-13] Microsoft Learn — Dialog controls: https://learn.microsoft.com/en-us/windows/apps/design/controls/dialogs-and-flyouts/dialogs
- [T-14] Nielsen Norman Group — Data Tables: Four Major User Tasks: https://www.nngroup.com/articles/data-tables/
- [T-15] SAP Fiori — List Report Floorplan (403, **doğrulanmadı**): https://www.sap.com/design-system/fiori-design-web/page-types/floorplans/list-report-floorplan-sap-fiori-element/
- [T-16] NuGet — FluentAvaloniaUI: https://www.nuget.org/packages/FluentAvaloniaUI
- [T-17] NuGet — Semi.Avalonia: https://www.nuget.org/packages/Semi.Avalonia
- [T-18] NuGet — Irihi.Ursa (Ursa.Avalonia): https://www.nuget.org/packages/Irihi.Ursa
- [T-19] NuGet — SukiUI: https://www.nuget.org/packages/SukiUI
- [T-20] NuGet — Material.Avalonia: https://www.nuget.org/packages/Material.Avalonia
- Yerel: `docs/UI_DESIGN_SPEC.md`, `docs/UI_CHANGELOG.md`, `src/DepoWise.Desktop/Themes/*.axaml`, `src/DepoWise.Desktop/Controls/*`, `src/DepoWise.Desktop/Views/*`, `artifacts/ui-ekran/**` (2026-10-12 görsel tarama).
