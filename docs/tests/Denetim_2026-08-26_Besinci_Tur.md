# BEŞİNCİ TUR — CANLI KULLANIMDA BİLDİRİLEN İKİ GERÇEK SORUN

> 2026-08-26 · Kullanıcının canlı kullanımda **bizzat tespit ettiği** iki sorun.
> Bu tur genel bir tarama turu **değildir**: kapsam bilinçli olarak bu iki soruna sınırlandı.

---

## 1. Başlangıç durumu (yeniden ölçüldü, önceki rapora güvenilmedi)

| Ölçüm | Sonuç |
|---|---|
| `git HEAD` | `6e92bbf` |
| `origin/master` senkronu | **ileride 0 / geride 0** (birebir senkron) |
| Çalışma ağacı | Temiz (yalnız kullanıcının kendi iki dosyası izlenmiyor — dokunulmadı) |
| Release derleme — API | **0 hata** |
| Release derleme — Web | **0 hata** |
| Release derleme — Masaüstü | **0 hata** |
| Tam test | **2451 geçti · 0 başarısız · 37 atlandı** (13 m 53 s) |
| Migration kataloğu | son dosya `Migration072_RoleGrantLimitsCompany` |
| Üretim şema sürümü | **72** |
| API / Web sürümü | v170 / v194 |
| Masaüstü sürümü | 1.0.153 |

---

## 2. SORUN #1 — Masaüstü tablosu görülemiyor: KÖK NEDEN

### Önce soru: veri gerçekten geliyor muydu?

**Evet — veri akışı kopuk DEĞİLDİ.** Kod üzerinden kanıtlandı:

| Katman | Web | Masaüstü |
|---|---|---|
| Çağrı | `GET /api/stock` | doğrudan servis |
| Metot | `svc.Stock.RecentMovements(s)` | `DesktopServices.Stock.RecentMovements(_session)` |
| Limit | 200 | 200 |
| Şube kapsamı | aynı `BranchScope` | aynı `BranchScope` |

İkisi **aynı sınıfın aynı metodunu** çağırıyor; ikinci bir sorgu yolu yok. Ayrıca ekranın alt
köşesindeki yazı `Status = $"{Movements.Count} hareket"` satırından gelir ve kullanıcının ekran
görüntüsünde **"19 hareket"** yazıyordu → koleksiyon **doluydu**. Yani sorun veride değil,
yerleşimdeydi.

### Kök neden (yerleşim)

`StockEntryView.axaml` kök `Grid`: `RowDefinitions="Auto,Auto,*,Auto"`

| Satır | İçerik | Davranış |
|---|---|---|
| 0 | Araç çubuğu | `Auto` |
| 1 | **Form** | `Auto` → **istediği boyu alır** |
| 2 | **Liste** | `*` → **yalnız ARTAN kadar** |
| 3 | Durum yazısı | `Auto` |

Formun istediği boy ölçüldü: **44 form alanı** + 130 px arama paneli + 180 px sepet + 44 px not
kutusu ≈ **700 px**. 947 px'lik pencerede araç çubuğu, kenar boşlukları ve durum satırından sonra
listeye kalan: **~50 px** → bir "şerit".

Web'de sorun yoktu çünkü sayfa **tarayıcıyla birlikte kayıyor**; masaüstünde pencere sabit ve
`Auto` satırı önce doyuyor.

### Düzeltme

- Form bir `ScrollViewer` içine alındı ve **kapsayıcı yüksekliğinin bir ORANIYLA** sınırlandı —
  **sabit piksel değil**. Taşarsa form kendi içinde kayar, tabloyu ezmez.
- Liste satırı `*` olarak **kaldı** ve **taban yükseklik** aldı → hiçbir pencere boyunda "şerit"
  olamaz.
- Pencere büyüyünce forma verilen pay da büyür → tablo da büyür.

**Neden sabit piksel değil:** "formu 420 px'e sabitle" 768 px ekranda taşar, 1440 px ekranda formu
gereksiz kırpardı.

**Test edilebilirlik:** karar mantığı saf fonksiyon olarak `DepoWise.Application.Ui.FormListeOrani`'ye
taşındı; masaüstünde yalnız ince bir `IValueConverter` kabuğu kaldı.

**Dokunulmayan:** API, veritabanı, senkron, `RecentMovements` sorgusu, ekranın işlevi ve tasarımı.

---

## 3. SORUN #2 — İşlem tarihi: MEVCUT ŞEMA ANALİZİ

### Şemada zaten ne vardı

`stock_documents` (Migration006, 2026-07):

