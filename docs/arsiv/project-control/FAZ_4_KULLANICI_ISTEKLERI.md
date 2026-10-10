# FAZ 4 — KULLANICI İSTEKLERİ (2026-09-06)

> **Kaynak:** kullanıcının 2026-09-06 tarihli promptu (masaüstünde yapılan elle testler + yeni istekler).
> **Durum:** 🔄 **UYGULAMADA** (2026-09-06 başladı). Test promptu alındı: `FAZ_4_TEST_PROMPTU.md`.
> Bu dosya, prompt unutulmasın/parçalanmasın diye **birebir kayıttır**; fazlara ayrılmıştır.
>
> ⚠️ Bu dosya `docs/project-control/CURRENT_PHASE.md` ile birlikte okunur. FAZ 3c-2 + 3d tamamlandı,
> yayınlanmadı; HEAD `aba7f7a`.

---

## İLERLEME (2026-09-06 · canlı güncellenir)

| Faz | Durum | Not |
|---|---|---|
| 4.1 Sayaç hatası | ✅ | Kök neden: `vehicles.current_meter` yalnız-ileri giden SAKLI değerdi; düzeltme/iptal ona dokunmuyordu. Sayaç artık GEÇERLİ kayıtlardan türetiliyor (elle beyan tabanı korunur). Y2 kuralı kullanıcı talimatıyla tersine çevrildi. Şüpheli sıçrama uyarısı eklendi. 12 yeni test. |
| 4.16 Personel–kullanıcı bağlama | ✅ | Kök neden: sabit `IsAdmin` kapısı + arayüzde yutulan yetki hatası → yanıltıcı "bağlanabilir kullanıcı yok" mesajı. Yeni yetki kalemi `btn-link-user` (migration yok). 5 test. |
| 4.13 Mükerrer "Tamam" | ✅ | 5 ayrı çağrıda ok ve cancel metni aynıydı. Tek butonlu `ConfirmService.InfoAsync` eklendi; regresyon testi hatanın geri gelmesini engelliyor. 3 test. |
| 4.8 Araç bakımları | ✅ | PLAKA kolonu (liste + detay) ve tarih/araç kodu/plaka sorgulama eklendi — servis bunu zaten destekliyordu, eksik olan arayüzdü. 5 test. |
| 4.15 Excel şube şifresi | ✅ | Şifre girilmeden ya da yanlışken "İçe Aktar" butonu AKTİF DEĞİL; sebep ekranda yazılı. İki platform. |
| 4.2 Onay uyarıları | ✅ | Masaüstünde 16, web'de 14 komuta standart onay eklendi; form içi işlemler gerekçeli muaf. Sözleşme testi geri sızmayı engelliyor. 3 test. |
| 4.14 Kolon kalıcılığı | ⏳ | sırada |
| 4.9 Günlük faaliyet filtreleri | ⏳ | |
| 4.7 Araç sekmeli pencere | ⏳ | |
| 4.5 + 4.6 Tanımlar / "+" yönetimi | ⏳ | |
| 4.10 Şablon dışı yetki | ⏳ | |
| 4.11 Tam ekran | ⏳ | |
| 4.12 Senkron animasyonu | ⏳ | |
| 4.3 Log ekranı | ⏳ | |
| 4.4 Senkron çakışma ekranı | ⏳ | |

---

## 0. HER FAZDA GEÇERLİ KURALLAR (kullanıcının açık şartları)

1. **İki ortam zorunlu.** Testler masaüstünde yapıldı; ama *"belirttiğim hatalar web'te de var anlamına
   gelmiyor"*. **Her istek için hem masaüstü hem web ayrı ayrı analiz edilir.** Ortamlardan biri analiz
   edilmeden işleme devam edilmez.
2. **İlgili ve etkilenen TÜM alanlar eksiksiz kontrol edilir.** Yarım bırakma yok.
3. **Çalışan hiçbir yapı bozulmayacak.**
4. **Onay isteme yok** — yayına kadar tüm yetki Claude'da. Bozuk/eksik kısımlar kendi önerisiyle
   tamamlanır. (FAZ 4.6'nın "serbest metni sabit tanımlıya çevirme" kısmı kullanıcı tarafından
   **İPTAL** edildi; bilgilendirme istisnası kalktı.)
