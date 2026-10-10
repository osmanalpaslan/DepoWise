# ŞUBE / ŞANTİYE — DOĞRULAMA VE UYGULAMA PLANI

**Tarih:** 2026-08-09 · **Ürün adı:** Alpnex (repo/klasör/namespace/geçici adreslere **dokunulmadı**)
**Durum:** YALNIZ ANALİZ VE PLAN — kod değiştirilmedi, migration yok, deploy yok, canlı veri değiştirilmedi.
**Önceki rapor:** [SUBE_SANTIYE_ALANLARI_DENETIM_RAPORU.md](SUBE_SANTIYE_ALANLARI_DENETIM_RAPORU.md)

> Canlı veritabanına **yalnız okuma** amaçlı bağlanıldı: `SET TRANSACTION READ ONLY`, kanıt amaçlı bir
> `UPDATE` PostgreSQL tarafından **25006** ile reddedildi, işlem `ROLLBACK` ile kapatıldı.

---

## 1. MEVCUT DURUM DOĞRULAMASI

| # | Önceki rapordaki tespit | Doğrulama | Sonuç |
|---|---|---|---|
| 1 | `branches.kind = branch \| site` var | `Migration001_CoreSchema.cs:33` | ✅ **Doğru** |
| 2 | Ayrı "şantiye" tablosu yok | Migration taraması | ✅ **Doğru** |
| 3 | Seçim listelerinde `kind` filtresi yok | `LookupService.cs:107` · `Organization/BranchService.cs:57` · `ListForLogin:254` | ✅ **Doğru — üç yerde de yok** |
| 4 | Masaüstü Araçlar'da "+" ile oluşturma | `VehiclesView.axaml:172` → `ConfirmAddBranch` → `LookupService.AddBranch` | ✅ **Doğru** |
| 5 | Web Araçlar `CreatePath="/api/branches"` | `Vehicles.razor:84` | ✅ **Doğru** |
| 6 | Web Talepler aynı mekanizma | `Requests.razor:27` | ✅ **Doğru** |
| 7 | Excel içe aktarma otomatik oluşturuyor | `ImportLookupResolver.cs:85-88` + `Resolve():57-60` | ✅ **Doğru** |
| 8 | Yetki açığı (`definitions/Create`) | `LookupService.cs:17,43,157-159` + `EnsureKnownTable:302` ("branches" izinli) + `AppModules:57` (`definitions` **admin-kısıtlı değil**) vs `AccessControl.cs:115` (`branches` **admin-kısıtlı**) | ✅ **Doğru — gerçek açık** |
| 9 | Masaüstü `site`, web `branch` üretiyor | `LookupService.cs:43` (`("kind","site")`) vs `Program.cs:1023` (boşsa `"branch"`) | ✅ **Doğru** |
| 10 | Şubeler masaüstünden sunucuya gönderilmiyor | `BusinessSyncService.cs:29-56` (listede yok) · `LookupSyncService.cs:61,98` (yalnız çekme) | ✅ **Doğru** |
| 11 | "13 kolondan yalnız 3'ünde FK" | **Canlı veritabanı taraması** | ⚠️ **DÜZELTME: 4 FK var** (aşağıda) |

### 1.1 Düzeltme — FK sayısı 3 değil 4

Canlı veritabanında `branches(id)`'e işaret eden yabancı anahtarlar:

```
branches.parent_id · personnel.branch_id · user_scopes.branch_id · vehicles.branch_id
```

Önceki raporda `user_scopes.branch_id` "Migration004'teki ikinci tablo" olarak belirsiz bırakılmıştı;
canlıda **adıyla doğrulandı**. Diğer tespitler aynen geçerli.

### 1.2 Senkron riski hakkında dürüst not

Kullanıcı talimatı gereği bu aşamada **kod yazılmadığı için hedefli test koşulamadı**. Bu nedenle
"masaüstünde oluşturulan şube + ona bağlı araç → sunucuda FK reddi" senaryosu **kanıtlanmış değil,
kod okumasına dayalı güçlü bir beklentidir** (§6). Canlı veride bugün **0 yetim kayıt** var; bu iki
şekilde açıklanabilir ve ikisi ayırt edilemiyor: (a) senaryo hiç yaşanmadı, (b) yaşandı ve ilgili
satırlar sunucuda sessizce atlandı. **Ayırt etmek için test gerekir (§9-K1).**

---

