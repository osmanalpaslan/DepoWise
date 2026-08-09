# DEPOWISE / ALPNEX — PROJE GENEL DURUM ANALİZİ

> **Bu dosya nedir?** Projenin mevcut durumunun koddan doğrulanmış fotoğrafı + gelecek geliştirmeler
> için kontrollü yol haritası. Tek başına yüklendiğinde başka bir yapay zekânın projeyi anlamasına
> yetecek şekilde yazılmıştır.
>
> **Bu aşamada hiçbir kod değiştirilmedi, migration çalıştırılmadı, deploy yapılmadı, veri değiştirilmedi.**

---

## 0. ANALİZ KÜNYESİ

| Alan | Değer | Durum |
|---|---|---|
| Analiz tarihi | 2026-08-09 | DOĞRULANDI |
| Git branch | `feature/masaustu-vektor-ikonlar` | DOĞRULANDI |
| Commit hash | `7e47214e7f932087a6caf2152d3fcf962b4ca6b7` | DOĞRULANDI |
| `master` son commit | `188b6eb` (tasarım paketi yayınlandı) | DOĞRULANDI |
| Masaüstü yayınlanan sürüm | **1.0.136** (`/api/releases/latest`) | DOĞRULANDI |
| Web | `depowise-web.fly.dev` — HTTP 200 | DOĞRULANDI |
| API | `depowise-erp.fly.dev` — `/health` 200 | DOĞRULANDI |
| Sunucu veritabanı | PostgreSQL (Neon, `depowise_prod`) | DOĞRULANDI (CLAUDE.md §4 + `ServerServices.cs:99`) |
| Masaüstü veritabanı | SQLite (`%LOCALAPPDATA%\DepoWise\Data`) | DOĞRULANDI |
| Migration sayısı | **62** (001–062, **boşluk yok**, hepsi katalogda kayıtlı) | DOĞRULANDI |
| Toplam tablo | **67** | DOĞRULANDI |
| API endpoint | **249** (118 GET, 98 POST, 20 DELETE, 13 PUT) | DOĞRULANDI |
| Sunucu diski | `/data` %40 dolu, 553 MB boş | DOĞRULANDI |

### Kod büyüklüğü (DOĞRULANDI)

| Proje | Dosya | Satır | Rol |
|---|---|---|---|
| `DepoWise.Infrastructure` | 143 | 19.833 | İş kuralları, veri erişimi, migration |
| `DepoWise.Desktop` | 141 (+53 `.axaml`) | 17.159 (+8.842) | Avalonia masaüstü |
| `DepoWise.Web` | 9 (+65 `.razor`) | 929 (+13.893) | Blazor Server |
| `DepoWise.Application` | 42 | 2.360 | Yetki, ortak sözleşmeler |
| `DepoWise.Api` | 9 | 3.627 | HTTP uçları |
| `DepoWise.Domain` | 1 | 13 | (neredeyse boş) |
| `tests` | 115 | 21.143 | 1017 test (984 geçiyor, 33 atlanıyor) |

### Analizin kapsamı

Okundu: migration kataloğu, `AccessControl`/`AppModules`/`SessionContext`, `ScopeResolver`,
`BusinessSyncService`/`BusinessSyncPushService`/`BusinessSyncPullService`, `ShellViewModel` timer,
`MaterialService` silme yolu, `EditLockGuard` kullanıcıları, `Program.cs` uç envanteri,
web `MainLayout`/`Login`, her iki platformun ekran listesi, `SqlDialect`, `THIRD_PARTY_NOTICES.md`.

### Analizin SINIRLARI — bu turda YAPILAMAYANLAR

Bunlar `DOĞRULANAMADI` sayılmalı, sonuç olarak sunulmamalıdır:

1. **Ekran-ekran tam parite denetimi yapılmadı.** 43 web + 36 masaüstü ekranın adları
   karşılaştırıldı; her ekranın alan/işlev/validasyon düzeyinde birebir karşılaştırması
   YAPILMADI. §5'teki tablo **ad düzeyinde**dir, alan düzeyinde değildir.
2. **Gerçek performans ölçümü yapılmadı.** Yük testi, profil, sorgu süresi ölçülmedi.
   Bu dosyadaki performans tespitleri **kod okumasına dayalı yapısal tespitlerdir**, ölçüm değildir.
3. **Canlı veri üzerinde hiçbir sorgu çalıştırılmadı.**
4. **Eşzamanlılık davranışı canlıda test edilmedi** (iki kullanıcı aynı anda kaydetme senaryosu).
5. **Tüm 249 uç tek tek yetki denetimi açısından incelenmedi.** Örnek uçlar incelendi.
6. **Rapor envanteri detaylı çıkarılmadı** (§19 kısmi).

> Bu sınırlar bir eksiklik değil, kapsam kararıdır. 1–2 ve 5 numaralı maddeler kendi başlarına
> birer iş kalemi olarak §31'e eklenmiştir (İŞ-19, İŞ-20).

---

## 1. MİMARİ — MEVCUT DURUM

```
                 ┌──────────────────────────┐
   Masaüstü ────►│  API (Fly.io)            │◄──── Web (Blazor Server, Fly.io)
   (Avalonia)    │  249 uç, PostgreSQL/Neon │      MudBlazor
   YEREL SQLite  └──────────────────────────┘
   (çevrimdışı)              │
        └── 15 sn'de bir senkron ──┘
```

**DOĞRULANDI — mimari değişmezler:**
- İş kuralları `DepoWise.Infrastructure` içinde **tek yerde**; hem API hem masaüstü aynı servisleri çağırır.
  Bu, "web ve masaüstü aynı davranır" garantisinin **yapısal** kaynağıdır ve projenin en güçlü yanıdır.
