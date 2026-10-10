# BAR-01 — Barkod / QR · KALICI KONTROL BELGESİ

> Oluşturma: **2026-08-28** · Karar: **ADR-177** · Roadmap: FAZ 5 / SIRA 14 (MASTER_ROADMAP §1)
> Analiz: [O_BARKOD_QR_00_ANALIZ.md](O_BARKOD_QR_00_ANALIZ.md) — PK-O1..O4 kullanıcı tarafından
> **A-A-A-A** olarak KESİNLEŞTİRİLDİ ve AYNEN uygulandı.

## 1. Uygulanan ürün kararları

| Karar | Uygulama |
|---|---|
| PK-O1=A | **Tara → bul → git + QR etiket üretimi.** Yeni barkod sistemi KURULMADI: tarama = mevcut Global Arama (ARA-01) yolu — USB okuyucu klavye taklidiyle kodu yazar + Enter basar; elle giriş aynı yol. Kamera/driver/SDK YOK (v1 dışı). QR etiketi yalnız **Malzeme · Araç · Ekipman**. |
| PK-O2=A | **QRCoder 1.6.0** (saf C#, MIT, native bağımlılık yok; `PngByteQRCode` — System.Drawing kullanmaz) Infrastructure'a eklendi. Masaüstü PNG'yi YERELDE üretir (çevrimdışı); web eklemeli `GET /api/qr/{entity}/{id}` ucundan indirir. İkinci QR sistemi yok. |
| PK-O3=A | **EAN-13/üretici barkodu v1 DIŞI** — hiçbir tabloya kolon/ALTER yapılmadı; yeni kimlik alanı yok. QR içeriği = kaydın MEVCUT benzersiz kodu **DÜZ METİN** (malzeme `code` · araç `internal_code` · ekipman `code`); URL/JSON/metadata yok. |
| PK-O4=A | **Tam-tek eşleşmede otomatik açılış:** arama sonucu TAM birebir eşleşme (Label/SubLabel, Türkçe-duyarsız) TÜM kaynaklarda TAM 1 taneyse panel yerine kayıt açılır (mevcut `OpenSearchHit`/`IDeepLinkTarget` yolu). Birden çok tam / yalnız kısmi / sıfır sonuç / **HasMore'lu grup** → mevcut panel AYNEN (fail-safe). Kutu otomatik açılışta temizlenir (ardışık tarama). |

## 2. Mimari

- **`QrLabelService`** (Infrastructure/Reporting, YENİ, statik): `Png(kod)` → PNG (ECC M) + `FileName(kod)`
  (dosya-adı temizleme). **SALT-OKUNUR ve DURUMSUZ** — DB'ye dokunmaz, dosya/senkron/audit kaydı üretmez.
- **`SearchService.TekTamEslesme`** (YENİ statik metod, ARA-01 dosyasında): tam-tek eşleşme kuralının TEK
  kaynağı; masaüstü `ShellViewModel.RunSearch` doğrudan kullanır, web `MainLayout.RunSearch` aynı kuralı
  JSON üzerinde birebir uygular (yorumlarla çift yönlü bağlandı). ARA-01'in kendisi DEĞİŞMEDİ (kategori
  gruplama, limit 5, min 2 karakter, yetki/tenant/BranchAccess/silinmiş süzme aynen).
- **Odak kısayolu Ctrl+K:** masaüstü `MainWindow.OnKeyDown` → arama kutusuna odak + tümünü seç; web
  `App.razor`'da keydown dinleyicisi → `#dw-global-search` (tarayıcı varsayılanı geçersiz kılınır).
  Ardışık tarama için metin seçilir/temizlenir.
- **API:** eklemeli TEK uç `GET /api/qr/{entity}/{id}` (materials|vehicles|equipment) — ham SQL YOK, kod
  kaynak modülün KENDİ servisiyle çözülür (`Materials.GetDetail` · `Vehicles.Get` · `Equipment.List`):
  `Require(View)` + tenant SERVİSTE; bilinmeyen tür 400, bulunamayan 404. Yanıt `image/png`.
  Mevcut hiçbir uç/DTO değişmedi; `/api/search` sözleşmesi AYNEN (eski istemciler etkilenmez).
- **Yetki:** yeni `barcode`/`qr` modülü YOK — tarama arama kapılarıyla, etiket kaynak View'ıyla.
- **UI:** masaüstü Malzemeler/Araçlar/Ekipman araç çubuğuna "QR Etiketi" (seçili satır → PNG kaydet+aç);
  web'de Malzeme/Araç düzenleme formu başlığına + Ekipman satır aksiyonlarına QR düğmesi. Yeni ekran yok.

## 3. Değişen/yeni dosyalar

| Dosya | Değişiklik |
|---|---|
| `Infrastructure/Reporting/QrLabelService.cs` | **YENİ** — QR PNG üretici (salt-okunur) |
| `Infrastructure/DepoWise.Infrastructure.csproj` | +QRCoder 1.6.0 |
| `Infrastructure/Search/SearchService.cs` | **Eklemeli** statik `TekTamEslesme` (mevcut arama koduna dokunulmadı) |
| `Api/Program.cs` | **Eklemeli** `GET /api/qr/{entity}/{id}` |
| `Desktop/ViewModels/ShellViewModel.cs` | RunSearch'e PK-O4 eklemesi (panel yolu aynen) |
| `Desktop/Views/MainWindow.axaml.cs` | Ctrl+K odak kısayolu |
| `Desktop/ViewModels/{Materials,Vehicles,Equipment}ViewModel.cs` + Views | `QrLabel` komutu + "QR Etiketi" düğmesi |
| `Desktop/FilePickerService.cs` | +`SavePngAsync` (eklemeli) |
| `Web/Components/Layout/MainLayout.razor` | RunSearch'e PK-O4 eklemesi + kutu id'si |
| `Web/Components/App.razor` | Ctrl+K dinleyicisi |
| `Web/Components/Pages/{Materials,Vehicles,Equipment}.razor` | QR düğmesi + indirme metodu |
| `tests/DepoWise.Tests/BarkodQrTests.cs` | **YENİ** — 15 test |

