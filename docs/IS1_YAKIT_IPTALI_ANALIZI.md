# İŞ 1 — YAKIT KAYDI İPTALİ · ANALİZ

**Tarih:** 2026-08-09 · **Öncelik:** P0 · **Durum:** YALNIZ ANALİZ — kod yazılmadı, migration yok, deploy yok,
canlı veriye yazılmadı.
**Onaylanan iş kuralı (K1):** İptal + yeniden giriş · üstüne yazma yok · fiziksel silme yok · **araç sayacı
geri alınmaz** · iptal denetim (audit) kaydına yazılır.

---

## 1. MEVCUT YAPI

### 1.1 Yakıt kayıtları nasıl oluşuyor

**İki ayrı kayıt türü var:**

| Tür | Tablo | Ne demek |
|---|---|---|
| **Depo girişi** | `fuel_depot_entries` | Tedarikçiden yakıt alımı (depoya giren) |
| **Dağıtım** | `fuel_distributions` | Depodan araca verilen yakıt (depodan çıkan) |

**Depo girişi (`AddDepotEntry`)** — `FuelService.cs:44-80`
1. Yetki: `fuel` / Oluştur · litre > 0 · para birimi desteklenmeli
2. `operation_id` ile **idempotent** (aynı işlem iki kez gelirse ikinci kez yazmaz)
3. `INSERT` → litre, birim fiyat, para birimi, kur, fatura no, not, tarih, işlem şubesi
4. **Denetim kaydı** yazılır (`fuel_depot_entry` / Create)

**Dağıtım (`Distribute`)** — `FuelService.cs:82-147`
1. Yetki: `fuel` / Oluştur · litre > 0
2. `operation_id` ile idempotent
3. **Depo bakiyesi kontrolü** → yetersizse *"Depo yakıtı yetersiz"* hatası
4. Aracın **mevcut sayacı okunur** (`prev`)
5. Birim fiyat verilmemişse **en son depo girişinin fiyatı** kullanılır (anlık kopya = snapshot)
6. `INSERT` → araç, `prev_meter`, `current_meter`, litre, fiyat, personel, "yakıtı alan", tarih, not
7. **Araç sayacı ileri alınır** — yalnız `MeterRule.ShouldAdvance(prev, yeni)` yani **yeni > mevcut** ise
8. `vehicle_meter_logs`'a sayaç değişim kaydı yazılır
9. **Denetim kaydı** yazılır (`fuel_distribution` / Create)

Tamamı **tek transaction** içinde (ya hepsi ya hiçbiri).

### 1.2 `is_deleted` yapısı nerede kullanılıyor

| Yer | Kullanım |
|---|---|
| **Tablo şeması** | ✅ Her iki tabloda `is_deleted BIGINT NOT NULL DEFAULT 0` **zaten var** (`Migration009`) |
| **Depo bakiyesi** | `SUM(...) WHERE company_id=@c AND **is_deleted=0**` |
| **Dağıtım listesi** | `WHERE fd.company_id=@c AND **fd.is_deleted=0**` |
| **Depo giriş listesi** | `WHERE company_id=@c AND **is_deleted=0**` |
| **Raporlar** | ReportService'te 23 yerde `is_deleted` filtresi |
| **Yazan kod** | ❌ **Hiç yok** — hiçbir servis `is_deleted=1` yapmıyor |

→ **Altyapı hazır, yalnız "iptal et" düğmesini çeviren kod eksik.**

### 1.3 Depo bakiyesi nasıl hesaplanıyor

```
Depo bakiyesi = Σ(depo girişleri litre, is_deleted=0) − Σ(dağıtımlar litre, is_deleted=0)
```
Anlık hesaplanıyor; saklanan bir "bakiye" alanı **yok**. → **Bir kayıt iptal edilince bakiye kendiliğinden
düzelir.**

### 1.4 Araç yakıt sarfiyatı nasıl hesaplanıyor

Yakıt Tüketim Raporu (`ReportService`), araç başına:

```
km    = Σ(current_meter − prev_meter)     -- yalnız is_deleted=0
litre = Σ(liters)                          -- yalnız is_deleted=0
maliyet = Σ(liters × unit_price)
tüketim = km / litre
```

→ İptal edilen kaydın **hem km'si hem litresi** toplamdan düşer; oran anlamlı kalır.

### 1.5 Raporlar yakıt kayıtlarını nasıl kullanıyor

| Rapor | Kullandığı | İptalden etkilenir mi |
|---|---|---|
| Yakıt Tüketim | `fuel_distributions` (km, litre, maliyet) | ✅ Otomatik düzelir |
| Depo Girişleri | `fuel_depot_entries` (litre, fiyat, tutar, fatura) | ✅ Otomatik düzelir |
| Yakıt Özeti | Bakiye + fiyat | ✅ Otomatik düzelir |
| Genel/Durum raporları | Şube bazlı yakıt sayımı | ✅ Otomatik düzelir |

### 1.6 Araç sayacı nasıl ilerliyor

- `MeterRule.ShouldAdvance(mevcut, gelen) => gelen > mevcut` → **yalnız ileri**
- `MeterRule.IsValidDirectSet` → doğrudan sayaç düzenlemede **geriye gitmek yasak**
- Dağıtım kaydedilirken `vehicles.current_meter` güncellenir + `vehicle_meter_logs`'a iz yazılır

### 1.7 Dağıtım hangi kayıtları oluşturuyor

| Kayıt | Tablo |
|---|---|
| Dağıtım satırı | `fuel_distributions` |
| Sayaç güncellemesi | `vehicles.current_meter` (koşullu) |
| Sayaç iz kaydı | `vehicle_meter_logs` (koşullu) |
| Denetim | `audit_log` |

**Stok hareketi ÜRETMEZ** — yakıt, malzeme stoğundan bağımsız. (İyi haber: iptal, stok defterine dokunmaz.)

### 1.8 Web ↔ masaüstü ortaklık

| Katman | Durum |
|---|---|
| İş kuralı (`FuelService`) | ✅ **Tek ve ortak** — ikisi de aynı servisi kullanır |
| API | `GET /api/fuel`, `GET /api/fuel/depot`, `GET /api/fuel/summary`, `POST /api/fuel/distribute`, `POST /api/fuel/depot` |
| **İptal/silme/düzenleme ucu** | ❌ **HİÇ YOK** |
| Masaüstü ekran | `FuelView` — Depo Girişi + Dağıtım sekmeleri |
| Web ekran | `Fuel.razor` — aynı iki bölüm |

→ İptal, **tek serviste** yazılırsa iki platform da aynı davranışı alır.

### 1.9 Mevcut "İptal" butonları gerçekte ne yapıyor

| Yer | Buton | Gerçekte |
|---|---|---|
| Masaüstü FuelView | "İptal" (2 adet) | `ClearDistCommand` / `ClearDepotCommand` → **formu temizler**, kaydı iptal etmez |
| Web Fuel.razor | Yalnız "Kaydet" / "Yenile" | İptal kavramı **hiç yok** |

→ Kullanıcı "İptal" görüp kaydı iptal ettiğini sanabilir; **yanıltıcı adlandırma**.

### 1.10 Denetim (audit) nasıl tutuluyor

`AuditWriter.Write(... "fuel_depot_entry" / "fuel_distribution", id, AuditActions.Create, kullanıcı)` —
oluşturmada yazılıyor. İptal için **`AuditActions.Reverse`** kullanılacak (stok iptalindeki desenin aynısı).

### 1.11 Senkronizasyon

Her iki tablo da senkron listesinde (`fuel_depot_entries`, `fuel_distributions`, modül `fuel`).
`updated_at` alanları var → iptal, LWW (son yazan kazanır) ile **diğer makinelere ve web'e yayılır**.

---

## 2. İPTAL İŞLEMİNİN ETKİLEYECEĞİ KAYITLAR

### 2.1 Depo girişi iptali

