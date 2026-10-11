---
name: yeni-ekran
description: DepoWise'a yeni bir menü ekranı (masaüstü + web) eklerken ya da mevcut ekranın alt ekranını/rotasını açarken kullan. Ekran kataloğu, menü simgesi, masaüstü yönlendirmesi, web sayfası, yetki, sabitlenmiş menü sayıları ve web'deki aynı-bileşen tuzağını eksiksiz kapsar. "yeni ekran", "menüye ekle", "ayrı sayfa olsun" isteklerinde çağrılır.
---

# Yeni ekran (DepoWise)

Önce masaüstü, web AYNI iş biriminde tamamlanır (`.claude/rules/platform-priority.md`). Ekran kataloğu TEK kaynaktır.

## 1. Katalog (tek satır) — `src/DepoWise.Application/Security/AppScreens.cs`
`new AppScreen("grup.ad", "<modül>", "<Menü grubu>", "<Etiket>", Both|D|W, "<web-rotası>", "<masaüstü:anahtar>", WebPermOverride: ...)`
- Modül yetkisi: mevcut modülün alt ekranıysa AYNI modül (ör. Kiralık Araçlar → "vehicles"). Herkese açık ekran
  (Hakkında/Tema/Güncelleme Notları gibi) → modül "about"/"theme" + `WebPermOverride: ""`. Yeni modül = yetki ağacı
  değişir → kullanıcı onayı.
- Bu dosya web'e bağlıdır (Compile Include) → web'de gereken ortak tip de buraya ya da bağlı başka dosyaya.

## 2. Simge — `Security/MenuIcons.cs`: `["grup.ad"] = "<mevcut kavram>"` (yeni kavram = iki platformda çizim gerekir).

## 3. Masaüstü
- `ViewModels/<Ad>ViewModel.cs` (`ViewModelBase`) + `Views/<Ad>View.axaml(.cs)` — ViewLocator adla eşler
  (`...ViewModel` → `...View`). Mevcut desen: `ctrl:Toolbar`, `Classes="Panel"`, `ctrl:StatePanel` boş/hata durumu.
- `ViewModels/ShellViewModel.cs` `Navigate` → `case "<masaüstü:anahtar>": CurrentPage = new ...; CurrentTitle = ...; break;`
- Tablo satırları: `Grid ctrl:ColumnRules.Enabled="True"` (sütun ayırıcı — `MasaustuTabloKolonHizasiTests`).
- Kayıt işlemi yapan komutlar `ConfirmService` onayı ister; form içi çıkar/iptal komutları `IslemOnaylariTests.Muaf`.

## 4. Web
- `Components/Pages/<Ad>.razor` → `@page "/<web-rotası>"`, başlık `MudText Class="dw-pagetitle"`.
- ⚠️ **Aynı bileşene ikinci `@page`** (ör. `/x` + `/x/{Section}`) ekliyorsan: Blazor sayfayı yeniden kurmaz →
  `WebSayfaAnahtari.AltEkrandaSifirlanan`'a ilk yol parçasını ekle (alt ekran veri/kip değiştiriyorsa) ya da yalnız
  görünüm değişiyorsa `WebSayfaAnahtariTests.YalnizGorunumDegisen`'e. Test sınıflandırılmamış sayfayı yakalar.
- Yeni CSS → `wwwroot/app.css`'te "18) MOB-W" mobil katmanından ÖNCE; yorum `/* */` dengesi (`WebCssButunlukTests`).

## 5. Sabitlenmiş sayılar (bilinçli güncelle, yorumda gerekçe)
- `AppScreensParityTests`: masaüstü/web menü şemaları (grup listesine anahtar/rota) + toplam (S13 masaüstü, S14 web).
- `MenuRenkTests.RNK12`: `AppScreens.All.Count`.
- Gerekirse `MasaustuTasarimPaketiTests` (filtre/kolon sayıları).

## 6. Doğrulama
- `test-kosusu`: ilgili filtreler (`AppScreens|ScreenTree|MenuRenk|MenuIkon|WebSayfa|IslemOnaylari`) + masaüstü headless
  testi (görünür metin/çizim + `artifacts/ui-ekran/*.png` gözle) + yayından sonra canlı Playwright (menüden aç-geç).
- İki ortamda aynı alan/buton/yetki — `iki-ortam-denetcisi` agent'ı (varsa) son kontrol.
