# RPR-01 — Web ↔ Masaüstü Rapor Filtre Paritesi

> Oluşturuldu: **2026-08-11** · Kaynak: `STK-06` §3 bulgusu (P-1) · **DURUM: ✅ TAMAMLANDI**
> Üretim davranışı **değişmedi** — bu iş bir *koruma* işidir, özellik işi değil.

---

## 1. KAPATILAN RİSK

Rapor **verisi** iki platformda ortaktır (tek `ReportService`) — bu yapısal olarak garanti.
Ama filtre **arayüzleri** ayrı ayrı ELLE yazılıyor:

| Katman | Web | Masaüstü |
|---|---|---|
| Görünürlük | `Reports.razor` → `@if (_sel?.UsesX == true)` | `ReportsViewModel` → `ShowX` + `[NotifyPropertyChangedFor]` |
| Ekran bloğu | aynı `@if` içinde `MudSelect` | `ReportsView.axaml` → `IsVisible="{Binding ShowX}"` |
| İstek gövdesi | `Run()` **ve** `ExportExcel()` **ayrı ayrı** | `BuildTable()` (tek yer) |

Kataloğa yeni bir filtre eklendiğinde bunlardan biri unutulursa **hiçbir şey patlamaz**:
filtre o platformda sessizce YOKTUR ve aynı kullanıcı iki cihazda farklı sonuç görür.
STK-06'da tam olarak bu risk oluştu (elle önlendi). RPR-01 bunu **kalıcı olarak** yakalar.

## 2. TAM ENVANTER

**12 rapor · 10 filtre bayrağı · 4 katman.** Bir filtrenin tam bağlanması için **6 yerde** iş var:

| # | Dosya | Ne gerekiyor |
|---|---|---|
| 1 | `ReportCatalog.cs` | `ReportFilters.X` + `ReportDescriptor.UsesX` |
| 2 | `ReportModels.cs` | `ReportRequest.XIds` |
| 3 | `Api/Program.cs` | katalog yanıtında `usesX` · `ReportReqDto` alanı · **SORGU ve EXPORT** uçlarında aktarım |
| 4 | `Web/Reports.razor` | `@if` bloğu · `CatItem` alanı · `Bool(e,"usesX")` · **SORGU ve EXPORT** gövdeleri |
| 5 | `Desktop/ReportsViewModel.cs` | `ShowX` · `[NotifyPropertyChangedFor]` · `BuildTable()` aktarımı |
| 6 | `Desktop/ReportsView.axaml` | `IsVisible="{Binding ShowX}"` bloğu + etiket |

### Rapor × filtre dağılımı (mevcut durum — hepsi ✅ tam bağlı)

| Rapor | Kullandığı filtreler |
|---|---|
| Araç Raporu · Yakıt Tüketim | Tarih · Şube · Araç · Araç Türü |
| Bakım Raporu | Tarih · Şube · Araç · Araç Türü · Bakım Tanımı · Teknisyen |
| Depo Girişi | Tarih · Şube · Tedarikçi |
| Talep Raporu | Tarih · Şube · Talep Eden · Durum |
| **Stok Durumu** | **Depo/Şantiye (Location)** |
| **Stok Sayım** | Tarih · **Depo/Şantiye (Location)** |
| Malzeme/Araç Şablonlu · Şablon Dışı | — (filtre yok) |
| Durum Rapor | Tarih |

## 3. TASARIM KARARI — NEDEN ORTAK UI KATMANI KURULMADI

| Seçenek | Değerlendirme |
|---|---|
| Ortak filtre UI bileşeni (Web+Avalonia) | ❌ İki ayrı UI framework'ünü birleştirmek gerekir — talimat §11 kapsam dışı |
| Filtre meta verisini Application'a taşımak | ❌ Application katmanına Razor/XAML bilgisi sızardı; üretim kodu riski |
| **Katalogdan sürülen parite TESTİ** | ✅ **Seçildi** — üretim kodu **hiç değişmez**, koruma kalıcı |

**Uygulanan:** Tek doğru kaynak `ReportFilters` enum'udur. Test, enum'un HER değeri için bir
"kablolama satırı" ister; satır yoksa kırılır. Her satır 4 katmanda doğrulanır.
Web ve Desktop projeleri test projesinden **referans verilmez** (Razor/Avalonia derlenmez) — iki
arayüzün **kaynak metni** okunur.

> İsimlendirme tek tip değildir (`Branch → ShowBranchSelect`, `Vehicle → ShowVehicleSelect`).
> Bu yüzden kablolama tablosu istisnaları **açıkça yazar**; tablo aynı zamanda sözleşmenin belgesidir.

## 4. TESTİN KENDİSİ TEST EDİLDİ (negatif ispat)

Tarayıcı **saf fonksiyondur** (`Scan`) ve metinleri parametre alır → gerçek kaynağın **kopyası**
kasten bozulup testin bunu yakaladığı kanıtlanır. **Üretim koduna sahte hata bırakılmadı.**