5. **Test:** işler bittikten sonra kullanıcı ayrı bir **test promptu** verecek; o prompt bu klasöre
   kaydedilecek. Kullanıcı *"projeyi detaylı test etmeni istiyorum"* dediğinde o dosya bulunur,
   analiz edilir ve test başlatılır.
6. **Yayın:** tüm testler başarılı olduğunda **otomatik ve eksiksiz yayınla** (ayrıca onay istenmez).
7. **Bitişte:** rapor sunulduktan, çalışan test kalmadığı doğrulandıktan sonra **bilgisayar uykuya alınır**.

---

## FAZ 4.1 — 🔴 SAYAÇ (KM/SAAT) HATASI — EN YÜKSEK ÖNCELİK

**Kullanıcının anlattığı olay:** `mustafa.alpaslan` (babası), **Yakıt Dağıtımları** ekranından bir araca
**yanlış sayaç** girdi. Hatalı kaydı **güncelledi**, ama sistem hâlâ **hatalı sayacı** gösteriyor.
Yeni yakıt girerken sayaç alanı **maskeli/kilitli** geldiği için revize de edemiyor.

- **Hatalı araç:** `KAM-ME 059` — plaka `06 FZ 4146`
- Kurgu: *"projeyi en yüksek sayaç bilgisi hangi kayıtta ise ondan al"* şeklindeydi.
- Kullanıcı **Bakım** ekranını kontrol etti, hatalı km bulamadı.
- **Bu sorun başka araçlarda da var.**

**Yapılacak:** kök nedeni tespit et (yalnız yakıt değil; sayaç yazan TÜM kaynaklar: yakıt dağıtımı,
yakıt depo, bakım, muayene, günlük faaliyet, iş emri, ekipman bakımı, import/Excel), düzelt ve
**tekrar yaşanmaması için** kalıcı önlem uygula. Düzeltme **canlı veriyi bozmadan** yapılmalı.

---

## FAZ 4.2 — DÜZENLE / SİL / İPTAL ONAY UYARILARI (proje geneli)

Tüm **Düzenle**, **Sil** ve **İptal** butonlarında, işlem yapılmadan önce onay sorulmalı:

- Düzenle → *"Kaydı düzenlemek istediğinize emin misiniz?"*
- Sil → *"Kaydı silmek istediğinize emin misiniz?"*
- İptal → aynı mantık.

Kurallar:
- Butona **zaten benzer bir kontrol/mesaj bağlıysa o buton PAS GEÇİLİR** (mükerrer uyarı olmaz).
- Farklı koşullar sebebiyle ek mesaj varsa: **önce bu genel mesaj**, sonra diğer koşulun mesajı.
- Masaüstü **ve** web.

---

## FAZ 4.3 — LOG / DEĞİŞİKLİK GEÇMİŞİ (anlaşılır hâle getirme)

Mevcut log bilgisi **anlaşılır değil**. İstenen:

- İşlem **tarihi + saati**
- **Yapılan işlem**
- İşlemin **önceki ve sonraki hâli** (hangi alanda ne değişti)
- **Günlere ayrılmış** görünüm: *"bugün şunu yapmış, ertesi gün bunu yapmış"*
- Ekranların yanı sıra **her kaydın kendine ait bir log ekranı** olmalı.

**Yapılacak:** tüm ekran ve kayıt alanları eksiksiz analiz edilerek uygulanır (masaüstü + web).

---

## FAZ 4.4 — SENKRON ÇAKIŞMALARI (uyarı + yeni ekran + müdahale)

- Çakışma uyarısı **web'de var, masaüstünde de olmalı**.
- Uyarı eksik: **hangi kayıt neyi ezdi**, **ne başarılı oldu** görünmüyor.
- Uyarıda **kazanan kullanıcı kim**, **kime karşı kaybedildi** kısa ve net yazmalı.
- Uyarıya **tıklanınca**: senkron çakışmalarının kaydını **ayrı tutan yeni bir ekran** açılmalı;
  uyarı o ekranın **ilgili kaydına köprü** olmalı.
