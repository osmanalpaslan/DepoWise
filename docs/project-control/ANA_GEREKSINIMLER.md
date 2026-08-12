# ALPNEX — ANA GELİŞTİRME GEREKSİNİMLERİ (kalıcı kapsam)

> Son güncelleme: **2026-08-12** · Bu dosya **kapsam kaydıdır**: bir madde yalnızca o tur
> konuşulmadığı için kapsamdan DÜŞMEZ. Her turun sonunda "tamamlananlar / devam edenler /
> henüz yapılmayanlar" bu dosyaya göre raporlanır.

| # | Gereksinim | Durum |
|---|---|---|
| G1 | Yetkiler ekranı — görüntüle / güncelle / **sıfırla** + yetki devri sınırı | 🟡 Kısmen var |
| G2 | Birden fazla yetki ağacının birleştirilmesi | 🔴 Mimari yeniden ele alınmalı |
| G3 | Tablo/grid satır seçimi (metne tıklama) | 🔴 Eksik (kök neden bulundu) |
| G4 | Türkiye standartlarına uygun ön muhasebe | 🔴 Yok |
| G5 | Web/Desktop ekran görünürlüğü yönetimi | 🔴 Yok |
| G6 | Yeni ekranların otomatik yetki kaydı | 🔴 Yok (5 yerde elle iş) |

---

## G1 — YETKİ SİSTEMİ

### Mevcut mimari (çalışan kısım)

```
SessionContext (UserId, CompanyId, RoleKeys, PermissionSet, OperatingBranchId, BlockedModules)
        │
        ├── AccessControl.Require(s, modul, aksiyon)   ← deny-by-default, tek kapı
        ├── PermissionSet: modül × {View,Create,Edit,Delete} + özel butonlar
        └── PermissionSnapshot(+Cache): oturum kurulurken hesaplanır
```

**Yazma zinciri (`PermissionService.SaveForUser`) — 6 kapı, hepsi TEK transaction içinde:**

1. `AccessControl.Require(actor, "permissions", Edit)`
2. `EnsureUserOwned` — çapraz-firma reddi
3. `EnsureManageableTarget` — admin, başka admini/süper admini düzenleyemez
4. Süper-admin düzeyi ekranlar yalnız Kısıtlı Süper Admin'e verilebilir
5. Admin düzeyi ekranlar yalnız Admin'e verilebilir (`IsAdminRestricted` + `CompanyGrantService`)
6. `RoleGrantService.BlockedForUser` — role kapatılmış ekran kimseye verilemez
7. **`GrantableLimit` + `ClampModule`** — aktörün kendinde olmayan yetki kırpılır
8. `EditLockGuard` — sürüm çakışmasında tüm işlem geri alınır (409)
9. `AuditWriter` — değişiklik audit'e yazılır

