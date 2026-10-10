# İŞ 2 — GÜNLÜK FAALİYET → STOK / BAKIM TUTARLILIĞI · ANALİZ

**Tarih:** 2026-08-09 · **Öncelik:** P0 · **Durum:** YALNIZ ANALİZ — kod yazılmadı, migration yok, deploy yok,
canlı veriye yazılmadı (yalnız salt-okuma ölçüm).

---

## 1. MEVCUT GÜNLÜK FAALİYET MİMARİSİ

### 1.1 Veri modeli — **Yakıttan farklı olarak burada GERÇEK ilişki var**

`daily_activities` (`Migration009_FuelDailyActivity.cs:60-83`):

```sql
id, company_id,
activity_type   TEXT NOT NULL,   -- maintenance | movement | extra_oil | extra_filter | repair
movement_kind   TEXT NULL,       -- movement | transfer   (yalnız hareket tipinde)
vehicle_id      TEXT NULL,       -- FK → vehicles(id)
from_location_id, to_location_id, operator_id, duration_days, description,
maintenance_id  TEXT NULL,       -- ✅ FK → vehicle_maintenances(id)   ← DOĞRUDAN İLİŞKİ
source_module   TEXT NOT NULL DEFAULT 'daily_activity',
stock_processed BIGINT NOT NULL DEFAULT 0,   -- bu faaliyet stok düşürdü mü
activity_date, operation_id,
created_at, updated_at, version, is_deleted
```

Ek olarak: `op_branch_id` (Migration027 ile eklendi).
Benzersiz indeks: `ux_daily_activities_op (operation_id)` → tekrar gönderim çift kayıt üretmez.

### 1.2 İlişki zinciri (kesin, koddan doğrulandı)

| Bağlantı | Nasıl | Güç |
|---|---|---|
| **Faaliyet → Bakım** | `daily_activities.maintenance_id` **FOREIGN KEY** | ✅ **Güçlü, kesin** |
| **Bakım → Bakım malzemesi** | `maintenance_materials.maintenance_id` **FOREIGN KEY** | ✅ Güçlü |
| **Bakım → Stok hareketi** | `stock_movements.note = maintenanceId` (**metin alanı**) + `operation_id` deseni | ⚠️ **Zayıf ama tutarlı** |
| Faaliyet → Stok hareketi | ❌ **Doğrudan bağ YOK** — yalnız bakım üzerinden dolaylı | — |
| Faaliyet → Araç / Personel / Şube | `vehicle_id` (FK), `operator_id`, `op_branch_id` | Bilgi amaçlı |

**Kritik ayrıntı:** Bakımın ürettiği stok hareketinde `document_id` **NULL**, `branch_id` **NULL**;
bakım kimliği **`note` sütununda** taşınıyor (`MaintenanceService.cs:372-392`).

### 1.3 Soruların net cevapları

| Soru | Cevap |
|---|---|
| Faaliyet hangi ID'leri tutuyor? | `vehicle_id`, `maintenance_id`, `operator_id`, `from/to_location_id`, `op_branch_id` |
| Bakım ile doğrudan ilişki var mı? | ✅ **Evet — FK** |
| Stok hareketi ile doğrudan ilişki var mı? | ❌ **Hayır** — yalnız bakım üzerinden |
| Bir faaliyet birden fazla stok hareketi oluşturabilir mi? | ✅ **Evet** — bakımdaki her malzeme satırı için bir hareket |
| Bir faaliyet birden fazla bakım kaydı oluşturabilir mi? | ❌ **Hayır** — tam olarak 1 (veya 0) |
| Aynı stok hareketi başka işlemce referanslanabilir mi? | ❌ Hayır — her hareketin `operation_id`'si benzersiz (`ux_stock_movements_operation`) |
| İlişki koddan kesin çıkarılabiliyor mu? | ✅ **Evet** (faaliyet→bakım FK; bakım→stok, mevcut `Cancel` mekanizmasının kendi kullandığı yol) |

> **Önemli:** Yakıt işindeki gibi "olmayan ilişkiyi uydurma" durumu **burada YOK**. Zincir gerçekten var.

---

## 2. FAALİYET TÜRLERİ VE ETKİLERİ

