---
paths:
  - "src/DepoWise.Application/Ui/ListColumns.cs"
  - "src/DepoWise.Web/Services/ListColumns.cs"
  - "src/DepoWise.Infrastructure/Database/GridQuery.cs"
  - "src/DepoWise.Infrastructure/**/*Service.cs"
  - "src/DepoWise.Web/Components/Pages/*.razor"
  - "src/DepoWise.Desktop/ViewModels/*ViewModel.cs"
  - "src/DepoWise.Desktop/Views/*.axaml"
---
# Liste ekranları (Malzemeler/Araçlar/Günlük Faaliyet ve benzerleri — ADR-087/088/089)

## Kural 1 — Yeni alan eklerken filtreye dahil et (kullanıcı isteği 2026-07-19)
Malzemeler/Araçlar (ve aynı deseni kullanan her ekran) formuna/listesine **yeni bir alan** eklerken, o alan
filtrelenebilir olacaksa aynı PR'da şunlar YAPILIR (birini atlama — ekran sessizce filtrelenemez kalır):

1. **Kolon kataloğu** (`DepoWise.Application/Ui/ListColumns.cs` — TEK dosya; web onu csproj `Compile Include … Link=`
   ile derler, ayrı kopya YOK — M-76 2026-10-26): yeni `ListColumn(Key, Label, IsNumeric: true/false)` ekle. `IsNumeric: true` yalnız
   gerçekten sayısal (fiyat/stok/yıl/sayaç gibi) alanlarda — metin kalırsa "içerir" araması geçerli olur.
2. **Filtre kaydı** (`MaterialService.SearchGrid` / `VehicleService.SearchGrid` benzeri): `GridFilter` record'una
   yeni parametre + `GridQuery.ColumnFilter(alias, filter.YeniAlan, kind, rawAlias)` satırı ekle. Hesaplanan/
   join'lenmiş kolon ise `GridInnerSql`'e `AS alias` ekle (derived-table deseni — bkz. dosyanın başındaki not).
3. **API ucu**: `/api/.../grid` endpoint'ine yeni query parametresi ekle, `GridFilter` constructor'ına geçir.
4. **UI (web+masaüstü)**: filtre kutusu otomatik gelir (kolon kataloğundan `_visibleColumns` döngüsüyle üretilir)
   — EKSTRA XAML/Razor gerekmez, yalnız kolon kataloğuna ekleme yeterlidir.
5. **Test**: en az bir `*GridTests.cs` testi — metin ise "içerir" araması, sayısal ise tam-eşleşme/karşılaştırma.

## Kural 2 — Filtrelenen sonucu Excel'e aktarma (kullanıcı isteği 2026-07-19)
Filtre/sıralama/sayfalama olan HER liste ekranında bir **"Excel'e Aktar"** butonu bulunur:
- Buton, o an ekrandaki **sayfa değil, FİLTRELENMİŞ TÜM SONUÇ KÜMESİNİ** (sayfalama sınırı olmadan) indirir.
- Buton üstünde/yanında kısa bir açıklama (tooltip) bulunur: ne işe yaradığını (aktif filtrelerle dışa
  aktarır) belirtir — kullanıcı "bu ne yapıyor" diye tahmin etmek zorunda kalmaz.
- Yeni bir liste ekranı (bu ADR-087/088/089 deseniyle) eklendiğinde bu buton da eklenir — atlanmaz.

## Kural 3 — Tablo sütunları veriyle kaymaz (KALICI, kullanıcı isteği 2026-10-29)
Masaüstü + web, rapor dahil TÜM tablolarda:
- Bir sütunun verisi diğer sütuna **taşmaz**; uzun yazı sütun içinde "…" ile kesilir (tamamı ipucunda/ayrıntıda görülür).
- Sütun konumları listelenen veriye göre **değişmez**: başlık, başlık altındaki filtre satırı ve her veri satırı AYNI genişlikleri kullanır.
- Uzun veriyi okumak isteyen kullanıcı sütunu başlıktaki tutamaktan **kendisi genişletir** (genişlik hatırlanır).
- Masaüstü mekanizması: `Controls/KolonGenislik.cs` (filtre satırı kaydı + `Tohumla`: esnek (*) ve üst sınırsız sütunlar HER ZAMAN
  tablonun görünür genişliğinden hesaplanıp piksel olarak sabitlenir; pencere/çerçeve genişliği değişince yeniden hesaplanır; kullanıcının
  sürüklediği sütun hesaba girmez) + `TableHeaderSync.cs`. Kesilen hücre yazısının tamamı ipucunda.
- **Neden (tekrarlanmasın):** yatay kaydırılabilir listede satırlar SINIRSIZ genişlikte ölçülür → esnek sütun her satırda kendi içeriği
  kadar olur (aynı tabloda "Malzeme" 200–430 px; işlem düğmesi olmayan satırda kalan alan da esnek sütuna geçer). Yalnız "taşma
  olunca" düzeltmek YETMEZ (2026-10-29 ilk düzeltme böyleydi, tablo sığarken satırlar yine kaydı). Esneklik başlığın ŞU ANKİ tanımından
  değil ÖZGÜN tanımdan okunur (TableHeaderSync başlığa satırın piksel genişliğini kopyalar). Asgari genişlik/üst sınır bilgisi
  Uygula'dan ÖNCE alınır (Uygula hücre MinWidth/MaxWidth'ini ezer). Yeni tabloda başlık/filtre/satır ızgaraları
  aynı `ColumnDefinitions` + `ctrl:ColumnRules.Enabled="True"` taşır; filtre satırı başlıkla aynı kapta `Border.TableFilterRow`.
- Testler: `UITTABLO1` (çok uzun veriyle hizalama + sığma), `UITTABLO2` (tablo SIĞARKEN farklı içerikli satırlar + iki pencere
  genişliği + alanı doldurma), `G31` (başlık-satır hizası), `TABLO1` (kaynak nöbetçisi). Görsel tarama tablo hücresinde "…" ile
  kesilip ipucu taşıyan yazıyı hata saymaz; başka her yazı denetlenir.