## 2. ŞUBE/ŞANTİYE ALANLARININ TAM LİSTESİ

**Toplam 26 alan** (masaüstü 15 · web 11) + 1 arka plan alanı (`user_scopes`).

### 2.1 Masaüstü (15)

| # | Ekran | Alan | Kontrol | Kaynak |
|---|---|---|---|---|
| M1 | Şube/Şantiye Tanımları | Yönetim (ad + tür + üst) | Form | `Organization.BranchService` |
| M2 | Giriş ekranı (`LoginWindow`) | Şube seçimi | Liste | `ListForLogin` (kind filtresiz) |
| M3 | Giriş-Çıkış (`:40`) | Şube (Şubeniz) | Salt-okunur | Oturum |
| M4 | Giriş-Çıkış (`:43`) | Kaynak Şube (Şubeniz) | Salt-okunur | Oturum |
| M5 | Giriş-Çıkış (`:48`) | Hedef Şube | LookupBox | `branches` |
| M6 | Günlük Faaliyet (`:59`) | Kaynak Şube / Şantiye | LookupBox | `branches` |
| M7 | Günlük Faaliyet (`:63`) | Hedef Şube / Şantiye | LookupBox | `branches` |
| M8 | Günlük Faaliyet (`:165`) | Hedef Şube | LookupBox | `branches` |
| M9 | **Araçlar (`:166`)** | **Şantiye / Şube** | LookupBox **+ "+"** | `branches` |
| M10 | Talepler (`:41`) | Şantiye | ComboBox | `branches` |
| M11 | Talep Operasyonları (`:103`) | Gönderen Şube | LookupBox | `branches` |
| M12 | Talep Operasyonları (`:107`) | Gönderilecek Şube | LookupBox | `branches` |
| M13 | Personel (`:46`) | Şube | LookupBox | `branches` |
| M14 | Kullanıcılar (`:47,99`) | Şube / Şube Ata | ComboBox | `branches` |
| M15 | Raporlar (`:38`) | Şube (çoklu) | Çoklu seçim | `branches` + `btn-branch-select` |
| M16 | İçe/Dışa Aktarma | Excel "Şube/Şantiye" sütunu | Metin → çözümleme | `ImportLookupResolver` |

### 2.2 Web (11)

| # | Ekran | Alan | Kontrol | Kaynak |
|---|---|---|---|---|
| W1 | Şube/Şantiye (`Branches.razor`) | Yönetim + üst şube | Form + LookupSelect | `/api/branches` |
| W2 | Firmalar (`:41`) | İlk Şube / Şantiye Adı | Serbest metin | — (yeni firma açılışı) |
| W3 | Giriş-Çıkış (`Stock:88`) | Kaynak Şube (Şubeniz) | **ReadOnly** | Oturum |
| W4 | Giriş-Çıkış (`Stock:93`) | Şube (Şubeniz) | **ReadOnly** | Oturum |
| W5 | Giriş-Çıkış (`Stock`) | Hedef Şube | MudSelect | `/api/branches` |
| W6 | **Araçlar (`:84`)** | **Şantiye / Şube \*** | LookupSelect **+ CreatePath** | `/api/branches` |
| W7 | **Talepler (`:27`)** | **Şantiye \*** | LookupSelect **+ CreatePath** | `/api/branches` |
| W8 | Günlük Faaliyet (`:42,43`) | Kaynak/Hedef Şube / Şantiye | LookupSelect | `/api/branches` |
| W9 | Günlük Faaliyet (`:62`) | Hedef Şube \* | LookupSelect | `/api/branches` |
| W10 | Talep Operasyonları (`:86,87`) | Gönderen / Gönderilecek Şube | LookupSelect | `/api/branches` |
| W11 | Personel (`:59`) · Kullanıcılar · Raporlar | Şube / Şube Ata / Şube-Şantiye (çoklu) | LookupSelect / MudSelect | `/api/branches` |

---

## 3. HER ALAN İÇİN `branch / site / both / readonly` DEĞERLENDİRMESİ

### 3.1 🔴 ÖNCE KRİTİK UYARI — "Şube → branch" filtresi sistemi BOZAR

Canlı veriden ölçüm:

| `kind` | Aktif adet | Örnekler |
|---|---|---|
| `branch` | **1** | ANKARA GENEL MERKEZ |
| `site` | **5** | KARAMAN, DÜZCE, NEVŞEHİR, TEST ŞANTİYE, (2. firma KARAMAN) |

