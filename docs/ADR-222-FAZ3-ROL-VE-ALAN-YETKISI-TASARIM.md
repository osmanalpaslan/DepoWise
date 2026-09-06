# ADR-222 — FAZ 3 tasarımı: rol bazlı ve alan bazlı yetki

> **Durum:** 📋 TASARIM — uygulama ONAYI BEKLİYOR · 2026-09-05
> **Bu turda kod yazılmadı, migration çalıştırılmadı, commit/push yapılmadı, üretime dokunulmadı.**
> Önkoşul: [ADR-221](ADR-221-YETKI-VE-MENU-MIMARISI.md) · Faz 1 mühürleri (`YetkiSirasiTests`, `YetkiZinciriTests`)

---

## A. MEVCUT MİMARİ — yeniden ölçüldü

### A.1 Faz 1 sayıları DOĞRULANDI (hepsi değişmemiş)

| Ölçüm | Faz 1 | Bugün |
|---|---|---|
| Modül | 60 | **60** ✅ |
| Ekran | 70 | **70** ✅ |
| Üst menü | 24 | **24** ✅ |
| Rapor | 27 | **27** ✅ |
| Özel buton | 8 | **8** ✅ |
| Servis yetki çağrısı | 329 / 60 dosya | **329 / 60** ✅ |
| Alan kataloğu | 25 alan / 3 ekran | **25 / 3** ✅ |

### A.2 🔴 EN ÖNEMLİ BULGU — merkezi DTO/mapping katmanı YOK

| Ölçüm | Değer |
|---|---|
| `Results.Ok(new { … })` — satır içi **anonim** yanıt | **256** |
| `Select(x => new { … })` — liste içi anonim şekillendirme | **38** |
| `record …Dto(…)` tanımı | 126 — **hepsi GİRDİ** (`Results.Ok(...Dto)` = **0**) |
| `Mappers/` · `Dtos/` klasörü | **yok** |

Yani **her uç kendi yanıt şeklini yerinde kuruyor**. "70 DTO'yu tek tek elden geçirme" uyarınız
yerinde ama sayı daha büyük: **256 ayrı şekillendirme noktası**.

### A.3 🔴 Masaüstü API'yi İŞ VERİSİ İÇİN KULLANMIYOR

Masaüstü ekranları servisleri **doğrudan** çağırır (`DesktopServices.Materials.SearchGrid(...)`) ve
dönen **domain kayıtlarını** (`MaterialRow`, `MaterialDetail`) kullanır. API yalnız senkron,
kimlik ve güncelleme için kullanılır.

**Sonuç:** API yanıtını süzmek masaüstünü **hiç korumaz**. Web ve masaüstü aynı DTO'yu paylaşmıyor —
paylaştıkları şey **servis katmanı**dır.

### A.4 🔴 DÖRDÜNCÜ KANAL: senkron ham satır gönderiyor

`BusinessSyncService.BuildSnapshot` **38 tabloyu** satır satır, **tüm kolonlarıyla**
(`Dictionary<string, object?>`) gönderir. Şube süzgeci var (GAP-6), **alan süzgeci yok**.
Senkron edilen tablolar arasında `materials` var ve `materials.unit_price` — yani klasik "maliyet"
alanı — **her cihaza tam olarak iniyor**.

### A.5 🔴 Masaüstü yerel veritabanı KULLANICI BAZLI DEĞİL

`AppPaths.DatabasePath` → `%LOCALAPPDATA%\Alpnex\Data\<ortam>\alpnex.db`

Yol **Windows kullanıcısına ve ortama** göredir; **Alpnex kullanıcısına göre değildir**. Aynı
Windows hesabında iki Alpnex kullanıcısı aynı yerel veritabanını paylaşır.

**Sonuç:** "alanı senkrondan çıkar" çözümü masaüstünde **çalışmaz**. Maliyeti göremeyen A
senkronlarsa kolon inmez; sonra aynı makinede maliyeti görebilen B giriş yaptığında veri
**yok** olur. Çevrimdışı çalışma bundan doğrudan zarar görür.

### A.6 🟢 İYİ HABER: dışa aktarım ve raporun TEK kapısı var

| Kanal | Kapı |
|---|---|
| Excel dışa aktarım | `ExcelExportService.Export(TableModel)` — **tek metot** |
| Raporlar | Hepsi `TableModel` döner |
| Dışa aktarım ucu | 11 uç, hepsi `ToTableModel(...)` → `Excel.Export(...)` (11 üretici) |

`TableModel(Title, Headers, Rows, …)` — **başlıklar metin, satırlar konumsal**. Alan kimliği
taşımıyor; süzmek için "başlık → alan anahtarı" eşlemesi gerekir. Ama **kapı tektir**.

