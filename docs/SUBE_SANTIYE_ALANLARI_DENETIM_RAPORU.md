# ŞUBE / ŞANTİYE ALANLARI DENETİM RAPORU

**Tarih:** 2026-08-08 · **Kapsam:** Web + Masaüstü + API + Veritabanı + Senkronizasyon
**Durum:** YALNIZ ANALİZ — kod değiştirilmedi, migration yapılmadı, deploy yapılmadı, canlı veriye dokunulmadı.

> Terim notu: *lookup/aranabilir liste* = yazdıkça filtreleyen açılır seçim kutusu ·
> *FK (foreign key / yabancı anahtar)* = "bu kayıt gerçekten var mı" diye veritabanının kendisinin
> denetlemesi · *DTO* = ekran ile sunucu arasında gidip gelen veri paketi.

---

## 1. GENEL DURUM

### 1.1 En önemli tespit: Şube ve Şantiye AYRI TABLO DEĞİL

Tek tablo var: **`branches`**, içinde **`kind`** kolonu (`Migration001_CoreSchema.cs:33`):

```sql
kind TEXT NOT NULL DEFAULT 'branch',   -- branch | site
```

Yani "Şube" = `kind='branch'`, "Şantiye" = `kind='site'`. Ayrı bir şantiye tablosu **yoktur** (arandı).

**Ama bu ayrım hiçbir seçim listesinde uygulanmıyor.** Hem masaüstü hem web, listeyi `kind` filtresi
olmadan çekiyor:

- Masaüstü: `LookupService.List(s,"branches")` → `SELECT ... FROM branches WHERE company_id=@c AND is_deleted=0` (`LookupService.cs:107`) — **kind yok**
- Web/API: `Organization.BranchService.List` → `WHERE b.company_id = @c AND b.is_deleted = 0` (`BranchService.cs:57`) — **kind yok**

→ **"Şube" etiketli alanlar şantiyeleri de, "Şantiye" etiketli alanlar şubeleri de gösteriyor.**
Senin "yanlış türün diğer alanlarda listelenmesine izin verilmemeli" kuralı **bugün sağlanmıyor.**

### 1.2 Sayısal özet

| Ölçüm | Sonuç |
|---|---|
| Bulunan Şube/Şantiye alanı (ekran bazlı) | **26** (masaüstü 15, web 11) |
| Serbest metin **seçim** alanı | **0** ✅ — hiçbir yerde yok |
| Listeden seçim (lookup/dropdown) | 22 ✅ |
| Salt-okunur gösterim (kendi şubesi) | 4 ✅ |
| **Yanlış tür karışması (kind filtresiz)** | **22 alanın tamamı** ❌ |
| **Alan üzerinden yeni tanım oluşturulabilen ekran** | **4** ❌ (masaüstü 1, web 2, içe aktarma 1) |
| Veritabanında FK ile korunan şube kolonu | **3 / 13** ❌ |
| Web ↔ masaüstü davranış farkı | **4 nokta** (§7) |

### 1.3 Senin 8 kuralına göre durum

| # | Kural | Durum |
|---|---|---|
| 1 | Serbest metin olmamalı | ✅ **Zaten sağlanıyor** |
| 2 | Gerçek tanımdan listelemeli | ✅ Sağlanıyor |
| 3 | Yalnız mevcut tanımlardan seçim | ⚠️ Genelde evet; 4 yerde yeni oluşturulabiliyor |
| 4 | "+ Yeni Tanım Ekle" bulunmamalı | ❌ **4 yerde var** |
| 5 | Ekleme yalnız tanım ekranından | ❌ İhlal (madde 4) |
| 6 | Yeni kayıt tüm listelerde görünmeli | ⚠️ Web'de anında; masaüstünde **yalnız eşitlemeden sonra** |
| 7 | Pasif kayıt davranışı | ⚠️ "Pasif" kavramı yok — sadece silme var (§3.3) |
| 8 | Yanlış tür listelenmemeli | ❌ **Hiçbir yerde uygulanmıyor** |

---

## 2. EKRAN BAZLI ENVANTER

### 2.1 MASAÜSTÜ

