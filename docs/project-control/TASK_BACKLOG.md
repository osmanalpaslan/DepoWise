# GÖREV LİSTESİ (BACKLOG)

> Son güncelleme: **2026-08-11** · Durumlar: `SIRADA` · `BEKLEMEDE` · `ENGELLİ` · `GELİŞTİRMEDE` · `TAMAMLANDI` · `ERTELENDİ`
> Maliyet: **A** şimdi/maliyetsiz · **B** opsiyonel · **C** canlıya geçişte · **D** gelir sonrası

---

## FAZ A — Kullanıcı bug'ları + yetki tamamlama

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

## FAZ C — Depo bazlı stok ⛔ **KARAR-7 bekliyor**

| ID | İş | Maliyet |
|---|---|---|
| `STK-01` | `stock_balances` → depo/şube boyutu (migration + geçmiş veri taşıma) | A |
| `STK-02` | `StockService` tüm yolları depo bazlı | A |
| `STK-03` | Malzeme kartı/liste bakiye gösterimi (web+masaüstü) | A |
| `STK-04` | Kritik stok uyarısı depo bazlı | A |
| `STK-05` | Kapsam birleştirme (`WEB-02` şube kapsamı dâhil) | A |
| `STK-06` | Raporlara depo boyutu | A |
| `STK-07` | Senkron/çakışma doğrulaması | A |
| `TRF-01` | Depo → depo transferi (tek transaction, iki hareket) | A |

⚠️ **Veri kaybı riski:** `STK-01` mevcut bakiyeleri böler/taşır. Otomatik uygulanmayacak — önce
geçiş planı + yedek + izole prova (FAZ H'deki yöntemle) yapılacak.

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
