# AKTİF DURUM

> Son güncelleme: **2026-08-11** · Bu dosya **her iş sonunda** güncellenir.

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

## ▶️ SIRADAKİ İŞ
**`STK-10`'un kalanı — planın §8 adım 1'inden başla** (adım 0 bitti).
Karar B ile: 3 filtre bayrağı × RPR-01'in 6 katmanı = **18 kablolama noktası** + 2 ekranın rapora
bağlanması + **B-1 davranış düzeltmesi** (sunucu tarafı lokasyon filtresi) + ~30 senaryo +
**6 kombinasyonda gerçek XLSX satır-satır karşılaştırması** + izole PG sorgu planı + tarayıcı render.
Kabul kriterleri kalıcı olarak planın **§10**'unda.

## ⛔ Karar bekleyenler
| İş | Neyi bekliyor |
|---|---|
| `STK-08` | **KARAR-8** — "Atanmamış" stok nasıl dağıtılacak (öneri: kullanıcı transferle) |
| `BKM-01…03` | KARAR-4 (bakımda negatif stok mu, onay kapısı mı) |
| `TMZ-02`, `BRM-01`, `YTK-01…04` | YET-01 (rol değişince yetkiler) |
| `SNK-05` | Çevrimdışı onay çakışması |

## 📌 Canlı ortam
API `depowise-erp` v149 · Web `depowise-web` v175 · Neon PG **17.10** · **canlı şema 63**
(64 henüz **deploy edilmedi** — dalda duruyor) · 3 firma · 8 kullanıcı · 6 lokasyon · 2461 malzeme ·
667 stok hareketi · Test 1411/1377/0/34 · Build 0 hata

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