| Ekran | Alan | Kontrol | Veri kaynağı | Durum | Sorun | Öneri |
|---|---|---|---|---|---|---|
| Şube/Şantiye Tanımları (`BranchesView`) | Şube/Şantiye yönetimi | Liste + form (`kind` seçimi var) | `Organization.BranchService` | ✅ Doğru | — | Tek yetkili ekleme noktası olarak kalmalı |
| Giriş-Çıkış (`StockEntryView:40`) | Şube (Şubeniz) | `SelectableTextBlock` (salt-okunur) | Oturum şubesi | ✅ Doğru | — | — |
| Giriş-Çıkış (`:43`) | Kaynak Şube (Şubeniz) | Salt-okunur | Oturum şubesi | ✅ Doğru | — | — |
| Giriş-Çıkış (`:48`) | **Hedef Şube** | `ctrl:LookupBox` | `branches` (kind filtresiz) | ⚠️ | Şantiyeler de listeleniyor | `kind='branch'` filtresi |
| Günlük Faaliyet (`DailyActivityView:59`) | Kaynak Şube / Şantiye | `LookupBox` | `branches` | ✅ (etiket iki türü de kapsıyor) | — | — |
| Günlük Faaliyet (`:63`) | Hedef Şube / Şantiye | `LookupBox` | `branches` | ✅ | — | — |
| Günlük Faaliyet (`:165`) | **Hedef Şube** | `LookupBox` | `branches` | ⚠️ | Şantiye de geliyor | `kind='branch'` |
| **Araçlar (`VehiclesView:166-176`)** | **Şantiye / Şube** | `LookupBox` **+ "+" butonu** | `branches` | ❌ **HATALI** | Alan üzerinden **yeni şantiye oluşturulabiliyor** (`ConfirmAddBranch`) | **"+" kaldırılmalı** |
| Talepler (`RequestsView:41`) | **Şantiye** | `ComboBox` | `branches` | ⚠️ | Şubeler de listeleniyor | `kind='site'` filtresi |
| Talep Operasyonları (`:103`) | Gönderen Şube | `LookupBox` | `branches` | ⚠️ | Şantiye de geliyor | `kind='branch'` |
| Talep Operasyonları (`:107`) | Gönderilecek Şube | `LookupBox` | `branches` | ⚠️ | Aynı | Aynı |
| Personel (`PersonnelView:46`) | Şube | `LookupBox` | `branches` | ⚠️ | Şantiye de geliyor | Karar gerekir (§10-K2) |
| Kullanıcılar (`UsersView:47,99`) | Şube / Şube Ata | `ComboBox` | Firmanın şubeleri | ⚠️ | Şantiye de geliyor | Karar gerekir |
| Raporlar (`ReportsView:38`) | Şube (çoklu) | Çoklu seçim | `branches` + `btn-branch-select` | ⚠️ | Şantiye de geliyor | Etiket "Şube/Şantiye" olmalı ya da filtre |
| İçe/Dışa Aktarma (`ImportExportView`) | Şube/Şantiye sütunu | Excel metni → çözümleme | `ImportLookupResolver:88` | ❌ **HATALI** | İsim bulunamazsa **otomatik yeni şantiye yaratıyor** | Karar gerekir (§10-K4) |
| Giriş ekranı (`LoginWindow`) | Şube seçimi | Liste | `ListForLogin` | ✅ Doğru | — | — |

### 2.2 WEB

