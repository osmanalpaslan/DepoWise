# AKTİF DURUM

> Son güncelleme: **2026-08-18** (denetim yayın turu) · Bu dosya **her iş sonunda** güncellenir.

---

## 🔵 MEVCUT FAZ: **FAZ C — Depo bazlı stok altyapısı**

**KARAR-7 = A** (malzeme kartı firma geneli, stok depo bazlı) — 2026-08-11 kesinleşti.
Tasarım: [`FAZ_C_DEPO_BAZLI_STOK_TASARIM.md`](FAZ_C_DEPO_BAZLI_STOK_TASARIM.md)

## ✅ TAMAMLANAN — `STK-01` + `STK-02` (tek iş birimi, 2026-08-11)

**Stok bakiyesi artık depo bazlı.** `stock_balances` anahtarı `(company_id, material_id, location_id)`
oldu; `location_id=''` = **ATANMAMIŞ**. Transfer bundan böyle bakiyede **görünür** (eskiden tek havuzda
toplandığı için görünmüyordu).

Migration ve kod **aynı iş biriminde** verildi — bilinçli: yalnız migration açılsaydı stok değerleri
**sessizce yanlış** görünürdü (en tehlikeli hata türü).

### Dönüştürülen 16 üretim noktası
| Tür | Adet | Ne yapıldı |
|---|---|---|
| CAS yazma | 4 | `ON CONFLICT(company_id, material_id, location_id)` · `locationId` **zorunlu parametre** (varsayılan yok — çağıran bilinçli seçsin) |
| Skaler + toplu okuma | 4 | `StockBalanceWriter.ReadTotal` / C#'ta `decimal` toplama (SQL `SUM` **yok** — SQLite'ta float hatası verir) |
| JOIN | 8 | `SqlDialect.StockTotalSubquery` → malzeme başına **tek satır**. `DISTINCT` ile gizleme **yok** |