- Web'in kendi iş mantığı yoktur; uzak API'yi çağırır.
- Masaüstü çevrimdışı çalışır (yerel SQLite), sonra senkronize eder.
- `ON DELETE CASCADE` **hiçbir yerde yok** (DOĞRULANDI: migration'larda tek eşleşme yok) — bu
  bilinçli bir karardır (`DialectPurge` toplu silmeyi kontrollü yapar).
- Soft delete 18 migration dosyasında (`is_deleted`) kullanılıyor.

---

## 2. 🔴 EN KRİTİK BULGU — STOK ŞUBE BAZLI DEĞİL

Bu, projenin "çok şubeli kullanım" hedefinin **önündeki tek en büyük engeldir** ve
diğer tüm şube işlerinden önce karara bağlanmalıdır.

### Tespit (DOĞRULANDI)

`Migration005_Materials.cs`:

```sql
CREATE TABLE stock_balances (
    company_id TEXT NOT NULL,
    material_id TEXT NOT NULL,
    quantity TEXT NOT NULL DEFAULT '0',
    updated_at BIGINT NOT NULL,
    PRIMARY KEY (material_id),          -- ⚠ branch_id YOK
    FOREIGN KEY (material_id) REFERENCES materials(id)
);
```

- `stock_balances` **birincil anahtarı yalnız `material_id`** → bir malzemenin **firma genelinde TEK bakiyesi** var.
- `stock_movements` tablosunda **`branch_id` VAR** (nullable) → hareketler şube etiketli.
- Sonraki 57 migration'ın hiçbiri `stock_balances`'a dokunmamış (DOĞRULANDI: tabloyu referans eden
  tek dosya Migration005).
- `materials` tablosunda **`branch_id` YOK** → malzeme tanımı firma genelinde.

### Bunun pratik anlamı

| Soru | Bugünkü cevap |
|---|---|
| "Şube A'da kaç litre filtre yağı var?" | **Cevaplanamıyor.** Bakiye firma geneli. |
| "Şube B'ye giren mal Şube A'nın stoğunu artırır mı?" | **Evet, artırır.** Tek havuz. |
| Şube A ve Şube B ayrı depo gibi çalışabilir mi? | **Hayır.** |
| Hareket defterinden şube bakiyesi hesaplanabilir mi? | Teorik olarak evet (`branch_id` var), ama **hiçbir yerde böyle hesaplanmıyor** ve `branch_id` NULL olabildiği için eski kayıtlar şubesiz. |

### §12'deki senaryonun gerçekliği

Sorulan senaryo şuydu: *"Şube A'da Filtre Yağı var; Şube B'de aynı malzeme ayrıca oluşturulmuş,
stok 0. Şube B kullanıcısı silerse Şube A'daki silinmemeli."*

**Bu senaryo mevcut mimaride oluşamaz** — çünkü malzeme firma genelindedir, "Şube B'nin ayrı
malzemesi" diye bir şey yoktur. İki şube **aynı** malzeme kaydını paylaşır.

**Ama daha kötü bir risk var (aşağıda §3).**

### RİSK

- **RİSK-1 (P0):** Çok şubeli operasyonda stok rakamları **anlamsız** olur. Şantiyedeki
  depocu kendi deposunda olmayan malı "var" görür. Yanlış çıkış yapılır.
- **RİSK-2 (P0):** Bu, ürünün "büyük firmalara satılması" hedefiyle **doğrudan çelişir**;
  çok depolu/çok şantiyeli hiçbir firma bunu kabul etmez.
- **RİSK-3:** Düzeltme migration gerektirir ve **canlı veriyi dönüştürmeyi** gerektirir
  (mevcut tek bakiye hangi şubeye yazılacak?). Ne kadar geç kalınırsa o kadar pahalı olur.

### ÖNERİ

Bu bir **karar sorusudur**, teknik detay değil → §34'te SORU-1 olarak açıldı.
Teknik olarak minimum maliyetli yol:

1. `stock_balances` PK'sını `(material_id, branch_id)` yap.
2. Mevcut satırları firmanın **varsayılan/ana şubesine** taşı (veri kaybı yok, additive).
3. `stock_movements.branch_id`'yi **NOT NULL** yap (geçmiş kayıtlar ana şubeye atanır).
4. Stok okuma/yazma yollarını şube parametreli hale getir.

**Tahmini maliyet: Orta-Yüksek.** Ama **ertelendikçe artar** — veri büyüdükçe dönüştürme riski büyür.

---

## 3. 🔴 İKİNCİ KRİTİK BULGU — MALZEME SİLMEDE ŞUBE KORUMASI YOK

### Tespit (DOĞRULANDI)

`MaterialService.cs:395`:

```sql
UPDATE materials SET is_deleted=1, version=version+1, updated_at=@now
WHERE id=@id AND company_id=@c AND is_deleted=0;
```

Silme öncesi kontroller (DOĞRULANDI): yalnız `AccessControl.Require(s, Module, PermissionAction.Delete)`.

**YOK olanlar:**
- Şube kontrolü yok (zaten malzeme şube bazlı değil).
- **Stok bakiyesi kontrolü yok** → stoğu olan malzeme silinebiliyor.
- **Kullanımda mı kontrolü yok** → bakım/talep/hareket kaydı olan malzeme silinebiliyor.

### RİSK

- **RİSK-4 (P0):** Malzeme silme yetkisi olan **herhangi bir şubedeki** kullanıcı, **tüm firmanın**
  kullandığı bir malzemeyi silebilir. Diğer şubeler o malzemeyi listede göremez hale gelir.
- Hafifletici: silme **soft delete**'tir (`is_deleted=1`), veri fiziksel olarak durur ve Çöp
  Kutusu'ndan geri alınabilir (DOĞRULANDI: `SpecialButtons.RestoreTrash` var). Yani **kurtarılabilir**,
  ama operasyon o an durur.

### ÖNERİ (düşük maliyet, yüksek fayda)

Silme öncesi **üç kontrol** ekle (migration gerektirmez, tek servis dosyası):
1. Stok bakiyesi ≠ 0 ise engelle → "Bu malzemenin stoğu var, silinemez."
2. Aktif hareket/bakım/talep kaydı varsa uyar.
3. Şube bazlı stok geldiğinde (§2) → başka şubede stok varsa engelle.

**Maliyet: Düşük. Öncelik: P0. Migration: gerekmez.**

---

## 4. EKRAN ENVANTERİ (ad düzeyinde — DOĞRULANDI)

**Web: 43 sayfa · Masaüstü: 36 ekran**

### Yalnız Web'de olanlar (7)

| Ekran | Neden yalnız web (değerlendirme) |
|---|---|
| `Companies`, `PurgeCompany`, `QuotaMonitor` | Süper admin/platform yönetimi — **iş gereği web'de olması doğru** |
| `CompanyPermissions`, `RolePermissions` | Yetki yönetimi — web'de merkezî olması makul |
| `ResetCompanyBusiness`, `MachineBackups` | Yönetimsel/tehlikeli işlemler — web'de doğru |
| `Stock`, `StockEntry` farkı | Masaüstünde `StockEntry`, web'de `Stock` — **isim farkı, DOĞRULANAMADI: aynı işi mi yapıyorlar?** |

### Yalnız Masaüstünde olanlar (5)

| Ekran | Değerlendirme |
|---|---|
| `Dashboard` | Web'de karşılığı `Home` — **isim farkı, işlev aynı olabilir (DOĞRULANAMADI)** |
| `Settings`, `ThemeSettings` | Web'de `Theme` var; masaüstü ayarları yerel — **iş gereği farklı olabilir** |
| `About`, `ComponentGallery`, `Placeholder` | Geliştirici/bilgi ekranları — parite gerekmez |
| `MachineManagement` | Web'de `Machines` var — **muhtemelen eşdeğer** |

### RİSK

- **RİSK-5 (P2):** Ekran adları iki platformda tutarsız (`Dashboard`/`Home`, `StockEntry`/`Stock`,
  `AuditLog`/`Audit`, `DailyActivity`/`Daily`, `ImportExport`/`ImportExcel`). Bu, parite denetimini
  zorlaştırır ve "eksik mi, farklı mı adlanmış mı" sorusunu her seferinde yeniden doğurur.
- **ÖNERİ:** Ekranlara **ortak bir anahtar** (zaten yetki sisteminde `moduleKey` var) üzerinden
  referans veren tek bir eşleme tablosu tutulsun. Yeniden adlandırma **gerekmez** — sadece eşleme.

### EKSİK

**§4 ve §5'in istediği alan/işlev/validasyon düzeyinde tam parite denetimi bu turda yapılmadı.**
Bu, tek başına 1–2 günlük bir iştir ve İŞ-19 olarak listelenmiştir.

---

## 5. YETKİ SİSTEMİ — DERİN ANALİZ

### Mevcut yapı (DOĞRULANDI)

```
Firma (company_id)
 └─ Şube kapsamı (user_scopes tablosu — çoklu şube ATANABİLİYOR ✓)
     └─ Rol (RoleKeys: SuperAdmin, CompanyAdmin, RestrictedSuperAdmin, ...)
         └─ Modül (moduleKey)
             └─ İşlem: View | Create | Edit | Delete   ← SADECE 4 İŞLEM
         └─ Özel buton (9 adet)
```

**`PermissionAction` enum'u (DOĞRULANDI — `AppModules.cs:4`):** `View, Create, Edit, Delete`. Başka yok.

**Özel butonlar (DOĞRULANDI — 9 adet):**
`btn-approve`, `btn-reverse`, `btn-restore`, `btn-reset-db`, `btn-logo`, `btn-add-lookup`,
`btn-export-reports`, `btn-export-mgr-reports`, `btn-branch-select`

### İYİ ÇALIŞAN YÖNLER (DOĞRULANDI — bunları bozmayın)

1. **Deny-by-default gerçekten uygulanmış.** `Explicit()` metodu izin yoksa `false` döner.
2. **`BlockedModules` mekanizması var:** süper adminin bir role kapattığı ekran, **admin bypass'ıyla
   bile** açılamıyor. Bu iyi bir tasarım.
3. **`ScopeResolver` fail-closed:** açık kapsamı olmayan admin-olmayan kullanıcı **boş liste** alır,
   "hepsi" değil. Bu doğru yön.
4. **Çoklu şube ataması ZATEN VAR:** `user_scopes(user_id, company_id, branch_id)` tablosu mevcut ve
   `BranchService` yazıyor, `ScopeResolver` okuyor. Yani "kullanıcı birden çok şubeye atanabilir"
   altyapısı **hazır**.
5. **Senkron push'ta bile yetki kontrolü var:** `BusinessSyncService.TableModule` sözlüğü her tabloyu
   bir modüle bağlar; kullanıcı ancak Create/Edit yetkisi olan tabloyu push edebilir. Bu, çoğu projede
   atlanan bir noktadır — burada düşünülmüş.

### EKSİKLER (§10'un sorduğu hiyerarşiye göre)

| İstenen katman | Mevcut mu? | Not |
|---|---|---|
| Firma | ✅ VAR | `company_id` güvenilir session'dan |
| Şube | ✅ VAR | `user_scopes` + `CanViewAllBranches` |
| **Birim** | ❌ **YOK** | Aşağıda §6 |
| Kullanıcı/Rol | ✅ VAR | |
| Modül | ✅ VAR | |
| Ekran | ⚠️ KISMİ | Ekran = modül; alt-ekran ayrımı `maintenance:defs` gibi anahtarlarla kısmen var |
| İşlem | ⚠️ **KISITLI** | Yalnız View/Create/Edit/Delete + 9 buton |
| **Kayıt tipi** | ❌ **YOK** | Günlük Faaliyet kayıt tipleri ayrı yetkilendirilemiyor |
| **Veri kapsamı** | ⚠️ KISMİ | Şube var; "yalnız kendi kaydı" gibi kapsam yok |

### Onay (Approve) yetkisi neden sorun

`btn-approve` **tek bir global buton yetkisidir**. Yani:
- "Bakım onaylayabilir ama talep onaylayamaz" **ifade edilemiyor**.
- §14'te istenen "bakım onayı → stok düşümü" akışı için **modül bazlı onay yetkisi** gerekli.

### ÖNERİ — yetki ağacı baştan yazılmalı mı?

**HAYIR. Baştan yazmayın.** Mevcut yapı sağlam; eksik olan **genişletme**.

Minimum maliyetli yol (üç küçük adım, hepsi additive):

1. **`PermissionAction`'a `Approve` ve `Cancel` ekle** → `permissions` tablosuna 2 kolon
   (`can_approve`, `can_cancel`), varsayılan 0. Mevcut yetkiler **aynen korunur** (deny-by-default
   zaten yeni kolonu 0 kabul eder). *Migration: küçük, additive, risksiz.*
2. **Kayıt tipi yetkisi için ayrı tablo yazmayın** — mevcut buton mekanizmasını kullanın:
   `btn-daily-<kayittipi>` biçiminde anahtarlar. Yeni tablo/migration **gerekmez**,
   `SpecialButtons`'a sabit eklemek yeterli.
3. **Birim** için §6.

**Maliyet: Düşük-Orta. Migration: 1 adet küçük additive.**

---

## 6. KULLANICI / PERSONEL / BİRİM (§8)

### Tespit (DOĞRULANDI)

`personnel` tablosu kolonları:
```
id, company_id, branch_id, full_name, title, phone,
is_active, created_at, updated_at, version, is_deleted
```

- **`title` (unvan) VAR** — `personnel_titles` diye ayrı bir tanım tablosu da var (senkron listesinde).
- **Birim / departman alanı YOK.** DOĞRULANDI.
- `users` ile `personnel` bağlantısı: `users.personnel_id` VAR (DOĞRULANDI — users tablosu kolonları arasında).

> ⚠️ **Terim karışıklığı uyarısı:** Projede `units` tablosu **ölçü birimidir** (adet, kg, litre).
> "Personel Birimi" bundan tamamen farklı bir kavramdır. Yeni alan eklenirken **`units` adı
> kullanılmamalı** — `departments` veya `personnel_units` gibi ayrı bir ad seçilmeli. Aksi halde
> kod okunamaz hale gelir.

### Birim alanı gerekli mi?

**Evet, ama yetki için değil, RAPOR için.** Değerlendirme:

| Amaç | Birim gerekli mi? | Not |
|---|---|---|
| Raporda "hangi birim ne kadar harcadı" | ✅ **Evet** | Bugün cevaplanamıyor |
| Yetkilendirme | ⚠️ Şart değil | Şube + rol çoğu senaryoyu karşılıyor |
| Günlük faaliyet kayıt tipi kısıtı | ⚠️ Dolaylı | Kayıt tipi yetkisi (§5) daha doğrudan çözer |

### ÖNERİ

**Minimum maliyetli yol:** `personnel` tablosuna `unit_id` ekle + `personnel_units` tanım tablosu.
`users` tablosuna **ekleme yapma** — kullanıcı zaten `personnel_id` ile personele bağlı, birim
oradan okunur. Böylece tek kaynak korunur, veri çiftlenmez.

**Migration:** 1 additive (yeni tablo + nullable kolon). **Risk: Düşük. Maliyet: Düşük.**

**Karar sorusu:** §34 SORU-3.

---

## 7. WEB GİRİŞİ — VARSAYIM YANLIŞ ÇIKTI (§9)

### Tespit (DOĞRULANDI)

Prompt'ta *"Mevcut durumda yalnızca admin ve üstü roller Web'e giriş yapabiliyorsa"* deniyor.

**Koddan doğrulama sonucu: BÖYLE BİR KISIT YOK.**

- `MainLayout.Guard()` (satır 312) **yalnız** `Auth.IsAuthenticated` kontrol eder. Rol kontrolü yok.
- `/api/auth/login` ucunda rol bazlı reddetme bulunamadı. Bulunan tek 403,
  **"Tüm Şubeler" yetkisiyle** ilgilidir (`Program.cs:238`), giriş yetkisiyle değil.
- Web menüsü zaten yetkiye göre filtreleniyor (`AuthState.CanView`).

### Sonuç

**Tüm yetkili kullanıcılar zaten web'e girebiliyor olmalı.** Kullanıcı bunun aksini gözlemliyorsa
sebep başka bir yerdedir:

| Olası gerçek sebep | Nasıl doğrulanır |
|---|---|
| Kullanıcının hiç modül yetkisi yok → giriyor ama **boş menü** görüyor, "giremiyorum" sanıyor | Yetki ağacından o kullanıcıya bakılır |
| Kullanıcı pasif (`is_active=0`) | Kullanıcı kaydına bakılır |
| Şube kapsamı atanmamış → `ScopeResolver` boş döner → veri göremiyor | `user_scopes` satırına bakılır |

**ÖNERİ:** Kod değişikliği önerilmiyor. Önce **gerçek bir normal kullanıcıyla web'e giriş denenmeli**
ve ne olduğu gözlenmeli. Sorun yetki verisindeyse kod düzeltmesi yanlış çözüm olur.

**Bu, §34 SORU-2'dir.**

---

## 8. EŞZAMANLI DÜZENLEME / KAYIT KİLİDİ (§13)

### Mevcut durum (DOĞRULANDI)

Proje **optimistic concurrency** kullanıyor: `EditLockGuard` + `version` kolonu.
Davranış: iki kişi aynı kaydı açar, ikinci kaydeden **409 Conflict** alır ve uyarılır.

**Kapsam — `EditLockGuard` kullanan 8 servis (DOĞRULANDI):**
`MaterialService`, `VehicleService`, `PersonnelService`, `MaintenanceService`,
`MaintenanceDefinitionService`, `DailyActivityService`, `RequestService`, `BranchService`

**Kapsam DIŞINDA kalanlar (RİSK-6, P1):**
- Yakıt (`fuel_distributions`, `fuel_depot_entries`) — **korumasız**
- Stok belgeleri (`stock_documents`) — **korumasız**
- Muayene/Sigorta (`inspections`) — **korumasız**
- Kullanıcılar (`users`) — **korumasız**

Bu, "stok, sayaç, yakıt, bakım ve onayda LWW yasaktır" (CLAUDE.md §4) kuralıyla **çelişiyor**:
yakıt ve stok belgelerinde koruma yok.

### §13'te istenen şey FARKLI bir modeldir

İstenen: *"Kullanıcı B kaydı açmaya çalışırsa girmesine izin verilmemeli, kullanıcı adı gösterilmeli."*

Bu **pessimistic record lock**'tur. Mevcut sistem **optimistic**'tir. Fark:

| | Optimistic (mevcut) | Pessimistic (istenen) |
|---|---|---|
| Kayıt açma | Herkes açabilir | Sadece ilk kişi |
| Çakışma anı | Kaydederken | Açarken |
| Stale lock riski | **YOK** | **VAR** (çökme/kapanma/ağ kopması) |
| Ek altyapı | Yok (kolon zaten var) | Kilit tablosu + heartbeat + süre aşımı temizliği |
| Çevrimdışı çalışma | Uyumlu | **Uyumsuz** — masaüstü çevrimdışıyken kilit alamaz |

### ÖNERİ — hangisi doğru?

**Bu proje için optimistic concurrency DOĞRU seçimdir. Pessimistic lock'a geçilmemelidir.**

Gerekçe (bu projeye özgü, genel tavsiye değil):
1. **Masaüstü çevrimdışı çalışıyor.** Ağ yokken sunucudan kilit alınamaz. Pessimistic lock
   çevrimdışı çalışmayı **kırar** — bu, ürünün ana özelliğidir.
2. Stale lock temizliği (§13'te sayılan 6 senaryo: logout, çökme, ağ kopması, timeout, bilgisayar
   kapanması) **kalıcı bir bakım yükü** doğurur ve pratikte hep sorun çıkarır.
3. Mevcut `version` altyapısı zaten çalışıyor ve 8 serviste kanıtlanmış.

**Ama §13'ün asıl derdi karşılanabilir** — düşük maliyetle:

> **"Yumuşak uyarı" (soft lock):** Kayıt açılırken sunucuya "bu kaydı kim görüntülüyor" sorulur;
> son 2 dakika içinde başka biri açtıysa **uyarı gösterilir** ("Bu kaydı şu an Ahmet Yılmaz da
> açmış görünüyor") ama **engellenmez**. Kilit yok → stale lock yok → temizlik yok.
> Kaydederken zaten mevcut 409 koruması devrede.

**Maliyet: Düşük. Migration: 1 küçük tablo (veya bellekte). Risk: Yok (engellemiyor).**

**Karar sorusu: §34 SORU-4.**

---

## 9. SENKRONİZASYON — DETAYLI ANALİZ (§22, §23)

### Gerçek yapı (DOĞRULANDI — tahmin değil)

| Soru | Cevap | Kaynak |
|---|---|---|
| Ne zaman başlıyor? | Oturum açıldıktan sonra `DispatcherTimer` | `ShellViewModel.cs:300` |
| Timer var mı? | ✅ Var | `ShellViewModel.cs:300` |
| **Aralık nedir?** | **15 SANİYE** (30 değil ❗) | `TimeSpan.FromSeconds(15)` |
| Her turda ne oluyor? | **5 işlem**: ping + makine kaydı + yetki kontrolü + iş verisi push/pull + günlük yedek | `ShellViewModel.cs:301` |
| Kaç tablo? | **22** (toplam 67 tablonun 22'si) | `BusinessSyncService.Tables` |
| Sıra var mı? | ✅ Var — önce lookup/tanımlar, sonra iş kayıtları (dependency sırası düşünülmüş) | `BusinessSyncService.cs:30-56` |
| Incremental mi? | ✅ Evet — makine kendi "watermark"ını tutuyor | `ShellViewModel.cs:255-257` |
| HTTP timeout | Push/Pull **300 sn**, lookup 12 sn, ping 6 sn | ilgili servisler |
| Eşzamanlılık kapısı | ✅ `SyncGate.TryEnter()` — manuel eşitleme/reset ile yarışı engelliyor | `ShellViewModel.cs:250` |
| Push'ta yetki | ✅ Tablo→modül eşlemesiyle kontrol ediliyor | `BusinessSyncService.TableModule` |

### Senkron kapsamı DIŞINDA kalan tablolar (DOĞRULANDI)

`material_templates`, `stock_change_logs`, `audit_logs` senkron listesinde **yok**.
(`branches`, `users`, `roles` ayrı servislerle — `CompanySyncService`/`LookupSyncService` — senkronize
ediliyor olabilir; **DOĞRULANAMADI**, ayrıca incelenmeli.)

- **RİSK-7 (P2):** `material_templates` masaüstünde oluşturulursa sunucuya gitmez → web göremez.
  Bu, tasarım kararı da olabilir; **DOĞRULANAMADI**.

### Sunucu yükü — yapısal hesap

Her masaüstü istemci **15 saniyede ~5-6 HTTP isteği** üretiyor (ping, makine, yetki, sürüm, push, pull).

| İstemci sayısı | Yaklaşık istek/dakika | Değerlendirme |
|---|---|---|
| 1 (bugün: baban) | ~22 | Sorunsuz |
| 10 | ~220 | Sorunsuz |
| 50 | ~1.100 | Fly.io tek makinede **izlenmeli** |
| 200 | ~4.400 | **Mevcut yapıyla riskli** |

> ⚠️ Bu **ölçüm değil, yapısal hesaptır**. Gerçek yük testi yapılmadı (DOĞRULANAMADI).

### §23'ün sorusu: sürekli bağlantı gerekli mi?

**HAYIR. Sürekli aktif bağlantıya (WebSocket/SignalR) GEÇMEYİN.**

| Seçenek | Değerlendirme |
|---|---|
| **A — Sürekli aktif bağlantı** | ❌ Her istemci için açık bağlantı = Fly.io'da bellek/soket maliyeti. Çevrimdışı çalışmayla kavramsal olarak çelişir. **Bugünkü kullanıcı sayısı için tamamen gereksiz.** |
| **B — Periyodik (mevcut)** | ✅ Basit, kanıtlanmış, çevrimdışıyla uyumlu. |
| **C — Event-driven** | ⚠️ Sunucudan istemciye tetik gerektirir → yine A'ya döner. |
| **D — Hybrid (ÖNERİLEN)** | ✅ Periyodik kalsın, **ama akıllı olsun**. |

**ÖNERİ (D) — düşük maliyetli, migration gerektirmez:**

1. **Boştaysa yavaşla:** Kullanıcı 5 dakikadır işlem yapmadıysa aralığı 15 sn → 60 sn'ye çıkar.
   Tek başına sunucu yükünü **~4 kat** azaltır.
2. **Gönderilecek bir şey yoksa push'u atla:** Yerel değişiklik yoksa HTTP isteği hiç yapılmasın.
3. **Beş işlemi ayır:** Günlük yedek kontrolü 15 saniyede bir çalışmamalı — saatte bir yeter.
4. **Exponential backoff:** Sunucu hata verirse 15 sn'de bir tekrar denemek yükü artırır;
   hata halinde aralık kademeli açılsın.

**Maliyet: Çok düşük (tek dosya, `ShellViewModel`). Etki: Yüksek.** Bu, **B kategorisinin en iyi işi**.

---

## 10. PERFORMANS (§6) — YAPISAL TESPİTLER

> ⚠️ Hiçbir ölçüm yapılmadı. Aşağıdakiler kod okumasına dayanır.

| Tespit | Durum | Not |
|---|---|---|
| `PageRequest.MaxLimit = 200` | DOĞRULANDI (önceki analizlerden) | 2463 malzemeli firmada seçicilerde sorun çıkarmıştı, sunucu-taraflı aramaya geçilerek çözülmüş |
| N+1 sorgular | KISMİ — `/api/materials` N+1'i daha önce düzeltilmiş (DEVAM.md) | Diğer uçlar **DOĞRULANAMADI** |
| Senkron polling | **RİSK** — §9'daki 15 sn/5 işlem | En büyük yapısal yük kaynağı |
| Canlı Sunucu ekranı polling | 3000 ms | Yalnız o ekran açıkken; kabul edilebilir |
| `Geometry` önbellek | Yeni eklendi (ikon) | Sorun değil |
| UI thread bloklanması | Kod `async` kullanıyor; `SyncGate` ve `Dispatcher.UIThread` doğru kullanılmış | Yapısal olarak iyi |
| Cache | **Görünür bir uygulama cache'i YOK** | Lookup verileri her seferinde çekiliyor olabilir — DOĞRULANAMADI |

**EN YÜKSEK GETİRİLİ PERFORMANS İŞİ:** §9'daki senkron aralığı optimizasyonu. Diğer her şeyden
önce bu yapılmalı — çünkü hem sunucu yükünü hem pil/ağ tüketimini doğrudan düşürür.

---

## 11. GÜNLÜK FAALİYET — MÜKERRER KAYIT (§15)

### Mevcut durum

`DailyActivityService` `EditLockGuard` kullanıyor (DOĞRULANDI) → eşzamanlı düzenleme korumalı.
**Mükerrer kayıt kontrolü olup olmadığı DOĞRULANAMADI** (bu turda servis detayı okunmadı).

### İstenen davranış için ÖNERİ

İstenen akış (uyarı + "Kaydı görüntüle" / "Yine de devam et") **doğru tasarlanmış**. Teknik olarak
en güvenli uygulama:

1. **Kontrol sunucuda yapılmalı**, istemcide değil. İstemcide yapılırsa iki kullanıcı aynı anda
   aynı kaydı girebilir.
2. **"Yine de devam et" bir bayrakla taşınmalı:** İstemci ikinci çağrıda `allowDuplicate=true`
   göndermeli. Sunucu bu bayrağı görürse kontrolü atlar. Böylece sonsuz döngü olmaz.
3. **Bu bayrak audit'e yazılmalı** (§16 ile birleşiyor): "kullanıcı uyarıyı gördü ve devam etti".
4. **Benzersizlik kısıtı (UNIQUE INDEX) KOYMAYIN.** Çünkü aynı gün aynı araca ikinci kayıt
   **meşru olabilir** (sabah ve öğleden sonra iki ayrı iş). Kısıt koyarsanız meşru kaydı da
   engellersiniz ve geri dönüşü migration gerektirir.

**Maliyet: Düşük. Migration: Gerekmez** (bayrak audit'e yazılacaksa §12'deki log tablosu yeterli).

---

## 12. UYARI VE AUDIT LOG (§16)

### Mevcut durum (DOĞRULANDI)

- `audit_logs` tablosu VAR, `AuditLogService` ve `AuditWriter` VAR.
- `stock_change_logs` tablosu VAR (Migration057).
- **Ama ikisi de "kullanıcı uyarı aldı ve şunu seçti" bilgisini tutmuyor** (DOĞRULANAMADI ama
  audit tipik olarak veri değişikliği kaydeder, kullanıcı kararını değil).

### §16'nın istediği şey yeni bir log türüdür

İstenen alanlar: kullanıcı, personel, **birim**, şube, ekran, işlem, kayıt, uyarı tipi, uyarı nedeni,
tarih/saat, **kullanıcının seçimi**, kaydı görüntüledi mi, yine de devam etti mi, sonuç.

Bu bir **"kullanıcı kararı logu"**dur ve audit'ten farklıdır. **ÖNERİ:** Ayrı tablo (`user_decision_logs`)
açın, `audit_logs`'u kirletmeyin — audit'in amacı veri değişikliğidir, karıştırılırsa ikisi de
kullanışsızlaşır.

⚠️ **Bağımlılık:** "birim" alanı §6'ya bağlı. Birim eklenmeden bu log birim bazlı raporlanamaz.

**Maliyet: Düşük-Orta. Migration: 1 yeni tablo. Öncelik: P2** (ticari ürün için değerli, bugün zorunlu değil).

---

## 13. BAKIM → ONAY → STOK (§14)

### Değerlendirme

İstenen akış **doğru ve sektör standardıdır**. Bakım personelinin stoğu doğrudan düşürmesi,
büyük firmalarda kabul edilmeyen bir kontrol açığıdır.

### Mevcut engeller (DOĞRULANDI)

1. **Onay yetkisi modül bazlı değil** (§5) — `btn-approve` tek global buton. Bakım onayı ayrı
   yetkilendirilemiyor. **Bu iş, §5 adım 1'e bağımlıdır.**
2. Bakımda negatif stok **bilinçli olarak serbest** (DEVAM.md: "Birim 8 — yetersiz stok artık
   ENGELLENMEZ"). Onay akışı gelirse bu kararla **çelişebilir** — birlikte ele alınmalı.

### ÖNERİ

Şimdi yapmayın. Sırası:
```
§5 adım 1 (Approve yetkisi)  →  bakım onay durumu (yeni kolon)  →  stok düşümünü onaya bağla
```
**Maliyet: Orta. Migration: 1 additive (durum kolonu). Öncelik: P1 — ama ticari satış hedefine bağlı.**

---

## 14. ÖLÜ KOD / TEKNİK BORÇ (§18)

| Tespit | Durum | Değerlendirme |
|---|---|---|
| `apps/web` (Next.js/Drizzle) | DOĞRULANDI — mevcut, `DONDURULDU.md` var | **Ölü kod.** 2026-06-27'den beri donmuş. Depoda duruyor, kimse kullanmıyor. |
| `DepoWise.Domain` | 1 dosya, **13 satır** | Neredeyse boş bir proje. Zararı yok ama mimari şema yanıltıcı. |
| `Placeholder.axaml`, `ComponentGallery.axaml` | Masaüstünde mevcut | Geliştirici ekranları — kullanıcıya görünüyorsa temizlenmeli (DOĞRULANAMADI) |
| Ekran adı tutarsızlığı | §4 | Bakım maliyeti yaratıyor |
| 33 atlanan test | DOĞRULANDI | Neden atlandığı **DOĞRULANAMADI** — incelenmeli |

**ÖNERİ:** `apps/web` **silinmesin** (geçmiş referans), ama `.gitignore`/README'de durumu net olsun.
Salt güzellik için refactoring **önerilmiyor**. `DepoWise.Domain`'e dokunmayın — çalışıyor.

---

## 15. OTOMATİK GÜNCELLEME (§25)

### Mevcut durum (DOĞRULANDI — kısmen)

- `AutoUpdateService` var, `AutoUpdateEnabled` ayarı var.
- Masaüstü ana ekranda: "Güncellemeyi İndir ve Kur" butonu + "Kontrol Et" + otomatik anahtar.
- Anahtarın açıklaması (DOĞRULANDI, ekrandaki tooltip): *"Açıkken yeni sürüm çıkınca otomatik
  onay penceresi gelir (reddedilirse 10 dk'da bir tekrar sorar)"*

### İstenen davranışla fark

| İstenen | Mevcut |
|---|---|
| İndirme izni sorMAsın | ❌ **Onay penceresi geliyor** |
| Otomatik indirsin/kursun | ⚠️ Onaydan sonra |
| Sadece yeniden başlatma için sorsun | ❌ |
| **Ertele** seçeneği | ⚠️ "10 dk sonra tekrar sorar" var, kullanıcı kontrollü erteleme YOK |

**Sonuç: Mevcut yapı isteneni KARŞILAMIYOR ama uzak da değil.** Değişiklik `AutoUpdateService`
içinde sınırlı; migration gerektirmez.

**RİSK-8:** Kullanıcı bir kayıt girerken otomatik güncelleme başlarsa veri kaybı olabilir.
Otomatik kurulum **yalnız boştayken** tetiklenmeli.

**Maliyet: Düşük. Öncelik: P2.**

---

## 16. RAPORLAR (§19) — KISMİ

**DOĞRULANAMADI:** Rapor envanteri ve NumCell/TotalRow standardına uyum bu turda çıkarılmadı.
`ReportModels.cs` okundu (DashboardAlert), `ReportsViewModel` yetki kontrolleri görüldü
(`btn-export-reports`, `btn-export-mgr-reports`, `btn-branch-select` ayrı yetkiler — iyi).

Bu bölüm İŞ-20 olarak listelendi.

---

## 17. ALAN/KOLON YÖNETİMİ (§20) VE PLATFORM GÖRÜNÜRLÜĞÜ (§21)

### Alan/Kolon Yönetimi — mimari hazır mı?

**Kısmen hazır.** `ListColumns.cs` **iki kopya** olarak var
(`DepoWise.Application/Ui/ListColumns.cs` + aynası `DepoWise.Web/Services/ListColumns.cs`) ve
`.claude/rules/list-screens.md` bunların **birlikte** güncellenmesini zorunlu kılıyor.

- ✅ Kolon kataloğu kavramı var, kolon görünürlüğü çalışıyor (`VisibleColumns`).
- ⚠️ **İki kopya olması teknik borçtur** — biri güncellenip diğeri unutulursa ekran sessizce bozulur.
- ❌ Firma bazlı tercih altyapısı **DOĞRULANAMADI** (`UserListPreferenceService` var, ama kişisel).

**§20'nin sorusu — rapor yalnız kendi kaynağının alanlarını mı kullanmalı?**

**ÖNERİ: Evet, başlangıçta yalnız kendi kaynağı.** İlişkili tabloların alanlarını seçtirmek
kullanıcıya "join" kavramını dayatır — yazılım bilmeyen kullanıcı için **kullanılamaz** olur ve
performans açısından kontrolsüz sorgular doğurur. İlişkili alanlar gerekiyorsa, **önceden
tanımlanmış** birkaç alan rapor kaynağına dahil edilmeli (derived-table deseni zaten var).

### Platform / Ekran Görünürlüğü

**"Görünürlük ≠ Yetki" ayrımı doğrudur ve korunmalıdır.** Mevcut sistemde bu ayrım **yok** —
görünürlük yetkiden türüyor (`CanSeeMenu` → `Can(View)`).

**RİSK-9:** Görünürlük ayrı bir katman olarak eklenirse, **yanlışlıkla yetki yerine geçirilmesi**
en büyük risktir. Ekleme yapılırsa API tarafında **hiçbir şey değişmemeli** — görünürlük yalnız
menü çizimini etkilemeli.

**Öncelik: P3.** Bugün gerekli değil.

---

## 18. §29 — ŞİMDİ / DÜŞÜK MALİYET / SONRA

### A — ŞİMDİ YAPILMASI GEREKENLER (canlıya çok kullanıcılı geçmeden önce ZORUNLU)

| ID | İş | Neden zorunlu |
|---|---|---|
| **A1** | Malzeme silmede stok/kullanım kontrolü (§3) | Bir kullanıcı tüm firmanın malzemesini silebiliyor |
| **A2** | Stok şube boyutu KARARI (§2) | Çok şubeli kullanımın önündeki tek engel; ertelendikçe pahalılaşır |
| **A3** | Eksik düzenleme kilitleri: yakıt, stok belgeleri, muayene (§8) | CLAUDE.md §4 kuralı ihlal ediliyor |
| **A4** | Süper admin parolası değiştirilmeli | Zayıf parola, canlıda çalıştığı doğrulandı |

### B — DÜŞÜK MALİYETLE ŞİMDİ YAPILABİLECEKLER

| ID | İş | Etki |
|---|---|---|
| **B1** | Senkron aralığı akıllandırma (§9) | Sunucu yükünde ~4 kat azalma, tek dosya |
| **B2** | `PermissionAction`'a Approve/Cancel (§5) | Sonraki 3 işin önkoşulu |
| **B3** | Personel birimi alanı (§6) | Rapor doğruluğu |
| **B4** | Günlük faaliyet mükerrer uyarısı (§11) | Veri kalitesi |
| **B5** | Kayıt tipi yetkisi (buton mekanizmasıyla, migration'sız) (§5) | İstenen özellik, sıfır şema maliyeti |
| **B6** | Ekran adı eşleme tablosu (§4) | Parite denetimini kalıcı kolaylaştırır |
| **B7** | Otomatik güncelleme davranışı (§15) | Kullanıcı deneyimi |

### C — YATIRIM / PARA SONRASI

| ID | İş | Neden ertelenmeli |
|---|---|---|
| **C1** | Gelişmiş izleme/monitoring | Ücretli servis; bugün Fly.io metrikleri yeter |
| **C2** | Kuyruk/background job altyapısı | **Gerçek ihtiyaç YOK.** Mevcut yük buna uzak |
| **C3** | Sürekli bağlantı (WebSocket) | §9'da gerekli olmadığı gösterildi |
| **C4** | Alan/Kolon Yönetimi UI (§17) | Büyük iş, bugün kimse istemiyor |
| **C5** | Platform görünürlük yönetimi (§17) | P3 |
| **C6** | Kullanıcı karar logu (§12) | Ticari ürün için değerli, bugün değil |
| **C7** | Bakım→onay→stok akışı (§13) | B2'ye bağımlı; büyük firma satışı olmadan gerekmiyor |

---

## 19. YATIRIM SONRASI GELİŞTİRME BACKLOGU

**ID:** Y-1
**Başlık:** Kuyruk / background job altyapısı
**Neden gerekli:** Uzun süren raporlar ve toplu içe aktarım sunucuyu meşgul ediyor
**Kullanıcı faydası:** Yoğun anda arayüz donmaz
**Teknik gereksinim:** Kuyruk altyapısı + işçi süreç
**Bağımlılıklar:** Yok
**Migration:** Muhtemelen 1 (iş kuyruğu tablosu)
**Web/Masaüstü/API:** API ağırlıklı
**Tahmini maliyet:** Yüksek
**Minimum maliyetli alternatif:** Ağır raporları zaten "Sorgula" butonuna bağlı; şimdilik yeterli
**Canlı öncesi zorunlu mu:** ❌ Hayır
**Öncelik:** P3 · **Ertelenebilir:** ✅ Evet
**Not:** *Sırf modern mimari olduğu için yapılmamalı. Gerçek ihtiyaç oluşana kadar bekletin.*

---

**ID:** Y-2
**Başlık:** Alan / Kolon Yönetimi ekranı
**Neden gerekli:** Kullanıcının teknik destek almadan rapor görünümü yönetmesi
**Kullanıcı faydası:** Yüksek (ticari satışta ayırt edici)
**Teknik gereksinim:** `ListColumns` çift kopyasının tekilleştirilmesi önce yapılmalı
**Bağımlılıklar:** ListColumns tekilleştirme
**Migration:** 1 (firma bazlı tercih tablosu)
**Web/Masaüstü/API:** Üçü de
**Tahmini maliyet:** Yüksek
**Minimum maliyetli alternatif:** Mevcut kolon gizle/göster zaten var — çoğu ihtiyacı karşılıyor
**Canlı öncesi zorunlu mu:** ❌ Hayır
**Öncelik:** P3 · **Ertelenebilir:** ✅ Evet

---

**ID:** Y-3
**Başlık:** Kullanıcı karar logu (uyarı takibi)
**Neden gerekli:** "Kim hangi uyarıyı aldı, ne seçti" sonradan raporlanabilsin
**Kullanıcı faydası:** Orta (denetim/sorumluluk)
**Teknik gereksinim:** Yeni tablo; `audit_logs` KİRLETİLMEMELİ
**Bağımlılıklar:** Personel birimi (B3) — birim bazlı rapor için
**Migration:** 1 yeni tablo
**Tahmini maliyet:** Orta
**Canlı öncesi zorunlu mu:** ❌ Hayır
**Öncelik:** P2 · **Ertelenebilir:** ✅ Evet

---

**ID:** Y-4
**Başlık:** Bakım → onay → stok düşümü akışı
**Neden gerekli:** Bakım personeli stoğu doğrudan düşürmemeli (büyük firma gereksinimi)
**Kullanıcı faydası:** Yüksek (kontrol)
**Teknik gereksinim:** Modül bazlı Approve yetkisi (B2)
**Bağımlılıklar:** **B2 zorunlu önkoşul**
**Migration:** 1 additive (bakım durum kolonu)
**Tahmini maliyet:** Orta
**Canlı öncesi zorunlu mu:** ❌ Hayır (mevcut kullanıcı tek kişi)
**Öncelik:** P1 (ticari satış hedefi için) · **Ertelenebilir:** ✅ Evet
**Not:** "Bakımda negatif stok serbest" kararıyla çelişebilir — birlikte ele alınmalı.

---

**ID:** Y-5
**Başlık:** Gelişmiş izleme (monitoring/alerting)
**Neden gerekli:** Çok firmalı işletmede arıza önceden görülsün
**Teknik gereksinim:** Harici ücretli servis
**Tahmini maliyet:** Orta (aylık gider)
**Minimum maliyetli alternatif:** Mevcut "Canlı Sunucu" ekranı + Fly.io metrikleri
**Canlı öncesi zorunlu mu:** ❌ Hayır
**Öncelik:** P3 · **Ertelenebilir:** ✅ Evet

---

## 20. §31 — BİRLEŞİK ÖNCELİK LİSTESİ

| ID | İş | Kategori | Platform | Bağımlılık | Migration | Risk | Maliyet | Öncelik | Şimdi/Ertele |
|---|---|---|---|---|---|---|---|---|---|
| İŞ-1 | Süper admin parolası değişimi | Güvenlik | — | Yok | ❌ | **Yüksek** | Çok düşük | **P0** | **ŞİMDİ** |
| İŞ-2 | Malzeme silmede stok/kullanım kontrolü | Veri bütünlüğü | API+2 | Yok | ❌ | Yüksek | Düşük | **P0** | **ŞİMDİ** |
| İŞ-3 | Stok şube boyutu KARARI (kod değil, karar) | Mimari | — | Yok | — | **Çok yüksek** | — | **P0** | **ŞİMDİ** |
| İŞ-4 | Eksik düzenleme kilitleri (yakıt/stok/muayene) | Eşzamanlılık | API+2 | Yok | ❌ | Yüksek | Düşük | **P0** | **ŞİMDİ** |
| İŞ-5 | Senkron aralığı akıllandırma | Performans | Masaüstü | Yok | ❌ | Düşük | Çok düşük | **P1** | **ŞİMDİ** |
| İŞ-6 | `PermissionAction` + Approve/Cancel | Yetki | Üçü | Yok | ✅ küçük | Orta | Düşük | **P1** | ŞİMDİ |
| İŞ-7 | Kayıt tipi yetkisi (buton mekanizması) | Yetki | Üçü | İŞ-6 | ❌ | Düşük | Düşük | **P1** | ŞİMDİ |
| İŞ-8 | Personel birimi alanı | Veri modeli | Üçü | Yok | ✅ additive | Düşük | Düşük | **P1** | ŞİMDİ |
| İŞ-9 | Günlük faaliyet mükerrer uyarısı | Veri kalitesi | Üçü | Yok | ❌ | Düşük | Düşük | **P1** | ŞİMDİ |
| İŞ-10 | Normal kullanıcı web girişi doğrulaması | Doğrulama | Web | Yok | ❌ | Düşük | Çok düşük | **P1** | **ŞİMDİ** |
| İŞ-11 | Stok şube boyutu UYGULAMA | Mimari | Üçü | İŞ-3 | ✅ **riskli** | Çok yüksek | Yüksek | **P0/P1** | Karara bağlı |
| İŞ-12 | Yumuşak kayıt uyarısı (soft lock) | Eşzamanlılık | Üçü | İŞ-4 | ⚠️ küçük | Düşük | Düşük | P2 | Ertele |
| İŞ-13 | Otomatik güncelleme davranışı | UX | Masaüstü | Yok | ❌ | Orta | Düşük | P2 | Ertele |
| İŞ-14 | Ekran adı eşleme tablosu | Bakım | — | Yok | ❌ | Yok | Çok düşük | P2 | ŞİMDİ |
| İŞ-15 | `ListColumns` çift kopya tekilleştirme | Teknik borç | Web+App | Yok | ❌ | Orta | Orta | P2 | Ertele |
| İŞ-16 | Kullanıcı karar logu | Denetim | Üçü | İŞ-8 | ✅ yeni tablo | Düşük | Orta | P2 | Ertele |
| İŞ-17 | Bakım→onay→stok | İş akışı | Üçü | İŞ-6 | ✅ additive | Orta | Orta | P1 | Ertele |
| İŞ-18 | 33 atlanan testin incelenmesi | Kalite | Test | Yok | ❌ | Orta | Düşük | P2 | Ertele |
| **İŞ-19** | **Tam ekran parite denetimi (alan düzeyi)** | Analiz | Üçü | Yok | ❌ | Orta | Orta | **P1** | ŞİMDİ |
| **İŞ-20** | **Rapor envanteri + standart denetimi** | Analiz | Üçü | Yok | ❌ | Düşük | Orta | P2 | Ertele |

---

## 21. §32 — BAĞIMLILIK ZİNCİRİ

```
İŞ-1 (parola)  ── bağımsız, hemen
İŞ-3 (stok şube KARARI)
   └─► İŞ-11 (stok şube uygulama)  ── en büyük iş, karar olmadan başlamayın
İŞ-2 (silme kontrolü)  ── bağımsız
İŞ-4 (eksik kilitler)
   └─► İŞ-12 (yumuşak uyarı)
İŞ-5 (senkron)  ── bağımsız, en yüksek getiri/maliyet oranı
İŞ-6 (Approve yetkisi)
   ├─► İŞ-7 (kayıt tipi yetkisi)
   └─► İŞ-17 (bakım onay → stok)
İŞ-8 (personel birimi)
   └─► İŞ-16 (karar logu, birim bazlı rapor)
İŞ-19 (parite denetimi)  ── bağımsız; sonucu yeni işler doğurabilir
```

**Kural:** Canlı veriye dokunan tek iş **İŞ-11**'dir. Tek seferde uygulanmamalı; en az 3 faza bölünmeli
(şema ekle → çift yazım → okuma geçişi).

---

## 22. §33 — GELİŞTİRME METODOLOJİSİ

Her iş için:

```
ANALİZ → KULLANICI ONAYI → GELİŞTİRME → TEST
   → WEB DOĞRULAMA → MASAÜSTÜ DOĞRULAMA → SENKRON DOĞRULAMA
   → DEPLOY → GELİŞTİRME RAPORU → SONRAKİ AŞAMA
```

**Bu projeye özgü eklemeler:**
- Masaüstü görsel doğrulaması **yalnız kullanıcıda yapılabilir** (Avalonia önizlemesi yok).
  Bu, her masaüstü işinde planlanmalı.
- Migration içeren işler **ayrı onay** ister (CLAUDE.md).
- Her iş sonunda `DEVAM.md` + `docs/YARIM_KALAN_ISLER.md` güncellenmeli.

---

## 23. §34 — KARAR GEREKTİREN SORULAR

### SORU-1 — Stok şube bazlı mı olsun? 🔴 EN ÖNEMLİ

**Neden önemli:** Çok şubeli kullanımın tamamı buna bağlı. Ertelendikçe dönüştürme maliyeti artar.
Geri dönüşü zor mimari karardır.

- **A) Stok firma genelinde kalsın (bugünkü hâli).** Maliyet sıfır. Ama çok şubeli/çok depolu
  operasyon **hiçbir zaman doğru çalışmaz**; ticari satış hedefi bundan zarar görür.
- **B) Stok şube bazlı olsun.** `stock_balances` PK → `(material_id, branch_id)`. Mevcut bakiyeler
  ana şubeye taşınır (veri kaybı yok). Maliyet: Yüksek. Risk: canlı veri dönüşümü.
- **C) Şimdilik A, ama yeni kayıtlar şube etiketli tutulsun** (hazırlık), geçiş sonraya bırakılsın.

**ÖNERİM: B — ama hemen değil, İŞ-1/2/4/5 bittikten sonra ve fazlara bölünerek.**
**Neden:** Babanız tek şubeyle çalıştığı sürece A yeterli görünüyor; ama ürünü satmayı hedeflediğiniz
an B zorunlu. Veri büyümeden yapmak, sonra yapmaktan **çok daha ucuz**. C ise iki maliyeti de öder.

---

### SORU-2 — Web girişi gerçekten kısıtlı mı?

**Neden önemli:** Yanlış teşhisle kod değiştirilirse var olmayan bir sorun "çözülür", gerçek sorun kalır.

- **A) Önce gerçek bir normal kullanıcıyla web'e giriş denensin**, sonuç gözlensin.
- **B) Doğrudan kod değişikliği yapılsın.**

**ÖNERİM: A.** **Neden:** Kodda giriş kısıtı **bulunamadı** (DOĞRULANDI). Sorun büyük olasılıkla
yetki verisinde (boş menü) veya şube kapsamında. Test 5 dakika sürer, kod değişikliği günler.

---

### SORU-3 — Personel birimi nereye eklensin?

**Neden önemli:** Yanlış yere eklenirse veri çiftlenir ve raporlar tutarsızlaşır.

- **A) `personnel` tablosuna `unit_id`** (kullanıcı birimini `personnel_id` üzerinden okur).
- **B) Hem `personnel` hem `users`'a ayrı ayrı.**