> ✅ **Kullanıcının istediği "kendinde olmayanı veremez" kuralı BÜYÜK ÖLÇÜDE VAR** ve
> **aksiyon seviyesinde** çalışıyor (`ClampModule` View/Create/Edit/Delete'i ayrı ayrı kırpar).
> Buton yetkileri de kırpılıyor (`clampBtns`).
> Kapı **servis katmanındadır** → API'den doğrudan çağrı da aynı kapıdan geçer. UI'ya bağımlı değil.

### 🔴 Bulunan açık — ESCALATION BACKDOOR

`PermissionService.cs:256`:

```csharp
// Açık hiç izni olmayan firma admini → geriye dönük uyum: sınırsız
if (mods.Count == 0 && btns.Count == 0 && actor.IsCompanyAdmin) return (null, null);
```

Firma admini genelde **admin bypass** ile çalışır ve `user_permissions` tablosunda **hiç satırı olmaz**.
Bu durumda kırpma **tamamen devre dışı** kalır → admin, kendinde olmayan her yetkiyi verebilir.
Yani kural kâğıt üzerinde var ama **tipik durumda uygulanmıyor**.

**Öneri:** admin bypass'ı *etkin yetki* olarak modelle — `GrantableLimit`, aktörün rolünden gelen
bypass'ı da hesaba katan **EffectivePermissionSet** döndürsün; boş-satır kısayolu kaldırılsın.
Geriye dönük kırılmayı önlemek için: süper admin sınırsız kalır, firma admini için `IsAdminRestricted`
dışındaki modüllerde bypass = tam yetki sayılır (bugünkü davranışın açık ve denetlenebilir hâli).

### 🔴 Eksikler

| Eksik | Kanıt |
|---|---|
| **Yetki sıfırlama YOK** | Servis, API, web, masaüstü — hiçbirinde reset yok |
| **API'de yalnız 2 uç** | `GET /api/permissions/{userId}` · `POST /api/permissions/{userId}` |
| Masaüstü Yetkiler ekranında **yalnız 2 komut** | `LoadUsers`, `Save` |
| Web Yetkiler ekranında **yalnız 1 buton** | "Yetkileri Kaydet" |
| **Salt-okunur yetki özeti yok** | "Bu kullanıcı neye erişebiliyor?" tek bakışta görülemiyor |
| **Alan/scope bazlı yetki YOK** | Yalnız `BranchScope` (şube) var; alan bazlı yetki hiç yok |

---

## G2 — BULUNAN YETKİ / EKRAN AĞAÇLARI (13 ADET)

| # | Ağaç | Yer | Tür | Eleman |
|---|---|---|---|---|
| 1 | `AppModules.All` | `Application/Security/AppModules.cs` | kod sabiti | 38 modül |
| 2 | `SpecialButtons.All` | aynı dosya | kod sabiti | 7 buton |
| 3 | `IsPublic` | aynı dosya | kod sabiti liste | 4 |
| 4 | `IsSuperAdminOnly` | aynı dosya | kod sabiti liste | 10 |
| 5 | `IsAdminRestricted` | aynı dosya | kod sabiti liste | 6 |
| 6 | `user_permissions` + `user_button_permissions` | DB (M015) | veri | kullanıcı × modül |
| 7 | `company_grant_limits` | DB (M032/M037) + `CompanyGrantService` | veri | firma × modül düzeyi |
| 8 | `role_grant_limits` | DB (M041) + `RoleGrantService` | veri | rol × modül |
| 9 | `permission_templates` | DB (M019) + `PermissionTemplateService` | veri | şablon |
| 10 | **Desktop `ShellViewModel.BuildGroups`** | `ViewModels/ShellViewModel.cs:687+` | kod sabiti | 17 grup / 40 link |
| 11 | **Desktop `ShellViewModel.Navigate` switch** | aynı dosya `:804` | kod sabiti | key → View |
| 12 | **Web `NavMenu.razor Groups[]`** | `Components/Layout/NavMenu.razor:71+` | kod sabiti | 46 link |
| 13 | Web `@page` route'ları | `Components/Pages/*.razor` | kod sabiti | 48 route |

Ek olarak: Web `NavMenu.Visible()` içinde `@admin` / `@super` / `@superr` **sözde-yetki anahtarları**
var — `AppModules` kataloğunda karşılığı yok, ayrı bir kural seti.

### 🔴 Ana problem

`NavMenu.razor:70` içindeki yorum durumu özetliyor:
> *"Masaüstü ShellViewModel.BuildGroups ile BİREBİR (isim/grup/sıra)."*

İki ekran ağacı **elle senkron tutuluyor**. Sayılar zaten ayrışmış: **web 46 link · masaüstü 40 link · 48 route**.

### Önerilen merkezi çözüm — `AppScreens` tek kaynağı

```csharp
public sealed record AppScreen(
    string Key,               // "materials.list"
    string ModuleKey,         // "materials"  → yetki
    string Group,             // "Malzemeler"
    string Label,
    string WebRoute,          // "materials"
    string DesktopNavKey,     // "materials"
    PlatformFlags Platforms,  // Web | Desktop | Both     ← G5
    PermissionAction MinAction = View);
```

- `AppModules.All` bundan **türetilir** (`Distinct(ModuleKey)`) → modül kataloğu elle yazılmaz.
- Masaüstü `BuildGroups` ve web `NavMenu.Groups` bundan **üretilir** → ayna kalmaz.
- Yeni ekran = **tek satır** → yetki ağacı, iki menü ve platform görünürlüğü kendiliğinden gelir (**G6**).
- Mimari testi (RPR-01 deseni): her `@page` route'unun ve her `Navigate` case'inin `AppScreens`'te
  karşılığı olduğunu **kaynak taramasıyla** doğrular → yeni ekran eklerken unutmak testi kırar.

---

## G3 — GRID SATIR SEÇİMİ

### Kök neden (tek nokta)

Masaüstü tablo deseni: **`ListBox.Table`** (`Themes/Components.axaml:311-380`).
Avalonia 12.0.4'te DataGrid paketi uyumsuz olduğu için ListBox kullanılmış.

Satır içeriği **`SelectableTextBlock`** ile yazılıyor — **793 kullanım / 40+ ekran**.

`SelectableTextBlock`, metin seçimini başlatmak için `PointerPressed` olayını **işler ve tüketir**
(`Handled = true`). Olay `ListBoxItem`'a **ulaşmaz** → satır seçilmez.
Satırın boş alanına tıklandığında olay doğrudan `ContentPresenter`'a gider → satır seçilir.
**Kullanıcının tarif ettiği davranışın birebir açıklaması budur.**

### Ortak çözüm noktası — TEK stil

`Themes/Components.axaml` içine, yalnız tablo satırları kapsamında:

```xml
<Style Selector="ListBox.Table SelectableTextBlock">
    <Setter Property="IsHitTestVisible" Value="False"/>
</Style>
```

- **Tek dosyada tek kural** → 40+ ekran birden düzelir, ekran ekran yama yok.
- Selector `ListBox.Table` ile sınırlı → tablo dışındaki metin seçilebilirliği **bozulmaz**
  (rapor/detay/log ekranlarında kopyalama çalışmaya devam eder).
- `Button`, `CheckBox`, `NumericUpDown`, `ComboBox` gibi gerçek kontroller **etkilenmez** —
  onlar `SelectableTextBlock` değil.
- Çift tık / klavye seçimi `ListBox`'ın kendi davranışıdır → **değişmez**.

**Bedel:** tablo hücresindeki metin artık fare ile seçilip kopyalanamaz. Kopyalama ihtiyacı varsa
satır sağ tık → "Kopyala" ya da mevcut Excel dışa aktarımı kullanılır. (Kullanıcı kararı gerekir.)

### Web tarafı

26 sayfa `MudTable`/`MudDataGrid` kullanıyor. HTML'de metin tıklamayı yutmaz; satır tıklaması
`<tr>` üzerindedir. **Aynı hata web'de beklenmiyor** — canlı testte doğrulanmalı, öncelik düşük.

### Etkilenecek ekranlar (masaüstü, `SelectableTextBlock` yoğunluğuna göre)
Araçlar (64) · Malzemeler (55) · Bakım (51) · Talepler (40) · Yakıt (34) · Firmalar (27) ·
Araç Şablonları (26) · Stok Sayım (26) · Kullanıcılar (24) · Giriş-Çıkış (24) ·
Stok Hareketleri (21) · Günlük Faaliyet (21) · +28 ekran daha

---

## G4 — ÖN MUHASEBE

### Mevcut altyapı — neredeyse sıfır

| Var olan | Gerçek durumu |
|---|---|
| `suppliers` | **Sadece lookup**: `(id, company_id, name)`. Vergi no, adres, cari bakiye YOK |
| `stock_documents.invoice_no` | Serbest metin. Fatura **kaydı** değil, not alanı |
| `materials.unit_price` + `currency_code` | Malzeme kartı fiyatı (tek fiyat, tarihsiz) |
| `stock_movements.unit_price` | Hareket anındaki fiyat snapshot'ı — **maliyet için değerli** |
| `Money` + `decimal` + `currency` disiplini | ✅ Sağlam temel |
| `branches` (lokasyon) | ✅ Şube boyutu her yerde var |
| `AuditWriter` | ✅ Değişiklik izi |

**Yok:** cari hesap · müşteri · fatura · fatura satırı · KDV · tahsilat · ödeme · kasa · banka ·
vade · hesap planı · dönem/kapanış.

### Önerilen mimari — 4 katman, sırayla

**Katman 1 — CARİ (temel, her şey buna bağlanır)**

```
parties (cari)            : id, company_id, code, title, type(musteri|tedarikci|ikisi),
                            tax_office, tax_no, tckn, address, phone, email, is_deleted, version
party_accounts (cari hsp) : id, company_id, party_id, currency, opening_balance
party_ledger (cari hrkt)  : id, company_id, party_id, doc_type, doc_id, direction(+1/-1),
                            amount, currency, fx_rate, due_date, branch_id, created_at, operation_id
```

> ⚠️ **Mevcut `suppliers` lookup'ı KALIR** (malzeme kartı ona bağlı). Yeni `parties` ile
> **eşleme** (`suppliers.party_id`) yapılır — veri taşınmaz, mükerrer tutulmaz.

**Katman 2 — BELGE (fatura)**

```
invoices      : id, company_id, party_id, kind(alis|satis|iade), doc_no, doc_date, due_date,
                branch_id, currency, fx_rate, subtotal, vat_total, grand_total, status, version
invoice_lines : id, invoice_id, material_id NULL, description, qty, unit_price,
                vat_rate, discount_rate, line_total
```

**Katman 3 — KASA / BANKA / TAHSİLAT-ÖDEME**

```
cash_accounts : id, company_id, name, kind(kasa|banka), currency, branch_id, iban
cash_ledger   : id, company_id, account_id, direction, amount, currency, doc_type, doc_id,
                party_id NULL, branch_id, value_date, operation_id
```

**Katman 4 — Raporlar** (cari ekstre, yaşlandırma, KDV özeti, kasa/banka defteri)

### Tek kaynak (single source of truth) kuralları

| Veri | TEK kaynak | Türetilen |
|---|---|---|
| Stok miktarı | `stock_movements` (defter) | `stock_balances` |
| Cari bakiye | `party_ledger` | ekrandaki bakiye |
| Kasa/banka bakiyesi | `cash_ledger` | ekrandaki bakiye |
| Malzeme maliyeti | `stock_movements.unit_price` | ortalama/son maliyet |
| Fatura tutarı | `invoice_lines` | `invoices` toplamları (yazılır ama defterden doğrulanır) |

**Mükerrer tutma yasağı:** fatura satırındaki miktar stoğu **doğrudan yazmaz** — mevcut
`StockService.ReceiveIn/IssueOut` çağrılır (`operation_id` ile idempotent). Böylece stok defteri
tek yazıcıdan geçmeye devam eder, paralel stok mantığı oluşmaz.

### Akış

```
FATURA (alış)
  ├→ StockService.ReceiveIn(...)   → stok defteri  (mevcut kod, DEĞİŞMEZ)
  ├→ party_ledger  +borç           → cari
  └→ invoices/invoice_lines        → belge

ÖDEME
  ├→ cash_ledger   -çıkış          → kasa/banka
  └→ party_ledger  -borç kapama    → cari
```

### Türkiye'ye özgü — **yapılandırılabilir**, sabit kodlanmaz

- KDV oranları `app_settings` / `vat_rates` tablosunda (bugün %1/%10/%20 — değişebilir)
- Para birimi + kur (`fx_rate` her belgede saklanır — geçmiş bozulmaz)
- Vade, belge no serisi (şube bazlı seri), tevkifat/stopaj alanları **şemada yeri açılır**, mantık sonra
- e-Fatura/e-Arşiv **bu fazın dışında** — alanlar (ETTN, senaryo) ileride eklenebilecek şekilde bırakılır

### ⚠️ Şube boyutu kaybedilmemeli
`invoices.branch_id`, `cash_accounts.branch_id`, `party_ledger.branch_id`, `cash_ledger.branch_id` —
hepsi zorunlu boyut. Stokta `location_id` ayrı kalır (fiziksel yer ≠ işlemi yapan şube — mevcut ayrım korunur).

---

## G5 — WEB/DESKTOP EKRAN GÖRÜNÜRLÜĞÜ

### Mevcut durum: **hiç yok**
Feature flag / platform visibility mekanizması **yok**. Menüler iki tarafta ayrı ayrı kodda sabit.

### Önerilen model — iki kavram AYRI kalır

```
ERİŞİM = PLATFORM_AKTIF(ekran, platform) && YETKI_VAR(kullanıcı, modül, aksiyon)
```

**Depolama:** mevcut `app_settings` deseni yeterli — yeni tablo:

```
screen_platform_visibility(company_id NULL, screen_key, web_enabled, desktop_enabled, updated_at)
```

`company_id NULL` = sistem geneli varsayılan; dolu = firmaya özel geçersiz kılma.
`CompanyGrantService` ile **aynı desen** → yeni mimari icat edilmez.

**Uygulama noktaları (5'i birden, yoksa boşluk kalır):**

| Katman | Ne yapılır |
|---|---|
| Menü (web+masaüstü) | `AppScreens` + görünürlük → link üretilmez |
| Web route | `_Host`/route guard: kapalı ekranda **404/403** — deep-link ile açılamaz |
| Masaüstü `Navigate` | Kapalı `key` için gezinme reddedilir |
| API | Ekrana özel uçlar için middleware kontrolü (`X-Client-Platform` başlığı + `screen_key` eşlemesi) |
| Oturum | `PermissionSnapshot` yanına `PlatformVisibilitySnapshot` — istemci tek yerden okur |

**Yönetim ekranı:** yeni modül `screen_visibility` ("Ekran Görünürlüğü") — süper admin,
`AppScreens` listesini iki onay kutusuyla (Web / Masaüstü) yönetir.

> ⚠️ Kapalı ekran, yetkisi olan kullanıcıya da açılmaz. Yetki **kaldırılmaz** — sadece o
> platformda erişilemez. İki kavram karışmaz.

---

## G6 — YENİ EKRANIN OTOMATİK YETKİ KAYDI

### Bugün bir ekran eklemek = **5 ayrı yerde elle iş**

1. `AppModules.All` → modül satırı
2. Desktop `ShellViewModel.BuildGroups` → nav link
3. Desktop `ShellViewModel.Navigate` switch → `case`
4. Web `NavMenu.razor Groups[]` → link
5. Web `.razor` → `@page` route

(+ gerekirse `IsSuperAdminOnly` / `IsAdminRestricted` listeleri)

Biri unutulursa: ekran menüde çıkmaz **ya da** yetki ağacında görünmez **ya da** yalnız bir platformda olur.

### Çözüm: `AppScreens` (G2) + mimari test

Yeni ekran = **AppScreens'e bir satır**. Menüler, yetki kataloğu ve platform görünürlüğü **türetilir**.
`AppScreensParityTests` (RPR-01 deseni, kaynak taraması):

- her `@page` route'u `AppScreens`'te var mı
- her `Navigate` case'i `AppScreens`'te var mı
- her `AppScreens` satırının web route'u ve masaüstü key'i gerçekten var mı
- `AppModules.All` = `AppScreens.Distinct(ModuleKey)` mi

> Otomatik keşif (reflection ile route tarama) **bilinçli olarak seçilmiyor**: güvenlik modelini
> zayıflatır (yeni ekran sessizce yetkisiz açılabilir). Bunun yerine **tek bildirim noktası +
> derleme/test zamanı zorlama** — deny-by-default korunur.

---

## GELİŞTİRME SIRASI (en düşük riskten en yükseğe)

| Sıra | İş | Neden bu sırada | Risk |
|---|---|---|---|
| **1** | **G3** grid tıklama | Tek stil satırı, veri/şema/yetki dokunmuyor, kullanıcı acısı en yüksek | 🟢 Çok düşük |
| **2** | **G1a** yetki sıfırlama + yetki özeti | Mevcut servise 1 metot + 1 uç + 2 ekran; şema değişmez | 🟢 Düşük |
| **3** | **G1b** escalation backdoor kapatma | Güvenlik düzeltmesi; testleri önce yaz | 🟡 Orta (mevcut adminleri kısıtlayabilir) |
| **4** | **G2/G6** `AppScreens` tek kaynağı | Menüler türetilir; G5 bunun üstüne kurulur | 🟡 Orta (menü regresyonu) |
| **5** | **G5** platform görünürlüğü | G2 olmadan yapılırsa üçüncü bir ağaç doğar | 🟡 Orta |
| **6** | **G4** ön muhasebe — Katman 1 (cari) | Bağımsız; stok defterine dokunmaz | 🟡 Orta |
| **7** | **G4** Katman 2 (fatura ↔ stok) | Stok defterine bağlanır — en dikkatli iş | 🔴 Yüksek |
| **8** | **G4** Katman 3-4 (kasa/banka, raporlar) | Önceki katmanlar oturduktan sonra | 🟡 Orta |

**Gerekçe:** G3 anında değer verir ve hiçbir şeyi kırmaz. G1 mevcut yapıyı tamamlar. G2/G6 bir sonraki
her ekranın maliyetini düşürür — G5 ve G4'ün ekranları ondan **sonra** gelmeli ki elle iş tekrarlanmasın.
G4 en sona bırakılır çünkü en büyük şema yatırımıdır ve stok defterine dokunur.

---

## TEST PLANI

| İş | İzole test |
|---|---|
| G3 grid | Avalonia headless: `ListBoxItem` seçimi metne tıklandığında tetiklenir · Button/CheckBox kendi davranışını korur · tablo dışı metin seçilebilir kalır |
| G1a sıfırlama | Sıfırlama sonrası kullanıcı hiçbir modüle erişemez · audit satırı oluşur · edit-lock çakışmasında geri alınır · yetkisiz aktör 403 |
| G1b backdoor | **Kendinde X yetkisi olmayan admin, X'i başkasına VEREMEZ** (bugün geçmiyor) · süper admin sınırsız kalır · aksiyon bazlı kırpma · doğrudan API çağrısı da reddedilir |
| G2/G6 | `AppScreensParityTests`: route ↔ nav ↔ modül ↔ AppScreens dört yönlü eşleşme · yeni ekran eklenince eksik bırakılan katman testi kırar |
| G5 | Kapalı ekran: menüde yok · deep-link 404 · masaüstü Navigate reddi · API reddi · **yetki VAR + platform KAPALI → erişim YOK** · yetki YOK + platform AÇIK → erişim YOK |
| G4 cari | Cari bakiye = `party_ledger` toplamı · çapraz-firma izolasyonu · şube boyutu korunur |
| G4 fatura↔stok | Fatura stok defterini `ReceiveIn` üzerinden yazar (paralel mantık yok) · aynı `operation_id` iki kez çalışmaz (idempotency) · fatura iptali ters kayıt üretir · bir satır hatalıysa **tamamı** geri alınır (atomiklik) · eşzamanlı iki fatura oversell üretmez |
| G4 kasa | Kasa bakiyesi = `cash_ledger` toplamı · tahsilat cariyi ve kasayı **tek transaction** içinde günceller |

**Ortak kural:** her iş için tenant izolasyonu · permission · rollback · concurrency · idempotency ·
validation testleri yazılmadan "tamamlandı" sayılmaz. GUI doğrulaması kullanıcıya aittir.

---

## PRODUCTION RİSKİ

**Bu turda production'a DOKUNULMADI** — yalnız repo/kaynak okundu.

Sıradaki işlerin production etkisi:

| İş | Şema | Production etkisi |
|---|---|---|
| G3 | yok | Yalnız masaüstü paketi (yeni sürüm gerekir) |
| G1a | yok | API + web + masaüstü deploy |
| G1b | yok | ⚠️ Davranış değişikliği — mevcut adminlerin yetki verme alanı daralabilir |
| G2/G6 | yok | Menü üretimi değişir — regresyon testi şart |
| G5 | **migration** (1 tablo) | Yeni tablo; mevcut veri değişmez |
| G4 | **migration** (8-10 tablo) | Yalnız EKLEME; mevcut tablolara dokunulmaz |

Hiçbiri mevcut iş verisini **değiştirmez**. Her deploy öncesi yedek + DEPLOYMENT.md sırası
(API → Web → masaüstü publish → **güncelleme paketi**) geçerlidir.

---

# TUR 2026-08-12/2 — G3 + G1a + G1b UYGULANDI, G2/G6 HARİTALANDI

| # | Gereksinim | Önceki | Şimdi |
|---|---|---|---|
| G1a | Yetki güncelleme · **sıfırlama** · **özet** | 🟡 | ✅ **TAMAM** (servis + API + web + masaüstü + audit + test) |
| G1b | Devretme sınırı / escalation | 🔴 açık vardı | ✅ **KAPANDI** (`AccessControl.GrantCeiling` tek kaynak) |
| G1 | Alan/scope bazlı yetki | 🔴 | 🔴 **HÂLÂ YOK** (yalnız `BranchScope`) |
| G2/G6 | Tek kaynak ekran mimarisi | 🔴 | 🟡 **HARİTALANDI + parite kilitlendi** (`AppScreens` henüz yok) |
| G3 | Tablo satır tıklama | 🔴 | ✅ **TAMAM** (tek stil + tünelleme davranışı, 40+ ekran) |
| G4 | Ön muhasebe | 🔴 | 🔴 **BAŞLANMADI** (tasarım hazır) |
| G5 | Platform görünürlüğü | 🔴 | 🔴 **BAŞLANMADI** (G2/G6 türetmesini bekliyor) |

## G3 — nasıl çözüldü
`SelectableTextBlock` `PointerPressed`'i tüketiyordu → satır seçilmiyordu. Basit `IsHitTestVisible=False`
metin kopyalamayı ve satır içi tooltip'leri (`MaintenanceView.axaml:462`) bozardı. Bunun yerine
`Controls/TableRowSelect.cs` **tünelleme (önizleme)** aşamasında satırı seçer, olayı **işaretlemez**:
metin seçimi/kopyalama, tooltip, çift tık ve klavye seçimi **aynen korunur**. Gerçek kontroller
(Button/CheckBox/ComboBox/TextBox/NumericUpDown…) hariç tutulur. Bağlanma: `Themes/Components.axaml`
içinde `ListBox.Table` seçicisine **tek setter**.

## G1b — kapatılan açık
Eski `GrantableLimit` yalnız aktörün açık satırlarına bakıyor, satırı olmayan firma adminini
**sınırsız** sayıyordu. Firma admini tipik olarak bypass ile çalışır ve satırı YOKTUR → kırpma
pratikte hiç uygulanmıyordu. Somut sonuç: **süper adminin aktörün ROLÜNE kapattığı bir modül,
aktör kendisi kullanamadığı hâlde başkasına verilebiliyordu.**
Yeni model: tavan = `AccessControl.GrantCeiling`, `AccessControl.Can` ile **aynı** kuralları uygular
(rol kilidi → süper-admin-only → admin bypass → açık izin). "Erişebildiğim" = "verebileceğim".
Firma admininin normal ekranları devretmesi **bozulmadı** (regresyon testi var).

## 🔴 YENİ BULGU — G2-B1: "Çöp Kutusu" yetki kataloğunda YOK
`trash` ekranı masaüstü menüsünde ve `Navigate` içinde var, web'de `@admin` sözde-anahtarıyla
gösteriliyor; ama `AppModules.All` içinde **yok** → **yetki ağacından yönetilemiyor**. Süper admin
bu ekranı belirli bir kullanıcıya devredemez, Rol Yetki Kontrol ile kısıtlayamaz. Pratikte yalnız
admin bypass'ı sayesinde admin'e açık (kazara doğru davranış).
`ScreenTreeParityTests.A5` bu listeyi **tam olarak** kilitler — yeni bir ekran aynı hataya düşerse test kırılır.
**Düzeltme yetki ağacını değiştirir → kullanıcı kararı gerekir; bu turda YAPILMADI.**

## Ölçülen ağaç büyüklükleri (parite testinden)
modül kataloğu 38 · özel buton 7 · masaüstü menü grubu 17 / bağlantı 40 · masaüstü `Navigate` anahtarı 40+ ·
web menü bağlantısı 46 · web route 48 (parametreli route'lar dahil)

---

# TUR 2026-08-12/3 — G2/G6 AppScreens TEK KAYNAK UYGULANDI

| # | Gereksinim | Önceki | Şimdi |
|---|---|---|---|
| G2/G6 | Tek kaynak ekran mimarisi | 🟡 haritalandı | ✅ **TAMAM** — iki menü de `AppScreens`'ten üretiliyor |
| G2-B1 | "Çöp Kutusu" katalog dışı | 🔴 açık | ✅ **KAPANDI** — kataloğa alındı, yönetim düzeyi |
| G5 | Platform görünürlüğü | 🔴 | 🟡 **VERİ HAZIR** (`ScreenPlatform`), çalışma zamanı yönetimi yok |

## Mimari

```
AppScreens.All  (48 ekran · 16 grup)         ← TEK BİLDİRİM NOKTASI
   ├── GroupsFor(Desktop) + ScreensOf(...)   → ShellViewModel.BuildGroups
   ├── GroupsFor(Web)     + ScreensOf(...)   → NavMenu.razor Groups
   ├── ModuleKey                             → AppModules yetki ağacı
   ├── WebRoute / DesktopNavKey              → route + gezinme paritesi (test)
   └── Platforms (Desktop|Web)               → G5'in temeli
```

Web'e **proje referansı verilmedi**; projenin yerleşik **paylaşılan kaynak dosya** deseni kullanıldı
(`<Compile Include="..\DepoWise.Application\Security\AppScreens.cs" />`) — `ListColumns`,
`MovementTypeOptions` ile aynı yol. Web'in "her şeyi API'den al" sınırı korundu; MudBlazor ikonları
Application katmanına sızdırılmadı (grup→ikon eşlemesi `NavMenu.razor` içinde).

## Yeni ekran eklemek: **5 nokta → 2 nokta**

| Önce (5) | Şimdi (2) |
|---|---|
| 1. `AppModules.All` | 1. **`AppScreens.All` — tek satır** |
| 2. Masaüstü `BuildGroups` | 2. Masaüstü `Navigate` içine `case` (ekranı AÇAN kod) |
| 3. Masaüstü `Navigate` | *(web route zaten `@page` ile sayfanın kendisinde)* |
| 4. Web `NavMenu.Groups` | |
| 5. Web `@page` | |

Modül yalnız YENİ bir yetki düğümü gerekiyorsa `AppModules.All`'a eklenir; mevcut bir modüle bağlanan
ekranlarda buna da gerek yok. Eksik bırakılan her katman **testte kırılır**.

## Platform farkları (G5 verisi, bugünkü gerçek)

- **Yalnız masaüstü (3):** `import_export` · `material_templates` · `stock.distribute`
- **Yalnız web (9):** `backup` · `company_permissions` · `import` · `machine_backups` ·
  `purge_company` · `quota_monitor` · `reset_company_business` · `role_permissions` · `server_status`
- Kalan **36** ekran iki platformda.

## Test
`AppScreensParityTests` (16) + güncellenmiş `ScreenTreeParityTests` (12) = **28**.
Taşıma regresyonu: masaüstü menüsü **40 bağlantı**, web menüsü **46 bağlantı** — sıra, başlık, route ve
yetki anahtarları taşımadan öncekiyle **birebir** doğrulanıyor.
**Tüm paket: 1637 geçti / 0 başarısız / 35 atlandı (PostgreSQL).**

---

# TUR 2026-08-12/4 — G5 PLATFORM GÖRÜNÜRLÜĞÜ UYGULANDI

| # | Gereksinim | Önceki | Şimdi |
|---|---|---|---|
| G5 | Web/Desktop ekran görünürlüğü | 🟡 veri hazır | ✅ **TAMAM** — 5 katman + yönetim ekranı |

## Model
`ERİŞİM = PLATFORM_AKTİF && YETKİ_VAR` — üç kavram ayrı: **Platform** (bu ekran bu uygulamada açık mı) ·
**Yetki** (`AccessControl`) · **Kapsam** (`BranchScope`).

`EFFECTIVE = AppScreens.Platforms (derleme varsayılanı) AND firma kaydı`

**⚠️ Yalnız DARALTIR.** Katalogda o platformda olmayan ekran veritabanı kaydıyla **açılamaz** — açılsaydı
menüde karşılığı olmayan bir giriş belirir, tıklanınca hiçbir yere gitmezdi. Elle DB'ye `enabled=1`
yazılsa bile katalog kazanır (savunma katmanı, test G5_10).

**Varsayılan:** kayıt yoksa katalog geçerli → migration **hiçbir ekranı kapatmaz** (test G5_27).

## 5 katman
| Katman | Uygulama |
|---|---|
| Masaüstü menü | `ShellViewModel.BuildGroups` platform filtresi |
| Masaüstü gezinme | `Navigate` başında **merkezi kapı** — kod içinden tetiklense de açılmaz |
| Web menü | `NavMenu` iki render yolunda da `Auth.PlatformOpenForRoute` |
| Web deep-link | `MainLayout` — platform kapalıysa `@Body` **render edilmez** |
| Oturum/önbellek | Firma başına 60 sn TTL + **yazmada anında düşürme**; web menüyle birlikte tazelenir |

## Yönetim ekranı
`/screen-visibility` — **yalnız süper admin** (`screen_visibility`, `IsSuperAdminOnly`). Gruplu tablo;
her ekran için Masaüstü/Web kutusu, "o platformda yok" ise kutu kapalı. Kapatmada açık onay + sonucun
düz anlatımı. Diğer süper admin ekranları gibi (Rol Yetki Kontrol, Kota İzleme) **yalnız web'de** sunulur.

## DB
`Migration065_ScreenPlatformVisibility` — `screen_platform_visibility(company_id, screen_key, platform,
enabled, …)`, `UNIQUE(company_id, screen_key, platform)`. **Firma bazlı** (izolasyon testi G5_19/20).
Desen `role_grant_limits` / `company_grant_limits` ile aynı. Idempotent (G5_26).
⚠️ **Production migration ÇALIŞTIRILMADI.**

## Test
`ScreenPlatformVisibilityTests` — **28 test**: varsayılan · null harita · bilinmeyen ekran ·
A/B/C/D senaryoları · daraltma kuralı · platform+yetki kombinasyonları · **admin/süper admin platform
kapısından muaf değil** · gezinme/route kapıları · firma izolasyonu · mükerrer kayıt · yetki ·
önbellek düşürme · audit · yönetim listesi · migration idempotent · **migration sonrası hiçbir ekran kapanmadı**.

**Tüm paket: 1665 geçti / 0 başarısız / 35 atlandı (PostgreSQL).**

## ⚠️ Bilinen sınır — API katmanı
Platform kısıtı API'de **uçtan uca zorlanmıyor**: sunucu, isteğin hangi platformdan geldiğini bilmiyor
(istemci platform başlığı YOK). Eklenip fail-closed yapılsaydı **eski masaüstü sürümleri (1.0.136)
tamamen kırılırdı**. Platform görünürlüğü bilinçli olarak **idari kapsam** aracıdır; güvenlik sınırı
**yetkidir** ve o serviste zorlanmaya devam ediyor. Final sürümde masaüstü de başlık göndermeye
başlarsa fail-closed'a geçilebilir — **ayrı karar**.

---

# TUR 2026-08-12/5 — G4-1 CARİ ALTYAPISI UYGULANDI

| # | Gereksinim | Önceki | Şimdi |
|---|---|---|---|
| G4-1 | Cari (parties + party_ledger) | 🔴 | ✅ **TAMAM** — servis + API + web + masaüstü + 35 test |
| G4-2 | Fatura + stok entegrasyonu | 🔴 | 🔴 sırada |

## Veri modeli
`parties` — kod (firma içinde benzersiz, **silinen kod yeniden kullanılabilir**: kısmi indeks) · ünvan ·
tip (müşteri/tedarikçi/her ikisi) · gerçek-tüzel kişi · vergi dairesi/VKN/TCKN · iletişim · adres ·
il/ilçe · para birimi · not · aktif/pasif · `supplier_id` (mevcut lookup ile **eşleme**, veri taşınmaz) ·
audit alanları + `version` (düzenleme kilidi).

`party_ledger` — tarih · belge türü/no · açıklama · **`direction` (+1 borç / −1 alacak)** · `amount` (TEXT/decimal) ·
para birimi · **vade** · `branch_id` · `source_type`/`source_id` (G4-2 fatura bağı) · **`operation_id`
(idempotency, benzersiz indeks)** · `is_reversed`.

## İki temel kural
1. **BAKİYE SAKLANMAZ.** `parties`'te bakiye kolonu YOK; her zaman `Σ(direction × amount)` ile
   hesaplanır (stok defterindeki kuralın cari karşılığı). Test P28 bunu kolon düzeyinde kilitler.
2. **⭐ STOKLA SINIR.** Cari işlemleri `stock_movements`/`stock_balances`'a **hiç dokunmaz** —
   test P27 yoğun cari işleminden sonra iki tablonun satır sayısının ve stok bakiyesinin
   **değişmediğini** kanıtlar. G4-2'de fatura stoğu yalnız `StockService.ReceiveIn/IssueOut`
   üzerinden yazacak; ikinci bir stok gerçekliği YOK.

## Entegrasyon
- **Yetki:** tek modül `parties` + 4 aksiyon. Ayrı `party_view/party_create/...` anahtarları AÇILMADI.
  Kapı **serviste** (P24/P25). Devretme sınırı regresyonu: P26.
- **AppScreens:** yeni grup **"Ön Muhasebe"** + 2 ekran (`accounting.parties`, `accounting.parties.new`),
  ikisi de Desktop+Web. Menüler otomatik türedi; parite testleri güncellendi (masaüstü 42, web 49 bağlantı).
- **G5:** varsayılan platform katalogdan gelir; `/screen-visibility`'den firma bazında yönetilebilir —
  ek kod GEREKMEDİ.
- **G3:** masaüstü tabloları ortak `ListBox.Table` deseninde → satır seçimi davranışı aynen geçerli.

## Ekranlar
**Masaüstü** (`PartiesViewModel` + `PartiesView`): sol liste (arama/tip/durum + sayfalama), sağ kart
(genel bilgiler · finansal özet · hesap hareketleri yürüyen bakiyeyle) · aktif-pasif · sil.
**Web** (`Parties.razor`, `/parties` + `/parties/{Section}`): aynı işlevler, aynı API.

## Test
`PartyAccountingTests` — **35 test**: CRUD · benzersiz kod · silinen kodun yeniden kullanımı ·
doğrulama (VKN 10 / TCKN 11 hane, boş bırakılabilir) · firma izolasyonu (P09/P10/P20) · borç/alacak/bakiye ·
ondalık · ekstre yürüyen bakiye · **idempotency** · **ters kayıt** · hareketi olan cari silinemez ·
arama/sayfalama/tip filtresi · yetki (4 aksiyon) · **devretme sınırı** · **stokla sınır** · audit ·
migration idempotent.

**Tüm paket: 1700 geçti / 0 başarısız / 35 atlandı (PostgreSQL).**

---

# TUR 2026-08-12/6 — G4-1b CARİ KULLANILABİLİRLİK TAMAMLANDI

| # | Gereksinim | Önceki | Şimdi |
|---|---|---|---|
| G4-1 | Cari altyapısı | ✅ (form UI hariç) | ✅ **TAM** |
| G4-1b | Cari formu + elle hareket | 🔴 | ✅ **TAMAM** (UI → API → Servis → DB → Test) |

## 🔴 Bu turda kapatılan gerçek açık
`PartyDocTypes.ManualEntry` yalnız KATALOG listesiydi; **servis her belge türünü kabul ediyordu**.
Arayüz/API atlanıp doğrudan `docType: "invoice"` ile hareket yazılabiliyordu → G4-2 aynı faturayı
işlediğinde cari **İKİ KEZ** borçlanırdı (sahte belge + mükerrer borç).

**Çözüm — iki ayrı yol:**
- `Add(...)` — **kullanıcı yolu**, yalnız `opening` + `adjustment`.
- `AddFromDocument(...)` — **belge yolu** (G4-2/G4-3), `SourceType` + `SourceId` **zorunlu**.

Kapı **servistedir**; UI ve API atlanabilir, kural yine geçerlidir (test M02).

## Katman katman durum
| Özellik | UI | API | Servis | DB | Test |
|---|---|---|---|---|---|
| Cari oluşturma/düzenleme | ✅ Web + Masaüstü | ✅ | ✅ | ✅ | ✅ |
| Elle hareket (açılış/düzeltme) | ✅ Web + Masaüstü | ✅ | ✅ | ✅ | ✅ |
| Ters kayıt (gerekçeli) | ✅ Masaüstü · ⚠️ Web'de yok | ✅ | ✅ | ✅ | ✅ |
| Aktif/pasif · silme | ✅ Web + Masaüstü | ✅ | ✅ | ✅ | ✅ |

## Test
`PartyManualEntryTests` — **15 test**: elle giriş kısıtı (2 yönlü) · belge yolu + kaynak zorunluluğu ·
belge yolu idempotency · açılış borç/alacak · tarih/vade/belge no/açıklama · şube · ondalık ·
yetki aksiyon ayrımı · **stok izolasyonu (elle + belge yolu)** · form akışı · çifte kimlik kuralı.

**Tüm paket: 1715 geçti / 0 başarısız / 35 atlandı (PostgreSQL).**

---

# TUR 2026-08-12/7 — G4-1c: DOĞRULAMA + İKİ GERÇEK EKSİK KAPATILDI

Bu tur **doğrulama turuydu**: G4-1b raporuna güvenilmeyip repo üzerinden katman katman kontrol edildi.
İki gerçek eksik bulundu; ikisi de kapatıldı.

## 🔴 EKSİK 1 (KRİTİK) — Cari senkronda YOKTU
`parties` ve `party_ledger`, `BusinessSyncService.Tables` listesinde **yer almıyordu**. Masaüstü
çevrimdışı cari açıp elle hareket girebildiği için bu kayıtlar **sunucuya hiç ulaşmıyordu**:
web'de görünmüyor, ikinci makineye gitmiyordu. Masaüstünün çevrimdışı çalışması projenin temel
gereksinimi olduğundan bu **gerçek bir veri kaybı yoluydu**.

**Bu eksik önceki turun raporunda fark edilmemişti.**

Düzeltme: iki tablo listeye eklendi (**`parties` önce** — `party_ledger.party_id` onu referans alır),
`TableModule`'a `parties` yetkisiyle bağlandı (senkron yolu yetki kapısını atlamaz).
Bakiye taşınmıyor çünkü **saklanmıyor** (stock_balances kararının aynısı).

## 🔴 EKSİK 2 — Web'de ters kayıt UI'ı yoktu
Masaüstünde vardı, web'de yoktu (parite ihlali). Eklendi: hareket satırında geri-al düğmesi +
tablo üstünde gerekçe alanı + onay. Masaüstüyle **aynı desen**.

## Katman katman durum (doğrulanmış)
| Özellik | Web | Desktop | API | Servis | DB | Sync | Test |
|---|---|---|---|---|---|---|---|
| Cari oluştur/düzenle | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Elle hareket | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Ters kayıt | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Aktif/pasif · silme | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

**G4-1 + G4-1b + G4-1c artık gerçekten TAM.**

## Test
`PartySyncTests` — 4 test: senkron kapsamı · FK sırası · yetki bağı · türetilmiş veri taşınmaması.
**Tüm paket: 1719 geçti / 0 başarısız / 35 atlandı (PostgreSQL).**

---

# TUR 2026-08-12/8 — G4-2: FATURA (ALIŞ / SATIŞ)

Ön muhasebenin ikinci ayağı: fatura. Cari (G4-1) ile stok defteri arasındaki bağ **burada** kuruldu.

## Mimari karar — AMBIENT TRANSACTION (kullanıcı onayladı)
Fatura; **fatura başlığı + satırlar + cari hareketi + stok belgesini** tek bir işlemde yazmak
zorunda. Ama `StockService` ve `PartyLedgerService` kendi transaction'larını açıyordu; SQLite'ta
ikinci bağlantı yazma kilidinde bloke olacağı için iç içe çağrı **imkânsızdı**. Seçenekler
(ambient transaction / saga / taslak-kesinleştir) kullanıcıya sunuldu, **ambient transaction** seçildi.

Uygulama:
- `StockService.RunDocumentOnce` → `RunDocumentInTx(conn, tx, …)` olarak ikiye ayrıldı; gövde
  transaction AÇMAZ ve COMMIT ETMEZ. Public `ReceiveInTx` / `IssueOutTx` eklendi.
- `PartyLedgerService.Write` → `WriteInTx(conn, tx, …)`; public `AddFromDocumentTx` eklendi.
- Mevcut public imzalar **değişmedi**; eski yol kendi transaction'ını açmaya devam ediyor.
- ⚠️ `StockBalanceWriter` yeniden deneme (retry) yalnız kendi transaction'ını açan yolda geçerli;
  ambient yolda istisna çağırana çıkar ve **tüm işlem** geri alınır.

**Tek yazıcı kuralı korundu:** fatura stok tablolarına DOKUNMAZ, cari defterine DOKUNMAZ.
Kendi paralel stok/cari hesabını **kurmaz**; yalnız üretilen belgelerin kimliğini referanslar
(`invoices.stock_document_id`, `invoices.ledger_entry_id`).

## Veri modeli — Migration067 (şema 67)
`invoice_series` · `vat_rates` · `invoices` · `invoice_lines`.

**Türkiye kuralları KODDA SABİT DEĞİL, VERİDİR:** KDV oranı `vat_rates`'ten, belge serisi
`invoice_series`'ten gelir; tevkifat oranı fatura satırında veri olarak durur. Oran değişirse
migration değil, kayıt güncellenir.

**Silme yok:** fatura fiziksel silinmez. İptal = `status='cancelled'` + ters stok belgesi +
ters cari hareketi. `cancel_stock_document_id` / `cancel_ledger_entry_id` ters belgeleri işaret eder.

## Idempotency
Tek `operation_id` üçe dağıtılır: fatura `op`, stok `op:stock`, cari `op:ledger`. Üçünde de kısmi
tekil indeks var. Aynı istek iki kez → **fatura=1, cari=1, stok=1**.

Ekranlarda `operation_id` form açılışında üretilir ve kayıt başarılı olana kadar **sabit kalır**
(çift tıklama ve ağ tekrarı ikinci fatura üretmez).

## Hesap kuralı
İskonto matrahtan düşer → KDV **iskontolu** tutar üzerinden → tevkifat **KDV üzerinden**.
Toplam fonksiyonu (`InvoiceService.Totals`) hem ekranda hem serviste **aynı koddur** — ekranda
başka, kayıtta başka tutar çıkamaz.

## Düzenleme politikası
Yalnız **bilgi alanları** (karşı belge no, vade, not) düzenlenebilir. Tutar/satır değişmez:
değişmesi gerekiyorsa **iptal + yeni fatura** (defterle sessiz fark oluşmaz). API'de tutar
değiştiren bir uç **yoktur**.

## Katman katman durum
| Özellik | Web | Desktop | API | Servis | DB | Sync | Test |
|---|---|---|---|---|---|---|---|
| Fatura listesi + filtre | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Yeni fatura (alış/satış) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Fatura detayı | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Gerekçeli iptal (ters kayıt) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Bilgi düzenleme | — | — | ✅ | ✅ | ✅ | ✅ | ✅ |
| Belge serisi / KDV katalogları | — | — | ✅ | ✅ | ✅ | ✅ | ✅ |

> Not: seri/KDV **yönetim ekranı** henüz yok (API + servis var, varsayılan seri ilk faturada
> otomatik oluşur). Yönetim ekranı G4-4 kapsamına alındı — **açıkça eksik olarak kaydedildi.**

## Yetki
Yeni modül **`invoices`** — cariden AYRI (depo görevlisi cari listesini görüp fatura kesemeyebilir).
`Delete` aksiyonunun serviste karşılığı **yoktur**: fatura silinmez, `Edit` ile iptal edilir.
AppScreens'e iki ekran eklendi (`accounting.invoices`, `accounting.invoices.new`); masaüstü ve web
menüleri bu katalogdan **türetildiği** için ayrıca menü kodu yazılmadı.

## Senkron
`vat_rates` → `invoice_series` → `invoices` → `invoice_lines` sırasıyla eklendi (FK sırası).
Etkiler kendi tablolarıyla taşınır (`party_ledger`, `stock_movements`); senkron bunları **yeniden
üretmez** → iki kez borçlanma olmaz.

## Test
- `InvoiceTests` — 33 test. Kritik ikisi:
  - **I01** aynı `operation_id` iki kez → fatura=1, cari=1, stok=1 (miktar ve bakiye iki katına çıkmıyor).
  - **I02** ortada hata (stok yetersiz) → fatura=0, satır=0, cari=0, stok belgesi=0, hareket=0.
  - Ayrıca: iskonto/KDV/tevkifat sırası · kuruş yuvarlaması · çift iptal engeli · tüketilmiş malın
    iptalinin reddi · tenant sızıntısı · yetki kapısı · 7 doğrulama senaryosu.
- `InvoiceSyncTests` — 5 test: kapsam · FK sırası · kaynak sırası · yetki bağı · etkilerin
  yeniden üretilmemesi.
- Menü taban çizgileri (S13/S14) fatura ekranlarıyla güncellendi — **gevşetilmedi**, beklenen
  değer bilinçli değişiklikle eşitlendi (42→44 masaüstü, 49→51 web).

**Tüm paket: 1757 geçti / 0 başarısız / 35 atlandı (PostgreSQL).**
**Release derlemesi: 0 hata.**

## Bu turda YAPILMAYANLAR (bilinçli)
- Deploy / publish / release **yapılmadı** (kullanıcı talimatı: Deploy=0, Publish=0, Release=0).
- Production veritabanına **hiçbir yazma yapılmadı** (INSERT/UPDATE/DELETE/DDL/Migration = 0).
- GUI tıklama testi **yapılmadı** — masaüstü ve web ekranları derlendi ve servis katmanı testlerle
  doğrulandı, ancak elle ekran testi yapılmadığı için **yapılmış gibi raporlanmıyor**.
- Belge serisi / KDV oranı **yönetim ekranı** yazılmadı (yukarıda not edildi).
