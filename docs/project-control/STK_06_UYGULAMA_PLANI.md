# STK-06 — Rapor + Dashboard Lokasyon Boyutu · Envanter + Uygulama Planı

> Oluşturuldu: **2026-08-11** · FAZ C · Ön koşul: `STK-01…05` ✅ (`07d77e0`, `cd5c4da`, `3d1996f`, `b053fde`)
> **DURUM: ENVANTER + PLAN TAMAM · KOD UYGULAMASI BAŞLAMADI** (gerekçe §9)

---

## 1. TAM RAPOR ENVANTERİ (11 rapor · `ReportCatalog.All`)

Rapor altyapısı **katalog-güdümlüdür**: `ReportDescriptor` (anahtar, filtre bayrakları, grup, export yetkisi)
→ `ReportService.<Metot>` → aynı `TableModel` hem Web hem masaüstü hem Excel'e gider.
➡️ **Web ↔ masaüstü veri paritesi YAPISAL olarak garanti** (tek servis, tek sorgu). Fark yalnız arayüzde olabilir.

| # | Rapor | Web | Masaüstü | Stok kullanıyor mu? | Kaynak tablo | Bugünkü kapsam | Lokasyon gerekli mi? |
|---|---|---|---|---|---|---|---|
| 1 | **Stok Durumu** (`stock`) | ✅ | ✅ | **EVET — miktar** | `stock_balances` (STK-02'de `StockTotalSubquery`) | **Firma geneli** · filtre YOK | 🔴 **EVET — toplam + kırılım + filtre** |
| 2 | **Stok Sayım** (`stock-count`) | ✅ | ✅ | **EVET — sayım farkı** | `stock_count_lines` + `stock_documents` | Tarih filtresi · **hangi depo sayıldı BELLİ DEĞİL** | 🔴 **EVET — kolon + filtre** |
| 3 | Malzeme — Şablonlu (`materials-template`) | ✅ | ✅ | **EVET — toplam stok** | `stock_balances` (alt sorgu) | Firma geneli (doğru) | 🟡 Kırılım gereksiz · **ATANMAMIŞ görünürlüğü** |
| 4 | Malzeme — Şablon Dışı (`materials-nontemplate`) | ✅ | ✅ | **EVET — stok kolonu** | `stock_balances` (alt sorgu) | Firma geneli (doğru) | 🟡 aynı |
| 5 | Bakım Raporu (`maintenance`) | ✅ | ✅ | Dolaylı — **malzeme MALİYETİ** | `maintenance_materials` | Şube = `op_branch_id` (**stok lokasyonu değil**) | ⚪ **HAYIR** → `BKM-04` |
| 6 | Araç Raporu (`vehicle`) | ✅ | ✅ | Dolaylı — parça maliyeti | `maintenance_materials` | Şube = `op_branch_id` | ⚪ HAYIR |
| 7 | Yakıt Tüketim (`fuel`) | ✅ | ✅ | ❌ (yakıt ayrı defter) | `fuel_distributions` | — | ⚪ HAYIR |
| 8 | Depo Girişi (`fuel-depot`) | ✅ | ✅ | ❌ (yakıt) | `fuel_depot_entries` | Şube = `op_branch_id` | ⚪ HAYIR |
| 9 | Talep Raporu (`requests`) | ✅ | ✅ | ❌ (miktar talep kalemi) | `material_requests` | Şube = talep şubesi | ⚪ HAYIR |
| 10 | Araç — Şablonlu / Şablon Dışı | ✅ | ✅ | ❌ | `vehicles` | — | ⚪ HAYIR |
| 11 | Durum Rapor (`status`) | ✅ | ✅ | ❌ (kayıt SAYISI) | çok tablo | Şube bazlı sayım | ⚪ HAYIR |

### 1.1 Export
Export **ayrı sorgu kullanmıyor**: `/api/reports/{type}/export` aynı `ReportService` metodunu çağırıp
`TableModel`'i Excel'e çeviriyor. ➡️ **Lokasyon filtresi eklenince export otomatik aynı filtreyi kullanır**
(STK-04'te Web listesinde görülen "ekran doğru, export eski sorgu" riski burada **yapısal olarak yok**).

## 2. DASHBOARD DENETİMİ

| KPI / özet | Kaynak | Durum |
|---|---|---|
| Malzeme sayısı · Araç · Personel · Bekleyen talep | `DashboardService.Count(...)` | Stokla ilgisiz ✅ |
| **Düşük stok sayısı** (`LowStockCount`) | `StockTotalSubquery` (STK-02) | **Firma toplamına göre** ✅ doğru |
| **Düşük stok uyarı listesi** (`LowStockList`) | `StockTotalSubquery` (STK-02) | ✅ doğru · `LIMIT 20` |
| Yakıt durumu | `fuel_*` | Stokla ilgisiz ✅ |

**`StockTotalSubquery` mantığının başka yerde TEKRAR EDİLİP EDİLMEDİĞİ arandı** → `stock_balances` geçen
üretim sorguları yalnızca STK-02'de dönüştürülen 8 JOIN + 4 okuma; **kopya/ikinci uygulama yok**.
Dashboard STK-04'te gerçek üretim kopyasıyla doğrulandı (2459 malzeme, satır çoğaltma yok).

➡️ **Dashboard'da düzeltme gerekmiyor.** İhtiyaç varsa "lokasyon bazlı dashboard" ayrı bir üründür → `STK-09`.

## 3. WEB ↔ MASAÜSTÜ PARİTE

| Boyut | Durum |
|---|---|
| Veri/hesaplama | **Aynı** — tek `ReportService` (yapısal garanti) |
| Filtre listesi | `ReportDescriptor.Filters` ortak; **UI blokları AYRI yazılıyor** (Web `Reports.razor`, masaüstü `ReportsViewModel`+`ReportsView.axaml`) → yeni filtre **iki tarafa da elle eklenmeli** |
| Export | Aynı servis → aynı sonuç |
| Çevrimdışı | Masaüstü raporu **yerel SQLite**'tan üretilir; API'ye gitmez ✅ |

⚠️ **Bulgu (P-1):** Filtre UI'si "otomatik gelir" diye belgelenmiş ama **gerçekte değil** — her filtre için
Web'de bir `@if (_sel?.UsesX)` bloğu, masaüstünde bir `ShowX` + XAML bloğu var. Yeni filtre eklerken
biri unutulursa **sessiz parite kaybı** olur. → `RPR-01` olarak devredildi (§8).

## 4. LOKASYON FİLTRESİ TASARIMI (karar)

### K-1 · `branchId` ≠ `locationId` — AYRI filtre eklenecek
Mevcut `ReportFilters.Branch`, Araç/Yakıt/Bakım/Talep raporlarında **`op_branch_id`** (kaydı işleyen şube)
anlamındadır — **stok lokasyonu DEĞİL**. Aynı bayrağı stok raporunda kullanmak iki kavramı birleştirir.
➡️ **Yeni `ReportFilters.Location` (512)** + **`ReportRequest.LocationIds`** eklenir. `BranchIds` DOKUNULMAZ.

### K-2 · Hangi rapora eklenecek (körlemesine dropdown YOK)
| Rapor | Karar | Gerekçe |
|---|---|---|
| **Stok Durumu** | ✅ Filtre + **kırılım satırları** | "Hangi depoda ne kadar" bu raporun asıl sorusu |
| **Stok Sayım** | ✅ Filtre + **"Depo" kolonu** | Sayım artık depoya ait (STK-04/05); hangi depo sayıldığı raporda yok |
| Malzeme Şablonlu/Şablon Dışı | ❌ Filtre yok | Bunlar **katalog kalitesi** raporlarıdır; stok yalnız yan bilgi. Firma toplamı doğru |
| Diğer 7 rapor | ❌ | Stok miktarı kullanmıyor ya da `op_branch_id` zaten var |

### K-3 · Filtre seçenekleri
`Tüm Şubeler` (filtre yok, **ATANMAMIŞ dahil**) · gerçek depolar · **`📦 Atanmamış`** (yalnız `location_id=''`).
Kaynak: **mevcut `/api/reports/scope` → `branches`** (yeni uç açılmaz). Masaüstü aynı listeyi **yerelden** alır.

### K-4 · Stok Durumu raporunun iki modu
| Mod | Çıktı |
|---|---|
| Filtre boş (varsayılan) | **Bugünkü davranış birebir** — malzeme başına tek satır, firma toplamı (regresyon yok) |
| Lokasyon seçili | Yalnız o lokasyon(lar)ın miktarı + **Depo kolonu** |

Toplam ve kırılım **aynı sorgudan** üretilir → "lokasyon toplamı = firma toplamı" invariantı yapısal olur.
`DISTINCT` **kullanılmayacak**.

## 5. TRANSFER RAPORLARI
Transfer için **ayrı rapor YOK**; transfer hareketleri `Stok Hareketleri` ekranında görünür (STK-04/05'te
`Kaynak → Hedef` eklendi). Transferin iki bacağı (`-10` kaynak, `+10` hedef) **ayrı hareket** olarak durur ve
hiçbir rapor bunları tek satıra indirgemiyor ✅.
➡️ **Bulgu (R-1):** Stok Hareketleri bir **ekran**, katalogda **rapor değil** → Excel'e aktarımı yok.
Depo bazlı stokta "hareket dökümü" doğal bir rapor ihtiyacıdır → `STK-10` olarak devredildi.

## 6. YETKİ / KAPSAM
Raporlar `ReportGate.ResolveCompany` + `TenantAccessGuard` ile firmaya kilitli; şube seçici yalnız
**yetkili şubeleri** listeliyor (`CanSelectBranches`). Lokasyon filtresi **aynı kapıdan** geçecek —
yeni yetki sistemi kurulmayacak. Sunucuda geçersiz/yabancı lokasyon **yok sayılmayacak**, `EnsureLocationOwned`
deseniyle **403** olacak (STK-03 standardı).

## 7. PERFORMANS
- Stok Durumu bugün **tek sorgu** (`StockTotalSubquery`) → lokasyon modunda da **tek sorgu** kalacak;
  malzeme × depo döngüsü **kurulmayacak**.
- Lokasyon adları **JOIN** ile aynı sorguda gelecek (satır başına sorgu yasak).
- Filtre listesi ekran başına **bir kez** yüklenecek (`/api/reports/scope` zaten öyle).
- **İndeks:** `ix_stock_balances_location(company_id, location_id)` Migration064'te **zaten var** → yeni indeks gerekmiyor.

## 8. UYGULAMA ADIMLARI (sıralı, atomik blok)

| # | Dosya | İş |
|---|---|---|
| 1 | `ReportCatalog.cs` | `ReportFilters.Location = 512` · `UsesLocation` · `stock` ve `stock-count` tanımlarına bayrak |
| 2 | `ReportModels.cs` | `ReportRequest.LocationIds` (yeni alan, **sona**; eski çağrılar bozulmaz) |
| 3 | `ReportService.StockStatus` | Lokasyon modu (filtre + Depo kolonu), tek sorgu, `DISTINCT` yok |
| 4 | `ReportService.StockCount` | **Depo kolonu** (`stock_documents.to_branch_id` → `branches.name`) + lokasyon filtresi |
| 5 | `Program.cs` | `ReportReqDto.LocationIds` → `ReportRequest`; `/api/reports/scope` `branches` yeniden kullanılır |
| 6 | `Reports.razor` (Web) | `UsesLocation` filtre bloğu (Tüm Şubeler / depolar / Atanmamış) + istek alanı + descriptor kaydı |
| 7 | `ReportsViewModel` + `ReportsView.axaml` | Masaüstü karşılığı — liste **yerelden** (çevrimdışı) |
| 8 | Testler | §14'teki 16 senaryo + regresyon |
| 9 | Doğrulama | build · tam takım · SQLite · izole PG · Web/Desktop parite · çevrimdışı · gerçek veri |
| 10 | Kayıt | `CURRENT_PHASE` · `MASTER_ROADMAP` · `TASK_BACKLOG` · `TEST_EVIDENCE` · QA raporu |

## 9. ⚠️ NEDEN KOD BU OTURUMDA BAŞLAMADI

Talimat madde 13: *"Çalışma oturumunun kapasitesi tüm işi doğrulamaya yetmeyecekse kodlamaya başlama;
planı ve kaldığın noktayı kontrol dosyasına kaydet."*

Bu oturumda `STK-02`, `STK-03`, `STK-04`, `STK-05` tamamlandı ve doğrulandı. STK-06 **10 adımda 7 dosya +
3 katman + 16 senaryo** gerektiriyor ve kabul ölçütü **tam doğrulama** (build + 1267 test + yeni testler +
iki lehçe + parite + çevrimdışı + gerçek veri). Kalan oturum kapasitesi bu doğrulamayı **garanti etmiyor**.

Bu proje boyunca korunan kural — *"yarım bırakılmış stok değişikliği, değerleri sessizce yanlış gösterir"* —
gereği kodlamaya **başlanmadı**. Envanter, kararlar ve adım listesi yukarıda; sonraki oturum
**doğrudan §8'den** başlayabilir.

## 10. YENİ DEVREDİLEN İŞLER
| Kod | İş | Kaynak |
|---|---|---|
| `RPR-01` | Rapor filtre UI'si iki platformda elle yazılıyor → parite testi ekle | §3 (P-1) |
| `STK-10` | "Stok Hareketleri" raporu (Excel aktarımlı, `Kaynak → Hedef` kolonlu) | §5 (R-1) |
| `STK-09` | Lokasyon bazlı dashboard (ihtiyaç doğarsa) | §2 |

**Silinmeyen açık işler:** `BKM-04` (bakım deposu) · `SNK-11` (`stock_balances` push yükü) ·
`STK-08` / **KARAR-8** (ATANMAMIŞ dağıtımı) · `STK-07` (senkron doğrulaması).