- Bu ekranda: **ezilen kullanıcının kaydı iptal edilip istenen kayıt "kazanan" yapılabilmeli**.
- Kazanan durumuna göre **kayıtlar güncellenmeli ve doğru sonuç vermeli**.
- **Oluşabilecek TÜM senaryolarda test edilmeli.**

---

## FAZ 4.5 — TANIMLAR EKRANI EKSİKLERİ

- Her ekranın **yeni kayıt formu** detaylı analiz edilir.
- Yanında **"+" ile ekleme butonu olan HER alan**, **Tanımlar** ekranına eklenir (eksikler kapatılır).
- Bundan sonra **yeni bir alana "+" eklenirse Tanımlar'a otomatik girmeli**.

---

## FAZ 4.6 — "+" BUTONU YÖNETİMİ (kapsam KÜÇÜLTÜLDÜ — 2026-09-06)

> 🔴 **KULLANICI KARARI (2026-09-06): "serbest metni sabit tanımlama işi İPTAL."**
> Açıklama gibi serbest metin alanlarını sabit tanımlı listeye çevirme özelliği **YAPILMAYACAK**.

**Kalan kapsam:** yalnızca **zaten sabit tanımlı (liste) olan** alanlar için, firma bazında
yanlarına **"+" (hızlı ekleme) butonu** verilip alınabildiği bir yönetim ekranı/bölümü.
Uygun bir mevcut ekrana (Alan Ayarları / Tanımlar) konumlandırılır; yeni ekran açmak şart değildir.

Bu haliyle FAZ 4.5 ile aynı aileden olduğu için **4.5 ile birlikte** uygulanır.

---

## FAZ 4.7 — ARAÇLAR LİSTESİ: SEKMELİ BİLGİ PENCERESİ

- Bugün: çift tıklama ile araç bilgi penceresi açılıyor.
- İstenen: bu pencere **sekmeli** hâle gelsin.
- Tabloda **tek sol tık** yapıldığında, sağdaki bilgi panelindeki veriler **ek olarak sekmeler hâlinde**
  bu pencerede görüntülensin.

---

## FAZ 4.8 — ARAÇ BAKIMLARI EKRANI: 2 EKSİK

1. Araçların listelendiği ekranda **plaka bilgisi yok**.
2. **Tarih, araç kodu ve plaka** ile sorgulama yapılacak **alan ve butonlar görünmüyor**.

---

## FAZ 4.9 — GÜNLÜK FAALİYET: TARİH ARALIĞI + ÇOKLU ARAÇ

- **Tarih aralığı** sorgulama alanı eklenmeli.
- Sonrasında tablo üzerindeki filtrelerden kullanıcı kendi sorgusunu yapabilmeli.
- **Birden fazla araç seçebilme** yapısı buraya da eklenmeli.

---

## FAZ 4.10 — ŞABLON DIŞI ARAÇ/MALZEME EKLEME → YETKİYE BAĞLANSIN

Şablon dışı araç ve malzeme eklemek **yetkiye tabi** olmalı; firmalar bunu kontrol edemeyebiliyor.
(Yeni yetki kalemi → yetki ağacına eklenir; `module_key` serbest metin olduğu için migration gerekmez.)

---

## FAZ 4.11 — MASAÜSTÜ: LOGIN SONRASI TAM EKRAN

Masaüstü uygulama, **login'den sonra ilk açılışta varsayılan olarak tam ekran** açılmalı.

---

## FAZ 4.12 — MASAÜSTÜ: SENKRON GERİ SAYIM + İLERLEME ANİMASYONU

- **Üst barda**, senkrona **kalan süreyi** ifade eden **animasyonlu görsel** (⚠️ **saniye görünmesin**).
- Boyut ve konum üst bara uygun olmalı.
- **Senkron başlayınca** bu animasyonun yerini **yüzdeli** bir ilerleme animasyonu almalı.
- Animasyonlar **modern** olmalı; gerekirse web'de araştırma yapılacak.

