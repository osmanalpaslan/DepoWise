# YARIM İŞLER VE EKRAN STANDARDİZASYONU ANALİZİ

**Tarih:** 2026-08-09 · **Ürün:** Alpnex (web + masaüstü)
**Durum:** YALNIZ ANALİZ — kod yazılmadı, migration yok, deploy yok, canlı veriye dokunulmadı.

> Terim notu: *CRUD* = oluştur/oku/güncelle/sil · *servis katmanı* = ekranın arkasındaki iş kuralı kodu ·
> *migration* = veritabanı yapısı değişikliği · *parite* = web ile masaüstünün aynı işi yapabilmesi.

---

## ÖNCE: LİSTENDEKİ 5 MADDENİN DOĞRULAMASI

| # | Senin maddеn | Kod doğrulaması | Sonuç |
|---|---|---|---|
| 1 | Çift tık ile ayrı pencere: yalnız Malzeme + Araç | `DoubleTapped` yalnız `MaterialsView` + `VehiclesView`; pencereler `MaterialQuickEditWindow`, `VehicleQuickEditWindow` | ✅ **Doğru** |
| 2 | Düzenleme kilidi eksik | `expectedVersion` yalnız 4 serviste: Material, Vehicle, Personnel, MaintenanceDefinition | ✅ **Doğru** — ama sebebi beklediğinden farklı (aşağıda) |
| 3 | Masaüstünde Malzeme Şablonları ekranı yok | `MaterialTemplatesView.axaml` + `MaterialTemplatesViewModel.cs` **mevcut** | ❌ **ARTIK GEÇERSİZ — bu iş TAMAMLANMIŞ** |
| 4 | Araç hızlı düzenlemede Sürücü eski tip | `VehicleQuickEditWindow` → `AutoCompleteBox` | ✅ Doğru — **ama tek o değil** (aşağıda) |
| 5 | Görev panosu adayları | `docs/GOREV_PANOSU.md` Görev B | ✅ Doğru |

---

## A) BİLİNEN YARIM İŞLER — ve gerçek durumları

### A1. Çift tık → ayrı pencerede Düzenle/Kaydet/Sil

Mevcut: **Malzemeler**, **Araçlar** (masaüstü). Eksik: Günlük Faaliyet, Personel, Stok Giriş/Çıkış, Yakıt,
Bakım, Talepler.

**🔴 Kritik tespit:** Bu, sandığın gibi "sadece ekran işi" değil. Kod incelemesinde şu çıktı:

| Servis | Oluştur | **Güncelle** | Sil / İptal |
|---|---|---|---|
| `MaterialService` · `VehicleService` · `PersonnelService` | ✅ | ✅ | ✅ |
| `DailyActivityService` | ✅ | ❌ **YOK** | ✅ (soft-delete) |
| `FuelService` | ✅ | ❌ **YOK** | ❌ **YOK** |
| `MaintenanceService` | ✅ | ❌ **YOK** | ✅ (Cancel = ters kayıt) |
| `RequestService` | ✅ | ✅ (durum/onay) | ✅ |

Yani **Günlük Faaliyet, Yakıt ve Bakım kayıtlarında "Düzenle" diye bir şey sistemde hiç yok.** Ekrana buton
koymak yetmez; önce servis + API + iki arayüz yazılması gerekir. Üstelik bu kayıtlar **stok ve yakıt
bakiyesini etkiliyor** → "düzenleme" doğrudan yapılırsa defter bozulur.

### A2. Düzenleme kilidi (aynı kaydı iki kişi açarsa ikincisi ezmesin)

Mevcut: Malzeme, Araç, Personel, Bakım Tanımı (`EditLockGuard` + `expectedVersion`).
Eksik: Günlük Faaliyet, Yakıt, Bakım kaydı.

**Bağımlılık:** Bu üç ekranda **düzenleme zaten yok** (A1) → kilit eklenecek bir şey de yok.
**Kilit, A1 tamamlanmadan yapılamaz.** Sırayı bu belirliyor.

### A3. Masaüstü Malzeme Şablonları — ✅ **TAMAMLANMIŞ**

`MaterialTemplatesView` masaüstünde var ve menüde kayıtlı. Bu madde listeden düşülebilir.

### A4. Araç hızlı düzenleme penceresi — sorun daha geniş

`VehicleQuickEditWindow`: Sürücü alanı `AutoCompleteBox`, ayrıca **6 adet `ComboBox`**.
`MaterialQuickEditWindow`: **6 adet `ComboBox`**, hiç `LookupBox` yok.

