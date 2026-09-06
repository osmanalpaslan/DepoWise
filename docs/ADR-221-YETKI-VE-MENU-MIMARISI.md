# ADR-221 — Granüler yetki mimarisi + menü hiyerarşisi (Aşama 0: analiz ve karar)

> **Durum:** 📋 TASLAK — uygulama ONAYI BEKLİYOR · 2026-09-05
> **Kapsam:** Aşama 0 yalnız analiz + karardır. **Bu belge hazırlanırken hiçbir kod, şema veya
> üretim verisi değiştirilmedi.**

---

## 0. Yönetici özeti

Analiz, işin başlangıçta göründüğünden **farklı** olduğunu gösterdi:

1. **Yetki sistemi zannedildiğinden çok daha olgun.** Menü, ekran, aksiyon (4), özel buton (8),
   rapor kalemi (27), şube kapsamı ve rol bazlı kilit katmanlarının hepsi **bugün çalışıyor**.
   "Yetki ekranını yeniden yaz" değil, **eksik iki katmanı eklemek** doğru iş tanımıdır.
2. **Gerçekten eksik olan iki şey var:** *alan (field) bazlı yetki* ve *rol → izin* modeli.
   Diğer istekler ya mevcut ya da mevcut yapının küçük genişletmesi.
3. **Mimari bu genişlemeyi zaten öngörmüş.** `PermissionSnapshot` içinde `ScopeUnitIds` (F4) ve
   `AllowedRecordTypes` (F5) alanları **ayrılmış ve bugün null**. Yani yeni bir yetki sistemi
   kurmaya gerek yok; ayrılan yuvaları doldurmak gerekiyor.
4. **Menü tarafında renk hiyerarşisi hiç yok** — bu istek gerçekten sıfırdan yapılacak tek şey.
5. **En büyük risk alan bazlı yetkidir.** Doğru yapılmazsa "maliyet alanını göremeyen kullanıcı"
   API yanıtında maliyeti görmeye devam eder; yanlış yapılırsa bugün çalışan formlar kırılır.

---

## 1. MEVCUT DURUM — ölçülmüş gerçekler

### 1.1 Katalog büyüklükleri (sayıldı)

| Katalog | Adet | Kaynak |
|---|---|---|
| Modül (yetki birimi) | **60** | `AppModules.All` |
| Ekran | **70** | `AppScreens.All` |
| Üst menü (grup) | **24** | `AppScreens.Groups` |
| Üst grup (section) | **6** | `AppScreens.Sections` |
| Özel buton yetkisi | **8** | `SpecialButtons.All` |
| Rapor yetki kalemi | **27** | `ReportCatalog.All` → `rpt_*` |
| Alan kataloğu | **25 alan / 3 ekran** | `FieldCatalog.All` |
| Migration | **93** (üretim şeması 91) | `MigrationCatalog` |

### 1.2 Veritabanı tabloları

| Tablo | İçerik | Migration |
|---|---|---|
| `roles` | Rol tanımı. `company_id NULL` = sistem rolü | 001 |
| `users` | Kullanıcı; `branch_id` = ana şube | 001 (+004) |
| `user_roles` | Kullanıcı ↔ rol (çoklu) | 001 |
| `user_permissions` | **Kullanıcı × modül → 4 bayrak** (`can_view/create/edit/delete`) | 001 |
| `user_button_permissions` | Kullanıcı × buton anahtarı | 015 |
| `user_scopes` | Kullanıcı × şube (veri kapsamı) | 004 |
| `role_grant_limits` | **Rol × modül → KAPALI** (negatif kilit) | 036 sonrası |
| `field_requirements` | Firma × ekran × alan → zorunlu mu | 087 |
| `audit_logs` | `entity_type/entity_id/action/before_json/after_json` | 001 |

**🔴 Kritik yapısal gerçek: `role_permissions` tablosu YOKTUR.** İzinler **yalnız kullanıcı
seviyesinde** tutulur. Roller iki şey yapar: (a) admin/süper admin gibi **yapısal bypass**,
(b) `role_grant_limits` ile **negatif kilit**. Bir role izin *verilemez*.

### 1.3 `AccessControl` karar mekanizması (sıra ile)

