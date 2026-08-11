# GÖREV LİSTESİ (BACKLOG)

> Son güncelleme: **2026-08-11** · Durumlar: `SIRADA` · `BEKLEMEDE` · `ENGELLİ` · `GELİŞTİRMEDE` · `TAMAMLANDI` · `ERTELENDİ`
> Maliyet: **A** şimdi/maliyetsiz · **B** opsiyonel · **C** canlıya geçişte · **D** gelir sonrası

---

> ⚠️ **SIRA DEĞİŞİKLİĞİ (2026-08-11, KARAR-7=A):** Mimari bağımlılık nedeniyle **FAZ C (depo bazlı stok)
> öne alındı**. FAZ A ve FAZ B görevleri **silinmedi** — aşağıda aynen duruyor ve FAZ C içindeki uygun
> boşlukta ya da FAZ C sonrasında yapılacak. Hiçbiri iptal değildir.

## FAZ A — Kullanıcı bug'ları + yetki tamamlama *(sırası ertelendi, iptal DEĞİL)*

### `YTK-05` — Yetki sıfırlama/toplu güncelleme butonu · A · **SIRADA**
**Sorun:** Yetkiler ekranında yalnız "Kaydet" var (`Permissions.razor:36`, `PermissionsView.axaml:25`).
Bir kullanıcının yetkilerini toptan kaldırmak için tüm kutular tek tek temizlenmek zorunda.
**Yapılacak:** "Tümünü Temizle" (ağaçtaki tüm kutuları kaldır) + "Geri Al/Yeniden Yükle". Kaydetme yine
tek kapıdan (`PermissionService.SaveForUser`) geçer; delegasyon tavanı ve düzenleme kilidi **korunur**.
**Kabul:** Web + masaüstü aynı davranış · 409 kilidi bozulmamış · yetkisiz kullanıcı kullanamaz · test.

### `UIX-01` — Tablo satır seçimi · A · BEKLEMEDE
**Sorun:** Satırdaki **yazıya** tıklayınca seçim bazen çalışmıyor; boşluğa tıklamak çalışıyor.
**Şüphe:** Metin öğesi tıklamayı yutuyor (`SelectableTextBlock` masaüstünde metin seçimi yapıyor;
web'de `MudTd` içi eleman event propagation'ı kesiyor olabilir).
**Yapılacak:** Önce **kök neden** tespiti (tek ekranda değil, ortak bileşende). Masaüstü: `DataGridView` /
`ListBox Classes="Table"` (31 ekran). Web: `DwDataGrid`/`DataList`/`CrudList` + `OnRowClick` (5 ekran).
Çözüm **ortak bileşen** düzeyinde olmalı; ekran ekran yama yapılmayacak.
**Kabul:** Yazıya tıklayınca da satır seçilir · metin kopyalama gereken yerlerde davranış korunur.

### `YTK-06` — Yeni ekranın yetki kataloğuna otomatik girmesi · A · BEKLEMEDE
**Sorun:** `AppModules.All` elle yazılan 37 elemanlı sabit dizi. Unutulursa ekran hiçbir yetki ağacında
görünmez. 4 yetki ağacı + menü **aynı** kataloğu kullanıyor → sorun çoklu yer değil, **unutma**.
**Yapılacak (maliyetsiz, sağlam):** Bir **doğrulama testi** — web rotalarını (`@page`) ve masaüstü menü
anahtarlarını tarayıp `AppModules.All` ile karşılaştırır; kataloğa eklenmemiş ekran varsa **test kırılır**.
Böylece insan hatası derleme/test aşamasında yakalanır. (Reflection/source generator gerekmez.)
**Kabul:** Katalogsuz yeni ekran eklendiğinde test kırmızı olur ve hangi ekran olduğunu söyler.

### `YTK-08` — Delegasyon tavanı regresyon testi · A · BEKLEMEDE
**Durum:** Kural **zaten uygulanmış** (`GrantableLimit` + `ClampModule` + `RoleAssignmentGuard`).
**Yapılacak:** API seviyesinde kalıcı test — aktör kendinde olmayan modülü/butonu veremez; şablonla da
veremez; UI atlatılarak API'ye doğrudan istek atılsa da veremez.

---

## FAZ B — Ekran görünürlük yönetimi