Ve **operasyonel kayıtların TAMAMI `site`'a bağlı** (§8 tablosu): 94 aracın 94'ü, 6 kullanıcının 6'sı,
tüm stok hareketleri, tüm belgeler, yakıt kayıtları… **`branch`'e bağlı tek bir kayıt bile yok.**

→ Eğer "Hedef Şube" alanına `kind='branch'` filtresi konursa, listede **yalnız ANKARA GENEL MERKEZ**
kalır; KARAMAN/DÜZCE/NEVŞEHİR **kaybolur** ve **şantiyeler arası transfer yapılamaz hale gelir.**
**Bu, çalışan sistemi bozar.** Bu yüzden önerim, adı "Şube" olan operasyonel alanlara `branch` filtresi
koymamak; bunun yerine **etiketi düzeltmek**tir.

### 3.2 Sınıflandırma

| Alan | Öneri | Gerekçe | Eminlik |
|---|---|---|---|
| M3, M4, W3, W4 (Şubeniz) | **readonly** | Oturumun kendi şubesi; değiştirilemez | **Kesin** |
| W2 (yeni firma ilk şube adı) | **readonly-dışı istisna** | Henüz hiç kayıt yok, yazılması zorunlu | **Kesin** |
| M1, W1 (tanım ekranları) | **both** (tür seçimi kullanıcıda) | Yönetim ekranı; iki türü de yönetir | **Kesin** |
| M2 (giriş şubesi) | **both** | Kullanıcılar `site`'a atanmış (6/6); `branch` filtresi girişi kilitler | **Kesin** (veri kanıtı) |
| M5, M8, W5, W9 ("Hedef Şube") | **both** + **etiketi "Hedef Şube / Şantiye" yap** | Gerçek hedefler şantiye; `branch` filtresi transferi bozar | **Yüksek** (veri kanıtı) |
| M6, M7, W8 ("Şube / Şantiye") | **both** — değişiklik yok | Etiket zaten doğru | **Kesin** |
| M9, W6 (Araç) | **both** — değişiklik yok | Etiket "Şantiye / Şube" doğru; 94/94 araç `site` | **Kesin** |
| M11, M12, W10 (Gönderen/Gönderilecek Şube) | **both** + etiket "Şube / Şantiye" | Faz 2'de eklendi, canlıda henüz veri yok (0 kayıt); transfer hedefleri şantiye olacak | **Orta** → K2'ye tabi |
| M10, W7 (Talep "Şantiye") | **?** | Canlıda **0 talep** var → veriden çıkarım yapılamıyor | ⚠️ **KULLANICI KARARI** |
| M13, W11-Personel ("Şube") | **both** + etiket | 1/1 personel `site`'a bağlı | **Orta** (tek kayıt) |
| M14, W11-Kullanıcı ("Şube") | **both** + etiket | 6/6 kullanıcı `site`'a bağlı | **Yüksek** |
| M15, W11-Rapor | **both** — değişiklik yok | Web etiketi zaten "Şube / Şantiye"; masaüstü etiketi düzeltilmeli | **Kesin** |
| M16 (Excel) | **both** ama **oluşturma yok** | §5 | **Kesin** |

**Özet:** Hiçbir alana `branch`-only ya da `site`-only filtresi **önerilmiyor**. Asıl düzeltme
**etiket tutarlılığı** ve **oluşturma yollarının kapatılması**. Bu, senin "gereksiz yere tek türe
zorlama" talimatınla da örtüşüyor.

---

## 4. TESPİT EDİLEN HATALAR VE YETKİ AÇIKLARI

### H1 — 🔴 Yetki atlatma (masaüstü + içe aktarma)

```
LookupService.cs:17   private const string Module = "definitions";
LookupService.cs:43   AddBranch(...) => Insert(s, "branches", name, ("kind","site"));
LookupService.cs:159  Insert(...) → AccessControl.Require(s, "definitions", Create);
LookupService.cs:302  EnsureKnownTable: "branches" İZİNLİ
AccessControl.cs:115  IsAdminRestricted: "branches" ADMIN-KISITLI
AppModules.cs:57      "definitions" → normal role verilebilir
```

→ **`definitions/Create` yetkisi olan normal bir personel, admin-kısıtlı `branches` modülüne hiç sahip
olmadan Şantiye oluşturabiliyor.** Hem masaüstü "+" butonundan hem Excel içe aktarmadan.
**Bu, UI'da buton gizleyerek çözülmez; servis katmanında kapatılmalıdır.**