**ÖNERİM: A.** **Neden:** B'de aynı bilgi iki yerde tutulur; biri güncellenip diğeri unutulursa
rapor yanlış çıkar. A'da tek kaynak vardır. Ayrıca `units` adı **kullanılmamalı** (ölçü birimiyle
karışır) — `personnel_units` veya `departments` denmeli.

---

### SORU-4 — Kayıt kilidi: optimistic mi, pessimistic mi?

**Neden önemli:** Pessimistic lock, masaüstünün çevrimdışı çalışmasını kırar — bu ürünün ana özelliğidir.

- **A) Mevcut optimistic devam** + eksik ekranlara yayılsın (İŞ-4).
- **B) Pessimistic record lock'a geçilsin** (§13'te tarif edilen).
- **C) Optimistic + "yumuşak uyarı"** (başkası açmışsa uyar, engelleme).

**ÖNERİM: A önce, sonra C.** **Neden:** B, çevrimdışı çalışmayla uyumsuzdur ve stale lock temizliği
kalıcı bir bakım yükü doğurur (§13'te sayılan 6 senaryonun her biri ayrı hata kaynağıdır).
C, istenen kullanıcı deneyiminin **%90'ını** stale lock riski olmadan verir.

---

### SORU-5 — Günlük faaliyette benzersizlik kısıtı konsun mu?

**Neden önemli:** Yanlış konursa meşru kayıtlar engellenir ve geri alması migration ister.

- **A) Veritabanı UNIQUE kısıtı konsun** (kesin engelleme).
- **B) Yalnız uyarı, kısıt yok** (kullanıcı devam edebilir).

