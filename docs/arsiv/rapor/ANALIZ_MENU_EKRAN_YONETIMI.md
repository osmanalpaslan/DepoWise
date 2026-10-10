# ANALİZ — Web'e özel "Menü / Ekran Yönetimi" ekranı

**Tarih:** 2026-08-18 · **Durum:** AŞAMA 1–2 bitti (envanter + karar önerisi). **Kod yazılmadı.**

---

## 1. MEVCUT MİMARİ (koddan doğrulandı)

### 1.1 Ekran kaydı (screen registry) — ZATEN VAR
`src/DepoWise.Application/Security/AppScreens.cs`

- `AppScreen(Key, ModuleKey, Group, Label, Platforms, WebRoute, DesktopNavKey, WebPermOverride)` — **59 ekran**.
- `AppScreens.Groups` → `AppScreenGroup(Title, DesktopIcon, ModuleKey)` — **17 grup**.
- Yorumunda açıkça yazıyor: *"yeni ekran = buraya TEK SATIR"*. Menüler bundan **üretilir**.
- **Reflection bilinçli olarak kullanılmıyor** (deny-by-default'u zayıflatmamak için).
- **Grup kimliği = `Title` metnidir.** `AppScreen.Group` grubu başlığıyla referans verir.

### 1.2 Menü üretimi — iki platform da kataloğu kullanıyor

| Katman | Dosya | Nasıl |
|---|---|---|
| Web menüsü | `Web/Components/Layout/NavMenu.razor:88` | `AppScreens.GroupsFor(Web)` + `ScreensOf(...)` |
| Masaüstü menüsü | `Desktop/ViewModels/ShellViewModel.cs:682` (`BuildGroups`) | `AppScreens.GroupsFor(Desktop)` + `ScreensOf(...)` |
| Web route koruması | `Web/Components/Layout/MainLayout.razor:68` | `Auth.PlatformOpenForRoute` |
| Masaüstü gezinme koruması | `ShellViewModel.cs:752` | `ScreenVisibility.IsEnabled(...)` |

`AppScreensParityTests` (S1–S16) iki menünün de gerçekten katalogdan beslendiğini kilitliyor.

### 1.3 Platform görünürlüğü — **ZATEN VAR (G5, 2026-08-12)**

> ⚠️ Kullanıcı isteğinin **5. ve 6. maddesi zaten yapılmış durumda.**

- `ScreenPlatform` enum: `None / Desktop / Web / Both` → istenen 4 durumun **birebir** karşılığı.
- `ScreenVisibility.cs` — çözümleyici. Kural: **yalnız DARALTIR**, katalogda olmayanı açamaz.
- `Migration065_ScreenPlatformVisibility` → tablo `screen_platform_visibility`
  (`company_id, screen_key, platform, enabled`), **firma bazlı**, satır yoksa katalog varsayılanı.
- `ScreenVisibilityService` — TTL önbellek + yazmada anında düşürme + **audit**.
- API: `GET /api/screens/visibility` · `GET /api/screens/visibility/manage` · `POST /api/screens/visibility`.
- Web ekranı: **`/screen-visibility` → "Ekran Platform Yönetimi"** (`AppScreens` içinde kayıtlı, `W` platformu).
- Yetki: modül `screen_visibility`, `AppModules.IsSuperAdminOnly` → **devredilemez**, süper admin.

### 1.4 Yetki sistemi

- `AppModules.All` (modül kataloğu) + üç kısıt katmanı: `IsSuperAdminOnly` / `IsAdminRestricted` / `IsExplicitOnly`.
- `AccessControl.Can/Require`, `PermissionSnapshotCache` (90 sn).
- Web menüsünde **geçiş dönemi sözde-anahtarları** var: `@admin`, `@super`, `@superr`, `""` (`WebPermOverride`).
- Kural (`ScreenVisibility.cs`): **ERİŞİM = PLATFORM_AKTİF && YETKİ_VAR.** Platform yetki vermez/bypass etmez.

### 1.5 Masaüstüne yapılandırma taşıma yolu — MEVCUT

`Desktop/LookupSyncService.PullAsync()` → girişte `GET /api/lookups/sync` çekip yerel SQLite'a upsert eder;
**çevrimdışıysa sessizce atlar**. Tanımlar (birim, tedarikçi, marka, şube…) masaüstüne böyle iniyor.

---

## 2. BULUNAN HATA (mevcut, bu analizde çıktı)

### MNU-B1 — "Masaüstü" kutusu gerçek masaüstü makinelerinde ETKİSİZ 🔴

**Kanıt:**

- `screen_platform_visibility` tablosu `BusinessSyncService.Tables` listesinde **YOK**
  (grep ile doğrulandı; listede 30+ tablo var, bu yok).
- `/api/lookups/sync` yanıtında da **YOK** (`Program.cs:1223-1232` — units/suppliers/…/branches).
- Masaüstü `ScreenVisibilityService.OverridesFor()` **yerel SQLite'tan** okur (`DesktopServices.Factory`).

**Sonuç:** yönetici web'den bir ekranı *masaüstünde kapat* dediğinde, o kayıt **sunucu veritabanında kalır**;
babanın makinesindeki yerel tabloya hiç ulaşmaz → yerelde tablo daima boş → katalog varsayılanı geçerli →
**ekran masaüstünde açık kalmaya devam eder.** G5'in web yarısı çalışıyor, masaüstü yarısı çalışmıyor.

**Çözüm (yeni mimari GEREKTİRMEZ):** `/api/lookups/sync` yanıtına bir bölüm + `LookupSyncService`'e bir
upsert. Çevrimdışı davranış korunur: en son inen ayar yerelde durur, hiç inmediyse katalog varsayılanı.

---

## 3. İSTENEN 7 MADDENİN DURUMU

| # | İstek | Durum |
|---|---|---|
| 1 | Ekran sırası değiştirme | ❌ yok (sıra = katalog sırası, sabit) |
| 2 | Ekranı başka üst menüye taşıma | ❌ yok |
| 3 | Üst menü adını değiştirme | ❌ yok |
| 4 | Üst menü sırasını değiştirme | ❌ yok |
| 5 | Platform (Web/Desktop/İkisi/Hiçbiri) | ✅ **VAR** (G5) — ama masaüstü yarısı MNU-B1 nedeniyle etkisiz |
| 6 | Menüde aktif/pasif | ✅ **VAR** (ikisi de kapalı = "Kapalı") |
| 7 | Görünen ekran adını değiştirme | ❌ yok (`Label` sabit) |

**Eksik olan yalnız "menü düzeni" (yerleşim): ad · grup · sıra.** Platform işi bitmiş.

---

## 4. BÜYÜK REFACTOR GEREKİYOR MU? → **HAYIR**

Tek gerçek mimari engel şuydu: *grup kimliği `Title` metni; adı değişirse kimlik kayar.*

**Çözüm (0 satır katalog refactoru):** `AppScreenGroup.Title` **değişmez SİSTEM ANAHTARI** kabul edilir
(katalogda zaten hiç değişmiyor ve her yerde kimlik olarak kullanılıyor); kullanıcının verdiği ad
**ayrı bir alanda** (`title_override`) durur. Böylece:

- `AppScreen.Group` referansları **aynen kalır**,
- `NavMenu.WebIcon(groupTitle)` ve `ShellViewModel` **aynen çalışır** (anahtara bakıyorlar, gösterilen ada değil),
- parite testleri (S3/S13/S14) **bozulmaz** (katalog varsayılanı değişmiyor).

→ Yeni bir navigation sistemi, yeni permission sistemi, yeni sync protokolü, yeni cache **gerekmiyor.**

---

## 5. ÖNERİLEN UYGULAMA (en küçük müdahale)

1. **Migration 070** — `screen_menu_layout` (firma · ekran: `label_override`, `group_key_override`, `sort_order`)
   + `menu_group_layout` (firma · grup: `title_override`, `sort_order`, `visible`).
   Satır yoksa **katalog varsayılanı** → migration sonrası hiçbir şey değişmez (§18 kuralı).
2. **`MenuLayout` çözümleyici** (Application) — `ScreenVisibility` ile aynı desen, saf mantık.
3. **`MenuLayoutService`** (Infrastructure) — `ScreenVisibilityService`'in birebir deseni:
   TTL önbellek + yazmada düşürme + audit + tek transaction (toplu kaydet).
4. **API** — mevcut `/api/screens/...` ailesine 2 uç: `GET /api/screens/layout/manage`,
   `POST /api/screens/layout` (toplu/atomik). Mevcut `S(c)` + `AccessControl.Require` kapısı.
5. **Web** — mevcut `/screen-visibility` ekranı **genişletilir** (bkz. §6, K-1).
6. **MNU-B1 düzeltmesi** — `/api/lookups/sync` + `LookupSyncService`'e platform **ve** yerleşim bölümü.
7. **Testler** — mevcut `ScreenPlatformVisibilityTests` desenine yeni sınıf; §25'teki 25 senaryo.

---

## 6. KARAR GEREKTİREN 2 NOKTA (kullanıcı onayı bekliyor)

**K-1 — Tek ekran mı, iki ekran mı?**
Zaten `/screen-visibility` "Ekran Platform Yönetimi" var ve aynı satır kümesini (59 ekran) yönetiyor.

- (A) **Önerilen:** o ekran genişletilip adı **"Menü / Ekran Yönetimi"** yapılır → tek yer, tek kaynak.
- (B) Ayrı `/menu-management` ekranı → aynı ekranlar iki yerde yönetilir (§2 "iki farklı yerde tanımlama" riski).

**K-2 — Masaüstü ne kadar etkilensin?**

- (A) **Önerilen:** MNU-B1 düzeltilir; platform **ve** menü düzeni masaüstüne de iner (mevcut lookup senkronuyla,
  çevrimdışı davranış korunarak).
- (B) Yalnız web etkilenir; MNU-B1 ayrı iş olarak bırakılır (masaüstü kutusu etkisiz kalmaya devam eder).

---

## 7. AÇIK RİSKLER

- `WebPermOverride` sözde-anahtarları (`@admin`/`@super`/`@superr`) korunacak; menü düzeni bunlara dokunmaz.
- Korumalı ekranlar: `users`, `permissions`, `screen_visibility` (kendisi) — gizlenirse yönetici kendini
  kilitleyebilir. §14 gereği kilit uygulanacak, **koddan kanıtlanarak** (bu üçü kilitlenir, rastgele ekran değil).
- Üretim: bu turda **hiçbir şey yapılmayacak** (§28).