→ Yani **iki hızlı düzenleme penceresi de** ortak arama/listeleme bileşenine geçmemiş; yalnız Sürücü değil.

### A5. Görev panosu bekleyen adaylar

Giriş-Çıkış'ta çoklu malzeme · Makine bazlı güncelleme yetkisi · Yedek ekranları · Giriş hız sınırı kararı.

---

## B) WEB ↔ MASAÜSTÜ EKSİK/TUTARSIZ ÖZELLİKLER

### 🔴 B1. Excel içe aktarma YALNIZ masaüstünde

| Kanıt | Sonuç |
|---|---|
| API'de `api/import` ucu sayısı: **0** | Sunucuda içe aktarma ucu **hiç yok** |
| Web'de içe aktarma sayfası | **Yok** (yalnız "Excel'e Aktar" = dışa aktarma var) |
| Masaüstü `ImportExportViewModel` | **Var** (7 içe aktarma servisi kullanıyor) |

→ Web kullanıcısı **hiçbir toplu veri aktarımı yapamıyor**. Bu, iki platform arasındaki **en büyük işlevsel
fark**. (Not: az önce tamamladığımız Şube/Şantiye import kuralı da yalnız masaüstünde etkili.)

### B2. Çift tık → hızlı düzenleme penceresi kavramı web'de yok

Web farklı bir desen kullanıyor (satıra tıkla → form/dialog). **Bu mutlaka birebir aynı olmak zorunda değil**,
ama "düzenleyebilme" yeteneği eşit olmalı. Bugün eşit **değil**: masaüstünde Malzeme/Araç hızlı düzenlenebiliyor.

### B3. Ekran envanteri farkları (tasarım gereği olanlar hariç)

| Yalnız web | Yalnız masaüstü |
|---|---|
| Firma Tanım, Kalıcı Silme, Kota İzleme, Canlı Sunucu, Rol/Firma Yetki Kontrol, Makine Yedekleri, Firma İş Verisini Sıfırla | **İçe/Dışa Aktarma (Excel)**, Bileşen Galerisi (geliştirici), Ekran Bilgisi |

Web-only olanlar **süper admin araçları** — bilinçli (ADR). Masaüstü-only olan **İçe/Dışa Aktarma** ise
bilinçli değil, **eksik**.

---

## C) EKSİK DÜZENLE / KAYDET / SİL İŞLEMLERİ

| Ekran | Oluştur | Düzenle | Sil/İptal | Kritik not |
|---|---|---|---|---|
| Malzemeler | ✅ | ✅ | ✅ | Tam |
| Araçlar | ✅ | ✅ | ✅ | Tam |
| Personel | ✅ | ✅ | ✅ | **Ayrı pencere yok** (satır içi form) |
| Talepler | ✅ | ✅ | ✅ | Onay/operasyon akışı ayrı |
| Günlük Faaliyet | ✅ | ❌ | ✅ | ⚠️ Silme **stok etkisini geri almıyor** (aşağıda) |
| Bakım kaydı | ✅ | ❌ | ✅ (İptal = ters kayıt) | Doğru desen |
| **Yakıt (depo girişi + dağıtım)** | ✅ | ❌ | ❌ | 🔴 **Yanlış kayıt KALICI** |
| Stok Giriş/Çıkış | ✅ | ❌ (tasarım gereği) | ✅ (ters kayıt) | Defter mantığı doğru |

### 🔴 C1. Yakıt kaydı düzeltilemiyor ve iptal edilemiyor

`FuelService` yalnız `AddDepotEntry` ve `Distribute` içeriyor. API'de yakıt için düzenleme/iptal/silme ucu
**yok**. Ekranlardaki "İptal" butonları **formu kapatma** butonu, kayıt iptali değil.

**Kullanıcıya etkisi:** Yanlış litre/fiyat/araç girilirse **düzeltilemiyor**; yakıt bakiyesi ve maliyet
raporları kalıcı olarak yanlış kalıyor. Tek çare veritabanına elle müdahale — ki bu yasak.

### ⚠️ C2. Günlük Faaliyet silme, stoğu geri almıyor

`DailyActivityService.Delete` yalnız `daily_activities.is_deleted=1` yapıyor. Koddaki kendi açıklaması:
*"Bakım tipinde bağlı bakım kaydı Bakım ekranında kalır (orada iptal edilir)."*

