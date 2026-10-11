# Görsel iyileştirme raporu — masaüstü "yeni kayıt" ekranları (2026-10-12)

> **1–2. tur:** yalnız görünüm ve yerleşim değişti.
> **3. tur (kullanıcı onayıyla):**
> - form yükseklikleri düzeltildi;
> - menü "Yeni" girişleri formu açıyor;
> - kalan ekranlar tarandı.
>
> İş mantığı, servis, veritabanı, API, eşitleme ve yetki değişmedi. Kullanıcı commit'e izin verdi; yayın kullanıcı
> kontrolünden sonra.

## 1. İncelenen ekranlar ve ekran görüntüleri
30 ekran, "yeni kayıt" formu açık olarak üç boyutta çizildi:
- 1126 × 1200 (geniş, formun tamamı görünür);
- 940 × 1200 (dar);
- 1126 × 760 (gerçek dizüstü yüksekliği).

Görüntüler: `artifacts/ui-ekran/gorsel/<ekran>-<boyut>.png`. Bulgu listesi: `artifacts/ui-ekran/gorsel/bulgular.txt`.
Düzeltme öncesi ilk tarama: `artifacts/ui-ekran/gorsel/once/`.

| Grup | Ekranlar | Son durum |
|---|---|---|
| Öncelikli | Bakım, Depo Giriş, Depo Çıkış, Araç, Yakıt, Günlük Faaliyet, Cari, Kasa/Banka | temiz; Yakıt'ta yalnız tablo başlığı notu var (§5) |
| Sık kullanılan | Malzeme, Personel, Talep, İş Emri | temiz |
| Kalan ekranlar | Duyuru, Şube, Takvim, Firma, Maliyet Merkezi, Belge, Ekipman, Denetim, Proje, Satın Alma, Sürümler, Malzeme Şablonu, Araç Şablonu, Fatura, Ödeme, Zimmet, Kullanıcı, Sayım | temiz |

Otomatik denetimler (`GorselTaramaTests.Olc`):
- **TAŞMA:** alan veya düğme pencerenin sağından taşıyor.
- **KESİK METİN:** tek satırlık yazı kutusuna sığmıyor.
- **ÇAKIŞMA:** form alanları üst üste biniyor.
- **SIKIŞIK FORM** (yeni): form kendi içinde kayarken pencerenin altı boş kalıyor. Kullanıcının bulduğu hata buydu.
- **ULAŞILAMAZ** (yeni): bir alan veya düğme pencerenin altında kalıyor ve kaydırılarak da ulaşılamıyor.

**SIKIŞIK FORM denetiminin kendisi de doğrulandı:**
- Malzeme ekranının eski sürümünde hatayı yakaladı: "kaydırıcı 470/1265 px, altta 630 px boş".
- Düzeltilmiş sürümde temiz çıktı.
- Denetimin ilk sürümü bu hatayı kaçırıyordu: pencerenin görünmez arka plan katmanını içerik sayıyordu. Düzeltildi.

## 2. Düzenlenen dosyalar
**Ortak stil**
- `Themes/Controls.axaml`: `FormBolum` bölüm başlığı.
- `Themes/Components.axaml`:
  - kapalı metin kutusunun görünümü;
  - kapalı sayı kutusunun görünümü (3. tur). Kapalı sayı kutusu açık gri blok gibi görünüyordu.