| Simüle edilen hata | Yakalandı |
|---|---|
| Filtre yalnız masaüstünde (Web `@if` bloğu yok) | ✅ |
| Ekranda var ama **EXPORT gövdesinde** gönderilmiyor | ✅ |
| Filtre yalnız Web'de (masaüstü XAML bloğu yok) | ✅ |
| Masaüstünde `[NotifyPropertyChangedFor]` unutulmuş (filtre takılı kalır) | ✅ |
| API katalog yanıtından alan düşmüş (Web filtreyi hiç göremez) | ✅ |

### 🔴 Negatif ispatın bulduğu gerçek zayıflık (kendi testimde)

İlk yazdığım Web kontrolü `_sel?.UsesLocation == true` arıyordu. Bu metin **istek gövdelerinde de**
geçtiği için, ekran bloğu tamamen silinse bile test **geçiyordu**. Negatif ispat testi bunu yakaladı;
token `@if (_sel?.UsesLocation ==` olarak sıkılaştırıldı.
➡️ Negatif ispat olmasaydı RPR-01 **çalışmayan bir koruma** olarak "tamamlandı" sayılacaktı.

## 5. KORUNAN DAVRANIŞLAR (STK-06 semantiği)

- **🌐 Tüm Şubeler ≠ 📦 Atanmamış** — testle kilitli, iki arayüz de ayrı ayrı sunuyor.
- Stok Durumu iki modu (filtre boş → firma toplamı, kırılım kolonu YOK; depo seçili → kırılım).
- Stok Sayım "Sayılan Depo" kolonu + lokasyon filtresi.
- Tarih varsayılanı (Bu Ay) iki arayüzde de uygulanır.
- Talep durumları tek kaynaktan (`RequestStatusOptions`).
- Sorgu ve export uçları **aynı** `ReportRequest`'i kurar (metin düzeyinde birebir doğrulandı).

## 6. ÇEVRİMDIŞI

**Dokunulmadı.** Masaüstü raporu yerel SQLite'tan üretilir; filtre seçenekleri de yerelden gelir
(`BranchService`). Testler bunu HTTP kullanmadan koşturur (`ApiTestHost` yok).
Masaüstü raporlarına **hiçbir API bağımlılığı eklenmedi**.

## 7. DEĞİŞEN DOSYALAR

| Dosya | Değişiklik |
|---|---|
| `tests/DepoWise.Tests/ReportFilterParityTests.cs` | **YENİ** — 18 senaryo (2 sınıf: kaynak taraması + davranış) |
| `src/DepoWise.Web/Components/Pages/Reports.razor` | **yalnız yorum düzeltmesi** — "rapor değişince sıfırlanır" yanlıştı; gerçekte kapsam yenilenince sıfırlanır, rapor değişince seçim korunur (masaüstü de aynı) |

**Üretim davranışı değişmedi.** Yeni servis/uç/migration/senkron değişikliği **yok**.

## 8. DOĞRULAMALAR

| Doğrulama | Sonuç |
|---|---|
| Çözüm derlemesi | **0 hata** |
| Tam test takımı | **1343 · 1310 geçti · 0 kaldı · 33 atlandı** (taban 1325; **+18 senaryo**) |
| Negatif ispat | 5 simüle hatanın **5'i** yakalandı |
| Çevrimdışı | Filtreli rapor yerel SQLite'ta çalışıyor, HTTP yok |
| Görsel (browser/XAML render) | ❌ **YAPILMADI** — bkz. §9 |
| PostgreSQL | Gerekmedi (SQL/lehçeye dokunulmadı); 33 atlanan PG testi tabanla aynı |

## 9. AÇIK SINIRLAR (dürüst kayıt)

1. **Görsel render kontrolü yapılmadı.** Doğrulama kod/kaynak düzeyindedir: filtrenin VAR olduğu,
   doğru bayrağa BAĞLI olduğu, etiketinin bulunduğu, değerin sorgu **ve** export'a aktarıldığı.
   Filtrenin ekranda **doğru hizalandığı / okunaklı olduğu** bu testin kapsamında **değildir**.
2. **Etiket eşitliği "içerir" düzeyindedir.** Birebir aynı metin zorunlu tutulmadı (piksel paritesi
   hedef değil): ortak, ayırt edici bir parça aranır (ör. "Depo / Şantiye").
3. **Export çıktısı XLSX olarak açılıp karşılaştırılmadı.** Export paritesi iki yoldan kanıtlandı:
   iki ucun **aynı** `ReportRequest`'i kurduğu (kaynak eşitliği) + aynı gövdenin aynı tabloyu ürettiği.
4. Test **kaynak ağacından** çalışmalıdır (arayüz dosyalarını okur). Kaynak bulunamazsa test
   **açık hata verir** — sessizce atlanmaz (talimat §10).

## 10. SIRADAKİ İŞ

`BKM-04` — **bakım malzeme tüketimi `branch_id=NULL` yazıyor** → tüketilen malzeme ATANMAMIŞ kovasına
düşüyor. STK-08 geçmiş atanmamış stoğu temizleme aracını verdi, ama bu yol **yenisini üretmeye devam
ediyor**. Bakım kaydında depo seçimi bir **iş kuralı** kararı gerektirir (aracın şubesi mi, kullanıcının
şubesi mi, açık seçim mi) — uydurulmayacak.