**ÖNERİM: B.** **Neden:** Aynı gün aynı araca ikinci kayıt **meşru olabilir** (sabah/öğleden sonra
iki ayrı iş). Kısıt koyarsanız gerçek işi engellersiniz. Uyarı + "yine de devam et" + audit kaydı
doğru dengedir.

---

## 24. ÖNERİLEN GELİŞTİRME SIRASI

**1. Süper admin parolası değiştir** — *Neden önce:* Canlı güvenlik açığı, doğrulandı. Bağımlılık: yok.
Platform: —. Migration: ❌. Risk: yok. Maliyet: çok düşük. Sonraki adıma etkisi: yok.

**2. Normal kullanıcıyla web girişi testi** — *Neden önce:* Kod yazmadan önce teşhis. Bağımlılık: yok.
Platform: Web. Migration: ❌. Risk: yok. Maliyet: çok düşük. Etkisi: İŞ-10'un gerekip gerekmediğini belirler.

**3. Malzeme silmede stok/kullanım kontrolü** — *Neden önce:* P0 veri riski, tek servis dosyası.
Bağımlılık: yok. Platform: API+web+masaüstü. Migration: ❌. Risk: düşük. Maliyet: düşük.

**4. Eksik düzenleme kilitleri (yakıt, stok belgeleri, muayene)** — *Neden önce:* Mevcut kural ihlali,
desen zaten 8 serviste kanıtlı. Bağımlılık: yok. Migration: ❌. Risk: düşük. Maliyet: düşük.