### `GRN-01` — Web/masaüstü ekran görünürlüğü yönetimi · A · BEKLEMEDE
**İhtiyaç:** "Ekran A → Masaüstü açık / Web kapalı" gibi ayarların **yönetim ekranından** yapılabilmesi.
**Tasarım (önerilen):** `screen_platforms(company_id NULL|firma, module_key, web bool, desktop bool)`.
NULL firma = platform varsayılanı. Menü kurucu (`MenuBuilder`) ve sayfa kapıları **yetki ∧ görünürlük**
olarak birleştirir. **Yetki ile karıştırılmaz:** yetki "kim", görünürlük "nerede". Görünürlük kapalıysa
o ortamda ekran **hiç** görünmez ama yetki verisi bozulmaz.
**Not:** Bilinçli web-only ekranlar (Kalıcı Silme, Rol/Firma Yetki Kontrol vb.) bu tabloya **varsayılan
kapalı** olarak taşınır → bugün koda gömülü olan fark **veriye** taşınmış olur.
**Kabul:** Yeni ekran eklendiğinde varsayılan kayıt otomatik oluşur · yetki ağacı etkilenmez · test.

---

## FAZ C — Depo bazlı stok 🔵 **AKTİF** *(KARAR-7 = A)*

Tasarım + migration planı: [`FAZ_C_DEPO_BAZLI_STOK_TASARIM.md`](FAZ_C_DEPO_BAZLI_STOK_TASARIM.md)
Görev tablosu: [`MASTER_ROADMAP.md`](MASTER_ROADMAP.md) → FAZ C (STK-00…08, STK-B1, TRF-01) · hepsi **A** sınıfı

### `STK-00` — Migration güvenlik kanıtı · ✅ **TAMAMLANDI** (2026-08-11)
Gerçek production yedeğinin izole kopyası üzerinde, defterden `GROUP BY (material_id, COALESCE(branch_id,''))`
ile üretilen lokasyon bazlı bakiyelerin **mevcut tek bakiyelerle toplamının birebir eşleştiği** kanıtlandı:
664 eski satır → 665 yeni satır · **uyuşmayan 0** · defterde/bakiyede eksik 0 ·
migration'ın ürettiği yeni negatif **1** (66 negatif zaten mevcut, ADR-086).
**Sonuç: veri kaybı riski YOK, veri uydurma gerekmiyor.**

