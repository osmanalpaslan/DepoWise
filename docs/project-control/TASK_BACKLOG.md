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

### `RPR-01` — Rapor filtre UI'si parite testi · ✅ **TAMAMLANDI** (2026-08-11)
Kayıt: [`RPR_01_FILTRE_PARITESI.md`](RPR_01_FILTRE_PARITESI.md)

Filtre UI'si "katalogdan otomatik gelir" diye belgelenmiş ama gerçekte Web (`Reports.razor`) ve masaüstü
(`ReportsViewModel`+XAML) blokları **elle** yazılıyor → yeni filtre eklenirken biri unutulursa **sessiz
parite kaybı**.

**Çözüm:** `ReportFilters` enum'undan sürülen parite testi — her bayrak için bir kablolama satırı
zorunlu, satır 4 katmanda (Application · API · Web · Masaüstü) doğrulanıyor. **Ortak UI katmanı
kurulmadı, üretim davranışı değişmedi** (yalnız bir yanlış yorum satırı düzeltildi).
Test projesi Web/Desktop'a referans vermez → iki arayüzün **kaynak metni** okunur.

**Envanter:** 12 rapor · 10 filtre bayrağı · bir filtre 6 dosyada bağlanıyor. Mevcut 10 bayrağın
**tamamı** iki platformda tam bağlı çıktı — gerçek parite eksiği bulunmadı.

**1325 → 1343/1310/0/33** · build 0 hata · 5 simüle hatanın 5'i yakalandı · çevrimdışı HTTP'siz doğrulandı.

**🔴 Bulgu:** Negatif ispat testi **kendi tarayıcımdaki** zayıflığı buldu — ilk Web kontrolü istek
gövdelerindeki metne takılıyor, ekran bloğu silinse bile geçiyordu. Sıkılaştırıldı.
⚠️ Görsel (browser/XAML render) kontrolü **yapılmadı**; doğrulama kod/kaynak düzeyindedir.

### `BKM-04` — Bakım malzemesinin çıktığı depo · ✅ **TAMAMLANDI** (2026-08-11)
Karar: [`DECISIONS.md` → ADR-103](../DECISIONS.md) · Analiz: [`BKM_04_LOKASYON_ANALIZI.md`](BKM_04_LOKASYON_ANALIZI.md)

**Sorun:** `MaintenanceService` stok yazarken lokasyonu **sabit boş** yazıyor (`branch_id=NULL` +
`Unassigned`) → her bakım tüketimi ATANMAMIŞ'a düşüyor. STK-08 geçmişi temizleme aracını verdi ama
bu yol **yenisini üretmeye devam ediyor**. Üretim kodunda lokasyonu dışarıdan almayan **tek** stok yazarı.

**Analizin belirleyici bulguları:** `op_branch_id` bağımsız alan değil, `OperatingBranchId`'nin kopyası
(A ≡ B) · **API oturumu `OperatingBranchId`'yi hiç set etmiyor** → Web'de o alan her zaman NULL ·
buna karşılık **iki bakım ekranı da "Tüm Şubeler"de kaydetmeyi zaten engelliyor** → kaydet anında
somut şube her platformda garanti.

**Karar (KARAR-9):** oturum şubesi **varsayılan**, kullanıcı **"Malzemenin çekildiği depo"** alanından
kendi firmasına ait aktif başka bir depo seçebilir. Atanmamış hedef olarak sunulmaz. Depo yoksa bakım
engellenmez (ATANMAMIŞ'a düşer). `vehicles.branch_id` KULLANILMAZ; `op_branch_id` ile karıştırılmaz.

#### Kabul kriterleri
- [x] `NewMaintenance`'a lokasyon **opsiyonel** olarak (sona) eklendi; mevcut çağrılar kırılmadı
- [x] Lokasyon verilmişse `EnsureLocationOwned` ile doğrulanıyor (yabancı/bilinmeyen/pasif → **403**)
- [x] Lokasyon verilmemişse **ATANMAMIŞ** (geriye dönük davranış birebir korunuyor)
- [x] `stock_movements.branch_id` **ve** `stock_balances.location_id` **aynı** seçilen lokasyonu kullanıyor
- [x] Aynı bakımın tüm stok yazımları **tek transaction**
- [x] **Kullanıcı seçimi sessizce ezilmiyor** — oturum şubesine/araç şubesine/`op_branch_id`'ye dönülmüyor
- [x] **İPTAL: ters hareket ORİJİNAL hareketin `branch_id`'sine yazıyor** (oturum şubesinden yeniden
      hesaplanmıyor) — özel regresyon testiyle kilitli