**Yanında bulunan gerçek hata:** sayım, sistem miktarını **firma genelinden** okuyup düzeltmeyi
**şubeye** yazıyordu → "genelden oku, lokasyona yaz" tutarsızlığı. Düzeltildi ve test edildi.

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Tam test takımı | **1223 / 1190 geçti / 0 kaldı / 33 atlandı** (taban 1206'ydı; **17 yeni senaryo**) |
| Çözüm derlemesi | **0 hata** |
| İzole PG provası (üretim yedeğinin kopyası) | 667 hareket · 664 → **665** bakiye satırı · **uyuşmayan 0** · toplam korundu (8952,3) · süre 173 ms |
| Dolu SQLite v63 → v64 yükseltmesi | 3 bakiye → 5 satır · toplam korundu · ondalıklar tam (0.1 / 0.2) |
| Doğrulama kapısı (kasten bozuk bakiye) | Migration **durdu**, şema 63'te kaldı, **hiçbir şey yazılmadı** |
| Dönüştürülen sorgular PostgreSQL'de | Liste **2459 satır = malzeme sayısı** → satır çoğaltma **yok**; detay/kırılım/servis toplamı **tutarlı** |

Yeni testler: [`tests/DepoWise.Tests/StockLocationTests.cs`](../../tests/DepoWise.Tests/StockLocationTests.cs)
QA raporu: [`docs/tests/Stok_Lokasyon_Test_Report.md`](../tests/Stok_Lokasyon_Test_Report.md)

> 🔒 **Masaüstü çevrimdışı mimarisine dokunulmadı.** Senkron **0 değişiklik** gerektirdi:
> `DbIntrospect.PrimaryKey` bileşik anahtarı okuyor, `BusinessSyncService` `ON CONFLICT` hedefini
> ondan kuruyor → üç kolonlu anahtar **otomatik** üretiliyor. `stock_movements` şeması değişmedi.

## ✅ TAMAMLANAN — `STK-03` API lokasyon boyutu (2026-08-11)

Envanter + sözleşme: [`STK_03_API_LOKASYON_PLANI.md`](STK_03_API_LOKASYON_PLANI.md)

**🔴 Asıl bulgu: lokasyon sahiplik doğrulaması YOKTU.** Stok yazma yolları gönderilen `branchId`'nin
firmaya ait olduğunu kontrol etmiyordu. STK-02'den sonra lokasyon bakiyenin **birincil anahtar** kolonu →
yabancı kimlik yazılsaydı o satır **hiçbir firmanın ekranında düzeltilemezdi**. Kapatıldı
(`EnsureLocationOwned`, 4 yazma yolunun tek geçiş noktasında + açılış stoğunda).

| Karar | Ne yapıldı | Neden |
|---|---|---|
| Doğrulama **serviste**, API'de değil | `StockService` + `OpeningStockService` | Masaüstü servisi **çevrimdışı** çağırıyor; API'de olsaydı o yol korumasız kalırdı |
| 3 anlam = 3 ayrı uç | `/balance/{id}` **değişmedi** · **yeni** `/locations` · **yeni** `/location` | Aynı ucu üç anlamda kullanmak sözleşmeyi belirsizleştirirdi; eski Web aynen çalışır |
| Hata kodu | Yabancı/bilinmeyen lokasyon → **403** | Mevcut standart (`EnsureBranchOwned`); yeni model icat edilmedi |
| Lokasyon **zorunlu değil** | Yoksa ATANMAMIŞ | Zorunluluk bir **UI kararı** → STK-04'e devredildi (bugün "Tüm Şubeler" `null` gönderiyor) |

**İstemci envanteri:** Web 5 sayfada stok uçlarını kullanıyor (`JsonElement`+`TryGetProperty` → eklenen
alanlar bozmaz) · **Masaüstü hiçbir stok ucunu kullanmıyor** (yerel servis + `business-push/pull`)
→ uç değişikliği masaüstünü **yapısal olarak** bozamaz.

**Kanıt:** **1240 / 1207 geçti / 0 kaldı / 33 atlandı** (taban 1223; **17 yeni senaryo** — 15 gerçek HTTP +
2 çevrimdışı) · build 0 hata · **sync kodu değiştirilmedi** · N+1 yok (lokasyon adları aynı sorguda JOIN).

## ✅ TAMAMLANAN — `STK-04` Web lokasyon desteği (2026-08-11)

Plan: [`STK_04_WEB_LOKASYON_PLANI.md`](STK_04_WEB_LOKASYON_PLANI.md)

**🔴 Üç gerçek hata bulundu ve düzeltildi (hepsi Web tarafında):**
1. **Sayım `branchId` göndermiyordu** → fark ATANMAMIŞ'a yazılıyor, kullanıcının saydığı depo hiç düzelmiyordu.
2. **Sayım ekranı firma geneli toplamı** "sistem stoğu" diye gösteriyordu → kullanıcı yanlış farkı görürdü.
3. **Açılış stoğu deposuz** gönderiliyordu → her açılış ATANMAMIŞ'a düşüyordu (canlıdaki 663 kaydın sebebi).

| Ekran | Yapılan |
|---|---|
| `Stock` | "Tüm Şubeler"de **zorunlu depo seçici** (eskiden hiç işlem yapılamıyordu) · bakiye çipi **seçili deponun** stoğu |
| `StockCount` | Sayılan depo **ekranda açık** · sistem stoğu **o deponun** (`/count-sheet`) · POST'a `branchId` |
| `StockMovements` | **Depo kolonu** (`Kaynak → Hedef`) · lokasyon **filtresi** (Tüm Şubeler / depo / Atanmamış) |
| `Materials` | Kartta **Toplam + depo kırılımı** (tek istek) · **açılış deposu** zorunlu |
| `Daily` · `Dashboard` · diğerleri | Bilinçli olarak **değiştirilmedi** / doğrulandı |

**🌐 Tüm Şubeler ≠ 📦 Atanmamış** ayrımı arayüzde, filtrelerde ve testlerde kilitlendi.
Yeni `LocationOptions` servisi lokasyon listesini **oturumda bir kez** indirir (N+1 yok);
kırılım yalnız **malzeme kartı** açılınca çekilir (liste satırlarında değil).

**Kanıt:** **1254 / 1221 geçti / 0 kaldı / 33 atlandı** (taban 1240; **14 yeni senaryo**) · build 0 hata ·
gerçek üretim kopyasında doğrulandı.

## ✅ TAMAMLANAN — `STK-05` Masaüstü + çevrimdışı lokasyon (2026-08-11)

Plan: [`STK_05_DESKTOP_OFFLINE_PLANI.md`](STK_05_DESKTOP_OFFLINE_PLANI.md)

**Yapısal bulgu:** Masaüstünde stok için AYRI veri katmanı YOK — doğrudan ortak `StockService`'i çağırıyor.
Bu yüzden STK-01…03'ün tamamı masaüstünde zaten yürürlükteydi; STK-05 **arayüz + eksik parametre** işiydi.

**🔴 Dört gerçek hata (Web'dekilerin masaüstü ikizleri) düzeltildi:**
1. Sayım `branchId` göndermiyordu → fark ATANMAMIŞ'a yazılıyordu.
2. Sayımda sistem miktarı **firma geneli** okunuyordu → kullanıcı yanlış farkı görürdü.
3. Açılış stoğu **deposuz** yazılıyordu → ATANMAMIŞ'a düşüyordu.
4. Giriş/çıkış bakiye çipi **firma geneli** gösteriyordu → "15 var" deyip çıkış reddedilirdi.

**Eklenenler:** Sayım ekranında **"Sayılan Depo"** · malzeme kartında **DEPO KIRILIMI** (tek sorgu) ·
hareket listesinde **DEPO / ŞANTİYE** kolonu (`Kaynak → Hedef`, Web ile **aynı metin**).

**🔒 Çevrimdışı mimariye DOKUNULMADI:** stok yazma yerel SQLite transaction'ı, API çağrısı yok ·
lokasyon listesi yerel veriden · `EnsureLocationOwned` serviste olduğu için çevrimdışı da koruyor ·
**senkron kodu değiştirilmedi**.

**Kanıt:** **1267 / 1234 geçti / 0 kaldı / 33 atlandı** (taban 1254; **13 yeni senaryo**) · build 0 hata ·
çevrimdışı→senkron lokasyonu **koruyor** · online→offline→online döngüsünde **kopya hareket yok** ·
şirket izolasyonu çevrimdışı da geçerli · dolu SQLite v63→v64 ve rollback kapısı **yeniden doğrulandı**.

## ✅ TAMAMLANAN — `STK-06` Rapor lokasyon boyutu (2026-08-11)

Plan + sonuçlar: [`STK_06_UYGULAMA_PLANI.md`](STK_06_UYGULAMA_PLANI.md)

**11 rapor tarandı; yalnız 2'si lokasyon gerektiriyordu ve ikisi de tamamlandı:**

| Rapor | Yapılan |
|---|---|
| **Stok Durumu** | Filtre boşken **eski davranış birebir** (regresyon yok) · depo seçilince **kırılım + "Depo / Şantiye" kolonu + decimal toplam satırı** · 📦 Atanmamış ayrı seçenek |
| **Stok Sayım** | 🔴 **"Sayılan Depo" kolonu** eklendi — hangi deponun sayıldığı raporda HİÇ görünmüyordu · lokasyon filtresi |

**Dashboard'da değişiklik gerekmedi** (STK-02 alt sorgusunun kopyası yok; üretim verisiyle doğrulandı).
**Export ayrı sorgu kullanmıyor** → filtreyi otomatik aldı. Diğer 9 rapora dokunulmadı.

`ReportFilters.Location` **ayrı bayrak** oldu — mevcut `Branch` filtresi "kaydı işleyen şube" demek,
stok lokasyonu değil; ikisi birleştirilmedi (testle kilitli).

**Kanıt:** **1281 / 1248 geçti / 0 kaldı / 33 atlandı** (taban 1267; **14 yeni senaryo**) · build 0 hata ·
izole PG üretim kopyasında doğrulandı · çevrimdışı ↔ sunucu rapor paritesi test edildi.

## ✅ TAMAMLANAN — `STK-07` Senkron sertifikasyonu (2026-08-11)

Kayıt: [`STK_07_SENKRON_SERTIFIKASYONU.md`](STK_07_SENKRON_SERTIFIKASYONU.md)

**11 senaryo GERÇEK HTTP senkron uçlarıyla koşturuldu** (masaüstü ayrı yerel SQLite ile temsil edildi;
stok işlemleri API'ye uğramadan çevrimdışı yazıldı, yalnız "bağlantı gelince" push edildi).

| Kanıtlanan | Sonuç |
|---|---|
| Çevrimdışı giriş/çıkış/transfer/sayım | Lokasyon senkronda **kaybolmuyor** |
| Transfer | **İki bacak da** taşınıyor; kaynak/hedef alanları birebir |
| Idempotency | Aynı paket **3 kez** gönderildi → kopya hareket ve bakiye değişimi **yok** |
| offline→online→offline→online | Yerel ve sunucu hareket sayısı **eşit** |
| Yakınsama | **Hareket kimlikleri dahil** iki taraf aynı |
| **Bakiyenin otoritesi** | Yerel bakiye kasten **999** yapıldı → senkron sonrası **10** = **defter kazandı** |
| **Delta pull** | Güncel sürümden sonrası **boş paket**; eski kayıt tekrar inmiyor; sürüm ilerliyor |
| Şirket izolasyonu | Yabancı depoya yazma **çevrimdışı da** reddediliyor |
| Bakiye tablosu | (malzeme, lokasyon) başına tek satır; **hayalet satır yok** |

🔒 **Senkron kodu DEĞİŞTİRİLMEDİ · offline mimariye dokunulmadı.**
**Kanıt:** 1281 → **1292 / 1259 geçti / 0 kaldı / 33 atlandı** · build 0 hata.

**Yeni bulgu `SNK-12`:** `branches` iş-senkronunda **yok** (web-otoriteli, ayrı org uçlarından geliyor).
Depo bazlı stokta sonucu: web'de açılan yeni depo, masaüstüne org senkronu inmeden stok işleminde
kullanılamıyor. **Hata değil** (çevrimdışı bilinemeyen depo uydurulmamalı) ama görünürlük işi.

## ✅ TAMAMLANAN — `SNK-12` Masaüstünde depo listesi tazeleme (2026-08-11)

**Sorun:** Şubeler iş-senkronunda taşınmaz (web-otoriteli, ayrı yoldan iner). Aynalama YALNIZ girişte
çalıştığı için, oturum açıkken web'de açılan yeni depo masaüstüne inmiyordu → o depoya stok işlemi
yapılamıyordu.

**Çözüm (en küçük):** Mevcut `BranchMirror` mekanizması **normal senkron turunda da** çağrılıyor,
**2 dakikalık kısıtlama** ile (şube listesi küçük ve nadir değişir; 15 sn'lik kadansta her tur indirmek
israf olurdu). **Yeni protokol / tablo / uç YOK · `stock_movements` senkronuna DOKUNULMADI.**
Saf aynalama mantığı `BranchMirrorApply`'a (Infrastructure) taşındı — test edilebilir olsun diye.

**Kanıt:** **1300 / 1267 geçti / 0 kaldı / 33 atlandı** (taban 1292; **8 yeni senaryo**) · build 0 hata.
Yeni depo aynalandıktan sonra **çevrimdışı** giriş/transfer/sayım çalışıyor · kopya yok · isim güncellemesi
yansıyor · silinen depo **pasife alınıyor, fiziksel silinmiyor** (geçmiş stok korunuyor) · **firma
izolasyonu** korunuyor · çevrimdışıyken yerel liste dokunulmadan kalıyor.

## ✅ TAMAMLANAN — `STK-08` Atanmamış stok toplu dağıtımı (2026-08-11)

Plan + sonuçlar: [`STK_08_UYGULAMA_PLANI.md`](STK_08_UYGULAMA_PLANI.md)

**FAZ C'nin son parçası tamamlandı.** Kullanıcı artık geçmişten kalan "Atanmamış" stoğu **kendi seçerek**
depolara dağıtabiliyor — sistem hiçbir tahmin yapmıyor (KARAR-8).

| Katman | Yapılan |
|---|---|
| Servis | `DistributeUnassigned` — **dar giriş noktası**; kaynak DAİMA ATANMAMIŞ, hareket türü `transfer` kalır |
| API | `GET /api/stock/unassigned` · `POST /api/stock/distribute` (kaynak alanı **yok**) |
| Web | `/stock/distribute` — liste + hedef + miktar + kalan + "Tümü" + onay |
| Masaüstü | Aynı ekran, **API'siz** (yerel SQLite) → **çevrimdışı çalışır** |

**🔴 İki gerçek bulgu:**
1. **Transferler geri alınmaz** (2026-08-06 kararı) — dağıtım da transfer olduğu için geri alınamaz.
   Planın "ters kayıtla geri alınır" varsayımı yanlıştı. İstisna **açılmadı**; düzeltme yolu yanlış
   depodan doğru depoya **yeni transfer**. İlk yazdığım ekran metinleri yanıltıcıydı → **düzeltildi**.
2. **Üretimde `DEPOWISE` firmasının hiç deposu yok** (0 şube). 8951,3 birim atanmamış stok var ama
   dağıtacak hedef yok → kullanıcı önce **Şubeler** ekranından depo oluşturmalı. İki arayüz de söylüyor.

**Kanıt:** **1317 / 1284 geçti / 0 kaldı / 33 atlandı** (taban 1300; **17 yeni senaryo**) · build 0 hata ·
izole üretim kopyasında doğrulandı: aşım **reddedildi**, **rollback** çalıştı, **firma toplamı korundu**.

## ✅ SON TAMAMLANAN — `SNK-11` Bakiye senkron yükünden arındırıldı (2026-08-11)

Kayıt: [`SNK_11_BAKIYE_SENKRON_YUKU.md`](SNK_11_BAKIYE_SENKRON_YUKU.md)

**Değişiklik TEK dosyada, iki satır:** `BusinessSyncService.Tables` listesinden `stock_balances`
çıkarıldı + gereksiz yetki eşlemesi kaldırıldı. **Tablo KALDIRILMADI** — yerel SQLite'ta ve sunucuda
aynen duruyor; masaüstü çevrimdışı stok işlemleri ve bakiye görüntüleme etkilenmedi.

**Neden güvenliydi:** taşınan bakiye zaten KULLANILMIYORDU — sunucu push sonrası defterden yeniden
hesaplıyor, masaüstü pull'u zaten hariç tutuyordu. STK-07 bunu kanıtlamıştı.

**Ölçülen fayda (üretim kopyası):** her senkron turunda **663 bakiye satırı / ~86 KB** artık taşınmıyor.
Paket 1807,1 KB · defter (663 hareket) yerinde.

**Kanıt:** **1325 / 1292 geçti / 0 kaldı / 33 atlandı** (taban 1317; +7 yeni senaryo) · build 0 hata ·
kasten bozuk bakiye sunucuya bulaşmıyor · çevrimdışı akışların TAMAMI çalışıyor · kopya yok.

⚠️ **3 mevcut test gerekçeli olarak yeniden yazıldı** (gevşetme değil — kilitledikleri davranış bilinçli
olarak kaldırıldı). Ayrıntı kayıtta §4.

## ✅ SON TAMAMLANAN — `RPR-01` Rapor filtre paritesi (2026-08-11)

Kayıt: [`RPR_01_FILTRE_PARITESI.md`](RPR_01_FILTRE_PARITESI.md)

**Üretim davranışı DEĞİŞMEDİ** — bu bir *koruma* işidir. Rapor verisi zaten ortak (`ReportService`),
ama filtre ARAYÜZLERİ iki tarafta elle yazılıyor: yeni filtre eklenirken biri unutulursa hiçbir şey
patlamaz, filtre o platformda **sessizce yok** olur.

**Çözüm (en küçük):** Tek doğru kaynak `ReportFilters` enum'u. Test, enum'un HER değeri için bir
kablolama satırı ister ve o satırı **4 katmanda** (Application · API · Web · Masaüstü) doğrular.
Ortak UI katmanı **kurulmadı**, üretim kodu **değişmedi** — test projesi Web/Desktop'a referans
vermediği için iki arayüzün **kaynak metni** okunur.

| Yakalanan hata türü | Durum |
|---|---|
| Filtre yalnız Web'e / yalnız masaüstüne eklenmiş | ✅ |
| Ekranda var ama **EXPORT gövdesinde** gönderilmiyor | ✅ |
| `[NotifyPropertyChangedFor]` unutulmuş (filtre yanlış raporda takılı kalır) | ✅ |
| API katalog yanıtından alan düşmüş (Web filtreyi hiç göremez) | ✅ |
| Kataloğa yeni bayrak eklenip parite tablosuna girmemiş | ✅ |

**🔴 Negatif ispat kendi testimdeki gerçek zayıflığı buldu:** ilk Web kontrolü `_sel?.UsesLocation == true`
arıyordu; bu metin istek gövdelerinde de geçtiği için **ekran bloğu silinse bile test geçiyordu**.
Token `@if (_sel?.UsesLocation ==` olarak sıkılaştırıldı. Negatif ispat olmasaydı RPR-01
**çalışmayan bir koruma** olarak "tamamlandı" sayılacaktı.

**Envanter:** 12 rapor · 10 filtre bayrağı · bir filtrenin tam bağlanması için **6 dosyada** iş var.
Mevcut 10 bayrağın **tamamı** her iki platformda tam bağlı çıktı (gerçek parite eksiği bulunmadı).

**Kanıt:** **1343 / 1310 geçti / 0 kaldı / 33 atlandı** (taban 1325; **+18 senaryo**) · build 0 hata ·
5 simüle hatanın 5'i yakalandı · çevrimdışı rapor filtreleri HTTP'siz doğrulandı.
⚠️ **Görsel (browser/XAML render) kontrolü YAPILMADI** — doğrulama kod/kaynak düzeyindedir.

## ✅ SON TAMAMLANAN — `BKM-04` Bakım malzemesinin çıktığı depo (KARAR-9, 2026-08-11)

Karar: [`DECISIONS.md` → ADR-103](../DECISIONS.md) · Analiz: [`BKM_04_LOKASYON_ANALIZI.md`](BKM_04_LOKASYON_ANALIZI.md)
Kabul kriterleri: [`TASK_BACKLOG.md` → BKM-04](TASK_BACKLOG.md)

**Sorun:** `MaintenanceService` stok yazarken lokasyonu **sabit boş** yazıyor → her bakım tüketimi
ATANMAMIŞ'a düşüyor. Üretim kodunda lokasyonu dışarıdan almayan **tek** stok yazarı.

**Analizin belirleyici bulguları:**
- `op_branch_id` bağımsız alan **değil** — `OperatingBranchId`'nin kopyası → A ≡ B.
- **API oturumu `OperatingBranchId`'yi hiç set etmiyor** → Web'de o alan **her zaman NULL**
  (`WEB-02`'nin bakım karşılığı). Oradan lokasyon türetmek Web'de hiçbir şeyi düzeltmezdi.
- Buna karşılık **iki bakım ekranı da "Tüm Şubeler"de kaydetmeyi zaten engelliyor** → kaydet anında
  somut şube her platformda garanti (masaüstünde oturumda, Web'de `Auth.BranchId`).

**KARAR-9:** oturum şubesi **varsayılan**; kullanıcı **"Malzemenin çekildiği depo"** alanından kendi
firmasına ait aktif başka bir depo seçebilir. Atanmamış hedef olarak **sunulmaz**. Depo yoksa bakım
engellenmez (ATANMAMIŞ'a düşer, 2026-08-06 korunur). `vehicles.branch_id` **kullanılmaz**;
`op_branch_id` ile **karıştırılmaz**.

⚠️ **İki kırmızı çizgi:** (1) kullanıcının depo seçimi **sessizce ezilemez**;
(2) **iptal, orijinal hareketin lokasyonuna** geri yazar — oturum şubesinden yeniden hesaplanmaz.

**Migration GEREKMEZ** · yeni senkron protokolü/tablo YOK · SNK-11 geri alınmaz.

### Uygulandı — 8 üretim dosyası (1 yeni)

| Katman | Yapılan |
|---|---|
| Servis | `NewMaintenance.StockLocationId` (opsiyonel, sona) · `EnsureLocationOwned` · defter **ve** bakiye aynı depoyu kullanır |
| **İptal** | Artık **defterden** besleniyor (`LoadUsageMovements`) → ters kayıt ORİJİNAL hareketin deposuna yazar; ekip-stoğu satırları **yapısal** olarak dışarıda; `reverses_movement_id` ile izlenebilir |
| API | 3 uçta opsiyonel `branchId` (eski istemci kırılmaz) |
| Masaüstü | Bakım + Günlük Faaliyet ekranlarına depo seçici; kural tek yerde (`StockLocationPicker`) |
| Web | Aynı seçici; liste `WriteTargets()` (Atanmamış YOK); POST'ta kullanıcının seçimi |

**`DailyActivityService` ve `MaintenanceImportService` DEĞİŞMEDİ** — kayıt modelini olduğu gibi
geçirdikleri için yeni alan kendiliğinden aktı (testle doğrulandı).

**Yanında düzeltilen:** eksik-stok uyarısı iki arayüzde de **firma geneline** bakıyordu → artık
**seçilen deponun** stoğuna bakıyor (STK-04/05'te düzeltilen aynı hata sınıfının bakımdaki ikizi).

**Kanıt:** **1387 / 1353 geçti / 0 kaldı / 34 atlandı** (taban 1343; **+44 senaryo**) · build 0 hata ·
mevcut 115 bakım/faaliyet testi **dokunulmadan** geçti · izole PostgreSQL'de doğrulandı (ters kayıt
aynı depoda + `reverses_movement_id` dolu) · çevrimdışı→senkron lokasyonu koruyor, kopya yok.

⚠️ **Görsel (tarayıcı render) kontrolü YAPILMADI.** Yerel API veritabanında hesap bilgisi olmadığı,
canlıya yazmak yasak olduğu ve parola girmem mümkün olmadığı için kapatılamadı — ayrıntı kayıtta §9.

## 📋 `STK-10` — Hareket raporu · **PLAN HAZIR, KOD BAŞLAMADI** (2026-08-11)

Plan + envanter: [`STK_10_HAREKET_RAPORU_PLANI.md`](STK_10_HAREKET_RAPORU_PLANI.md)

Envanter çıkarıldı, lokasyon semantiği koddan **doğrulandı** (tahmin yok), tasarım kararları alındı.
**Kod bilinçli olarak başlatılmadı** — gerekçe planın §9'unda (talimat §19: tek oturumda tam
doğrulanamayacak iş kodlanmaz).

**🔴 Üç bulgu:**
1. **Web'de lokasyon filtresi sessizce eksik sonuç verebilir** — süzme `limit` ile kesilmiş liste
   üzerinde **istemcide** yapılıyor. Filtre sunucuya taşınınca kapanır (STK-10 içinde).
2. **`STK-B1` STK-10'un ön koşuluydu** → ✅ **2026-08-11'de tamamlandı** (aşağıdaki bölüm).
3. **Masaüstünde lokasyon filtresi hiç yok** (Web'de var) — parite eksiği, STK-10 içinde kapanır.

**✅ KARAR (kullanıcı, 2026-08-11): SEÇENEK B.** Arama kutusu kataloğa **gerçek `Search` filtresi**
olarak girer; ekranda kalıp export dışında bırakılmaz. Ekran ve XLSX aynı filtrelenmiş kümeyi üretir.
Malzeme filtresi Search'ün yerine geçmez — ikisi birlikte bulunur. (ADR-104)

**Kalan boyut (adım 1'den itibaren):** **3** filtre bayrağı × RPR-01'in **6 katmanı** =
**18 kablolama noktası** · + 2 ekranın rapora bağlanması · + B-1 davranış düzeltmesi · + ~30 senaryo ·
+ ilk kez **6 kombinasyonda gerçek XLSX satır-satır karşılaştırması** · + izole PG sorgu planı.

## ✅ SON TAMAMLANAN — `STK-B1` Hareket türü kataloğu (STK-10 adım 0, 2026-08-11)

Sonuç kaydı: [`STK_10_HAREKET_RAPORU_PLANI.md`](STK_10_HAREKET_RAPORU_PLANI.md) §12

**8 hareket türünün 8'i artık iki platformda AYNI Türkçe adla görünüyor.** Önceden aynı hareket
**üç** ayrı yerde, **üç farklı** biçimde gösteriliyordu ve üçü de eksikti.

| `movement_type` | **Nihai etiket (Web = Masaüstü)** | Önceki |
|---|---|---|
| `opening` · `in` · `out` · `transfer` | Açılış · Giriş · Çıkış · Transfer | zaten aynıydı |
| `adjustment` | **Sayım Düzeltme** | 🔴 masaüstü "Düzeltme" ↔ diğerleri "Sayım Düzeltme" |
| `usage` | **Bakım Tüketimi** | 🔴 üçünde de **ham İngilizce** |
| `usage_reverse` | **Bakım Tüketimi İptali** | 🔴 üçünde de **ham İngilizce** |
| `reverse` | **İptal (Ters Kayıt)** | 🔴 masaüstü ham · web "İptal (ters)" · kart "İptal" |

**Çözüm:** `MovementTypeOptions` tek doğru kaynak. Web Application'a referans vermediği için proje
zaten kullandığı deseni izledik: **tek dosya, iki projede derlenir** (`ListColumns` gibi) → ayna yok,
ıraksama imkânsız. Ölü `count` dalı kaldırıldı (o bir `doc_type`).

**Kanıt:** **1411 / 1377 geçti / 0 kaldı / 34 atlandı** (taban 1387; **+24 senaryo**) · build 0 hata ·
8 türün 8'i **gerçek servislerle üretilip** ekranda ne göründüğü doğrulandı · kaynak taraması yeni bir
tür kataloğa girmezse testi **kırıyor** · migration/senkron/veri **dokunulmadı**.

⚠️ **Yan bulgu — kendi testimi düzelttim:** BKM-04'ün 4 iptal testi ters kaydı sıra indeksiyle
seçiyordu; dondurulmuş saatte `created_at` eşit olunca sıralama GUID'e düşüyor → **flaky**'ydiler
(5 koşuda ~1 kırılma, BKM-04'te şans eseri geçmişler). Tür üzerinden seçime çevrildi, 5/5 kararlı.
Üretim etkilenmedi.

⚠️ **Görsel kontrol YAPILMADI** — BKM-04'teki aynı engel (yerel API veritabanında hesap yok, canlıya
bağlanmak yasak, parola giremem). Ayrıntı ve statik risk değerlendirmesi planın §12'sinde.

## ⏸️ `STK-10` kalanı — adım 1'e GEÇİLMEDİ, iki karar netleşti (2026-08-11)

Adım 1 öncesi planın dayandığı varsayımlar koddan sınandı → **iki plan hatam çıktı** (§13) ve
kullanıcının sorduğu kesişim **karara bağlandı** (§14). Kod yazılmadı (gerekçe §15).

**🔴 D-1 — "Export limit uygulamaz" YANLIŞTI.** Sorgu ve export ucu **AYNI** `BuildReport`'u aynı
tavanla (`maxRows`, varsayılan **50.000**) çağırıyor. "Ekran = export" hedefi için bu doğru davranış;
ama kabul kriteri düzeltildi: export ekranla **aynı** tavana tabidir.

**🔴 D-2 — "Run'ın limiti SQL'e iner" YANLIŞTI.** `Run` kesmeyi **bellekte**, `Dispatch`'ten SONRA
yapıyor. Yani `StockMovements` sorgusu **kendi SQL LIMIT'ini** taşımalı ve **filtre → sırala → LIMIT**
sırası **SQL içinde** kurulmalı. (B-1 düzeltmesinin teknik karşılığı.)

**✅ D-3 — ClosedXML test projesinden erişilebilir.** RPR-01'in açık bıraktığı gerçek XLSX satır-satır
karşılaştırması STK-10'da yapılabilir.

**✅ KARAR — `BranchScope` × `Location` kesişimi:** *kapsam DIŞ SINIRDIR, lokasyon filtresi içeride
daraltır, asla genişletmez* (`WHERE kapsam AND lokasyon`). Sonuç: Depo A oturumu **Depo B filtresiyle
BOŞ** sonuç alır → yetki aşılmaz. §3'teki "A→B hem A hem B'de görünür" kuralı **kapsamı yeten**
kullanıcı için geçerlidir. Tam tablo planın §14'ünde; testle kilitlenecek.

## ✅ SON TAMAMLANAN — `STK-10a` Stok Hareketleri raporu (2026-08-11)

Sonuç kaydı: [`STK_10_HAREKET_RAPORU_PLANI.md`](STK_10_HAREKET_RAPORU_PLANI.md) §16

Hareket defteri artık **kataloglanmış bir rapor**: `Date` + `Location` filtreli, **Kaynak → Hedef**
kolonlu ve **Excel'e aktarılabilir**. (`Search`/`Material`/`MovementType` **STK-10b'nindir**.)

**🔴 En önemli bulgu: Web ve masaüstünde HİÇ KOD DEĞİŞMEDİ.** Rapor katalog-güdümlü olduğu için iki
platformun Raporlar ekranında **kendiliğinden** göründü; `Date` ve `Location` filtreleri STK-06'dan
zaten 6 katmanda bağlıydı. **RPR-01 hiç değiştirilmeden yeşil kaldı** — "yeni bayrak eklemeyen artım"
seçiminin doğrudan getirisi.

**⚡ Sorgu planı (izole PG, gerçek çıktı):** `Limit → Sort → Index Scan` — tarih filtresi mevcut
`ix_stock_movements_material` indeksini kullanıyor, lokasyon filtresi SQL'e inmiş.
➡️ **Yeni indeks EKLENMEDİ** (plan kuralı: yalnız ölçüm gerekçelendirirse). Tavan artık **SQL'de**
(`LIMIT`) — `Run`'ın bellekte kesmesi ikinci emniyet ağı (D-2 düzeltmesi uygulandı).

**🔴 XLSX boşluğu KAPANDI:** RPR-01'de "yalnız aynı servis çağrılıyor" dolaylı ispatıyla bırakılmıştı.
Artık **6 filtre kombinasyonu × 2 hat** (servis + gerçek HTTP) için XLSX **açılıp hücre hücre**
rapor sonucuyla karşılaştırılıyor.

**Kanıt:** **1452 / 1417 geçti / 0 kaldı / 35 atlandı** (taban 1411; **+41 senaryo**) · build 0 hata ·
29 çevrimdışı + 11 gerçek HTTP + 1 izole PG senaryosu · firma izolasyonu · **BranchScope × Location**
sınırı testli (Depo A oturumu + Depo B filtresi → **BOŞ**).

⚠️ **2 mevcut test gerekçeli güncellendi** (gevşetme değil — katalog sayısı 12→13, lokasyonlu rapor
listesi 2→3; ikisi de TAM EŞLEŞME ile sınanmaya devam ediyor + yeni nöbetçi eklendi). Ayrıntı §16.1.
⚠️ **Görsel render kontrolü YAPILMADI** — aynı engel (§16.5).

## ⏸️ `STK-10b` — doğrulama yapıldı, KOD BAŞLAMADI (2026-08-11)

Kayıt: [`STK_10_HAREKET_RAPORU_PLANI.md`](STK_10_HAREKET_RAPORU_PLANI.md) §18

**Zorunlu 7 doğrulamanın tamamı yapıldı → plan ile kod arasında ENGELLEYİCİ FARK YOK.**
STK-10a yerinde · ADR-104 kayıtlı · RPR-01 `Map` 10 satır · `MovementTypeOptions` 8 tür ·
`BranchScope` `AND`'leniyor · rapor iki platformda katalogdan görünüyor · testler yeşil.

**🔴 Kendi iddiamı düzelttim:** §15'te "18 kablolama noktası atomik, dilimlenemez" demiştim —
**fazla katıymış**. RPR-01 her bayrağı **kendi içinde** denetliyor; bir bayrağı tam bağlayıp
diğerlerine dokunmamak testi **yeşil** bırakıyor.
➡️ Atomik birim **1 bayrak × 6 katman = 6 nokta**. STK-10b, her adımı yeşil biten **dört** artıma
bölünebilir: **10b-1** `MovementType` (seçenek kaynağı zaten var) → **10b-2** `Search` →
**10b-3** `Material` + autocomplete → **10b-4** iki ekranın bağlanması + **B-1 düzeltmesi**.

**Neden kodlanmadı:** bu oturumda RPR-01 + BKM-04 + STK-B1 + STK-10a tamamlandı; STK-10b'nin tamamı
(18 kablolama + 2 ekran + autocomplete + ~40 senaryo + 10 XLSX kombinasyonu + PG + çoklu tam-takım)
kalan kapasiteyle güvenilir biçimde bitirilemez. Yarıda kalsa sonuç "eksik ama yeşil" değil
**KIRMIZI** olurdu (bayrak eklenip 6 katmanı bitirilmezse RPR-01 kırılır).

## ✅ SON TAMAMLANAN — `STK-10b-1` Hareket Türü filtresi (2026-08-11)

Sonuç kaydı: [`STK_10_HAREKET_RAPORU_PLANI.md`](STK_10_HAREKET_RAPORU_PLANI.md) §19

`MovementType` filtresi **6/6 katmanda** bağlandı; **RPR-01 gevşetilmeden yeşil** (14/14) ve artık
yeni bayrağı **kendi denetliyor**. Seçenekler yalnız `MovementTypeOptions`'tan (STK-B1) →
**`/api/reports/scope`'a yeni alan eklenmedi**, ikinci harita oluşmadı.

**🔴 Uygulama sırasında kendi hatamı yakaladım:** `MovementTypes`'ı önce `LocationIds`'ten **önce**
eklemiştim. Bu kayıt API'de **pozisyonel** de kuruluyor → `LocationIds` argümanı sessizce kayar,
lokasyon filtresi çalışmayı bırakırdı. Sona taşındı, alanın yanına kalıcı uyarı yorumu kondu.

**Semantik:** kanonik anahtarla sorgulanıyor (etiketle değil) · bilinmeyen anahtar **fail-closed**
(veri sızdırmıyor) · boş liste = filtre yok · **`BranchScope` genişlemiyor** (Depo A oturumu, Depo B'deki
`usage` hareketini türle isteyince bile göremiyor) · transfer **iki satır** kalıyor.

**⚡ Sorgu planı:** `movement_type` filtresi **SQL'e indi**; Limit/Sort yerinde; **yeni indeks yok**.
Testle ayrıca kanıtlandı: tavan **filtrelenmiş küme** üzerine iniyor (bellekte süzülmüyor).

**Kanıt:** **1480 / 1445 geçti / 0 kaldı / 35 atlandı** (taban 1452; **+28 senaryo**) · build 0 hata ·
23 çevrimdışı + 5 yeni HTTP + izole PG (sorgu planı dahil) · **9 XLSX kombinasyonu** hücre hücre.

⚠️ **2 mevcut test güncellendi** — ikisi de **STK-10a'da benim koyduğum kapsam nöbetçileriydi** ve
yeni filtreyi doğru yakaladılar. Gevşetilmediler: tam-eşitlik korundu, üstüne **Search (2048) ve
Material (4096) hâlâ kapalı** nöbetçileri eklendi. Ayrıntı §19.7.
⚠️ **Görsel render kontrolü YAPILMADI** — aynı engel (§19.8).

## ✅ SON TAMAMLANAN — `STK-10b-2` Serbest metin arama (2026-08-11)

Sonuç kaydı: [`STK_10_HAREKET_RAPORU_PLANI.md`](STK_10_HAREKET_RAPORU_PLANI.md) §21

`Search` filtresi **6/6 katmanda** bağlandı; **RPR-01 gevşetilmeden yeşil** (14/14). Semantik mevcut
`SearchMovements`'tan **birebir** taşındı — yeni arama mimarisi icat edilmedi.

**🔴 BULGU — belge notu aramada YOK (mevcut davranış, değiştirilmedi):**
`ApplyLine` hareket satırının `note`'unu **NULL** yazıyor; kullanıcının giriş/çıkış belgesine yazdığı
not `stock_documents.note`'a gidiyor. Mevcut arama ise **`sm.note`**'a bakıyor. Yani "not" araması
bugün yalnız **ters kayıt gerekçesi** ve **bakım tüketimi** kayıtlarını buluyor — kullanıcının stok
belgesine yazdığı notu **bulmuyor**, ekran etiketi bunu vaat etse de.
Semantiği taşıdım, **değiştirmedim**; mevcut davranışı testle kilitledim (rapor ve ekran aynı sonucu
veriyor → kayma yok). ⛔ **Kararı senin:** arama `d.note`'u da kapsasın mı? → yeni iş **`STK-B2`**.

**Büyük/küçük harf:** mutlak iddia edilmedi — `LIKE` davranışı **lehçeye bağlı** (SQLite duyarsız,
PG duyarlı). Test, **rapor ile mevcut ekranın AYNI sonucu verdiğini** kilitliyor.

**🔒 `BranchScope` genişlemiyor:** Depo A oturumu Depo B bacağını arayarak da göremiyor; yabancı firma
kaydı aramayla da çıkmıyor.

**Kanıt:** **1521 / 1486 geçti / 0 kaldı / 35 atlandı** (taban 1480; **+41 senaryo**) · build 0 hata ·
36 çevrimdışı + 5 yeni HTTP + izole PG (sorgu planı: arama `~~` ile SQL'de) · **10 XLSX kombinasyonu**.
**Yeni indeks eklenmedi** — `LIKE '%…%'` baştan joker içerdiği için B-tree kullanılamaz; trigram
gerekirdi, mevcut hacim gerektirmiyor.

⚠️ **2 mevcut test yine güncellendi** — aynı kapsam nöbetçileri, `Search`'ü de doğru yakaladılar.
Gevşetilmediler; **`Material` (4096) hâlâ kapalı** nöbetçisi duruyor.
⚠️ **Görsel render kontrolü YAPILMADI** (§21.7).

## ✅ SON TAMAMLANAN — `STK-10b-3` Malzeme filtresi + autocomplete (2026-08-12)

Sonuç kaydı: [`STK_10_HAREKET_RAPORU_PLANI.md`](STK_10_HAREKET_RAPORU_PLANI.md) §23

`Material` filtresi **6/6 katmanda** bağlandı; **RPR-01 gevşetilmeden yeşil** (14/14).
`MaterialIds` bir **liste**tir (diğer kimlik filtreleriyle aynı sözleşme) ve kaydın **SON** alanına
eklendi — pozisyonel argüman kayması artık kalıcı testle korunuyor.

**⚡ 2461 malzeme İNDİRİLMİYOR:** iki platform da **mevcut** arama desenini kullanıyor
(Web `/api/materials?search=` + `MudAutocomplete` · masaüstü yerel `Materials.List(term)`).
`/api/reports/scope` **büyümedi** — kaynak taramasıyla kilitlendi. Yeni uç açılmadı.

**⚡ Yeni indeks EKLENMEDİ:** izole PG planı, filtrenin **zaten var olan**
`ix_stock_movements_material` indeksini kullandığını gösteriyor (`Index Cond: material_id`).

**🔴 BULGU — kapsam dışı, yeni iş `RPR-02`:** HTTP hattında rapor isteği oturumun şubesini
**taşımıyor** (JWT yalnız kullanıcı+firma; `CreateSessionForUser` şube atamıyor). Yani **web'de**
şube daralması yalnız açık `branchIds` seçimiyle oluyor; giriş ekranındaki şube rapora yansımıyor.
Bu STK-10b-3'ün getirdiği bir durum **değil**, tüm raporlarda mevcut mimari — masaüstü etkilenmiyor.
**Düzeltilmedi**, karar/iş olarak ayrıldı.

**Kanıt:** **1553 / 1518 geçti / 0 kaldı / 35 atlandı** (taban 1521; **+32 senaryo**) · build 0 hata ·
25 çevrimdışı + 7 yeni HTTP + izole PG · **10 XLSX kombinasyonu** hücre hücre.

⚠️ **2 mevcut test yine güncellendi** — aynı kapsam nöbetçileri, `Material`'ı doğru yakaladılar.
Gevşetilmediler; nöbetçi **bir sonraki bite (8192) kaydırıldı** ve "yalnız `stock-movements`'ta açık"
tam-eşleşmesi eklendi.
⚠️ **Görsel render kontrolü YAPILMADI** — Raporlar ekranı giriş formunun arkasında; parolayı bir alana
yazmam (güvenlik kuralı) ve canlıya bağlanmam. Yapılmış gibi gösterilmedi (§23.6).

## ✅ SON TAMAMLANAN — `STK-10b-4` Ekranlar + B-1 · **STK-10 BİTTİ** (2026-08-12)

Sonuç kaydı: [`STK_10_HAREKET_RAPORU_PLANI.md`](STK_10_HAREKET_RAPORU_PLANI.md) §24 · Karar: **ADR-105**

**🔴 B-1 KAPATILDI.** Web ekranı lokasyonu, sunucudan gelen **limitli** listenin üzerinde
**istemcide** süzüyordu → seçilen depoya ait hareket ilk N kaydın dışındaysa **sessizce
kayboluyordu**. Filtre artık **SQL'de ve LIMIT'ten önce**. Eski davranışın aynı veride kaydı
kaybettiği, regresyon testine dönüştürüldü (hem çevrimdışı hem gerçek HTTP).

**Ekran = Rapor = XLSX.** Lokasyon/tür/arama/malzeme filtrelerinin WHERE'i tek üreteçten geliyor
(`StockMovementFilterSql`) → rapor ve ekran aynı satır kümesini vermek **zorunda**. Ekran `TableModel`'e
çevrilmedi (gereksiz yeniden tasarım olurdu); yalnız filtre mantığı birleştirildi.

**Masaüstü paritesi:** masaüstü hareket ekranına da **lokasyon filtresi** eklendi (web'de vardı,
masaüstünde yoktu — STK-10 envanterindeki parite eksiği). Ağ gerekmiyor, çevrimdışı çalışıyor.

**Kanıt:** **1589 / 1554 geçti / 0 kaldı / 35 atlandı** (taban 1553; **+36 senaryo**) · build 0 hata ·
31 çevrimdışı + 5 yeni HTTP + izole PG · 9 "ekran = rapor" + 6 "rapor = XLSX" kombinasyonu.
**Yeni indeks yok, migration yok.**

⚠️ **Görsel render kontrolü YAPILMADI** — ekran giriş formunun arkasında (§24.6).
🔎 **Yol üstünde kararsız bir test bulundu ve KÖK NEDENİ DÜZELTİLDİ** (R34): `SyncBalancePayloadTests`
senkron paketinin TAMAMINDA ham "777" metnini arıyordu; rastgele bir GUID "777" içerdiğinde kırılıyordu.
Assertion **gevşetilmedi, keskinleştirildi** (bakiye TABLOSUNUN pakette olmadığı doğrulanıyor). Retry/skip yok.

## ▶️ SIRADAKİ İŞ
**STK-10 bitti.** Sıradaki en doğru iş: **`STK-08` (KARAR-8)** — Migration064 canlıya alınmadan önce
"Atanmamış" stoğun nasıl dağıtılacağının kararı; deploy'un önündeki tek iş kuralı engeli budur.
⛔ Karar bekleyenler: **`STK-B2`** (arama `stock_documents.note`'u kapsasın mı) ·
**`RPR-02`/R33** (web isteği oturum şubesini taşımıyor) · **`KARAR-8`**.

## ⛔ Karar bekleyenler
| İş | Neyi bekliyor |
|---|---|
| `STK-08` | **KARAR-8** — "Atanmamış" stok nasıl dağıtılacak (öneri: kullanıcı transferle) |
| `BKM-01…03` | KARAR-4 (bakımda negatif stok mu, onay kapısı mı) |
| `TMZ-02`, `BRM-01`, `YTK-01…04` | YET-01 (rol değişince yetkiler) |
| `SNK-05` | Çevrimdışı onay çakışması |

## 📌 Canlı ortam — **YAYIN TURU 2026-08-12** (commit `a0d5c91`)
API `depowise-erp` **v151** · Web `depowise-web` **v177** · Neon PG **17.10** · **canlı şema 64**
(migration **çalışmadı** — kod kataloğu da 64, uygulanacak yeni sürüm yoktu) ·
**Masaüstü sürümü `1.0.137`** (paket sunucuda, SHA-256 `5ADE0CE5…D9F6BE`, 85,5 MB) ·
1 aktif firma (`Oze İnşaat`, 5 şube) · Test **1591/0/35** · Release derlemesi 0 hata

> Yayın sırası DEPLOYMENT.md'ye uygun: **API → Web → masaüstü publish → masaüstü güncelleme paketi.**
> Production'a **hiçbir INSERT/UPDATE/DELETE yapılmadı**; STK-08 dağıtımı **yapılmadı**.

## ⚠️ Açık riskler
- **Deploy edilince** stoğun neredeyse tamamı **"ATANMAMIŞ"** görünecek → KARAR-8 alınmadan kullanıcıya
  sürpriz olur. Gerçek rakamlar (üretim kopyasında ölçüldü): **DEPOWISE firması 8951,3** (663 satır,
  gerçek depo stoğu 0) · üç firma toplamı **8953,3**. Web artık bunu "Atanmamış" olarak AÇIKÇA gösteriyor
  ve "Tüm Şubeler" ile karıştırmıyor; dağıtım yine de KARAR-8 bekliyor.
- Migration, bakiyesi defterle **uyuşmayan** bir veritabanında **bilinçli olarak durur** (sessiz bozulma
  yerine açık hata). Böyle bir **masaüstü** veritabanı varsa güncelleme başlamaz → önce yeniden hesaplama
  gerekir. Üretim PG kopyasında uyuşmazlık **yok**.
- Masaüstü **paketi yayınlanmadı** — Grup 6 masaüstü düzeltmeleri kullanıcıya ulaşmadı.
- Branch **`master`'a birleştirilmedi**.
- **66 malzemede negatif stok** zaten mevcut (ADR-086 devralınan eksik stok); migration **1 yeni** negatif
  üretir (defterin söylediği) — toplam 67.

---

## ✅ SON TAMAMLANAN — `STK-MB` Çok şubeli stok doğrulaması + `D-1` / `H-1` (2026-08-12)

Gerçek kullanıcı testine geçmeden önce çok lokasyonlu stok modeli uçtan uca izole ortamda doğrulandı
(`MultiBranchStockScenarioTests`, 22 test). Denetimde **iki gerçek kod açığı** bulundu ve kapatıldı.

### D-1 — `Transfer` hedefi boş bırakılabiliyordu
`StockService.Transfer` kaynağın boşluğunu reddediyor, **hedefin** boşluğunu kontrol etmiyordu. Boş hedef
`ApplyDelta` yolunda sessizce `""` (ATANMAMIŞ) kovasına çevriliyordu → transfer, stoğu depodan çıkarıp
**"lokasyonu bilinmiyor" durumuna geri atabiliyordu** (STK-08'in çözdüğü belirsizliğin yeniden üretimi).
API ve masaüstü hedefi zaten zorunlu tutuyordu → kullanıcıdan tetiklenemiyordu; eksik olan **servis
katmanındaki savunma katmanıydı** (masaüstü bu servisi çevrimdışı doğrudan çağırır).
`DistributeUnassigned` ile **aynı** kural/mesaj eklendi.

### H-1 — Dağıtım listesi sessizce kesiliyordu (+ sıfır-satır tuzağı)
1. `ListUnassigned` varsayılan 500 satır döndürüyordu; web/masaüstü limiti yükseltmiyor ve **kaç kaydın
   gizlendiğini söyleyen bilgi taşımıyordu**. Canlıda ATANMAMIŞ'ta 676 listelenebilir satır var.
2. **Asıl tehlike:** `qty == 0` elemesi `LIMIT`'ten **sonra** C#'ta yapılıyordu → sıfırlar limitten yer
   kapıyordu. Dağıtılan kalem ATANMAMIŞ'ta 0 satırı olarak kaldığı için **ikinci turda liste sıfırlarla
   dolup gerçek kalemleri dışarı itebilirdi**. İzole testte kanıtlandı: 500 sıfır + 10 pozitif kalemde
   eski yol **hiçbir pozitif kalem** döndürmüyor.

Çözüm: sıfır filtresi `LIMIT`'ten **önce SQL'e** indi (`SqlDialect.NumericValue`, iki lehçe) · yeni
`UnassignedPage` (toplam / dağıtılabilir / gizli / kullanıcı metni) · ekran varsayılanı **2000** ·
web ve masaüstü **aynı** cümleyi gösteriyor. Eski `ListUnassigned` imzası ve varsayılanı **korundu**.

**Ölçülen (izole, canlı dağılımın birebir kopyası):** 677 ham satır → 676 listelenebilir · **görünen 676** ·
**dağıtılabilir 610** · **gizli 0** · arama ile erişilebilen pozitif **610** · negatif 66 (görünür,
dağıtılamaz) · silinmiş 1 (hiç görünmez).

**Test:** `UnassignedListLimitTests` (15 test) — A/B/C/D sınırları (499·500·501·676), F arama, çok turlu
dağıtım, geriye uyum, yetki, ölçekli sıfır ("0.000"), web/masaüstü sözleşme taraması.
Rapor: [`docs/tests/CokSubeliStok_Test_Report.md`](../tests/CokSubeliStok_Test_Report.md).

**Tüm paket: 1591 geçti · 0 başarısız · 35 atlandı (hepsi PostgreSQL — ortamda PG sunucusu yok).**

## ▶️ SIRADAKİ İŞ
**Yayın turu:** Web deploy + **masaüstü publish/update paketi** (kullanıcı kararı 2026-08-12 —
geliştirme turu kapatıldı, gerçek kullanıcı testine geçiliyor). Migration **gerekmiyor**: kod kataloğu
ve canlı şema **ikisi de 64**. STK-08 gerçek dağıtımı **yapılmayacak** — kullanıcı canlıda kendi test edecek.

---

## 2026-08-13 — MASAÜSTÜ GUI DOĞRULAMA TURU (şube kapsamı)

**Masaüstü GUI otomasyonu kuruldu ve 28 maddelik checklist gerçek UI etkileşimiyle koşturuldu:
25 GEÇTİ · 0 BAŞARISIZ · 3 koşturulmadı (madde 8 · 11 · 27).** Windows UI Automation ile Avalonia
penceresi sürülüyor; ek paket YOK. Ortam tamamen izole (yerel API + `DEPOWISE_ENVIRONMENT=GuiTest`),
üretime hiç bağlanılmadı.

**GUI testi ALTI GERÇEK ÜRÜN HATASI buldu — hepsi düzeltildi + 15 regresyon testi eklendi:**

| Kod | Hata | Neden önemliydi |
|---|---|---|
| GUI-01 | Şube kapsamı **masaüstünde fiilen yoktu** (paket `user_scopes` taşımıyor + `AuthService.Login` oturuma koymuyor) | Kapsamı A+B olan kullanıcı **yetkisiz Şube C'ye giriş yapabiliyordu**; makine o şubeye bağlanıyordu |
| GUI-02 | Elle cari hareketi `branch_id = NULL` yazılıyordu | Şubesiz satır her şubede görünür → **A'nın bakiyesi B'nin ekstresinde ve altı raporun tamamında** |
| GUI-02b | Ters kayıt şubesiz + kapsam kontrolsüz | Yetkisiz şubenin hareketi iptal edilebiliyordu |
| GUI-03 | "Tüm yetkili şubeler" etiketi ile veri çelişiyordu | Etiket A+B derken yalnız çalışma şubesi geliyordu |
| GUI-04 | Rapor şube filtresinde yetkisiz şube listeleniyordu | Deny-by-default ihlali (masaüstü + `/api/reports/scope`) |
| GUI-05 | "Şube Kapsamı" bölümü **sessizce kayboluyordu** | Kapsam yerelden, kullanıcı/yetki sunucudan okunuyordu; hata da eziliyordu |

Düzeltmelerde **ikinci bir kapsam mantığı üretilmedi** — hepsi tek otorite `BranchAccess` üzerinden yürür.
Masaüstü **ve** web/API birlikte düzeltildi (platform önceliği kuralı).

Rapor: [`docs/tests/Sube_Kapsami_GUI_Test_Report.md`](../tests/Sube_Kapsami_GUI_Test_Report.md) ·
Checklist: [`docs/tests/Masaustu_GUI_Checklist.md`](../tests/Masaustu_GUI_Checklist.md)

### ▶️ SIRADAKİ TEK İŞ (güncellendi)
**Yayın turu — ama GUI-01/02 nedeniyle önce şu karar gerekir:** GUI-02 düzeltmesi yalnız BUNDAN SONRA
girilecek hareketleri şubeye bağlar. Canlıda **şubesiz (branch_id NULL) mevcut cari hareketler varsa**
bunlar hâlâ her şubede görünür. Yayın öncesi canlı veride şubesiz hareket olup olmadığı sayılmalı;
varsa kullanıcıya sorulup toplu şube ataması yapılmalıdır (veri düzeltme kararı kullanıcınındır).

---

## 2026-08-18 — ŞUBE YAPISI + SIFIRLAMA + YETKİ DEVRİ TURU

Kullanıcı talebi üzerine üç alanda **salt-okunur tam analiz** yapıldı (16 bulgu), ardından hepsi
düzeltildi. Analiz: [`docs/ANALIZ_SUBE_VE_SIFIRLAMA.md`](../ANALIZ_SUBE_VE_SIFIRLAMA.md)

**Kullanıcının şartı:** *"Web'den firma verisi sıfırladığımda babam mevcut kullanıcısıyla girip sıfırdan
veri girebilsin; şubeler ve kullanıcılar silinmesin."* → Sunucu tarafı bu şartı zaten karşılıyordu,
**masaüstü tarafı SIF-01 hatasıyla bozuyordu.** Düzeltildi ve testle kilitlendi.

### Düzeltilen bulgular

| Kod | Bulgu | Neden önemliydi |
|---|---|---|
| **SIF-01** 🔴 | Yerel sıfırlama, ADR-083'ün **tam silme** fonksiyonunu çağırıyordu (firma+kullanıcı+şube+yetki siliniyordu) | Sıfırlama sonrası o makinede **çevrimdışı giriş imkânsız** hâle geliyordu — şantiyede internet yoksa kilitlenme |
| **SIF-03** 🟡 | Silme listesi senkron sözleşmesinden okunuyordu → 7 tablo + 8 satır tablosu atlanıyordu | Sıfırlama sonrası **eski stok bakiyeleri / muayene / sayaç** öksüz kalıyordu |
| **SIF-06** 🟡 | Şablonlar (malzeme + araç) senkronda **hiç taşınmıyordu** | Masaüstünde açılan şablon web'e, web'deki masaüstüne gitmiyordu |
| **ŞB-01** 🔴 | Şube aynası `kind` + `parent_id` taşımıyordu | **Kullanıcının bildirdiği hata:** üst şube seçilip kaydediliyor, ekran hemen "—" gösteriyordu (sunucudaki veri doğruydu) |
| **ŞB-04** 🔴 | `parent_id` hiçbir yerde OKUNMUYORDU | "Üst Şube" yalnız bir etiketti: kapsam yayılmıyor, rapor toplamıyordu |
| **ŞB-02** 🟠 | Döngü koruması yoktu (A→B, B→A) | ŞB-04 ile ağaç gezildiğinden sonsuz döngü riski |
| **ŞB-03** 🟠 | Silinmiş üst şube listede görünüyor + alt şubesi olan şube silinebiliyordu | Kopuk referans |
| **ŞB-06** 🟠 | Web "+" hızlı ekleme `companyId` göndermiyordu | Süper admin başka firmadayken şube **kendi firmasına** açılıyordu |
| **YET** 🟠 | "Yerel Sıfırlama" yetki ağacında YOKTU, devredilemiyordu | Kullanıcı isteği: menü maddesi olsun + devredilebilsin |

### Yeni yetki katmanı — "AÇIK-VERİLİR" (`AppModules.IsExplicitOnly`)
Sistemde yalnız iki uç vardı: *hiç devredilemez* (süper-admin-only) veya *admin bypass ile örtük açık*.
Kullanıcının istediği üçüncü katman eklendi: **devredilebilir ama asla örtük verilmeyen.**
Zincir: **Süper Admin / Kısıtlı Süper Admin → Admin → Personel** — her kademe yalnız kendisinde olanı verir.
İlk üyesi: `local_reset` — **Yerel Veri Sıfırlama** (web ekranı `/local-reset`).

### Yanında bulunan ek açık (kapsam dışıydı, kapatıldı)
İçe aktarım oturum **kopyası** (web + masaüstü) `ScopeBranchIds`/`HomeBranchId` taşımıyordu →
içe aktarım yolunda kullanıcı **kısıtsız** sayılıyor, kapsam dışı şubeye kayıt basılabiliyordu.

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Tam test takımı | **2018 toplam · 1983 geçti · 0 başarısız · 35 atlandı** (bilinen PostgreSQL atlamaları) |
| Yeni testler | 43 (`BusinessResetCoverageTests` 5 · `BranchHierarchyTests` 11 · `TemplateSyncTests` 4 · `BranchParentScopeTests` 11 · `ExplicitOnlyModuleTests` 12) |
| Derleme | Masaüstü + Web + API — **0 hata** |

### ✅ YAYINLANDI (2026-08-18)
**API + Web + masaüstü 1.0.139 canlıya alındı.** Ayrıntı aşağıdaki "YAYIN TURU" bölümünde.

---

## 2026-08-18 — YAYIN TURU (API v? / Web / masaüstü 1.0.139)

Yayın öncesi **tam doğrulama** yapıldı; ardından `DEPLOYMENT.md` sırasına göre API → Web → masaüstü.

### Yayın öncesi testler

| Koşum | Sonuç |
|---|---|
| Tam takım (SQLite) | **2018 toplam · 1983 geçti · 0 başarısız · 35 atlandı** |
| Çözüm derlemesi (Release) | **0 hata** (40 uyarı — mevcut) |
| PostgreSQL — üretim lehçesi, **izole `depowise_test`** üzerinde | **20 / 20 geçti** |

**PostgreSQL sınıf sınıf:** `PostgresPurgeTests` 3/3 (SIF-03'ü doğrudan kapsar: *iş verisini siler,
firma+kullanıcı KORUR*) · `PostgresEndToEndTests` 1/1 (giriş → `BranchTree` PG'de) ·
`PostgresSyncRecoveryTests` 1/1 (SIF-06 apply) · `PostgresEditLockTests` 6/6 (şube düzenleme kilidi) ·
`PostgresMigrationTests` 1/1 · `PostgresStockMovementOrderingTests` 5/5 · `PostgresStockConcurrencyTests` 3/3.

> ⚠️ **Yanıltıcı bir ara sonuç kayda geçirilir:** PG testleri ilk kez topluca koşturulduğunda **9 test
> başarısız** oldu. İnceleme sonucu sebep **ağ kopması** çıktı (`SocketException: bilinen böyle bir ana
> bilgisayar yok` = DNS çözülemedi; ayrıca "bağlantı uzaktaki ana bilgisayar tarafından kapatıldı").
> Aynı testler bağlantı düzeldikten sonra sınıf sınıf koşturulduğunda **tamamı geçti**. Ürün hatası
> DEĞİLDİ. Bu testler zaten normal takımda atlanır (`ApiTestHost` `DEPOWISE_PG_URL`'i süreç genelinde
> null'lar) → taban koşumda da koşmuyorlardı, yani gerileme değildi.

### Yayın adımları ve kanıtlar

| Adım | Komut | Kanıt |
|---|---|---|
| Uçuş öncesi | `flyctl secrets list` | `DEPOWISE_PG_URL` **Deployed** → PostgreSQL aktif, SQLite'a düşmedi |
| Uçuş öncisi | `df -h /data` | %45 dolu · 506 MB boş (ADR-070 disk riski yok) |
| API | `flyctl deploy --config fly.toml --ha=false` | makine `started` · `/health` **200** |
| DB teyidi | `/api/releases/latest` | Gerçek veri döndü (1.0.138) → SQLite geri dönüşü OLMADI |
| Web | `flyctl deploy --config fly.web.toml --ha=false` | `/` **200** · yeni ekran `/local-reset` **200** |
| Masaüstü publish | `-p:Version=1.0.139` | 270 dosya — 1.0.138 ile **birebir aynı ağaç** |
| Paket | `Compress-Archive` | `DepoWise-desktop-1.0.139.zip` · **89.919.610 bayt** |
| Yayın | `node scripts/publish_release.mjs` | `/api/releases/latest` = **1.0.139** |
| Checksum | yerel ↔ sunucu | `D7AF2D3D9E47BED6CB22651F28525AC615901DDBFEC5555D704DF5E95D5D44D5` — **EŞLEŞTİ** |
| İndirme ucu | `/api/releases/1.0.139/download` | **200** · 89.919.610 bayt |
| Yayın sonrası disk | `df -h /data` | %45 (eski paketler otomatik temizlendi — ADR-070) |

**Migration ÇALIŞMADI** — bu turda şema değişikliği yoktu; üretim şema sürümü **68**'de kaldı.
Production veritabanına hiçbir INSERT/UPDATE/DELETE yapılmadı. DEPOWISE ve Öze İnşaat verisine dokunulmadı.

### ▶️ SIRADAKİ TEK İŞ
**Firma verisi sıfırlama işlemi artık güvenle yapılabilir** — ama SIF-02 hâlâ açık olduğu için
operasyonel adım zorunlu: kullanıcılara programı **tamamen kapattırın** → web'den sıfırlayın →
tekrar açtırın. Makineler açılışta 1.0.139'a güncellenip temizliği uygulayacaktır.

---

## 2026-08-18 — DENETİM YAYIN TURU (API + Web + masaüstü 1.0.140) · Migration **069**

Kapsamlı denetimin (7 tur, 18 bulgu) **tamamı** canlıya alındı.

### Yayın öncesi doğrulama
| Koşum | Sonuç |
|---|---|
| Tam takım (SQLite) | **2083 toplam · 2048 geçti · 0 başarısız · 35 atlandı** |
| PostgreSQL (izole `depowise_test`) | 6/6 — Migration069 iki lehçede de çalışıyor |
| Çözüm derlemesi (Release) | **0 hata** |
| Bu denetimde eklenen test | **69** |

### 🔐 Migration öncesi yedek
Neon **dal (branch) anlık kopyası** alındı — kopyala-yaz olduğu için anında ve maliyetsiz:
- Proje: `alpdepo` (`autumn-morning-75319830`) · Kaynak: `production` (`br-bold-snow-asagtawc`)
- **Yedek dalı: `pre-migration-069` (`br-bold-tooth-asxxn1dv`)** — 2026-08-18T18:29:30Z
- Geri dönüş gerekirse bu dal migration ÖNCESİ tam veriyi taşır.

### Yayın adımları ve kanıtlar
| Adım | Kanıt |
|---|---|
| Uçuş öncesi | `DEPOWISE_PG_URL` secret var · `/data` %45 (505 MB boş) · API/Web 200 |
| **API** | `flyctl deploy -c fly.toml --ha=false` → makine `started`, `/health` **200** |
| **Migration 069** | **Uygulandı.** Kanıt: `ServerServices:115` başlangıçta `MigrationRunner.Run()` çağırır; `Run()` içinde **try/catch YOKTUR** → migration hata verse uygulama hiç açılmazdı. Uygulama sağlıklı ve gerçek veriyle yanıt veriyor. Şema sürümü: **68 → 69** |
| DB teyidi | `/api/releases/latest` gerçek veri döndü → SQLite'a geri dönüş OLMADI |
| **Web** | `flyctl deploy -c fly.web.toml --ha=false` → `/` 200 · `/local-reset` `/branches` `/reports` `/trash` hepsi **200** |
| Masaüstü publish | 270 dosya — 1.0.139 ile **birebir aynı ağaç** |
| Paket | `DepoWise-desktop-1.0.140.zip` · **89.923.623 bayt** |
| Yayın | `/api/releases/latest` = **1.0.140** |
| Checksum | `7A86A68A6E328F3108324FDD8D3868404A3EC49018015F6D5DCAF5DE66E2F624` — **EŞLEŞTİ** |
| İndirme ucu | **200** |
| Yayın sonrası disk | %45 (eski paketler otomatik temizlendi — ADR-070) |

### ⚠️ Kullanıcıların fark edeceği davranış değişiklikleri
1. **Şubeyle sınırlı kullanıcılar** artık diğer şubelerin araç/personel/stok hareketi/talep verisini
   ne raporlarda ne de kendi bilgisayarlarında görecek (gizlilik düzeltmesi — SNK-A7, DEN-E1/E2).
2. **Üst şubeye yetkili kullanıcılar** alt şubeleri de görecek ve onlara **yazabilecek** (ŞB-04).
3. Yetkisi olmayan kullanıcılar web'de **Çöp Kutusu geri yükleme**, **Excel'e Aktar**, **stok/yakıt
   iptal** butonlarını artık **göremeyecek** (DEN-F1/F1b — sunucu bunları zaten engelliyordu).
4. Masaüstünde iptal edilen **cari hareket ve stok belgesi artık web'de de iptal görünecek**
   (SNK-A1/A2 — en kritik düzeltme; öncesinde web'de bakiye yanlıştı).

### ▶️ SIRADAKİ TEK İŞ
Kullanıcı talebi: **"Şube / Şantiye kendi tanım ekranı dışında oluşturulamamalı."**
İnceleme yapıldı (kod okuması, üretime dokunulmadı): şube oluşturabilen **3** nokta var —
(1) Şube/Şantiye Tanım ekranı ✔ · (2) aynı ekrandaki "+" kısayolu (yalnız ad sorar, yarım kayıt) ·
(3) **Firma ekranındaki "İlk Şube / Şantiye Adı" alanı** (web + masaüstü) ← **kuralın ihlali burada**.
Kaldırılması tek başına yetmez: kod "şubesiz firmaya kullanıcı eklenemez" kuralını uyguluyor →
akış kararı gerekiyor. Kullanıcı yayın sonrası bu konuya bakacağını belirtti.

---

## ✅ SON TAMAMLANAN — `MNU` Menü / Ekran Yönetimi (2026-08-18)

**Kullanıcı isteği:** web'e özel bir menü/ekran yönetim ekranı — ekran sırası, üst menü ataması,
üst menü adı/sırası, platform seçimi, menüde aktif/pasif, görünen ekran adı.

**Envanterde çıkan gerçek:** istenen 7 maddeden **2'si (platform + aktif/pasif) G5 ile 2026-08-12'de
zaten yapılmıştı.** Eksik olan yalnız **menü düzeni**: ad · üst menü · sıra.

**Karar (ADR-109):** ayrı ekran açılmadı; mevcut `/screen-visibility` genişletildi ve adı
**"Menü / Ekran Yönetimi"** oldu. Route · ekran anahtarı · yetki modülü **değişmedi**.
Grup kimliği için katalog başlığı "değişmez sistem anahtarı" kabul edildi → **katalogda tek satır bile
değişmeden** yeniden adlandırma güvenli hâle geldi. Büyük refactor gerekmedi.

**Yeni yapı:** `Migration070_MenuLayout` (`screen_menu_layout`, `menu_group_layout`) · `MenuLayout`
(saf çözümleyici, web projesine linklendi — masaüstü ve web **aynı sıralama kodunu** çağırır) ·
`MenuLayoutService` (`ScreenVisibilityService` deseni) · `/api/screens/layout/manage` · `/layout` ·
`/layout/reset`. **Satır yoksa katalog varsayılanı** → migration sonrası menü birebir aynı.

### Bu turda bulunan ve düzeltilen 5 gerçek hata
| Kod | Hata | Etki |
|---|---|---|
| **MNU-B1** | Platform ayarı gerçek masaüstü makinelere **hiç inmiyordu** | "Masaüstü" kutusu etkisizdi (ADR-110) |
| **MNU-B2** | Süper admin yönetim ekranını kapatıp **kendini kalıcı kilitleyebiliyordu** | Geri dönüşü yoktu (ADR-111) |
| — | `HasFlag(Desktop\|Web)` yüzünden **tek platformlu 14 ekran** listede yoktu | Yönetilemiyorlardı |
| — | Satır taşınınca `<select>` **eski değeri** gösteriyordu (Blazor konum bazlı yeniden kullanım) | Yanlış grup görünüyordu |
| — | Reddedilen değişiklikten sonra **onay kutusu yanlış durumda** kalıyordu | Kullanıcı kapattığını sanabilirdi |

### Doğrulama
- Release build: **API + Web + Masaüstü → 0 hata**.
- Otomatik test: **+48 senaryo**; tam takım **2098 geçti / 0 başarısız / 35 atlandı** (toplam 2133).
- Gerçek tarayıcı GUI testi: **17 maddenin 16'sı ✅, 1'i 🟡** (yetkisiz kullanıcı GUI'de değil, sunucu
  tarafında doğrulandı — üç uç da 401). Ayrıntı: `docs/tests/Menu_Ekran_Yonetimi_Test_Report.md`.
- **Üretime hiçbir işlem yapılmadı** (INSERT/UPDATE/DELETE/DDL/Migration/Deploy/Publish/Restart = 0).

### Sıradaki tek iş
Kullanıcı onayıyla **deploy** (API + Web + masaüstü sürümü). Deploy öncesi Neon yedek şubesi alınmalı;
Migration 070 üretim şemasını 70'e çıkaracak.

---

## 🚀 YAYIN — `MNU` Menü / Ekran Yönetimi canlıda (2026-08-19)

**API + Web deploy edildi. Masaüstü publish bu turda BİLİNÇLİ olarak yapılmadı** (kullanıcı kararı;
ayrı tur). Canlıdaki masaüstü sürümü **1.0.140** olarak kalıyor.

### Deploy öncesi doğrulama
| Kontrol | Sonuç |
|---|---|
| Release build (API · Web · Masaüstü) | **0 hata** |
| Tam takım (SQLite) | **2098 geçti · 0 başarısız · 35 atlandı** |
| PostgreSQL (izole `depowise_test`) | **48 geçti · 0 başarısız · 0 atlandı** |
| Web GUI (17 madde) | **17/17 geçti** — geçen turda kısmi kalan "yetkisiz kullanıcı" maddesi gerçek personel hesabıyla tamamlandı (4 uç da **403**) |
| Masaüstü GUI | **geçti** — gerçek Avalonia penceresi, UI Automation |
| Üretim yedeği | alındı + `pg_restore -l` ile doğrulandı |

### ⭐ MNU-B1'in GERÇEK MASAÜSTÜNDE kanıtı
İzole ortamda web'den yapılan düzen değişiklikleri **gerçek masaüstü uygulamasının menüsünde** göründü:
grup adı `Yakıt` → **Akaryakıt**, ekran adı → **Yakıt Çıkışı**, sıra (**Özet** başa), taşıma
(**Depo Girişleri** → Yönetim). Bu değişiklikler eskiden masaüstüne **hiç ulaşmıyordu**.

### Deploy sonucu
| | |
|---|---|
| API | `depowise-erp` v158 · makine `started` · `/health` 200 · `/api/public/companies` 200 |
| Üretim firması | `ed271d0ca2b04a73b97f5025a53a04b4 / Oze İnşaat` ✔ |
| Migration 070 | **uygulandı** — `70 \| menu_layout \| 2026-08-18 21:45:15+00` |
| Şema sürümü | **69 → 70** |
| Web | `depowise-web` v181 · `/`, `/login`, `/screen-visibility`, `/reports`, `/parties` → **200** |

### Üretim verisi (salt-okunur, READ ONLY + ROLLBACK)
| | Önce | Sonra |
|---|---|---|
| Firma | 3 | **3** |
| Kullanıcı | 8 | **8** |
| Şube | 10 | **10** |
| Stok hareketi | 663 | **663** |
| Tablo | 75 | **77** (yalnız Migration 070'in 2 tablosu) |

Yeni tabloların üçü de **0 satır** → hiçbir firmanın menüsü değişmedi, herkes katalog varsayılanıyla
devam ediyor. Özellik **kullanılmaya hazır ama kapalı** durumda.

### Sıradaki tek iş
Masaüstü sürüm publish'i (ayrı tur). Masaüstü kullanıcıları menü düzenini ancak yeni sürümle görecek;
**1.0.140 çalışmaya devam eder** (ayarlar inmez, katalog varsayılanı geçerlidir — bozulma yok).