### H2 — 🟠 Alan üzerinden tanım oluşturma (4 nokta)

| Nokta | Katman | Yetki | Üretilen tür |
|---|---|---|---|
| Masaüstü Araçlar "+" | UI + `LookupService` | `definitions/Create` | `site` |
| Web Araçlar `CreatePath` | UI + `/api/branches` | `branches/Create` (admin) | `branch` |
| Web Talepler `CreatePath` | UI + `/api/branches` | `branches/Create` (admin) | `branch` |
| Excel içe aktarma | `ImportLookupResolver` | `definitions/Create` | `site` |

Web tarafındaki iki nokta **yetki açığı değil** (doğru servisi çağırıyor, yetkisiz kullanıcı 403 alır)
ama **kurala aykırı** ve kafa karıştırıcı. Masaüstü ve içe aktarma **hem kurala aykırı hem yetki açığı**.

### H3 — 🟡 İki platform farklı `kind` üretiyor

Aynı ekran (Araçlar), aynı alan: masaüstü `site`, web `branch`. Veri tutarsızlığı üretir.

### H4 — 🟡 `kind` filtresi hiçbir yerde yok

Üç okuma noktasının hiçbirinde filtre yok. **Ama §3.1 gereği çözüm filtre değil, etiket düzeltmesi.**

### H5 — 🟡 Benzersizlik kısıtı yok

`branches` üzerinde (company_id, kind, name) benzersizliği yok. Canlıda **bugün mükerrer yok** (ölçüldü),
ama "+" ve içe aktarma yolları kolayca kopya üretebilir.

---

## 5. EXCEL İMPORT PROBLEMİ

### 5.1 Mevcut mimari (incelendi)

- Her içe aktarma servisi (`VehicleImportService`, `PersonnelImportService`, `MaterialImportService`,
  `FuelImportService`, `FuelDepotImportService`, `MaintenanceImportService`, `InspectionImportService`)
  **`DryRun` (önizleme) + `Commit`** desenini kullanır.
- Sonuç tipi: `ImportResult(ok, total, valid, ..., invalid, errors)`; hata birimi
  **`ImportRowError(RowNumber, Message)`**, üst sınır `ImportResult.MaxReportedErrors`.
- ✅ **Kısmi içe aktarma DESTEKLENİYOR:** geçerli satırlar aktarılır, geçersizler **satır numarasıyla**
  raporlanır.
- Bugünkü şube davranışı: `ImportLookupResolver.Resolve()` (`:45-61`) eşleşme bulamazsa **sessizce
  oluşturur** ve adı `CreatedNames` listesine ekler (kullanıcıya "şu tanımlar oluşturuldu" olarak gösterilir).

### 5.2 Önerilen davranış — mevcut mimariye BİREBİR oturuyor

Şube/şantiye için otomatik oluşturmayı kaldır ve **var olan hata mekanizmasını kullan**:

1. `ImportLookupResolver.Branch(name)` eşleşme bulamazsa **`null` döndürsün ve oluşturmasın.**
2. İlgili içe aktarma servisinin `Validate` adımı, şube alanı dolu ama çözülemediyse satırı
   **geçersiz** saysın: `ImportRowError(satırNo, "Şube/Şantiye bulunamadı: 'KARAMN'. Lütfen Şube/Şantiye
   Tanımları ekranından ekleyin ya da adı düzeltin.")`
3. `DryRun` (önizleme) bu hataları **aktarımdan önce** gösterir → kullanıcı düzeltir.
4. Kısmi aktarım korunur: diğer satırlar normal şekilde aktarılır.

**Yeni mimari, yeni tablo, yeni migration gerekmez.** Diğer tanım türlerinin (birim, marka, tip…)
otomatik oluşturma davranışına **dokunulmaz** — yalnız şube/şantiye kuraldan çıkarılır.

⚠️ **Etki uyarısı:** Bugün Excel'deki yazım hataları sessizce yeni şantiye üretiyordu; bundan sonra
**satır hatası** olacak. Bu, kullanıcı için görünür bir davranış değişikliğidir → **K3 kararı**.

---

## 6. SENKRONİZASYON RİSKİ VE TEST SONUCU

### 6.1 Mekanizma (kod kanıtı)

