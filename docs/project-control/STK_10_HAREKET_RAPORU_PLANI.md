# STK-10 — "Stok Hareketleri" Raporu · Envanter + Uygulama Planı

> Oluşturuldu: **2026-08-11** · Kaynak: `STK-06` §5 bulgusu (R-1)
> **DURUM: 📋 PLAN HAZIR — ADIM 0 (`STK-B1`) ✅ TAMAMLANDI, kalanı KOD BAŞLAMADI** (gerekçe §9, sonuç §12)
> Ön koşul: STK-01…08 ✅ · RPR-01 ✅ · BKM-04 ✅

---

## 1. BUGÜNKÜ DURUM

"Stok Hareketleri" bir **ekran**, katalogda **rapor değil** → Excel'e aktarımı yok.

| Katman | Dosya | Satır |
|---|---|---|
| Servis | `StockService.SearchMovements` | tarih + metin araması + `limit` (varsayılan 500, tavan 5000) |
| API | `GET /api/stock/movements` | aynı üç parametre |
| Web | `StockMovements.razor` | 179 satır · tarih + arama + **lokasyon (istemci tarafı)** |
| Masaüstü | `StockMovementsViewModel` + `.axaml` | 65 + 75 satır · tarih + arama · **lokasyon filtresi YOK** |

### Hareket üreten yollar ve alanların GERÇEK anlamı (koddan doğrulandı, tahmin YOK)

`ApplyLine` → `InsertMovement(..., branchId, branchFromId, ...)`:

| Tür | `direction` | `branch_id` | `branch_from_id` | Not |
|---|---|---|---|---|
| `opening` | +1 | konulan depo | NULL | `OpeningStockService` |
| `in` | +1 | **hedef** depo | NULL | |
| `out` | −1 | **kaynak** depo | NULL | |
| `transfer` (çıkış bacağı) | −1 | kaynak | **kaynak** (eşit) | `StockService.cs:154` |
| `transfer` (giriş bacağı) | +1 | **hedef** | **kaynak** | `StockService.cs:155` |
| `adjustment` | ± | sayılan depo | sayılan depo | sayım farkı |
| `usage` | −1 | **kaynak** depo | NULL | bakım tüketimi (BKM-04) |
| `usage_reverse` | +1 | orijinalin deposu | NULL | bakım iptali (BKM-04) |

➡️ **Türetilecek KAYNAK/HEDEF kuralı** (yeni anlam icat edilmiyor, mevcut veriden okunuyor):
- `direction > 0` → `branch_id` = **HEDEF**; kaynak = `branch_from_id` (farklıysa), yoksa "—"
- `direction < 0` → `branch_id` = **KAYNAK**; hedef = "—"
- Transfer iki AYRI satırdır ve öyle kalır (defteri tek satıra indirmek yasak).

## 2. 🔴 İKİ GERÇEK BULGU (kodlamadan önce çıktı)

### B-1 · Web'de lokasyon filtresi SESSİZCE EKSİK SONUÇ verebilir
`StockMovements.razor` lokasyon süzmesini **istemcide**, zaten `limit` ile kesilmiş liste üzerinde
yapıyor (`Filtered` özelliği). 500 hareketlik pencerede Depo A'ya ait 3 satır varsa, 501. hareketten
sonrasındaki Depo A satırları **hiç görünmez** — kullanıcı bunu anlamaz.
➡️ STK-10 filtreyi **sunucuya** taşıyınca kendiliğinden kapanır. Ayrı iş açmaya gerek yok.

### B-2 · `STK-B1` artık STK-10'un ÖN KOŞULU — **ve ilk saydığımdan daha kötü**

> ⚠️ **DÜZELTME (2026-08-11, ikinci envanter):** Bu bölüm önce "7 tür" diyordu. **8 tür var** —
> `reverse` (`StockService.ReverseDocument`, satır 353) atlanmıştı. Aşağıdaki tablo koddan doğrulanmıştır.

**Üretimdeki 8 `movement_type` ve iki platformun etiket haritaları:**

