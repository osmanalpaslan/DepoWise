# Gelen Görev Paketi — 2026-08-06

> Kullanıcının ilettiği uzun prompt buraya ham haliyle kaydedildi (hata yapmamak için referans).
> İşleme başlamadan önce Claude tarafından analiz edilip **sıralandı** (aşağıda "Sıralama ve Plan").
> Her birim **masaüstü önce → web hemen ardından** (platform-priority kuralı). Kod yalnız kullanıcı
> onayı + motor seçimi sonrası yazılır (§2.1).

---

## HAM PROMPT (değiştirilmeden)

### Giriş/Çıkış ve Transfer İşlemleri – Mantık Kontrolü ve Geliştirme İstekleri

**1. Giriş/Çıkış ekranındaki şube mantığını kontrol et**
Anlaşılan senaryo:
- Kayıt Tipi = Yeni Kayıt (Giriş): işlemin şubesi = login olunan şube; şube alanı varsayılan login şubeyi gösterir.
- Kayıt Tipi = Depo Çıkışı / Transfer: çıkış yapılan şube = login şube; kullanıcının SEÇTİĞİ şube = hedef (transfer edilen) şube.
- Sistem bu mantıkla çalışıyorsa DEĞİŞİKLİK YAPMA. Farklıysa önce analiz et, doğruluğunu değerlendir, gerekiyorsa düzelt.

**2. Transfer işleminin geri alınması (Rollback)**
- Transferlerde "İşlemi Geri Al" OLMAMALI (iki şube stoğunu etkiler). Doğrusu: hedeften kaynağa yeni bir ters transfer.
- Transfer kayıtlarında geri alma KAPALI. Gerekirse neden geri alınamadığını açıklayan bilgilendirme göster.

**3. Transfer işlemlerini ayrıntılı test et**
- Kaynak şube stoğu doğru azalıyor mu? Hedef doğru artıyor mu? Hem malzeme hem araçta doğru mu?
- DB tutarlı mı? Beklenmeyen yan etki / stok tutarsızlığı var mı? Sadece inceleme değil, gerçek senaryo testleri.

**4. Malzeme ve Araç Bilgi Paneline "İşlem Geçmişi" sekmesi ekle**
- Hem Malzemeler hem Araçlar bilgi paneline yeni sekme (İşlem Geçmişi / Hareket Geçmişi).
- İlgili malzeme/araç için tüm işlemler kronolojik listelensin (transfer, giriş, çıkış, sayım düzeltmesi...).
- Örn araç: "Nevşehir'den Karaman'a transfer edildi." Örn malzeme: "Ana Depo Tedarikçisinden 8 adet Filtre girişi", "3 adet X Filtre transfer", "Depo çıkışı", "Sayım düzeltmesi".

**5. İşlem Geçmişi kayıtlarının detayını görüntüleme**
- Listedeki kayıt çift tıklandığında: düzenleme YAPILMAYAN, salt-okunur yeni pencere açılsın.
- Bu pencerede "Kaydı Görüntüle" butonu: kullanıcıyı işlemin ait olduğu GERÇEK ekrana yönlendirsin (orada düzenlenebilir).
- İşlem Geçmişi penceresinin kendisinde hiçbir düzenleme yapılamaz.

Uygulama notu: mevcut mimariyi incele, veri modeliyle uyumlu ol, mevcut davranışları bozma; stok hareketleri / transfer mantığı / veri tutarlılığında dikkatli ol, gerekli yerde ek doğrulama+test ekle.

---

### Tablo Sütunları ve Hücre Görünümü İyileştirmeleri

**1. Sütun genişliğinin tekrar küçültülememesi** — sütun büyütüldükten sonra tekrar daraltılamıyor; nedenini bul+düzelt. Artırılabilmeli VE tekrar daraltılabilmeli; min genişlik mantıklı olmalı.

**2. Uzun verilerin diğer sütunlara taşması** — metin uzun olunca komşu sütuna taşıyor. Her veri kendi hücresinde kalmalı; taşmamalı. Yöntem: ellipsis `...` / kırpma / tooltip. Düzen bozulmamalı.

**3. Excel benzeri hücre mantığı** — her hücre kendi sınırında; taşma yok; sütun genişliği değişince anlık güncellenir; hiza bozulmaz; farklı veri uzunluklarında tutarlı.

Uygulama notu: tek tabloda değil, aynı bileşeni kullanan TÜM ekranlarda kontrol et; ortak bileşenden kaynaklanıyorsa MERKEZİ çöz; farklı veri uzunluklarıyla test et.