| Yön | Durum |
|---|---|
| Sunucu → Masaüstü | ✅ `LookupSyncService.cs:61,98` — `branches` çekiliyor (kind + parent_id dahil) |
| Masaüstü → Sunucu | ❌ **Yok** — `BusinessSyncService.Tables` listesinde `branches` **bulunmuyor** (`:29-56`) |

Gerekçe kodda yazılı: *"branches PUSH'a dahil DEĞİL (web-otoriteli; kod/şifre taşır)"* — yani bu
**bilinçli bir tasarım kararı**. Sorun kararın kendisi değil; masaüstünde **yine de şube
oluşturulabilmesi**.

### 6.2 Beklenen zincir

1. Masaüstünde "+" ile şantiye oluşur → **yalnız yerel** veritabanında.
2. Araç o şantiyeye atanır → `vehicles` **push listesindedir**.
3. Sunucuda `vehicles.branch_id → branches(id)` **FK'si vardır** (canlıda doğrulandı) ve PostgreSQL FK'yi
   her zaman zorlar.
4. → Aracın satırı sunucuda **FK ihlaliyle reddedilir**; `BusinessSyncService` satır hatasını yakalar
   (SQLite'ta satır-başı `try/catch`, PG'de savepoint kurtarma) → satır **"skipped"** olur, kullanıcı
   yalnız "N kayıt uygulanmadı" uyarısı görür.

### 6.3 Test sonucu: **KOŞULMADI**

Bu aşamada kod yazma yasağı olduğu için hedefli test yazılamadı. Elimizdeki tek gerçek veri: canlıda
**0 yetim şube referansı** (§7). Bu, senaryonun yaşanmadığını **kanıtlamaz** — reddedilen satırlar
sunucuya hiç yazılmadığı için zaten yetim görünmezler.

### 6.4 Geçmiş "araçlar sunucuya ulaşmıyor" olayıyla ilişki

**İlişkilendirmiyorum.** O olayın kök nedeni kayıtlarda farklı belirlenmişti (push watermark'ının
başka bir tablonun zaman damgası yüzünden kendi satırlarını atlaması — Z4 düzeltmesi). Bu yeni senaryo
**aynı desen değildir**; yalnız **benzer bir sonuç** (satırın sessizce ulaşmaması) üretebilir.
Kanıt olmadan aynı sebebe bağlamak yanlış olur.

---

## 7. FK VE VERİ BÜTÜNLÜĞÜ ANALİZİ (canlı, salt-okuma)

### 7.1 Mevcut FK'ler (canlı doğrulama)

`branches.parent_id` · `personnel.branch_id` · `user_scopes.branch_id` · `vehicles.branch_id` → **4 adet**

### 7.2 FK'siz kolonlar + yetim ölçümü

| Tablo.kolon | Dolu kayıt | **Yetim** | FK var mı |
|---|---|---|---|
| users.branch_id | 6 | **0** | ❌ |
| materials.branch_id | 4 | **0** | ❌ |
| material_requests.branch_id | 0 | 0 | ❌ |
| material_requests.ops_from_branch_id | 0 | 0 | ❌ |
| material_requests.ops_to_branch_id | 0 | 0 | ❌ |
| stock_movements.branch_id | 1 | **0** | ❌ |
| stock_movements.branch_from_id | 1 | **0** | ❌ |
| stock_movements.op_branch_id | 2 | **0** | ❌ |
| stock_documents.from_branch_id | 1 | **0** | ❌ |
| stock_documents.to_branch_id | 1 | **0** | ❌ |
| vehicle_maintenances.op_branch_id | 0 | 0 | ❌ |
| fuel_depot_entries.op_branch_id | 1 | **0** | ❌ |
| fuel_distributions.op_branch_id | 0 | 0 | ❌ |
| daily_activities.op_branch_id | 0 | 0 | ❌ |
| request_status_history.op_branch_id | 0 | 0 | ❌ |
| sync_devices.branch_id | 2 | **0** | ❌ |

**Sonuç: canlı veride tek bir yetim şube referansı yok.** Yani bugün FK eklenirse **teknik olarak
başarılı olur**.

### 7.3 FK neden eksik? (analiz)

- `personnel`, `vehicles`, `user_scopes` **ilk şemada** (Migration004/007) FK ile doğmuş.
- Sonradan `ALTER TABLE ... ADD COLUMN` ile eklenen kolonların **hiçbirine** FK konmamış
  (Migration014/025/027/055/061). Sebep teknik: **SQLite'ta var olan bir tabloya sonradan FK eklenemez**
  (tablo yeniden oluşturmak gerekir). Yani bu bir ihmal değil, **SQLite kısıtının doğal sonucu**.