| Değer | Üreten | Masaüstü `TypeText` | Web `StockMovements.razor:170` |
|---|---|---|---|
| `opening` | `OpeningStockService` | "Açılış" | "Açılış" |
| `in` | `ApplyLine` ← ReceiveIn | "Giriş" | "Giriş" |
| `out` | `ApplyLine` ← IssueOut | "Çıkış" | "Çıkış" |
| `transfer` | `ApplyLine` ← Transfer/Dağıtım | "Transfer" | "Transfer" |
| `adjustment` | `ApplyLine` ← Count | **"Düzeltme"** | **"Sayım Düzeltme"** ⚠️ FARKLI |
| `reverse` | `ReverseDocument` | 🔴 **ham "reverse"** | "İptal (ters)" ⚠️ FARKLI |
| `usage` | `MaintenanceService` (BKM-04) | 🔴 **ham "usage"** | 🔴 **ham "usage"** |
| `usage_reverse` | `MaintenanceService` (BKM-04) | 🔴 **ham "usage_reverse"** | 🔴 **ham "usage_reverse"** |
| ~~`count`~~ | — | — | ⚠️ Web'de eşleme var ama bu bir **`doc_type`**, movement_type DEĞİL → **ölü dal** |

**Üç ayrı kusur:**
1. **3 tür ham İngilizce** görünüyor (masaüstünde `reverse` + `usage` + `usage_reverse`; Web'de 2'si).
   BKM-04 bunu görünür hâle getirdi — artık her bakım tüketimi gerçek depolu bir `usage` satırı üretiyor.
2. **İki platform aynı harekete FARKLI ad veriyor** (`adjustment`, `reverse`) — RPR-01'in önlemek için
   kurulduğu sessiz parite kaybının ta kendisi, ama etiket düzeyinde.
3. Web'de **ölü dal** (`count`) var — hiç eşleşmez, yanlış güven verir.

➡️ "Hareket türü" filtresi bu katalog olmadan yazılamaz (seçenek listesi nereden gelecek?).
**STK-B1, STK-10'un adım 0'ıdır.** Çözüm: `MovementTypeOptions` (Application katmanında **tek doğru
kaynak**, `RequestStatusOptions` deseninin ikizi) → `TypeText`, Web haritası ve filtre listesi **aynı**
yerden beslenir. Mevcut kayıtların `movement_type` DEĞERLERİ değişmez; yalnız **gösterim** katmanı düzelir.
Migration/veri dönüşümü **YOK**.

### B-3 · Masaüstünde lokasyon filtresi hiç yok (parite eksiği)
Web'de var (istemci tarafı), masaüstünde **yok**. STK-10 ikisini de sunucu/servis tarafında eşitler.

## 3. LOKASYON FİLTRESİ SEMANTİĞİ (karar — testle kilitlenecek)

| Seçim | Kural |
|---|---|
| 🌐 **Tüm Şubeler** | Filtre YOK — firmanın tüm hareketleri (Atanmamış **dahil**) |
| Belirli depo `X` | `branch_id = X` **VEYA** `branch_from_id = X` |
| 📦 **Atanmamış** | `branch_id` boş/NULL **VE** `branch_from_id` boş/NULL |

**Transfer A→B doğrulaması** (kullanıcının istediği davranış):
- Filtre **A** → çıkış bacağı (`branch_id=A`) **ve** giriş bacağı (`branch_from_id=A`) görünür
- Filtre **B** → giriş bacağı (`branch_id=B`) görünür
- Filtre **C** → hiçbiri görünmez

⚠️ "Atanmamış" gerçek depo gibi gösterilmez; kolonda `"Atanmamış"` etiketiyle çıkar (STK-06 standardı).

## 4. YENİ FİLTRE BAYRAKLARI — RPR-01'in 6 KATMANI