**Davranış (onaylı)**
- `ViewModels/ShellViewModel.cs`: menüdeki "Yeni Araç Ekle / Yeni Cari / Yeni Fatura / Yeni Hesap" girişleri artık formu
  açık getiriyor. Form, ekrandaki "Yeni" düğmesinin aynı komutuyla açılıyor; ayrı bir yol yazılmadı. Yetki yoksa form
  açılmıyor, liste görünüyor (Malzeme'deki kalıpla aynı). Web bu girişlerde zaten formu açıyordu.

**Ekranlar** (`src/DepoWise.Desktop/Views/`)
- **Form yüksekliği (3. tur, kullanıcı bulgusu):**
  - `MaterialsView`, `VehiclesView`:
    - Form açıkken liste gizleniyor. Form artık listenin esneyen satırına yerleşiyor ve ekranda kalan tüm yüksekliği
      kullanıyor.
    - Daha önce 470 / 500 piksellik sabit bir kutuya sıkışıyordu.
  - `MaterialTemplatesView`, `VehicleTemplatesView`, `RequestsView`, `MaintenanceView` (2 form), `DailyActivityView`,
    `UsersView`:
    - Liste formla birlikte görünüyor.
    - Form, ekran yüksekliğinin oranıyla sınırlı (`FormListeOrani`, Depo Giriş ile aynı); büyük ekranda büyüyor.
    - Kullanıcılar formunda daha önce hiç kaydırma yoktu; dizüstünde "Oluştur" düğmesine ulaşılamıyordu.
- **Dar pencerede alt satıra geçen form satırları:** `MaintenanceView`, `FuelView`, `WorkOrdersView`, `PurchasingView`,
  `AnnouncementsView`.
- **Diğer:**
  - `PartiesView`, `FinanceView`: boş durum paneli, tam genişlik açılır kutular, Vazgeç → Kaydet sırası.
  - `PersonnelView`: "+" düğmesi.
  - `BranchesView`: sığmayan ipucu yazısı kısaltıldı; bilgi zaten alan açıklamasında var.
  - `CalendarView`: açıklama satırı alt satıra geçiyor.
  - `VehiclesView`, `MaterialsView`, `MaterialTemplatesView`, `VehicleTemplatesView`: `FormBolum` bölüm başlıkları.
  - Yer tutucu yazı birliği ("… seç / ara..."), 10 ekran.

**Testler**
- `tests/DepoWise.Desktop.UiTests/GorselTaramaTests.cs`: 30 ekran, 5 denetim, 3 boyut.
- `tests/DepoWise.Desktop.UiTests/serverurl.txt` + csproj: arayüz testleri canlı sunucuya hiçbir koşulda istek atamaz;
  adres erişilemez yerel adrese yönlenir. Oturum anahtarı olmadığı için zaten istek atılmıyordu; bu ek güvence.
- `tests/DepoWise.Tests/MasaustuYeniKayitFormuTests.cs` (yeni, 25 test):
  - menüdeki her ":new" girişi formu açar;
  - form yalnız yetki varsa açılır;
  - formlar sabit piksel kaydırıcıya konamaz;
  - oran bağlaması ve esneyen satır yerinde.

## 3. Ortak iyileştirmeler ve etkiledikleri ekranlar
| İyileştirme | Etkilenen ekranlar |
|---|---|
| Form yüksekliği ekrana göre (sabit piksel yok) | Malzeme, Araç, Malzeme Şablonu, Araç Şablonu, Talep, Bakım (Tanım + Araç Bakımı), Günlük Faaliyet, Kullanıcılar |
| Kapalı metin kutusu ve kapalı sayı kutusu sessiz görünüm | Tüm `TextBox.Field` ve `NumericUpDown.Field` kutuları |
| `FormBolum` bölüm başlığı | Araç, Malzeme, Malzeme Şablonu, Araç Şablonu |
| Sarmalı form satırları | Bakım, Yakıt, İş Emri, Satın Alma, Duyuru |
| Menü "Yeni" → form | Yeni Araç Ekle, Yeni Cari, Yeni Fatura, Yeni Hesap (Malzeme Yeni Kayıt zaten açıyordu) |

## 4. Çalıştırılan komutlar ve gerçek sonuçlar
| Komut | Sonuç |
|---|---|
| `scripts/run_ui_tests.ps1` (tam arayüz takımı) | 41/41 geçti |
| `scripts/run_ui_tests.ps1 -Filter G09_Malzeme`, eski Malzeme sürümüyle | SIKIŞIK FORM bulgusu üretti (beklenen) |
| `scripts/run_ui_tests.ps1 -Filter G09_Malzeme`, düzeltilmiş sürümle | bulgu yok |
| `scripts/run_tests.ps1 -Filter "<AXAML tarayan 36 test sınıfı + AppScreens + yeni test>" -PostgresAtla` | 371/371 geçti |
| `scripts/run_tests.ps1` (tam takım) | sonuç §4a'da |
| Masaüstü derleme | 0 hata |

### 4a. Tam takım
- `scripts/run_tests.ps1 -Bekle` (PostgreSQL dahil, 1.0.199 notu eklendikten sonra): **4101/4101 geçti, atlanan 0**,
  süre 37 dk 43 sn.
- Ardından aynı kodla `scripts/run_ui_tests.ps1`: **41/41 geçti**.
- Tek tarama bulgusu Yakıt tablosu başlığı, 940 px (§5, ayrı iş).

## 5. Başarısız testler ve çözülemeyen sorunlar
- Arada kararsız (flaky) bir test çıktı ve çözüldü. Yeni tarama testi ortak test ortamına araç ekliyor, bu da UIT10'u
  koşu sırasına göre düşürüyordu. Tohum kaldırıldı; tam takım 6 kez üst üste yeşil.
- **Tablo başlığı kayması — bu işte düzeltilmedi, ayrı iş önerisi:**
  - Belirti:
    - Pencere tablonun bütün sütunlarına yetmeyecek kadar darsa başlık satırı pencereye sıkışıyor.
    - Satırlar ise yatay kaydırılabildiği için kendi genişliğini koruyor.
    - Sonuç: "SAYAÇ" gibi esneyen sütunun başlığı daralıyor ve sonraki başlıklar bir sütun kayıyor.
  - Nerede görüldü: Yakıt dağıtım tablosu, 940 px. Boş Cari ve Kasa listelerinde de "Ünvan" / "Hesap Adı" başlığı
    sıkışık.
  - Kapsam: başlık + liste yapısı (`TableHeader` + `ListBox.Table`) uygulamadaki tabloların çoğunda aynı. Düzeltme ortak
    tablo yapısına dokunmayı gerektiriyor, bu yüzden ayrı ve testli bir iş olarak önerildi.
- **İki ortam denetimi (2026-10-12):**
  - Engel ve eksik yok; menü "Yeni" girişleri iki ortamda eş.
  - Eskiden beri süren küçük bir fark var: web'de `/parties/new` ve `/finance/new` boş formu yetki sormadan açıyor
    (`Parties.razor:399`, `Finance.razor:363`). Kayıt sunucuda yetkiyle reddedildiği için güvenlik açığı değil.
  - Masaüstünde form yetki yoksa hiç açılmıyor. Web'e tek satırlık `Auth.CanCreate` koşulu eklenmesi ayrı iş olarak
    önerildi; kullanıcı uyurken web yayını yapılmadı.
- Küçük görsel notlar, bırakıldı:
  - Cari ve Kasa form başlıkları ("Yeni Cari") karışık harfli; başlık ViewModel'den geliyor.
  - Depo Giriş "Şube (Şubeniz)" etiketi "Tüm Şubeler" modunda boş görünüyor.

## 6. Headless ile gerçek pencere arasındaki bilinen farklar
- Kabuk (sol menü, üst çubuk) yok. Gerçekte kullanılabilir genişlik ve yükseklik daha az; 1126 × 760 boyutu bunu
  yaklaşık olarak temsil ediyor.
- Yazı tipi Inter ile çiziliyor. ClearType yok ve ölçek 96 DPI, bu yüzden görüntüler pikseli pikseline aynı değil.
- Test verisi az: bir şube, bir malzeme, bir personel. Başka test sınıflarının kayıtları da koşu sırasına göre listelerde
  görünebiliyor (ör. Yakıt'taki SRK-01).
- Arayüz testleri sunucuya bağlanmıyor. Belgeler, Projeler, Takvim ve Kullanıcılar listeleri bu yüzden boş.

## 7. Kalan iş
- Tablo başlığı kayması (§5): ortak tablo yapısında ayrı, testli iş.
- Kullanıcının gerçek pencerede kontrolü, özellikle açık temada.
- Yayın: kullanıcı kontrolünden sonra.