---

## FAZ 4.13 — MANUEL SENKRON PENCERESİ: MÜKERRER "TAMAM" BUTONU

Manuel senkron tetiklendikten sonra senkron alanına tıklanınca açılan pencerede **2 adet "Tamam"**
butonu çıkıyor. İsim hatalıysa düzelt, **mükerrerse kaldır**.

---

## FAZ 4.14 — "KOLONLARI AYARLA" SEÇİMLERİ KALICI OLSUN

"Kolonları Ayarla" → Kaydet dendiğinde, kullanıcı **yeni bir değişiklik yapana kadar** her login'de
aynı seçim geçerli kalsın. Her oturumda kolon ekleyip çıkarmak zorunda kalınmasın.

---

## FAZ 4.15 — EXCEL MERKEZİ: ŞUBE ŞİFRESİ KAPISI

Excel Merkezi'nde **farklı şube** seçildiğinde, şube şifresi **hiç girilmemiş ya da yanlış girilmişse**:
**"Excel'den içe aktar" butonu aktif olmadan önce** şifre uyarıları verilmeli ve **işleme devam
edilmemeli**.

---

## FAZ 4.16 — PERSONELE KULLANICI BAĞLAMA: HATA + YETKİ

1. 🔴 **Hata:** Personel ekranından personele kullanıcı bağlanmak istendiğinde, içeride tanımlı
   kullanıcılar olduğu hâlde liste boş geliyor ve şu uyarı çıkıyor:
   *"Bağlanabilir (henüz bir personele bağlı olmayan) kullanıcı yok. Önce 'Kullanıcılar' ekranından
   hesap açın."*
2. **Yetki:** Personele kullanıcı bağlama **ayrı bir yetki** olmalı ve **yetki ağacına eklenmeli**.
   Personel/Kullanıcılar ekranına erişen herkes değil, **yalnız bağlama yetkisi olan** kullanıcı
   bağlayabilmeli.

---

## SIRA ÖNERİSİ (uygulama sırası)

| # | Faz | Neden bu sırada |
|---|---|---|
| 1 | **4.1 Sayaç hatası** | Canlı veride yanlış sonuç üretiyor; babanın günlük işini engelliyor. |
| 2 | **4.16 Personel–kullanıcı bağlama** | Açık fonksiyon hatası (ekran çalışmıyor). |
| 3 | **4.13 Mükerrer Tamam butonu** | Tek noktalı, hızlı düzeltme. |
| 4 | **4.8 Araç bakımları eksikleri** | Küçük, kapalı kapsam. |
| 5 | **4.15 Excel şube şifresi kapısı** | Güvenlik/veri bütünlüğü kapısı. |
| 6 | **4.2 Onay uyarıları** | Proje geneli, mekanik ama geniş. |
| 7 | **4.14 Kolon seçimi kalıcılığı** | Kullanıcı ayarı kalıcılığı. |
| 8 | **4.9 Günlük faaliyet filtreleri** · **4.7 Araç sekmeli pencere** | Ekran işleri. |
| 9 | **4.5 Tanımlar eksikleri** · **4.10 Şablon dışı yetki** | Katalog + yetki. |
| 10 | **4.11 Tam ekran** · **4.12 Senkron animasyonu** | Masaüstü kabuk. |
| 11 | **4.3 Log ekranı** · **4.4 Senkron çakışma ekranı** | En büyük iki iş; en sona. |
| 12 | ~~4.6 Alan tipi ekranı~~ | **İPTAL** — yalnız "+" butonu yönetimi kaldı, 4.5 ile birlikte yapılır. |

---

## TEST VE YAYIN

- Kullanıcı ayrı bir **test promptu** verecek → bu klasöre `FAZ_4_TEST_PROMPTU.md` olarak kaydedilecek.
- Tetikleyici cümle: **"projeyi detaylı test etmeni istiyorum"** → o dosya bulunur, analiz edilir, test başlar.
- Tüm testler başarılıysa → **otomatik, eksiksiz yayın** (ek onay istenmez).
- Rapor sunulduktan ve **çalışan test kalmadığı doğrulandıktan** sonra → **bilgisayar uykuya alınır**.