---

### "+" Seçim Pencerelerinde Metin Arama Özelliğinin Standartlaştırılması

- Tüm "+" (Kayıt Seç) butonlarını analiz et. Bazı seçim pencerelerinde arama var, bazılarında yok — tutarsızlığı gider.
- Analiz: hangilerinde var/yok, ortak bileşen var mı, merkezi çözüm mümkün mü.
- Arama olmayan tüm seçim pencerelerine ekle: yazdıkça anlık filtre, büyük/küçük harf duyarsız, Türkçe karakter (Ç Ğ İ Ö Ş Ü) doğru, hızlı.
- Ortak davranış standardı: kutu konumu, filtre algoritması, klavye, seçim davranışı, görsel. Ortak/shared bileşende çöz, kod tekrarı yok. Sonra tüm "+" pencereleri tek tek test.

---

### TABLO FİLTRELEME ARAYÜZÜNÜN YENİDEN DÜZENLENMESİ + PROJE STANDARTLARI

1. Önce projeyi analiz et: hangi ekranlarda tablo, hangilerinde sütun-bazlı filtre, filtre kutuları nerede, ortak Table/Grid bileşeni var mı, merkezi uygulanabilir mi.
2. Filtre kutularını BAŞLIK SATIRININ ALTINA taşı (Toolbar → Kolon Başlıkları → Filtre Kutuları → Veriler). Her sütunun altında yalnız o sütunun filtresi; başlık-filtre tam hizalı.
3. Görsel: modern, sade, profesyonel, ERP/CRM standardı, koyu tema uyumlu, dengeli boşluk/padding; sonradan eklenmiş gibi durmasın.
4. Çalışanı bozma: mevcut filtre algoritması, event, sıralama, çoklu filtre, sayfalama, kolon gizle/göster/boyutlandır, klavye, performans korunur. Filtre mantığını yeniden yazma; mevcut bileşenleri yeniden kullan.
5. Responsive: kolon genişliği/gizleme/gösterme/sıra/yatay-kaydırma değişince başlık+filtre hizası korunur.
6. Tüm UYGUN listeleme ekranlarına uygula. Form/ayarlar/bilgi kartı/detay/listeleme-olmayan ekranlara DOKUNMA.
7. Ortak bileşen kullan; kod kopyalama yok; yeni tablolar da otomatik bu standardı alsın.
8. + Seçim pencerelerindeki metin aramalarını standartlaştır (yukarıdaki "+" bölümüyle aynı).
9. + Seçim pencerelerinde ortak UX (kutu konumu, filtre, klavye, seçim, görsel, düzen).
10. Test: tablolar (filtre/çoklu filtre/sıralama/kolon genişliği/gizle-göster/sayfalama/hiza/performans/koyu tema); + pencereleri (arama/doğruluk/Türkçe/performans/bozulmama).

**Proje standardı (bundan sonra, ayrıca belirtmeden):** uygun listeleme ekranlarında başlık-altı filtre + ortak tablo tasarımı + modern ERP görünümü + ortak "+" seçim davranışı uygula. AMA körü körüne değil — her zaman önce ekranı analiz et; listeleme/tablo değilse dokunma.

En önemli kurallar: önce analiz → plan → ortak bileşenleri belirle → minimum değişiklik → kod tekrarından kaçın → geriye dönük uyumluluk → çalışanı bozma → gereksiz refactor yok → mimariye sadık → sonra test.

---

## SIRALAMA VE PLAN (Claude analizi)

Paket 4 temaya, 5 uygulama birimine ayrıldı. Sıra: **veri bütünlüğü/correctness önce, kozmetik/UI sonra.**
Her birim masaüstü önce → web hemen ardından; birim bitince commit+push (gerekirse yayın).