`AccessControl.Can(session, moduleKey, action)` şu sırayla karar verir:

| # | Kural | Sonuç |
|---|---|---|
| 1 | `IsPublic(modül)` | Yalnız `View` serbest, yazma kapalı |
| 2 | `BlockedModules` içeriyor (rol kilidi) | **DENY** — admin bypass'ı bile geçemez (süper admin ve geliştirici modu muaf) |
| 3 | `IsPublicRead` + `View` | ALLOW |
| 4 | `IsSuperAdminOnly` | Süper admin → ALLOW; diğerleri → yalnız **açık izin** |
| 5 | `IsExplicitOnly` | Süper admin → ALLOW; diğerleri → yalnız **açık izin** (admin bypass GEÇERSİZ) |
| 6 | `IsAdmin` (süper admin ∨ firma admini ∨ geliştirici modu) | **ALLOW** (bypass) |
| 7 | Aksi | `user_permissions` satırı — yoksa **DENY** (deny-by-default) |

**Yani precedence bugün fiilen şudur ve YAZILI DEĞİLDİR:**

```
rol kilidi (deny)  >  yapısal modül sınıfı  >  admin bypass  >  açık izin  >  deny-by-default
```

Bu ADR'nin en önemli çıktılarından biri: **bu kuralı yazılı hâle getirmek ve korumak.**

### 1.4 `PermissionSet` ve önbellek

- `PermissionSet` = `Dictionary<moduleKey, ModulePermission>` + `HashSet<buttonKey>`. Değişmez.
- `PermissionSnapshot` = firma + kullanıcı + roller + `PermissionSet` + `CanViewAllBranches` +
  `BlockedModules` + `ScopeBranchIds` + `HomeBranchId` + `BranchDescendants`.
- **Boş bırakılmış yuvalar:** `ScopeUnitIds` (F4/BRM-01) ve `AllowedRecordTypes` (F5/GNL-03) —
  bugün daima `null`, hiçbir yerde okunmuyor. **Yeni katmanların yeri burasıdır.**
- `PermissionSnapshotCache`: süreç içi `ConcurrentDictionary`, anahtar `companyId|userId`,
  **TTL 90 sn**, olumsuz sonuç önbelleğe alınmaz (fail-closed). Yetki yazan her nokta
  `InvalidateUser`, rol kilidi değişimi `InvalidateAll` çağırır → **yetki kaybı gecikmez**.

> **Yetki değişikliği kullanıcıya ne zaman yansır?** Yazma anında invalidate edildiği için
> **hemen**. TTL yalnız üst sınır güvencesidir. Bu davranış korunacaktır.

### 1.5 `BranchAccess` — veri kapsamı

Formül: `ETKİN = İZİNLİ ∩ (İSTENEN ?? OTURUM ?? İZİNLİ)`, fail-closed.