→ Kullanıcı faaliyeti siliyor, **stoktan düşen malzeme geri gelmiyor**; ayrıca Bakım ekranından da iptal
etmesi gerektiğini bilmesi gerekiyor. Bilinçli bir tasarım ama **kullanıcı için tuzak**.

---

## D) EKSİK DÜZENLEME KİLİTLERİ

| Servis | Kilit | Neden |
|---|---|---|
| Material · Vehicle · Personnel · MaintenanceDefinition | ✅ Var | `expectedVersion` + `EditLockGuard` |
| DailyActivity · Fuel · Maintenance kaydı | ❌ Yok | **Düzenleme özelliği olmadığı için** — A1'e bağımlı |
| Request (talep) | ⚠️ Kısmi | Durum geçişlerinde operasyon durum makinesi var; alan düzenlemede sürüm kontrolü yok |
| Branch (şube/şantiye) | ❌ Yok | `Update` var ama `expectedVersion` yok → iki admin aynı anda düzenlerse ikincisi ezer |

**Yeni tespit:** `BranchService.Update` sürüm kontrolü yapmıyor. Az kişi kullandığı için risk düşük ama
tutarsızlık gerçek.

---

## E) EKSİK / TUTARSIZ YETKİLENDİRMELER

| # | Konu | Durum |
|---|---|---|
| E1 | Şube/Şantiye tanım yetkisi | ✅ **Az önce düzeltildi** (servis kilidi) |
| E2 | Yakıt kaydı iptali | ❔ Özellik yok → yetki de tanımlanmamış. Eklenirse **yeni yetki gerekir** (`btn-reverse` benzeri) |
| E3 | Günlük Faaliyet silme | `daily_activity/Delete` yetkisi var; ama stok etkisi ayrı yetkiyle (`stock` + `btn-reverse`) geri alınıyor → **iki farklı yetki, tek işlem** |
| E4 | Makine bazlı güncelleme yetkisi | Görev panosunda bekliyor — hangi makinenin güncelleme alacağını kısıtlama |
| E5 | Talep alan düzenleme | Onay sonrası hangi alanların değişebileceği kod düzeyinde net değil |

---

## F) UI/UX STANDARDINDAN GERİDE KALAN EKRANLAR

Ortak arama/listeleme bileşeni `LookupBox` yaygınlaştı, ama **24 masaüstü ekranında hâlâ `ComboBox`** var
(bazıları meşru: tema seçimi, filtre, sabit liste). Gerçekten `LookupBox` olması gerekenler:

| Ekran | Eski kontrol | Öncelik |
|---|---|---|
| `VehicleQuickEditWindow` | 1 `AutoCompleteBox` + 6 `ComboBox` | Yüksek (senin 4. madden) |
| `MaterialQuickEditWindow` | 6 `ComboBox` | Yüksek (aynı pencere ailesi) |
| `FuelView` | 1 `AutoCompleteBox` | Orta |
| `VehicleTemplatesView` | 12 `ComboBox` | Orta |
| `UsersView` (10), `PersonnelView` (5), `MaintenanceView` (5) | `ComboBox` | Orta |

**Ayrıca:** Günlük Faaliyet, Yakıt, Bakım, Talepler ekranlarında **liste satırına çift tık** davranışı yok →
kullanıcı alışkanlığı ekranlar arasında tutarsız.

---

## G) ORTAK BİLEŞENE DÖNÜŞTÜRÜLMESİ GEREKEN TEKRARLAR

| # | Tekrar | Nerede | Öneri |
|---|---|---|---|
| G1 | Hızlı düzenleme penceresi | `MaterialQuickEditWindow`, `VehicleQuickEditWindow` — ikisi ayrı ayrı yazılmış | Ortak "kayıt düzenleme penceresi" iskeleti; yeni ekranlar buna bağlanır |
| G2 | Seçim kutusu | `LookupBox` var ama 24 ekranda eski `ComboBox` | Kademeli geçiş |
| G3 | Kolon kataloğu **iki kopya** | `Application/Ui/ListColumns.cs` **ve** `Web/Services/ListColumns.cs` | 🔴 İkisi elle senkron tutuluyor — **"Alan/Kolon Yönetimi" ekranının ön koşulu** |
| G4 | Grid filtre/sayfalama | Masaüstü `GridController`, web `DwDataGrid` | Ortak `GridCell`/`NumCell` var; kural motoru hâlâ ayrı |
| G5 | İçe aktarma doğrulama | 7 ayrı `*ImportService` benzer `Validate`/`DryRun` deseni | Ortak taban sınıf |