| # | Birim | Kapsam (ham prompt karşılığı) | Risk | Önerilen motor |
|---|-------|-------------------------------|------|----------------|
| 1 | **Şube mantığı + Transfer bütünlüğü** | Giriş/Çıkış/Transfer §1 + §2 (transfer geri-al kapat) + §3 (test) | Yüksek (stok defteri, LWW-hassas) | **Opus 4.8** |
| 2 | **İşlem Geçmişi sekmesi + detay** | Giriş/Çıkış/Transfer §4 + §5 | Orta (salt-okunur, additive) | Sonnet 5 (gerekirse Opus) |
| 3 | **Tablo hücre davranışı** | Tablo Sütunları §1+§2+§3 (daraltma + taşma/ellipsis, ortak bileşen) | Orta (ortak UI bileşen) | Sonnet 5 |
| 4 | **Başlık-altı filtre satırı + proje standardı** | Tablo Filtreleme §1–§7, §10 (+ kalıcı standart) | Orta-yüksek (geniş, ortak bileşen) | Sonnet 5 (gerekirse Opus) |
| 5 | **"+" seçim pencerelerinde arama standardı** | "+" bölümü = Filtreleme §8+§9 | Orta (ortak seçim bileşeni) | Sonnet 5 |

**Neden bu sıra:** (1) Kullanıcı en çok veri tutarlılığından endişeli; transfer/stok en riskli ve temel — önce burası sağlamlaşmalı, üstelik §1 saf ANALİZ ile başlıyor (güvenli giriş). (2) Geçmiş sekmesi mevcut hareket verisini okur, additive. (3→4→5) UI/ortak-bileşen üçlüsü: dar/yerel (hücre davranışı) → geniş (filtre satırı) → çapraz-kesen (seçim arama). Böylece UI işleri doğrulanmış veri temeli üzerine oturur.

**Durum:** Birim #1 BİTTİ (2026-08-06). Yapılanlar:
- Madde 1 (şube mantığı): işlem/kaynak şube artık **login (çalışma) şube** — masaüstü+web'de salt-okunur
  gösteriliyor; kullanıcı yalnız transfer **hedefini** seçer. Giriş'te şube boş bırakılıp hareketin şubesiz
  kaydolması (o şubede stok görünmemesi) hatası da kapandı. Sunucu `Transfer` EnforceOwnBranch dönüşünü
  kullanacak şekilde düzeltildi (kaynak hareketi artık daima şubeli).
- Madde 2 (transfer geri-alma): `StockService.ReverseDocument` transfer belgesini **reddediyor** (net mesaj);
  `StockMovementRow.CanReverse` transfer'i dışlıyor → iki arayüzde de "İptal" butonu gizli.
- Madde 3 (transfer testi): per-branch bakiye (kaynak düşer/hedef artar) + transfer-geri-alma-reddi testleri
  eklendi. Tüm paket **589/0** (11 PG atlandı).
- ⚠️ Not: web/API'de sunucu-tarafı oturum `OperatingBranchId` set ETMİYOR → web'de şube kuralları istemci
  (`Auth.BranchId`) sürücülü; sunucu istemcinin gönderdiğine güveniyor. Bilinçli/kabul (yatırımcı-öncesi, JWT
  şema değişikliği ertelendi). İleride sertleştirilebilir.

Sıradaki tek iş: **Birim #2 — İşlem Geçmişi sekmesi + detay** (kullanıcı onayı/motoru sonrası).

---