### 7.4 FK ekleme önerisi: **ŞİMDİ DEĞİL**

- PostgreSQL'de eklenebilir; **SQLite'ta (masaüstü) eklenemez** → iki veritabanı arasında davranış farkı
  doğar (daha önce kesin olarak istemediğin durum).
- Bugün yetim kayıt yok; acil bir bütünlük sorunu **yok**.
- Asıl riski (masaüstünde şube oluşturma) **FK değil, §4'teki oluşturma yollarının kapatılması** çözer.

---

## 8. K2 İŞ KURALI ANALİZİ

### 8.1 İstenen tablo — canlı veriden ölçülmüş

| Alan | Mevcut bağlantı | Mevcut kullanım (canlı) | `kind` açısından çıkarılabilen kural | Eminlik |
|---|---|---|---|---|
| `vehicles.branch_id` | FK → branches | **94 kayıt → 94'ü `site`** | Araçlar **şantiyeye** bağlanıyor | **Yüksek** |
| `users.branch_id` | FK yok | **6 → 6'sı `site`** | Kullanıcılar **şantiyeye** atanıyor | **Yüksek** |
| `user_scopes.branch_id` | FK → branches | (kapsam tablosu) | Yetki kapsamı şube/şantiye ayırmıyor | Orta |
| `personnel.branch_id` | FK → branches | **1 → 1'i `site`** | Personel şantiyeye bağlanıyor | Orta (tek kayıt) |
| `materials.branch_id` | FK yok | **4 → 4'ü `site`** | Malzeme şantiyeye bağlanıyor | Orta |
| `stock_movements.branch_id` / `branch_from_id` / `op_branch_id` | FK yok | **1 / 1 / 2 → hepsi `site`** | Stok hareketleri şantiyede geçiyor | Orta (az veri) |
| `stock_documents.from_branch_id` / `to_branch_id` | FK yok | **1 / 1 → ikisi de `site`** | Transferler **şantiyeler arası** | Orta (az veri) |
| `fuel_depot_entries.op_branch_id` | FK yok | **1 → `site`** | Yakıt şantiyede | Düşük |
| `material_requests.branch_id` ve `ops_*` | FK yok | **0 kayıt** | **Çıkarım yapılamıyor** | ⚠️ **YOK** |
| `vehicle_maintenances` / `daily_activities` / `fuel_distributions`.`op_branch_id` | FK yok | **0 kayıt** | Çıkarım yapılamıyor | ⚠️ **YOK** |

### 8.2 Veriden çıkan model

```
kind='branch'  →  1 adet: "ANKARA GENEL MERKEZ"   (üst kayıt / genel merkez)
kind='site'    →  5 adet: KARAMAN, DÜZCE, NEVŞEHİR, ...   (üstü genel merkez olan ŞANTİYELER)
Operasyonel kayıtların TAMAMI → site
```

`parent_id` kullanımı da bunu destekliyor: şantiyelerin çoğunun **üstü var** (`ust:alt`), genel merkezin
yok. Yani fiilen **"Şube = genel merkez, Şantiye = sahadaki iş yeri"** gibi kullanılıyor.

### 8.3 Ama bu **kesin iş kuralı değildir**

- Örneklem küçük (1 branch, 5 site) ve tek firma gerçek kullanımda.
- Kodda, belgelerde veya ADR'lerde "araç şantiyeye bağlanır" gibi **yazılı bir kural yok** (arandı).
- Yarın ikinci bir gerçek şube açılırsa bugünkü desen değişebilir.

→ **Bu yüzden kendi başıma iş kuralı üretmiyorum. K2 senin kararın (§9).**

---

## 9. KULLANICI KARARI GEREKTİREN KONULAR

