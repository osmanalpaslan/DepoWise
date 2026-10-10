# STK-10 — "Stok Hareketleri" Raporu · Envanter + Uygulama Planı

> Oluşturuldu: **2026-08-11** · Kaynak: `STK-06` §5 bulgusu (R-1)
> **DURUM: `STK-B1` ✅ (§12) · `STK-10a` ✅ TAMAMLANDI (§16) · `STK-10b` ⏳ BEKLİYOR (§17)**
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
- [ ] **`BranchScope` × `Location` kesişimi §14'e uyuyor**: kapsam DIŞ SINIR, lokasyon içeride daraltır;
      Depo A oturumu Depo B filtresiyle **BOŞ** sonuç alır (yetki aşılmaz) — ayrı testle kilitli
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
- [ ] Ekran ve export **AYNI** satır tavanına tabi (§13/D-1 — ikisi de `maxRows`, varsayılan 50.000);
      export'un ekrandan farklı bir küme üretmediği testle kanıtlandı
- [ ] SQL'de sıra **filtre → sırala → LIMIT** (§13/D-2: `Run`'ın kesmesi bellekte, ikinci emniyet ağı)
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

---

## 13. 🔴 PLAN DÜZELTMELERİ (2026-08-11, adım 1 öncesi doğrulama)

Adım 1'e geçmeden önce planın dayandığı üç varsayım koddan sınandı. **İkisi yanlıştı.**

