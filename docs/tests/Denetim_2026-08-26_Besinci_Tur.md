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

---

## 5. Web / Masaüstü paritesi

| Konu | Web | Masaüstü |
|---|---|---|
| Liste verisi | `GET /api/stock` → `StockService.RecentMovements` | doğrudan `StockService.RecentMovements` |
| Alan adı | "İşlem Tarihi" | "İşlem Tarihi" |
| Varsayılan | bugün (`_docDate = DateTime.Today`) | bugün (`new DateTimeOffset(DateTime.Today)`) |
| Üst/alt sınır | **yok** (geçmiş ve gelecek serbest) | **yok** |
| API alanı | `docDate` (Unix ms) | aynı servis parametresi `docDate` |
| Kaydetme yolları | receive · issue · transfer (**3/3**) | ReceiveIn · IssueOut · Transfer (**3/3**) |
| Gösterilen tarih | `dateText` → işlem tarihi | `DateText` → işlem tarihi |

İkinci bir tarih mantığı **yoktur**; iki platform da aynı servis imzasını ve aynı SQL ifadesini
kullanır. `IST17` testi bunu kaynak üzerinden kilitler (üç çağrı noktasının üçü de sayılır).

---

## 6. Senkron / çevrimdışı etkisi

`stock_documents` senkron tablo listesindedir (`BusinessSyncService.Tables`) ve paket
`SELECT * FROM tablo` ile üretilir → `doc_date` ve `created_at` sütunları **olduğu gibi** taşınır.
Senkron kodunda değişiklik **gerekmedi**.

| Senaryo | Sonuç | Test |
|---|---|---|
| Çevrimdışı girilen geri tarihli hareket, günler sonra senkronlanır | işlem tarihi **korunur**, senkron zamanı yerine geçmez | `IST14` |
| Aynı paket **tekrar** uygulanır | tarihler **değişmez** | `IST15` |
| Aynı `operationId` ile farklı tarihle ikinci deneme | yeni belge açılmaz, **ilk tarih korunur** | `IST16` |
| Kayıt zamanı senkron sırasında | **korunur** (sunucu saati yazmaz) | `IST14` |

---

## 7. Rapor etkisi

| Rapor / ekran | Önce | Sonra |
|---|---|---|
| Stok Hareketleri **ekranı** (web + masaüstü) | `sm.created_at` | **işlem tarihi** |
| Stok Hareketleri **raporu** + Excel | `sm.created_at` | **işlem tarihi** |
| Stok Sayım raporu | `d.doc_date` | değişmedi (zaten doğruydu) |
| Araç maliyet raporu (parça) | `sd.doc_date` | değişmedi |
| Audit / denetim kaydı | `created_at` | **değişmedi** (gerçek zaman) |

**"Bitiş günü" sorunu tekrar üretilmedi:** `DateFilter` hâlâ `col <= @to` kullanır ve gün sonu
değerini çağıran (arayüz) verir — bu kural değiştirilmedi, yalnız `col` işlem tarihine bağlandı.
İzole uçtan uca ölçümde 20.08–20.08 aralığı geri tarihli hareketi **buldu**, 26.08–26.08 aralığı
**bulmadı** (ve tersi) → iki tarih gerçekten ayrışmış durumda.

---

## 8. Testler

### Yeni test sınıfları (2 sınıf · 31 test)

| Sınıf | Test | Kapsam |
|---|---|---|
| `MasaustuFormListeYerlesimTests` | **12** | Yerleşim kararının matematiği + görünümün o kararı uygulaması |
| `IslemTarihiTests` | **19** | İşlem tarihi / kayıt zamanı ayrımı · rapor · Excel · senkron · parite |

### Sayılar

| Koşu | Sonuç |
|---|---|
| Taban (tur başı, yeniden ölçüldü) | 2451 · 0 · 37 |
| **Final koşu 1** | **2482 · 0 başarısız · 37 atlanan** (16 m 42 s) |
| **Final koşu 2 (bağımsız)** | **2482 · 0 · 37** (15 m 44 s) — **birebir aynı** |
| **PostgreSQL (izole küme)** | **47 · 0 · 0 atlanan** |
| **Yedek lehçe kapısı** | **4 · 0 · 0** |

Devre dışı bırakılan, gevşetilen veya retry ile örtülen test **yoktur**.

> ⚠️ **PostgreSQL koşusunda önce 7 test kırıldı — kök neden araştırıldı ve düzeltildi.**
> Hata ürün kaynaklı değildi: `PostgresTestGuard` izole test veritabanını **51 MB** ölçüp
> "50 MB'tan büyük → test veritabanı sayılmaz" diyerek **bilinçli olarak reddetti** (fail-closed
> koruma çalışıyordu). Veritabanı önceki turların verisiyle şişmişti. **Kapı gevşetilmedi**;
> izole test veritabanı sıfırdan oluşturuldu (0 tablo) ve koşu **47/47** geçti. Üretim
> (Neon `depowise_prod`) tamamen ayrı bir sunucudadır ve bu işlemden etkilenmemiştir.

