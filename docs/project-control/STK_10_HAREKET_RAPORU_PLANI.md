# STK-10 — "Stok Hareketleri" Raporu · Envanter + Uygulama Planı

> Oluşturuldu: **2026-08-11** · Kaynak: `STK-06` §5 bulgusu (R-1)
> **DURUM: 📋 PLAN HAZIR — KOD BAŞLAMADI** (gerekçe §9)
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

### B-2 · `STK-B1` artık STK-10'un ÖN KOŞULU
`movement_type` üretimde **7 değer**: `opening · in · out · transfer · adjustment · usage · usage_reverse`.
`StockMovementRow.TypeText` yalnız **5**'ini çeviriyor → kullanıcı ekranda ham İngilizce
**"usage" / "usage_reverse"** görüyor. **BKM-04 bunu görünür hâle getirdi**: artık her bakım tüketimi
gerçek depolu bir `usage` satırı üretiyor.
➡️ "Hareket türü" filtresi bu katalog düzeltilmeden yazılamaz (filtre listesi nereden gelecek?).
**STK-B1, STK-10'un 1. adımı olarak içine alınmalıdır** — ayrı iş olarak bırakılırsa STK-10 yarım kalır.

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

STK-10 iki **yeni** `ReportFilters` değeri gerektirir. RPR-01'in koruma testi bunları **zorunlu** kılar:
satır eklenmezse test kırılır, katman atlanırsa test kırılır.

| Bayrak | `ReportRequest` | Web JSON | Masaüstü | Etiket |
|---|---|---|---|---|
| `Material = 1024` | `MaterialIds` | `materialIds` | `ShowMaterial` | "Malzeme" |
| `MovementType = 2048` | `MovementTypes` | `movementTypes` | `ShowMovementType` | "Hareket Türü" |

Her bayrak için dokunulacak **6 yer** (RPR-01 `Checklist`): katalog · istek modeli · API (katalog alanı
+ DTO + **sorgu ve export** uçları) · Web (`@if` bloğu + `CatItem` + `Bool` + **iki gövde**) ·
Masaüstü VM (`ShowX` + `NotifyPropertyChangedFor` + `BuildTable`) · Masaüstü XAML.
**+ `ReportFilterParityTests.Map`'e iki satır.**

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
**Filtreler:** `Date | Location | Material | MovementType` · **`RequiresDate = true`** (defter büyür,
tarihsiz tam tarama yasak — mevcut ağır rapor kuralı).

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
- ⚠️ Ekranın **arama kutusu** (kod/ad/not/belge) rapor filtrelerinde karşılığı yok. Ya `ReportRequest`'e
  `SearchText` eklenir (yeni bayrak → 6 katman daha) ya da ekranda kalır. **Bu bir üründür kararı** — §10.

## 7. VERİ MODELİ / MIGRATION

**Gerekmiyor.** `stock_movements` tüm alanları taşıyor (`branch_id`, `branch_from_id`, `group_id`,
`is_reversed`, `reverses_movement_id`). Yeni kolon/tablo/indeks **açılmayacak**.
İndeks kararı: `ix_stock_movements(company_id, created_at)` mevcut mu diye **gerçek sorgu planıyla**
ölçülecek; gerekmiyorsa eklenmeyecek.
**STK-11 (float artığı) bu işte ÇÖZÜLMEYECEK** — rapor ham değeri gösterir, sessiz yuvarlama yapmaz.

## 8. UYGULAMA SIRASI (kod bu adımdan başlar)

