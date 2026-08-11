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

## ✅ SON TAMAMLANAN — `SNK-12` Masaüstünde depo listesi tazeleme (2026-08-11)

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

## 🟡 DEVAM EDEN — `STK-08` Atanmamış stok dağıtımı · PLAN TAMAM, KOD BAŞLAMADI

Plan: [`STK_08_UYGULAMA_PLANI.md`](STK_08_UYGULAMA_PLANI.md) — sonraki oturum **doğrudan §1 KARAR (T-1)'den**
koda başlayabilir. **Çalışma ağacında yarım kod YOK.**

**🔴 Analizde bulunan kritik engel:** mevcut `Transfer` servisi ATANMAMIŞ'ı kaynak olarak **kabul etmiyor**
— üstelik üç ayrı yerde:
1. Boş kaynak `ArgumentException` ile reddediliyor.
2. `EnforceOwnBranch` şubeye bağlı kullanıcıda boş kaynağı **sessizce kendi şubesine çeviriyor**
   → dağıtım yanlış depodan düşerdi (**sessiz veri bozulması**).
3. Şube-bazlı negatif kalkanı boş lokasyonda **çalışmıyor** → "10 varken 11 dağıt" engellenmezdi.

**Karar (T-1):** `Transfer` gevşetilmeyecek (herhangi bir istemcinin kazara lokasyonsuz transfer üretmesine
kapı açardı). Bunun yerine AYRI ve DAR bir giriş noktası: `DistributeUnassigned` — **aynı** belge/hareket
makinesini kullanır, hareket türü **`transfer`** kalır (yeni tür yok), kendi yeterlilik kontrolünü yapar.

**KARAR-8 (kalıcı):** otomatik dağıtım YOK; kullanıcı gerçek transfer hareketleriyle dağıtır.

## ▶️ SIRADAKİ İŞ
**`STK-08` — plan §1 KARAR (T-1)'den başla:** `StockService.DistributeUnassigned` → 2 API ucu
(`GET /api/stock/unassigned`, `POST /api/stock/distribute`) → Web ekranı → masaüstü ekranı (çevrimdışı)
→ 30 senaryo → tam doğrulama → kontrol dosyaları.

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
667 stok hareketi · Test 1300/1267/0/33 · Build 0 hata

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
