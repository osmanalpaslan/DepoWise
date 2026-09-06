# ADR-223 — FAZ 3b: alan bazlı yetki — envanter, tehdit modeli ve tasarım

> **Durum:** 📋 TASARIM — uygulama ONAYI BEKLİYOR · 2026-09-05
> **Bu turda kod yazılmadı, migration oluşturulmadı, commit/push yapılmadı, üretime dokunulmadı.**
> Önkoşul: [ADR-221](ADR-221-YETKI-VE-MENU-MIMARISI.md) · [ADR-222](ADR-222-FAZ3-ROL-VE-ALAN-YETKISI-TASARIM.md) (Faz 3a uygulandı)

---

## 0. Neden bu turda kod yazılmadı

Talimatın §55'i "HEMEN KOD YAZMA — önce envanter, tehdit modeli, ADR" diyor. Ayrıca §52 iki
**durma kuralı** tanımlıyor ve ikisi de bu fazda tetiklendi:

- *"sync mimarisi değişmek zorunda"*
- *"local DB güvenlik modeli değişmek zorunda"*

§12 ayrıca G.1 kararını **körü körüne kabul etmememi** istedi. Yeniden inceledim ve
**önceki gerekçenin bir kısmı yanlıştı** (bkz. §3.2) — ama sonuç değişmedi. Gerekçeyi düzeltmek
zorundayım, çünkü yanlış gerekçeyle doğru sonuca varmak sonraki kararları bozar.

---

## 1. ALAN ENVANTERİ — ölçüldü, tahmin edilmedi

### 1.1 Hassas alan sınıfları (şemadan tarandı)

| Sınıf | Kolon(lar) | Bulunduğu tablo sayısı |
|---|---|---|
| **Birim fiyat / maliyet** | `unit_price` | **8 tablo** (malzeme, bakım, yakıt, şablon, fatura, satın alma, ekipman bakımı…) |
| Kur | `fx_rate`, `rate_to_base` | 4 |
| Stopaj | `withholding_amount` | 1 |
| Maliyet merkezi | `cost_center_id` | 3 |
| **Finansal tablolar (bütünüyle hassas)** | `parties` · `invoices` · `invoice_lines` · `party_ledger` · `finance_accounts` · `finance_transactions` | 6 tablo |

### 1.2 🔴 TEK BİR ALANIN yayılımı — `unit_price`

| Kanal | Dokunma noktası |
|---|---|
| Şema | **8 tablo** |
| API yanıtı (`unitPrice`) | **7 yer** |
| Servis katmanı (`UnitPrice`) | **45 kullanım / 12 dosya** |
| Masaüstü (kod + XAML) | **13 dosya** |
| Web bileşenleri | **8 dosya** |
| Raporlar | **17 referans** |
| Senkron | `materials` tablosu içinde (tam kolon) |

**≈ 90 dokunma noktası — TEK alan için.** Ekrandan ekrana `if (CanField(...))` eklemek
matematiksel olarak sürdürülemez; talimatın §6'daki uyarısı yerindedir.

### 1.3 Mevcut alan altyapısı

| Yapı | İçerik | Kapsam |
|---|---|---|
| `FieldCatalog` | `(ScreenKey, ScreenLabel, FieldKey, Label, SystemRequired)` | **25 alan / 3 ekran** (Araçlar · Malzemeler · Yakıt) |
| `field_requirements` (M087) | `(company_id, screen_key, field_key, required)` | Firma bazlı **zorunluluk** — yetki DEĞİL |

**Katalog 70 ekranın 3'ünü kapsıyor.** Alan yetkisi ancak katalogdaki alanlar için tanımlanabilir;
kapsam kademeli büyütülmelidir (kapsanmayan ekran = bugünkü davranış).

---

## 2. VERİ AKIŞI — dört kanal (Faz 3 tasarımından, doğrulandı)

```
                       ┌─ WEB      : API → 256 satır içi anonim yanıt
SERVİS KATMANI ────────┼─ MASAÜSTÜ : servisi DOĞRUDAN çağırır (API yok), domain kaydı alır
(329 yetki çağrısı)    ├─ EXPORT/RAPOR : TableModel → ExcelExportService.Export  ← TEK KAPI
                       └─ SENKRON  : BuildSnapshot → ham satır (tüm kolonlar)
```

Ortak nokta **servis katmanıdır** — DTO değil. Alan süzme oraya konursa üç kanal birden korunur.

---

## 3. TEHDİT MODELİ — G.1 yeniden değerlendirildi (§12)

### 3.1 Senkron KİMİN adına üretiliyor?

```csharp
app.MapGet("/api/sync/business-pull", (HttpContext c, long? since) => {
    var s = S(c); if (s is null) return Results.Unauthorized();
    var snapshot = svc.BusinessSync.BuildSnapshot(s.CompanyId, "server", since ?? 0, s);
```

**Uç kimlik doğrulamalı ve KULLANICI OTURUMUNU geçiriyor.** GAP-6 ile ön muhasebe tabloları
zaten kullanıcının izinli şubeleriyle süzülüyor.

### 3.2 🔴 ÖNCEKİ GEREKÇEM YANLIŞTI — düzeltme

ADR-222 G.1'de "senkron cihaz bazlıdır, kullanıcı kimliği yoktur" ima etmiştim. **Yanlış.**
Kimlik var ve şube süzgeci zaten uygulanıyor. Yani **teknik olarak alan süzme mümkündür.**

Engel başka yerde ve daha sinsi:

### 3.3 🔴 GERÇEK ENGEL — paylaşılan yerel veritabanı + paylaşılan imleç

| Bulgu | Kanıt |
|---|---|
| Yerel DB **Alpnex kullanıcısına göre değil** | `AppPaths.DatabasePath` → `%LOCALAPPDATA%\Alpnex\Data\<ortam>\alpnex.db` — yalnız Windows kullanıcısı + ortam |
| Senkron imleci de **kullanıcı bazlı değil** | `app_settings` anahtarı `(company_id, setting_key)`; `Set(..., userId)` parametresi **yalnız audit içindir** |
| Çıkışta yerel veri **silinmiyor** | `App.Logout()` → yalnız `RememberMe` temizler, `Session = null`, giriş ekranı |
| Yerel SQLite **şifresiz** | `SqliteConnectionStringBuilder` — parola/SQLCipher yok |

**Sonuç — somut hata senaryosu (varsayım değil, mekanizmadan çıkıyor):**

1. Maliyeti göremeyen **A** senkronlar → `unit_price` kolonu inmez, imleç **T** anına ilerler.
2. Aynı makinede maliyeti görebilen **B** giriş yapar → imleç ortak olduğu için **T'den sonrasını** çeker.
3. T'den önceki satırların `unit_price` değeri **kalıcı olarak boş kalır**.

Bu, güvenlik kazancı değil **veri kaybıdır** ve çevrimdışı çalışmayı bozar.

### 3.4 Tehdit modeli — kapsam içi / dışı

| Tehdit | Kapsam | Gerekçe |
|---|---|---|
| Kullanıcı uygulamada yetkisiz alanı **görmeye** çalışır | ✅ **İÇİNDE** | Servis katmanında süzülür |
| Yetkisiz alanı **API'den** almaya çalışır | ✅ İÇİNDE | Aynı servis kapısı |
| **Export/rapor** üzerinden almaya çalışır | ✅ İÇİNDE | `TableModel` tek kapı |
| **Masaüstü servisinden** almaya çalışır | ✅ İÇİNDE | Aynı servis kapısı |
| Aynı makinedeki **başka Alpnex kullanıcısı** uygulama üzerinden görmeye çalışır | ✅ İÇİNDE | Okuma servisten geçer; yeni oturumun izni uygulanır |
| Kullanıcı `.db` dosyasını **harici bir araçla** açar | ❌ **DIŞINDA** | Şifresiz SQLite; çözümü şifreleme ya da kullanıcı bazlı DB — ayrı ve çok daha büyük iş |
| Cihazı çalan/ele geçiren saldırgan | ❌ DIŞINDA | Aynı gerekçe |