---

## UYGULAMA DURUMU (2026-09-06 — tüm fazlar tamamlandı)

| Faz | Konu | Durum | Ana dokunulan yerler |
|---|---|---|---|
| 4.1 | Araç sayacı düzeltilemiyor | ✅ | `VehicleMeterService` (yeni) · `FuelService` · `MaintenanceService` · API `meter/recalc` · web+masaüstü |
| 4.2 | Düzenle/Sil/İptal onayları | ✅ | `ConfirmService` · `DialogExtensions` · tüm ekranlar (muaf listesi belgelendi) |
| 4.3 | Anlaşılır log + kayıt logu | ✅ | `AuditFields`/`AuditDiff`/`AuditSnapshot` (yeni) · `AuditWriter` · `AuditLogService.ForEntity` · `RecordHistoryWindow` · `AuditHistoryDialog` |
| 4.4 | Senkron çakışma ekranı | ✅ | Migration094 · `BusinessSyncService.PromoteLoser` · `SyncConflictsWindow` · `/sync-conflicts` |
| 4.5 | Tanımlarda eksik alanlar | ✅ | `LookupService` · `Definitions.razor` · `PersonnelTitleEditor` |
| 4.6 | "+" düğmesi yönetimi | ✅ (serbest metin→sabit tanım İPTAL) | `LookupPlusCatalog` (yeni) · Alan Ayarları (web+masaüstü) |
| 4.7 | Araç penceresi sekmeli | ✅ | `VehicleQuickEditWindow` |
| 4.8 | Araç bakımları: plaka + sorgu | ✅ | `MaintenanceService` (plaka) · bakım listesi filtreleri |
| 4.9 | Günlük faaliyet filtreleri | ✅ | `DailyActivityService` (tarih aralığı + çoklu araç) · API · web+masaüstü |
| 4.10 | Şablon dışı ekleme yetkisi | ✅ | `btn-template-free-create` · `MaterialService.SablonKapisi` |
| 4.11 | Masaüstü tam ekran | ✅ | `MainWindow` açılış durumu |
| 4.12 | Senkron geri sayım animasyonu | ✅ | `ShellViewModel` halka sayacı · `ArcProgressConverter` |
| 4.13 | Mükerrer "Tamam" düğmesi | ✅ | Senkron penceresi |
| 4.14 | Kolon seçimi kalıcılığı | ✅ | `ServerListPrefsClient` (yeni) — seçim sunucuda da saklanır (ikinci PC'de kaybolmaz) |
| 4.15 | Excel Merkezi şube şifresi | ✅ | `ImportExcel.razor` + masaüstü karşılığı |
| 4.16 | Personele kullanıcı bağlama | ✅ | `btn-link-user` · `UserService.RequireLinkPermission` |

### Yönetici dikkatine (yeni yetkiler — deny-by-default)

Aşağıdaki üç yetki yeni eklendi ve **hiç kimseye otomatik verilmez**. Firma yöneticisi, Yetki Ağacından
ilgili rollere vermelidir; verilmezse ilgili düğme görünmez/çalışmaz (mevcut kayıtlar etkilenmez):

- **Şablon Dışı Araç / Malzeme Ekleme** (`btn-template-free-create`) — FAZ 4.10
- **Personele Kullanıcı Bağlama** (`btn-link-user`) — FAZ 4.16
- **Senkron Çakışmasını Çözme (Kazananı Değiştirme)** (`btn-conflict-resolve`) — FAZ 4.4

### Şema

- **Migration094 `conflict_snapshots`** — yalnız `ADD COLUMN` (`data_conflicts`: `winner_json`,
  `loser_json`, `resolution`, `resolved_by`, `resolved_at`). Backfill/UPDATE/DELETE **yok**; mevcut
  çakışma kayıtları olduğu gibi kalır. FAZ 4.4'ün "üzerine yazılanı geri getir" isteği bu veri
  olmadan teknik olarak karşılanamıyordu.