| Sütun | Anlam |
|---|---|
| `doc_date BIGINT NOT NULL` | **belgenin iş günü** |
| `created_at BIGINT NOT NULL` | **kaydın oluşturulma zamanı** |

`stock_movements` her satırı `document_id` ile bu belgeye bağlar.

### Servis katmanı zaten ayırıyordu

`StockService.RunDocumentInTx`:

```csharp
var now  = _clock.UtcNow.ToUnixTimeMilliseconds();   // GERÇEK kayıt zamanı
var date = docDate ?? now;                            // iş günü (opsiyonel parametre)
InsertDocument(..., docNo, date, ..., now, ...);      // doc_date = date · created_at = now
AuditWriter.Write(..., _clock);                       // audit DAİMA gerçek saat
```

`ReceiveIn` · `IssueOut` · `Transfer` · `Count` — **dördü de** baştan beri opsiyonel `docDate`
parametresi alıyordu.

### Eksik olan neydi

Yalnız **arayüz** (masaüstü + web) ve **API DTO alanı**. Zincirin geri kalanı hazırdı.

### Kalıp doğrulaması

Projede her iş alanının kendi iş tarihi sütunu var: `vehicle_maintenances.performed_date`,
`fuel_depot_entries.entry_date`, `material_requests.request_date`, `daily_activities.activity_date`,
`fuel_distributions.distribution_date`, `stock_documents.doc_date`. Stok hareketi **ekranı**, bu
kalıbın dışında kalıp `created_at` kullanan **tek istisnaydı**.

### 🟢 MIGRATION AÇILMADI — şema **72'de kaldı**

Yeni kolon gerekmedi. `stock_movements`'a ikinci bir tarih sütunu eklemek aynı bilgiyi iki yerde
tutup ayrışma riski üretirdi.

---

## 4. Tarih semantiği (net ayrım)

| Kavram | Nerede | Kim belirler | Örnek |
|---|---|---|---|
| **İşlem tarihi** | `stock_documents.doc_date` | **Kullanıcı** (geçmiş/gelecek serbest) | `2026-08-25` |
| **Kayıt / audit zamanı** | `stock_movements.created_at` + `audit_logs.created_at` | **Sunucu saati** — kullanıcı değiştiremez | `2026-08-26 17:30:00` |
| **Senkron zamanı** | senkron kayıtları | Senkron anı | `2026-08-27 09:12:44` |

Kullanıcı 26.08'de 25.08 tarihli kayıt girerse: hareket **25.08**'e ait görünür, audit'te **26.08**'de
oluşturulduğu durur, senkron 27.08'de olsa bile **hiçbiri diğerinin yerine geçmez**.

### Nerede hangisi kullanılır

| Yer | Tarih |
|---|---|
| Hareket ekranı (web + masaüstü) "TARİH" kolonu | **işlem tarihi** |
| Stok Hareketleri raporu + Excel + tarih aralığı filtresi | **işlem tarihi** |
| Stok Sayım raporu, araç maliyet raporu (zaten `doc_date` kullanıyordu) | **işlem tarihi** |
| Audit / denetim kaydı | **kayıt zamanı** |
| Liste **sıralaması** (`ORDER BY sm.created_at DESC`) | **kayıt zamanı — bilinçli** |

> ⭐ **Sıralama neden değişmedi:** kullanıcı geri tarihli bir kayıt girdiğinde onu listenin **en
> üstünde** görmeye devam etsin diye. İşlem tarihine göre sıralamak, az önce kaydedilen satırı
> listenin ortasına düşürüp "kaydedilmedi mi?" izlenimi verirdi.

### Tek kaynak

`StockMovementFilterSql.IslemTarihiSql = "COALESCE(d.doc_date, sm.created_at)"` — ekran ve rapor
**aynı** ifadeyi kullanır; ikisi ayrı yazılsaydı sessizce ayrışabilirlerdi.

### Geçmiş veri neden güvende

Bu tur öncesi **hiçbir** çağıran `docDate` göndermiyordu (`date = docDate ?? now`, `created_at = now`
— aynı değişken) → mevcut **tüm** satırlarda `doc_date == created_at`. `COALESCE` ifadesi geçmiş
kayıtların görünümünü **hiç değiştirmez**.

### Stok muhasebesi değişmedi

İleri tarihli hareket bakiyeyi **beklemeden** etkiler — mevcut iş kuralı budur ve bu turda
**değiştirilmedi**. Test `IST13` bunu kilitler; ileride sessizce değişirse uyarır.