### A.7 ⭐ BEKLENMEDİK BULGU — kayıt tipi yetkisi ZATEN VAR

`AllowedRecordTypes` yuvasının boş olmasını Faz 0'da "kayıt tipi yetkisi yok" diye yorumlamıştım.
**Yanlıştı.** Kayıt tipi yetkisi **çalışıyor** — ama o yuvayla değil, farklı bir desenle:

```
DailyActivityTypeGate → "datype_" öneki → user_permissions'ta serbest metin anahtar
ReportCatalog        → "rpt_"    öneki → user_permissions'ta serbest metin anahtar
```

Her ikisi de **migration GEREKTİRMEDİ** ve her ikisi de **geçiş güvenli** kuralı kullanıyor:
*hiç anahtar verilmemişse hepsi görünür; ilk anahtar verildiği anda yalnız verilenler görünür.*

Bu, Faz 3'ün en önemli girdisidir: **projenin kendi kanıtlanmış deseni budur.**

### A.8 Yetki tabloları ve karar zinciri (Faz 1'den, değişmedi)

`roles` · `users` · `user_roles` · `user_permissions` (modül × 4 bayrak) ·
`user_button_permissions` · `user_scopes` · `role_grant_limits` (rol × modül **kapatma**) ·
`field_requirements` (firma × ekran × alan → zorunlu mu) · `audit_logs`

**`role_permissions` tablosu YOK.** Roller yalnız (a) yapısal bypass, (b) negatif kilit sağlar.

Önbellek: `PermissionSnapshotCache`, anahtar `companyId|userId`, **TTL 90 sn**, yazan her nokta
`InvalidateUser`/`InvalidateAll` çağırır → yetki kaybı **gecikmez**.

---

## B. EKSİK KATMANLAR

| # | Katman | Durum |
|---|---|---|
| B1 | Rol → izin | **YOK** (yalnız rol kilidi var) |
| B2 | Alan bazlı yetki | **YOK** (alan kataloğu var: 25 alan / 3 ekran) |
| B3 | Kayıt tipi | ✅ **VAR** (`datype_`) — yuva boş ama işlev çalışıyor |
| B4 | Veri kapsamı | ✅ **VAR** (şube; `user_scopes` + `BranchAccess`) |
| B5 | Birim/organizasyon kapsamı | YOK (`ScopeUnitIds` yuvası boş) — **domain'de karşılığı yok** |
| B6 | Rapor | ✅ VAR (`rpt_`) |
| B7 | Aksiyon/buton | ✅ VAR (8 özel buton) |

---

## C. ÖNERİLEN VERİ MODELİ

### C.1 Rol izinleri — TEK yeni tablo

```sql
CREATE TABLE role_permissions (
    id          TEXT PRIMARY KEY,
    company_id  TEXT NOT NULL,
    role_id     TEXT NOT NULL,
    module_key  TEXT NOT NULL,      -- "materials" · "rpt_stock" · "datype_x" · "fld_..." (serbest metin)
    can_view    BIGINT NOT NULL DEFAULT 0,
    can_create  BIGINT NOT NULL DEFAULT 0,
    can_edit    BIGINT NOT NULL DEFAULT 0,
    can_delete  BIGINT NOT NULL DEFAULT 0,
    created_at  BIGINT NOT NULL,
    updated_at  BIGINT NOT NULL,
    version     BIGINT NOT NULL DEFAULT 1
);
CREATE UNIQUE INDEX ux_role_permissions ON role_permissions(role_id, module_key);
```

`user_permissions`'ın **birebir aynısı**, `user_id` yerine `role_id`. Böylece:
aynı okuma kodu · aynı birleştirme mantığı · aynı yetki ağacı · `rpt_`/`datype_`/`fld_`
önekleri **kendiliğinden** rol seviyesinde de çalışır.

**Boş tablo = bugünkü davranış, bit bit.** (K1)

### C.2 Alan yetkisi — 🔴 MIGRATION GEREKMEZ

Alan izinleri **ayrı tablo istemez**; mevcut `user_permissions`/`role_permissions` satırı olarak
`fld_` önekiyle yazılır — `rpt_` ve `datype_` ile **aynı kanıtlanmış desen** (A.7):

```
anahtar : "fld_<ekran>_<alan>"      örn. fld_materials_unit_price
can_view: alan GÖRÜNÜR mü
can_edit: alan DÜZENLENEBİLİR mi
```

Dört durum ikiden türer — ayrı bir enum gerekmez:

| can_view | can_edit | Anlam |
|---|---|---|
| 0 | – | **Hidden** — yanıttan ÇIKARILIR |
| 1 | 0 | **Read-only** — döner, yazma reddedilir |
| 1 | 1 | **Editable** |