> **Yazılı sınır:** *Alan yetkisi, uygulama üzerinden yetkisiz erişimi engeller. Cihazın yerel
> veritabanına dosya düzeyinde erişebilen bir kişinin veriyi doğrudan okumasını ENGELLEMEZ.
> Senkron tüm kolonları taşıdığı için, alan bir kez cihaza indikten sonra cihazda fiziksel olarak
> bulunur ve çıkış yapıldığında da silinmez.*

### 3.5 Senkron için üç seçenek — ölçülmüş bedelleriyle

| # | Seçenek | Bedel | Değerlendirme |
|---|---|---|---|
| **S1** | Senkronu **süzme** (bugünkü) | Alan cihazda fiziksel olarak bulunur | 🟢 Veri kaybı yok · uygulama içi güvenlik tam |
| **S2** | Senkronu süz | §3.3'teki **kalıcı veri kaybı** | 🔴 **Kabul edilemez** — ortak imleç bunu kaçınılmaz kılıyor |
| **S3** | Yerel DB + imleci **Alpnex kullanıcısı bazlı** yap | Her kullanıcı için ayrı tam senkron (disk + ağ + ilk açılış süresi); `AppPaths`, ayarlar, imleç, yedekleme, güncelleme akışları etkilenir | 🔴 Faz 3b'nin **10 katı** iş; ayrı faz konusu |

**Önerim: S1** — ve sınırı yukarıdaki gibi **yazılı** tutmak. S3 gerçekten isteniyorsa ayrı bir
faz olarak planlanmalı; bu fazın içine sıkıştırmak çalışan çevrimdışı yapıyı riske atar.

---

## 4. ALAN YETKİSİ MODELİ

### 4.1 Anahtar biçimi — ekran bazlı (§3'ün şartı)

```
fld_<ekranAnahtarı>_<alanAnahtarı>        örn. fld_materials_unit_price
```

Ekran anahtarı **zorunlu**: aynı mantıksal alan farklı ekranlarda farklı yetki gerektirebilir
(talimattaki Material Detail / Edit / List örneği). `FieldCatalog` zaten `(ScreenKey, FieldKey)`
ikilisiyle çalışıyor → aynı sözlük.

**Çakışma kontrolü:** mevcut önekler `rpt_` ve `datype_`; `fld_` ikisiyle de çakışmıyor ve
`user_permissions.module_key` serbest metin (tip TEXT, benzersizlik `(user_id, module_key)`).

### 4.2 İki seviye — VIEW ve EDIT

| `can_view` | `can_edit` | Anlam |
|---|---|---|
| 0 | – | **Hidden** — yanıttan çıkarılır, export/rapora girmez |
| 1 | 0 | **Read-only** — döner ama yazma REDDEDİLİR/yok sayılır |
| 1 | 1 | **Editable** |

`can_create`/`can_delete` alan seviyesinde **kullanılmaz**. Gerekçe: domain'de karşılığı yok
(bir alan "silinmez"), gereksiz karmaşıklık talimatta da yasaklanmış.

**"EDIT, VIEW'ı ima eder mi?" (§29):** **EVET, ima eder.** Göremediği bir alanı düzenleyebilen
kullanıcı, değeri okumadan yazar — ne arayüzde anlamlı ne de güvenli. Kural:
`EDIT ⇒ VIEW`; `can_edit=1, can_view=0` kombinasyonu **geçersizdir** ve yazma anında reddedilir.

### 4.3 🔴 "Korumalı alan" — deny icat etmeden gizleme

**Sorun:** gizleme özünde DENY'dir; K1 yalnız ALLOW diyor ve geriye dönük uyumluluk varsayılanı
**görünür** yapıyor. Yalnız-ALLOW ile görünür varsayılan bir alan gizlenemez.

**Neden "kullanıcının hiç `fld_` anahtarı yoksa hepsini görür" kuralı (datype_ deseni) YETMEZ:**
bir alanı gizlemek için o ekrandaki **diğer tüm alanları tek tek vermek** gerekir (11 alanlı
ekranda 1 alanı gizlemek için 10 atama). Daha kötüsü: ileride eklenen **yeni bir alan**, açık
anahtarı olan kullanıcılardan **sessizce gizlenir** — yükseltmede gerçek bir regresyon.

**Çözüm — firma seviyesinde opt-in:**

```
1) FİRMA: "korumalı alanlar" listesi   → boşken HİÇBİR ŞEY değişmez (bugünkü davranış)
2) KULLANICI/ROL: korumalı alanlar için fld_ ALLOW verilir (deny-by-default yalnız o alanlarda)
```

Kısıtlama kararı **yetki katmanında değil firma yapılandırmasında** durur → K1 ve K5 korunur.