| Ekran | Alan | Kontrol | Veri kaynağı | Durum | Sorun | Öneri |
|---|---|---|---|---|---|---|
| Şube/Şantiye (`Branches.razor`) | Yönetim ekranı | Form + `LookupSelect` (üst şube) | `/api/branches` | ✅ Doğru | — | Tek yetkili ekleme noktası |
| Firmalar (`Companies.razor:41`) | İlk Şube / Şantiye Adı | `MudTextField` (serbest metin) | — | ✅ **Kabul edilebilir** | Yeni firma açılırken hiç şube yok, yazılması zorunlu | Dokunulmamalı |
| Giriş-Çıkış (`Stock.razor:88,93`) | Kaynak Şube / Şube (Şubeniz) | `MudTextField` **ReadOnly** | Oturum şubesi | ✅ Doğru | — | — |
| Giriş-Çıkış (`Stock.razor`) | Hedef Şube | `MudSelect` | `/api/branches` | ⚠️ | Şantiye de geliyor | `kind='branch'` |
| **Araçlar (`Vehicles.razor:84`)** | **Şantiye / Şube \*** | `LookupSelect` **+ `CreatePath="/api/branches"`** | `/api/branches` | ❌ **HATALI** | Alan üzerinden **yeni şube oluşturulabiliyor** | `CreatePath` kaldırılmalı |
| **Talepler (`Requests.razor:27`)** | **Şantiye \*** | `LookupSelect` **+ `CreatePath="/api/branches"`** | `/api/branches` | ❌ **HATALI** | Aynı | `CreatePath` kaldırılmalı |
| Günlük Faaliyet (`Daily.razor:42,43`) | Kaynak/Hedef Şube / Şantiye | `LookupSelect` (CreatePath **yok**) | `/api/branches` | ✅ | — | — |
| Günlük Faaliyet (`Daily.razor:62`) | Hedef Şube \* | `LookupSelect` | `/api/branches` | ⚠️ | Şantiye de geliyor | `kind='branch'` |
| Talep Operasyonları (`:86,87`) | Gönderen / Gönderilecek Şube | `LookupSelect` (CreatePath yok) | `/api/branches` | ⚠️ | Şantiye de geliyor | `kind='branch'` |
| Personel (`Personnel.razor:59`) | Şube | `LookupSelect` | `/api/branches` | ⚠️ | Şantiye de geliyor | Karar gerekir |
| Kullanıcılar (`Users.razor`) | Şube Ata | `MudSelect` | `/api/branches` | ⚠️ | Aynı | Karar gerekir |
| Raporlar (`Reports.razor`) | Şube / Şantiye (çoklu) | `MudSelect` çoklu | `/api/branches` | ✅ Etiket doğru | — | — |

**Sonuç:** Senin en çok endişelendiğin madde — **serbest metin** — hiçbir seçim alanında yok. Asıl
problemler: **tür karışması**, **alan üzerinden tanım oluşturma** ve **senkronizasyon** (§6).

---

## 3. VERİ MODELİ

### 3.1 `branches` tablosu

```sql
id TEXT PRIMARY KEY, company_id TEXT NOT NULL, parent_id TEXT NULL,
name TEXT NOT NULL, kind TEXT NOT NULL DEFAULT 'branch',   -- branch | site
created_at, updated_at, version, is_deleted
+ code TEXT, password_hash TEXT            (Migration024)
```

`parent_id` ile hiyerarşi mümkün (şantiye bir şubeye bağlanabilir) — **ama seçim ekranlarında bu ilişki
hiç kullanılmıyor.**

### 3.2 Şubeye referans veren kolonlar ve FK durumu