| Kayıt | Ne olmalı |
|---|---|
| `fuel_depot_entries.is_deleted` | 0 → **1**, `updated_at` + `version` artar |
| Depo bakiyesi | ✅ Otomatik düşer (sorgu filtreli) |
| Raporlar | ✅ Otomatik çıkar |
| Denetim | ➕ **Reverse kaydı yazılır** (kim, ne zaman, gerekçe) |
| ⚠️ **Depo bakiyesi negatife düşebilir mi?** | **EVET** — açık karar gerekiyor (bkz. §11-Y1) |
| Güncel yakıt fiyatı | ⚠️ İptal edilen giriş "en son" ise, sonraki dağıtımlar bir **önceki** fiyatı kullanır (geçmiş kayıtlar etkilenmez, fiyat kopyası saklı) |

### 2.2 Dağıtım iptali

| Kayıt | Ne olmalı |
|---|---|
| `fuel_distributions.is_deleted` | 0 → **1** |
| Depo bakiyesi | ✅ Otomatik **artar** (çıkış geri sayılmaz) |
| Yakıt tüketim raporu | ✅ Bu kaydın km + litre + maliyeti çıkar |
| **Araç sayacı** | ❌ **DEĞİŞTİRİLMEZ** (onaylanan kural) |
| `vehicle_meter_logs` | ❌ Dokunulmaz (geçmiş iz) |
| Denetim | ➕ Reverse kaydı |
| Stok defteri | ❌ Etkilenmez (yakıt stok üretmiyor) |

### 2.3 🔴 Yeni tespit — "zincir" sorunu (ortada kalan kaydın iptali)

Her dağıtım kendi `prev_meter` ve `current_meter` değerini **o anki araç sayacından** alıyor.

**Örnek:**
| Kayıt | prev | current | Δ km |
|---|---|---|---|
| D1 | 10.000 | 10.200 | 200 |
| D2 | 10.200 | 10.500 | 300 |
| D3 | 10.500 | 10.800 | 300 |