- [x] Masaüstü: varsayılan + değiştirilebilir seçici, **çevrimdışı çalışıyor**, yeni API bağımlılığı yok
- [x] Web: `RequireBranchAsync` korunuyor · seçici var · `Auth.BranchId` yalnız **varsayılan** için ·
      kullanıcı seçimi tekrar ezilmiyor · POST gövdesinde `branchId`
- [x] Günlük Faaliyet bakım + ilave işlem yolları **aynı** lokasyon semantiğini kullanıyor
- [x] Excel içe aktarım yeni sözleşmeye doğru bağlı (oturum şubesi taşıma davranışı korunuyor)
- [x] `from_team_stock=1` satırları **hiçbir** stok hareketi üretmiyor (değişmedi)
- [x] Negatif stok kuralı, idempotency, metadata düzenleme davranışı **değişmedi**
- [x] **Migration yok · yeni senkron protokolü yok · yeni tablo yok** · SNK-11 geri alınmadı
- [x] Mevcut test grupları korunuyor (gevşetme/silme yok) + 23 yeni senaryo
- [x] Web/masaüstü paritesi kanıtlandı · çevrimdışı→senkron→PG'de lokasyon korunuyor

**Kapsam dışı:** geçmiş ATANMAMIŞ bakım tüketimleri **taşınmaz/tahmin edilmez** · mevcut stoklar
yeniden dağıtılmaz · KARAR-8 kapsamındaki stoklara dokunulmaz. Yalnız **yeni** tüketim akışı.

**SONUÇ (2026-08-11):** 8 üretim dosyası (1 yeni: `StockLocationPicker`). `DailyActivityService` ve
`MaintenanceImportService` **değişmedi** — kayıt modelini olduğu gibi geçirdikleri için yeni alan
kendiliğinden aktı. **İptal artık DEFTERDEN besleniyor** (`LoadUsageMovements`): lokasyon yalnız orada
tutulduğu için "orijinal hareketin deposuna geri yaz" kuralının tek doğru uygulaması bu; ekip-stoğu
satırlarının atlanması da bayrakla değil **yapısal** oldu. Ters kayda `reverses_movement_id` yazılıyor
(geri izlenebilirlik; yeni kolon değil).
**1343 → 1387/1353/0/34** (+44 senaryo) · build 0 hata · mevcut 115 bakım/faaliyet testi dokunulmadan
geçti · izole PostgreSQL'de doğrulandı.
⚠️ **Görsel (tarayıcı render) kontrolü YAPILMADI** — gerekçesi kayıtta §9.

### `STK-10a` — "Stok Hareketleri" raporu · ✅ **TAMAMLANDI** (2026-08-11) · `STK-10b` ⏳ SON ARTIM KALDI
Plan + envanter: [`STK_10_HAREKET_RAPORU_PLANI.md`](STK_10_HAREKET_RAPORU_PLANI.md)