Kodda **5 tür** var (`DailyActivityService.cs:17-19` + `activity_type`):

| # | Tür | Kaydeden metot | Stok düşürür mü | Bakım kaydı oluşturur mu | Başka kayıt |
|---|---|---|---|---|---|
| 1 | **`maintenance`** (Bakım) | `SaveMaintenanceActivity` | ✅ **Evet** | ✅ Evet | Sayaç ilerlemesi + sayaç iz kaydı |
| 2 | **`extra_oil`** (İlave Yağ) | `SaveExtraActivity` | ✅ Evet | ✅ Evet | Aynı |
| 3 | **`extra_filter`** (İlave Filtre) | `SaveExtraActivity` | ✅ Evet | ✅ Evet | Aynı |
| 4 | **`repair`** (Tamir) | `SaveExtraActivity` | ✅ Evet | ✅ Evet | Aynı |
| 5 | **`movement`** (Hareket / Sevkiyat) | `SaveMovement` | ❌ **Hayır** (`stockProcessed: false`) | ❌ Hayır | Yok |

`movement` türünün iki alt biçimi var (`movement_kind`): **movement** (hareket) ve **transfer** (sevkiyat).

### 2.1 Tür bazında "bugün silinince ne oluyor"

| Tür | Faaliyet | Bakım kaydı | Stok | Sonuç |
|---|---|---|---|---|
| 1–4 (bakım/ilave/tamir) | ✅ Gizlenir | ❌ **Aktif kalır** | ❌ **Düşük kalır** | 🔴 **TUTARSIZ** |
| 5 (hareket/sevkiyat) | ✅ Gizlenir | — | — | ✅ Tutarlı (etkisi yok) |

### 2.2 İptalde hangi kayıtlar da iptal edilmeli

| Tür | İptal edilmesi gerekenler |
|---|---|
| 1–4 | Faaliyet + **bağlı bakım kaydı** + **bakımın stok hareketleri** (ters hareketle) |
| 5 | Yalnız faaliyet |

⚠️ **Sayaç istisnası:** Bakım kaydı araç sayacını ilerletiyor. Yakıt işindeki kuralla aynı şekilde
**sayaç geri alınmamalı** — bu, K1/Y2'de onaylanmış proje kuralıdır.

---

## 3. MEVCUT SİLME / İPTAL DAVRANIŞI

### 3.1 Uçtan uca akış

```
UI (masaüstü DailyActivityView "Sil" / web Daily.razor çöp kutusu)
  → onay penceresi
  → masaüstü: DesktopServices.DailyActivity.Delete(...)   |   web: DELETE /api/daily/{id}
  → DailyActivityService.Delete(session, id)
  → UPDATE daily_activities SET is_deleted=1, updated_at=@now WHERE ...
```

### 3.2 Ne yapılıyor / ne yapılmıyor

| Kontrol | Durum |
|---|---|
| Fiziksel `DELETE` mi? | ❌ Hayır — **soft delete** (`is_deleted=1`) ✅ |
| `version` artırılıyor mu? | ❌ **HAYIR** (yalnız `updated_at`) |
| Denetim (audit) kaydı? | ❌ **HAYIR — hiç yazılmıyor** |
| Bağlı bakım etkileniyor mu? | ❌ Hayır |
| Stok geri dönüyor mu? | ❌ **Hayır** |
| Transaction? | Tek `UPDATE` — transaction bile açılmıyor |
| Yetki | ✅ `daily_activity` / **Delete** |

### 3.3 Mevcut uyarı metni

Kod bugün kullanıcıyı **uyarıyor** ama sorunu çözmüyor:

- Masaüstü: *"…kaydı silinsin mi? **(Bağlı bakım kaydı Bakım ekranında kalır.)**"*
- Web: aynı uyarı (kodda "Masaüstüyle parite" notu var)

→ Yani bugünkü davranış, senin karar analizindeki **(B) seçeneği**: yalnız uyarı.
Senin kararın **(A)**: bağlı kayıtlar da iptal edilsin.

---

## 4. STOK BAĞLANTILARI (işin en kritik kısmı)