### `STK-01` — `stock_balances` şema değişimi · ✅ **TAMAMLANDI** (2026-08-11)
`Migration064_StockBalanceLocation`: `(company_id, material_id, location_id)` birincil anahtar ·
`location_id TEXT NOT NULL` (`''` = ATANMAMIŞ, çünkü PG'de PK kolonu NULL olamaz) · defterden C#/decimal
ile yeniden hesaplama · **migration içi doğrulama adımı** (toplam eşleşmezse transaction geri alınır) ·
SQLite'ta tablo yeniden kurulur (PK değişimi ALTER ile olmaz).
**`STK-02` ile AYNI iş biriminde etkinleştirildi** (tek başına etkinleştirmek stoğu sessizce yanlış gösterirdi).

### `STK-02` — Tüm okuma/yazma yolları lokasyon farkında · ✅ **TAMAMLANDI** (2026-08-11)
16 üretim çağrı noktası dönüştürüldü: **4 yazma** (CAS artık `ON CONFLICT(company_id, material_id, location_id)`) ·
**3 skaler + 1 toplu okuma** (C#'ta `decimal` toplama; SQL `SUM` kullanılmaz — SQLite'ta float hatası verir) ·
**8 JOIN** → `SqlDialect.StockTotalSubquery` (malzeme başına TEK satır; `DISTINCT` ile gizleme YOK).
Yanında bulunan **gerçek hata**: sayım sistem miktarını firma genelinden okuyup düzeltmeyi şubeye yazıyordu → düzeltildi.

**Kanıtlar:** 17 yeni senaryo (`tests/DepoWise.Tests/StockLocationTests.cs`) · tam takım **1223/1190/0/33** ·
izole PG kopyasında migration provası (667 hareket · 664→665 bakiye · **uyuşmayan 0** · toplam korundu) ·
dolu SQLite v63→v64 yükseltmesi kayıpsız · doğrulama kapısının gerçekten durdurup geri aldığı kanıtlandı ·
dönüştürülen sorgular PG'de çalıştırıldı (liste 2459 satır = malzeme sayısı → **satır çoğaltma yok**).

⚠️ **Kullanıcının bileceği sonuç:** 666/667 hareket lokasyonsuz olduğu için migration sonrası stoğun
neredeyse tamamı **"ATANMAMIŞ"** görünecek (8953,3 birim). Dağıtım **KARAR-8** ile ele alınacak; veri uydurulmayacak.

⚠️ **Yeni risk (masaüstü):** migration, bakiyesi defterle uyuşmayan bir veritabanında **bilinçli olarak
durur** (sessiz bozulma yerine açık hata). Böyle bir masaüstü veritabanı varsa güncelleme başlatılamaz →
önce sunucu-otoriteli yeniden hesaplama (`RecomputeBalances`) gerekir. Üretim PG kopyasında uyuşmazlık YOK.

### `STK-03` — API lokasyon boyutu · ✅ **TAMAMLANDI** (2026-08-11)
Envanter + sözleşme: [`STK_03_API_LOKASYON_PLANI.md`](STK_03_API_LOKASYON_PLANI.md)

**🔴 Asıl bulgu — lokasyon sahiplik doğrulaması YOKTU.** Stok yazma yolları gönderilen `branchId`'nin
firmaya ait olduğunu kontrol etmiyordu. STK-02'den beri lokasyon `stock_balances`'ın **birincil anahtar**
kolonu → yabancı kimlik yazılsaydı o satır hiçbir firmanın ekranında düzeltilemezdi.
**Çözüm:** `EnsureLocationOwned` — `RunDocumentOnce`'ın tek geçiş noktasında (4 yazma yolu birden) +
açılış stoğunda. **Servis katmanında**, API'de değil: masaüstü bu servisi çevrimdışı çağırıyor.

**Sözleşme:** 3 farklı anlam = 3 ayrı uç. `/balance/{id}` (firma toplamı) **hiç değişmedi** →
eski Web aynen çalışır. **YENİ:** `/balance/{id}/locations` (kırılım + `total`) ve
`/balance/{id}/location?locationId=` (tek lokasyon). Hareket listesine 4 lokasyon alanı eklendi (sona).

**İstemci envanteri:** Web 5 sayfada stok uçlarını kullanıyor (`JsonElement`+`TryGetProperty` → eklenen
alanlar bozmaz). **Masaüstü hiçbir stok ucunu kullanmıyor** — yerel servis + `business-push/pull`.

**Kanıt:** **1240/1207/0/33** (taban 1223) · 15 HTTP + 2 çevrimdışı senaryo · build 0 hata ·
sync kodu **değiştirilmedi** (senaryo 19 hâlâ doğru olduğunu kanıtlıyor) · N+1 yok.

➡️ **STK-04'e devredilen bağımlılık:** `receive`/`issue` için lokasyonu **zorunlu** yapmak bir UI kararıdır
(bugün "Tüm Şubeler" oturumu `branchId=null` gönderiyor); API sözleşmesi hazır, dayatma STK-04'te.

---

## FAZ D — Ön muhasebe alan hazırlığı

### `MUH-01` — Cari + maliyet merkezi + belge alanları · A · FAZ C'ye bağlı
Malzeme alışı, yakıt, bakım ve şantiye giderine `cari_id`, `maliyet_merkezi (şube/şantiye)`, `belge_no/tarih`
alanları. **FAZ C migration'ları ile birlikte** yapılır ki tek geçişte bitsin ve geçmiş veri boş kalmasın.

---

## FAZ E — Senkron ölçeklenme

| ID | İş | Maliyet |
|---|---|---|
| `SNK-06` | Girişte tam pull → kalıcı imleçle delta (`LoginViewModel.cs:441`) | A |
| `SNK-07` | Snapshot sayfalama (batch/chunk) | A |
| `SNK-08` | Yanıt sıkıştırma (gzip) | A |
| `SNK-09` | Delta ölçütü monoton sunucu sırası | A |
| `SNK-10` | Silinen kayıtların delta ile taşındığı testi | A |
| `SNK-05` | **KARAR BEKLİYOR** — çevrimdışı onay sunucuya yansısın mı? | — |

---

## FAZ F — Güncelleme

`GNC-01` otomatik güncelleme davranışı · `GNC-02` **API↔istemci sürüm uyumu** · `GNC-03` disk/paket saklama politikası

## FAZ G — Kalan parite / rapor

`PRT-02` ekran adı eşleme · `RPR-01` rapor envanteri · `P-1` masaüstü "Bağı Kaldır" ·
Personel/Muayene filtre+export · Personel 200 kayıt tavanı

## FAZ H — Ön muhasebe modülü

`MUH-02` cari hesap (müşteri/tedarikçi, borç/alacak) · `MUH-03` kasa/banka + tahsilat/ödeme ·
`MUH-04` gider dağıtımı → şantiye maliyeti · `MUH-05` ön muhasebe raporları
**Kapsam dışı:** e-Fatura/e-Arşiv, beyanname, yasal defter (D sınıfı).

## FAZ I — Test / performans

`TST-01` 33 atlanan test · index denetimi · N+1 taraması · liste sayfalama tamamlama

## FAZ J — Canlıya geçiş

Güvenlik sertleştirme · API sürümleme kararı · yük testi

---

## Devredilen teknik borçlar (fazlanmadı — kapanmadı)

| ID | Kısa | Sınıf |
|---|---|---|
| `G6-10` | `/api/vehicles/models` brandId doğrulanmıyor | ⚪ |
| `G6-11` | Süper admin başka firmanın şubesini silemiyor | ⚪ |
| `G6-12` | Admin, başka adminin yetki matrisini okuyabiliyor | ⚪ |
| `G6-13` | Sistem Logu filtreleri istemci tarafında | ⚪ |
| `G6-14` | `SetLocked` `branches`'i kabul ediyor | ⚪ |
| `G6-15` | Lookup `Rename` mükerrer ad kontrolü yok | ⚪ |
| `G6-16` | Şube/kullanıcı JOIN'lerinde firma süzgeci yok | ⚪ |
| `G6-17` | Şablon güncelleme/sürüm/restore yok | ⚪ |
| `G6-18` | Web Çöp Kutusu parolayı bellekte tutuyor | ⚪ |
| `G6-19` | Tanım ve matrislerde düzenleme kilidi yok | ⚪ |
| `G6-21` | Şube silme koruması alt şubeleri kapsamıyor | ⚪ |
| `G6-22` | Masaüstü Çöp Kutusu parolası yerel doğrulanıyor | ⚪ |
| `G6-24` | `ListBrands`'te ölü `brand_type IS NULL` koşulu | ⚪ |
| `H-6` | Masaüstü sunucu adresi **7 dosyada** tekrar | 🟠 |
| `H-7` | `Contracts.cs:6` eskimiş `/api/v1` yorumu | ⚪ |
| `GRP3-JOIN` | `MaintenanceService:290,366` JOIN firma süzgeci | ⚪ |
| `brands/vehicle_models JOIN` | firma süzgeci | ⚪ |
| `500→400` | Zorunlu query parametresi eksikken 500 | ⚪ |
| `WEB-01b` · `GUV-01b` · `TLP-B5` · `MUA-01/02` · `G2-08` · `TMZ-01/03` | muhtelif | ⚪ |
| `WEB-02` | Web'de şube kapsamı çalışmıyor → `STK-05` ile birleşti | 🟠 |

### `STK-04` — Web lokasyon desteği · ✅ **TAMAMLANDI** (2026-08-11)
Plan/envanter: [`STK_04_WEB_LOKASYON_PLANI.md`](STK_04_WEB_LOKASYON_PLANI.md)

**🔴 Üç gerçek hata (hepsi Web'de) bulundu ve düzeltildi:** sayım `branchId` göndermiyordu (fark
ATANMAMIŞ'a yazılıyordu) · sayım ekranı firma geneli toplamı "sistem stoğu" diye gösteriyordu ·
açılış stoğu deposuz gönderiliyordu (canlıdaki 663 lokasyonsuz açılışın sebebi).

**Yapılan:** `LocationOptions` servisi (oturumda tek indirme) · `Stock` ("Tüm Şubeler"de zorunlu depo
seçici — eskiden hiç işlem yapılamıyordu; bakiye çipi seçili depo) · `StockCount` (sayılan depo açık +
`/count-sheet`) · `StockMovements` (depo kolonu `Kaynak → Hedef` + lokasyon filtresi) ·
`Materials` (kartta Toplam + kırılım, açılış deposu). `Daily`/`Dashboard`/diğerleri bilinçli olarak
değiştirilmedi veya doğrulandı.

**Yeni uç:** `GET /api/stock/count-sheet` · `POST /api/materials` → `openingLocationId` (opsiyonel,
eski istemciler bozulmaz).

**Kanıt:** **1254/1221/0/33** (taban 1240; 14 yeni senaryo) · build 0 hata · gerçek üretim kopyasında
doğrulandı (DEPOWISE ATANMAMIŞ **8951,3**; üç firma toplamı 8953,3 — değer değiştirilmedi).

➡️ **Devredilen bulgu `B-1`:** bakım malzeme tüketimi `branch_id=NULL` yazıyor → ATANMAMIŞ'a düşüyor.
UI'da bakım deposu seçimi yok; **uydurulmadı**. Ayrı iş: `BKM-04` (STK-05 sonrası).

### `STK-05` — Masaüstü + çevrimdışı lokasyon desteği · ✅ **TAMAMLANDI** (2026-08-11)
Plan/envanter: [`STK_05_DESKTOP_OFFLINE_PLANI.md`](STK_05_DESKTOP_OFFLINE_PLANI.md)

**Yapısal bulgu:** masaüstünde stok için ayrı veri katmanı **yok** — ortak `StockService` çağrılıyor.
STK-01…03 masaüstünde zaten yürürlükteydi; bu faz **arayüz + eksik parametre** işiydi.

**🔴 4 hata düzeltildi** (Web'dekilerin masaüstü ikizleri): sayım `branchId` göndermiyordu ·
sayımda sistem miktarı firma genelindendi · açılış stoğu deposuzdu · bakiye çipi firma geneliydi.

**Eklendi:** "Sayılan Depo" alanı · malzeme kartında DEPO KIRILIMI (tek sorgu) · hareket listesinde
DEPO/ŞANTİYE kolonu (`Kaynak → Hedef`; ortak `LocationFlowText` → Web ile aynı metin).

**🔒 Çevrimdışı mimari korundu · senkron kodu değiştirilmedi.**
**Kanıt:** 1267/1234/0/33 (taban 1254; 13 yeni senaryo) · çevrimdışı→senkron lokasyonu koruyor ·
online→offline→online döngüsünde kopya yok · şirket izolasyonu çevrimdışı da geçerli ·
v63→v64 + rollback kapısı yeniden doğrulandı.

➡️ **Devredildi:** `SNK-11` (`stock_balances` push paketinden çıkarılsın — gereksiz yük, zararsız) ·
`BKM-04` (bakım tüketimine depo seçimi).

### `STK-06` — Rapor lokasyon boyutu · ✅ **TAMAMLANDI** (2026-08-11)
Plan: [`STK_06_UYGULAMA_PLANI.md`](STK_06_UYGULAMA_PLANI.md) — sonraki oturum **doğrudan §8'den** başlar.

**Envanter:** 11 raporun **2'si** lokasyon gerektiriyor (**Stok Durumu** · **Stok Sayım**) ·
2 malzeme raporu firma toplamıyla zaten doğru · 7 rapor stok miktarı kullanmıyor ·
**dashboard'da düzeltme gerekmiyor** (STK-02 alt sorgusunun kopyası yok, üretim verisiyle doğrulandı) ·
export ayrı sorgu kullanmıyor → filtreyi otomatik alır.

**Karar:** `ReportFilters.Location` **ayrı bayrak** — mevcut `Branch` filtresi `op_branch_id` demek
(stok lokasyonu değil); ikisi birleştirilmeyecek. Filtre boşken bugünkü davranış birebir korunacak.

### `RPR-01` — Rapor filtre UI'si parite testi · A · **YENİ** (STK-06 envanterinden)
Filtre UI'si "katalogdan otomatik gelir" diye belgelenmiş ama gerçekte Web (`Reports.razor`) ve masaüstü
(`ReportsViewModel`+XAML) blokları **elle** yazılıyor → yeni filtre eklenirken biri unutulursa **sessiz
parite kaybı**. `ReportDescriptor.Uses*` bayraklarını iki platformun filtre bloklarıyla karşılaştıran test.

### `STK-10` — "Stok Hareketleri" raporu · A · **YENİ** (STK-06 envanterinden)
Stok Hareketleri bugün yalnız **ekran**; katalogda rapor değil → Excel'e aktarımı yok. Depo bazlı stokta
`Kaynak → Hedef` kolonlu, tarih + depo + malzeme filtreli bir hareket dökümü doğal ihtiyaç.

### `STK-09` — Lokasyon bazlı dashboard · B · **YENİ** (ihtiyaç doğarsa)
Bugünkü dashboard firma toplamıyla doğru çalışıyor. "Depo seçip o deponun KPI'larını görme" ayrı bir üründür.

**STK-06 SONUÇ (2026-08-11):** `ReportFilters.Location` + `ReportRequest.LocationIds` eklendi.
**Stok Durumu** iki modlu (filtre boş → eski davranış birebir; depo seçili → kırılım + Depo kolonu +
decimal toplam). **Stok Sayım**'a "Sayılan Depo" kolonu + filtre. Web + masaüstü birlikte; masaüstü
lokasyon listesi **yerelden** (çevrimdışı). Export ayrı sorgu kullanmadığı için filtreyi otomatik aldı.
Dashboard'a dokunulmadı. **1281/1248/0/33** · build 0 hata · izole PG üretim kopyasında doğrulandı.

### `STK-11` — Eski float artığı miktarlar · B · **YENİ** (STK-06 ölçümünden)
Üretim verisinde `0.31999999999999995` / `-0.21999999999999997` gibi **eski float artığı** bakiye değerleri
var. Firma geneli rapor 6 ondalıkta kesip gürültüyü temizliyor, lokasyon kırılımı ham değeri taşıyor →
iki yol arasında **2×10⁻¹⁷** fark. Stok için anlamsız büyüklük ama defterden yeniden hesaplama ile
normalize edilebilir. **Veri dokunuşu** olduğu için ayrı iş.

### `STK-07` — Senkron sertifikasyonu · ✅ **TAMAMLANDI** (2026-08-11)
Kayıt: [`STK_07_SENKRON_SERTIFIKASYONU.md`](STK_07_SENKRON_SERTIFIKASYONU.md)

11 senaryo **gerçek HTTP senkron uçlarıyla** koşturuldu (masaüstü ayrı yerel SQLite ile temsil edildi).
Kanıtlanan: lokasyon senkronda kaybolmuyor · transferin iki bacağı da taşınıyor · idempotency (aynı paket
3 kez → kopya yok) · offline→online döngüsü temiz · yakınsama hareket kimlikleri dahil · şirket izolasyonu
çevrimdışı da geçerli · **bakiyenin otoritesi defter** (kasten bozulan bakiye senkronla düzeldi) ·
**delta pull gerçekten delta** (güncel sürümden sonrası boş) · hayalet lokasyon satırı yok.
**Senkron kodu DEĞİŞTİRİLMEDİ.** 1281 → **1292/1259/0/33** · build 0 hata.

### `SNK-12` — Masaüstünde depo listesi tazeleme · A · **YENİ** (STK-07 bulgusu)
`branches` iş-senkronu (business-push/pull) kapsamında **değil** — web-otoriteli, ayrı org uçlarından
geliyor. Depo bazlı stokta sonucu: web'de açılan yeni depo, masaüstüne org senkronu inmeden **stok
işleminde kullanılamıyor** (`EnsureLocationOwned` reddeder). Hata değil ama kullanıcıya "depo listem
eksik" dedirtir. Org senkronu sonrası liste tazelensin + kullanıcıya görünür olsun.

### `ARV-01` — Yayın betiği yerel arşiv temizliği · ✅ **TAMAMLANDI** (2026-08-11)
`scripts/publish_release.mjs` artık paketi sunucuya **başarıyla yükledikten sonra** yerelde yalnız
**en yeni 3 sürümü** tutuyor (zip + açılmış klasör birlikte). Sunucudaki eşi ADR-070'te zaten vardı;
yerelde yoktu ve 88 sürüm birikip **28 GB** yemişti. Temizlik yayını asla başarısız saymaz.