---

## 9. Kasten bozma (mutasyon) sonuçları

Her mutasyon gerçek bir regresyonu taklit eder; kaynak her denemeden sonra geri yüklenir.

| # | Mutasyon | Sonuç | İlk kıran test |
|---|---|---|---|
| M1 | Kullanıcının seçtiği tarih yok sayılıyor (`date = now`) | ✅ kırıldı | `IST5` |
| M2 | `created_at`, işlem tarihiyle yazılıyor (audit kirlenir) | ✅ kırıldı | `IST12` |
| M3 | Ekran/rapor tekrar kayıt zamanını gösteriyor | ✅ kırıldı | `IST5` |
| M4 | Rapor tarih filtresi yanlış alana bağlı | ✅ kırıldı | `IST8` |
| M5 | API ucu tarihi servise geçirmiyor | ✅ kırıldı | `IST17` |
| M6 | Masaüstü formu tarihi göndermiyor (transfer) | ✅ kırıldı | `IST17` |
| M7 | Web formu tarihi göndermiyor | ✅ kırıldı | `IST17` |
| M8 | Masaüstü varsayılanı bugün değil | ⚠️ **önce kaçtı** → test güçlendirildi → ✅ kırıldı | `IST17` |
| M9 | Form yüksekliği sabit (responsive değil) | ✅ kırıldı | `YRL2` |
| M10 | Liste taban yüksekliği korunmuyor | ⚠️ **önce kaçtı** → test güçlendirildi → ✅ kırıldı | `YRL3` |
| M11 | Görünümde form sınırlaması kaldırıldı | ✅ kırıldı | `YRL8` |
| M12 | Liste satırı `Auto` yapıldı | ✅ kırıldı | `YRL7` |

**İlk tur: 10/12. Kaçan ikisi gizlenmedi; testler güçlendirildi ve tekrar denendi → 12/12.**

- **M8 neden kaçtı:** test yalnız metnin dosyada geçmesine bakıyordu; mutasyon alan tanımını
  boşaltsa bile `ClearForm` içindeki aynı metin testi geçiriyordu. Artık **alan tanımının kendisi**
  düzenli ifadeyle sınanıyor.
- **M10 neden kaçtı:** test en küçük 400 px pencereyi deniyordu; varsayılan oranla
  (0,45 × 400 = 180 px) taban zaten tam sağlanıyordu, yani taban mantığı **hiç çalışmıyordu**.
  Artık 250–380 px de sınanıyor ve ayrıca taban mantığının kendisi için `YRL3b` eklendi.

---

## 10. İzole doğrulamalar

### Web (gerçek tarayıcı, izole sunucu + izole veritabanı)

| Kontrol | Sonuç |
|---|---|
| "İşlem Tarihi" alanı ekranda | ✅ var, açıklama metniyle |
| Varsayılan değer | ✅ **26.08.2026** (bugün) |
| Geçmiş tarih seçimi | ✅ **20.08.2026** seçilebildi, engel yok |

### Uçtan uca (web'in kullandığı API uçları, izole sunucu)

| Senaryo | Sonuç |
|---|---|
| Geri tarihli giriş (20.08) | ✅ listede **20.08.2026** görünüyor |
| İleri tarihli giriş (30.08) | ✅ listede **30.08.2026** görünüyor |
| Tarihsiz giriş (eski istemci) | ✅ kayıt zamanı — **eski davranış aynen** |
| Rapor 20.08–20.08 | ✅ **yalnız** geri tarihli hareket |
| Rapor 26.08–26.08 | ✅ geri tarihli hareket **çıkmıyor** (iki tarih gerçekten ayrışmış) |
| Liste sırası | ✅ en son girilen en üstte (sıralama kuralı korundu) |

### Masaüstü (izole ortam)

| Kontrol | Sonuç |
|---|---|
| Ortam | `DEPOWISE_ENVIRONMENT=IzoleTur5` → ayrı klasör (`Alpnex/Data/IzoleTur5`) |
| Açılış | ✅ 45 sn ayakta, çıktıda hata yok, çökme yok |
| Şema | ✅ **72**, **79 tablo** — yeni migration yok |
| Üretim verisi | ✅ **dokunulmadı** |

> ⚠️ **SINIR — dürüst beyan.** Avalonia arayüzü bu ortamda otomatize edilemiyor; masaüstü
> ekranındaki tıklama akışları **sürülemedi** ve sahte GUI testi **üretilmedi**.
>
> ⭐ Bunun yerine yerleşim bağlaması için **kontrollü bir deney** yapıldı: `#KokYerlesim` adı
> kasten bozulup derlendi ve Avalonia **`AVLN2000: Unable to use a compiled binding with a name
> binding if the name cannot be found at compile time`** hatası verdi. Yani bu bağlama **derleme
> anında doğrulanıyor**; gerçek derlemenin 0 hata vermesi, bağlamanın çalışma anında
> çözüleceğinin kanıtıdır.

---

## 11. Yayın ve yayın sonrası kontroller