| Soru | Cevap |
|---|---|
| Hangi stok hareketi iptal edilmeli? | Bakım kaydına ait `movement_type='usage'` hareketleri |
| Stok otomatik geri döner mi? | **Bakım iptal edilirse EVET** — `MaintenanceService.Cancel` her malzeme için `+qty` uygular ve `usage_reverse` hareketi yazar |
| Stok hareketinin `is_deleted`'i var mı? | Hareketler **silinmez**; ters hareketle dengelenir (defter mantığı, ADR) |
| Stok hareketi başka belgeye bağlı mı? | ❌ `document_id` **NULL**; bağ `note = maintenanceId` |
| Aynı malzemeden birden çok hareket varsa? | Her satır ayrı `operation_id` (`{op}:mat:{i}`) → **karışmaz** |
| Geçmiş tarihli faaliyet iptal edilirse? | Ters hareket **bugünün tarihiyle** yazılır; bakiye anında düzelir. Geçmiş tarihli raporlarda eski hareket görünmeye devam eder (defter mantığının doğal sonucu) |
| Raporlar nasıl hesaplıyor? | Stok raporları defterden (`Σ yön × miktar`) → ters hareket otomatik yansır |
| **"Bakım ekibi stoğu" işaretli satırlar** | `Cancel` bunları **atlar** — kayıt sırasında zaten stoktan düşülmemişlerdi (2026-08-08 kuralı) ✅ |

**Sonuç:** Stok tarafında **yeni mekanizma yazmaya gerek yok**; mevcut `MaintenanceService.Cancel`
doğru işi zaten yapıyor. Eksik olan tek şey **faaliyet silinince onun çağrılması**.

---

## 5. BAKIM BAĞLANTILARI

| Soru | Cevap |
|---|---|
| İlişki kesin mi? | ✅ `daily_activities.maintenance_id` **FK** |
| Bakımın iptal mekanizması var mı? | ✅ **Var** — `MaintenanceService.Cancel(session, id, gerekçe)` |
| Tekrar iptal edilebilir mi? | ✅ **Güvenli** — zaten iptalse sessizce çıkar (idempotent) |
| Denetim kaydı? | ✅ Bakım iptalinde `Reverse` yazılıyor |
| Maliyet etkisi | Bakım raporları `is_cancelled=0` filtreli (`ReportService.cs:435, 568`) → maliyet otomatik düşer |
| Stok hareketleri kime bağlı? | **Bakıma** (faaliyete değil) → bakım iptali stoğu doğru geri verir |
| İki iptal aynı transaction'da olabilir mi? | ⚠️ **Bugün HAYIR** — bkz. §8 |
| Yetki | Bakım iptali `maintenance` / **Edit** ister |

---

## 6. VERİ TUTARLILIĞI PROBLEMİ (özet)

**"10 adet malzeme kullanıldı → faaliyet silindi"** senaryosunda bugün:

| Kayıt | Durum |
|---|---|
| Günlük Faaliyet | Gizlenir ✅ |
| Bakım kaydı | **Aktif kalır** ❌ |
| Stok bakiyesi | **10 adet düşük kalır** ❌ |
| Stok hareketi | Defterde durur ❌ |
| Günlük Faaliyet raporu | Kayıt **yok** |
| Bakım raporu | Kayıt **var** |
| Stok raporu | Çıkış **var** |

→ **Üç rapor birbirini tutmuyor.** Bu, P0 sınıfı bir veri tutarlılığı problemidir.

### 6.1 Canlı veri durumu (salt-okuma ölçüm)

| Ölçüm | Değer |
|---|---|
| Günlük faaliyet (aktif / silinmiş) | **0 / 0** |
| Bakım tipli faaliyet | 0 |
| Bakım kaydı (aktif / iptal) | **0 / 0** |
| Bakım malzemesi | 0 |
| `usage` / `usage_reverse` hareketi | **1 / 0** |
| **Silinmiş faaliyet ama bakımı hâlâ aktif** | **0** ✅ |
| **Yetim `maintenance_id`** | **0** ✅ |

→ **Canlıda bugün hiç tutarsız kayıt yok** ve düzeltme, gerçek kullanım başlamadan devreye girecek.
Otomatik ilişki kurulamayan kayıt **yok**.

---

## 7. ÖNERİLEN ÇÖZÜM