> ✅ **KARAR (kullanıcı, 2026-08-11): SEÇENEK B.** Mevcut ekrandaki "Ara (kod, malzeme, not, belge no)"
> alanı kataloğa **gerçek bir `Search` filtresi** olarak girer. Ekranda kalıp export dışında bırakılmaz;
> ekran ve XLSX **aynı** filtrelenmiş kümeyi üretir. Mevcut arama kabiliyeti (kod + ad + not + belge no)
> **korunur** ve Malzeme filtresi onun yerine geçmez — ikisi birlikte bulunur.

STK-10 **üç** yeni `ReportFilters` değeri gerektirir. RPR-01'in koruma testi bunları **zorunlu** kılar:
Map'e satır eklenmezse test kırılır, herhangi bir katman atlanırsa test kırılır.

| Bayrak | `ReportRequest` alanı | Tip | Web JSON | Masaüstü | Etiket |
|---|---|---|---|---|---|
| `Search = 1024` | `SearchText` | **`string?`** (liste DEĞİL) | `searchText` | `ShowSearch` | "Ara" |
| `Material = 2048` | `MaterialIds` | `IReadOnlyList<string>?` | `materialIds` | `ShowMaterial` | "Malzeme" |
| `MovementType = 4096` | `MovementTypes` | `IReadOnlyList<string>?` | `movementTypes` | `ShowMovementType` | "Hareket Türü" |

### K-0 · `Search` skaler bir alandır — RPR-01 uyumu
Diğer tüm filtreler çoklu seçim (`…Ids` listesi); `Search` **tek metin**. RPR-01'in `Scan`'i alan adına
göre çalıştığı için (`{camelField} = ` sayımı ≥ 2, `d.{prop}` sayımı ≥ 2) bu fark **sorun çıkarmaz**;
`Map` satırında `RequestProps = ["SearchText"]` yazmak yeterlidir. Yeni bir tarama kuralı gerekmez.

### K-0b · `Search` semantiği DEĞİŞMEZ
Bugünkü `SearchMovements` koşulu birebir korunur:
`m.code LIKE @q OR m.name LIKE @q OR sm.note LIKE @q OR d.invoice_no LIKE @q OR d.doc_no LIKE @q`
Yeni arama mimarisi **icat edilmez**; sorgu parçası olduğu gibi rapora taşınır.

Her bayrak için dokunulacak **6 yer** (RPR-01 `Checklist`): katalog · istek modeli · API (katalog alanı
+ DTO + **sorgu ve export** uçları) · Web (`@if` bloğu + `CatItem` + `Bool` + **iki gövde**) ·
Masaüstü VM (`ShowX` + `NotifyPropertyChangedFor` + `BuildTable`) · Masaüstü XAML.
**+ `ReportFilterParityTests.Map`'e ÜÇ satır.**

### K-1 · Malzeme filtresi `/api/reports/scope`'a EKLENMEYECEK
Üretimde **2461 malzeme** var; scope yanıtına eklemek her rapor ekranı açılışında 2461 satır indirmek
demektir (§11 performans kuralına aykırı). Bunun yerine **mevcut arama deseni**:
Web `MudAutocomplete` + `/api/materials?search=` (Bakım ekranındaki birebir aynı desen) ·
masaüstü `DesktopServices.Materials.List(..., search)`.
➡️ `ReportFilters.Material` bayrağı var ama seçenek kaynağı scope DEĞİL — bu, RPR-01 parite testinde
**açıkça belgelenmeli** (aksi hâlde "scope'ta yok" diye yanlış alarm üretir).

### K-2 · Hareket türü seçenekleri SABİT liste (STK-B1)
`RequestStatusOptions` deseninin ikizi: `MovementTypeOptions` (Application katmanında, tek doğru kaynak).
Web sunucudan (scope), masaüstü doğrudan sabitten okur → iki platform **aynı** etiketleri gösterir.
`StockMovementRow.TypeText` de bu kaynaktan beslenir → ham "usage" metni ekranlardan kalkar.

## 5. RAPOR TASARIMI