**5. Senkron aralığı akıllandırma** — *Neden önce:* En yüksek getiri/maliyet oranı; sunucu yükünü
~4 kat düşürür. Bağımlılık: yok. Platform: masaüstü. Migration: ❌. Maliyet: çok düşük.

**6. Stok şube boyutu KARARI (SORU-1)** — *Neden önce:* Sonraki büyük işlerin hepsi buna bağlı.
Bu bir **karar adımıdır**, kod değil.

**7. `PermissionAction` + Approve/Cancel** — *Neden önce:* Kayıt tipi yetkisi ve bakım onayının önkoşulu.
Migration: ✅ küçük additive. Risk: orta (yetki dokunuşu). Maliyet: düşük.

**8. Kayıt tipi yetkisi (Günlük Faaliyet)** — Bağımlılık: adım 7. Migration: ❌ (buton mekanizması).

**9. Personel birimi alanı** — Migration: ✅ additive. Etkisi: rapor doğruluğu + adım 16'nın önkoşulu.

**10. Günlük faaliyet mükerrer uyarısı** — Migration: ❌. Etkisi: veri kalitesi.

**11. Tam ekran parite denetimi (İŞ-19)** — *Neden burada:* Kritik işler bittikten sonra, ama büyük
işlere başlamadan önce. Yeni iş kalemleri doğurur.

