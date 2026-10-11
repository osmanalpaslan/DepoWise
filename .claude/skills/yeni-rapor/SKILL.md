---
name: yeni-rapor
description: DepoWise'a yeni bir rapor eklerken, mevcut rapora sütun/filtre eklerken ya da yeni bir rapor filtresi (ReportFilters bayrağı) açarken kullan. Katalog → servis → dispatch → API → masaüstü → web → testler zincirini eksiksiz yürütür; iki ortamın aynı kalmasını ve sabitlenmiş sayı/nöbetçi testlerin bilinçli güncellenmesini sağlar. "yeni rapor", "rapora sütun ekle", "rapora filtre ekle" isteklerinde çağrılır.
---

# Yeni rapor / rapora sütun-filtre (DepoWise)

Raporlar TEK `TableModel` üretir; masaüstü (`ReportsView` DataGridView) ve web (`Reports.razor`) aynı modeli çizer →
servis doğruysa iki ekran da otomatik doğru. Eksik kalan yer genelde FİLTRE kablolaması ve nöbetçi testlerdir.

## A. Yeni rapor
1. **Katalog** `src/DepoWise.Application/Reports/ReportCatalog.cs` → `ReportDescriptor(key, ad, kullanıcı açıklaması,
   kategori, ReportGroup.Standard|Manager, filtre bayrakları, RequiresDate, ExportStandard, InfoNote, DataModule)`.
   - Defter/hareket gibi büyüyen veride `RequiresDate: true`. `DataModule` = verinin ekranı (ör. "stock", "fuel").
   - `InfoNote` sade Türkçe: satır neyi temsil eder, neler dahil DEĞİL (iptaller), toplam nasıl.
2. **Servis** `src/DepoWise.Infrastructure/Reporting/ReportService.cs`: `AccessControl.Require` + `ReportGate.EnsureRunnable`
   + `ReportGate.ResolveCompany` + `ReportScope.BranchSql/BindBranch` (şube DIŞ SINIR) + `DateFilter/BindDates` +
   `InList/BindList`. TEK sorgu, adlar JOIN ile (N+1 yok), LIMIT SQL'de (`maxRows`). Para/miktar `Money.Parse`.
   - SQLite + PostgreSQL ORTAK SQL: `COALESCE`, `CAST(.. AS REAL)`, skaler alt sorgu; `GROUP_CONCAT/string_agg` YOK
     (isim birleştirme C#'ta). Metin kolonuna `COALESCE(x,'')` yalnız kolon TEXT ise (PG tip hatası).
   - Sayısal hücre `Num(...)` / `NumCell`, `numeric[]` dizisi başlık sayısıyla AYNI uzunlukta, `TotalRow` ayrı.
   - En önemli sütunu kullanıcı söylediyse başa yakın koy. Sütun kimliği BAŞLIK METNİDİR (genişlik kaydı ona bağlı).
3. **Dispatch**: `ReportService.Run` içindeki `switch` → `"key" => Metot(s, req, maxRows)`.
4. **Sayı nöbetçileri**: `ReportArchitectureTests` ve `CustomRaporTests` → `ReportCatalog.All.Count` +1 (yorumda gerekçe).

## B. Yeni filtre bayrağı (ReportFilters)
Sıradaki boş bit (2'nin kuvveti) kullanılır. Bu 7 yer BİRLİKTE değişir:
1. `ReportFilters` enum + `ReportDescriptor.Uses<X>`.
2. `ReportRequest` (`ReportModels.cs`) → alan **EN SONA** (API pozisyonel kuruyor; araya eklemek alanları kaydırır).
3. API `Program.cs` (kullanıcı hunk'ları var → `guvenli-commit`): katalog JSON `uses<x>`, `ReportReqDto` SONUNA alan,
   iki `new ReportRequest(...)` çağrısına (ekran + export) argüman.
4. Masaüstü `ReportsViewModel`: `[NotifyPropertyChangedFor(nameof(Show<X>))]`, `Show<X>`, seçenek koleksiyonu + yükleme,
   istekte alan. `ReportsView.axaml`: `ctrl:FormField` (etiket iki ortamda ortak parça içersin).
5. Web `Reports.razor`: `CatItem` POZİSYONEL kaydına `Uses<X>` + `Bool(e,"uses<x>")` AYNI sırada, seçim alanı, kapsam
   yenilenince sıfırlama, İKİ istek gövdesine (ekran + export) alan.
6. Testler: `ReportFilterParityTests.Map` satırı; `StockMovementsMaterialFilterTests.MaterialIds_Kaydin_SON_Alani`
   (alan sırası); `StockReportLocationTests` (bayrağı kullanan rapor listeleri + "sıradaki boş bayrak" nöbetçisi).
7. Seçim çipi/kaldırma komutu eklediysen `IslemOnaylariTests.Muaf` (form içi, kayıt değil) gerekçeli.

## C. Mevcut rapora sütun
- Araya sütun eklemek İNDEKSLE okuyan testleri kaydırır: `grep -rn '"<key>"\|<Metot>(' tests/` → her test dosyasında
  `[n]` indekslerini kaydır (FuelConsumption, GunlukRaporlar, PostgresGunlukRaporlar, YakitTarihGun… tam takım yakalar).
- `numeric[]` ve `TotalRow` uzunluklarını güncelle; `InfoNote`'a sütunun anlamını ekle.

## D. Test + doğrulama
- SQLite servis testi (gerçek servislerle veri üret: StockService/FuelService…), PG paritesi `[Collection("PostgresSchema")]`
  + `PostgresTestGuard.SkipUnlessSafe()`, filtre kesişimleri, yabancı firma kimliği sızmaz, iptaller hariç.
- Arayüz: `test-kosusu` → masaüstü headless ile filtre görünür/çip/çalıştırma (ör. UIT14), ekran görüntüsünü gözle kontrol.
- Tam takım; sonra `yayin`. Kullanıcıya rapor: sütun listesi + filtre davranışı sade dille.