**Anahtar:** `stock-movements` · **Kategori:** `Stock` · **Grup:** `Standard` · **Export:** `ExportStandard`
**Filtreler:** `Date | Location | Search | Material | MovementType` · **`RequiresDate = true`**
(defter büyür, tarihsiz tam tarama yasak — mevcut ağır rapor kuralı).

| # | Kolon | Kaynak |
|---|---|---|
| 1 | Tarih | `created_at` (dd.MM.yyyy HH:mm) |
| 2 | Tür | `movement_type` → `MovementTypeOptions` etiketi |
| 3 | Kod | `materials.code` |
| 4 | Malzeme | `materials.name` |
| 5 | Miktar | `NumCell` (işaretli: giriş +, çıkış −) |
| 6 | Birim | `units.name` |
| 7 | **Kaynak** | §1 kuralı |
| 8 | **Hedef** | §1 kuralı |
| 9 | Belge No | `stock_documents.doc_no` |
| 10 | Fatura No | `stock_documents.invoice_no` |
| 11 | Durum | `is_reversed` → "İptal edildi" |
| 12 | Açıklama | `note` |

**`reverses_movement_id` (BKM-04):** ayrı kolon **açılmayacak** — kimlik kullanıcıya bir şey ifade etmez.
Bunun yerine Tür kolonunda "Bakım Tüketimi (İptal)" etiketi zaten ayrımı veriyor. *(Karar: gerekliyse
sonra "İptal Edilen Belge" kolonu eklenir; şimdilik gürültü.)*

## 6. SERVİS / EXPORT / EKRAN MİMARİSİ

- `ReportService.StockMovements(s, req)` → **tek sorgu**, lokasyon adları JOIN'le (N+1 yok),
  `SearchMovements`'ın mevcut JOIN'leri temel alınır.
- `ReportCatalog` dispatch'e eklenir → `Run` üzerinden gelir → **export otomatik aynı gövdeyi kullanır**
  (STK-06'da kanıtlandı: `/api/reports/{type}/export` aynı `ReportRequest`'i kurar; RPR-01 bunu kilitledi).
- **Mevcut ekranlar korunur ve rapora BAĞLANIR:** `StockMovements` ekranı satır aksiyonu içermiyor
  (salt-okunur) → tabloyu rapor modelinden beslemek UX kaybı yaratmaz. İkinci paralel sistem kurulmaz.
- ✅ Ekranın **arama kutusu** artık `ReportFilters.Search` olarak katalogda (karar B, §4).
- 🔴 **B-1 düzeltmesi burada uygulanır:** lokasyon süzmesi **sunucuya** taşınır. Sıra ZORUNLU:
  **önce filtrele → sonra sırala → sonra limit/sayfala.** Bugünkü "önce limit, sonra istemcide süz"
  akışı kaldırılır. Export ise **limit uygulamadan** tüm filtrelenmiş kümeyi alır (mevcut rapor
  standardında export `maxRows` ile ekrandan ayrışır → bu davranış korunur ve testle kilitlenir).

## 7. VERİ MODELİ / MIGRATION

**Gerekmiyor.** `stock_movements` tüm alanları taşıyor (`branch_id`, `branch_from_id`, `group_id`,
`is_reversed`, `reverses_movement_id`). Yeni kolon/tablo/indeks **açılmayacak**.
İndeks kararı: `ix_stock_movements(company_id, created_at)` mevcut mu diye **gerçek sorgu planıyla**
ölçülecek; gerekmiyorsa eklenmeyecek.
**STK-11 (float artığı) bu işte ÇÖZÜLMEYECEK** — rapor ham değeri gösterir, sessiz yuvarlama yapmaz.

## 8. UYGULAMA SIRASI (kod bu adımdan başlar)

