# FAZ A — Kullanıcı bug'ları + yetki tamamlama

> **Durum:** ✅ TAMAMLANDI · **2026-09-04** · ADR-209
> **Kapsam:** `YTK-05` · `UIX-01` · `YTK-06` · `YTK-08`
> **Migration:** GEREKMEDİ (dördü de arayüz/test katmanı)

---

## Önce ölçüldü: dördünün ne kadarı zaten yapılmıştı

Bu fazın kalemleri yol haritasına **2026-08 öncesinde** yazılmıştı. Aradan geçen G1/G2/G3 turları
bazılarını farkında olmadan büyük ölçüde tamamlamıştı — bu yüzden ilk iş kod yazmak değil,
**bugünkü gerçeği yeniden ölçmek** oldu. Eskimiş bir "yapılacak" varsayımıyla çalışmak, ya aynı işi
ikinci kez yapmak ya da gerçek boşluğu kaçırmak demekti.

| İş | Zaten var olan | **Gerçekte kalan boşluk** |
|---|---|---|
| `YTK-05` | Toptan yazma altyapısı (tek çağrıda full-replace + sürüm kilidi + tavan kırpma), grup başına "Tümünü Seç / Temizle", sunucuya yazan "Yetkileri Sıfırla" | **Tüm ağacı** kapsayan "Tümünü Temizle" — iki platformda da yoktu |
| `UIX-01` | Kök neden çözülmüştü (G3: `TableRowSelect`, tünelleme) ve ortak `ListBox.Table` stiline bağlıydı | Ortak stili **kullanmayan 3 ekran** düzeltmenin dışında kalmıştı → hata orada **hâlâ canlıydı** |
| `YTK-06` | `AppScreens` tek kaynak + 20'den fazla parite testi; masaüstü yönü kilitli (`S9`) | **Web yönü açıktı** — kataloğa yazılmamış yeni bir `.razor` sayfası hiçbir testi kırmadan geçerdi |
| `YTK-08` | Devretme tavanı hem UI hem servis katmanında; 7 regresyon testi (`G1b_*`) | **Yok** — iş zaten bitmişti, yalnız yol haritası güncellenmemişti |

---

## `YTK-05` — Tümünü Temizle

**Sorun.** Yetki ağacı 8 kategoriye ayrılmış durumda ve her kategoride "Temizle" var. Bir kullanıcıya
**sıfırdan** yetki kurarken (en sık yapılan iş) 8 grubu tek tek temizlemek gerekiyordu.

**Yapılan.** Düzenleme modunda görünen tek bir **"Tümünü Temizle"** düğmesi — iki platformda da.

**"Yetkileri Sıfırla"dan farkı (bilinçli olarak ayrı düğme):**