**Senin kararın (A) güvenle uygulanabilir** — çünkü ilişki gerçek ve iptal mekanizması hazır.

### 7.1 Davranış

```
Faaliyet iptal isteği
  ├─ Tür 5 (hareket/sevkiyat)  → yalnız faaliyet iptal (onay basit)
  └─ Tür 1–4 (bakım/ilave/tamir)
        ├─ Bağlı bakım + malzeme sayısı okunur
        ├─ Kullanıcıya AÇIK onay gösterilir:
        │   "Bu faaliyete bağlı bakım kaydı ve 10 adet malzeme çıkışı bulunmaktadır.
        │    Faaliyeti iptal ederseniz bağlı kayıtlar da iptal edilecektir. Devam edilsin mi?"
        └─ Onaylanırsa TEK İŞLEMDE:
             • bakım kaydı iptal (is_cancelled=1)
             • malzemeler ters hareketle stoğa geri (usage_reverse)
             • faaliyet iptal (is_deleted=1, version+1)
             • denetim kaydı (faaliyet için de) yazılır
             • ⚠️ araç sayacı GERİ ALINMAZ
```

### 7.2 Ek düzeltmeler (mevcut eksikler)

| Eksik | Öneri |
|---|---|
| Faaliyet silmede **denetim kaydı yok** | `Reverse`/`Delete` denetim kaydı eklensin |
| `version` artmıyor | Artırılsın (senkron LWW tutarlılığı) |
| Bakım **zaten iptal edilmişse** | İkinci kez iptal edilmez (mevcut idempotency yeterli) |
| İptal edilen faaliyet | Tekrar iptal edilemesin / düzenlenemesin |

---

## 8. TRANSACTION / ATOMİKLİK ⚠️ **En önemli teknik engel**

### 8.1 Bugünkü durum

| Servis | Transaction |
|---|---|
| `DailyActivityService.Delete` | **Transaction açmıyor** (tek UPDATE) |
| `MaintenanceService.Cancel` | **Kendi bağlantısını ve transaction'ını açıyor** (`CancelOnce`) |

→ Delete içinden Cancel çağrılırsa **iki ayrı transaction** olur. Senin istemediğin yarım durum
mümkün hale gelir:

> Bakım iptal edildi (stok geri geldi) → faaliyet iptali hata verdi → **faaliyet duruyor ama bakımı iptal**

### 8.2 Çözüm seçenekleri

| Seçenek | Nasıl | Değerlendirme |
|---|---|---|
| **T-a** | `MaintenanceService`'e "verilen bağlantı/transaction ile çalış" iç giriş noktası eklenip her ikisi **tek transaction**ta yapılır | ✅ **Önerim.** Faz 3-Ön'de `StockService` için aynısını yaptık; desen tanıdık. İş kuralları değişmez |
| T-b | Sıra: önce faaliyet, sonra bakım; hata olursa geri al (telafi) | ❌ Kırılgan; telafi de patlayabilir |
| T-c | Şimdilik iki ayrı işlem, hata olursa kullanıcıya bildir | ❌ Yarım durum bırakır — senin açıkça istemediğin şey |

**T-a yeni altyapı gerektirmez**; mevcut `StockBalanceWriter.Run` tekrar sarmalayıcısı da korunur.

---

## 9. WEB DURUMU

| Konu | Durum |
|---|---|
| Liste ekranı | `Daily.razor` — grid + satır bazlı çöp kutusu ikonu (`:105`) ve buton (`:199`) |
| Silme | `DELETE /api/daily/{id}` |
| Onay | ✅ Var (`Dialog.Confirm`) — bağlı bakım uyarısı dahil |
| Yetki | `Auth.CanDelete("daily_activity")` ile buton gizleniyor |
| Detay/düzenleme | ❌ Düzenleme yok (İş 5 kapsamında) |
| İptal edilenlerin görünürlüğü | ❌ Gösterme seçeneği yok |

## 10. MASAÜSTÜ DURUMU

| Konu | Durum |
|---|---|
| Liste ekranı | `DailyActivityView` — "Sil" butonu |
| Silme | `DailyActivityService.Delete` (doğrudan servis) |
| Onay | ✅ Var — bağlı bakım uyarısı dahil |
| Yetki | `CanDelete` kontrolü + "Yetki yok." mesajı |
| İptal edilenlerin görünürlüğü | ❌ Yok |

