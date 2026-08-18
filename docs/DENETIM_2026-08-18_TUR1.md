# Kapsamlı Denetim — Tur 1: Senkron Kapsamı, Veri Tutarlılığı, Yetki Kapıları

Tarih: **2026-08-18** · Yöntem: **salt-okunur** kod + gerçek şema karşılaştırması
Araç: geçici denetim programı boş bir SQLite'a migration'ları uygular, GERÇEK şemayı koddaki
kataloglarla (senkron listesi, silme kapsamı, yetki modülleri, ekran kataloğu) karşılaştırır.
Hiçbir üretim/geliştirme veritabanına dokunulmadı.

> Bu **Tur 1**'dir. Kapsanan boyut: *veritabanı ↔ senkron ↔ silme ↔ yetki kapısı*.
> Henüz kapsanmayanlar Tur 2+ listesinde (en altta).

---

## ÖZET

| # | Kod | Bulgu | Şiddet |
|---|---|---|---|
| 1 | SNK-A1 | Cari hareket iptali sunucuya gitmiyor → **web'de bakiye yanlış** | 🔴 Yüksek |
| 2 | GUV-A1 | Güncelleme paketi **yetki kontrolünden önce** diske yazılıyor | 🔴 Yüksek |
| 3 | SNK-A3 | **Muayene / Sigorta** verisi hiç senkron olmuyor | 🔴 Yüksek |
| 4 | SNK-A4 | Stok **sayım satırları** senkron olmuyor (belge gidiyor, satırlar gitmiyor) | 🔴 Yüksek |
| 5 | SNK-A2 | Stok iptali sunucuda "iptal" görünmüyor; ikinci kez iptal edilebiliyor | 🟠 Orta |
| 6 | SNK-A6 | Düzenlemede fiziksel silinen satırlar karşı tarafta kalıyor (mükerrer kalem) | 🟠 Orta |
| 7 | SNK-A7 | Senkron şube kapsamı yalnız ön muhasebede var → diğer şubelerin verisi iniyor | 🟠 Orta |
| 8 | GUV-A2 | `/api/backup/list` yetki kontrolsüz — her kullanıcı yedek listesini görüyor | 🟠 Orta |
| 9 | SNK-A5 | 5 yardımcı tablo hiç senkron olmuyor (muadil, uyumlu araç, durum geçmişi…) | 🟡 Düşük |

**Ortak kök neden (1, 2, 5, 6):** senkron **yalnız upsert**'tir ve deltayı bir *zaman damgası*
üzerinden hesaplar. Damgası olmayan ya da güncellemede damgası tazelenmeyen bir satırın
**değişikliği hiç taşınmaz**; `is_deleted` kolonu olmayan bir satırın **silinmesi hiç taşınmaz**.

---

## 🔴 1 · SNK-A1 — Cari hareket iptali sunucuya gitmiyor (bakiye yanlış)

**Nerede:** `PartyLedgerService.Reverse` (`:253`) · `party_ledger` tablosu

**Ne oluyor:**
1. Masaüstünde bir cari hareket iptal edilir.
2. Kod iki şey yapar: aslın `is_reversed=1` yapılır **ve** karşı kayıt `is_reversed=1` ile eklenir
   (ikisi de bakiyeye girmesin, defterde iz kalsın diye — doğru tasarım).
3. `party_ledger` tablosunda **`updated_at` kolonu YOKTUR** → senkron damgası `created_at`'e düşer.
4. Aslın `created_at`'i değişmediği için **`is_reversed=1` güncellemesi sunucuya HİÇ gitmez.**
   Karşı kayıt yeni olduğu için gider.

**Sonuç — sunucudaki bakiye hesabı** (`PartyService.cs:380`) `WHERE is_reversed=0` ile çalışır:

| | Asıl kayıt | Karşı kayıt | Bakiyeye giren |
|---|---|---|---|
| Masaüstü | `is_reversed=1` (hariç) | `is_reversed=1` (hariç) | **0 — doğru** |
| Sunucu / web | `is_reversed=0` (**dahil**) | `is_reversed=1` (hariç) | **asıl tutar — YANLIŞ** |

Yani **masaüstünde iptal edilen bir borç, web'de hâlâ duruyor.** Ön muhasebe yeni canlıya alındığı
için bu hata henüz az veri etkilemiş olabilir; erken yakalandı.

**Önerilen düzeltme:** `party_ledger`'a `updated_at` eklensin (mevcut satırlarda `created_at` ile
doldurulsun), iptal bu kolonu tazelesin. Senkron damgası `updated_at`'i tercih ettiği için delta
otomatik olarak güncellemeyi taşır.

---