| | Tümünü Temizle | Yetkileri Sıfırla |
|---|---|---|
| Ne yapar | Ekrandaki kutuları boşaltır | Sunucuda yetkileri **siler** |
| Geri alınabilir mi | Evet — Vazgeç eski hâli getirir | Hayır |
| Sunucuya yazar mı | **Hayır** (Kaydet'e kadar hiçbir şey olmaz) | Evet, anında |
| Görünüm | Nötr | Kırmızı (yıkıcı) |

İkisi de onay penceresi sorar; metinler teknik terim içermez.

- Masaüstü: `PermissionsViewModel.ClearAllPerms` + `PermissionsView.axaml` (Kaydet'in yanında)
- Web: `Permissions.razor` → `ClearAllPerms()`, mevcut `PermMatrix.Clear()` kancasına bağlandı

## `UIX-01` — Tablo satır seçimi

**Geçmiş.** G3 (2026-08-12) kök nedeni çözmüştü: satır metinleri `SelectableTextBlock` ile yazılıyor
ve bu kontrol tıklamayı **tüketiyor**, olay satıra hiç ulaşmıyor. Çözüm olayı **tünelleme**
aşamasında yakalamaktı (`TableRowSelect`), ortak `ListBox.Table` stiline bağlandı.

**Kalan açık.** Ortak stili kullanmayan, ama tablo gibi davranan **3 çıplak liste** düzeltmenin
dışında kalmıştı. Üçünde de seçim **işlevseldir** — satır seçilemeyince düğmeler hiçbir şey yapmaz:

| Ekran | Seçim neye bağlı | Kullanıcının gördüğü |
|---|---|---|
| Bekleyen Onaylar | Onayla / Reddet | Satıra tıklıyor, Onayla tepkisiz |
| Ekip Listesi | Sağdaki üyeler paneli | Ekip adına tıklıyor, panel açılmıyor |
| Ekipman Bakım Kayıtları | Düzenle / Sil | Satır seçilmiyor |

**Yapılan.** Üç listeye davranış doğrudan bağlandı (`ctrl:TableRowSelect.Enabled="True"`).
`Classes="Table"` eklenmedi — o, görünümü de değiştirirdi; burada amaç **davranışı** düzeltmekti.

**Kapsam kilidi (asıl değerli kısım).** `TabloSatirSecimiKapsamTests` bütün masaüstü ekranlarını
tarar: satır şablonunda `SelectableTextBlock` olan ve `SelectedItem`'a bağlı **her** liste, ya ortak
stili kullanmalı ya da davranışı açıkça bağlamalıdır. Yeni bir ekran aynı hataya düşerse test kırılır
— bu sınıf hata bir daha **sessizce** geri gelemez.

**Web ölçüldü, kusur ÇIKMADI.** Kullanıcının kuralı gereği "aynı hata web'de de vardır" varsayılmadı:
- `MudTable`'da hücre içeriği düz metindir, tıklama `<tr>`'ye ulaşır → satır tıklaması **çalışıyor**.
- `dw-grid` tablolarında etkileşim çift tıkla açılan düzenleme penceresidir ve **çalışıyor**.
- Ortak `DwDataGrid` yalnız **Raporlar** ekranında kullanılıyor: salt-okunur rapor çıktısı, satır
  seçiminin bir anlamı yok → oraya seçim eklemek gereksiz kapsam büyütmesi olurdu, yapılmadı.

Tek bulgu: `Vehicles.razor` ve `Materials.razor` içinde **kodla çelişen eski bir yorum** vardı
("tek tık = sağdaki detay paneli") — o panel kaldırılmıştı. Yanıltıcı yorum silindi.

## `YTK-06` — Yeni ekranın yetki kataloğuna otomatik dâhil olması

**Mevcut mekanizma zaten güçlü:** yeni ekran = `AppScreens`'e **tek satır**; menüler o listeden
üretilir ve 20'den fazla parite testi her katmanın gerçekten beslendiğini doğrular.

**Bulunan açık — tek yönlüydü.** `S9` masaüstünde "katalogda olmayan ekran kalmadı" kilidini
kuruyordu; **web yönü yoktu**. Yeni bir `.razor` sayfası eklenip kataloğa yazılmazsa ekran menüde
çıkmaz, **yetki ağacından yönetilemez** ve platform yönetiminin dışında kalır — üstelik hiçbir test
kırılmadığı için bu **sessizce** olurdu.

**Yapılan.** `S9b_Webde_Yetim_Ekran_Yok` — her `@page` route'u kataloğa bağlı olmalı; liste büyürse
test kırılır ve yeni satır bilinçli bir karar gerektirir.

**Test ilk koşuşunda 7 aday buldu; hepsi tek tek incelendi — hiçbiri hata değildi, hepsi kayıtlı
istisna çıktı.** Bu ayrımı yapmadan listeye "geçsin diye" eklemek testi anlamsızlaştırırdı:

| Route | Karar | Gerekçe |
|---|---|---|
| `/` · `/login` · `/Error` | istisna | Ana ekran, giriş sayfası ve ASP.NET Core hata sayfası — ekran değil |
| `/fuel` · `/maintenance` | istisna | **Takma ad**: grup adresine gidilince birincil alt ekran açılır; katalog alt ekranları tutar (`fuel/dist`, `maintenance/defs`). Masaüstündeki `S9` istisnalarının birebir karşılığı |
| `/material-templates` · `/stock/distribute` | istisna | Katalogda **bilinçli olarak** masaüstü işaretli ve gerekçesi satırın üstünde yazılı: biri "web'de ekran var ama menüde listelenmiyor", diğeri STK-08 "web'de Stok İşlemleri ekranından açılır" |

Son ikisi ilk bakışta kusur gibi göründü (web sayfası var, katalog "yok" diyor) — git geçmişi ve
katalog yorumları kontrol edilince **kayıtlı bir tasarım kararı** olduğu görüldü. Kataloğu
"düzeltmek" burada yanlış olurdu: web menüsüne, projenin bilerek koymadığı iki giriş eklerdi.

## `YTK-08` — Delegasyon tavanı regresyon testi

**Ölçüm sonucu: iş zaten bitmiş.** Kural ("kimse kendinde olmayan yetkiyi veremez") UI'da değil
**servis katmanında** zorunlu; API'yi atlayıp doğrudan servis çağrısı da aynı kapıdan geçiyor.
7 regresyon testi mevcut (`PermissionGrantCeilingTests.G1b_*`): aksiyon kırpma · aktörde hiç olmayan
modülün satırının bile yazılmaması · role kapatılmış modülün firma admini tarafından bile
devredilememesi · süper adminin sınırsız kalması · buton yetkisi · tavan ile etkin yetkinin aynı
sonucu vermesi.

Yol haritası satırı güncellendi; **kod değişikliği gerekmedi**.