| # | İş | Dosya |
|---|---|---|
| 0 | **STK-B1**: `MovementTypeOptions` (tek doğru kaynak) + `TypeText` bu kaynaktan | `Application/Reports` + `StockService` |
| 1 | `ReportFilters.Material` + `MovementType` + `UsesX` | `ReportCatalog.cs` |
| 2 | `ReportRequest.MaterialIds` + `MovementTypes` (sona) | `ReportModels.cs` |
| 3 | `ReportService.StockMovements` — tek sorgu, Kaynak/Hedef, lokasyon semantiği | `ReportService.cs` |
| 4 | Katalog kaydı + `Run` dispatch | `ReportCatalog.cs` |
| 5 | API: katalog alanları · DTO · **sorgu + export** uçları · scope'a `movementTypes` | `Program.cs` |
| 6 | **Masaüstü** (kural: önce masaüstü) — Raporlar ekranına 2 filtre + hareket ekranını rapora bağla | VM + XAML ×2 |
| 7 | Web — aynı ikisi | `Reports.razor` + `StockMovements.razor` |
| 8 | **RPR-01 `Map`'e 2 satır** + malzeme filtresinin scope istisnası belgelenir | `ReportFilterParityTests.cs` |
| 9 | 30 senaryo (§12) + **gerçek XLSX round-trip** (ClosedXML test projesinde mevcut) | yeni test dosyaları |
| 10 | Doğrulama: build · tam takım · SQLite · izole PG · gerçek HTTP · çevrimdışı · tarayıcı | — |

## 9. ⚠️ NEDEN KOD BU OTURUMDA BAŞLAMADI

Talimat §19: *"Eğer tek oturumda güvenli biçimde kodlama, build, tam test, yeni STK-10 testleri, SQLite,
PostgreSQL, Web/Desktop paritesi, export, gerçek HTTP, offline doğrulanamayacaksa kodlamaya başlama."*

Bu oturumda **`RPR-01`** (18 senaryo) ve **`BKM-04`** (analiz + karar + tam uygulama + 44 senaryo + izole
PG) tamamlandı ve gönderildi. STK-10'un gerçek boyutu envanterden sonra netleşti:

- **2 yeni filtre bayrağı × 6 katman = 12 kablolama noktası** (RPR-01 bunları zorunlu kılıyor)
- **+ STK-B1** (B-2: ön koşul, ayrı sanılıyordu)
- **+ 2 ekranın** rapor altyapısına bağlanması (Web + masaüstü)
- **+ ~30 senaryo** ve ilk kez **gerçek XLSX satır-satır karşılaştırması**
- **+** izole PG · gerçek HTTP · çevrimdışı · tarayıcı render

Kalan oturum kapasitesi bu doğrulamaların **tamamını** garanti etmiyor. Bu proje boyunca korunan kural —
*"yarım bırakılmış stok/rapor kodu değerleri sessizce yanlış gösterir"* — gereği kodlamaya
**başlanmadı**. Sonraki oturum **doğrudan §8 adım 0'dan** başlayabilir; envanter ve kararlar yukarıdadır.

## 10. KULLANICIDAN GEREKEN TEK KARAR

**Mevcut ekranın "Ara (kod, malzeme, not, belge no)" kutusu ne olsun?**
Rapor filtrelerinde karşılığı yok ve kullanıcıların alıştığı bir alan.

| Seçenek | Sonuç |
|---|---|
| **A** — Arama ekranda kalsın, raporda olmasın | En küçük değişiklik; ama ekran ve export **farklı** süzebilir (STK-10'un amacına aykırı) |
| **B** — `ReportFilters.Search` olarak kataloğa eklensin | Ekran = export garantisi; **6 katman daha** kablolama |
| **C** — Arama kaldırılsın, yerine Malzeme filtresi geçsin | En temiz sözleşme; ama "not/belge no" araması **kaybolur** (davranış kaybı) |

Öneri: **B** — STK-10'un asıl amacı "ekran ve export aynı veriyi üretsin"; A bunu kırar, C kullanıcıdan
mevcut bir yeteneği alır. Karar verilmeden §8 adım 1'e geçilmemeli.

## 11. YENİ DEVREDİLEN İŞ

| Kod | İş | Kaynak |
|---|---|---|
| — | **B-1 ve B-3 STK-10 içinde çözülecek** (ayrı iş açılmadı) | §2 |
| `STK-B1` | Artık **bağımsız değil** — STK-10 adım 0 | §2 (B-2) |