| # | Karar | Seçenekler | Önerim |
|---|---|---|---|
| **K1** | §6 senkron senaryosu **testle kanıtlansın mı**? | (a) Evet — hedefli test yaz (kod yazımı gerektirir) · (b) Hayır, doğrudan düzelt | **(a)** — hem kanıt hem kalıcı koruma |
| **K2** | Şube/Şantiye ayrımı nasıl uygulansın? | (a) **Filtre koyma; etiketleri "Şube / Şantiye" olarak birleştir** · (b) Etikete göre katı filtre (⚠️ **transferi bozar**, §3.1) · (c) Alan bazında sen belirle | **(a)** |
| **K3** | Excel'de tanınmayan şube/şantiye | (a) **Satır hatası ver, oluşturma** · (b) Bugünkü gibi otomatik oluştur · (c) Yalnız uyar, yine de oluştur | **(a)** |
| **K4** | Talep ekranındaki alanın adı "Şantiye" | (a) "Şube / Şantiye" yap · (b) "Şantiye" kalsın, `site` filtresi koy · (c) Dokunma | **Senin kararın** — canlıda 0 talep olduğu için veriden çıkaramıyorum |
| **K5** | Masaüstü ve web farklı `kind` üretiyor (H3) | Oluşturma yolları kapatılınca **sorun kendiliğinden biter** | Bilgi amaçlı |
| **K6** | Benzersizlik kısıtı (company+kind+name) | (a) Şimdilik yalnız servis içinde kontrol (migration yok) · (b) Migration ile veritabanı kısıtı · (c) Yapma | **(a)** |
| **K7** | Eksik FK'ler | (a) **Şimdi ekleme** (SQLite'ta imkânsız → davranış farkı) · (b) Yalnız PostgreSQL'e ekle · (c) İleride | **(a)** |
| **K8** | Silinmiş şubenin geçmişte görünümü | (a) "(silinmiş) X" göster · (b) Bugünkü davranış kalsın | **(a)**, düşük öncelik |

---

## 10. ÖNERİLEN TEKNİK ÇÖZÜM

### Ç1 — Şube/şantiye oluşturma yollarını kapat (dört katman birden)

| Katman | Değişiklik | Neden bu katman |
|---|---|---|
| **Servis (asıl kilit)** | `LookupService`: `AddBranch` **kaldırılsın**; `EnsureKnownTable` izin listesinden **"branches" çıkarılsın** | UI atlansa, doğrudan servis çağrılsa bile oluşturma **imkânsız** olur → H1 kapanır |
| **İçe aktarma** | `ImportLookupResolver.Branch()` **oluşturmasın**, `null` dönsün | §5 |
| **Masaüstü UI** | `VehiclesView.axaml:172` "+" butonu ve `StartAddBranch`/`ConfirmAddBranch` komutları kaldırılsın | Görsel tutarlılık |
| **Web UI** | `Vehicles.razor:84` ve `Requests.razor:27`'den `CreatePath`/`CreateField` kaldırılsın | Buton kaybolur |
| **API** | `POST /api/branches` **olduğu gibi kalır** (`branches/Create` + admin-kısıtlı) — tanım ekranının meşru yolu | Merkezî oluşturma korunur |

> Not: Web'de `CreatePath` kaldırılmasa bile yetkisiz kullanıcı zaten 403 alır; yani web'deki risk
> "yetki açığı" değil "kural ihlali"dir. Asıl kilit **servis katmanındadır**.

### Ç2 — Etiket birliği (K2-a onaylanırsa)

"Şube" diyen ama fiilen iki türü de gösteren alanlar **"Şube / Şantiye"** olarak yeniden adlandırılır:
M5, M8, M11, M12, M13, M14, M15, W5, W9, W10, W11. **Veri veya sorgu değişmez, yalnız etiket.**

### Ç3 — İçe aktarmada satır hatası (K3-a onaylanırsa)

`ImportRowError(satırNo, "Şube/Şantiye bulunamadı: '<ad>' …")` — mevcut `DryRun`/`Commit` ve kısmi
aktarım mimarisi aynen kullanılır.

### Ç4 — Servis içi benzersizlik uyarısı (K6-a onaylanırsa)

`Organization.BranchService.Create/Update`: aynı firmada aynı `kind` + aynı ad varsa **anlaşılır hata**.
Migration yok.

---

## 11. MIGRATION GEREKTİREN / GEREKTİRMEYEN DEĞİŞİKLİKLER

### ✅ Migration GEREKTİRMEYENLER (bu paketin tamamı)

| Değişiklik | Dosya/katman | Risk |
|---|---|---|
| `LookupService.AddBranch` kaldırma + allowlist'ten "branches" çıkarma | `LookupService.cs` | Düşük |
| İçe aktarmada otomatik oluşturmayı durdurma | `ImportLookupResolver.cs` + ilgili `*ImportService` doğrulaması | Orta (davranış değişir) |
| Masaüstü "+" kaldırma | `VehiclesView.axaml` + `VehiclesViewModel.cs` | Düşük |
| Web `CreatePath` kaldırma | `Vehicles.razor`, `Requests.razor` | Düşük |
| Etiket birliği | 11 alan (axaml + razor) | Çok düşük |
| Benzersizlik kontrolü (servis içi) | `Organization/BranchService.cs` | Düşük |