> **Durum (2026-08-12): ✅ STK-10 TAMAMEN BİTTİ.** `STK-10a` · `10b-1` (Hareket Türü) ·
> `10b-2` (Arama) · `10b-3` (Malzeme) · `10b-4` (ekranlar + **B-1**) — hepsi tamam, RPR-01
> gevşetilmeden yeşil. Ekran = rapor = XLSX yapısal olarak garanti (tek filtre üreteci, ADR-105).
> Karar bekleyen iki iş: **`STK-B2`** (arama `stock_documents.note`'u kapsasın mı) ve
> **`RPR-02`**/R33 (web isteği oturumun şubesini taşımıyor — tüm raporları etkiler, §23.5).

Stok Hareketleri bugün yalnız **ekran**; katalogda rapor değil → Excel'e aktarımı yok. Depo bazlı stokta
`Kaynak → Hedef` kolonlu, tarih + depo + malzeme + tür filtreli hareket dökümü doğal ihtiyaç.

**Envanterden çıkan 3 bulgu:** (1) Web'de lokasyon filtresi `limit`'le kesilmiş liste üzerinde
**istemcide** çalışıyor → sessizce eksik sonuç verebilir · (2) **`STK-B1` ön koşul oldu**: `movement_type`
7 değer üretiyor, `TypeText` 5'ini çeviriyor → kullanıcı ham "usage" görüyor (BKM-04 görünür yaptı) ·
(3) masaüstünde lokasyon filtresi **hiç yok** (parite eksiği). Üçü de STK-10 içinde kapanacak.

**Lokasyon semantiği koddan doğrulandı** (planda tablo): `direction>0` → `branch_id` HEDEF ·
`direction<0` → `branch_id` KAYNAK · transfer **iki ayrı satır** kalır. Filtre: `branch_id=X OR
branch_from_id=X` → A→B transferi hem A hem B filtresinde görünür.

✅ **KARAR VERİLDİ (kullanıcı, 2026-08-11): SEÇENEK B** — arama kutusu kataloğa **gerçek `Search`
filtresi** olarak girer; ekran ve XLSX aynı filtrelenmiş kümeyi üretir. Malzeme filtresi Search'ün
yerine geçmez.

**🔴 İkinci envanter düzeltmesi:** `movement_type` **8 değer** (7 değil — `reverse` atlanmıştı) ve
Web ile masaüstünün etiket haritaları **ıraksamış** (`adjustment`/`reverse` farklı, `usage`/
`usage_reverse` ikisinde de ham, Web'de ölü `count` dalı). `STK-B1` bunu tek kaynağa bağlar.

**Filtreler:** `Date | Location | Search | Material | MovementType` → 3 yeni bayrak ×
RPR-01'in 6 katmanı = **18 kablolama noktası**.

**Kabul kriterleri:** planın **§10**'unda kalıcı olarak kayıtlı (katalog/sözleşme · veri-semantik ·
filtre-arama · export · platform-ortam başlıkları altında madde madde).

**✅ Adım 0 (`STK-B1`) TAMAMLANDI** (2026-08-11) — 8 hareket türü tek kaynağa bağlandı, ham İngilizce
kaçağı ve Web↔masaüstü ıraksaması kapandı. Kayıt: planın §12'si.

**⏸️ Adım 1'e geçilmedi (2026-08-11).** Öncesinde doğrulama yapıldı ve **iki plan hatası** düzeltildi
(§13): export ekranla **AYNI** satır tavanına tabi (ayrı yol yok) · `Run`'ın limiti **bellekte**
uygulanıyor → sorgu kendi SQL LIMIT'ini taşımalı, **filtre→sırala→LIMIT SQL içinde**.
Ayrıca **`BranchScope` × `Location` kesişimi karara bağlandı** (§14): kapsam DIŞ SINIR, lokasyon
içeride daraltır → Depo A oturumu Depo B filtresiyle **BOŞ** alır, yetki aşılmaz.

🔒 **İş bölünemez:** RPR-01, filtre bayrağının 6 katmanda birden bağlı olmasını zorunlu kılıyor →
18 kablolama noktası **atomik**. Önerilen bölünme (onay bekliyor): **STK-10a** (rapor + Date/Location
+ gerçek XLSX doğrulaması, **yeni bayrak yok**) → **STK-10b** (3 bayrak + 2 ekran + B-1).

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

### `SNK-12` — Masaüstünde depo listesi tazeleme · ✅ **TAMAMLANDI** (2026-08-11)
`branches` iş-senkronu (business-push/pull) kapsamında **değil** — web-otoriteli, ayrı org uçlarından
geliyor. Depo bazlı stokta sonucu: web'de açılan yeni depo, masaüstüne org senkronu inmeden **stok
işleminde kullanılamıyor** (`EnsureLocationOwned` reddeder). Hata değil ama kullanıcıya "depo listem
eksik" dedirtir. Org senkronu sonrası liste tazelensin + kullanıcıya görünür olsun.

### `ARV-01` — Yayın betiği yerel arşiv temizliği · ✅ **TAMAMLANDI** (2026-08-11)
`scripts/publish_release.mjs` artık paketi sunucuya **başarıyla yükledikten sonra** yerelde yalnız
**en yeni 3 sürümü** tutuyor (zip + açılmış klasör birlikte). Sunucudaki eşi ADR-070'te zaten vardı;
yerelde yoktu ve 88 sürüm birikip **28 GB** yemişti. Temizlik yayını asla başarısız saymaz.

**SNK-12 SONUÇ (2026-08-11):** Mekanizma (`BranchMirror`) **zaten vardı** ama yalnız GİRİŞTE ve masaüstünden
yapılan şube işlemlerinden sonra çalışıyordu → oturum açıkken web'de açılan depo masaüstüne inmiyordu.
**Çözüm:** aynı mekanizma normal senkron turunda da çağrılıyor (`ShellViewModel` senkron döngüsü),
**2 dakikalık kısıtlama** ile (şube listesi küçük ve nadir değişir; 15 sn'lik kadansta her tur indirmek
israf olurdu). **Yeni protokol/tablo/uç YOK.** Saf aynalama mantığı `BranchMirrorApply`'a taşındı
(Infrastructure) — Avalonia bağımlılığı olmadan test edilebilsin diye. **8 yeni senaryo.**
Çevrimdışıysa aynalama hiç çalışmaz, yerel liste korunur → çevrimdışı stok işlemi sürer.

### `STK-08` — Atanmamış stok toplu dağıtımı · ✅ **TAMAMLANDI** (2026-08-11)
Plan: [`STK_08_UYGULAMA_PLANI.md`](STK_08_UYGULAMA_PLANI.md)

**🔴 Analiz bulgusu:** mevcut `Transfer` servisi ATANMAMIŞ'ı kaynak kabul etmiyor — üç ayrı engel:
boş kaynak reddediliyor · `EnforceOwnBranch` boş kaynağı **sessizce kullanıcının şubesine çeviriyor**
(sessiz veri bozulması riski) · şube-bazlı negatif kalkanı boş lokasyonda çalışmıyor (aşım engellenmezdi).

**Karar (T-1):** `Transfer` **gevşetilmeyecek**. Ayrı ve dar giriş noktası `DistributeUnassigned` —
aynı belge/hareket makinesi, hareket türü `transfer` kalır (yeni tür yok), kendi yeterlilik kontrolü,
`EnforceOwnBranch` çağrılmaz, hedef `EnsureLocationOwned`'dan geçer, ATANMAMIŞ hedef olamaz.

**Kapsam:** servis + 2 API ucu + Web ekranı + masaüstü ekranı (çevrimdışı) + 30 test senaryosu.
Yeni yetki düğümü **açılmayacak** (mevcut `stock` + `Create` yeterli).

**STK-08 SONUÇ (2026-08-11):** `DistributeUnassigned` (dar giriş noktası) + `ListUnassigned` (tek sorgu) +
2 API ucu + Web ekranı (`/stock/distribute`) + masaüstü ekranı (çevrimdışı) + **17 senaryo**.
`Transfer` gevşetilmedi; hareket türü `transfer` kaldı; yeni yetki düğümü/migration/senkron değişikliği yok.
**1300 → 1317/1284/0/33** · build 0 hata · izole üretim kopyasında doğrulandı (toplam korundu, aşım
reddedildi, rollback çalıştı).

**🔴 B-1 (bulgu):** Transferler **geri alınmaz** (2026-08-06 kararı) — dağıtım da transfer olduğu için
geri alınamaz. Planın "ters kayıtla geri alınır" varsayımı yanlıştı. STK-08 istisna **açmadı**; düzeltme
yolu yanlış depodan doğru depoya **yeni transfer**. İlk yazılan ekran metinleri yanıltıcıydı, düzeltildi.

**🔴 B-2 (bulgu):** Üretimde **`DEPOWISE` firmasının hiç deposu yok** (0 şube; diğer iki firmada 1 ve 5).
8951,3 birim atanmamış stok var ama dağıtacak hedef yok → kullanıcı önce **Şubeler** ekranından depo
oluşturmalı. Her iki arayüz de depo yoksa bunu açıkça söylüyor.

### `SNK-11` — Bakiye senkron yükünden arındırıldı · ✅ **TAMAMLANDI** (2026-08-11)
Kayıt: [`SNK_11_BAKIYE_SENKRON_YUKU.md`](SNK_11_BAKIYE_SENKRON_YUKU.md)

`BusinessSyncService.Tables`'tan `stock_balances` çıkarıldı (+ gereksiz yetki eşlemesi kaldırıldı).
**Tablo kaldırılmadı**; yerel SQLite ve sunucu sorguları aynen duruyor. Taşınan bakiye zaten
kullanılmıyordu (sunucu defterden yeniden hesaplıyor, masaüstü pull'u hariç tutuyordu).
**Fayda (üretim kopyasında ölçüldü):** her turda **663 satır / ~86 KB** taşınmıyor.
**1317 → 1325/1292/0/33** · build 0 hata. 3 mevcut test gerekçeli yeniden yazıldı (kayıtta §4).
**Bulgu:** `stock_balances` senkrondaki tek bileşik-PK'lı tabloydu → yetenek ayrı testle kilitlendi.

### `GUI-01..05` — Masaüstü GUI doğrulama turu · ✅ **TAMAMLANDI** (2026-08-13)
Kayıt: [`docs/tests/Sube_Kapsami_GUI_Test_Report.md`](../tests/Sube_Kapsami_GUI_Test_Report.md)

Windows UI Automation ile masaüstü giriş ekranının **arkasına geçildi**; 28 maddelik checklist gerçek
UI etkileşimiyle koşturuldu: **25 geçti / 0 başarısız / 3 koşturulmadı** (madde 8 · 11 · 27).
**Altı gerçek ürün hatası** bulundu ve düzeltildi (GUI-01 kapsamın masaüstünde hiç uygulanmaması ·
GUI-02/02b elle cari hareketinin ve ters kaydın şubesiz olması · GUI-03 etiket–veri çelişkisi ·
GUI-04 raporda yetkisiz şube · GUI-05 kapsam panelinin sessizce kaybolması).
**1926 → 1941/1941/0/35** (iki tam koşu, aynı sonuç) · Release build 0 hata · üretime hiç bağlanılmadı.

**🔴 AÇIK (kullanıcı kararı gerekir) — şubesiz mevcut cari hareketler.** GUI-02 düzeltmesi yalnız
bundan sonra girilecek hareketleri şubeye bağlar. Canlıda daha önce elle girilmiş hareketler
`branch_id = NULL` olabilir ve şubesiz satır tasarım gereği **her şubede** görünür. Yayın öncesi
canlı veride şubesiz hareket **sayılmalı**; varsa toplu şube ataması kullanıcı onayıyla yapılmalıdır.
Bu tur canlı veriye **bakmadı**.