## 🔴 2 · GUV-A1 — Güncelleme paketi yetki kontrolünden ÖNCE diske yazılıyor

**Nerede:** `Program.cs` `/api/releases` (POST)

```
var s = Session(ctx); if (s is null) return Unauthorized();   // yalnız "oturum var mı"
...
await svc.ReleasePackages.SaveAsync(version, fs, ...);        // ← DOSYA DİSKE YAZILIR
...
var id = svc.Releases.Publish(s, ...);                        // ← süper admin kontrolü BURADA
```

`Publish` süper admin ister ✔ — ama **dosya çoktan yazılmıştır.**

**İstek gövdesi sınırı 1 GB** (`Program.cs:17,20`), sunucu diski **974 MB** (505 MB boş).

**Sonuç:** herhangi bir oturum sahibi (depo görevlisi dahil) tek bir istekle diski doldurabilir.
ADR-070: disk dolunca SQLite yazamaz ve **login dahil TÜM API 500 döner** — bu tam kesinti
**12.07.2026'da fiilen yaşandı**. Ayrıca yayındaki paket ezilerek güncelleme mekanizması kırılabilir
(masaüstü checksum'ı tutmaz → hiçbir makine güncellenemez).

**Not:** Kardeş uç `/api/setup` (`:3225`) aynı işi **doğru** yapıyor: `if (s is null || !s.IsSuperAdmin)`
kontrolü dosyaya dokunmadan ÖNCE. Yani bu bir tasarım tercihi değil, **gözden kaçmış sıra hatası.**

**Önerilen düzeltme:** yetki kontrolü dosya yazımından öne alınsın (tek satır taşıma). Ek olarak
paket boyutu için makul bir üst sınır (ör. 300 MB).

---

## 🔴 3 · SNK-A3 — Muayene / Sigorta verisi hiç senkron olmuyor

**Nerede:** `vehicle_inspections` tablosu · `BusinessSyncService.Tables` içinde **YOK**

- Ekran `Muayene / Sigorta` **hem masaüstünde hem web'de** var (`AppScreens:130`, `Both`).
- `InspectionService` kaydı **yerel** veritabanına yazar (masaüstü çevrimdışı çalışır).
- Tablo senkron listesinde olmadığı için **hiçbir yöne taşınmaz.**
- Tablo aslında senkrona tamamen hazır: `company_id`, `updated_at`, `version`, `is_deleted` **var**.

**Sonuç:** masaüstünde girilen muayene/sigorta/kasko kaydı web'de **hiç görünmez**; web'de girilen
masaüstüne inmez. Araç uyarıları (muayene yaklaşıyor) makineler arasında tutarsız olur.

Bu, geçen turda düzeltilen **SIF-06 (şablonlar)** ile **birebir aynı sınıf** bir eksiktir.

---

## 🔴 4 · SNK-A4 — Stok sayım satırları senkron olmuyor

**Nerede:** `stock_count_lines` · senkron listesinde **YOK** (ebeveyni `stock_documents` **VAR**)

Sayım belgesi karşı tarafa gidiyor ama **satırları gitmiyor** → belge var, içi boş görünüyor.
`Stok Sayım` ekranı da `Both` (iki platformda da var).

---

## 🟠 5 · SNK-A2 — Stok iptali sunucuda "iptal" görünmüyor

**Nerede:** `StockService.MarkReversed` (`:1159`) · `StockService.SetDocumentStatus` (`:1168`)

```
UPDATE stock_movements SET is_reversed=1 WHERE id=@id;            -- damga tazelenmiyor
UPDATE stock_documents SET status=@s, version=version+1 ...       -- damga tazelenmiyor
```

İkisinde de `updated_at` kolonu yok; `SetDocumentStatus` metodu `long now` parametresini **alıyor
ama hiç kullanmıyor** — damga niyeti var, uygulanmamış.

**Bakiye BOZULMUYOR** (bunu ayrıca doğruladım): sunucudaki `RecomputeBalances` tüm hareketleri
toplar, `is_reversed` filtresi kullanmaz; ters kayıt satırı senkronla gittiği için toplam sıfırlanır.

**Ama:**
- Web'de iptal edilmiş belge **hâlâ "aktif"** görünür, hareket "İptal edildi" etiketi almaz.
- Web kullanıcısı aynı belgeyi **ikinci kez iptal edebilir** → deftere gereksiz 2 satır daha girer
  (bakiye yine doğru kalır ama hareket geçmişi yanıltıcı olur).

---

## 🟠 6 · SNK-A6 — Düzenlemede silinen satırlar karşı tarafta kalıyor

Senkron **upsert**'tir; silme yalnız `is_deleted=1` ile taşınır. Şu iki yerde satırlar **fiziksel**
siliniyor ve bu tablolarda `is_deleted` **yok**:

| Yer | Kod | Sonuç |
|---|---|---|
| Talep düzenleme | `RequestService.cs:179` `DELETE FROM material_request_items` + yeniden ekleme | Eski kalemler karşı tarafta kalır → **mükerrer talep kalemi** |
| Araç şablonu düzenleme | `VehicleTemplateService.cs:274` `DELETE FROM vehicle_template_materials` | Aynı |

Talep düzenlemesi masaüstünde kapalı görünüyor (kaydettikten sonra düzenleme yok), bu yüzden asıl
yön **web → masaüstü**: web'de düzenlenen talebin eski kalemleri **masaüstünde kalır**.

---

## 🟠 7 · SNK-A7 — Senkron şube kapsamı yalnız ön muhasebede uygulanıyor

`BusinessSyncService.BranchScopedTables` yalnız `party_ledger`, `invoices`, `finance_accounts`,
`finance_transactions` içerir (GAP-6 ile ön muhasebeye özel eklenmiş).

`branch_id` taşıdığı hâlde kapsam dışı kalanlar: **`materials`, `vehicles`, `personnel`,
`stock_movements`, `material_requests`, `stock_change_logs`.**

**Sonuç:** yalnız "Şube A"ya yetkili bir kullanıcının bilgisayarına **tüm şubelerin** malzeme, araç,
personel ve stok hareketi verisi iner. Ekranda filtrelense bile veri fiziksel olarak o makinededir.

Bu bir **gizlilik** sorunudur (veri bozulması değil). Düzeltmenin yan etkisi olabilir: kapsamı
daraltmak, bugüne kadar veriyi görebilen kullanıcılarda "veri kayboldu" algısı yaratır → **kullanıcı
kararı gerekir.**

---

## 🟠 8 · GUV-A2 — `/api/backup/list` yetki kontrolsüz

```
app.MapGet("/api/backup/list", (HttpContext c) =>
    S(c) is null ? Results.Unauthorized() : Results.Ok(svc.DbBackup.ListBackups()...))
```

Yalnız "oturum var mı" bakılıyor. Kardeşleri `/api/backup/create` ve `/api/backup/download/{name}`
**süper admin** istiyor. `backup` modülü ise yönetim düzeyi (`IsAdminRestricted`).

**Sonuç:** herhangi bir firmanın herhangi bir kullanıcısı sunucu yedek dosyalarının **adlarını,
boyutlarını ve tarihlerini** görebiliyor. İndiremiyor (o uç korumalı) → **bilgi sızıntısı**, veri
sızıntısı değil.

---

## 🟡 9 · SNK-A5 — Senkron dışı kalan diğer tablolar

| Tablo | Ne kaybediliyor |
|---|---|
| `material_equivalents` | Muadil malzeme eşleşmeleri |
| `material_compatible_vehicles` | Uyumlu araç eşleşmeleri |
| `maintenance_definition_vehicles` | Bakım tanımı ↔ araç eşleşmesi |
| `request_status_history` | Talep durum/onay geçmişi |
| `vehicle_meter_logs` | Araç sayaç geçmişi |
| `file_records` | Dosya/fotoğraf künyeleri (dosyalar sunucuda; ayrıca değerlendirilmeli) |

---

## KASITLI OLDUĞU DOĞRULANANLAR (hata değil)

- `stock_balances` senkronda yok — **doğru** (türetilmiş veri, defterden hesaplanır — SNK-11).
- `invoices` tablosunda `is_deleted` yok — **doğru** (fatura silinmez, iptal edilir).
- `request_ops_warehouse` / `request_ops_purchase` modüllerinin ekranı yok — **doğru** (Faz 1'de
  yalnız yetki ağacına eklendi, ekran Faz 2'de gelecek).
- `export` / `files` modüllerinin ekranı yok — **doğru** (buton/alan yetkisi olarak kullanılıyorlar).
- `ReleaseStore.Safe()` dosya adı temizliği — **dizin aşımı (path traversal) engelli** ✔.
- `/api/backup/download` dizin aşımına karşı korumalı ✔.

---

## TUR 2+ — HENÜZ TARANMAYAN BOYUTLAR

1. Servis katmanı: her yazma yolunda tenant + yetki + audit üçlüsü tam mı?
2. Masaüstü ↔ web davranış eşitliği (ekran ekran alan/buton karşılaştırması).
3. Idempotency / `operation_id` tekilliği: çift gönderimde mükerrer kayıt üreten yol var mı?
4. Para ve sayaç kuralları: negatif stok, sayaç geriye gitme, decimal/para birimi tutarlılığı.
5. Rapor sorguları: şube kapsamı ve firma izolasyonu her raporda uygulanıyor mu?
6. UI doğrulamaları: numeric/tarih alanları, zorunlu alanlar, hata mesajları.
7. Migration geri-uyumluluk ve PostgreSQL/SQLite lehçe farkları.

---

# TUR 2 — Servis katmanı: yetki · tenant · audit · önbellek tazeliği

**Yöntem:** Infrastructure altındaki tüm `*Service.cs` dosyalarında yazma yapan (INSERT/UPDATE/DELETE)
public metotlar ayrıştırıldı; her biri için yetki kapısı, tenant çözümü, audit ve yetki-önbelleği
tazeleme kontrol edildi.

## ✅ TEMİZ ÇIKANLAR (bulgu yok — kayda geçiriliyor)

| Kontrol | Sonuç |
|---|---|
| Yetkisiz yazma yolu | **YOK.** Aday çıkan 4 metot incelendi, hepsi korumalı (`PermissionTemplateService` gizli `RequireSuper` yardımcısıyla; diğer 3'ü kullanıcının kendi verisi). |
| Tenant (firma) izolasyonu | **TEMİZ.** `companyId` parametresi alan tüm metotlar ya `TenantAccessGuard` ya `ResolveCompany` ya da süper admin kapısı kullanıyor. |
| JWT → oturum çözümü | **DOĞRU ve fail-closed** (`AuthService.LoadSnapshot`): çapraz firma YALNIZ süper admine; uydurma firma id'si → `null`; silinmiş firma → kendi firmasına düşer (kilitlenme yok). |
| Şube kapsamı kaydetme | **TAM**: `PermissionService.SaveBranchScope` audit yazıyor **ve** yetki fotoğrafını düşürüyor. |

## 🟡 DEN-B1 — "Tüm Şubeler" yetkisi audit YAZMIYOR ve önbelleği DÜŞÜRMÜYOR

`UserService.SetViewAllBranches` (`:734`) — süper admin kapısı VAR ✔, ama:
- `AuditWriter.Write` **yok** → bu yetkiyi kimin ne zaman verdiği/aldığı **hiçbir yerde kayıtlı değil**.
- `_snapshots?.InvalidateUser(userId)` **yok** → değişiklik **90 saniyeye kadar etkisiz kalır**.
  Kardeş metotlar (`DeleteUser :135`, `SetActive :272`, `SetRoles :398`) üçü de düşürüyor.

⚠️ Asıl risk **geri alma** yönünde: yetki kaldırıldıktan sonra kullanıcı 90 sn daha **tüm şubelerin**
verisini görmeye devam eder. "Tüm Şubeler" firma genelinde veri açan bir yetkidir.

## 🟡 DEN-B2 — `EnrollmentService` hiç audit yazmıyor

Dosyada `AuditWriter` geçiş sayısı: **0**. Yetki kapıları doğru (`ApproveDevice`/`RevokeDevice` admin,
`SetQuota` süper admin) ✔ ama şu işlemler **izsiz**: cihaz onayı · cihaz iptali · cihaz silme ·
token yenileme · makine kotası değiştirme · cihaza firma/şube atama.

Karşılaştırma: `RoleGrantService.SetMatrix` için aynı eksik G6-06'da fark edilip düzeltilmişti
("bu platformdaki en yetkili işlemlerden biri ama iz bırakmıyordu"). Aynı sınıf, burada atlanmış.

## 🟡 DEN-B3 — ÖLÜ KOD: ikinci bir `BranchService` ve `CompanyService`

`src/DepoWise.Infrastructure/Org/BranchService.cs` ve `Org/CompanyService.cs` **hiçbir yerden
referans edilmiyor** (gerçek olanlar `Organization/` altında). Aynı klasördeki `PersonnelService`,
`PersonnelTitleService`, `ScopeResolver` ise KULLANILIYOR — yani klasör tamamen ölü değil, karışık.

Ölü `Org/BranchService.AssignScope` (`:112`) `user_scopes`'a yazıyor ve **audit yazmıyor**.

⚠️ **Neden önemli:** bu tam olarak SIF-01'i doğuran hata sınıfı — iki benzer dosyadan **yanlış olanı**
düzeltmek. Aday tamamlama (IntelliSense) ikisini de gösterir.

## 📋 TUR 2 → YAPILACAKLAR

- `DEN-B1` — `SetViewAllBranches`'e audit + `InvalidateUser` ekle.
- `DEN-B2` — `EnrollmentService`'in 7 yazma metoduna audit ekle.
- `DEN-B3` — `Org/BranchService.cs` + `Org/CompanyService.cs` sil (ölü kod).