| # | İş | Dosya | Bağımsız mı? |
|---|---|---|---|
| **0** | ✅ **STK-B1 TAMAMLANDI (2026-08-11)** — `MovementTypeOptions` tek doğru kaynak (8 tür); **üç** gösterim yüzeyi de ona bağlandı; ölü `count` dalı kaldırıldı | `Application/Ui/MovementTypeOptions.cs` (yeni) · `StockService` (2 yüzey) · `StockMovements.razor` · `DepoWise.Web.csproj` | ✅ tamamlandı |
| 1 | `ReportFilters.Search` + `Material` + `MovementType` + `UsesX` | `ReportCatalog.cs` | 0'a bağlı |
| 2 | `ReportRequest.SearchText` + `MaterialIds` + `MovementTypes` (sona) | `ReportModels.cs` | |
| 3 | `ReportService.StockMovements` — **tek sorgu**, Kaynak/Hedef, lokasyon semantiği, **filtre→sırala→limit** | `ReportService.cs` | |
| 4 | Katalog kaydı + `Run` dispatch | `ReportCatalog.cs` | |
| 5 | API: katalog alanları · DTO · **sorgu + export** uçları · scope'a `movementTypes` (malzeme **YOK**, K-1) | `Program.cs` | |
| 6 | **Masaüstü** (kural: önce masaüstü) — Raporlar'a 3 filtre + hareket ekranını rapora bağla | `ReportsViewModel` + `ReportsView.axaml` + `StockMovementsViewModel` + `.axaml` | |
| 7 | Web — aynı üçü + **B-1 düzeltmesi** (sunucu tarafı lokasyon filtresi) | `Reports.razor` + `StockMovements.razor` | |
| 8 | **RPR-01 `Map`'e 3 satır** + malzeme filtresinin scope istisnası belgelenir | `ReportFilterParityTests.cs` | |
| 9 | ~30 senaryo (§12) + **6 kombinasyonda gerçek XLSX round-trip** (ClosedXML test projesinde mevcut) | yeni test dosyaları | |
| 10 | Doğrulama: build · tam takım · SQLite · izole PG (+sorgu planı) · gerçek HTTP · çevrimdışı · tarayıcı | — | |

## 9. ⚠️ NEDEN KOD BU OTURUMDA (YİNE) BAŞLAMADI

Talimat §16: *"Bu prompttaki işi tek oturumda güvenli biçimde kodlayıp tam doğrulayamıyorsan
KODLAMAYA BAŞLAMA. … bu oturumu kodsuz kapat."*

Bu oturumda **`RPR-01`** (18 senaryo) ve **`BKM-04`** (analiz + karar + tam uygulama + 44 senaryo +
izole PG) tamamlandı ve gönderildi. Karar B ile STK-10 **büyüdü**, küçülmedi:

| Kalem | Boyut |
|---|---|
| Yeni filtre bayrağı | **3** (Search + Material + MovementType) × RPR-01'in **6 katmanı** = **18 kablolama noktası** |
| STK-B1 (adım 0) | 8 tür · 2 ıraksamış etiket haritası · 3 masaüstü + 1 web ekranı · 1 ölü dal |
| Ekran bağlama | 2 ekran (Web + masaüstü) + **B-1 davranış düzeltmesi** (sunucu tarafı filtre + sıralama/limit sırası) |
| Test | ~30 senaryo + **6 kombinasyonda gerçek XLSX satır-satır karşılaştırması** (bu proje için ilk) |
| Doğrulama | build · tam takım · SQLite · izole PG **+ sorgu planı** · gerçek HTTP · çevrimdışı · tarayıcı render |

Kalan oturum kapasitesi bu doğrulamaların **tamamını** garanti etmiyor. Bu proje boyunca korunan kural —
*"yarım bırakılmış stok/rapor kodu değerleri sessizce yanlış gösterir"* — ve kullanıcının §16'daki açık
talimatı gereği **hiçbir üretim koduna dokunulmadı**.

### ➡️ Sonraki oturum için: **adım 0 tek başına tamamlanabilir**
`STK-B1` (§8 adım 0) STK-10'un geri kalanından **bağımsızdır**: gösterim katmanı düzeltmesi, migration
yok, rapor altyapısına dokunmuyor. Kapasite dar bir oturumda **yalnız adım 0** yapılıp tam
doğrulanabilir; STK-10'un kalanı sonraki oturuma kalır ve **yarım kalmış olmaz** (adım 0 kendi başına
kullanıcıya değer üretir: ham "usage/reverse" metinleri ekranlardan kalkar).