## 4. Testler

`BarkodQrTests` **15/15**: üç kaynaktan servis-çözümlü QR üretimi (BAR1) · içerik koda bağlı/deterministik +
güvenli dosya adı (BAR2) · Türkçe + boş içerik reddi (BAR3) · **QR ucunda kaynak yetkisi (BAR4)** ·
**QR ucunda tenant (BAR5)** · **tam-tek eşleşme açılır — kısmi komşular engellemez (BAR6)** · **iki kaynakta
aynı kod → açılmaz (BAR7)** · kısmi/sıfır → açılmaz (BAR8) · HasMore → açılmaz (BAR9) · **silinmiş kayıt
taranamaz (BAR10)** · **yetkisiz kaynak taranamaz / yetki verilince bulunur (BAR11)** · **BranchAccess
kapsam dışı taranamaz (BAR12)** · **tenant kodu taranamaz (BAR13)** · **tarama+tekrar QR üretimi kaynak
satırları BİT-BİT değiştirmez (BAR14)** · **şema 81'de kalır (BAR15)**.
Not: QRCoder'da çözücü yok (PK-O2 tek-bağımlılık kararı) — QR içeriği, üretimin deterministik ve girdiye
bağlı oluşuyla + girdinin servis-çözümlü kod oluşuyla kanıtlanır (BAR1+BAR2); ayrı decode kütüphanesi
bilinçli eklenmedi.
**Regresyon: TAM test paketi koşuldu (2883 test)** — 2845 geçti · 37 atlanan (PostgreSQL gerektiren
testler — yerel ortamda PG yok; production'a bağlanılmadı) · 1 başarısız: `TSR12_Katalog_Emojileri_Degismedi`.
Kök neden O DEĞİL: testteki sabit "17 grup" kilidi, FAZ 1-4'ün (C..L — canlıda yayında) eklediği 7 menü
grubuyla ÇOKTAN eskimişti; o turlarda tam paket değil hedefli paketler koşulduğundan görülmemişti (O grup
eklemez — kanıt: bu turda AppScreens'e dokunulmadı). Düzeltme gevşetme değil: sabit, kataloğun kendi
sayısına bağlandı (emoji-boşaltma denetimi TÜM gruplarda sürüyor; regex-katalog eşleşmesi artık eskiyemez).
Düzeltme sonrası sınıf 20/20; arama/Excel/barkod paketleri 122/122. Üç Release build (API+Web+Masaüstü)
**0 hata** (masaüstünde 1 derleme hatası bulunup düzeltildi: masaüstü grid satır tipinde alan adı `Code`).

## 5. Canlı veri güvenliği

Canlıya yazma YOK · production'a bağlanılmadı · mevcut kayıt değişimi YOK (BAR14 bit-bit) · fiziksel silme
YOK · **MIGRATION YOK (şema 81)** · deploy YOK. Tarama/QR üretimi hiçbir iş operasyonu TETİKLEMEZ
(stok/zimmet/İE/bakım/durum — hiçbiri; QR = navigasyon) · senkrona yeni veri girmez · SNK-13'e dokunulmadı ·
M import kapsamı değişmedi · ARA-01 güvenlik kapıları aynen (testle yeniden kilitlendi).

## 6. Offline / platform davranışı

Masaüstü: tarama YEREL SQLite'tan (çevrimdışı tam işlev; Proje/Evrak K'daki gibi çevrimiçi-yalnız), QR
üretimi YERELDE (sunucusuz). Web: tarama `/api/search`, QR indirme `/api/qr/...`. İki platformda aynı
kural: tara → global arama → tam-tek eşleşmede aç, aksi halde mevcut panel.

## 7. Bilinen sınırlar / gelecek

Kamera taraması YOK (v1 dışı — N Mobil fazının konusu) · EAN-13/üretici barkodu YOK (PK-O3; gerekirse ayrı
kanıtlı iş) · toplu etiket sayfası YOK (kayıt başına PNG) · masaüstü otomatik açılışta kayıt-AÇMA yalnız
mevcut 4 `IDeepLinkTarget` ekranında tam (diğerlerinde ekrana gidilir — K'daki mevcut sınır) · İş emri
çıktısına QR ileride eklemeli gelebilir. Adlar eşleşirse (iki kayıtta aynı AD) otomatik açılış zaten
devreye girmez (çoklu tam eşleşme kuralı).

## 8. Canlıya alınma durumu

⛔ **YAYINLANMADI** — yeni çalışma stratejisi gereği build+test seviyesinde tamamlandı; migration
olmadığından yayın bekleyen şema borcu yok (ileride toplu yayına yalnız kod olarak girer: API+Web+masaüstü paketi).

## 9. Sonraki roadmap işi

**N — Mobil (önce responsive web)** (FAZ 5/SIRA 15). 7b Bakım-Ekipman genişletmesi hâlâ serbest sırada.