İzinli şube belirleme sırası:
1. `user_scopes` satırları varsa **yalnız onlar** (admin bypass'ı bunu kaldırmaz),
2. `CanViewAllBranches` ∨ admin → sınırsız,
3. `users.branch_id` varsa o şube **+ alt şubeleri** (ŞB-04 ağaç genişletmesi),
4. hiçbiri yoksa → **sınırsız**.

> ⚠️ 4. madde bilinçli bir gevşekliktir: şubesi atanmamış kullanıcıyı kilitlemek bugün çalışan
> kullanıcıları kırardı. **Bu davranış değiştirilmeyecek** (bkz. §7 geriye dönük uyumluluk).

### 1.6 Rapor yetkileri (`rpt_`)

```
CanSee(rapor) = Can(kategoriModülü, View)  ⋁  Can("rpt_" + raporAnahtarı, View)
```

**OR** olması bilinçlidir: kategori bazlı eski atamalar aynen çalışır, ince kontrol isteyen
yönetici kategoriyi kaldırıp kalemleri tek tek verir. `rpt_*` anahtarları `user_permissions`
tablosunda serbest metin olarak durur → **migration gerekmedi**. Liste `ReportCatalog`'dan
üretilir → yeni rapor yetki ağacına kendiliğinden gelir.

### 1.7 API ve servis authorization

- API `Program.cs` içinde **40** doğrudan yetki çağrısı,
- Servis katmanında **329 çağrı / 60 dosya**.
- Asıl kapı **servis katmanındadır** — bilinçli: masaüstü servisleri **çevrimdışı** da çağırır,
  yalnız API'de olsaydı o yol korumasız kalırdı.
- Tenant: `TenantAccessGuard.ResolveCompanyId` payload'daki `company_id`'yi **yok sayar**,
  oturumu esas alır; `EnsureOwnership` kayıt sahipliğini fail-closed doğrular.

### 1.8 Web ve masaüstünün mevcut yetki davranışı

| | Web | Masaüstü |
|---|---|---|
| Modül izni | `AuthState.CanView/CanCreate/...` (sunucudan `/api/me/menu`) | `AccessControl` doğrudan (aynı kod) |
| Buton izni | `AuthState.CanButton` (DEN-F1, 2026-08-18) | `AccessControl.CanUseButton` |
| Şube kapsamı | `BranchAccess` (servis) | `BranchAccess` (aynı) |
| Karar kodu | **Ortak** — `DepoWise.Application.Security` | **Ortak** |

Web'in Application'a **proje referansı yoktur**; ortak dosyalar `<Compile Include>` ile bağlanır
(bilinçli sınır: "web her şeyi API'den alır"). Yeni ortak katman da bu desene uymalıdır.

### 1.9 Yetki ekranının bugünkü hâli

Beklenenden zengin: `PermMatrix` bileşeni (gruplu matris), grup başına **Tümünü Seç/Temizle**,
**Tümünü Temizle**, **şablondan doldur**, **Yetki Özeti**, **Yetkileri Sıfırla**, ayrı
**Şube Kapsamı** bölümü, `role_permissions` (Rol Yetki Kontrol) ekranı.

**Eksikler:** arama yok · modül filtresi yok · indeterminate (kısmi) durum yok ·
değişiklik özeti (diff) yok · alan bazlı yetki yok · sanallaştırma yok.

### 1.10 Menü hiyerarşisi ve renk

- Üç seviye: **Section (6) → Group (24) → Screen (70)**; tek kaynak `AppScreens`,
  sıralama/etiket `MenuLayout` (iki platform aynı kod), simgeler `MenuIcons` (2026-09-05).
- **Renk hiyerarşisi YOK.** Masaüstünde 27 tema token'ı × açık/koyu var ama hiçbiri gruba bağlı
  değil; hiyerarşi yalnız **simge + kalınlık + girinti** ile anlatılıyor. Web'de MudBlazor teması
  tek `primary` renk üzerine kurulu.
- Bu, isteklerin içinde **gerçekten sıfırdan yapılacak tek parçadır**.

---

## 2. RİSKLER (mevcut yapıda tespit edilen)

| # | Risk | Şiddet | Açıklama |
|---|---|---|---|
| R1 | **Precedence yazılı değil** | 🔴 YÜKSEK | Kural yalnız `Can()` içindeki sıradan okunuyor. Yeni katman eklerken sırayı bozmak, canlı kullanıcıların yetkisini **sessizce** değiştirir. |
| R2 | **Rol → izin yok** | 🟠 ORTA | 81 personelli firmada her kullanıcıya tek tek 60 modül × 4 bayrak verilmesi gerekir. Ölçeklenmiyor. |
| R3 | **Alan yetkisi yok** | 🟠 ORTA | Maliyet/fiyat gibi hassas alanlar herkese açık. |
| R4 | **Şubesiz kullanıcı = sınırsız** | 🟠 ORTA | Bilinçli ama gevşek. Sıkılaştırma canlı kullanıcıları kilitleyebilir. |
| R5 | **Yetki ekranında arama/filtre yok** | 🟡 DÜŞÜK | 60 modül + 27 rapor + 8 buton = ~95 satır; yönetilebilir ama alan yetkisi eklenince patlar. |
| R6 | **Yetim izin satırları** | 🟡 DÜŞÜK | Katalogdan çıkarılan anahtarların satırları DB'de kalıyor. Zararsız (deny-by-default) ama kirlilik. |
| R7 | **Rapor izni OR mantığı** | 🟡 DÜŞÜK | Kategori izni tek başına tüm raporları açar. Bilinçli, ama "yalnız 2 rapor ver" isteyen yönetici için kafa karıştırıcı. |

---

## 3. ÖNERİLEN YENİ MİMARİ

### 3.1 Temel karar: YENİ SİSTEM KURULMAZ, KATMAN EKLENİR

Mevcut model (`user_permissions` + `user_button_permissions` + `user_scopes` +
`role_grant_limits`) **korunur**. Üstüne üç yeni katman gelir:

| Katman | Yeni tablo | Yuva |
|---|---|---|
| Rol izinleri | `role_permissions` | yeni |
| Alan yetkisi | `field_permissions` | yeni (`FieldCatalog` genişletilir) |
| Kayıt tipi kapsamı | `user_record_scopes` | `PermissionSnapshot.AllowedRecordTypes` (**hazır**) |

Aksiyon kümesi `View/Create/Edit/Delete` **genişletilmez**. Onaylama, dışa aktarma, iptal gibi
işlemler bugün zaten **özel buton yetkisi** olarak modellenmiş ve çalışıyor; ikinci bir mekanizma
kurmak aynı kavramı iki yere bölerdi. Yeni aksiyon = yeni `SpecialButtons` kalemi.

### 3.2 PRECEDENCE — kesin kural (R1'in cevabı)

Aşağıdaki sıra **yazılı kuraldır**, kodda tek yerde uygulanır ve testle kilitlenir:

```
1. Rol kilidi (role_grant_limits)      → DENY   [süper admin muaf]
2. Yapısal modül sınıfı                 → sınıfa özel kural
   (IsPublic / IsPublicRead / IsSuperAdminOnly / IsExplicitOnly)
3. Admin bypass (süper admin, firma admini, geliştirici modu) → ALLOW
4. Kullanıcı açık izni (user_permissions)  → ALLOW/DENY
5. Rol izni (role_permissions) [YENİ]      → ALLOW
6. Varsayılan                              → DENY
```

**Neden bu sıra — seçenekler değerlendirildi:**

| Seçenek | Karar | Gerekçe |
|---|---|---|
| **Saf "deny wins"** (herhangi bir deny her şeyi ezer) | ❌ | Sektör standardı budur ([AWS](https://docs.aws.amazon.com/en_en/IAM/latest/UserGuide/reference_policies_evaluation-logic_AccessPolicyLanguage_Interplay.html), [Azure](https://learn.microsoft.com/en-us/azure/role-based-access-control/overview)) **ama** bu projede negatif izin yalnız `role_grant_limits`'te var ve o zaten 1. sırada. Genel "deny wins" eklemek, bugün admin bypass'ıyla çalışan yöneticileri kilitlerdi. |
| **Explicit allow wins** (açık izin admin bypass'ını ezer) | ❌ | Bugünkü davranışı ters çevirir; hiçbir kullanıcının yetkisi *artmaz* ama admin davranışı değişir. Sessiz değişim yasağına aykırı. |
| **Kullanıcı > rol (union, kullanıcı üstte)** | ✅ | Seçildi. Rol izni **yalnız ekler**, asla kaldırmaz. |
| **Tenant admin override** | ✅ (mevcut) | Firma admini normal modüllerde tam yetkili; `IsSuperAdminOnly`/`IsExplicitOnly` sınıflarında değil. **Değiştirilmiyor.** |

**🔴 Kilit kural — sessiz değişim yasağı:** rol izinleri **yalnız ALLOW üretebilir** (union).
Rolde izin olmaması hiçbir şeyi kapatmaz. Böylece `role_permissions` tablosu boşken sistemin
davranışı **bugünle bit bit aynıdır**. Yayın günü hiçbir kullanıcının yetkisi değişmez.

### 3.3 Alan (field) yetkisi — üç durum

`Hidden` / `ReadOnly` / `Editable`. Varsayılan **`Editable`** (kayıt yoksa bugünkü davranış).

**Güvenlik kararı (BÖLÜM 23'ün cevabı):** `Hidden` alan API yanıtından **çıkarılır**, yalnız
arayüzde gizlenmez. Uygulama noktası tek olmalı — DTO'ları tek tek elden geçirmek 70 ekranda
kaçınılmaz olarak unutma üretir. Bunun için **merkezi bir çıkış süzgeci** (serileştirme öncesi
alan maskeleme) tasarlanacak; ayrıntısı Faz 7'nin ilk işidir ve ayrıca onaylanacaktır.

`ReadOnly` alan yanıtta **döner** (kullanıcı görebilmeli) ama **yazma yolunda reddedilir** —
istemci alanı gönderse bile sunucu değeri yok sayar, sessizce kabul etmez.

### 3.4 Menü renk/token mimarisi

`MenuIcons` deseninin **birebir aynısı** — kanıtlanmış ve yeni kurulmuş:

```
AppScreens (grup/ekran)  →  MenuPalette (ortak: grup → RENK AİLESİ anahtarı)
                              ├─ Masaüstü: aile → Avalonia fırça (Palette.axaml)
                              └─ Web:      aile → CSS değişkeni / MudBlazor rengi
```

- Renk **ekrana değil GRUBA** bağlanır; ekran rengini **grubundan miras alır** →
  yeni ekran eklendiğinde renk tanımlamak **gerekmez** (BÖLÜM 5'in şartı).
- Hiyerarşi: **Section = en doygun**, **Group = orta**, **Screen = en açık ton** (aynı aile).
- Renk **tek başına anlam taşımaz**: simge + kalınlık + girinti aynı bilgiyi taşır (BÖLÜM 7/51).
- Açık/koyu tema için aile başına iki ton kümesi; kontrast WCAG AA ölçülecek.

### 3.5 Yetki ekranı UX

Araştırma ([Salesforce izin kümeleri](https://help.salesforce.com/s/articleView?language=en_US&id=release-notes.rn_permissions_field_security_perm_set.htm&release=244&type=5),
[izin matrisi desenleri](https://www.shadcn.io/blocks/tables-permission-matrix),
[üç durumlu ağaç](https://fwdtools.com/ui-snippets/checkbox-tree/)) üç şeyi doğruladı:

1. **Matris + detay** ikilisi: satır = ekran, kolon = aksiyon; satıra tıklayınca alan/buton/rapor
   detayı açılır. Mevcut `PermMatrix` bu yapıya zaten yakın → **yeniden yazılmaz, genişletilir**.
2. **Indeterminate (kısmi) durum** zorunlu: "Tümünü Seç" sonrası tek tek kaldırma yapılınca üst
   kutu ne dolu ne boş görünmeli.
3. **Sanallaştırma** ancak satır sayısı yüzleri aşınca gerekir. Bugün ~95 satır; alan yetkisiyle
   ~400'e çıkar → **ölçülecek**, gerekirse eklenecek. Ölçmeden eklenmeyecek.

Eklenecekler: arama (ekran/alan/buton/rapor adı) · modül filtresi · "yalnız yetkili/yetkisiz/
değişmiş" süzgeci · **değişiklik özeti (diff)** · indeterminate.

---

## 4. TEST STRATEJİSİ — ve otomasyon gerçeği

### 4.1 Playwright — DOĞRULANDI (varsayım değil)

| Kontrol | Sonuç |
|---|---|
| `.mcp.json` tanımı | **VAR** (`npx @playwright/mcp@latest`, izole, origin kısıtlı) |
| `settings.local.json` | `disabledMcpjsonServers: ["context7","playwright"]` → **KAPALI** |
| Bu oturumda yüklü araçlar | `mcp__playwright__*` **yok**; `mcp__Claude_Browser__*` **var** |
| `node_modules/@playwright` | **YOK** (kurulu değil; `npx` indirir) |

**Karar: Playwright açılmasına gerek yok.** Gerekçe kapasite değil, **eşdeğerlik**: yerleşik
tarayıcı araçları gerçek bir tarayıcıyı sürüyor — gezinme, tıklama, form doldurma, DOM okuma,
konsol ve **ağ isteklerini görme** dahil. Yetki testi için kritik olan "butonu gördüm ve
API'nin reddettiğini kanıtladım" zinciri bu araçlarla kurulabiliyor.

Playwright'ın gerçek üstünlüğü **CI'da başsız, tekrarlanabilir koşu**dur. Bu proje bugün CI
kullanmıyor; yalnız bunun için ikinci bir tarayıcı yığını kurmak, bakım maliyeti getirir.
İleride CI kurulursa karar yeniden açılmalıdır.

### 4.2 🔴 ÖNEMLİ DÜZELTME — giriş yapamama sorunu ÇÖZÜLDÜ

Önceki turlarda "oturum açmayı gerektiren ekranları göremiyorum, parola yazmıyorum" dedim.
**Analiz bu kısıtın sandığım kadar geniş olmadığını gösterdi** ve bunu düzeltmem gerekiyor.

`docs/tests/Masaustu_GUI_Checklist.md` (2026-08-13) gösteriyor ki proje daha önce **28 maddelik
gerçek GUI testini** koşturmuş: **izole ortamda kendi test kullanıcılarını oluşturup**
(`admin`, `depo1`, `superadmin`) onlarla giriş yapmış.

**Kilit fark:** kullanıcının **gerçek üretim parolasını** yazmıyorum — ama **testin kendi
oluşturduğu** hesabın parolasını kullanmakta hiçbir sakınca yok. Yani:

> Yetki senaryolarının tamamı — web ve masaüstü, gerçek giriş dahil — **izole ortamda uçtan uca
> otomatik test edilebilir.** Sizden parola istememe gerek yok.

Ayrıca `.env.test.local` + `tools/qa/live-sync-check.mjs` deseni mevcut: betik parolayı ortam
dosyasından okur, ben değeri hiç görmem.

### 4.3 Masaüstü UI otomasyonu — DOĞRULANDI

**Kurulu bir çözüm VAR ve daha önce kullanılmış:** Windows'un yerleşik **UI Automation** arayüzü
PowerShell üzerinden Avalonia penceresini sürüyor — **ek paket yok**:

- okuma: `AutomationElement.FindAll`
- yazma: `ValuePattern.SetValue`
- tıklama: `InvokePattern.Invoke` + gerçek `mouse_event`
- ekran görüntüsü: `Graphics.CopyFromScreen`

İzolasyon deseni de belgeli: `DEPOWISE_ENVIRONMENT=GuiTest` (ayrı SQLite), yerel API
`127.0.0.1:5099`, ortak önbellek dosyaları yedeklenip md5 ile geri yükleniyor.

`FlaUI`/`Appium`/`WinAppDriver` **kurulu değil** ve **gerekmiyor** — yerleşik UIA aynı işi
bağımlılıksız yapıyor. `Avalonia.Headless` bir seçenek olarak durur (birim seviyesinde hızlı
UI testi) ama gerçek kullanıcı davranışını UIA kadar temsil etmez.

### 4.4 Test katmanları

| Katman | Araç | Kapsam |
|---|---|---|
| Birim | xUnit | Precedence tablosu, `PermissionSet`, alan kararı |
| Entegrasyon | `ApiTestHost` (gerçek HTTP + JWT) | Her uç için yetkili/yetkisiz, IDOR, tenant |
| Veritabanı | SQLite + izole Neon dalı | Migration, eski→yeni eşleme, geri alma |
| Web E2E | Yerleşik tarayıcı + izole yerel sunucu | Gerçek giriş, menü, buton, alan, rapor |
| Masaüstü E2E | PowerShell UIA + `GuiTest` ortamı | Aynı senaryolar |
| Performans | Ölçüm testi (mevcut `BuyukVeriOlcumTests` deseni) | 10.000+ kayıt, izin hesabı satır başına OLMAMALI |

---

## 5. MIGRATION STRATEJİSİ

| Kural | Uygulama |
|---|---|
| Yalnız **ekleme** | Yeni tablolar; mevcut tabloların hiçbir kolonu değişmez/silinmez |
| Boş tablo = eski davranış | `role_permissions` ve `field_permissions` boşken sistem **bugünle birebir aynı** |
| Veri taşıma **YOK** | Mevcut `user_permissions` satırları olduğu yerde kalır, dönüştürülmez |
| İki lehçe | SQLite + PostgreSQL ayrı ayrı test edilir |
| Doğrulama | İzole Neon dalında koşulur; **üretime uygulanmaz** |
| Yedek | Her aşamada `pg_dump` |

**Geriye dönük uyumluluk kanıtı:** "eski izin → yeni izin" eşlemesi *gerekmiyor*, çünkü eski model
**aynen çalışmaya devam ediyor**. Yeni katmanlar **yalnız ek yetki** verir. Bu, "kullanıcıların
yanlışlıkla fazla yetki kazanması" riskini de karşılar: yeni tablolar **boş doğar**.

---

## 6. FAZLARA AYRILMIŞ UYGULAMA PLANI

| Faz | İçerik | Migration | Risk |
|---|---|---|---|
| **1** | Precedence'ı yazılı hâle getir + **mevcut davranışı kilitleyen test seti** (kod değişikliği yok, yalnız test) | ✖ | 🟢 |
| **2** | Menü renk/token mimarisi (`MenuPalette`) — iki platform | ✖ | 🟢 |
| **3** | `role_permissions` + snapshot'a union | ✔ | 🟡 |
| **4** | Yetki ekranı UX: arama · filtre · indeterminate · diff özeti | ✖ | 🟢 |
| **5** | Kayıt kapsamı (`AllowedRecordTypes` yuvası) | ✔ | 🟡 |
| **6** | `FieldCatalog` genişletme (3 → hedef ekranlar) | ✖ | 🟢 |
| **7** | Alan yetkisi: model + **merkezi çıkış süzgeci** + iki platform | ✔ | 🔴 |
| **8** | Rapor yetkisi ince ayar (R7) | ✖ | 🟡 |
| **9** | E2E + performans + tam regresyon | ✖ | 🟢 |

**Faz 1 bilinçli olarak testtir.** Mevcut davranışı kilitlemeden altına katman eklemek, sessiz
yetki değişimi riskinin ta kendisidir. Önce bugünkü doğruyu yazılı ve otomatik doğrulanır hâle
getiriyoruz; sonra üstüne inşa ediyoruz.

**Faz 7 (alan yetkisi) tek başına en riskli iştir** ve ayrıca onaylanmalıdır.

---

## 7. AÇIK KARARLAR — sizin onayınız gerekiyor

| # | Soru | Önerim |
|---|---|---|
| K1 | Rol izinleri **yalnız ekleyebilsin** mi (union), yoksa rolde deny de olsun mu? | **Yalnız union.** Deny eklemek precedence'ı ikiye böler ve sessiz kayıp riski doğurur. |
| K2 | `Hidden` alan API'den tamamen çıkarılsın mı, yoksa maskelensin mi (`null`)? | **Çıkarılsın.** Maskelenmiş `null`, "değer yok" ile "görmüyorsun"u karıştırır. |
| K3 | Şubesiz kullanıcı "sınırsız" davranışı (R4) sıkılaştırılsın mı? | **Hayır, bu turda değil.** Canlı kullanıcıları kilitleyebilir; ayrı iş olarak ele alınmalı. |
| K4 | Rapor izni OR mantığı (R7) korunsun mu? | **Korunsun.** Değiştirmek bugün rapor gören kullanıcıların bir kısmını kör eder. |
| K5 | Faz sırası bu mu? | Faz 1–2 hemen başlanabilir; 3 ve 7 ayrı onay. |

---

## 8. BÖLÜM 57 kontrol listesi — Aşama 0 durumu

Aşama 0 analiz aşamasıdır; aşağıdakiler **henüz uygulanmadı**, planlandı:
menü hiyerarşisi görselliği · grup renkleri · renk mirası · yetki ekranı yeniden tasarımı ·
alan/rapor/kapsam yetkileri · tümünü seç/indeterminate.

**Aşama 0'da doğrulanmış olanlar:**

- [x] Mevcut mimari uçtan uca çıkarıldı (tablolar, karar mekanizması, önbellek, kapsam, API)
- [x] Precedence kuralı **ölçülerek** yazıldı (varsayım yok)
- [x] Geriye dönük uyumluluk stratejisi: yeni tablolar boş doğar → **davranış değişmez**
- [x] Playwright durumu **dosyadan doğrulandı** (tanımlı, kapalı, kurulu değil)
- [x] Masaüstü UI otomasyonu **bulundu** (yerleşik UIA + `GuiTest` izolasyonu, daha önce kullanılmış)
- [x] Giriş yapma kısıtı **çözüldü** (izole ortamda test kullanıcısı oluşturma deseni)
- [x] **Üretime dokunulmadı, kod değiştirilmedi, commit/push yapılmadı**