## 10. ✅ STK-10 KABUL KRİTERLERİ (kalıcı kayıt)

Aşağıdakilerin **tamamı** sağlanmadan STK-10 "tamamlandı" sayılmaz:

**Katalog / sözleşme**
- [ ] `stock-movements` katalogda **gerçek rapor** (anahtar · ad · açıklama · filtreler · export yetkisi)
- [ ] `ReportFilters.Search | Material | MovementType` eklendi; `ReportFilters` **tek doğru kaynak** kaldı
- [ ] RPR-01 `Map`'e **3 satır** eklendi; koruma testi **gevşetilmedi** ve yeni raporla gerçekten koşuyor
- [ ] Üç bayrak da **6 katmanın hepsinde** bağlı (katalog · istek · API sorgu **ve** export · Web · masaüstü)

**Veri / semantik**
- [ ] Kaynak/Hedef kolonları §1 kuralına uyuyor; `direction>0` hedef, `direction<0` kaynak
- [ ] Transfer **iki satır** kalıyor (tek satıra indirgenmedi)
- [ ] Lokasyon filtresi `branch_id=X OR branch_from_id=X`; A→B hem A hem B'de, C'de **yok**
- [ ] 🌐 Tüm Şubeler ≠ 📦 Atanmamış; Atanmamış gerçek depo gibi gösterilmiyor
- [ ] "Şube" (`op_branch_id`) ile "stok lokasyonu" **birleştirilmedi**
- [ ] 8 `movement_type` değerinin **tamamı** Türkçe; `usage`/`usage_reverse`/`reverse` **ham görünmüyor**
- [ ] İki platform **aynı** etiketi gösteriyor (bugün `adjustment` ve `reverse` ıraksıyor)

**Filtre / arama**
- [ ] `Search` kod + ad + not + belge no üzerinde çalışıyor (mevcut anlam **korundu**)
- [ ] Malzeme filtresi Search'ün **yerine geçmedi**; ikisi birlikte var
- [ ] Malzeme seçenekleri `/api/reports/scope`'a **eklenmedi** (2461 satır indirilmiyor — K-1)
- [ ] **Filtre → sırala → limit** sırası; lokasyon filtresi **sunucuda** (B-1 kapandı)
- [ ] 500+ satırlık veride 501. eşleşme **kaybolmuyor**

**Export**
- [ ] Ekran ve XLSX **aynı** `ReportRequest`'ten üretiliyor; export **ayrı sorgu kullanmıyor**
- [ ] **6 kombinasyonda** üretilen XLSX gerçekten **açılıp satır/sütun bazında** rapor sonucuyla
      karşılaştırıldı (filtresiz · lokasyon · Search · tür · lokasyon+Search · tarih+lokasyon+Search+tür)
- [ ] Export'ta ekran limiti yüzünden **veri kaybı yok**
- [ ] XLSX'te Kaynak/Hedef kolonları ve "Atanmamış" doğru

**Platform / ortam**
- [ ] Web + masaüstü: aynı filtreler, kolonlar, boş sonuç davranışı, aynı sonuç kümesi
- [ ] Masaüstü **çevrimdışı** çalışıyor; rapor için **yeni API bağımlılığı yok**
- [ ] Firma izolasyonu: istemciden yabancı malzeme/lokasyon kimliği göndererek başka firma görülemiyor
- [ ] N+1 yok; malzeme×depo sorgu patlaması yok; indeks **yalnız ölçümle** gerekçelendirildi
- [ ] Migration **yok** (zorunlu değilse açılmaz) · senkron protokolü **değişmedi** · SNK-11 geri gelmedi
- [ ] STK-11 (float artığı) **çözülmedi**, rapor ham değeri gösteriyor
- [ ] Mevcut testlerin hiçbiri silinmedi/gevşetilmedi/atlanmadı
- [ ] Gerçek tarayıcı render kontrolü yapıldı **ya da** yapılmadığı açıkça raporlandı