| Bileşen | Öncesi | Sonrası |
|---|---|---|
| API (`fly.toml`) | v170 | **v171** |
| Web (`fly.web.toml`) | v194 | **v195** |
| Masaüstü | 1.0.153 | **1.0.154** |
| Şema | 72 | **72 (DEĞİŞMEDİ)** |

| Kontrol | Sonuç |
|---|---|
| API `/health` | **200** |
| Web `/` `/login` `/stock` `/reports` | hepsi **200** |
| Güncelleme kaydı | `api/releases/latest` → **1.0.154** |
| **Üç yönlü sağlama** | yerel dosya = yayın kaydı = indirilen paket → `1EC9B2AE…BA10`, **89.975.746 bayt** |
| 5xx / istisna | API **0** · Web **0** |
| Crash-loop / yeniden başlatma | **yok** |
| PostgreSQL gerçekten bağlı | `/api/public/companies` gerçek veri döndürüyor |
| Disk (ADR-070) | **%39** (351 MB / 974 MB) |
| Paket saklama politikası | **tam 3 paket** (1.0.152-153-154) |

**Üretimde YAPILMAYANLAR:** SQL INSERT/UPDATE/DELETE **yok** · migration **yok** · DDL **yok** ·
ACL değişikliği **yok** · secret değişikliği **yok** · test verisi **yok** · gerçek kayıt
değiştirme/silme **yok**. Üretim yalnız salt-okunur sağlık ve yayın doğrulaması için kullanıldı.

---

## 12. Değiştirilen dosyalar

| Dosya | Değişiklik |
|---|---|
| `src/DepoWise.Application/Ui/FormListeOrani.cs` | **yeni** — yerleşim oranı kararı (saf, test edilebilir) |
| `src/DepoWise.Desktop/Converters/FormUstSiniriConverter.cs` | **yeni** — ince Avalonia kabuğu |
| `src/DepoWise.Desktop/Views/StockEntryView.axaml` | form `ScrollViewer` + oran sınırı · liste taban yüksekliği · **İşlem Tarihi** alanı |
| `src/DepoWise.Desktop/ViewModels/StockEntryViewModel.cs` | `DocDate` alanı (varsayılan bugün) · 3 kaydetme yolunda `docDate` |
| `src/DepoWise.Web/Components/Pages/Stock.razor` | **İşlem Tarihi** alanı · `DocDateMs()` · 3 istekte `docDate` |
| `src/DepoWise.Api/Program.cs` | 3 DTO'ya opsiyonel `DocDate` · 3 uçta servise aktarım |
| `src/DepoWise.Infrastructure/Materials/StockMovementFilterSql.cs` | `IslemTarihiSql` — ekran+rapor tek kaynağı |
| `src/DepoWise.Infrastructure/Materials/StockService.cs` | ekran sorgusu işlem tarihini gösterir/süzer · `StockMovementRow` anlamı belgelendi |
| `src/DepoWise.Infrastructure/Reporting/ReportService.cs` | Stok Hareketleri raporu işlem tarihini gösterir/süzer |
| `tests/.../MasaustuFormListeYerlesimTests.cs` | **yeni** — 12 test |
| `tests/.../IslemTarihiTests.cs` | **yeni** — 19 test |
| `docs/DECISIONS.md` · `KNOWN_ISSUES.md` · bu rapor | ADR-155 · ADR-156 |

---

## 13. Bilinçli olarak DOKUNULMAYANLAR

- `BranchAccess` · `BranchService` kapsam kuralları · tenant izolasyonu · yetki mimarisi
- Rapor **dispatch** yapısı · `AppScreens` kataloğu
- Migration runner / kataloğu · **yeni migration açılmadı**
- Senkron firma kapıları · idempotency indeksleri · update/checksum/release mekanizması
- **Stok muhasebesi:** ileri tarihli hareket bakiyeyi hemen etkiler (mevcut kural korundu)
- **Liste sıralaması:** `ORDER BY sm.created_at DESC` (en son girilen en üstte)
- **`StockMovementRow.CreatedAt` alan adı:** JSON sözleşmesi (`createdAt`/`dateText`) korundu
- `RecentMovements` sorgusunun veri kaynağı, limiti ve şube kapsamı
- ARC-01 · YET-01 (ürün kararı, kullanıcıda) · PostgreSQL yedekleme · Satın Alma

---

## 14. Kalan gerçek problemler

Bu iki sorun dışında **karar gerektiren yeni bir gerçek problem tespit edilmedi**. Önceki turlardan
devam eden ve **kullanıcının kararını bekleyen** iki madde aynen duruyor:

- **ARC-01** — araç seçicisinin firma geneli olması (kanıt iki yöne de çekiyor, 12+ çağrı noktası).
- **YET-01** — işlevsiz iki yetki anahtarının yetki ağacından kaldırılması (teknik risk yok).

**Çözülemeyen bir şey yoktur.** Tek kalıcı sınır, Avalonia arayüzünün otomatize edilememesidir;
bu turda yerleşim bağlaması için kontrollü derleme deneyiyle (§10) telafi edildi.