**Migration gerekli mi? (§4'ün kanıt şartı):**

| Seçenek | Migration | Değerlendirme |
|---|---|---|
| `field_requirements`'a kolon ekle | ✔ mevcut tabloyu değiştirir | ❌ Çalışan tabloya dokunmak; semantik de farklı (zorunluluk ≠ koruma) |
| `app_settings`'e JSON liste | ✖ | ❌ `app_settings` bugün yalnız `developer_mode` taşıyor; yapısal liste için uygun değil, sorgulanamaz |
| **Yeni `field_protections` tablosu** | ✔ yalnız CREATE | ✅ M087 ile **birebir aynı şekil ve felsefe**; boş doğar → davranış değişmez |

**Karar: yeni tablo.** Kanıt yükümlülüğü uygulamada şu testle karşılanacak:
*"`field_protections` boşken tüm alanlar bugünkü gibi görünür/düzenlenebilir"* — Faz 3a'daki
RL1'in alan karşılığı.

---

## 5. MERKEZİ ÇÖZÜMLEME — ikinci motor YOK

```
PermissionSnapshot (mevcut)
        │  fld_ anahtarları zaten burada — EK ALTYAPI YOK
        ▼
AlanErisimi (yeni, saf fonksiyon)   ← AccessControl.Can üzerine kurulur
        │   Gorunur(session, screen, field) / Duzenlenebilir(...)
        ▼
┌───────┴───────────────┬──────────────────┬─────────────────┐
Servis okuma            TableModel süzgeci   Servis yazma
(web + masaüstü + API)  (export + rapor)     (EDIT kapısı)
```

- **Yeni tablo/önbellek YOK:** `fld_` anahtarları `user_permissions`/`role_permissions` içinde,
  `PermissionSnapshot`'a zaten geliyor, `InvalidateUser`/`InvalidateAll` zaten çalışıyor.
- **Rol birleşimi bedava:** Faz 3a'nın union'ı `module_key`'e bakmadığı için `fld_` anahtarları
  rol seviyesinde **kendiliğinden** çalışır (RL11 aynı şeyi `rpt_` için kanıtladı).
- **Performans:** karar `PermissionSet` sözlük araması → **O(1), sorgusuz**. Talimatın §37'deki
  "her satır için CanField → DB" yasağı yapısal olarak imkânsız hâle gelir.
- **Satır başına değil, SORGU başına:** izinli alan kümesi liste sorgusundan ÖNCE bir kez
  hesaplanır; 10.000 satır için 10.000 kez değil **1 kez**.

### 5.1 "null ile saklama" yasağı (§7) — nasıl karşılanacak

| Kanal | Yöntem |
|---|---|
| API (anonim nesne) | Gizli alan nesneye **hiç konmaz** (koşullu şekillendirme). Global `WhenWritingNull` **KULLANILMAZ** — mevcut istemcileri kırar |
| Export/rapor | `TableModel` kolonu **düşürülür** (başlık + hücre birlikte) |
| Masaüstü (domain kaydı, C# tipi) | Alan fiziksel olarak çıkarılamaz → **arayüzde kolon/alan hiç oluşturulmaz** + değer okunmaz. Bu, tipli dilin sınırı olarak **yazıya geçirilir** |

---

## 6. RİSK MATRİSİ

| # | Risk | Seviye | Azaltma |
|---|---|---|---|
| R1 | Tek alan **90 dokunma noktasında** | 🔴 HIGH | Kaynakta (servis) süz; uçlara tek tek dokunma |
| R2 | Senkron alanı cihaza indiriyor | 🔴 HIGH | S1 + **yazılı sınır** — onay gerekli |
| R3 | Ortak imleç → süzülmüş senkron **veri kaybı** yapar | 🔴 HIGH | S2 **reddedildi**; gerekçe belgelendi |
| R4 | Alan kataloğu 3/70 ekran | 🟡 MEDIUM | Kademeli kapsam; kapsanmayan = bugünkü davranış |
| R5 | `TableModel` alan kimliği taşımıyor | 🟡 MEDIUM | Başlık → alan anahtarı eşlemesi + test |
| R6 | Masaüstünde alan fiziksel olarak çıkarılamaz | 🟡 MEDIUM | Arayüzde hiç oluşturma + sınırı yaz |
| R7 | Yetki ekranı satır sayısı (~400–800) | 🟡 MEDIUM | Ölç, gerekirse sanallaştır |
| R8 | Yeni alan eklendiğinde sessiz gizlenme | 🟢 LOW | Firma opt-in modeli bunu **yapısal olarak** önler |

---

## 7. ONAY BEKLEYEN KARARLAR

| # | Karar | Önerim |
|---|---|---|
| **D1** | **Senkron sınırı**: S1 (süzme) + yazılı tehdit modeli sınırı | ✅ **S1** — S2 veri kaybı yapar, S3 ayrı faz |
| **D2** | `field_protections` yeni tablosu (yalnız CREATE, boş doğar) | ✅ Evet — diğer iki seçenek elendi |
| **D3** | `EDIT ⇒ VIEW` kuralı | ✅ Evet — aksi hâlde okumadan yazma |
| **D4** | Alan kataloğu kapsamı: önce hangi ekranlar? | **Malzemeler + Ön Muhasebe** (en hassas: `unit_price`, finansal tablolar) |
| **D5** | Masaüstünde alanın fiziksel olarak çıkarılamaması | Sınır olarak kabul + yazıya geçir |

---

## SONUÇ

> ## 🟡 FAZ 3b TASARIMINDA ONAY BEKLEYEN NOKTALAR VAR

Envanter, veri akışı ve tehdit modeli **ölçülerek** çıkarıldı. Tasarım hazır; ancak §52'nin iki
durma kuralı tetiklendiği için (senkron mimarisi + yerel DB güvenlik modeli) uygulamaya
geçmeden **D1–D5 onayı** gerekiyor.

**Önceki G.1 gerekçesindeki hata düzeltildi:** senkron cihaz bazlı değil, kullanıcı bazlıdır ve
alan süzme teknik olarak mümkündür. Sonucu değiştiren şey **paylaşılan yerel veritabanı ve
paylaşılan senkron imlecidir** — süzme uygulanırsa güvenlik değil **veri kaybı** üretir.

---
---

# ⭐ UYGULAMA KAYDI — FAZ 3b-3 + 3b-4 (2026-09-05, kullanıcı onayı D1–D5)

> Bu bölüm tasarımın değil, **yapılan işin** kaydıdır. Kapsam kullanıcının verdiği onayla sınırlıdır:
> **3b-3 (merkezi alan yetkisi modeli) + 3b-4 (yalnız Malzemeler ve Ön Muhasebe servis entegrasyonu)**.
> 3b-5 ve sonrası UYGULANMADI. Commit/push yapılmadı, üretime dokunulmadı.

## 1. D1 — SENKRON SINIRI (kullanıcının dayattığı metin, aynen)

> **Alan yetkisi, Alpnex uygulamasının yetkilendirilmiş kullanım yolunu kontrol eder. Kullanıcının
> fiziksel olarak erişebildiği ve SQLite'ın bulunduğu cihazdaki ham yerel veriyi korumak bu fazın
> güvenlik garantisi değildir.**

Uygulamada bunun anlamı: senkron **süzülmedi** (S1). Masaüstü yerel veritabanı firmanın tüm
verisini indirmeye devam eder; alan yetkisi **uygulama içindeki** okuma/yazma yollarında uygulanır.
Gerekçe §3.3'te ölçüldü: paylaşılan yerel veritabanı + paylaşılan senkron imleci nedeniyle süzme
güvenlik değil **veri kaybı** üretirdi.

## 2. D3 — KANONİK YAZMA KURALI (tek davranış, üç durum)

Kullanıcı "dört kombinasyonu test et ve **tek bir kanonik davranış** ADR'ye yazılsın" dedi.
Kanonik davranış `FieldAccess.YazmaDegeri` içinde tek yerde tanımlıdır:

| Durum | Okuma | Yazma | Neden |
|---|---|---|---|
| Alan **korumasız** | Görünür | Gönderilen değer yazılır | **Bugünkü davranış** — varsayılan budur |
| Korumalı, **görünmüyor** | Gizli (0) | Gönderilen değer **yok sayılır**, kayıttaki değer **korunur** | Kullanıcı değeri hiç görmedi; 0 yazmak **sessiz veri kaybı** olurdu |
| Korumalı, görünür, **düzenlenemez** | Görünür | Değer **değiştiyse 403**, aynıysa geçer | Gördüğü değeri değiştirmeye çalışmıştır; ama her kaydetmeyi 403 yapmak ekranı kullanılamaz kılardı |
| Korumalı, görünür, düzenlenebilir | Görünür | Gönderilen değer yazılır | Tam yetki |

**`EDIT ⇒ VIEW`**: dördüncü kombinasyon (`view=0, edit=1`) **oluşamaz** —
`FieldAccess.Duzenlenebilir` görünürlüğü şart koşar, `FieldAccess.GecerliMi` de yazma anında eler.
Testler: `AL4` (kural), `AL5` (veri kaybı yok), `AL6`/`AL7` (403 ve geçiş).

## 3. NE YAPILDI

### 3.1 Yeni dosyalar
| Dosya | İş |
|---|---|
| `Migration093_FieldProtections.cs` | `field_protections` — yalnız CREATE, **boş doğar**, iki lehçede aynı SQL |
| `Application/Security/FieldAccess.cs` | **Tek karar noktası.** `Gorunur` / `Duzenlenebilir` / `YazmaDegeri` / `IzinliAlanlar` |
| `Application/Security/FieldProtectionCatalog.cs` | Korunabilir alanların **tek doğru kaynağı** |
| `Infrastructure/Organization/FieldProtectionService.cs` | Firma seçimini okur/yazar; yetkili, denetim kayıtlı, önbellek düşürür |
| `Infrastructure/Accounting/AccountingFieldGate.cs` | Ön muhasebenin dört servisi için ortak anahtar geçişi |
| `tests/AlanYetkisiTests.cs` | 22 test (AL1–AL22) |

### 3.2 İki kademeli model — deny icat etmeden gizleme (K1 korundu)
1. **FİRMA**: alan `field_protections` içinde mi? Değilse **hiçbir yetki sorusu sorulmaz**.
2. **KULLANICI/ROL**: korumalı alanda deny-by-default; yalnız açık `fld_<ekran>_<alan>` izni açar.

Böylece rol izinleri **yalnız ALLOW** üretmeye devam eder ve Faz 1 precedence sırası hiç değişmez.
`fld_` anahtarları serbest metin `module_key` sayesinde mevcut `user_permissions` /
`role_permissions` satırlarında durur → **izinler için migration gerekmedi**; Faz 3a birleşimi
`module_key`'e bakmadığı için rol seviyesinde **kendiliğinden** çalışır (test `AL14`).

### 3.3 Kapsanan alanlar (katalog = gerçekten uygulanan)
| Ekran | Alan | Nerede uygulanır |
|---|---|---|
| Malzemeler | `unit_price` | Kart · liste · grid · özet · filtre · sıralama · dışa aktarım · özel rapor · API · yazma |
| Cari Kartlar | `balance` | Liste borç/alacak/bakiye · bakiye kartı · ekstre (yürüyen bakiye dahil) · API |
| Faturalar | `grand_total` | Liste tutarı · API; **detay ve Tahsilat/Ödeme fail-closed** |
| Kasa / Banka | `amount` | Hareket listesi · ekstre · yürüyen bakiye · API |
| Kasa / Banka | `balance` | Hesap listesi · hesap kartı · tek hesap bakiyesi · API |

**Katalogda yalnız gerçekten süzülen alan vardır.** Serviste uygulanmayan bir alanı katalogda
göstermek, yöneticiye "korudum" dedirtip aslında hiçbir şey yapmamak olurdu.

### 3.4 🔴 ÇIKARIM KANALLARI DA KAPATILDI
Değeri gizleyip yan kanalı açık bırakmak gizlemek değildir. Kapatılanlar ve testleri:

| Kanal | Davranış | Test |
|---|---|---|
| Gizli alana **filtre** | Filtre yok sayılır (sonuç kümesi daralmaz) | `AL9` |
| Gizli alana göre **sıralama** | İstek düşer, varsayılan sıralamaya dönülür | `AL10` |
| Fiyattan **türeyen toplam** (stok değeri) | Hesaplanmaz; kutu hiç oluşturulmaz | `AL11` |
| Cari/kasa **yürüyen bakiye** | Gizlenir (ardışık iki satırın farkı tutarı verirdi) | `AL17`, `AL19` |
| Fatura **detayı** ve tahsilat listesi | Fail-closed 403 (sıfırlanmış tutar yanlış bilgi olurdu) | `AL18` |
| **Ön muhasebe raporları** (tutarları SQL'den okur, servisi atlar) | Altı rapor fail-closed 403 | `AL23` |

### 3.5 "null ile saklama" yasağı (§7/K2)
| Kanal | Yöntem |
|---|---|
| API | Gizli alan anonim nesneye **hiç konmaz**. Görünür durumda yanıt sözleşmesi **harfiyen eskisi gibidir** (mevcut istemciler etkilenmez) |
| Dışa aktarım / rapor | Kolon **başlığıyla birlikte** düşer (`AL12`) |
| Masaüstü (C# kaydı) | Alan fiziksel olarak çıkarılamaz (**D5 sınırı**) → değer 0'lanır, **arayüz kolonu/alanı hiç oluşturmaz** |

### 3.6 Arayüz (masaüstü + web)
- **Masaüstü** `MaterialsViewModel`: gizli alan görünür kolon listesinden **ve kolon seçiciden** düşer;
  kart alanı `IsVisible` ile hiç oluşturulmaz; Excel kolonu düşer. Kullanıcının kayıtlı kolon tercihi
  **silinmez** — yetki geri verilirse kolon kendiliğinden geri gelir.
- **Web** `Materials.razor`: karar **sunucudan okunur** — satırda alan var mı diye bakılır. Böylece
  ekran sunucudan asla ayrışmaz ve **ikinci bir yetki kararı üretilmez**. Özet kutusu `stockValue`
  gelmiyorsa çizilmez (`GridSummary.StockValue` artık `decimal?`).

## 4. AÇIKÇA YAPILMAYANLAR (kapsam dışı — 3b-5 ve sonrası)

1. **Ön muhasebe ARAYÜZLERİ** (cari/fatura/kasa ekranları, web ve masaüstü) henüz kolon gizlemiyor.
   Veri katmanı korunuyor (tutarlar 0 gelir, API'da alan yok) ama ekranlar "0,00" gösterir.
   → **Bir firmada ön muhasebe alan koruması, 3b-5 tamamlanmadan AÇILMAMALIDIR.**
2. **Alan yetkisi yönetim EKRANI** yok. Koruma bugün yalnız `FieldProtectionService` üzerinden
   (kod/test) açılabilir; yetki ağacına `fld_` satırları eklenmedi.
3. **Senkron süzme** yok (D1 kararı, yukarıda).
4. **DENY / geçersiz kılma** yok (K1 gereği); model ileride eklenebilecek şekilde bırakıldı.
5. Malzeme dışındaki `unit_price` kolonları (**yakıt**, **hareket**, **fatura satırı**) bu korumanın
   kapsamında **değildir** — bunlar farklı tablolarda, farklı anlamda alanlardır.

## 5. GERİ DÖNÜŞ

Koruma satırları silinince (ya da tablo boş bırakılınca) sistem **bugünkü davranışına birebir döner**;
`AL1` bunu kilitler. Migration yalnız CREATE olduğu için tabloyu bırakmak da yeterlidir.

---
---

# ⭐ UYGULAMA KAYDI — FAZ 3b-5 (2026-09-05) — YÖNETİM KATMANI VE GERÇEK GUI

> 3b-3/3b-4 **kararı** kurmuştu; bu faz onu **yönetilebilir** ve **görünür** yaptı.
> Commit/push yok, üretime dokunulmadı, üretimde migration çalıştırılmadı.

## 1. MİMARİ CEVAP (kullanıcı §1'in sorusu)

> *"Bir firma yöneticisi bir alanı korumalı yaptığında, bu korumanın hangi ekranda tanımlanacağı ve
> daha sonra hangi kullanıcı/rolün bu alanı görebileceği/düzenleyebileceği mevcut mimariye **en az
> değişiklikle** nasıl yönetilecek?"*

**Cevap: yeni bir yetki ekranı AÇILMADI. Mevcut yetki ağacı GENİŞLETİLDİ.**

Anahtar gözlem: `fld_<ekran>_<alan>` zaten bir **modül anahtarıdır**. Yetki ağacı
`AppModules.Grouped()` üzerinden kurulur ve bu ağaca rapor (`rpt_`) ile kayıt tipi (`datype_`)
kalemleri **zaten** aynı yolla enjekte ediliyordu. Alan kalemleri de üçüncü örnek oldu:

```
field_protections (FİRMA: bu alan hassas mı?)
        │
        ▼
AppModules.Grouped(korumalıAlanlar)        ← TEK kaynak, iki platform da bunu okur
        │  ekranın HEMEN ARDINA "Alan › Ekran › Alan Adı" satırı
        ├──────────────► /api/modules → PermMatrix (WEB)
        └──────────────► PermissionsViewModel → PermissionsView (MASAÜSTÜ)
                                │
                         SaveForUser / SaveForRole   ← mevcut yol, değişmedi
                                │
                         user_permissions ∪ role_permissions   ← K1 korundu
                                │
                         PermissionSnapshot → FieldAccess → servis/API
```

**Sonuç:** yeni tablo yok (koruma tablosu 3b-3'te açılmıştı), yeni yetki motoru yok, yeni kaydetme
yolu yok, `module_key` sözlüğü tek. Eklenen tek şey **görünürlük ve etiketleme**.

## 2. KOŞULLU LİSTELEME — neden yalnız korumalı alanlar görünüyor

Ağaçta **yalnız korumalı alanlar** satır olarak belirir. Gerekçe dürüstlüktür: korumasız alanı zaten
herkes görür; ağaçta boş bir "Birim Fiyat ☐" kutusu göstermek yöneticiye **kapalı olduğu izlenimi**
verirdi. Koruma yoksa satır da yoktur → yayın günü yetki ağacı **birebir bugünküdür** (test `YK1`).

`AppModules.Grouped()` parametresizken (mevcut tüm çağrılar ve testler) alan satırı ÜRETMEZ.

## 3. ALAN SATIRININ ŞEKLİ

| Kolon | Alan kalemi | Ekran modülü |
|---|---|---|
| Oku | ✅ kutu | ✅ kutu |
| Yaz | **—** (çizilmez) | ✅ kutu |
| Düzelt | ✅ kutu | ✅ kutu |
| Sil | **—** (çizilmez) | ✅ kutu |

Bir ALANDA "yaz"/"sil" anlamsızdır. Anlamsız kutuyu gri göstermek yerine **hiç çizmemek** seçildi:
var olmayan seçenek yanlış soru sordurmaz.

**EDIT ⇒ VIEW üç kemerle uygulanır:**
1. **Arayüz** (web `PermMatrix.AlanSet`, masaüstü `ModulePermNode` kısmi metotları): "Düzelt"
   işaretlenince "Oku" da işaretlenir; "Oku" kaldırılınca "Düzelt" kalkar → yönetici geçersiz
   kombinasyonu **oluşturamaz**.
2. **Sunucu** (`PermissionService.AlanKalemleriniDogrula`): doğrudan HTTP/servis çağrısında
   `view=0, edit=1` **reddedilir** (sessizce düzeltilmez — niyet belirsizdir).
3. **Çalışma anı** (`FieldAccess.Duzenlenebilir`): doğrudan veritabanına ekilmiş bozuk satır bile
   yazma yetkisi ÜRETMEZ (test `AL4` bunu artık DB'ye satır ekerek ölçüyor).

## 4. WEB VE MASAÜSTÜ AYNI KARARI KULLANIR (kullanıcı §9)

- **Masaüstü** `FieldAccess.Gorunur/Duzenlenebilir`'i **doğrudan** çağırır.
- **Web** aynı fonksiyonun sonucunu `/api/field-access` ucundan okur; **kendi başına yetki
  hesaplamaz.** Test `AA10` ikisinin aynı sonucu verdiğini ölçer.

⚠️ `/api/field-access` bir **güvenlik kapısı değildir**; gerçek kapı serviste ve veri uçlarındadır.
Bu uç yalnız "boş kolon / 0,00 gösterme" (yanlış bilgi) sorununu çözer.

## 5. GERÇEK GUI TESTİNİN BULDUKLARI (§28'in tam olarak istediği şey)

Testler yeşildi ama ekran yanlıştı. Gerçek tarayıcı/uygulama turunda **üç hata** çıktı:

| # | Belirti | Kök neden | Düzeltme |
|---|---|---|---|
| G1 | Malzeme listesinde **kolon başlığı kalıyor, hücre düşüyordu** → kolonlar kayıyordu (gizlemekten kötü) | `Materials.razor`'da kolon döngüsü **dört yerde**; yalnız gövdedeki değiştirilmişti | Dördü de `CizilecekKolonlar` kullanır |
| G2 | Özet şeridinde **"₺0 stok değeri"** görünüyordu | `ApiClient` özeti ELLE eşliyor; olmayan alanı `0m` yapıyordu → "gizli" ile "sıfır" ayırt edilemiyordu | `decimal?` + alan yoksa `null` |
| G3 | **Yeni Malzeme formunda** alan hâlâ vardı | Görünürlük yanıt ŞEKLİNDEN çıkarılıyordu; `/materials/new` ızgarayı hiç yüklemez | Karar `/api/field-access`'ten okunur (masaüstüyle aynı kaynak) |

Üçü de düzeltildi ve **aynı gerçek tarayıcı turunda yeniden doğrulandı**.

## 6. BULUNAN GERÇEK ÜRÜN HATASI (bu fazın kapsamı dışındaydı, kapatıldı)

**Belirti:** süper admin OLMAYAN bir yönetici (ör. firma admini) **rapor (`rpt_`)**, **kayıt tipi
(`datype_`)** ya da alan (`fld_`) yetkisi verdiğinde işlem başarılı dönüyor ama **izin
kaydolmuyordu.** Hata yok, sonuç da yok — sessiz kusur.

**Kök neden:** `PermissionService.GrantableLimit` devretme tavanı sözlüğünü yalnız
`AppModules.All` üzerinde kuruyordu. Önekli anahtarlar bilinçli olarak `All`'da DEĞİLDİR (menü
maddesi değiller) → sözlükte bulunamıyor, `ClampModule` dört bayrağı da siliyor, satır "boş" sayılıp
hiç yazılmıyordu. **Süper adminde tavan `null` (sınırsız)** olduğu için sorun görünmüyordu; bu
yüzden bugüne kadar fark edilmedi.

**Düzeltme:** sözlükte olmayan anahtarın tavanı **aynı kaynaktan** (`AccessControl.GrantCeiling`)
istek anında hesaplanır. **Kural değişmedi**; yalnız eksik anahtarlar da aynı kuraldan geçiyor.
Bilinen modüllerin davranışı birebir aynıdır ve tavan hâlâ çalışır (test `YK16` ikisini de ölçer).

**Etki:** hiçbir mevcut izin değişmez; yalnız bundan sonraki rapor/kayıt-tipi/alan yetkisi verme
işlemleri **gerçekten kaydolur**.

## 7. KAPSAM DIŞI BULGU (değiştirilmedi)

`PartiesView.axaml` başlık ızgarası ile satır ızgarasının kolon genişlikleri **uyuşmuyor**:
başlıkta "ÜNVAN" x=257'de, satırda karşılığı x=357'de (100 px kayma, başlıklar üst üste biniyor).
**Ölçülerek doğrulandı ki bu bu fazdan gelmiyor:** alan koruması KAPALIYKEN (yönetici görünümü,
BAKİYE kolonu açık) da aynı kayma var. Kozmetik; bu görevin kapsamında değiştirilmedi.

## 8. AÇIK KALANLAR

1. **Fatura ve Kasa/Banka ekranlarının GÖRSEL doğrulaması yapılmadı** — kod yolu Malzemeler ve Cari
   ile aynı desendedir ve servis/API testleriyle kanıtlıdır, ama **gerçek ekran görüntüsüyle
   doğrulanmadı**. "Doğrulandı" diye yazılmıyor.
2. **Açık/koyu tema karşılaştırması yapılmadı** (her iki platformda koyu temada bakıldı).
3. **Mobil/responsive görünüm** bu turda ölçülmedi.
4. Senkron süzme yok (D1) · DENY yok (K1) — ikisi de bilinçli ve yazılı.
5. Ön muhasebede alan ayrımı **kaba**: borç/alacak/bakiye TEK kalemdir (matematiksel olarak
   birbirinden türetilebildikleri için ayırmak sahte incelik olurdu); kasa ile banka da tek
   `finance` alanı altındadır.

---
---

# ⭐ UYGULAMA KAYDI — FAZ 3b-6 (2026-09-05) — GÖRSEL BORÇ KAPATMA VE TABLO HİZASI

> Amaç yeni mimari değil; 3b-5'te **açıkça yapılmadı** diye yazılan görsel doğrulamaları gerçekten
> yapmak ve ölçülen hizalama hatasını düzeltmek. Commit/push/deploy yok, migration gerekmedi.

## 1. PARTIESVIEW HİZA HATASI — KÖK NEDEN

**Belirti (3b-5'te ölçülmüştü):** Cari Hesaplar'da başlık ızgarası satır ızgarasından **100 px**
kayıktı; "KOD" ile "ÜNVAN" üst üste biniyordu.

**Kök neden (ölçülerek bulundu, varsayılmadı):** tablolar iki AYRI `Grid` kullanır — başlık
(`Border.TableHeader`, `DockPanel.Dock="Top"`) ve satırlar (`ListBox.Table` şablonu). `ListBox.Table`
stili `ScrollViewer.HorizontalScrollBarVisibility="Auto"` taşır:

| | Genişlik | Sonuç |
|---|---|---|
| Satırlar | **doğal** genişlik (kolon toplamı 790 px) | yatay kaydırılır |
| Başlık | `DockPanel`'in **dar** genişliği (590 px) | `Auto` kolonlar kırpılır, `*` kolon küçülür |

İki grid farklı kullanılabilir genişlikte ölçüldüğü için kolon genişlikleri ayrıştı. **Alan
gizlemeyle ilgisi yoktu:** koruma kapalıyken (yönetici görünümü, BAKİYE açık) de aynı kayma ölçüldü.

**Çözüm — `Controls/TableHeaderSync.cs` (yeni, salt görsel):** başlığı gövdeyle aynı ölçüm ve
kaydırma bağlamına sokar.
1. **Genişlik:** başlık içeriğinin `MinWidth`'i = listenin `Extent.Width` − başlığın yatay padding'i.
   (Padding düşülmezse `*` kolon tam padding kadar — ölçüldü: **24 px** — fazla alır.)
2. **Hizalama:** içerik `HorizontalAlignment=Left`. (Aksi hâlde Avalonia taşan içeriği ORTALAR ve
   başlık sola kayar — ölçüldü: **112 px**.)
3. **Kaydırma:** liste yatay kaydıkça başlık `TranslateTransform` ile aynı miktarda ötelenir.

Değerler listenin KENDİ `ScrollViewer`'ından okunur; ikinci bir genişlik kaynağı üretilmez.

**Doğrulama (gerçek GUI, UI Automation ölçümü):** 1000 · 1180 · 1500 · 1800 px pencere
genişliklerinde ve 900 px dar pencerede başlık/satır x koordinatları **birebir aynı**. 10.000 kayıtla
(201 sayfa) da hizalı; gizli kolon varken de hizalı.

Aynı çözüm Faturalar, Kasa/Banka ve cari ekstresi tablolarına da uygulandı (aynı desen, aynı sorun).

## 2. BULUNAN İKİNCİ ÜRÜN HATASI — KASA/BANKA BAŞLIĞI GİZLENMİYORDU

**Belirti:** Kasa/Banka'da bakiye korumalıyken satır hücresi gizleniyor ama **"BAKİYE" başlığı
ekranda kalıyordu** — kullanıcının açık şartının ("başlık kalmamalı") ihlali. Üstelik başlıkta `*`
kolon 200 px, satırda 344 px ölçülüp tablo kayıyordu.

**Kök neden:** 3b-5'te `FinanceView`'de satır hücrelerine görünürlük bağlaması eklenmiş, **başlık
hücrelerine eklenmemişti** (Parties ve Invoices'ta eklenmişti — atlanan tek yer).

**Düzeltme:** hesap listesi ve ekstre başlıklarındaki `BAKİYE` de `IsVisible` bağlaması aldı.
**Regresyon:** `MasaustuTabloHizaTests.TH1` — korunan kolon başlığı görünürlük bağlaması taşımak
ZORUNDA. Test, bağlama kasten kaldırılarak (mutasyon) hatayı gerçekten yakaladığı kanıtlandı.

## 3. BULUNAN ÜÇÜNCÜ ÜRÜN HATASI — GİZLİ BAKİYE "0.00" OLARAK SIZIYORDU (WEB)

**Belirti:** web Kasa/Banka'da hesap seçilince kart başlığında **"0.00 TRY"** yazıyordu. Gizli değer
sıfır olarak gösteriliyordu — "yanıltıcı 0,00 bırakma" şartının ihlali.

**Kök neden:** `Finance.razor`'da liste kolonu ve ekstre korunmuştu; **hesap kartı başlığı**
korunmamıştı.

**Düzeltme:** bakiye kapalıyken kart başlığındaki tutar ve onun açıklama metni HİÇ oluşturulmaz.
**Doğrulama:** aynı tarayıcı turunda yeniden ölçüldü — sayfada "0,00" kalmadı.

## 4. BULUNAN DÖRDÜNCÜ ÜRÜN HATASI — FAIL-CLOSED MESAJI ANLAŞILMIYORDU

**Belirti:** kısıtlı kullanıcı faturaya tıkladığında (3b-5'te bilinçli fail-closed) ekranda
şu görünüyordu:
> *"Fatura detayı alınamadı: Response status code does not indicate success: 403 (Forbidden)."*

Kullanıcının yazılım bilgisi yok; sunucunun ürettiği açık Türkçe mesaj kayboluyordu.

**Kök neden:** `ApiClient.GetObjectAsync` / `GetArrayAsync` `EnsureSuccessStatusCode()` kullanıyordu.
Oysa sunucu `{"error":"..."}` gövdesi döndürüyor ve `ErrorMessageAsync` **tam bu iş için** zaten
yazılmıştı — GET yollarında uygulanmamıştı.

**Düzeltme:** iki GET yolu da hata gövdesindeki mesajı kullanır. **Davranış değişmedi** (yine
istisna fırlar); yalnız mesaj anlaşılır oldu:
> *"Fatura detayı alınamadı: Fatura tutarlarını görme yetkiniz olmadığı için fatura detayını açamazsınız."*

Bu düzeltme tüm ekranların GET hatalarını iyileştirir (tek merkez).

## 5. GERÇEK GUI KAPSAMI

| Ekran | Masaüstü koyu | Masaüstü açık | Web koyu | Web açık | Web mobil |
|---|---|---|---|---|---|
| Cari Hesaplar | ✅ | ✅ | ✅ | ✅ | ✅ |
| Faturalar | ✅ | ✅ | ✅ | ✅ | ✅ |
| Kasa / Banka | ✅ | ✅ | ✅ | ✅ | ✅ |
| Malzemeler | ✅ | ✅ | ✅ | ✅ | ✅ |
| Yetkiler | ✅ | — | ✅ | ✅ | ✅ |
| Sol menü (Faz 2 borcu) | ✅ | ✅ | — | — | — |

Her ekran hem **yetkili** hem **kısıtlı** kullanıcıyla açıldı; aynı ekranda alanın yetkilide VAR,
kısıtlıda YOK olduğu ölçülerek karşılaştırıldı (sahte yeşil önlemi).

## 6. KAPSAM DIŞI BULGULAR (değiştirilmedi)

1. **Mobilde Kasa/Banka kartlarında alan etiketi yok** — MudBlazor mobil kart görünümüne geçerken
   `DataLabel` verilmediği için yalnız değerler görünüyor. Alan yetkisiyle ilgisi YOK, bu fazdan
   önce de böyleydi.
2. **Masaüstünde seçili satır 3 px kayıyor** (seçim kenarlığı). Kolonlar kendi içinde tutarlı;
   başlık/gövde hizasını etkilemiyor.
3. **Yetkiler ekranı mobilde sıkışık** ("Ne olur?" açıklama sütunu dar) — taşma YOK, okunur.

## 7. AÇIK KALANLAR

- Fatura **detay/yeni/düzenleme** formları kısıtlı kullanıcıda fail-closed olduğu için görsel olarak
  açılamıyor; **yetkili kullanıcıda** açılıp tutarların göründüğü doğrulandı, kısıtlıda ekranın
  açılmadığı ve nedenin anlaşılır yazıldığı doğrulandı.
- Masaüstü **Yetkiler** ekranı açık temada ayrıca açılmadı (koyu temada doğrulandı; web'de her iki
  temada doğrulandı).

---

# FAZ 3c — KAÇAK KANALLARIN KAPATILMASI (2026-09-05)

> **Amaç:** Faz 3b'de kapatılan bir alanın (**malzeme birim fiyatı**) *aynı bilgiyi taşıyan başka
> ekranlardan* okunabildiği tespit edildi. Faz 3c bu **kaçak kanalları** kapatır.
> **Yeni yetki motoru KURULMADI**, `AccessControl` · `role_permissions` · `user_permissions` ·
> `field_protections` · `fld_` düzeni · EDIT⇒VIEW · şube kapsamı · tenant sınırı · yetki sırası
> **değiştirilmedi**. **Yeni migration gerekmedi.**

## 1. Ölçülen kaçak (varsayım değil)

`fld_materials_unit_price` korumalıyken kullanıcı fiyatı **hâlâ** şuradan görüyordu:

| Kanal | Taşıyıcı | Durum (öncesi) |
|---|---|---|
| Stok Hareketleri listesi/grid'i | `stock_movements.unit_price` | 🔴 açık — fiyat aynen geliyordu |
| Malzeme şablonu | `material_templates.unit_price` | 🔴 açık |
| Stok hareketi raporu | — | ✅ zaten fiyat taşımıyor (ölçüldü) |

Her ikisi de **malzeme birim fiyatının başka bir taşıyıcısıdır**; bu yüzden kataloğa **yeni alan
eklenmedi** — aynı alanın diğer taşıyıcıları **aynı karara** bağlandı (tek karar noktası: `FieldAccess`).

## 2. Yapılan değişiklik (en küçük doğru değişiklik)

| Katman | Dosya | Ne yapıldı |
|---|---|---|
| Okuma | `StockService.SearchMovements` / `SearchMovementsGrid` | Fiyat maskelenir. Karar **sorgu başına bir kez** (satır başına DEĞİL) — ADR-223 performans sözleşmesi korunur. |
| Yazma | `StockService.ApplyLine` (tek yazma noktası) | Fiyatı göremeyen kullanıcının gönderdiği fiyat **yok sayılır**. |
| Okuma+Yazma | `MaterialTemplateService.Get` / `Update` | Okumada maskelenir; güncellemede **saklı değer korunur** (`FieldAccess.YazmaDegeri`). |
| Web UI | `Stock.razor`, `MaterialTemplates.razor` | Kolon **başlığıyla birlikte** çizilmez; giriş alanı açılmaz. Karar `/api/field-access`'ten gelir. |
| Masaüstü UI | `StockEntryView(.axaml/ViewModel)`, `MaterialTemplatesView(...)` | Aynı karar `MaterialService.FiyatGorunur` üzerinden; ikinci bir yetki mantığı yok. |

### Yazma davranışında bilinçli fark (raporlanmıştır)

- `materials.unit_price` **NOT NULL DEFAULT '0'** → gizliyken `0` göstermek **yanıltıcıydı**, alan
  yanıttan tamamen çıkarıldı.
- `stock_movements.unit_price` **NULL kabul eder** → gizliyken `null` + ekranda `—` bu alanın
  **doğal** durumudur. Bu yüzden JSON alanı silinmedi, **null** döndürülüyor.
  Gerekçe: üç uçta 18 alanlık koşullu projeksiyonu elle yazmak bakım tuzağı olurdu.
- Yeni hareket **yeni kayıttır**; korunacak eski değer yoktur → 403 yerine "fiyatsız hareket" yazılır.

## 3. Testler (dar kapsam — Faz 3c test politikası)

| Süit | Sonuç |
|---|---|
| `AlanKacakKanaliTests` (yeni, KK1–KK7) | 7 ✅ |
| `AlanYetkiApiTests` (+ yeni `AA13`) | ✅ |
| `AlanYetkisiTests` | ✅ |
| **Toplam** | **43 geçti / 0 başarısız** — 39 sn test, 46 sn duvar saati |

Geniş regresyon **çalıştırılmadı** (kullanıcı talimatı). Build: Infrastructure · Api · Web · Desktop
**0 hata**.

## 4. GERÇEK GUI doğrulaması (A/B — sahte yeşil önlemi)

Ham veritabanına `unit_price = 777.55` taşıyan bir hareket ekildi; **koruma AÇIK/KAPALI** iki durumda
aynı ekran ölçüldü.

### Web (`/stock`, kısıtlı kullanıcı `p5depo2`)

| | Koruma KAPALI | Koruma AÇIK |
|---|---|---|
| Kolon başlıkları | 11 — `B. FİYAT` **var** | 10 — `B. FİYAT` **yok** |
| İlk satır hücreleri | 11 (`777.55` görünür) | 10 (`777` sayfada **hiç yok**) |
| Form alanı `Birim Fiyat` | var | yok |
| Başlık ↔ hücre sayısı | eşit | eşit → **kolon kayması yok** |

`/material-templates`: koruma açıkken `Birim Fiyat` etiketi ekranda **yok**.

### Masaüstü (gerçek pencere, UI Automation ile ölçüldü)

**Malzeme Giriş-Çıkış** ekranı, kolon başlığı ve satır hücresi **piksel konumuyla**:

| Koruma | Başlıklar (x) | Satır (x) |
|---|---|---|
| KAPALI | TİP 367 · MALZEME 447 · YÖN 787 · MİKTAR 847 · **B.FİYAT 947** · AÇIKLAMA 1027 | … · **777.55 @947** · not @1027 |
| AÇIK | TİP 367 · MALZEME 447 · YÖN 827 · MİKTAR 887 · AÇIKLAMA 987 | … · `5` @887 · not @987 |

Her iki durumda da **başlık ve hücre x konumları birebir aynı** → alan gizlendikten sonra
**kolon kayması yok** (Faz 3b-6'da düzeltilen hizalama korunuyor).

**Stok Hareketleri** ekranı (ayrı ekran, `StockMovementsView`) — servis katmanı kanıtı:

- Koruma KAPALI → satır nesnesi: `UnitPrice = 777.55, PriceText = 777.55`
- Koruma AÇIK  → satır nesnesi: `UnitPrice = , PriceText = —`
- Ham veritabanı her iki durumda da `777.55` → **koruma yalnız GÖRÜNÜMÜ etkiler, veriyi silmez.**

Ekran görüntüleri: `artifacts/p3c-desk-gc-acik.png` · `artifacts/p3c-desk-gc-korumali.png`
(artifacts git'te değildir).

## 5. KAPSAM DIŞI — HÂLÂ AÇIK (sonraki faz)

`unit_price` bilgisinin bu fazda **kapatılmayan** diğer taşıyıcıları:

- bakım kalemleri · yakıt kayıtları · fatura satırı birim fiyatı · satın alma · ekipman bakımı

Kataloğa hiç girmemiş, benzer hassasiyetteki diğer alanlar: `fx_rate` (kur), `withholding_amount`
(stopaj), `cost_center_id` (masraf merkezi).

Bunlar **bilerek** bu fazın dışında bırakıldı (dar kapsam kuralı) ve burada kayda geçirilmiştir.

---

# FAZ 3c-2 — KALAN KAÇAK KANALLAR (2026-09-05)

> Yeni yetki motoru KURULMADI · katalog GENİŞLETİLMEDİ · yeni migration GEREKMEDİ.
> Aynı alanın (`materials.unit_price`) kalan taşıyıcıları **aynı karara** bağlandı.

## 1. Ölçüm — hangi `unit_price` gerçekten malzeme fiyatını taşıyor?

| Tablo | `material_id` var mı? | Karar |
|---|---|---|
| `purchase_order_lines` | ✅ NOT NULL | 🔴 taşıyıcı → **kapatıldı** |
| `invoice_lines` | ✅ NULL (opsiyonel) | 🔴 malzeme satırında taşıyıcı → **kapatıldı** |
| `maintenance_materials` · `equipment_maintenance_materials` | ✅ | detayda fiyat **gösterilmiyor** → doğrudan kaçak YOK; türetilmiş maliyet raporda kapatıldı |
| `fuel_depot_entries` · `fuel_distributions` | ❌ | **kapsam dışı** — yakıtın litre fiyatı, malzeme fiyatı değil (ADR-223 §4.5) |

## 2. 🔴 FAZ 3c'DE ÜRETİLEN GERÇEK HATA — düzeltildi

FAZ 3c'nin yazma kapısı `StockService.ApplyLine` içindeydi ve **sunucunun kendi kaydından okuduğu**
fiyatı da siliyordu: fiyatı göremeyen depo görevlisi **mal kabul** yaptığında, siparişte YAZILI olan
fiyat stok hareketine geçmiyordu. Bu güvenlik değil **sessiz veri kaybı**dır.

**Düzeltme:** kapı yalnız **kullanıcının gönderdiği** fiyata uygulanır
(`ReceiveInTx(..., fiyatSunucuKaynakli: true)` → `PurchaseOrderService.Receive`). Regresyon: **KL5**.

## 3. Yapılan değişiklik

| Katman | Ne yapıldı |
|---|---|
| `PurchaseOrderService.Lines` | fiyat maskelenir (karar **sorgu başına bir kez**) |
| `PurchaseOrderService.List` | sipariş TOPLAMI hiç hesaplanmaz (miktar biliniyorken fiyat geri hesaplanabilirdi) |
| `PurchaseOrderService.Create` | gönderilen fiyat yok sayılır → NULL ("fiyat belirtilmedi" zaten geçerli durum) |
| `InvoiceQueryService.Get` | **malzeme satırı içeren** fatura detayı fail-closed; hizmet faturası eskisi gibi açılır |
| `InvoiceService.Create` | malzeme satırlı fatura **açık ret** — 0 yazmak yanlış mali belge üretirdi (§6 sessiz veri kaybı yasağı) |
| `ReportService` | `miktar × birim_fiyat` kolonları tabloda **hiç yer almaz** (araç ×2, bakım, günlük faaliyet ×2) |
| Web + masaüstü | Satın Alma: fiyat alanı ve TOPLAM kolonu başlığıyla birlikte çizilmez |

**Neden kolon siliniyor, sıfırlanmıyor:** "0 ₺" yanlış bilgidir. Toplam kolonu da silinir; aksi hâlde
`toplam − yakıt` ile malzeme maliyeti geri elde edilirdi.

## 4. Doğrulama

| Kontrol | Sonuç |
|---|---|
| `AlanKacakKanali2Tests` (KL1–KL8, yeni) | 8 ✅ |
| Komşu süitler (`SatinAlma`, `AlanKacakKanali`, `PartyAccounting`, `AccountingReport`, `GunlukRaporlar`) | **96 geçti / 0 başarısız** (1 atlanan — önceden de atlanıyordu) |
| Build: Infrastructure · Api · Web · Desktop | 0 hata |
| Web GUI A/B (`/purchasing`) | koruma AÇIK → "Birim Fiyat" alanı YOK · KAPALI → var ✅ |

## 5. Kapsam dışı (raporlandı, değiştirilmedi)

- **Yakıt** `unit_price` — ADR-223 §4.5 gereği farklı alan.
- **Maliyet merkezi özeti** ve **iş emri maliyeti**: yakıt + bakım + satın alma karışık toplamlar;
  kapatılması ayrı bir kapsam kararı gerektirir (D4).
- **`fx_rate` · `withholding_amount` · `cost_center_id`**: kataloğa yeni alan eklemek **D4 kapsam
  kararıdır** (ADR-222 §14 "ayrıca onay"). Varsayımla genişletilmedi.

---
---

# FAZ 3d — YETKİ EKRANI UX (2026-09-05)

> ADR-222 §12'nin planladığı faz: **arama · filtre · diff · indeterminate**.
> Yetki mimarisi, precedence, EDIT⇒VIEW, `fld_` düzeni ve kaydetme yolu **hiç değişmedi**;
> eklenen her şey **görünürlük** ve **kaydedilmemiş değişikliğin gösterimi**dir.
> **Migration gerekmedi · yeni API ucu yok.**

## 1. Eklenenler (web + masaüstü, aynı davranış)

| Özellik | Ne işe yarar |
|---|---|
| **Arama** | Ağaç 300+ satır; yönetici aradığı ekranı gözle tarıyordu. Ekran/alan adı ve grup adında arar. |
| **Yalnız verilenler** | Kullanıcının fiilen sahip olduğu satırlar. |
| **Yalnız değişenler** | Kaydetmeden önce **yalnız kendi değişikliğini** gözden geçirme. |
| **Üç durumlu grup kutusu** | "Hepsi / hiçbiri / **KISMEN**" ayrımı artık görünür; kısmi grup eskiden ancak satır satır okunarak anlaşılıyordu. |
| **Değişiklik izi** | Değişen satırda ● işareti + canlı rozet: *"N satır değişti · X yetki eklenecek · Y yetki kaldırılacak"*. |

## 2. Kaydetme özeti düzeltildi (gerçek bir eksik)

Özet eskiden yalnız "ekran var/yok" karşılaştırıyor ve kullanıcıya
*"(İşlem hakları — ekle/düzenle/sil — değişmiş olabilir.)"* diyordu; yani ekran **ne kaydettiğini tam
söyleyemiyordu**. Artık işlem hakkı ve özel buton değişiklikleri de sayılır, o belirsiz cümle kalktı.

## 3. Kritik sözleşme — süzgeç veri kaybettirmez

Süzgeç **yalnız görünürlüktür**: gizli satırın işaretleri korunur ve aynen kaydedilir.
`Collect()` süzgeç değişkenlerine hiç bakmaz — bu bir **testle kilitlendi** (UX3). Buna karşılık
"Tümünü Seç / Temizle" ve grup kutusu **yalnız görünen** satırlara uygulanır: yönetici ekranda
görmediği bir satırı yanlışlıkla yetkilendirmiş ya da silmiş olmaz.

## 4. Doğrulama

| Kontrol | Sonuç |
|---|---|
| `YetkiEkraniUxTests` (UX1–UX4, yeni) | 10 ✅ (bağlama adları · üç durum · süzgeç-kaydetme ayrımı · web/masaüstü paritesi) |
| Yetki süitleri (`AlanYetkiEkrani`, `YetkiSirasi`, `RolIzinleri`, `AlanYetkisi`, `MasaustuTabloHiza`) | **83 geçti / 0 başarısız** |
| Build: Web · Desktop | 0 hata |
| **Web GUI** (gerçek tarayıcı, `/permissions`) | arama: matris **22 → 2 satır** · ● işareti çıktı · rozet *"1 satır değişti · 1 yetki eklenecek · 0 kaldırılacak"* · yalnız kısmi grup indeterminate ikonu gösterdi ✅ |
| **Masaüstü GUI** (gerçek pencere, UI Automation) | arama: **342 → 16 → 342** kutu (geri dönüşte kayıp yok) · rozet canlı güncellendi · grup kutuları `Indeterminate` / `Off` doğru ✅ |

## 5. Kapsam dışı

Yetki **şablonu** ekranı aynı `PermMatrix` bileşenini kullandığı için arama/süzgeç/rozeti otomatik
aldı; ayrıca bir uyarlama yapılmadı. Masaüstü "Yetki Şablonları" ekranı bu turda değiştirilmedi.