## 11. YENİ DEVREDİLEN İŞ

| Kod | İş | Kaynak |
|---|---|---|
| — | **B-1 ve B-3 STK-10 içinde çözülecek** (ayrı iş açılmadı) | §2 |
| `STK-B1` | Artık **bağımsız değil** — STK-10 adım 0 | §2 (B-2) |

---

## 12. ✅ ADIM 0 (`STK-B1`) TAMAMLANDI — 2026-08-11

### Kesinleşen 8 tür ve NİHAİ etiketler (iki platformda AYNI)

| # | `movement_type` | Üreten | **Etiket (Web = Masaüstü)** | Önceki durum |
|---|---|---|---|---|
| 1 | `opening` | `OpeningStockService` | **Açılış** | üçünde de aynıydı ✅ |
| 2 | `in` | `StockService.ApplyLine` ← ReceiveIn | **Giriş** | aynıydı ✅ |
| 3 | `out` | `StockService.ApplyLine` ← IssueOut | **Çıkış** | aynıydı ✅ |
| 4 | `transfer` | `StockService.ApplyLine` ← Transfer/Dağıtım | **Transfer** | aynıydı ✅ |
| 5 | `adjustment` | `StockService.ApplyLine` ← Count | **Sayım Düzeltme** | 🔴 masaüstü "Düzeltme" ↔ diğer ikisi "Sayım Düzeltme" |
| 6 | `usage` | `MaintenanceService` (BKM-04) | **Bakım Tüketimi** | 🔴 **üçünde de HAM** |
| 7 | `usage_reverse` | `MaintenanceService` (BKM-04) | **Bakım Tüketimi İptali** | 🔴 **üçünde de HAM** |
| 8 | `reverse` | `StockService.ReverseDocument` | **İptal (Ters Kayıt)** | 🔴 masaüstü HAM · web "İptal (ters)" · malzeme kartı "İptal" |

**Etiket gerekçeleri (terminoloji uydurulmadı, mevcut projeden alındı):**
- `adjustment` → "Sayım Düzeltme": bu hareketi **yalnız** `StockService.Count` üretir; üç haritanın
  ikisi zaten böyle diyordu.
- `reverse` → "İptal (Ters Kayıt)": projenin kanonik terimi **"Ters Kayıt"**tır
  (`AuditLogService: "reverse" => "Ters Kayıt"`, `AppModules.Reverse`, Migration006 yorumu); kullanıcının
  butonda gördüğü eylem ise "İptal". Etiket ikisini birleştirir → mevcut web/malzeme-kartı adlarının üst kümesi.
- `usage` / `usage_reverse` → BKM-04 belgelerinin tutarlı terimi "bakım tüketimi". İkisi **ayrı**
  adlandırıldı; `reverse` ve `adjustment` ile de karışmıyor (testle kilitli).

### Düzeltilen üç gösterim yüzeyi
| Yüzey | Kullanan ekranlar |
|---|---|
| `StockMovementRow.TypeText` | masaüstü **Stok Hareketleri** + **Stok Giriş/Çıkış** |
| `StockService.RecentForMaterial` | malzeme kartı "Son Hareketler" — **Web ve masaüstü ORTAK** |
| `Web/StockMovements.razor` | web **Stok Hareketleri** |

### Nasıl paylaşıldı (yeni mimari kurulmadı)
Web, Application'a **proje referansı vermez** (bilinçli sınır: her şeyi API'den alır). Proje bu sorunu
daha önce `ListColumns` ve `RequestOperationStatus` için çözmüş: **tek dosya, iki projede derlenir**
(`<Compile Include="..\DepoWise.Application\...">`). `MovementTypeOptions` da aynı desene bağlandı →
**ayna dosya yok**, iki liste ıraksayamaz.

