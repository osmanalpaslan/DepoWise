# BKM-04 — Bakım Malzemesinin Çıktığı Depo · Analiz + Karar

> Oluşturuldu: **2026-08-11** · Kaynak: `STK-04` bulgusu B-1 · Karar: **KARAR-9 / ADR-103**
> Analiz salt-okunur yapıldı; kod bu belge yazılana kadar değiştirilmedi.

---

## 1. Mevcut davranış (analiz anı)

Bakım kaydı kaydedilirken tüketilen malzeme için iki yazma yapılıyor ve **ikisi de lokasyonu sabit "boş" yazıyor**:

| Yazma | Yer | Değer |
|---|---|---|
| Hareket defteri | `MaintenanceService.InsertUsageMovement` | `branch_id` → **NULL** (SQL'e gömülü) |
| Bakiye | `MaintenanceService.ApplyDelta` | `StockBalanceWriter.Unassigned` (sabit) |

Kodun kendi yorumu bunu zaten devredilmiş iş olarak işaretliyordu: *"bakım malzemesinin hangi depodan
düştüğünün seçilmesi bir ürün/UX gereksinimidir… burada tahmin edilerek bir şube SEÇİLMEZ."*

**Kapsam sınırı:** `MaintenanceService`, tüm üretim kodunda **lokasyonu dışarıdan almayan tek stok yazarı**.
`StockService` ve `OpeningStockService` zaten `branchId` alıyor. `from_team_stock = 1` satırları stoğa
**hiç dokunmuyor** → BKM-04 yalnız işaretsiz satırları ilgilendirir.

**Beş giriş noktası** aynı `Save`'e bağlanıyor:

| # | Yol | Platform |
|---|---|---|
| 1 | `POST /api/maintenance` | Web |
| 2 | `MaintenanceViewModel` → yerel servis | Masaüstü (çevrimdışı) |
| 3 | Günlük Faaliyet → bakım (`SaveMaintenanceActivity`) | Her ikisi |
| 4 | Günlük Faaliyet → ilave işlem (`SaveExtraActivity`) | Her ikisi |
| 5 | `MaintenanceImportService` (Excel) | Her ikisi |

## 2. Lokasyon bilgisi sistemde nereden alınabilir

### Kaynak 1 — `vehicle_maintenances.op_branch_id`
Kolon var ve dolduruluyor — ama değeri `s.OperatingBranchId`'den geliyor.
➡️ **A ve B seçenekleri aynı veriyi gösterir.** Sistemde bakımın kendine ait, ayrı girilen bir şubesi YOK.

### Kaynak 2 — `SessionContext.OperatingBranchId`
| Platform | Durum |
|---|---|
| Masaüstü | ✅ Dolu — girişte seçilir (`LoginViewModel`) |
| **Web** | ❌ **HER ZAMAN NULL** — API oturumu JWT'den kurulur, şube claim'i yok |

➡️ 🔴 **Bugün Web'den girilen her bakım kaydının `op_branch_id`'si NULL.**
Bu, `PARITY_MATRIX.md`'deki `WEB-02` bulgusunun bakım tarafındaki karşılığıdır.

### Kaynak 3 — Arayüzün elindeki şube (İKİ PLATFORMDA DA GARANTİ) ✅
```
Masaüstü: BranchGuard.RequireBranchAsync(_session, "Bakım Takibi")   → MaintenanceViewModel
Web:      Dialog.RequireBranchAsync(Auth, "Bakım Takibi")            → Maintenance.razor
```
Her iki bakım ekranı da "Tüm Şubeler" modunda kaydetmeyi **zaten engelliyor** → kaydet anında somut bir
şube her zaman var. Web'in bunu istek gövdesinde göndermesi, STK-04'te stok ekranları için kurulan
**mevcut desenin aynısıdır** — yeni kavram değil.

### Kaynak 4 — `vehicles.branch_id`
Kolon var, araç oluştururken zorunlu (`RequireVehicleFields`). Ama kural sonradan eklendi → eski
araçlarda NULL olabilir. **KARAR-9 ile bu kaynak REDDEDİLDİ** (§4).

### Bulunamayan kaynaklar
"Bakımın yapıldığı yer", servis/atölye lokasyonu, malzemenin çekildiği depo — hiçbiri sistemde yok.

## 3. Seçenek karşılaştırması

A ve B aynı veriyi kullandığı için tabloda tek sütun.

| Ölçüt | **A/B — Oturum şubesi** | **C — Kullanıcı seçsin** | **D — Aracın şubesi** |
|---|---|---|---|
| İş mantığına uygunluk | 🟡 "İşi yapan şube = veren depo" varsayımı | 🟢 Gerçeği kullanıcı bilir | 🟡 Araç orada, parça oradan çıkmamış olabilir |
| Mevcut veri modeli | 🟢 Alan zaten var | 🟡 Parametre taşınmalı | 🟢 Alan var, zorunlu |
| Web deneyimi | 🟢 Ek tıklama yok | 🔴 Her bakımda ek seçim | 🟢 Ek tıklama yok |
| Masaüstü deneyimi | 🟢 Aynı | 🔴 Aynı maliyet | 🟢 Aynı |
| Çevrimdışı | 🟢 Oturumda, ağ gerekmez | 🟢 Depo listesi yerelden | 🟢 Araç kaydı yerelde |
| Yetki / firma güvenliği | 🟢 Şube yetkiden geçti | 🟡 `EnsureLocationOwned` şart | 🟡 `EnsureLocationOwned` şart |
| **Yanlış stok düşme riski** | 🟡 Başka depodan geldiyse yanlış | 🟢 En düşük | 🔴 **Sessiz yanlış** |
| Geçmiş kayıtlarla uyum | 🟢 Dokunulmuyor | 🟢 Aynı | 🟢 Aynı |
| Yanlış seçme riski | 🟢 Seçim yok | 🔴 Acele eden yanlış seçer | 🟢 Seçim yok |
| Ek işlem maliyeti | 🟢 Sıfır | 🔴 Her bakımda bir alan | 🟢 Sıfır |
| API sözleşmesi | 🟡 Opsiyonel `branchId` | 🟡 Zorunlu olursa kırıcı | 🟢 Değişiklik yok |
| Senkron | 🟢 Etkisiz | 🟢 Etkisiz | 🟢 Etkisiz |
| Geriye dönük uyum | 🟢 Şube yoksa ATANMAMIŞ | 🔴 Zorunluysa eski istemci kırılır | 🟡 Eski araçta NULL |
| Çoklu depo modeline geçiş | 🟡 Sonradan C'ye yükseltilebilir | 🟢 Zaten hedef | 🔴 Yol kapalı |

## 4. ✅ KARAR-9 (kullanıcı, 2026-08-11) — A/B temel + C üst katman

Tam metin: [`docs/DECISIONS.md` → ADR-103](../DECISIONS.md)

1. Varsayılan = kullanıcının aktif/oturum şubesi.
2. Bakım formunda **"Malzemenin çekildiği depo"** alanı bulunur.
3. Alan varsayılan olarak oturum şubesinin deposunu gösterir.
4. Kullanıcı **kendi firmasına ait aktif** başka depo/şantiye seçebilir.
5. Özel seçim yapılmazsa varsayılan depodan düşer.
6. Açıkça farklı depo seçilirse **o depodan** düşer.
7. **"Atanmamış" yeni yazma hedefi olarak sunulmaz.**
8. Firmada hiç uygun depo yoksa bakım **engellenmez** → ATANMAMIŞ olarak devam eder (2026-08-06 korunur).
9. Yabancı / bilinmeyen / pasif lokasyon **kabul edilmez** → `EnsureLocationOwned`, **servis katmanında**.
10. `vehicles.branch_id` stok lokasyonu için **KULLANILMAZ**.
11. `op_branch_id` ile stok lokasyonu **karıştırılmaz** (bakım raporundaki "Şube" anlamını korur).

### ⚠️ Sessiz yönlendirme YASAK
Kullanıcı depo seçimini değiştirdiğinde bu gerçekten hareketе yansımalı. Sessizce kullanıcının şubesine
dönmek, aracın şubesini kullanmak, `op_branch_id` üzerinden yeniden hesaplamak veya başka lokasyona
yönlendirmek **yasaktır**. (Aynı hata sınıfı STK-08'de bulunmuştu.)

### ⚠️ İptal simetrisi
Ters hareketin lokasyonu iptal anındaki oturumdan **yeniden hesaplanmaz**; **orijinal hareketin
`branch_id`'si okunur**. Depo A'dan düşen 5, kullanıcı Depo B ile giriş yapmış olsa bile Depo A'ya döner.

## 5. Etkilenen davranışlar

| Alan | Etki |
|---|---|
| Stok bakiyesi | Kırılım doğru depoya taşınır; **firma toplamı değişmez** |
| Stok Durumu raporu | Bakım tüketimi seçilen lokasyonda görünür |
| Bakım raporu | **Değişmez** — "Şube" hâlâ `op_branch_id` |
| Transfer / ters kayıt | Etkilenmez (ayrı yol) |
| Senkron | Kod değişikliği YOK — `branch_id` kolon kesişimiyle taşınır |
| Negatif stok | Kural değişmez; negatif artık **depo bazında** görünür |
| Ekip stoğu | Etkilenmez — stoğa hiç dokunmayan satırlar |

## 6. Migration

**GEREKMEZ.** `stock_movements.branch_id` ve `stock_balances.location_id` (Migration064) zaten var.
Yeni tablo/kolon/indeks/senkron protokolü açılmaz.

## 7. Uygulama sırası

1. KARAR-9 kaydı (ADR-103) ✅
2. Kontrol dosyaları (`CURRENT_PHASE` · `TASK_BACKLOG` · `MASTER_ROADMAP`) ✅
3. Bu analiz dosyası ✅
4. `MaintenanceService` — lokasyon sözleşmesi + `EnsureLocationOwned` + iptal simetrisi
5. `DailyActivityService` + `MaintenanceImportService` aktarımı
6. **Masaüstü** (kural: masaüstü önce) — ViewModel + XAML
7. API uçları (opsiyonel `branchId`)
8. Web ekranları
9. Testler
10. Doğrulama + kayıt güncelleme

## 8. Uygulama sonucu (2026-08-11) — ✅ TAMAMLANDI

### Değişen üretim dosyaları (8)

| Katman | Dosya | Yapılan |
|---|---|---|
| Servis | `MaintenanceService.cs` | `NewMaintenance.StockLocationId` (opsiyonel, **sona**) · `EnsureLocationOwned` · `ApplyDelta`/`InsertUsageMovement` lokasyonu **parametre** olarak alır · **iptal defterden okur** |
| API | `Api/Program.cs` | `MaintenanceDto.BranchId` + `ExtraActivityDto.BranchId` (opsiyonel, sona) → 3 uçta aktarım |
| Masaüstü | `StockLocationPicker.cs` (**yeni**) | Varsayılan/seçenek kuralı **tek yerde**: yalnız gerçek depolar, varsayılan = oturum şubesi |
| Masaüstü | `MaintenanceViewModel.cs` · `MaintenanceView.axaml` | Depo seçici + "Düşülecek depo: …" + depo yoksa uyarı · eksik-stok uyarısı **seçilen depoya** göre |
| Masaüstü | `DailyActivityViewModel.cs` · `DailyActivityView.axaml` | Aynı seçici (bakım + İlave Yağ/Filtre/Tamir) |
| Web | `Maintenance.razor` · `Daily.razor` | Aynı seçici (`WriteTargets` → Atanmamış YOK) · POST'ta `branchId` · eksik-stok uyarısı seçilen depodan |

**`DailyActivityService` değişmedi** — `NewMaintenance`'ı olduğu gibi geçirdiği için yeni alan kendiliğinden akıyor.
**`MaintenanceImportService` değişmedi** — içe aktarım oturumu şubeyi zaten taşıyor (testle doğrulandı).

### İptal simetrisi nasıl çözüldü

`LoadMaintenanceMaterials` (malzeme satırlarından okuma) **kaldırıldı**; yerine `LoadUsageMovements`
geldi — iptal artık **defterin kendisinden** beslenir:
- Lokasyon yalnız defterde tutuluyor (malzeme satırında depo kolonu yok, migration da açılmadı).
- "Bakım ekibi stoğu" satırları hiç hareket üretmediği için **yapısal olarak** dışarıda kalır
  (eskiden bayrakla atlanıyordu).
- Aynı malzeme iki satırda geçse bile her hareket **kendi** lokasyonuna döner.
- Ters kayda `reverses_movement_id` yazılıyor → geri izlenebilirlik (yeni kolon değil, mevcut alan).

### Doğrulamalar

| Doğrulama | Sonuç |
|---|---|
| Çözüm derlemesi | **0 hata** |
| Tam test takımı | **1387 · 1353 geçti · 0 kaldı · 34 atlandı** (taban 1343; **+44 senaryo**) |
| Mevcut bakım/faaliyet testleri | **115/115** — hiçbiri değiştirilmedi/gevşetilmedi |
| SQLite (çevrimdışı) | 27 senaryo, HTTP yok |
| Web gerçek HTTP hattı | 9 senaryo |
| Arayüz paritesi (kaynak taraması) | 7 senaryo |
| **İzole PostgreSQL** | 1 senaryo — boş yerel DB'de koştu; defterde `usage` ve `usage_reverse` **aynı** `branch_id`, ters kayıtta `reverses_movement_id` dolu. DB sonra **silindi** |
| Görsel (tarayıcı render) | ❌ **YAPILMADI** — bkz. §9 |

### Ölçülen davranış (izole PostgreSQL, gerçek satırlar)

```
usage_reverse | 9006eeaa…c6 | 4 | eae8d93b…b2   ← ters kayıt: AYNI depo + orijinal hareket kimliği
usage         | 9006eeaa…c6 | 4 | (yok)         ← orijinal: seçilen depo (Depo B)
```

## 9. Görsel doğrulama — YAPILMADI (dürüst kayıt)

Gerçek tarayıcı render kontrolü **yapılamadı**. Denenen ve neden yürümediği:

1. **Yerel API + yerel Web** ayağa kaldırıldı (`api-local` / `web-local`, canlıya bağlanmadan).
   Yerel sunucu veritabanında (`src/DepoWise.Api/data/depowise-server.db`) **zaten kullanıcılar var**,
   bu yüzden tohum parolası üretilmedi ve giriş yapılamadı. Bu veritabanını sıfırlamak/yeniden
   adlandırmak **kullanıcının yerel geliştirme verisine dokunmak** olurdu → yapılmadı.
2. **Canlı ortam** üzerinden bakmak, bakım kaydı denemesi anlamına gelirdi → **canlı veriye yazma yasağı**.
3. Ayrıca oturum açmak parola girmeyi gerektiriyor; bu, ajan güvenlik kurallarım gereği yapılamaz.

➡️ Şu kontroller **açık kaldı**: seçicinin konumu · uzun depo adları · dar pencere / mobil responsive ·
malzeme satırlarının taşması · seçili depo bilgisinin görünürlüğü · depo olmayan firmadaki uyarının
görünümü. **Kod düzeyinde** doğrulananlar (§8) bunların yerine geçmez.

**Bu boşluğu kapatmanın yolu:** kullanıcı yerel API için bir hesap/parola sağlarsa ya da
`src/DepoWise.Api/data` dizininin geçici olarak yenilenmesine izin verirse tek oturumda kapatılabilir.