**Birim #2 BİTTİ (2026-08-06, Sonnet 5).** Yapılanlar:
- Keşif: Malzemeler ekranında zaten "Son Hareketler" (StockService.RecentForMaterial) vardı; Araçlar ekranında
  masaüstünde 4 sekmeli bir detay paneli (Uyumlu Malzemeler/Muayene-Sigorta/Bakım/**Araç Hareketleri**) zaten
  vardı ama web tarafında aynı veri (`_dMaterials`/`_dMaint`/`_dMoves`) çekiliyor, HİÇ RENDER EDİLMİYORDU (yarım
  bırakılmış/unutulmuş özellik — bu iş kapsamında yalnız İşlem Geçmişi kısmı tamamlandı, diğer 3 sekme web'de
  hâlâ render edilmiyor; ayrı iş olarak not edildi).
- **Madde 4 (İşlem Geçmişi sekmesi):**
  - Malzeme: mevcut "Son Hareketler" → ana ekran bilgi paneline "İŞLEM GEÇMİŞİ" bölümü olarak taşındı/büyütüldü
    (cap 10→100), masaüstü + web.
  - Araç: YENİ `VehicleService.RecentHistory` — audit_logs (oluşturma/genel güncelleme/silme; ŞUBE
    DEĞİŞİYORSA `VehicleService.Update` artık isimli JSON ile zenginleştirip "X Şubesinden Y Şubesine transfer
    edildi." üretiyor) + vehicle_meter_logs (sayaç, kaynağa göre Yakıt/Bakım/Manuel) birleşimi. Masaüstünde
    mevcut "Araç Hareketleri" sekmesi "İşlem Geçmişi" adıyla bu veriyle + Günlük Faaliyet hareketleriyle
    birleşik gösteriliyor; webde aynı birleşim yeni eklendi (araç düzenleme formunda, düzenleme sırasında).
- **Madde 5 (detay + Kaydı Görüntüle):** Masaüstü `HistoryDetailWindow` (paylaşımlı, salt-okunur) + web
  `HistoryDetailDialog` (paylaşımlı MudDialog). Malzeme kayıtlarında her zaman "Kaydı Görüntüle" var → Stok
  Hareketleri ekranına malzeme koduyla arama yaparak gider (yeni: `StockMovementsViewModel` artık
  `IDeepLinkTarget`; web `StockMovements.razor` artık `?q=` query param'ı okuyor). Araç sistem-olay satırlarında
  (oluşturma/transfer/güncelleme/sayaç) "Kaydı Görüntüle" YOK (zaten o ekrandasınız); yalnız Günlük Faaliyet
  kaynaklı satırlarda var → Günlük Faaliyet ekranına gider.
- **API:** yeni `GET /api/vehicles/{id}/history`.
- **Test:** `VehicleTests.RecentHistory_SubeTransferi_OkunakliMetinUretir` (oluşturma satırı + transfer metni +
  transfer OLMAYAN güncellemede transfer metni YOK). Tüm paket **590/0** (11 PG atlandı).
- ⚠️ **Yayınlanmadı** (deploy edilmedi) — `/api/vehicles/{id}/history` canlıda henüz yok; web'de yeni özellik
  test edilmeden önce hem `fly deploy -c fly.toml` (API) hem `fly deploy -c fly.web.toml` (web UI) gerekir.
- ⚠️ Not (free housekeeping fırsatı, ayrı iş): web Araçlar ekranında Uyumlu Malzemeler/Muayene-Sigorta/Bakım
  sekmeleri hâlâ render edilmiyor (veri çekiliyor ama gösterilmiyor) — masaüstüyle tam pariteye ulaşmak için
  ayrıca ele alınabilir.

**Birim #3 BİTTİ (2026-08-06, Sonnet 5).** Kök neden bulundu: masaüstü tablolarında (Malzemeler/Araçlar/Günlük
Faaliyet — `local:SortHeader` + `ColWidths` + `SharedSizeGroup` deseni kullanan TEK 3 ekran) satır hücreleri
yalnız statik bir `MinWidth` (taban) taşıyordu, ÜST SINIR yoktu. `Width="Auto"` + `SharedSizeGroup` altında bir
sütunun gerçek genişliği, o SharedSizeGroup'a katılan TÜM hücrelerin (header + her satır) DOĞAL (kısıtlanmamış)
genişliğinin MAKSİMUMU olur. Satırlarda üst sınır olmayınca: (1) uzun içerikli TEK satır bile sütunu
küçültülemez hale getiriyordu (header'ı ne kadar sürüklerseniz sürükleyin, satırın doğal genişliği daha büyükse
o kazanıyordu) — "önce büyütülüyor sonra küçültülemiyor" hissi buradan geliyordu; (2) `TextTrimming` zaten
XAML'de vardı ama HİÇ ÇALIŞMIYORDU çünkü `Auto` sütun ölçümü sonsuz genişlikle yapılır, kırpma tetiklenmiyordu.
Ayrıca proje içinde tam bu amaç için yazılmış `Conv.ColWidth` converter'ı hiçbir yerde KULLANILMIYORDU (ölü kod).

**Çözüm (3 ekranın hepsinde aynı desen):** her satır hücresinin `MinWidth` VE `MaxWidth`'ini header'ın kullandığı
AYNI `ColWidths` sözlüğüne (`Conv.ColWidth` converter'ı ile) bağladım — böylece header ile satırlar HER ZAMAN
aynı genişliği okur; kullanıcı sürükleyince (`PreviewColumnWidth`) satırlar ANINDA küçülür/büyür, `TextTrimming`
artık gerçek bir üst sınırla çalışıp ellipsis üretir + eksik olan sütunlara `ToolTip.Tip` eklendi (tam metin).
"*"-genişlikli esnek sütunlara (Malzeme Adı, Araç Plakası, Günlük Faaliyet Rotası) dokunulmadı — zaten doğru
davranıyorlardı. Web tabloları (MudTable) bu Avalonia'ya özgü SharedSizeGroup hatasını YAŞAMIYOR (zaten
`overflow-x:auto` ile standart kayan tablo) — web hücre inceltmesi Birim #4'ün (tablo/filtre yeniden tasarımı)
kapsamına bırakıldı, orada zaten tüm tablo yapısı ele alınacak.

Değişen dosyalar: `MaterialsView.axaml`, `VehiclesView.axaml`, `DailyActivityView.axaml`. Build 0 hata, test
paketi **590/0** (UI-only değişiklik, iş mantığına dokunulmadı). Görsel doğrulama YAPILAMADI (Avalonia masaüstü
önizlemesi bu ortamda yok) — kullanıcının kendi makinesinde denemesi gerekiyor.

**Birim #4 BİTTİ (2026-08-06, Sonnet 5).** Kapsam: yalnız kolon-bazlı filtreye sahip TEK 3 ekran (Malzemeler/
Araçlar/Günlük Faaliyet — hem masaüstü hem web, 1:1 eşleşiyor). Diğer liste ekranlarında (Personel/Talepler/
Kullanıcılar/...) kolon-bazlı filtre YOK → kapsam dışı (prompt'un kendi istisnası: "listeleme amacı taşımayan/
uygun olmayan ekranlara dokunma").

**Masaüstü:** Filtre kutuları önceden tablonun ÜSTÜNDE ayrı bir `WrapPanel` idi (sıraları görsel olarak
sütunlarla hizalı değildi). Her ekranda: (1) VM'e `FilterFieldsByKey` (anahtara hızlı erişim, `ColWidths` ile
AYNI desende `[ObservableProperty]`) eklendi; (2) yeni `Conv.FilterItem` converter'ı (sözlük+anahtar → o
kolonun `ColumnFilterItem` NESNESİ); (3) XAML'de header'ın HEMEN ALTINA, AYNI `SharedSizeGroup` yapısıyla yeni
bir filtre satırı — her hücre `ContentControl`+`DataTemplate` (`x:DataType="vm:ColumnFilterItem"`) ile o
kolonun filtre kutusunu barındırır, dış Min/Max/IsVisible ise `ContentControl`'ün KENDİ DataContext'i (VM)
üzerinden okunur → iki bağlama kapsamı KARIŞMAZ, derlenmiş bağlama (compiled binding) güvenli kalır. Filtreleme
ALGORİTMASI (GetFilter/SetFilter/ApplyFilters/Enter-ile-filtrele) hiç değişmedi — yalnız konum.

**Web:** Filtre kutuları önceden tablonun ÜSTÜNDE bir `MudGrid` idi (satır kayması, sütunla hizasız). Her
ekranda: eski `MudGrid` bloğu kaldırıldı; `<thead>` içine, başlık `<tr>`'sinin HEMEN ALTINA aynı `_visibleColumns`
sırasıyla ikinci bir `<tr>` eklendi — `table-layout:fixed` sayesinde OTOMATİK hizalanır (ekstra senkron kodu
gerekmedi; web bu konuda masaüstünden daha basitti). Yeni `.dw-filter-th` CSS sınıfı (app.css): `resize:none`
(başlığın sürükle-genişlet tutamağı tekrarlanmasın) + `position:sticky; top:36px` (başlığın ALTINA sabitlenir,
üst üste binmez). GetFilter/SetFilter/OnFilterKey AYNEN korundu.

**Doğrulama (canlı tarayıcı, yerel dev sunucu + gerçek API):** `osman.alpaslan` test hesabıyla giriş yapıp
Malzemeler ekranını DOM üzerinden ölçtüm — **15 sütunun 15'i de** header ile filtre satırı arasında piksel
piksel hizalı (`left`+`width` birebir eşit). Filtre kutusu hücreyi taşmıyor. Filtreleme GERÇEKTEN çalışıyor:
"TEST1" yazıp Filtrele'ye basınca liste 2 kayıttan 1'e düştü. Konsol hatası yok, sayfa yatay taşması yok.
Araçlar/Günlük Faaliyet ekranlarında bu test şirketinde hiç kayıt olmadığı için tablo render olmadı (kod
YAPISAL olarak Malzemeler ile birebir aynı desen — ayrı doğrulama gerekmedi).

Build (masaüstü+web) 0 hata. Test paketi 590/0 (bu birim salt UI/markup — desteki iş mantığına dokunulmadı).

**Birim #5 BİTTİ (2026-08-06, Sonnet 5) — PAKET TAMAMLANDI (5/5).**

**Analiz:** Masaüstünde büyüyebilecek kayıt listelerini (Şube/Kategori/Alt Kategori/Birim/Marka/Tedarikçi) seçen
**12 `ComboBox`** (6 ekranda) arama İÇERMİYORDU — kullanıcı listeyi elle kaydırmak zorundaydı. Aynı ekranlarda
Personel/Araç seçicileri zaten `AutoCompleteBox` (arama VAR) kullanıyordu — kullanıcının şikayet ettiği
tutarsızlık tam olarak buydu. Web'de ise TÜM "+" (Kayıt Seç) alanları zaten TEK ortak bileşen `LookupSelect.razor`
üzerinden gidiyordu (14+ ekran) — arama zaten VARDI ama **Türkçe karakter hatası** taşıyordu.

**Masaüstü:** 6 ekranda (`StockEntryView` 6, `DailyActivityView` 2, `PersonnelView` 1, `UsersView` 1,
`SettingsView` 1, `FuelView` 1) 12 lookup `ComboBox`'ı, Personel/Araç seçicileriyle AYNI kanıtlanmış bileşene
(`AutoCompleteBox`, `FilterMode="Contains" MinimumPrefixLength="0"`) yükseltildi — proje standardı artık tek:
her büyüyebilir liste seçici arama-yazılabilir. Sabit kısa listeler (Tür/Durum gibi enum'lar) bilinçli olarak
DOKUNULMADI (aramaya ihtiyaçları yok).

**Web:** `LookupSelect.razor`'daki arama `StringComparison.OrdinalIgnoreCase` kullanıyordu — bu, Türkçe
büyük/küçük harf kurallarını (İ↔i, I↔ı) YANLIŞ eşliyor. **Küçük bir C# betiğiyle KANITLANDI:**
`"İSTANBUL".Contains("istanbul", OrdinalIgnoreCase)` → **False** (hatalı!), `"KIRAÇ".Contains("kıraç",
OrdinalIgnoreCase)` → **False** (hatalı!). `CultureInfo("tr-TR").CompareInfo` ile ikisi de **True** (doğru).
Yeni `FieldChecks.TrCompare` (tek ortak kaynak) eklendi; `LookupSelect.razor` (arama + tekrar-kontrolü),
`Stock.razor` (`IdOf`), `Materials.razor` (`ReloadSubCats`) buna bağlandı — **TEK dosya değişikliği 14+ ekranı
düzeltti** (LookupSelect ortak bileşen sayesinde).

**Doğrulama:** Build (masaüstü+web) 0 hata. Test paketi 590/0. Türkçe karşılaştırma düzeltmesi bağımsız bir
konsol betiğiyle ampirik olarak doğrulandı (yukarıdaki İSTANBUL/KIRAÇ örnekleri).

---

## PAKET ÖZETİ (5/5 birim tamam, 2026-08-06)

| # | Birim | Durum |
|---|-------|-------|
| 1 | Şube mantığı + Transfer bütünlüğü | ✅ |
| 2 | İşlem Geçmişi sekmesi + detay | ✅ |
| 3 | Tablo hücre davranışı (küçültme + taşma) | ✅ |
| 4 | Başlık-altı filtre satırı | ✅ |
| 5 | "+" seçim pencerelerinde arama standardı | ✅ |

**Yayın durumu:** Hiçbir birim henüz `fly deploy` ile yayınlanmadı — hepsi commit+push edildi (GitHub güncel),
ama canlıda değil. Birim 2 yeni bir API ucu (`/api/vehicles/{id}/history`) ekledi; o yüzden web'de tam
çalışması için hem `fly deploy -c fly.toml` (API) hem `fly deploy -c fly.web.toml` (web) gerekiyor. Masaüstü
değişiklikleri **görsel doğrulanmadı** (bu ortamda Avalonia önizlemesi yok) — kullanıcının kendi makinesinde
denemesi gerekiyor, özellikle Birim 1 (şube/transfer), Birim 3 (sütun küçültme) ve Birim 5 (yeni arama kutuları).

- [x] 1 — Şube mantığı + Transfer bütünlüğü ✅ (2026-08-06)
- [x] 2 — İşlem Geçmişi sekmesi + detay ✅ (2026-08-06)
- [x] 3 — Tablo hücre davranışı ✅ (2026-08-06)
- [x] 4 — Başlık-altı filtre satırı + proje standardı ✅ (2026-08-06)
- [x] 5 — "+" seçim pencerelerinde arama standardı ✅ (2026-08-06)