**Fark:** Web ile masaüstü davranışı **bugün aynı** (kodda "parite" notu var). Yeni kural da **ortak
serviste** yazılacağı için parite korunur.

---

## 11. YETKİLENDİRME

| Konu | Durum |
|---|---|
| Faaliyet silme | `daily_activity` / **Delete** — servis katmanında zorunlu ✅ |
| Bakım iptali | `maintenance` / **Edit** |
| ⚠️ **Yeni durum** | Birleşik iptalde **iki yetki birden** mi aranacak? → **Karar gerekiyor (K2)** |
| "Ters Kayıt" (`btn-reverse`) | Bakım iptalinde **kullanılmıyor** (yalnız stok belge iptali ve —yeni— yakıtta) |
| UI gizleme | Her iki platformda da var |
| Sunucu kontrolü | ✅ Var (asıl kapı serviste) |
| Web'de özel buton yetkisi görünürlüğü | Bu işte **sorun değil** — `btn-reverse` kullanılmıyor. *(Yakıt işinde bulunan genel eksik ayrı iş olarak duruyor.)* |

---

## 12. RAPOR ETKİLERİ

| Rapor | İptal sonrası | Kanıt |
|---|---|---|
| Günlük Faaliyet | Kayıt çıkar | `is_deleted=0` filtresi |
| Bakım | Kayıt çıkar | `vm.is_cancelled=0` (`ReportService.cs:435, 568`) |
| Bakım maliyeti | Maliyet düşer | Aynı filtre |
| Stok / Stok Hareketleri | Ters hareket görünür, bakiye düzelir | Defter mantığı |
| Genel/Durum (şube sayımları) | Sayımlar düşer | `is_deleted` / `is_cancelled` filtreleri |
| Yakıt | **Etkilenmez** | Faaliyet yakıt üretmiyor |

→ Çözüm uygulandığında **üç rapor da aynı gerçeği** gösterecek.

---

## 13. MIGRATION GEREKSİNİMİ

**GEREKMİYOR.**

| Alan | Durum |
|---|---|
| `daily_activities.is_deleted` / `version` / `updated_at` | ✅ Var |
| `daily_activities.maintenance_id` (FK) | ✅ Var |
| `vehicle_maintenances.is_cancelled` / `version` | ✅ Var |
| `maintenance_materials` (malzeme satırları) | ✅ Var |
| `stock_movements` ters hareket | ✅ Mevcut mekanizma |
| Denetim tablosu | ✅ `audit_logs` yeterli |

---

## 14. TEST PLANI

| # | Test | Beklenen |
|---|---|---|
| 1 | Hareket/sevkiyat faaliyeti iptali | Yalnız faaliyet iptal; stok/bakım **hiç etkilenmez** |
| 2 | Bakım tipli faaliyet iptali | Faaliyet + bakım + stok **birlikte** iptal |
| 3 | Bağlı stok hareketleri | Her malzeme için `usage_reverse` yazılır |
| 4 | Stok bakiyesi | İptal öncesi seviyeye **döner** |
| 5 | Bağlı bakım kaydı | `is_cancelled=1` olur |
| 6 | Bakım maliyeti | Rapordan düşer |
| 7 | Faaliyet raporu ↔ stok raporu | **Aynı gerçeği** gösterir |
| 8 | Faaliyet raporu ↔ bakım raporu | **Aynı gerçeği** gösterir |
| 9 | Çok malzemeli bakım (3 satır) | Üçünün de stoğu geri döner |
| 10 | "Bakım ekibi stoğu" işaretli satır | **Geri eklenmez** (şişmez) |
| 11 | İşlem ortasında hata | **Tam rollback** — ne faaliyet ne bakım iptal olur |
| 12 | Yetkisiz kullanıcı | Reddedilir |
| 13 | İptal edilmiş faaliyet tekrar iptal | Engellenir / güvenli |
| 14 | Bakım başka yerden zaten iptal edilmişse | Faaliyet iptali yine çalışır, stok **iki kez geri gelmez** |
| 15 | İptal denetim kaydı | Faaliyet **ve** bakım için yazılır |
| 16 | **Araç sayacı** | **GERİ ALINMAZ** |
| 17 | Web doğrulaması | Onay + iptal + liste |
| 18 | Masaüstü doğrulaması | Aynı |
| 19–22 | Regresyon | Faaliyet oluşturma · stok işlemleri · bakım işlemleri · raporlar bozulmaz |