D2 iptal edilirse rapor 200 + 300 = **500 km** gösterir (D2'nin litresi de düştüğü için oran tutarlı kalır).
**Buraya kadar sorun yok.**

**Asıl sorun düzeltmede:** Kullanıcı D2'yi iptal edip doğrusunu girmek isterse, yeni kayıt `prev`'i
**aracın güncel sayacından** (10.800) alır. Kullanıcı gerçek değeri (10.500) yazarsa fark **−300** olur →
rapordaki toplam km **yanlış azalır**.

**Neden:** `NewDistribution` kaydında **`PrevMeter` alanı yok**; `prev` her zaman araçtan okunuyor.

**Çözüm önerisi (§11-Y2):** "İptal Et ve Yeniden Gir" akışında, iptal edilen kaydın `prev_meter` değeri yeni
kayda **taşınsın**. Bunun için servise isteğe bağlı bir `PrevMeter` girişi eklenmeli — **veritabanı
değişikliği gerekmez**, kolon zaten var.

---

## 3. WEB DURUMU

| Konu | Durum |
|---|---|
| Ekran | `Fuel.razor` — Depo Girişi + Dağıtım listeleri var |
| Kayıt iptali | ❌ Yok |
| Buton | Yalnız "Kaydet", "Yenile" |
| Gerekli | Liste satırında **"İptal Et"** + gerekçe soran onay + iptal edilmişleri ayırt etme |

## 4. MASAÜSTÜ DURUMU

| Konu | Durum |
|---|---|
| Ekran | `FuelView` — iki sekme |
| Kayıt iptali | ❌ Yok ("İptal" butonu formu temizliyor) |
| Gerekli | Aynı iptal akışı + **butonun adı netleştirilmeli** ("Vazgeç") |

---

## 5. GEREKLİ KOD DEĞİŞİKLİKLERİ

| # | Katman | Dosya | Değişiklik |
|---|---|---|---|
| 1 | **Servis (ortak)** | `FuelService.cs` | `CancelDepotEntry(session, id, gerekçe)` + `CancelDistribution(session, id, gerekçe)` — `is_deleted=1`, `version+1`, `updated_at`, **denetim kaydı**; tekrar güvenli (zaten iptalse sessizce çık); yetki: `fuel`/Edit + `btn-reverse` özel butonu |
| 2 | Servis (ortak) | `FuelService.cs` | `NewDistribution`'a **isteğe bağlı `PrevMeter`** (düzeltme akışı için — §2.3) *(Y2 onayına bağlı)* |
| 3 | Servis (ortak) | `FuelService.cs` | Listelere isteğe bağlı "iptal edilenleri de göster" seçeneği *(Y3 onayına bağlı)* |
| 4 | **API** | `Program.cs` | `POST /api/fuel/{id}/cancel` ve `POST /api/fuel/depot/{id}/cancel` (gerekçe gövdede) |
| 5 | **Masaüstü** | `FuelViewModel` + `FuelView.axaml` | Liste satırında "İptal Et" + gerekçe/onay penceresi; mevcut "İptal" butonu → **"Vazgeç"** |
| 6 | **Web** | `Fuel.razor` | Aynı akış (MudBlazor onay diyaloğu) |
| 7 | Yetki | — | **Yeni yetki YOK** — mevcut `btn-reverse` (ters kayıt) kullanılır |

**Not:** Sayaca ve `vehicle_meter_logs`'a **hiç dokunulmayacak** — kural gereği.

---

## 6. GEREKLİ TESTLER

| # | Test | Beklenen |
|---|---|---|
| T1 | Depo girişi iptali → bakiye | Bakiye iptal edilen litre kadar **azalır** |
| T2 | Dağıtım iptali → bakiye | Bakiye iptal edilen litre kadar **artar** |
| T3 | **Dağıtım iptali → araç sayacı** | Sayaç **DEĞİŞMEZ** (10.500 → 10.500) |
| T4 | Dağıtım iptali → `vehicle_meter_logs` | Kayıt sayısı **değişmez** |
| T5 | İptal → listeler | İptal edilen kayıt listede **görünmez** |
| T6 | İptal → yakıt tüketim raporu | Km, litre ve maliyet toplamından **düşer** |
| T7 | İptal → denetim | `Reverse` kaydı + gerekçe yazılır |
| T8 | Aynı kaydı iki kez iptal | İkincisi **hata vermez** (tekrar güvenli) |
| T9 | Yetkisiz kullanıcı iptal denemesi | **Reddedilir** (`btn-reverse` yok) |
| T10 | İptal → senkron | `is_deleted=1` diğer tarafa **yayılır** |
| T11 | Depo girişi iptali bakiyeyi negatife düşürürse | **Y1 kararına göre** davranış |
| T12 | Düzeltme akışı (iptal + yeni) | **Y2 kararına göre** `prev_meter` doğru taşınır |
| T13 | Regresyon | Mevcut yakıt ekleme/dağıtım testleri aynen geçer |

---

## 7. MIGRATION GEREKİYOR MU?

**HAYIR.**

| Kontrol | Sonuç |
|---|---|
| `is_deleted` kolonu | ✅ İki tabloda da **var** |
| `version`, `updated_at` | ✅ Var |
| `prev_meter` kolonu (Y2 için) | ✅ **Var** — yalnız servis parametresi eklenecek |
| Yeni tablo/kolon/indeks | ❌ Gerekmez |
| Denetim tablosu | ✅ Mevcut `audit_log` yeterli |

---

## 8. CANLI VERİ RİSKİ

| Risk | Seviye | Not |
|---|---|---|
| Mevcut kayıtların bozulması | 🟢 **Yok** | Yalnız yeni kod eklenir; var olan veri değişmez |
| Yanlışlıkla iptal | 🟡 Orta | Gerekçe + onay penceresi zorunlu; iptal **geri alınabilir** olmalı mı? → **Y4** |
| Sayaç bozulması | 🟢 Yok | Sayaca hiç dokunulmuyor |
| Rapor geçmişinin değişmesi | 🟡 Orta | İptal edilen kayıt eski raporlardan da düşer (doğal sonuç) |
| Canlı veri hacmi | 🟢 Çok düşük | Canlıda **1 depo girişi**, dağıtım yok (salt-okuma ölçümü) → gerçek kullanım başlamadan devreye girecek |

---

## 9. UYGULAMA SIRASI

| Adım | İş |
|---|---|
| 1 | `FuelService` iptal metotları + (Y2 onaylanırsa) `PrevMeter` |
| 2 | Birim testleri (T1–T13) |
| 3 | API uçları |
| 4 | Masaüstü ekran + buton adı düzeltmesi |
| 5 | Web ekran |
| 6 | Tüm test paketi + web/masaüstü doğrulama |
| 7 | Build → deploy (API + web) → masaüstü paketi 1.0.131 → canlı doğrulama → rapor |

---

## 10. TESPİT EDİLEN YENİ BAĞIMLILIKLAR

| # | Bağımlılık | Etki |
|---|---|---|
| B1 | **`prev_meter` zinciri** (§2.3) | Düzeltme akışının doğru çalışması için servise `PrevMeter` girişi gerekir. Migration yok, ama **karar gerekir (Y2)** |
| B2 | `btn-reverse` özel buton yetkisi | Bugün yalnız stok iptalinde kullanılıyor; yakıt için de kullanılırsa **stok iptal yetkisi olan herkes yakıt da iptal edebilir**. Ayrı yetki istenirse Yetki Ağacına yeni madde (**Y5**) |
| B3 | "İptal" buton adı çakışması | Masaüstünde mevcut "İptal" formu temizliyor; yeni işlevle karışmaması için **"Vazgeç"** yapılmalı (küçük UI değişikliği) |
| B4 | İleride "Alan/Kolon Yönetimi" | Listeye "Durum (İptal/Aktif)" kolonu eklenirse kolon kataloğuna girmeli — **şimdi gerekmiyor**, ileride uyumlu |

---

## 11. SENDEN ONAY BEKLEYEN KARARLAR

Bunlar **daha önce vermediğin**, iş kuralı gerektiren yeni sorular:

**Y1 — Depo girişi iptali bakiyeyi eksiye düşürürse ne olsun?**
Örnek: 1000 L girildi, 800 L araçlara dağıtıldı. 1000 L'lik giriş iptal edilirse bakiye **−800 L** olur.
- **(a)** İzin verme: *"Bu girişi iptal edemezsiniz; 800 L zaten dağıtılmış. Önce ilgili dağıtımları iptal edin."* ← **önerim**
- (b) İzin ver, eksi bakiyeye düşsün (açılış stoğu gibi)
- (c) Uyar ama izin ver

**Y2 — Düzeltme yaparken sayaç zinciri korunsun mu?** (§2.3)
- **(a)** Evet: "İptal Et ve Yeniden Gir" akışı, iptal edilen kaydın **başlangıç sayacını** yeni kayda taşısın ← **önerim**
- (b) Hayır: kullanıcı sayacı elle yazsın (ortadaki kayıt düzeltilirse rapor km'si bozulabilir)

**Y3 — İptal edilen kayıtlar listede görünsün mü?**
- **(a)** Varsayılan gizli, "İptal edilenleri göster" kutusuyla üstü çizili görünsün ← **önerim**
- (b) Hiç görünmesin (yalnız denetim kaydında kalsın)

**Y4 — İptal geri alınabilsin mi?**
- **(a)** Hayır; yanlışlıkla iptal edilirse kayıt **yeniden girilir** ← **önerim** (stok/bakım deseniyle aynı)
- (b) Evet, "iptali geri al" düğmesi olsun

**Y5 — İptal yetkisi hangisi olsun?**
- **(a)** Mevcut **"Ters Kayıt" (`btn-reverse`)** yetkisi kullanılsın — yeni yetki yok ← **önerim**
- (b) Yakıta özel yeni bir yetki eklensin (Yetki Ağacına yeni madde)

---

## 12. BU AŞAMADA YAPILMAYANLAR

Kod yazılmadı · migration oluşturulmadı/çalıştırılmadı · canlı veriye yazılmadı (yalnız salt-okuma sayım) ·
deploy yapılmadı · geliştirmeye başlanmadı.
