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

## ✅ SON TAMAMLANAN — `STK-05` Masaüstü + çevrimdışı lokasyon (2026-08-11)

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

## 🟡 DEVAM EDEN — `STK-06` Rapor lokasyon boyutu · ENVANTER+PLAN TAMAM, KOD BAŞLAMADI

Plan: [`STK_06_UYGULAMA_PLANI.md`](STK_06_UYGULAMA_PLANI.md) — sonraki oturum **doğrudan §8'den** başlar.

**Envanter sonucu:** 11 raporun **2'si** lokasyon boyutu gerektiriyor (**Stok Durumu**, **Stok Sayım**);
2 malzeme raporu firma toplamıyla **zaten doğru**; kalan 7 rapor stok miktarı kullanmıyor.
**Dashboard'da düzeltme GEREKMİYOR** — STK-02'deki `StockTotalSubquery` tek uygulama, kopyası yok;
STK-04'te gerçek üretim kopyasıyla doğrulandı.

**Kararlar (plan §4):** `ReportFilters.Location` **ayrı bayrak** olacak — mevcut `Branch` filtresi
`op_branch_id` (kaydı işleyen şube) demek, stok lokasyonu DEĞİL, ikisi birleştirilmeyecek ·
filtre boşken **bugünkü davranış birebir** korunacak (regresyon yok) · export ayrı sorgu kullanmıyor,
filtreyi otomatik alacak · yeni indeks gerekmiyor (Migration064'te var).

⚠️ **Kod neden başlamadı:** Talimat "kapasite tüm işi DOĞRULAMAYA yetmeyecekse kodlamaya başlama" diyor.
Bu oturumda STK-02…05 tamamlandı; STK-06'nın kabul ölçütü tam doğrulama (build + 1267 test + yeni testler +
iki lehçe + parite + çevrimdışı + gerçek veri) ve kalan kapasite bunu garanti etmiyordu. Yarım bırakılmış
stok değişikliği değerleri **sessizce yanlış** gösterir → başlanmadı.

## ▶️ SIRADAKİ İŞ
**`STK-06` — plan §8 adım 1'den başla:** `ReportFilters.Location` bayrağı → `ReportRequest.LocationIds` →
`ReportService.StockStatus` + `StockCount` → API → Web `Reports.razor` → masaüstü `ReportsViewModel`+XAML →
16 senaryo → tam doğrulama → kontrol dosyaları.

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
667 stok hareketi · Test 1267/1234/0/33 · Build 0 hata

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