---

## 15. CANLI VERİ RİSKİ

| Risk | Seviye |
|---|---|
| Mevcut veri bozulması | 🟢 **Yok** — yalnız yeni kod; canlıda 0 faaliyet, 0 bakım |
| Geriye dönük düzeltme gereği | 🟢 **Yok** — tutarsız kayıt **0** ölçüldü |
| Yanlışlıkla toplu iptal | 🟡 Orta — açık onay + iz kaydı ile azaltılır |
| Yarım kalan işlem | 🟠 **T-a çözülmezse gerçek risk** (§8) |
| Sayaç bozulması | 🟢 Yok — sayaca dokunulmayacak |

---

## 16. YENİ BULGULAR / BAĞIMLILIKLAR

| # | Bulgu | Bu işi engelliyor mu |
|---|---|---|
| **B1** | `DailyActivityService.Delete` **denetim kaydı yazmıyor** ve `version` artırmıyor | ❌ Engellemiyor — bu işin içinde düzeltilmesi doğal |
| **B2** | `MaintenanceService.Cancel` kendi transaction'ını açıyor → **paylaşımlı transaction girişi gerekiyor** (§8) | ⚠️ **Bu işin ön koşulu** (T-a) |
| **B3** | Bakım→stok bağı **`note` metin alanında** taşınıyor (`document_id` NULL) | ❌ Engellemiyor; mevcut `Cancel` bu bağa muhtaç değil (malzeme satırlarından gidiyor). **Ayrı iş** olarak iyileştirilebilir |
| **B4** | İptal edilen faaliyetler için "gösterme" seçeneği yok (yakıtta eklendi) | ❌ Engellemiyor — **K3 kararına** bağlı |
| **B5** | Günlük Faaliyet **düzenleme** yok | ❌ Bu işin kapsamı değil — İş 5 |

**Bu işi doğrudan engelleyen bir bulgu YOK.** B2 bir ön koşul ama aynı iş içinde çözülebilir.

---

## 17. SENDEN ONAY BEKLEYEN KARARLAR

**K1 — Atomiklik yöntemi (§8)**
Faaliyet + bakım + stok iptalinin **tek işlemde** yapılabilmesi için `MaintenanceService`'e paylaşımlı
transaction girişi eklenecek (Faz 3-Ön'de `StockService`'e yaptığımızın aynısı).
- **(a)** Evet, T-a uygulansın *(önerim — yarım durum imkânsız olur)*
- (b) İki ayrı işlem kalsın, hata olursa kullanıcıya bildirilsin

**K2 — Birleşik iptalde hangi yetki aransın?**
- **(a)** Yalnız `daily_activity/Delete` yeterli olsun *(önerim — kullanıcı zaten faaliyeti siliyor; bakım iptali bunun sonucu)*
- (b) Ayrıca `maintenance/Edit` de aransın (yetkisi yoksa iptal edemez)
- (c) Ayrıca "Ters Kayıt" özel butonu da aransın

**K3 — İptal edilen faaliyetler listede görünsün mü?** *(yakıttaki gibi)*
- **(a)** Varsayılan gizli + "İptal edilenleri göster" seçeneği *(önerim — iki ekran aynı davransın)*
- (b) Hiç görünmesin (bugünkü davranış)

**K4 — Buton adı: "Sil" mi "İptal Et" mi?**
Kayıt fiziksel silinmiyor; yakıtta "İptal Et" dedik.
- **(a)** "İptal Et" olarak değiştirilsin *(önerim — tutarlılık)*
- (b) "Sil" kalsın

---

## 18. BU AŞAMADA YAPILMAYANLAR

Kod yazılmadı · migration oluşturulmadı · deploy yapılmadı · canlı veriye **yazılmadı**
(yalnız salt-okuma sayım) · geliştirmeye başlanmadı.