**12. Ekran adı eşleme tablosu** — Bağımlılık: adım 11. Maliyet: çok düşük.

**13. Stok şube boyutu UYGULAMA — Faz 1 (şema ekle)** — Bağımlılık: adım 6 kararı. Migration: ✅ **riskli**.

**14. Stok şube — Faz 2 (çift yazım)** — Eski ve yeni birlikte yazılır, doğrulanır.

**15. Stok şube — Faz 3 (okuma geçişi)** — Okumalar şube bazlı olur. Geri dönüş planı hazır olmalı.

**16. Otomatik güncelleme davranışı** — Migration: ❌. Maliyet: düşük.

**17. Yumuşak kayıt uyarısı (soft lock)** — Bağımlılık: adım 4.

**18. Rapor envanteri + standart denetimi (İŞ-20)**

**19. Kullanıcı karar logu** — Bağımlılık: adım 9. Migration: ✅ yeni tablo.

**20. Bakım → onay → stok akışı** — Bağımlılık: adım 7. Migration: ✅ additive.
*Neden en sonda:* En büyük iş akışı değişikliği; öncesindeki her şeyin oturmuş olması gerekir.

---

## 25. SONRAKİ ADIMLAR (ilk 10, kısa)

1. Süper admin parolasını değiştir
2. Normal kullanıcıyla web girişini test et
3. Malzeme silmede stok/kullanım kontrolü ekle
4. Yakıt/stok belgeleri/muayene ekranlarına düzenleme kilidi ekle
5. Senkron aralığını akıllandır (boştayken yavaşla, boş push'u atla)
6. **Stok şube kararını ver** (SORU-1)
7. Yetki sistemine Approve/Cancel ekle
8. Günlük Faaliyet kayıt tipi yetkisini ekle
9. Personel birimi alanını ekle
10. Günlük Faaliyet mükerrer kayıt uyarısını ekle

---

## 26. BU DOSYANIN KULLANIMI

Bu dosya **kalıcı planlama referansıdır**. Her iş bittiğinde:
- İlgili satır §20 tablosunda güncellenir (durum sütunu eklenir),
- Yeni karar çıkarsa §23'e eklenir,
- Mimari değişirse §1 güncellenir.

**Geliştirme raporları buraya yazılmaz** — onlar `DEVAM.md` ve `docs/` altındadır.

**Çelişki durumunda öncelik:** kullanıcının son açık talebi > `docs/DEPOWISE_ANALYSIS.md` >
bu dosya > `CLAUDE.md` > mevcut kod.