### D-1 · ❌ "Export limit uygulamaz" — YANLIŞ
Plan §6 şöyle diyordu: *"Export ise limit uygulamadan tüm filtrelenmiş kümeyi alır."*
**Gerçek:** `/api/reports/{type}` ve `/api/reports/{type}/export` **AYNI** `BuildReport`'u çağırır ve
**aynı** `max` değerini geçirir (`ReportLimits.Resolve` → varsayılan **50.000**, ayarla değişebilir,
1000'in altına düşmez). Yani ekran ve export **aynı tavana** tabidir.

➡️ **Sonuç (iyi haber):** "ekran = export" hedefi için bu doğru davranıştır; ayrı bir export yolu
kurmaya gerek YOK.
➡️ **Ama kabul kriteri düzeltilmeli:** "Export'ta ekran limiti yüzünden veri kaybı yok" maddesi,
*"export ekranla AYNI tavana tabidir ve tavan 50.000'dir"* olarak yeniden yazılmalı. 50.000'i aşan
bir filtre sonucunda **ikisi de** kesilir — bu bilinçli bir koruma, sessiz kayıp değil. Kullanıcıya
tavana ulaşıldığını söyleyip söylemeyeceğimiz **ayrı bir ürün kararıdır** (§15).

### D-2 · ❌ "Run'ın limiti SQL'e iner" — YANLIŞ
`ReportService.Run` önce `Dispatch` çağırır, **sonra bellekte** `table.Rows.Take(maxRows)` uygular.
Yani tavan **sorgudan sonra** işler; sorgu tüm eşleşen satırları materyalize eder.

➡️ **Sonuç:** `StockMovements` sorgusu **kendi SQL LIMIT'ini** taşımalı ve
**filtre → sıralama → LIMIT sırası SQL içinde** kurulmalıdır. `Run`'ın kesmesi yalnız ikinci bir
emniyet ağıdır. (Mevcut `SearchMovements` zaten SQL'de `ORDER BY … LIMIT @lim` yapıyor — desen oradan
alınacak.) Bu, B-1 düzeltmesinin (§2) teknik karşılığıdır.

### D-3 · ✅ ClosedXML test projesinden erişilebilir — DOĞRULANDI
`tests/DepoWise.Tests/bin/.../ClosedXML.dll` mevcut (Infrastructure üzerinden geliyor).
Gerçek XLSX'i açıp **satır satır** karşılaştırma teknik olarak mümkün → RPR-01'in açık bıraktığı
boşluk STK-10'da kapatılabilir.

## 14. ✅ KARAR: `BranchScope` × `Location` KESİŞİMİ (kodlamadan önce netleştirildi)

Kullanıcının açıkça sorduğu nokta. Koddan türetilen kesin tanım:

`BranchScope.Sql(s, "sm.branch_id")` → `AND (sm.branch_id = @opb OR sm.branch_id IS NULL)`
(NULL satırlar bilinçli olarak gizlenmez — geçmiş/atanmamış kayıt kaybolmasın diye.)

**KURAL: Kapsam DIŞ SINIRDIR; lokasyon filtresi onun İÇİNDE daraltır, asla genişletmez.**
Sorgu `WHERE kapsam AND lokasyonFiltresi` biçiminde kurulur — ikisi `OR`'lanmaz.

Sonuçları (A→B transferi, iki bacak):

| Oturum | Lokasyon filtresi | Görünen | Gerekçe |
|---|---|---|---|
| Tüm Şubeler | (yok) | **iki bacak** | kapsam sınırsız |
| Tüm Şubeler | **A** | **iki bacak** | çıkış bacağı `branch_id=A`, giriş bacağı `branch_from_id=A` |
| Tüm Şubeler | **B** | **giriş bacağı** | `branch_id=B` |
| Tüm Şubeler | **C** | **hiçbiri** | eşleşme yok |
| **Depo A** | (yok) | **yalnız çıkış bacağı** | giriş bacağının `branch_id`'si B → kapsam dışı |
| **Depo A** | **A** | **yalnız çıkış bacağı** | kapsam zaten B'yi eliyor |
| **Depo A** | **B** | **BOŞ** | 🔒 kullanıcı Depo B hareketini göremez — **yetki aşılmaz** |

➡️ §3'teki "A→B hem A hem B filtresinde görünür" kuralı **kapsamı yeten kullanıcı için** geçerlidir.
Şubeye bağlı kullanıcıda kapsam kazanır. Bu **bilinçli** bir güvenlik sınırıdır ve testle kilitlenecek
(`Depo_A_Oturumu_Depo_B_Filtresiyle_BOS_Sonuc_Alir`).

## 15. ⏸️ NEDEN ADIM 1'E GEÇİLMEDİ — VE ÖNERİLEN BÖLÜNME

Talimat: *"Kalan kapasiten kabul kriterlerinin tamamını güvenilir biçimde doğrulamaya yetmeyecekse
KOD YAZMA."*

Bu oturumda tamamlanan ve gönderilen işler: **RPR-01** (18 senaryo) · **BKM-04** (analiz + karar +
tam uygulama + 44 senaryo + izole PG) · **STK-B1** (24 senaryo + flaky test düzeltmesi).
STK-10'un kalanı bunların **hepsinden büyük** tek parçadır ve **bölünemez**:

> 🔒 **Neden bölünemez:** RPR-01'in koruma testi, bir `ReportFilters` bayrağının **6 katmanın
> hepsinde** bağlı olmasını zorunlu kılar. `Search`/`Material`/`MovementType` bayraklarından birini
> ekleyip katmanlarını tamamlamamak **RPR-01'i kırar**. Yani "önce sözleşme, sonra arayüz" gibi bir
> dilimleme mümkün değildir — 18 kablolama noktası **atomiktir**.

### Önerilen bölünme (kullanıcı onayı gerekir — kendi başıma daraltmadım)

| Artım | Kapsam | Neden bütün bir iş |
|---|---|---|
| **STK-10a** | Raporu katalogda `Date + Location` ile aç · `ReportService.StockMovements` (Kaynak/Hedef + §14 kesişimi + SQL'de filtre→sırala→limit) · **gerçek XLSX satır-satır doğrulama (6 kombinasyon)** · izole PG sorgu planı | **Yeni bayrak YOK** → RPR-01 hiç değişmeden yeşil kalır. `Date` ve `Location` bloklarının **altı katmanı da zaten bağlı** (STK-06). Rapor Raporlar ekranında filtreleriyle **kendiliğinden** görünür. Kullanıcı ilk kez hareket defterini Excel'e aktarabilir. |
| **STK-10b** | `Search` + `Material` + `MovementType` bayrakları (18 kablolama) · iki hareket ekranının rapora bağlanması · **B-1 düzeltmesi** · kalan senaryolar | Bayraklar atomik; ekran bağlama ve B-1 onlarla birlikte anlamlı. |

**Önerim: STK-10a → STK-10b.** 10a tek başına yarım iş değildir; 10b'ye kadar mevcut Stok Hareketleri
ekranı bugünkü hâliyle çalışmaya devam eder (hiçbir davranış bozulmaz).

⚠️ Kullanıcı "tek seferde tamamı" derse, iş **iki oturuma yayılacağı** baştan kabul edilmeli ve ara
oturumda kod **derlenebilir + testleri yeşil** bırakılmalıdır (RPR-01 yüzünden bu ancak 18 kablolama
noktasının tamamı bittiğinde mümkündür).

---

## 16. ✅ STK-10a TAMAMLANDI — 2026-08-11

Katalog + `Date`/`Location` filtreleri + Kaynak/Hedef + **gerçek XLSX satır-satır doğrulaması**.
`Search` / `Material` / `MovementType` **eklenmedi** — onlar STK-10b'nindir.

### Değişen dosyalar (4 üretim + 4 test)

| Dosya | Değişiklik | Neden |
|---|---|---|
| `Reporting/ReportService.cs` | **`StockMovements` metodu** (yeni) + `Dispatch`'e `maxRows` parametresi + `stock-movements` dispatch | Rapor gövdesi; tavanın SQL'e inebilmesi için Dispatch imzası genişletildi (diğer raporların davranışı değişmedi) |
| `Application/Reports/ReportCatalog.cs` | `stock-movements` descriptor'ı (`Date \| Location`, `RequiresDate`, `ExportStandard`) | Raporu kataloğa alır |
| `tests/StockMovementsReportTests.cs` | **YENİ** — 29 senaryo (çevrimdışı + XLSX) | |
| `tests/ApiStockMovementsReportTests.cs` | **YENİ** — 11 senaryo (gerçek HTTP + XLSX) | |
| `tests/PostgresStockMovementsReportTests.cs` | **YENİ** — 1 senaryo (PG + sorgu planı) | |
| `tests/ReportArchitectureTests.cs` · `tests/StockReportLocationTests.cs` | Katalog sayısı 12→13 · lokasyonlu rapor listesi 2→3 | Bilinçli katalog eklemesi; **gevşetme değil** (tam eşleşme korundu, §16.1) |

### 🔴 EN ÖNEMLİ BULGU: Web ve masaüstünde **HİÇ KOD DEĞİŞMEDİ**

Rapor katalog-güdümlü olduğu için yeni rapor iki platformun Raporlar ekranında **kendiliğinden**
göründü ve `Date` + `Location` filtreleri **zaten bağlıydı** (STK-06'dan, RPR-01'in 6 katman
güvencesiyle). Bu, "yeni bayrak eklemeyen artım" seçiminin doğrudan getirisidir:
**RPR-01 hiç değiştirilmeden yeşil kaldı**, arayüz kablolaması gerekmedi.

### 16.1 Değiştirilen iki mevcut test (gerekçeli — gevşetme DEĞİL)

| Test | Neden değişti |
|---|---|
| `ReportArchitectureTests.Katalog_TumAnahtarlar_RunTarafindanTaninir` | `Assert.Equal(12, …Count)` → **13**. Sayı, "sessizce rapor eklendi/silindi" nöbetçisidir; kataloğa **bilinçli** bir rapor eklendi. Testin asıl gövdesi (her rapor için ad/açıklama/kategori/`RequiresDate ⊆ UsesDate`/`ByKey`) **13 raporun hepsinde** koşmaya devam ediyor. |
| `StockReportLocationTests.Lokasyon_Filtresi_Yalniz_…` | Beklenen liste `["stock","stock-count"]` → **`[…,"stock-movements"]`**. Hâlâ **TAM EŞLEŞME** ile sınanıyor → kalan 10 raporun lokasyon filtresi olmadığını kanıtlamaya devam ediyor. Ayrıca **yeni bir nöbetçi eklendi**: STK-10b'nin bayrağı (1024) hiçbir raporda açık olmamalı. |

### 16.2 Doğrulamalar

| Doğrulama | Sonuç |
|---|---|
| Çözüm derlemesi | **0 hata** |
| Tam test takımı | **1452 · 1417 geçti · 0 kaldı · 35 atlandı** (taban 1411; **+41 senaryo**) |
| RPR-01 koruma testi | ✅ **Değiştirilmedi ve yeşil** (yeni bayrak eklenmediği için hiç dokunulmadı) |
| SQLite / çevrimdışı | 29 senaryo, HTTP yok; masaüstü istek deseni (`ReportsViewModel.BuildTable`) ayrıca sınandı |
| Gerçek HTTP | 11 senaryo — katalog ucu · rapor ucu · export ucu · yetki (403) · kimliksiz istek |
| **Gerçek XLSX** | **6 kombinasyon × 2 hat (servis + HTTP)** — XLSX açılıp **hücre hücre** karşılaştırıldı |
| İzole PostgreSQL | Rapor çalıştı (23 satır) · lokasyon filtresi doğru · LIMIT etkili · **sorgu planı incelendi** |
| Firma izolasyonu | Yabancı firma hareketi sızmıyor; yabancı depo filtre olarak gönderilse bile boş döner |
| Görsel render | ❌ **YAPILMADI** (§16.5) |

### 16.3 ⚡ Sorgu planı (izole PostgreSQL, gerçek çıktı)

```
Limit
  -> Sort  (Sort Key: sm.created_at DESC, sm.id DESC)
       -> Nested Loop
            -> Index Scan using ix_materials_company on materials m
                 Index Cond: (company_id = 'A')
            -> Index Scan using ix_stock_movements_material on stock_movements sm
                 Index Cond: (material_id = m.id) AND (created_at >= 0) AND (created_at <= …)
                 Filter: (company_id = 'A') AND ((branch_id = 'x') OR (branch_from_id = 'x'))
```

➡️ **`Limit` ve `Sort` SQL'de** · tarih filtresi **mevcut indeksi** (`ix_stock_movements_material`)
kullanıyor · lokasyon filtresi SQL'e inmiş.
➡️ **YENİ İNDEKS EKLENMEDİ** — gerçek plan gerektirmiyor (plan §7 kuralı: indeks yalnız ölçüm
gerekçelendirirse). Mevcut indeksler: `stock_movements_pkey`, `ix_stock_movements_material`,
`ux_stock_movements_operation`.
⚠️ Sınır: plan **küçük test verisiyle** alındı; üretim ölçeğinde planlayıcı farklı bir birleşim
seçebilir. Yapısal gerçekler (Limit/Sort/Filter SQL'de) ölçekten bağımsızdır.

### 16.4 Kilitlenen davranışlar

**Kaynak/Hedef:** `direction>0` → hedef dolu/kaynak "—" · `direction<0` → kaynak dolu/hedef "—" ·
transfer **iki satır** (giriş bacağı `Depo A → Depo B`, çıkış bacağı `Depo A → —`).

**Lokasyon filtresi:** Tüm Şubeler (Atanmamış dahil hepsi) · Depo A → transferin **iki bacağı** ·
Depo B → **giriş bacağı** · Depo C → **boş** · 📦 Atanmamış → yalnız iki tarafı da boş olanlar ·
çoklu seçim = birleşim (kesişen bacaklar iki kez sayılmaz).

**🔒 BranchScope × Location:** Depo A oturumu + A → yalnız kapsam içindeki bacak (Tüm Şubeler aynı
filtreyle **iki** bacak görüyor → fark kapsamdan) · **Depo A oturumu + Depo B → BOŞ** (yetki aşılmaz) ·
lokasyon filtresi verilmese de kapsam uygulanıyor.

**STK-B1 korundu:** 8 hareket türü raporda doğru Türkçe etiketle; hiçbiri ham İngilizce değil;
etiketler `MovementTypeOptions`'tan — **ikinci harita kurulmadı**.

**BKM-04 korundu:** bakım tüketimi seçilen depoda (kaynak) · ters kaydı orijinal hareketin deposunda.

**Sınırlar:** tavan SQL'de (`maxRows` → `LIMIT`) ve sıralama korunuyor · ekran ile export **aynı
tavana** tabi · boş sonuçta XLSX yine üretiliyor (başlıklı, satırsız).

### 16.5 Görsel kontrol — YAPILMADI (dürüst kayıt)

Engel değişmedi (BKM-04 · STK-B1 ile aynı): yerel API veritabanında hesap yok, `launch.json` env
değişkeni desteklemiyor, canlıya bağlanmak ve parola girmek yasak, CLI test-kullanıcı mekanizması yok.
**Kontrol edilemeyenler:** filtrelerin hizası · kolon genişlikleri · uzun malzeme/depo adları ·
dar pencere · boş sonuç görünümü · XLSX'in Excel'de göründüğü hâli.
*(XLSX'in **içeriği** ClosedXML ile hücre hücre doğrulandı; doğrulanmayan yalnız görsel sunumdur.)*

## 17. ▶️ STK-10b — BEKLEYEN SONRAKİ ARTIM

Kapsam (bu oturumda **başlanmadı**): `Search` + `Material` + `MovementType` bayrakları
(**18 kablolama noktası**, RPR-01 gereği atomik) · iki hareket ekranının rapora bağlanması ·
**B-1 düzeltmesi** (Web'de istemci tarafı lokasyon süzmesinin kaldırılması) · kalan senaryolar.
Kabul kriterleri §10'da; `Search` sözleşmesi §4'te (ADR-104 / KARAR-10).

---

## 18. STK-10b — KODLAMA ÖNCESİ DOĞRULAMA (2026-08-11) · KOD BAŞLAMADI

### 18.1 Zorunlu 7 doğrulama — plan ile kod arasında **FARK YOK**

| # | Doğrulanan | Sonuç |
|---|---|---|
| 1 | STK-10a kod durumu | ✅ `ReportService.StockMovements` (satır 809) · `Dispatch` → `"stock-movements"` · katalog kaydı yerinde |
| 2 | KARAR-10 / ADR-104 | ✅ `DECISIONS.md`'de kayıtlı — `Search` kataloğa girer, mevcut 5 alan semantiği korunur |
| 3 | RPR-01 kuralları | ✅ `Map` **10 satır** (mevcut 10 bayrak) · 6 katman taraması yürürlükte |
| 4 | STK-B1 `MovementTypeOptions` | ✅ **8 tür**, tek kaynak, üç yüzey ona bağlı |
| 5 | `BranchScope` uygulaması | ✅ `ReportScope.BranchSql` → `AND (col IN (…) OR col IS NULL)`; STK-10a'da `AND`'lenerek kullanılıyor |
| 6 | Raporun Web/Desktop bağlantısı | ✅ Masaüstü `ReportItems = ReportCatalog.All` · Web `/api/reports/catalog` · **kod değişikliği gerekmemişti** |
| 7 | Mevcut testler | ✅ 1452/1417/0/35 yeşil · `Reports.razor`'da 10 filtre bloğu mevcut |

➡️ **Engelleyici fark yok.** STK-10b güvenle uygulanabilir; tek sınır **kapasite**.

### 18.2 🔴 KENDİ İDDİAMI DÜZELTİYORUM: atomiklik **bayrak başınadır**, 18'in tamamı değil

§15'te *"18 kablolama noktası ATOMİK, dilimlenemez"* demiştim. Doğrulama sırasında bunun
**fazla katı** olduğunu gördüm:

> RPR-01'in koruma testi **her bayrağı KENDİ içinde** denetler (`Map` satırı + o bayrağın 6 katmanı).
> Bir bayrağı **tam** bağlayıp diğer ikisine hiç dokunmamak testi **YEŞİL** bırakır.

➡️ Atomik birim **1 bayrak × 6 katman = 6 nokta**'dır. Yani STK-10b, her adımı **yeşil** biten
**üç** artıma bölünebilir. Bu, "yarım kalırsa testler kırmızı kalır" riskini ortadan kaldırır.

### 18.3 Önerilen yeşil-güvenli bölünme (onay bekliyor)

| Artım | Kapsam | Neden tek başına bütün |
|---|---|---|
| **10b-1** | `MovementType` bayrağı (6 nokta) | Seçenek kaynağı **zaten var** (`MovementTypeOptions`, STK-B1) → yeni altyapı yok. En küçük ve en düşük riskli. |
| **10b-2** | `Search` bayrağı (6 nokta) | Sorgu parçası mevcut `SearchMovements`'tan olduğu gibi taşınır (ADR-104 K-0b). |
| **10b-3** | `Material` bayrağı (6 nokta) + autocomplete deseni | Tek yeni UI deseni burada; scope'a 2461 malzeme **eklenmeyecek** (K-1). |
| **10b-4** | İki hareket ekranının rapora bağlanması + **B-1 düzeltmesi** | Bayraklardan bağımsız; ekran davranışı değişikliği. |

Sıra önerisi: **10b-1 → 10b-2 → 10b-3 → 10b-4**.

### 18.4 ⏸️ NEDEN BU OTURUMDA KODLANMADI

Talimat: *"bu oturumda güvenli biçimde tamamlayıp doğrulayamayacağın ortaya çıkarsa kodlamaya
başlamadan önce DUR… Kod yazıp testleri kırmızı bırakarak oturumu kapatma."*

Bu oturumda tamamlanan ve gönderilen işler: **RPR-01** · **BKM-04** (analiz + karar + uygulama + 44
senaryo + izole PG) · **STK-B1** (24 senaryo) · **STK-10a** (41 senaryo + gerçek XLSX + PG sorgu planı)
— artı üç planlama turu. STK-10b'nin tamamı (18 kablolama + 2 ekran yeniden bağlama + malzeme
autocomplete + ~40 senaryo + 10 XLSX kombinasyonu + PG + çoklu tam-takım koşusu) kalan kapasiteyle
**güvenilir biçimde bitirilemez**.

**Kritik olan:** yarıda kalırsa sonuç "eksik ama yeşil" değil, **KIRMIZI** olurdu — bir bayrak
eklenip 6 katmanı bitirilmezse RPR-01 kırılır. Bu yüzden hiçbir üretim koduna dokunulmadı.
§18.3'teki bölünmeyle her adım yeşil biter.

---

## 19. ✅ STK-10b-1 TAMAMLANDI — Hareket Türü filtresi (2026-08-11)

`MovementType` filtresi **6/6 katmanda** bağlandı. `Search` (10b-2), `Material` (10b-3) ve ekran
bağlantıları (10b-4) **bu artımda YOK**.

### 19.1 MOVEMENTTYPE 6/6 KABLOLAMA

| # | Katman | Ne yapıldı | RPR-01 nasıl doğruluyor |
|---|---|---|---|
| 1 | Katalog / descriptor | `ReportFilters.MovementType = 1024` + `UsesMovementType` + `stock-movements` bayrağı | `ReportDescriptor.UsesMovementType` özelliği (reflection) |
| 2 | İstek modeli | `ReportRequest.MovementTypes` — **SONA** eklendi | `ReportRequest.MovementTypes` özelliği (reflection) |
| 3 | API sorgu | `usesMovementType = d.UsesMovementType` + `ReportReqDto.MovementTypes` + `/api/reports/{type}` | `usesMovementType = d.UsesMovementType` metni + `d.MovementTypes` ≥ 2 |
| 4 | API export | `/api/reports/{type}/export` aynı gövde | aynı `d.MovementTypes` sayımı (2 = sorgu + export) |
| 5 | Web | `@if (_sel?.UsesMovementType == true)` bloğu + `CatItem` alanı + `Bool(e,"usesMovementType")` + **iki gövde** + "Hareket Türü" etiketi | `@if (…` · `Bool(e, …)` · `movementTypes = ` ≥ 2 · etiket |
| 6 | Masaüstü | `ShowMovementType` + `[NotifyPropertyChangedFor]` + `MovementTypes` koleksiyonu + `LoadMovementTypes()` + `BuildTable` + XAML bloğu | `public bool ShowMovementType` · `SelectedReport?.UsesMovementType ==` · `nameof(...)` · ≥3 geçiş · `IsVisible="{Binding ShowMovementType}"` · etiket |

**+ `ReportFilterParityTests.Map`'e satır eklendi** → koruma testi bu bayrağı artık **kendi** denetliyor.
**RPR-01 gevşetilmedi, istisna eklenmedi** — 14/14 yeşil.

### 19.2 🔴 UYGULAMA SIRASINDA YAKALANAN KENDİ HATAM

`ReportRequest.MovementTypes`'ı önce **`LocationIds`'ten ÖNCE** eklemiştim. Bu kayıt API uçlarında
**POZİSYONEL** olarak da kuruluyor (`new ReportRequest(true, d.FromDate, …, d.LocationIds)`) →
araya eklemek `LocationIds` argümanını **sessizce** `MovementTypes`'a kaydırırdı: lokasyon filtresi
çalışmayı bırakır, kimse fark etmezdi. Derlemeden önce görüp **sona** taşıdım ve kayda geçirdim
(alanın yanında kalıcı uyarı yorumu var).

### 19.3 Tek kaynak korundu (STK-B1)

Seçenekler **yalnız** `MovementTypeOptions.All`'dan geliyor — Web bu dosyayı zaten derliyor
(paylaşılan dosya, STK-B1). ➡️ **`/api/reports/scope`'a yeni alan EKLENMEDİ**; iki platform aynı
sabitten besleniyor, ikinci harita/kopya oluşmadı. Kaynak taramalı test bunu kilitliyor.

### 19.4 Semantik

- Filtre **KANONİK `movement_type` anahtarıyla** çalışır; kullanıcıya gösterilen ETİKET gönderilirse
  eşleşmez (testle kanıtlı).
- **Fail-closed:** bilinmeyen anahtar veri sızdırmaz (boş sonuç), "filtre yok" gibi davranmaz.
- Boş liste = filtre yok (tüm türler).
- Çoklu seçim = birleşim (mevcut çoklu-filtre sözleşmesi).
- **`BranchScope` GENİŞLEMEZ:** `WHERE kapsam AND lokasyon AND tür`. Depo A oturumu, Depo B'de üretilmiş
  `usage` hareketini türle isteyince bile **göremez**.
- **Transfer iki satır** kalmaya devam ediyor (tür filtresi `transfer` → 2 satır).

### 19.5 Doğrulamalar

| Doğrulama | Sonuç |
|---|---|
| Çözüm derlemesi | **0 hata** |
| Tam test takımı | **1480 · 1445 geçti · 0 kaldı · 35 atlandı** (taban 1452; **+28 senaryo**) |
| RPR-01 | ✅ **14/14** — gevşetilmedi, istisna yok, yeni bayrağı denetliyor |
| Çevrimdışı / SQLite | 23 senaryo (`StockMovementsTypeFilterTests`), HTTP yok |
| Gerçek HTTP | 16 senaryo (5 yeni: katalog bayrağı · filtre · fail-closed · 3× export XLSX) |
| İzole PostgreSQL | Tür filtresi doğru (20 giriş / 2 transfer bacağı) · fail-closed · tür+lokasyon · **sorgu planı** |
| Gerçek XLSX | 6 kombinasyon (servis) + 3 kombinasyon (HTTP) — hücre hücre |
| Görsel render | ❌ **YAPILMADI** (§19.8) |

### 19.6 ⚡ Sorgu planı (izole PG, tür filtresiyle)

```
Limit → Sort (created_at DESC, id DESC)
  → Nested Loop
      → Index Scan using ix_materials_company on materials
      → Index Scan using ix_stock_movements_material on stock_movements
           Index Cond: (material_id) AND (created_at >= …) AND (created_at <= …)
           Filter: (company_id) AND (movement_type = 'in') AND ((branch_id=…) OR (branch_from_id=…))
```
➡️ **`movement_type` filtresi SQL'e indi**; Limit/Sort yerinde. **Yeni indeks EKLENMEDİ** — plan
gerektirmiyor. Ayrıca testle kanıtlandı: tavan **filtrelenmiş küme** üzerine iniyor (bellekte süzülmüyor).

### 19.7 ⚠️ Güncellenen 2 mevcut test (gevşetme DEĞİL — nöbetçiler görevini yaptı)

Her ikisi de **STK-10a'da benim koyduğum kapsam nöbetçileriydi** ve yeni filtreyi doğru şekilde yakaladılar:

| Test | Neden değişti | Nasıl GÜÇLENDİ |
|---|---|---|
| `StockMovementsReportTests.Rapor_Katalogda_…` | `Filters == Date\|Location` → `…\|MovementType` | Küme hâlâ **tam eşitlikle** sınanıyor; ayrıca **Search (2048) ve Material (4096) hâlâ kapalı** olmalı diye iki yeni nöbetçi eklendi |
| `StockReportLocationTests.Lokasyon_Filtresi_…` | 1024 artık meşru kullanımda | Yerine **"tür filtresi YALNIZ `stock-movements`'ta açık"** tam-eşleşme kontrolü + 2048/4096 nöbetçileri kondu |

### 19.8 Görsel kontrol — YAPILMADI (dürüst kayıt)

Engel değişmedi (BKM-04 · STK-B1 · STK-10a ile aynı): yerel API veritabanında hesap yok,
`launch.json` env değişkeni desteklemiyor, canlıya bağlanmak ve parola girmek yasak, CLI
test-kullanıcı mekanizması yok. **Kontrol edilemeyenler:** filtrenin ekrandaki yeri/hizası ·
8 seçeneğin görünümü · uzun etiketlerin ("Bakım Tüketimi İptali", "İptal (Ters Kayıt)") taşması ·
dar pencere · Web/masaüstü görünüm paritesi.
*(Etiket **içerikleri** ve seçenek kaynağı testlerle doğrulandı; doğrulanmayan yalnız görsel sunum.)*

### 19.9 Kapsam dışı bırakılanlar (bilinçli)

- **"MovementType + Search" XLSX kombinasyonu ÜRETİLMEDİ** — `Search` filtresi STK-10b-2'nindir
  (talimatın açık isteği). Test dosyasında da bu not düşüldü.
- `Material` filtresi ve autocomplete · iki hareket ekranının rapora bağlanması · **B-1 düzeltmesi**
  → 10b-3 / 10b-4.

## 20. ▶️ KALAN ARTIMLAR

| Artım | Kapsam | Durum |
|---|---|---|
| **10b-2** | `Search` bayrağı (6 nokta) — sorgu parçası `SearchMovements`'tan taşınır (ADR-104 K-0b) | ⏳ bekliyor |
| **10b-3** | `Material` bayrağı (6 nokta) + autocomplete deseni (scope'a 2461 malzeme EKLENMEZ, K-1) | ⏳ bekliyor |
| **10b-4** | İki hareket ekranının rapora bağlanması + **B-1 düzeltmesi** | ⏳ bekliyor |

---

## 21. ✅ STK-10b-2 TAMAMLANDI — Serbest metin arama (2026-08-11)

`Search` filtresi **6/6 katmanda** bağlandı. `Material` (10b-3) ve ekran bağlantıları + B-1 (10b-4)
**bu artımda YOK**.

### 21.1 SEARCH 6/6 KABLOLAMA

| # | Katman | Ne yapıldı |
|---|---|---|
| 1 | Katalog / descriptor | `ReportFilters.Search = 2048` + `UsesSearch` + rapora bayrak |
| 2 | İstek modeli | `ReportRequest.SearchText` — **SKALER `string?`**, SONA eklendi |
| 3 | API sorgu | `usesSearch = d.UsesSearch` + `ReportReqDto.SearchText` + `/api/reports/{type}` |
| 4 | API export | `/api/reports/{type}/export` aynı gövde |
| 5 | Web | `@if (_sel?.UsesSearch == true)` + `MudTextField` + `CatItem` + `Bool` + **iki gövde** |
| 6 | Masaüstü | `ShowSearch` + `[NotifyPropertyChangedFor]` + `SearchText` + `BuildTable` + XAML `TextBox` |

**+ RPR-01 `Map` satırı** (`RequestProps = ["SearchText"]`) → **14/14 yeşil**, gevşetilmedi.

⚠️ **Etiket parçası bilinçli olarak uzun seçildi** (`"Ara (kod, malzeme, not, belge)"`). Kısa "Ara"
yazılsaydı RPR-01'in etiket kontrolü mevcut **"Araç"/"Araç ara"** metinlerine takılıp blok silinse
bile geçerdi — RPR-01'de daha önce yakalanan zayıflığın aynısı.

### 21.2 🔴 BULGU: belge notu aramada YOK (mevcut davranış, DEĞİŞTİRİLMEDİ)

Testleri yazarken 7 senaryo kırıldı ve nedeni koddan doğrulandı:

> `StockService.ApplyLine` hareket satırının `note`'unu **NULL** yazar. Kullanıcının giriş/çıkış/
> transfer/sayım belgesine yazdığı not **`stock_documents.note`**'a gider. Mevcut arama ise
> **`sm.note`**'a bakar — `d.note`'a **DEĞİL**.

➡️ Yani bugün "not" araması **yalnız** hareket satırında not bulunan yolları buluyor:
**ters kayıt gerekçesi** ve **bakım tüketimi**. Kullanıcının stok giriş belgesine yazdığı not
aranabilir **değil** — ekranın etiketi ("not") bunu vaat etse de.

**STK-10b-2 bu davranışı DEĞİŞTİRMEDİ** — talimat "yeni arama semantiği icat etme, mevcut davranışı
taşı" diyor. Semantik birebir taşındı ve **mevcut davranış testle kilitlendi**
(`Belge_Notu_Aramada_YOK_Mevcut_Davranis`: hem rapor hem mevcut ekran boş dönüyor → kayma yok).

⛔ **KULLANICI KARARI GEREKİR:** arama `d.note`'u da kapsasın mı? Kapsarsa kullanıcı beklentisine
uyar ama **davranış değişikliğidir** (bugün bulunmayan kayıtlar bulunmaya başlar). Ayrı iş olarak
kaydedilmeli. → `STK-B2` (aşağıda).

### 21.3 Semantik (mevcut ekrandan birebir)

`(m.code LIKE @q OR m.name LIKE @q OR sm.note LIKE @q OR d.invoice_no LIKE @q OR d.doc_no LIKE @q)`
· kalıp `"%" + Trim() + "%"` · boş/yalnız-boşluk → **filtre yok** · kısmi eşleşme (içerir).

**Büyük/küçük harf:** mutlak iddia edilmiyor — `LIKE` davranışı **lehçeye bağlıdır** (SQLite ASCII'de
duyarsız, PostgreSQL duyarlı). Test, davranışın ne olduğunu değil **rapor ile mevcut ekranın AYNI
sonucu verdiğini** kilitliyor → semantik taşındı, değiştirilmedi.

**🔒 `BranchScope` genişlemiyor:** `WHERE kapsam AND lokasyon AND tür AND arama` — arama tüm dalları
tek parantezde toplar ve dış WHERE'e `AND` ile bağlanır. Depo A oturumu, Depo B bacağını arayarak da
göremiyor; yabancı firma kaydı aramayla da çıkmıyor.

### 21.4 Doğrulamalar

| Doğrulama | Sonuç |
|---|---|
| Çözüm derlemesi | **0 hata** |
| Tam test takımı | **1521 · 1486 geçti · 0 kaldı · 35 atlandı** (taban 1480; **+41 senaryo**) |
| RPR-01 | ✅ **14/14** — gevşetilmedi, istisna yok |
| Çevrimdışı / SQLite | 36 senaryo (`StockMovementsSearchFilterTests`), HTTP yok |
| Gerçek HTTP | 21 senaryo (5 yeni: katalog bayrağı · sunucu-taraflı arama · 3× export XLSX) |
| İzole PostgreSQL | Arama doğru · boşluk = filtre yok · arama+tür+lokasyon üçlüsü · **sorgu planı** |
| Gerçek XLSX | 7 kombinasyon (servis) + 3 (HTTP) — hücre hücre |
| Görsel render | ❌ **YAPILMADI** (§21.7) |

### 21.5 ⚡ Sorgu planı (izole PG, arama + tür ile)

```
Limit → Sort (created_at DESC, id DESC)
  → Nested Loop Left Join
      Filter: ((m.code ~~ '%PG%') OR (m.name ~~ '%PG%') OR (sm.note ~~ '%PG%')
               OR (d.invoice_no ~~ '%PG%') OR (d.doc_no ~~ '%PG%'))
      → Nested Loop
          → Index Scan using ix_materials_company on materials
          → Index Scan using ix_stock_movements_material on stock_movements
               Index Cond: (material_id) AND (created_at >= …) AND (created_at <= …)
               Filter: (company_id) AND (movement_type = 'in')
      → Index Scan using stock_documents_pkey on stock_documents
```
➡️ Arama **SQL'e indi** (`~~` = `LIKE`), `Limit`/`Sort` yerinde. **Yeni indeks EKLENMEDİ** —
`LIKE '%…%'` baştan joker içerdiği için B-tree indeksi zaten kullanılamaz; trigram (`pg_trgm`)
gerektirirdi ve mevcut veri hacmi bunu gerektirmiyor. Testle ayrıca kanıtlandı: tavan
**filtrelenmiş küme** üzerine iniyor.

### 21.6 ⚠️ Güncellenen 2 mevcut test (yine kapsam nöbetçileri)

STK-10a/10b-1'de **benim eklediğim** nöbetçiler `Search`'ü de doğru yakaladı. Gevşetilmediler:
`Rapor_Katalogda_…` filtre kümesi **tam eşitlikle** sınanmaya devam ediyor; `Lokasyon_Filtresi_…`
içine **"arama filtresi YALNIZ `stock-movements`'ta açık"** tam-eşleşmesi eklendi.
**`Material` (4096) hâlâ kapalı** nöbetçisi ikisinde de duruyor.

### 21.7 Görsel kontrol — YAPILMADI

Engel değişmedi. **Kontrol edilemeyenler:** Search kutusunun filtreler arasındaki konumu · uzun arama
metninin görünümü · diğer filtrelerle hizası · dar pencere · Web/masaüstü görsel paritesi.

## 22. ▶️ KALAN ARTIMLAR

| Artım | Kapsam | Durum |
|---|---|---|
| **10b-3** | `Material` bayrağı (6 nokta) + autocomplete (scope'a 2461 malzeme EKLENMEZ, K-1) | ✅ **TAMAM** (§23) |
| **10b-4** | İki hareket ekranının rapora bağlanması + **B-1 düzeltmesi** | ✅ **TAMAM** (§24) |
| **`STK-B2`** | Arama `stock_documents.note`'u da kapsasın mı? **Davranış değişikliği → kullanıcı kararı** (§21.2) | ⛔ karar bekliyor |
| **`RPR-02`** 🆕 | HTTP hattında rapor isteği **oturumun şubesini taşımıyor** (§23.5) | ⛔ karar/yeni iş bekliyor |

---

## 23. ✅ STK-10b-3 — `Material` filtresi + autocomplete (2026-08-12)

`ReportFilters.Material = 4096` · `ReportRequest.MaterialIds` (**liste**, kaydın **SON** alanı) ·
`sm.material_id IN (…)`. **6/6 katman** bağlı, **RPR-01 gevşetilmeden yeşil (14/14)**.

### 23.1 Neden `Material` de LİSTE (skaler değil)

`Search` skalerdir (tek metin); `Material` ise diğer **kimlik** filtreleriyle (`BranchIds`,
`VehicleIds`, `LocationIds`, `MovementTypes`) aynı sözleşmeyi izler. Arayüz bugün **tek** malzeme
seçtirir → 0/1 elemanlı gelir; sunucu tarafı ileride çoklu seçime hazırdır ve SQL tek biçimde kalır.

### 23.2 ⚡ Seçenekler ÖN YÜKLENMİYOR (talimat §2/§12 — K-1)

| Platform | Kaynak | Yeni uç? |
|---|---|---|
| Web | **mevcut** `/api/materials?search=…` + `MudAutocomplete` (Talep/Bakım/Stok ekranlarıyla aynı çağrı) | ❌ yok |
| Masaüstü | **mevcut** yerel `Materials.List(session, PageRequest{Limit=30}, term)` | ❌ yok (ağ da yok) |

`/api/reports/scope` **büyümedi** — testle kilitlendi (`Rapor_Kapsamina_Malzeme_Listesi_Eklenmedi`:
scope gövdesinde `FROM materials` yok, Web'de `Read(doc,"materials")` yok).

### 23.3 🔴 Pozisyonel argüman kayması — önce tarandı

`new ReportRequest(` çağrılarının **tamamı** kodlamadan önce tarandı: pozisyonel olan yalnız **2** uç
(`/api/reports/{type}` ve `.../export`). `MaterialIds` **sona** eklendi ve bu kural artık
`MaterialIds_Kaydin_SON_Alani` testiyle **kalıcı olarak** korunuyor (son 4 alanın sırası sabitlendi).

### 23.4 Doğrulamalar

| Doğrulama | Sonuç |
|---|---|
| Çözüm derlemesi | **0 hata** |
| Tam test takımı | **1553 · 1518 geçti · 0 kaldı · 35 atlandı** (taban 1521; **+32 senaryo**) |
| RPR-01 | ✅ **14/14** — gevşetilmedi, istisna yok |
| Çevrimdışı / SQLite | 25 senaryo (`StockMovementsMaterialFilterTests`), HTTP yok |
| Gerçek HTTP | 28 senaryo (7 yeni: katalog · filtre · kombinasyon · 3× export XLSX · açık şube kapsamı) |
| İzole PostgreSQL | Malzeme filtresi · fail-closed · 4'lü kombinasyon · LIMIT filtre sonrası · **sorgu planı** |
| Gerçek XLSX | **10 kombinasyon** (servis) + 3 (HTTP) — hücre hücre |
| Görsel render | ❌ **YAPILMADI** (§23.6) |

### 23.5 ⚡ Sorgu planı — **mevcut indeks kullanılıyor, yeni indeks EKLENMEDİ**

```
Limit → Incremental Sort (created_at DESC, id DESC)
  → Nested Loop
      → Index Scan Backward using ix_stock_movements_material on stock_movements sm
            Index Cond: (material_id = …)          ← filtre SQL'e indi, İNDEKS KULLANILIYOR
            Filter: (company_id = …)
      → Index Scan using ix_materials_company on materials m
```
Malzeme filtresi **zaten var olan** `ix_stock_movements_material` indeksini kullanıyor →
**yeni indeks gerekmedi** (talimat §12: varsayılan olarak eklenmez).

**🔴 BULGU (kapsam dışı, yeni iş `RPR-02`):** HTTP hattında oturumun `OperatingBranchId`'si
**hiç kurulmuyor** — JWT yalnız kullanıcı+firma taşır, `AuthService.CreateSessionForUser` şube
atamaz (kodda doğrulandı; tek istisna içe-aktarma ucudur). Sonuç: **web'de** rapor şube daralması
YALNIZ açık `branchIds` seçimiyle olur; giriş ekranında seçilen şube rapora **yansımaz**. Bu
STK-10b-3'ün getirdiği bir şey **değil** — tüm raporlarda mevcut mimari. Masaüstü (çevrimdışı) yolu
etkilenmiyor: orada oturum şubesi gerçekten dolu ve daraltma testli. **Bu artımda düzeltilmedi.**

### 23.6 Görsel kontrol — YAPILMADI (gerekçe netleştirildi)

Raporlar ekranı **giriş (login) formunun arkasında**; ekranı açmak için parola alanı doldurmak
gerekiyor. **Parolayı ben bir alana yazmam** (güvenlik kuralı) ve canlı sisteme bağlanmam (talimat
§13). Bu yüzden render kontrolü **yapılamadı** — yapılmış gibi de gösterilmedi.
**Kontrol edilemeyenler:** autocomplete genişliği · uzun malzeme adlarının taşması · seçim sonrası
görünüm · seçimin temizlenmesi · Search + Malzeme'nin aynı satırdaki hizası · dar pencere ·
Web/masaüstü görsel paritesi.

---

## 24. ✅ STK-10b-4 — Ekranların rapora bağlanması + B-1 (2026-08-12) · **STK-10 TAMAMLANDI**

Karar kaydı: **ADR-105**. Kapsam: iki Stok Hareketleri ekranının rapor altyapısıyla aynı sözleşmeye
bağlanması ve Web'deki **B-1** hatasının kapatılması.

### 24.1 Yaklaşım — ekran rapora "çevrilmedi", filtreler BİRLEŞTİRİLDİ

Ekranı `TableModel`'e çevirmek gereksiz bir yeniden tasarım olurdu (ekranın kolonları rapordan
zengin: yön · birim fiyat · belge alanları · iptal durumu). Bunun yerine **filtre mantığı tek
kaynağa** indirildi:

```
StockMovementFilterSql.Build(locationIds, movementTypes, search, materialIds)
        ▲                                   ▲
        │                                   │
ReportService.StockMovements        StockService.SearchMovements
   (rapor + XLSX)                    (web ekranı + masaüstü ekranı)
```

➡️ İkinci bir hareket sorgulama mimarisi **yok**. Ekran ile rapor aynı satır kümesini vermek
**zorunda** (testle kilitlendi, §24.4).

### 24.2 🔴 B-1 — önce / sonra

| | ÖNCE (hatalı) | SONRA |
|---|---|---|
| Filtre nerede | Web'de **istemcide**, gelen listenin üzerinde | **SQL'de**, `WHERE` içinde |
| Sıra | LIMIT → (istemci) filtre | **filtre → sıralama → LIMIT** |
| 501./1001. sıradaki depo kaydı | **Sessizce kaybolurdu** | Gelir |
| Masaüstü | Lokasyon filtresi **hiç yoktu** | Var (parite) |

Kesin sıra: `firma → BranchScope → tarih → lokasyon → tür → arama → malzeme →
ORDER BY created_at DESC, tie DESC → LIMIT`.

Uç sözleşmesi: `/api/stock/movements?location=…&type=…&material=…&q=…` — **tekrarlanabilir**
parametreler; **yok** = filtre yok, **boş değer** (`?location=`) = 📦 **Atanmamış**
(rapor sözleşmesindeki `""` ile birebir aynı anlam).

### 24.3 🔒 BranchScope × Location (değişmedi, artık EKRANDA da testli)

| Oturum | Filtre | Sonuç |
|---|---|---|
| Tüm Şubeler | Depo A | A'yı ilgilendiren **iki bacak** da görünür |
| Depo A kapsamı | Depo A | Yalnız kapsam içindeki bacaklar |
| Depo A kapsamı | Depo B | **BOŞ** (yetki aşılamaz) |

### 24.4 Doğrulamalar

| Doğrulama | Sonuç |
|---|---|
| Çözüm derlemesi | **0 hata** |
| Tam test takımı | **1589 · 1554 geçti · 0 kaldı · 35 atlandı** (taban 1553; **+36 senaryo**) |
| RPR-01 | ✅ **14/14** — gevşetilmedi, istisna yok |
| Çevrimdışı / SQLite | 31 senaryo (`StockMovementsScreenReportParityTests`), HTTP yok |
| Gerçek HTTP | `WebStockLocationContractTests` 14 → **19** (+5, B-1 dahil) |
| İzole PostgreSQL | Ekran yolu + rapor yolu aynı sonucu veriyor · LIMIT filtre sonrası · **sorgu planı** |
| Ekran = Rapor | **9 filtre kombinasyonu** — satır SAYISI ve SIRASI birebir |
| Rapor = XLSX | **6 kombinasyon** hücre hücre |
| Görsel render | ❌ **YAPILMADI** (§24.6) |

### 24.5 ⚡ Sorgu planı (izole PG, ekran sorgusu + lokasyon filtresi)

```
Limit → Sort (created_at DESC, id DESC)
  → Nested Loop
      → Index Scan using ix_materials_company on materials
      → Index Scan using ix_stock_movements_material on stock_movements
            Index Cond: (material_id = m.id) AND (created_at >= …) AND (created_at <= …)
            Filter: (company_id = …) AND ((branch_id = …) OR (branch_from_id = …))
```
➡️ Lokasyon koşulu **SQL'e indi** ve `Limit`'in **altında** — yani tavan filtrelenmiş kümeye
uygulanıyor. **Yeni indeks EKLENMEDİ** (mevcut indeksler yetti). Test DB (`stk10b4_test`) silindi,
PG durduruldu.

### 24.6 Görsel kontrol — YAPILMADI

Stok Hareketleri ekranı **giriş (login) formunun arkasında**; açmak için parola alanı doldurmak
gerekir. Parolayı bir alana **yazmam** (güvenlik kuralı) ve canlıya bağlanmam (talimat).
Yapılmış gibi gösterilmedi. **Kontrol edilemeyenler:** lokasyon filtresinin diğer alanlarla hizası ·
uzun depo/malzeme adlarının taşması · dar pencere · boş sonuç görünümü · filtre temizleme ·
Web/masaüstü görsel paritesi.

### 24.7 🔴 Yol üstünde bulunan KARARSIZ TEST — kök neden bulundu, düzeltildi

Tam takımın bazı koşularında **1** test kırılıyordu. Peşine düşüldü ve yakalandı:
`SyncBalancePayloadTests.Yalniz_Bakiye_Degisirse_Sunucu_Etkilenmez_Yerel_Calismaya_Devam_Eder`.

**Neden üretim kodu değil, testin kendisiydi:** `Assert.DoesNotContain("777", Snapshot())` senkron
paketinin TAMAMINDA ham `"777"` metnini arıyordu. Paketteki **rastgele GUID'lerden** biri `777`
dizisini içerdiğinde test sebepsiz kırılıyordu (yakalanan örnek: `…0077788757fd6`).

**Düzeltme gevşetme DEĞİL, keskinleştirme:** artık paketin `tables` bölümünde `stock_balances`
tablosunun **hiç olmadığı** (asıl sözleşme) + `"quantity":"777"` alanının bulunmadığı doğrulanıyor.
**Retry/skip kullanılmadı.** Bu kırılganlık STK-10b-4'ten ÖNCE de vardı; ilgisizdi.
Son durum: tam takım **1589 · 1554 geçti · 0 kaldı** (`KNOWN_ISSUES` R34 kapatıldı).