### ⛔ Migration GEREKTİRENLER — **bu pakete DAHİL DEĞİL**

| Değişiklik | Neden ertelendi |
|---|---|
| `branches` üzerinde benzersiz indeks | Şimdilik servis kontrolü yeterli (K6) |
| 14 kolona FK ekleme | **SQLite'ta sonradan FK eklenemez** → iki veritabanı arasında davranış farkı (K7) |
| `is_active` kolonu ("pasif" kavramı) | Bugün yumuşak silme yeterli; talep edilmedi |

**Sonuç: önerilen paketin tamamı migration'sızdır ve canlı veriye dokunmaz.**

---

## 12. UYGULAMA SIRASI

| Adım | İş | Migration | Onay |
|---|---|---|---|
| **0** | K1–K4 kararlarını al | — | **Şimdi** |
| **1** | *(K1-a ise)* Senkron senaryosunu kanıtlayan test | Yok | Adım 0 sonrası |
| **2** | **Servis kilidi**: `LookupService`'ten şube oluşturmayı kaldır + testler | Yok | — |
| **3** | **İçe aktarma**: satır hatası davranışı + testler | Yok | K3 |
| **4** | **UI temizliği**: masaüstü "+" ve web `CreatePath` kaldır | Yok | — |
| **5** | *(K2-a ise)* Etiket birliği (11 alan, web + masaüstü) | Yok | K2 |
| **6** | *(K6-a ise)* Servis içi benzersizlik kontrolü | Yok | K6 |
| **7** | Tam test paketi + ekran QA (yalnız değişen ekranlar: Araçlar, Talepler, İçe Aktarma) | — | — |
| **8** | Deploy (API + web + masaüstü paketi) | Yok | **Ayrı onay** |

Her adım sonunda test koşulur ve sonuç raporlanır; **riskli adıma geçmeden önce onay alınır.**

---

## 13. RİSKLER VE GERİ DÖNÜŞ PLANI

| # | Risk | Seviye | Önlem / geri dönüş |
|---|---|---|---|
| R1 | İçe aktarma davranışı değişince eskiden geçen dosyalar **hata verir** | 🟠 Yüksek | `DryRun` önizlemesi aktarımdan önce uyarır; kullanıcıya net satır no + ad; istenirse eski davranışa dönmek tek satır |
| R2 | Masaüstünde şube ekleyemeyen kullanıcı **iş yapamaz hale gelir** | 🟡 Orta | Şube tanımı zaten admin işi; web tanım ekranı açık. **K2 öncesi kullanıcıya sorulmalı: sahada masaüstünden şantiye açma ihtiyacı var mı?** |
| R3 | Etiket değişikliği kullanıcıyı şaşırtır | 🟢 Düşük | Yalnız etiket; veri/akış aynı |
| R4 | Benzersizlik kontrolü mevcut kopyalarda hata üretir | 🟢 Düşük | Canlıda **mükerrer yok** (ölçüldü); kontrol yalnız yeni kayıtta |
| R5 | Gizli bir yerde şube oluşturma yolu kalması | 🟡 Orta | Servis katmanı kapatıldığı için **teknik olarak imkânsız** hale gelir; test ile doğrulanır |
| R6 | Canlı veri bozulması | 🟢 **Yok** | Paketin tamamı migration'sız; veri yazma/dönüştürme yok |

### Geri dönüş

Tüm paket **kod-içi**; şema değişmediği için geri dönüş **tek komutla önceki sürüme deploy**tir
(ileri/geri uyumlu, veri kaybı riski yok). Migration olmadığı için veritabanı tarafında geri alınacak
bir şey yoktur.

---

## 14. BU AŞAMADA YAPILMAYANLAR

Kod değiştirilmedi · migration oluşturulmadı/çalıştırılmadı · deploy yapılmadı · canlı sunucuya
yazılmadı · mevcut veriler değiştirilmedi · kullanıcı kararı gerektiren hiçbir konuda karar verilmedi ·
repo/klasör/namespace adlarına ve geçici sunucu/web adreslerine **dokunulmadı**.