| Tablo.kolon | FK var mı |
|---|---|
| `branches.parent_id` | ✅ Var |
| `personnel.branch_id` | ✅ Var (`Migration004:32`) |
| *(Migration004'teki ikinci tablo)*`.branch_id` | ✅ Var (`Migration004:43`) |
| `vehicles.branch_id` | ✅ Var (`Migration007:88`) |
| `users.branch_id` | ❌ **Yok** |
| `materials.branch_id` | ❌ **Yok** |
| `stock_movements.branch_id` | ❌ **Yok** |
| `stock_movements.branch_from_id` | ❌ **Yok** |
| `stock_documents.from_branch_id` | ❌ **Yok** |
| `stock_documents.to_branch_id` | ❌ **Yok** |
| `material_requests.branch_id` | ❌ **Yok** |
| `material_requests.ops_from_branch_id` / `ops_to_branch_id` | ❌ **Yok** |
| `*.op_branch_id` (Migration027 — çok tabloya eklendi) | ❌ **Yok** |
| `request_status_history.op_branch_id` | ❌ **Yok** |
| `sync_devices.branch_id` | ❌ **Yok** |

**Önemli:** Hepsi **kimlik (id) saklıyor** — yani "isim yerine metin saklama" sorunu **yok**. Eksik olan
şey **FK ile korunma**. FK olmadığı için var olmayan bir şube kimliği yazılabilir ve veritabanı itiraz etmez.

### 3.3 "Pasif" kavramı — **YOK**

`branches` tablosunda `is_active` **yoktur**; yalnız `is_deleted` vardır. `BranchService.Delete`
(`:146-158`) kaydı `is_deleted=1` yapar (yumuşak silme).

| Soru | Cevap |
|---|---|
| Pasifleştirilen şube seçim listelerinde çıkar mı? | **Hayır** — tüm listeler `is_deleted=0` filtreli |
| Geçmiş kayıtlar bozulur mu? | **Hayır** — kimlik saklandığı ve fiziksel silme olmadığı için geçmiş korunur |
| Ama geçmiş kayıtta şube adı görünür mü? | ⚠️ **Ekrana göre değişir** — liste ile eşleştiren ekranlarda silinmiş şube adı **boş/"—"** görünebilir. (Doğrulanması gereken nokta — §10-K5) |

Yani senin "pasif" dediğin şey bugün **silme** ile aynı şey. Ayrı bir "pasif ama seçilemez" durumu yok.

---

## 4. API / DTO KONTROLÜ

| Ölçüm | Sonuç |
|---|---|
| `BranchId` içeren DTO alanı | **43** |
| `FromBranchId` / `ToBranchId` | **7 + 7** |
| `BranchName` (isim taşıyan) | **4** — ve hepsi **yalnız görüntüleme/rapor çıktısı** (`Program.cs:2068, 2321`) |

✅ **Sonuç: API tarafında şube her zaman kimlikle taşınıyor.** İsimle taşıma yalnız ekranda gösterim
amaçlı; kayıt oluştururken kullanılmıyor.

**Uçlar:**
- `GET /api/branches` → `Branches.List` (kind filtresi **yok**)
- `POST /api/branches` → `Branches.Create`; `Kind` boş gelirse **`"branch"`** varsayılır (`Program.cs:1023`)
- `PUT` / `DELETE /api/branches/{id}` → yönetim uçları

---

## 5. YETKİ KONTROLÜ

### 5.1 Normal yol (doğru)

`Organization.BranchService` — `Create` → `branches/Create`, `Update` → `branches/Edit`,
`Delete` → `branches/Delete`. Ve `branches` modülü **admin-kısıtlı**: `AppModules.IsAdminRestricted`
(`AccessControl.cs:114-115`) → alt rollere (Personel) verilemez.

### 5.2 🔴 KRİTİK BULGU — masaüstünde yetki atlatma

Masaüstündeki "+" butonu `Organization.BranchService`'i **kullanmıyor**; `LookupService`'i kullanıyor:

```csharp
// LookupService.cs:43
public string AddBranch(SessionContext s, string name) => Insert(s, "branches", name, ("kind", "site"));
// LookupService.cs:157-159
private string Insert(...) { AccessControl.Require(s, Module, PermissionAction.Create); ... }
// LookupService.cs:17
private const string Module = "definitions";
```

→ **`definitions/Create` yetkisi olan bir kullanıcı, admin-kısıtlı `branches` modülüne hiç sahip
olmadan Şantiye oluşturabiliyor.** "Tanım Düzenle" yetkisi verilmiş normal bir personel bunu yapabilir.

Aynı açık **içe aktarmada** da var: `ImportLookupResolver.cs:88` → `_lookups.AddBranch(...)` ile Excel'den
gelen tanınmayan şube/şantiye adı için **otomatik yeni kayıt** üretiliyor.

### 5.3 Web tarafı

Web'in `CreatePath="/api/branches"` yolu **doğru servisi** çağırır → `branches/Create` ister. Yani
web'de yetkisiz kullanıcı **403** alır (buton görünür ama işlem başarısız olur) — güvenlik açığı değil,
ama kafa karıştırıcı ve senin kuralına aykırı.

### 5.4 İki platform, iki farklı `kind`

| Platform | Alan üzerinden oluşturulan kayıt |
|---|---|
| Masaüstü ("+") | **`kind='site'`** (Şantiye) |
| Web (`CreatePath`) | **`kind='branch'`** (Şube — varsayılan) |

Aynı ekran (Araçlar), aynı alan, **iki farklı tür** üretiyor.

---

## 6. SENKRONİZASYON KONTROLÜ

### 6.1 Şubeler tek yönlü akıyor

- **Sunucu → Masaüstü:** `LookupSyncService.cs:61,98` → `Upsert(conn, tx, "branches", ...)` (kind ve
  parent_id dahil).
- **Masaüstü → Sunucu:** `branches` **push listesinde YOK**. `BusinessSyncService.cs:29`:
  > "NOT: branches PUSH'a dahil DEĞİL (web-otoriteli; kod/şifre taşır) — sunucuda zaten var."

### 6.2 🔴 Bunun sonucu (kod kanıtına dayalı, testle doğrulanmalı)

Masaüstündeki "+" ile oluşturulan şantiye **yalnız o bilgisayarda** kalır:

1. Sunucuya **hiç gitmez** → web'de ve diğer makinelerde **görünmez**.
2. O şantiyeye atanan **araç** ise push listesindedir (`vehicles`) ve `branch_id` ile sunucuya gider.
3. Sunucuda `vehicles.branch_id` → `branches(id)` **FK'si vardır** (`Migration007:88`) ve PostgreSQL
   FK'yi her zaman zorlar → **o aracın satırı sunucuda reddedilir (FK ihlali)** ve senkron "skipped"
   olarak atlar.

→ Yani masaüstünden eklenen bir şantiyeye araç bağlanırsa, **o araç sunucuya hiç ulaşmayabilir.**
Bu, daha önce yaşanan "araçlar sunucuya ulaşmıyor" tipi şikâyetlerle **aynı desende** bir risktir.
**Bu bir hipotezdir; hedefli bir testle kanıtlanmalıdır (§10-K1).**

### 6.3 Yeni şube ne zaman listelerde görünür?

| Nereden eklendi | Web'de | Masaüstünde |
|---|---|---|
| Web / Şube Tanımları | **Anında** | **Bir sonraki tanım eşitlemesinden sonra** (giriş veya "Eşitle") |
| Masaüstü "+" (hatalı yol) | **Hiç görünmez** | Yalnız o makinede |

---

## 7. WEB ↔ MASAÜSTÜ KARŞILAŞTIRMASI

| # | Nokta | Masaüstü | Web | Fark |
|---|---|---|---|---|
| 1 | Araçlar ekranında şube oluşturma | "+" butonu → `LookupService` → `definitions/Create` → `kind='site'` | `CreatePath` → `/api/branches` → `branches/Create` → `kind='branch'` | **Farklı yetki + farklı tür** |
| 2 | Talepler ekranında şube oluşturma | Yok (yalnız `ComboBox`) | **Var** (`CreatePath`) | Web'de fazladan |
| 3 | Yeni şubenin görünürlüğü | Eşitleme sonrası | Anında | Gecikme |
| 4 | Talepler ekranı kontrolü | `ComboBox` | `LookupSelect` (aranabilir) | Kullanım farkı |
| 5 | Kind filtresi | Yok | Yok | **Aynı şekilde hatalı** (fark değil) |

---

## 8. RİSKLER

| # | Risk | Seviye | Gerekçe |
|---|---|---|---|
| R1 | **Masaüstünde oluşturulan şube sunucuya gitmez; ona bağlı araç senkronda reddedilebilir** | 🔴 **Kritik** | §6.2 — sessiz veri kaybı görüntüsü |
| R2 | **`definitions/Create` ile admin-kısıtlı şube tanımı oluşturma (yetki atlatma)** | 🔴 **Kritik** | §5.2 |
| R3 | **Aynı isimli mükerrer şube/şantiye** | 🟠 Yüksek | Benzersizlik kısıtı **yok**; "+" ve içe aktarma kolayca kopya üretir |
| R4 | **Yanlış tür seçimi** (Şube alanında şantiye seçmek) | 🟠 Yüksek | §1.1 — hiçbir yerde filtre yok |
| R5 | **İçe aktarma sırasında sessizce yeni şantiye yaratılması** | 🟠 Yüksek | `ImportLookupResolver.cs:88` — yazım hatası yeni kayıt doğurur |
| R6 | **FK'siz 10 kolon** → var olmayan şube kimliği yazılabilir | 🟡 Orta | §3.2 |
| R7 | **Raporların yanlış şube üzerinden filtrelenmesi** | 🟡 Orta | R4'ün sonucu; rapor şube seçicisi de karışık liste kullanıyor |
| R8 | Silinmiş şubenin geçmiş kayıtlarda adının boş görünmesi | 🟡 Orta | §3.3 — doğrulanmalı |
| R9 | İki platformun farklı `kind` üretmesi | 🟡 Orta | §5.4 — veri tutarsızlığı |
| R10 | Geçmiş kayıtların bozulması | 🟢 Düşük | **Bugün risk yok** — yumuşak silme + kimlik saklama |
| R11 | Serbest metin nedeniyle yanlış şube girilmesi | 🟢 **Yok** | Serbest metin seçim alanı bulunmadı |

---

## 9. ÖNERİLEN DÜZELTME SIRASI

### 🔴 Kritik (önce)
1. **Araçlar (masaüstü + web) ve Talepler (web) ekranlarındaki şube oluşturma imkânını kaldır.**
   → R1 + R2 + R3'ü aynı anda kapatır. **Migration gerekmez, veri dönüşümü gerekmez.**
2. **`LookupService.AddBranch`'i devre dışı bırak** (yetki atlatma yolunu kapat). → R2.

### 🟠 Yüksek
3. **`kind` filtresi**: "Şube" alanları `branch`, "Şantiye" alanları `site` göstersin; iki türü birden
   kapsayan alanlar ("Şube / Şantiye") olduğu gibi kalsın. → R4 + R7. *(Karar gerekir — §10-K2)*
4. **İçe aktarmada otomatik şube/şantiye yaratmayı durdur**; eşleşmeyen satırı hata olarak raporla.
   → R5. *(Karar gerekir — §10-K4)*

### 🟡 Orta
5. **Aynı firmada aynı isim+tür için benzersizlik** kontrolü. → R3. **Bu bir migration ister** ve mevcut
   veride kopya varsa temizlik gerektirir → **canlı veri analizi şart** (§10-K3).
6. **FK ekleme** (10 kolon). → R6. **Migration ister**; mevcut veride sahipsiz kimlik varsa FK eklenemez
   → önce salt-okuma denetimi gerekir (§10-K6).
7. Silinmiş şubenin geçmişte nasıl göründüğünü netleştir (§10-K5).

### 🟢 Düşük
8. Masaüstü Talepler ekranındaki `ComboBox`'ı web ile aynı aranabilir kontrole çevir (tutarlılık).
9. Etiket birliği: "Şube", "Şantiye", "Şube / Şantiye" adlandırmasını tek kurala bağla.

**Canlı veriye dokunacak adımlar: yalnız 5 ve 6.** 1–4 ve 8–9 **kod-içi**, migration gerektirmez.

---

## 10. KODLAMA ÖNCESİ KARAR LİSTESİ

| # | Karar | Seçenekler | Önerim |
|---|---|---|---|
| **K1** | §6.2'deki senkron riski (masaüstü şubesi + araç) önce **testle kanıtlansın mı**? | (a) Evet, hedefli test yaz · (b) Hayır, doğrudan düzelt | **(a)** — hem kanıt olur hem gerileme koruması |
| **K2** | Şube/Şantiye ayrımı ekranlarda **nasıl uygulansın**? | (a) Etikete göre katı filtre (Şube→`branch`, Şantiye→`site`) · (b) Hepsi iki türü de göstersin, etiketler "Şube / Şantiye" olarak birleştirilsin · (c) Şimdilik dokunma | **(a)** — ama **iş kuralını sen doğrulamalısın**: örneğin bir *araç* şantiyeye mi şubeye mi bağlanır, *talep* hangisinden açılır? Bunu koddan çıkaramıyorum. |
| **K3** | Aynı isimli şube/şantiye engellensin mi? | (a) Evet (firma+tür+isim benzersiz, migration) · (b) Yalnız uyarı ver · (c) Hayır | **(b) sonra (a)** — önce canlıda kopya var mı bakılmalı |
| **K4** | İçe aktarmada tanınmayan şube/şantiye | (a) Hata ver, satırı atla · (b) Otomatik oluştur (bugünkü) · (c) Kullanıcıya eşleştirme ekranı | **(a)** |
| **K5** | Silinen şubenin geçmiş kayıtlarda görünümü | (a) Adı "(silinmiş) X" olarak gösterilsin · (b) Boş kalsın (bugünkü) | **(a)** |
| **K6** | FK ekleme (10 kolon) | (a) Faz olarak planla (önce salt-okuma denetimi) · (b) Şimdilik erteleme · (c) Hiç ekleme | **(a)** — ama Faz 3'ten sonra |
| **K7** | Sıra | Önce kritik 1–2 (migration'sız, düşük risk), sonra K2 kararına göre 3–4 | Onayına sunuldu |

### Sana özellikle sormam gereken iş kuralı (koddan çıkaramadım)

> **Araç, Talep, Personel, Kullanıcı ve stok hareketleri "Şube"ye mi, "Şantiye"ye mi, yoksa ikisine de
> bağlanabilir mi?** Bugün sistem hepsinde ikisini birden gösteriyor ve hangisinin doğru olduğuna dair
> kodda veya belgelerde bir kural yok. K2 kararı buna bağlı.

---

## 11. BU AŞAMADA YAPILMAYANLAR

Kod değiştirilmedi · migration çalıştırılmadı · deploy yapılmadı · canlı veriye dokunulmadı ·
doğru çalışan hiçbir alana müdahale edilmedi.