`can_create`/`can_delete` alan seviyesinde **kullanılmaz** (0 kalır) — domain'de karşılığı yok,
gereksiz karmaşıklık eklemem.

### C.3 🔴 ÇÖZÜLMESİ GEREKEN ÇEKİRDEK ÇELİŞKİ — ve çözümü

**Çelişki:** "Hidden" özünde bir **DENY**'dir. K1 rol katmanını **yalnız ALLOW** yaptı ve geriye
dönük uyumluluk "yayın günü kimse bir şey kaybetmesin" diyor → varsayılan **görünür** olmalı.
Varsayılan görünürken, yalnız-ALLOW bir modelle bir alan **gizlenemez**.

**Çözüm — iki seviyeli model (deny icat ETMEDEN):**

```
1) FİRMA SEVİYESİ — "korumalı alan" listesi   (yeni tablo: field_protections)
   Bir alan burada YOKSA  → herkese görünür/düzenlenebilir  = BUGÜNKÜ DAVRANIŞ
   Bir alan buraya EKLENİRSE → o alan artık deny-by-default olur

2) KULLANICI/ROL SEVİYESİ — korumalı alanlar için ALLOW verilir  (fld_ anahtarları)
```

```sql
CREATE TABLE field_protections (
    id         TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    screen_key TEXT NOT NULL,
    field_key  TEXT NOT NULL,
    created_at BIGINT NOT NULL,
    UNIQUE(company_id, screen_key, field_key)
);
```

`field_requirements` (087) ile **aynı şekil ve aynı felsefe** — firma bazlı, opt-in, boşken
davranış değişmez.

**Neden bu bir DENY değil:** kısıtlama kararı **yetki katmanında değil, firma yapılandırmasında**
durur. Yetki katmanı yalnız ALLOW üretmeye devam eder (K1 korunur) ve Faz 1 precedence'i
(K5) hiç değişmez. Deny gerçekten gerektiğinde ileride 1½. katman olarak eklenebilir —
mimari buna kapalı değildir.

**Yayın günü etkisi: SIFIR.** `field_protections` boş doğar → hiçbir alan korumalı değildir →
hiçbir kullanıcı hiçbir şey kaybetmez.

---

## D. YETKİ ÇÖZÜMLEME ALGORİTMASI

Faz 1'de mühürlenen sıra **değişmez**; rol yalnız 4. basamağı besler:

```
1. Rol kilidi (role_grant_limits)            → DENY   [süper admin muaf]
2. Yapısal modül sınıfı                       → sınıfa özel
3. Admin bypass                               → ALLOW
4. AÇIK İZİN =  user_permissions ∪ role_permissions      ← YENİ: birleşim
5. Varsayılan                                 → DENY
```

**Birleştirme kuralı (bayrak bazlı OR):**

```
etkin.CanView   = kullanıcı.CanView   ∨ rol₁.CanView   ∨ rol₂.CanView …
etkin.CanCreate = kullanıcı.CanCreate ∨ rol₁.CanCreate ∨ …
```

- Aynı izin iki rolden gelirse: OR → tek sonuç, çakışma yok.
- Kullanıcı izni ile rol izni çakışırsa: OR → **kullanıcı hiçbir şey kaybetmez** (K1).
- `role_permissions` boşken 4. basamak bugünküyle **birebir aynı** değeri üretir.

`Explicit()` metodunun tek satırı değişir: `s.Permissions.For(key)` yerine önceden birleştirilmiş
küme okunur. **Birleştirme oturum kurulurken bir kez yapılır** (D.2), her çağrıda değil.

### D.2 Snapshot'a yansıma ve önbellek

`PermissionSnapshot.Permissions` **zaten** birleştirilmiş kümeyi taşıyabilir — mevcut `PermissionSet`
tipi değişmez. `AuthService` snapshot kurarken kullanıcı satırlarını ve rol satırlarını okuyup
birleştirir: **istek başına ek sorgu yok**, snapshot başına +1 sorgu.

**Ne zaman etkili olur:** yetki yazan nokta `InvalidateUser` çağırır → **anında**.
Rol izni değişince o role sahip **herkes** etkilenir → `InvalidateAll` (rol kilidi bugün zaten
böyle yapıyor). **Yeniden giriş GEREKMEZ.** TTL 90 sn yalnız üst sınırdır.

---

## E. ALAN SÜZME MİMARİSİ — 20 sorunun cevabı