### 🔴 STK-10'a devreden not: şube kapsamı × lokasyon filtresi
`SearchMovements`, `BranchScope.Sql(s, "sm.branch_id")` uygular. **Depo A ile giriş yapmış bir oturum
transferin yalnız KAYNAK bacağını görür** (hedef bacağın `branch_id`'si Depo B'dir). Bu mevcut ve doğru
davranıştır; STK-B1'de değiştirilmedi. **STK-10'un lokasyon filtresi tasarlanırken bu etkileşim hesaba
katılmalıdır:** §3'teki "A→B hem A hem B filtresinde görünür" kuralı, şube kapsamlı oturumda kapsam
filtresiyle kesişir. Testle belgelendi (`Transferin_Iki_Bacagi_da_Transfer_Etiketli`).

### Doğrulamalar
| Doğrulama | Sonuç |
|---|---|
| Çözüm derlemesi | **0 hata** |
| Tam test takımı | **1411 · 1377 geçti · 0 kaldı · 34 atlandı** (taban 1387; **+24 senaryo**) |
| Gerçek kod yolu | 8 türün 8'i gerçek servislerle üretildi; defterde **tam 8 tür**, hepsi katalogda |
| Ham değer kaçağı | Hareket listesi ve malzeme kartında **hiçbir satır** ham İngilizce göstermiyor |
| Gelecek koruması | Kaynak taraması: üretimde katalogda olmayan tür bulunursa test **kırılır** |
| Migration / senkron / veri | **Dokunulmadı** — yalnız gösterim katmanı |
| Görsel (tarayıcı/XAML render) | ❌ **YAPILMADI** — aşağıda |

### ⚠️ Bu iş sırasında DÜZELTİLEN kendi test hatam
`MaintenanceStockLocationTests`'teki 4 iptal testi ters kaydı **sıra indeksiyle** (`[1]`) seçiyordu.
Test saati dondurulmuş olduğu için orijinal hareket ile ters kaydın `created_at` değeri AYNI oluyor ve
`ORDER BY created_at, id` **rastgele GUID'e** düşüyordu → testler **flaky**'ydi (5 koşuda ~1 kırılma).
BKM-04'te şans eseri geçmişlerdi. Tür üzerinden seçime çevrildi; 5 ardışık koşuda **27/27** kararlı.
**Üretim etkilenmedi:** iptal her hareketi kendi deposuna geri yazar, sıradan bağımsızdır.

### Görsel kontrol — YAPILMADI (dürüst kayıt)
BKM-04'teki **aynı engel**: yerel API veritabanında (`src/DepoWise.Api/data/depowise-server.db`) zaten
kullanıcılar var → tohum parolası üretilmiyor ve giriş yapılamıyor. Denenen ve elenen yollar:
1. `DEPOWISE_SERVER_DATA` ile ayrı veri dizini — `launch.json` şeması **env değişkeni desteklemiyor**,
   dev sunucusu Bash ile başlatılamıyor.
2. Mevcut veritabanını sıfırlamak/yeniden adlandırmak → **kullanıcının yerel verisine dokunmak** olurdu.
3. Canlıdan bakmak → **canlıya bağlanma yasağı**.
4. CLI ile geçici test kullanıcısı → **böyle bir mekanizma yok** (`SeedPassword` yalnız `users` tablosu
   BOŞken çalışıyor; tüm API uçları kimlik doğrulaması istiyor).

**Statik risk değerlendirmesi (render yerine geçmez):** masaüstünde tür kolonları `Auto` genişlikli
(`MinWidth` 80–90) ve `TextTrimming` **yok** → uzun etiket (`"Bakım Tüketimi İptali"`, 21 karakter)
**kırpılmaz ama kolonu genişletir**; dar pencerede diğer kolonları sıkıştırabilir. Web'de `MudTd`
sarmaladığı için kırpılma riski yok. Bu ancak gerçek render ile kesinleşir.

**Kapatmanın yolu:** yerel API için bir hesap/parola sağlanması **ya da** `src/DepoWise.Api/data`
dizininin geçici olarak yenilenmesine izin verilmesi.