---

## H) GÖREV PANOSUNDAKİ BEKLEYEN İŞLER

| İş | Not |
|---|---|
| Giriş-Çıkış'ta çoklu malzeme | Tek işlemde birden çok malzeme; stok motoru zaten çok satır destekliyor (`IReadOnlyList<StockLine>`) → **çoğunlukla UI işi** |
| Makine bazlı güncelleme yetkisi | Hangi makinenin güncelleme alacağı |
| Yedek ekranları | Kısmen var (Sunucu/Makine Yedekleri web'de) |
| Giriş hız sınırı kararı | Güvenlik kararı — sende |

---

## I) KOD İNCELEMESİNDE BULDUĞUM, LİSTENDE OLMAYAN ÖNEMLİ EKSİKLER

| # | Bulgu | Neden önemli |
|---|---|---|
| **I1** | **Yakıt kaydı düzeltilemiyor/iptal edilemiyor** | Yanlış kayıt kalıcı; yakıt maliyeti ve bakiye hatalı kalır |
| **I2** | **Excel içe aktarma web'de hiç yok** | Web kullanıcısı toplu veri giremiyor; en büyük platform farkı |
| **I3** | **Günlük Faaliyet silmesi stoğu geri almıyor** | Kullanıcı "sildim" sanıyor, stok düşük kalıyor |
| **I4** | `BranchService.Update`'te sürüm kontrolü yok | İki admin aynı anda şube düzenlerse biri ezilir |
| **I5** | Kolon kataloğu iki ayrı dosyada elle senkron | Birine alan eklenip diğerine eklenmezse ekranlar sessizce ayrışır — **Alan/Kolon Yönetimi ekranı bunun üstüne kurulacak** |
| **I6** | Talepler ekranı hâlâ eski `ComboBox` deseninde | Yeni yazılmış ekran, yeni standarda göre değil |
| **I7** | `MaterialQuickEditWindow` de standart dışı | Senin 4. madden yalnız Araç diyordu; ikisi de |
| **I8** | 14 şube kolonunda FK yok | Ayrı iş olarak raporlanmıştı; hâlâ açık |
| **I9** | `material_request_items` / `maintenance_materials` tablolarında `company_id` yok (M-S1a) | İkinci firma açılmadan **önce** yapılmalı |
| **I10** | Senkron performansı (Faz S) | 22 sorgulu sürüm hesabı, her push'ta tam bakiye hesabı, yankı pull |

---

## J) BAĞIMLILIKLAR (en kritik kısım — teknik sıralama)

```
[T1] Ortak "kayıt düzenleme" altyapısı (servis + API + pencere iskeleti)
      │
      ├──> [T2] Günlük Faaliyet düzenleme ──┐
      ├──> [T3] Yakıt düzeltme/iptal ───────┼──> [T5] Düzenleme kilidi (bu üç ekran)
      └──> [T4] Bakım kaydı düzenleme ──────┘

[T6] Kolon kataloğu tekilleştirme  ──> [T7] "Alan/Kolon Yönetimi" ekranı ──> yeni rapor alanları

[T8] Excel içe aktarma API uçları ──> [T9] Web içe aktarma ekranı

[T10] LookupBox geçişi (bağımsız, ekran ekran yapılabilir)

[T11] M-S1a company_id migration ──> ikinci firma açılışı
```

**Kullanıcı için sade özet:** "Düzenleme kilidi" (senin 2. madden) tek başına yapılamaz; önce **düzenleme
özelliğinin kendisi** gerekiyor. Aynı şekilde "Alan/Kolon Yönetimi" ekranı için önce **kolon kataloğunun tek
dosyaya indirilmesi** gerekiyor.

---

## ÖNCELİKLENDİRİLMİŞ İŞ LİSTESİ

### 🔴 P0 — Veri/mali risk

| # | İş | Ekranlar | Web | Masaüstü | Migration | Bağımlılık | Risk | Kullanıcı etkisi |
|---|---|---|---|---|---|---|---|---|
| **P0-1** | **Yakıt kaydı iptali (ters kayıt)** | Yakıt Dağıtımları, Depo Girişleri | ❌ yok | ❌ yok | **Muhtemelen hayır** (mevcut ters-kayıt deseni) | — | Orta | Yanlış yakıt kaydı düzeltilebilir hale gelir |
| **P0-2** | **Günlük Faaliyet silme ↔ stok tutarlılığı** | Günlük Faaliyet | ⚠️ | ⚠️ | Hayır | — | Orta | Silince stok da geri gelir **veya** net uyarı çıkar |
| **P0-3** | **M-S1a `company_id` migration** | (arka plan) | — | — | **EVET** | İkinci firma öncesi | Orta | Firma verisi izolasyonu |

### 🟠 P1 — Günlük kullanımda önemli eksik

| # | İş | Ekranlar | Web | Masaüstü | Migration | Bağımlılık | Risk | Kullanıcı etkisi |
|---|---|---|---|---|---|---|---|---|
| **P1-1** | Ortak kayıt düzenleme altyapısı (**T1**) | — | ortak | ortak | Hayır | — | Orta | Sonraki tüm düzenleme işlerinin temeli |
| **P1-2** | Günlük Faaliyet düzenleme | Günlük Faaliyet | ❌ | ❌ | Hayır | P1-1 | Orta-yüksek | Yanlış faaliyet düzeltilebilir |
| **P1-3** | Bakım kaydı düzenleme | Bakım | ❌ | ❌ | Hayır | P1-1 | Orta-yüksek | Aynı |
| **P1-4** | **Excel içe aktarma → API + web ekranı** | İçe/Dışa Aktarma | ❌ yok | ✅ var | Hayır | — | Orta | Web'den toplu veri girişi |
| **P1-5** | Düzenleme kilidi (Günlük/Yakıt/Bakım) | 3 ekran | ❌ | ❌ | Hayır | P1-2/3, P0-1 | Düşük | İki kişi aynı kaydı ezmez |
| **P1-6** | Giriş-Çıkış'ta çoklu malzeme | Giriş-Çıkış | ❌ | ❌ | Hayır | — | Düşük | Tek işlemde çok malzeme |
| **P1-7** | `BranchService` sürüm kontrolü | Şube/Şantiye | ❌ | ❌ | Hayır | — | Düşük | İki admin çakışmaz |

### 🟡 P2 — Kullanım kolaylığı / UI

| # | İş | Ekranlar | Web | Masaüstü | Migration | Bağımlılık | Risk |
|---|---|---|---|---|---|---|---|
| **P2-1** | Çift tık → hızlı düzenleme yaygınlaştırma | Personel, Talepler, (sonra diğerleri) | farklı desen | kısmi | Hayır | P1-1 | Düşük |
| **P2-2** | Hızlı düzenleme pencerelerini LookupBox'a geçir | Malzeme + Araç pencereleri | — | ❌ | Hayır | — | Düşük |
| **P2-3** | Kalan ekranlarda LookupBox geçişi | Yakıt, Talepler, Araç Şablonları, Kullanıcılar… | — | kısmi | Hayır | — | Düşük |
| **P2-4** | Kolon kataloğu tekilleştirme (**T6**) | ortak | ✅+❌ | ✅ | Hayır | — | Orta |
| **P2-5** | "Alan/Kolon Yönetimi" ekranı | yeni | — | — | **Muhtemelen evet** | P2-4 | Orta |

### 🟢 P3 — Sonra

| # | İş | Not |
|---|---|---|
| P3-1 | Faz S — senkron performansı | 22 sorgu → 1, hedefli bakiye hesabı, yankı pull |
| P3-2 | FK ekleme (14 kolon) | SQLite kısıtı nedeniyle ertelendi |
| P3-3 | Şube/şantiye benzersizlik kuralı | İş kuralı kararı |
| P3-4 | Makine bazlı güncelleme yetkisi | Görev panosu |
| P3-5 | Giriş hız sınırı | Güvenlik kararı |
| P3-6 | İçe aktarma servisleri ortak tabana | Teknik borç |

---

## MIGRATION GEREKTİREN İŞLER (canlı veri!)

| İş | Migration | Not |
|---|---|---|
| **P0-3 / M-S1a `company_id`** | ✅ **EVET** | Additive + ebeveynden geri doldurma; planı hazır |
| P2-5 Alan/Kolon Yönetimi | ⚠️ Muhtemelen | Kullanıcı bazlı kolon tercihi tablosu gerekebilir |
| P3-2 FK ekleme | ✅ EVET | SQLite kısıtı → ertelendi |
| **Diğer tüm P0/P1/P2 işleri** | ❌ **HAYIR** | Mevcut tablolarla yapılabilir |

---

## ÖNERİLEN GELİŞTİRME SIRASI

| Sıra | İş | Neden bu sırada |
|---|---|---|
| **1** | **P0-1 Yakıt iptali** | Tek başına yapılabilir, bağımlılığı yok, en somut veri riski |
| **2** | **P0-2 Günlük Faaliyet silme ↔ stok** | Küçük kapsam, aynı aile, kullanıcı tuzağını kapatır |
| **3** | **P0-3 M-S1a migration** | Tek migration; ikinci firma açılmadan bitmeli |
| **4** | **P1-1 Ortak düzenleme altyapısı** | 5, 6 ve P2-1'in ön koşulu |
| **5** | P1-2 + P1-3 Günlük Faaliyet / Bakım düzenleme | Altyapı hazırken art arda |
| **6** | P1-5 Düzenleme kilidi (3 ekran) | Düzenleme var olunca anlam kazanır |
| **7** | **P1-4 Excel içe aktarma → web** | Bağımsız, en büyük platform farkı |
| **8** | P1-6 Çoklu malzeme · P1-7 Şube sürüm kontrolü | Küçük, bağımsız |
| **9** | P2-2 + P2-3 LookupBox geçişi | Ekran ekran, risksiz |
| **10** | P2-4 → P2-5 Kolon kataloğu → Alan/Kolon Yönetimi | Raporların genişleyebilmesi için |
| **11** | P3 kuyruğu | Faz S, FK, benzersizlik… |

---

## SENİN ONAYLAMAN GEREKEN KRİTİK KARARLAR

| # | Karar | Seçenekler | Önerim |
|---|---|---|---|
| **K1** | **Yakıt kaydı yanlış girildiğinde ne olsun?** | (a) **İptal = ters kayıt** (defter bozulmaz, iz kalır) · (b) Düzenleme (kaydın üstüne yaz) · (c) Silme | **(a)** — stok/bakım ile aynı mantık, geçmiş korunur |
| **K2** | **Günlük Faaliyet silinince stok ne olsun?** | (a) Bağlı bakım/stok da **otomatik iptal** edilsin · (b) Bugünkü gibi ayrı kalsın ama **net uyarı** çıksın · (c) Stok hareketi varsa **silmeye izin verme**, önce iptal ettir | **(a)** en kullanışlısı; ama (c) en güvenlisi — **senin tercihin** |
| **K3** | **"Düzenleme" gerçekten düzenleme mi olsun?** | (a) **İptal + yeniden giriş** (defter mantığı, güvenli) · (b) Gerçek güncelleme (eski değer audit'te kalır) | **(a)** stoğu etkileyen kayıtlar için, **(b)** etkilemeyen alanlar (açıklama, not) için — karma |
| **K4** | **Excel içe aktarma web'e taşınsın mı?** | (a) Evet (API + web ekranı) · (b) Masaüstünde kalsın | **(a)** — ama büyük iş; sıralamada 7. sırada |
| **K5** | **M-S1a migration ne zaman?** | (a) Şimdi (3. sırada) · (b) İkinci firma açılmadan hemen önce | **(a)** — canlıda 0 yetim kayıt varken en güvenli an |
| **K6** | Çift tık yaygınlaştırma hangi ekranlarla başlasın? | (a) Personel + Talepler · (b) Günlük Faaliyet + Yakıt · (c) Sonra karar | **(a)** — düzenleme altyapısı gerektirmeyen ekranlar |
| **K7** | Öncelik sırasını onaylıyor musun? | Yukarıdaki 11 adımlık sıra | Onayına sunuldu |

---

## ÇALIŞMA BİÇİMİ (senin belirlediğin)

Her iş ayrı ele alınacak: **ANALİZ → onayın → GELİŞTİRME → TEST → WEB + MASAÜSTÜ doğrulama → DEPLOY → RAPOR.**
Bir işin içinde başka bir işin kapsamına giren sorun çıkarsa **sessizce büyütmeyeceğim**; önce bildireceğim.

Her geliştirmede **web ve masaüstü birlikte** değerlendirilecek; tek platformda bırakılan iş "tamamlandı"
sayılmayacak. Yeni eklenen her alan, ileride "Alan/Kolon Yönetimi" ekranına bağlanabilmesi için
kolon kataloğu + filtre + API + rapor zincirine birlikte eklenecek.

---

## BU AŞAMADA YAPILMAYANLAR

Kod yazılmadı · migration oluşturulmadı/çalıştırılmadı · deploy yapılmadı · canlı veriye dokunulmadı ·
hiçbir iş başlatılmadı.