| # | Soru | Cevap (gerçek kod) |
|---|---|---|
| 1 | Merkezi süzme nerede? | **Tek bir yer YOK.** Dört ayrı kanal var: servis · API (256 nokta) · export/rapor (`TableModel`) · senkron (`BuildSnapshot`) |
| 2 | DTO → yanıt dönüşümü nerede? | Uçta, satır içi anonim nesnede (256 yer). Mapping katmanı yok |
| 3 | Web ve masaüstü aynı DTO'yu mu kullanır? | **HAYIR.** Masaüstü servisleri doğrudan çağırır, domain kaydı alır. Ortak olan **servis katmanıdır** |
| 4 | Masaüstü çevrimdışı aynı güvenliği alabilir mi? | **Kısmen.** Servis katmanında evet; ama yerel DB'de veri zaten durur (A.5) |
| 5 | İç içe nesnelerde? | Servis katmanında kaynağında süzülürse otomatik; anonim nesnede değil |
| 6 | Koleksiyon içindeki iç içe nesnelerde? | Aynı — kaynakta süzme şart |
| 7 | Dışa aktarımda? | `TableModel` **tek kapı** → kolon indeksiyle düşürülebilir |
| 8 | Raporda? | Aynı kapı |
| 9 | Excel/CSV/PDF? | Hepsi `ExcelExportService.Export(TableModel)`'den geçer |
| 10 | Serileştirme öncesi mi sonrası mı? | **Öncesi** — sonrası JSON string'i yeniden ayrıştırmak demektir, kırılgan ve pahalı |
| 11 | Entity seviyesinde süzmek güvenli mi? | **En güvenlisi bu**: kaynakta olmayan veri hiçbir kanala giremez |
| 12 | Reflection tabanlı merkezi süzme kabul edilebilir mi? | Anonim nesnede **çalışmaz** (her uç ayrı tip). Domain kaydında çalışır ama gereksiz |
| 13 | Expression/derlenmiş erişimci gerekir mi? | **Hayır** — kaynakta süzmede erişimciye gerek yok |
| 14 | 10.000+ kayıtta maliyet? | Kaynakta süzme: **sıfıra yakın** (SQL'de kolon seçilmez). Satır başına reflection: 10.000 × alan sayısı → ölçülmeli, muhtemelen kabul edilemez |
| 15 | Alan izni önbelleği? | `PermissionSnapshot` içinde — **ek altyapı yok** |
| 16 | Geçersiz kılma? | Mevcut `InvalidateUser`/`InvalidateAll` |
| 17 | Eski önbellekle sızıntı olur mu? | Yazan nokta invalidate ettiği için **hayır**; en kötü 90 sn (mevcut kabul edilmiş risk) |
| 18 | Detay ucu doğrudan çağrılırsa? | Bugün **evet, gizli alan döner** — süzme yoksa |
| 19 | Dışa aktarım ucu doğrudan çağrılırsa? | Bugün **evet** — `TableModel` süzülmediği sürece |
| 20 | UI'dan gizlenen alan HTTP'den alınabilir mi? | **EVET, bugün alınabilir.** UI gizleme güvenlik değildir — teyit edildi |

### E.1 Önerilen mimari: **kaynakta süzme (servis katmanı)**

```
SessionContext → AllowedFields(screenKey)   [snapshot'tan, sorgusuz]
        ↓
Servis okuma metodu SELECT'i buna göre kurar / alanı null bırakır
        ↓
├─ Web  : API anonim nesnesi zaten boş alanı taşır → JSON'da OMIT (JsonIgnoreCondition)
├─ Masaüstü : domain kaydı zaten boş → ekranda yok
├─ Export/Rapor : TableModel kolonu düşürülür (tek kapı)
└─ Senkron : ⚠️ AYRI KARAR — bkz. G
```

**Neden bu:** 256 anonim noktaya dokunmadan, **60 dosyadaki servis okuma metotlarında** çözülür.
Zaten orada 329 yetki çağrısı var — desen kurulu.

**Yanıttan OMIT etme:** `.NET`'te `JsonIgnoreCondition.WhenWritingNull` global olarak açılırsa
**tüm** null alanlar kaybolur → mevcut istemcileri kırar. Bu yüzden **alan bazlı**: gizli alan
anonim nesneye **hiç konmaz** (uçta koşullu şekillendirme) ya da servis `null` döner + o uçta
`WhenWritingNull` yerel olarak uygulanır. **Bu, 256 noktanın bir alt kümesini etkiler** — yalnız
korumalı alan taşıyan uçlar.

---

## F. ROL BİRLEŞTİRME — cevaplar

| Soru | Cevap |
|---|---|
| Rol → izin ilişkisi | `role_permissions` (C.1), `user_permissions`'ın aynası |
| Kullanıcı birden çok role sahip olabilir mi? | **Evet** — `user_roles` çoklu; bugün de öyle |
| Birleştirme | Bayrak bazlı **OR** (union) |
| Kullanıcı izni + rol izni | **OR** — kullanıcı hiçbir şey kaybetmez |
| Aynı izin iki rolden | OR → çakışma kavramı yok |
| Önbellek | Mevcut `PermissionSnapshotCache`, ek yapı yok |
| Snapshot | `PermissionSet` birleştirilmiş gelir; tip değişmez |
| Audit | **Evet** — mevcut `audit_logs`, `entity_type="role_permission"` |
| Ne zaman etkili | **Anında** (invalidate) |
| Yeniden giriş gerekir mi? | **Hayır** |

---

## G. KAPSAM MODELİ

| Yuva | Karar |
|---|---|
| `ScopeBranchIds` | ✅ Kullanımda — **dokunulmaz** |
| `AllowedRecordTypes` | **Boş kalsın.** Kayıt tipi yetkisi `datype_` ile zaten çalışıyor (A.7); ikinci bir yol açmak aynı kavramı ikiye böler. Yuva ileride gerçek bir ihtiyaç doğarsa kullanılır |
| `ScopeUnitIds` | **Boş kalsın.** Alpnex'te şube dışında bir organizasyon birimi YOK; uydurma kapsam eklemem |
| "Yalnız kendi kayıtları" | Domain'de karşılığı zayıf — **bu fazda kapsam dışı**, ayrıca değerlendirilmeli |

**K3 gereği** şubesiz kullanıcı davranışı **değiştirilmez**.

### G.1 🔴 SENKRON — ayrı ve zor karar

Alan gizleme senkronda **uygulanamaz** (A.4 + A.5): yerel DB Alpnex kullanıcısına göre değil.
Üç seçenek var, üçü de bedelli:

| Seçenek | Bedel |
|---|---|
| (a) Senkronu süzme — alan cihazda kalır | Alan güvenliği **yalnız arayüz/API'de** olur; cihaza fiziksel erişimi olan görebilir |
| (b) Senkronu süz | Aynı makinedeki diğer kullanıcı **veri kaybeder**; çevrimdışı bozulur |
| (c) Yerel DB'yi Alpnex kullanıcısına göre ayır | Büyük değişiklik; her kullanıcı için ayrı tam senkron (disk + ağ maliyeti) |

**Önerim: (a)** — ve bunu ADR'de **açık bir sınır** olarak yazmak. Gerekçe: tehdit modeli
"aynı firmanın çalışanı, kendi cihazında, uygulama üzerinden" iken (a) yeterlidir. "Cihazına
fiziksel erişimi olan kötü niyetli kullanıcı" tehdidi ayrı ve çok daha büyük bir iştir.
**Bu kararı sizin onaylamanız gerekir.**

---

## H. UI/UX MİMARİSİ

Mevcut `PermMatrix` **yeniden yazılmaz, genişletilir** (bugün zaten gruplu matris + grup başına
tümünü seç/temizle + şablon + özet + şube kapsamı var).

```
Sol: modül/grup ağacı + arama + filtre (yetkili / yetkisiz / değişmiş)
Orta: MATRİS — satır = ekran, kolon = o ekranın GERÇEK aksiyonları (dinamik)
Satıra tıkla → detay sekmeleri: [Alanlar] [Butonlar] [Raporlar] [Kapsam]
Alt: DEĞİŞİKLİK ÖZETİ (eklenecek / kaldırılacak) → Kaydet
```

- **Kolonlar dinamik**: ekranın gerçekten desteklediği aksiyonlar gösterilir (rapor ekranına
  "Sil" konmaz). Kaynak: `AppScreens` + `SpecialButtons` + `ReportCatalog` + `FieldCatalog`.
- **Her seviyede** tümünü seç / temizle / **indeterminate**.
- **Rol sekmesi**: aynı matris, hedef kullanıcı yerine rol.
- **Delegasyon tavanı** rol düzenlemede de uygulanır: `AccessControl.GrantCeiling` **zaten var**
  ve `Can()` ile aynı kuralları kullanıyor → rol için de aynısı çağrılır (§11 şartınız).

---

## I. BACKEND GÜVENLİĞİ

Faz 1'in `YetkiZinciriTests` deseni genişletilir: her yeni izin türü için **dört halka**
(UI kararı · gerçek HTTP · servis · veri) + **403/401 şartı** (500 red sayılmaz).
**ZN8 korunur**: dışa aktarımda genel `export` + veri modülü izni **ikisi de** gerekir.

---

## J. ÖNBELLEK / PERFORMANS

| Nokta | Değerlendirme |
|---|---|
| Rol birleştirme | Snapshot başına +1 sorgu; istek başına **0** |
| Alan izni okuma | Snapshot'tan sözlük araması — **O(1)** |
| Kaynakta süzme | SQL kolonu seçilmez → **negatif maliyet** (daha az veri) |
| Satır başına reflection | **Önerilmiyor**; 10.000 × alan sayısı — ölçülmeden kabul edilemez |
| Yetki ekranı satır sayısı | Bugün ~95 → alan izinleriyle **~400–800**; sanallaştırma **ölçülüp** gerekirse eklenir |

---

## K. MIGRATION PLANI

| Migration | İçerik | Risk |
|---|---|---|
| M1 | `role_permissions` tablosu (yalnız CREATE) | 🟢 Boş doğar → davranış değişmez |
| M2 | `field_protections` tablosu (yalnız CREATE) | 🟢 Boş doğar → davranış değişmez |
| — | Alan izinleri | **Migration YOK** — `fld_` önekiyle mevcut tabloya yazılır |

Veri taşıma **yok**. Geri alma: tabloyu bırakmak yeterli (kod boş tabloyla bugünkü gibi çalışır).
İki lehçe (SQLite + PostgreSQL) ayrı test edilir. **Üretim migration'ı bu fazda YAPILMAZ.**

---

## L. TEST PLANI

**Birim:** rol birleşimi (OR) · precedence bozulmadı (Faz 1 tabloları **aynen** geçmeli) ·
alan görünürlük/düzenlenebilirlik · korumasız alan = bugünkü davranış · delegasyon tavanı ·
indeterminate hesabı · önbellek geçersiz kılma.

**Entegrasyon:** migration iki lehçede · boş tablo = bugünkü davranış (**kritik**) ·
`ApiTestHost` ile gerçek HTTP + JWT · yanıt gövdesinde gizli alan **yok**.

**Güvenlik (her izin için ALLOW + DENY + doğrudan uç):** UI gizli/API açık · API gizli/servis açık ·
servis atlama · doğrudan ID (IDOR) · yetkisiz dışa aktarım · yetkisiz alan.

**Web E2E:** izole ortam + kendi test kullanıcım (Faz 2'de kanıtlandı) — giriş, rol değişimi,
menü/buton/alan görünürlüğü, salt-okunur, çıkış/giriş.

**Masaüstü E2E:** Windows UI Automation (Faz 2'de kullanıldı) — **Faz 2'nin görsel doğrulama borcu
burada kapatılır**.

**10.000+ kayıt:** liste · detay · alan süzme · dışa aktarım · rapor (mevcut `BuyukVeriOlcumTests` deseni).

---

## M. RİSK MATRİSİ

| # | Risk | Seviye | Azaltma |
|---|---|---|---|
| R1 | Senkron alanı cihaza indiriyor (A.4/A.5) | 🔴 **HIGH** | Sınır olarak kabul et (G.1-a) — **onayınız gerekli** |
| R2 | 256 anonim yanıt noktası | 🔴 **HIGH** | Kaynakta süz → yalnız korumalı alan taşıyan uçlara dokun |
| R3 | Alan gizleme özünde DENY (C.3) | 🔴 **HIGH** | İki seviyeli model: firma opt-in + ALLOW |
| R4 | Ekran özelinde KISITLAMA union-only ile imkânsız | 🔴 **HIGH** | Bkz. §18 S8/S9 — **tasarım çatışması, açıkça raporlanıyor** |
| R5 | Yetki ekranı satır sayısı patlaması | 🟡 MEDIUM | Ölç, gerekirse sanallaştır |
| R6 | Rol izni yanlış birleşirse yetki **artar** | 🟡 MEDIUM | Faz 1 tabloları + "boş tablo = aynı davranış" testi |
| R7 | `TableModel` alan kimliği taşımıyor | 🟡 MEDIUM | Başlık → alan anahtarı eşlemesi + test |
| R8 | Alan kataloğu 3/70 ekranı kapsıyor | 🟡 MEDIUM | Kapsam kademeli büyütülür; kapsanmayan ekran = bugünkü davranış |
| R9 | Rol izni değişince `InvalidateAll` | 🟢 LOW | Zaten rol kilidinde uygulanıyor |
| R10 | İki migration | 🟢 LOW | Yalnız CREATE, boş doğar |

---

## §18 — SORULARINIZA NET CEVAPLAR

**1. Mevcut sistemi tamamen değiştirmeden rol izni eklemek mümkün mü?**
**EVET.** Tek yeni tablo + `Explicit()` içinde tek satırlık okuma değişikliği. Boş tabloyla
davranış bit bit aynı.

**2. `PermissionSnapshot` merkez olarak kullanılabilir mi?**
**EVET.** Zaten merkez; `PermissionSet` tipi bile değişmeden birleştirilmiş küme taşıyabilir.

**3. Alan süzme merkezi yapılabilir mi?**
**KISMEN.** Tek bir merkez yok: dört kanal var. **Servis katmanı** üçünü (web/masaüstü/rapor)
birden korur; senkron dördüncüdür ve ayrı karar ister (S7/R1).

**4. Web + masaüstü aynı alan modelini kullanabilir mi?**
**EVET** — ama DTO'da değil, **servis katmanında**. İkisinin ortak noktası orası.

**5. Yanıttan alanı OMIT etmek güvenli yapılabilir mi?**
**EVET**, ama global `WhenWritingNull` ile **DEĞİL** (mevcut istemcileri kırar). Alanı anonim
nesneye hiç koymamak gerekir → yalnız korumalı alan taşıyan uçlara dokunulur.

**6. Export/rapor/iç içe DTO korunabilir mi?**
**EVET.** Export ve rapor **tek kapıdan** geçiyor (`TableModel` → `ExcelExportService.Export`).
İç içe nesneler kaynakta süzülünce otomatik korunur.

**7. `ScopeUnitIds` ve `AllowedRecordTypes` nasıl kullanılmalı?**
**KULLANILMAMALI — boş kalmalı.** Kayıt tipi yetkisi `datype_` ile zaten çalışıyor; birim kapsamı
domain'de yok. İkinci bir yol açmak aynı kavramı ikiye böler.

**8. Global + ekran-özelinde model union-only K1 ile mümkün mü?**
**KISMEN — ve burada gerçek bir tasarım çatışması var.**
- ✅ "Dar ver, geniş verme" **çalışır**: global vermeyip ekran bazında vermek union-only'de sorunsuz.
- ❌ "Geniş ver, bir ekranda kıs" (*Global: Material=Edit, Ekran: Material Detail=Read-only*)
  **ÇALIŞMAZ** — bu bir DENY'dir ve K1 bu fazda deny'i yasaklıyor.

**9. Mümkün değilse tasarım nerede çatışıyor?**
Tam olarak şurada: **ekran özelinde kısıtlama = deny**. Sessizce bir precedence icat etmedim.
Üç seçenek var, kararı size bırakıyorum:
- **(a)** Bu fazda yalnız "dar ver" desteklensin; "geniş ver + kıs" **desteklenmez** ve arayüzde
  böyle bir seçenek **gösterilmez** *(önerim)*.
- **(b)** K1 gevşetilip ekran seviyesinde deny eklensin — precedence'e 4½. basamak girer,
  Faz 1 mühürleri güncellenir.
- **(c)** Ertelensin: model bugün allow-union kalsın, deny ayrı bir faz olsun.

**10. 10.000+ kayıtta performans riski nerede?**
Satır başına reflection ile alan süzmede. **Kaynakta süzmede risk yok** — aksine daha az veri.

**11. En riskli migration hangisi?**
Hiçbiri: ikisi de yalnız `CREATE TABLE` ve boş doğar. **Asıl risk migration'da değil**,
`Explicit()` içindeki birleştirme satırında — yanlışsa yetki **artar** ve bu sessizdir.

**12. Hangi parçalar ayrı faza bölünmeli?**
- **Faz 3a** — rol izinleri (M1 + birleştirme + rol sekmesi) 🟢
- **Faz 3b** — alan yetkisi modeli + firma opt-in (M2) 🟡
- **Faz 3c** — alan süzmenin servis katmanına yayılması 🔴 *(en büyük iş)*
- **Faz 3d** — yetki ekranı UX (arama/filtre/diff/indeterminate) 🟢
- **Faz 3e** — E2E + 10.000 kayıt + masaüstü görsel borcu 🟢

**13. İlk uygulama fazı hangisi olmalı?**
**Faz 3a (rol izinleri).** Küçük, kapalı, tek tablo, Faz 1 mühürleriyle korunuyor ve en çok
istenen özellik (81 personele tek tek izin vermek bugün ölçeklenmiyor).

**14. Hangi noktada ayrıca onay almalıyım?**
- **S8/S9 kararı** (a/b/c) — bunsuz Faz 3b'ye başlanamaz.
- **G.1 senkron sınırı** (alan cihazda kalıyor) — güvenlik sınırı, sizin kabulünüz gerek.
- **Faz 3c'ye geçiş** — en büyük ve en riskli parça.
- **Alan kataloğunun hangi ekranları kapsayacağı** — kapsam kararı.

---

## SONUÇ

> ## 🟡 FAZ 3 TASARIMINDA ÇÖZÜLMESİ GEREKEN BLOKE NOKTALAR VAR

**Faz 3a (rol izinleri) bloke DEĞİL** — tasarımı tamam, onayla başlanabilir.

**Bloke olan iki nokta:**
1. **S8/S9** — ekran özelinde kısıtlama union-only ile mümkün değil; (a)/(b)/(c) kararınız gerekli.
2. **G.1** — senkron alan gizlemeyi taşıyamıyor; kabul edilen güvenlik sınırı onayınız gerekli.

Bu ikisi karara bağlanmadan Faz 3b/3c'ye başlamak, sessizce bir precedence icat etmek ya da
gerçekleşmeyecek bir güvenlik vaadi vermek olurdu.

---

# UYGULAMA KAYDI — FAZ 3a (2026-09-05)

> **Durum:** ✅ UYGULANDI · Migration092 · **üretime uygulanmadı, commit/push yapılmadı**

## Sabitlenen kararlar (kullanıcı onayı)

| Karar | İçerik |
|---|---|
| **S8/S9 = (a)** | Rol izni yalnız ALLOW. "Globalde ver, ekranda engelle" **bu fazda YOK**; DENY eklenmedi, yeni precedence basamağı açılmadı. Arayüz bu davranışı **sunmuyor** (RL14 kilitler). Mimari ileride DENY eklenmesine kapalı değildir: kısıtlama gerektiğinde 4½. basamak olarak girebilir. |
| **G.1 = kabul** | Alan güvenliği **UI + API + Servis** katmanlarındadır. Cihazın yerel SQLite'ına fiziksel/teknik erişimi olan kişiyi engellemez; senkron tüm kolonları taşıdığı için alan cihazda **fiziksel olarak bulunabilir**. Bu bir açık gizleme değil, **tanımlı tehdit modeli sınırıdır**. 3c'de yeniden değerlendirilebilir. |
| **K1** | Birleşim (union), yalnız ALLOW |
| **K3** | Şubesiz kullanıcı "sınırsız" davranışı **değiştirilmedi** |
| **K4** | Rapor kategori **VEYA** kalem mantığı **korundu** |
| **K5** | Faz 1 precedence mühürleri **değiştirilmedi** (31 test aynen geçiyor) |

## Veri modeli

`role_permissions` — `user_permissions`'ın aynası (`user_id` → `role_id`).
`role_button_permissions` — `user_button_permissions`'ın aynası.

**⚠️ `role_grant_limits` ile KARIŞTIRILMAMALI:** o tablo rol × modül **KAPATMA**dır (negatif) ve
"Rol Yetki Kontrol" ekranı onu yönetir. Yeni tablolar **VERME**dir (pozitif).

`module_key` serbest metin olduğu için `rpt_` (rapor) ve `datype_` (kayıt tipi) önekleri rol
seviyesinde **kendiliğinden** çalışır — ayrı migration gerekmedi (RL11 kanıtlar).

## Etkin izin algoritması

```
ETKİN AÇIK İZİN = user_permissions  ∪  role_permissions(kullanıcının rolleri, aynı firma)
                  bayrak bazlı VEYA
```

Tek yerde: `AuthService.LoadPermissions(conn, companyId, userId)` — masaüstünün `Login`'i ve
web/API'nin `CreateSessionForUser`'ı bu metodu paylaşır, dolayısıyla **iki platform aynı kümeyi**
alır. Bu küme `AccessControl.Can`'in **4. basamağıdır**; 1–3. basamaklar (rol kilidi, yapısal sınıf,
admin bypass) ondan önce çalışır ve rol ALLOW'u hiçbirini aşamaz.

**Firma süzgeci zorunlu:** roller sistem geneli olabilir (`roles.company_id IS NULL`), izin satırı
firmaya aittir. Süzgeç olmasa bir firmanın rolüne verdiği izin başka firmaya sızardı (RL7).

## Önbellek

Rol izni değişince `PermissionSnapshotCache.InvalidateAll()` — o role sahip **herkes** etkilendiği
için. Etki **anında**; yeniden giriş gerekmez, TTL beklenmez (RL12, RA3).

## Devretme tavanı

`SaveForRole` de `GrantCeiling` ile kırpar — aktör kendinde olmayanı bir role vererek dolaylı olarak
kazandıramaz. Kontrol **backend'dedir**, doğrudan HTTP çağrısıyla da sınandı (RL9, RA5).

## Kapsam dışı (bilinçli)

**Rol bazlı ŞUBE KAPSAMI eklenmedi.** Şube kapsamı `user_scopes` ile kullanıcı seviyesindedir ve
`BranchAccess` tek yorumlayıcıdır; role taşımak ikinci bir kapsam otoritesi yaratırdı (RL8 rol
izninin kapsamı genişletmediğini kilitler).
