# STK-05 — Masaüstü + Çevrimdışı Lokasyon Desteği · Envanter + Plan

> Oluşturuldu: **2026-08-11** · FAZ C · Ön koşul: `STK-01…04` ✅ (`07d77e0`, `cd5c4da`, `3d1996f`)
> **Kod yazmadan önce** çıkarılan envanterdir.

---

## 1. ENVANTER — Masaüstü stok katmanları

### 1.1 En önemli yapısal bulgu
**Masaüstünde stok için AYRI bir veri katmanı YOK.** `DepoWise.Desktop` doğrudan
`DepoWise.Infrastructure`'ın `StockService` / `OpeningStockService` / `MaterialService`
sınıflarını çağırır (yerel SQLite bağlantısıyla).

➡️ **Sonuç:** STK-01/02/03'te yapılan her şey (lokasyon bazlı bakiye, `EnsureLocationOwned`,
`GetLocationBalances`, `GetCountSheet`, hareket lokasyonu) masaüstünde **zaten yürürlükte**.
Masaüstünde kendi `stock_balances` SQL'i **hiç yok** (`grep` ile doğrulandı — yalnız
`BusinessSyncPullService` yorumlarında adı geçiyor).

STK-05'in işi bu yüzden **veri katmanı değil, ARAYÜZ + eksik parametre** işidir.

### 1.2 Ekran envanteri

| Ekran (ViewModel) | Stok ilişkisi | Lokasyon durumu |
|---|---|---|
| `StockEntryViewModel` | Giriş · çıkış · transfer · ters kayıt | Lokasyon = `session.OperatingBranchId` (login şubesi) ✅ · **bakiye çipi FİRMA GENELİ** 🔴 · "Tüm Şubeler"de işlem yok |
| `StockCountViewModel` | Sayım | **`branchId` HİÇ GÖNDERİLMİYOR** 🔴 · **sistem miktarı firma geneli** 🔴 |
| `MaterialsViewModel` | Malzeme kartı + açılış stoğu | **Açılış `branchId`siz** 🔴 · kartta kırılım yok |
| `StockMovementsViewModel` | Hareket listesi | **Lokasyon kolonu yok** (servis STK-03'te hazırladı) |
| `DailyActivityViewModel` | Depo çıkışı | Login şubesi ✅ (Web ile aynı; dokunulmaz) |
| `DashboardViewModel` · `ReportsViewModel` | KPI / rapor | Ortak servis → STK-02'de düzeltildi ✅ |
| `MaintenanceViewModel` | Bakım tüketimi | `branch_id = NULL` → ATANMAMIŞ · **BKM-04'e devredildi**, dokunulmaz |
| `FuelViewModel` · `ImportExportViewModel` · `RequestsViewModel` | — | Stok lokasyonu **değil** (`branchId` başka anlamda) |

### 1.3 `branchId` anlam ayrımı (talimat madde 31)

| Anlam | Nerede (Desktop) | STK-05 kapsamı |
|---|---|---|
| **Stok lokasyonu** | `StockEntryViewModel` · `StockCountViewModel` · `MaterialsViewModel` (açılış) · `DailyActivityViewModel` | ✅ EVET |
| Oturum çalışma şubesi | `LoginViewModel`, `ShellViewModel`, `SessionContext.OperatingBranchId` | ❌ dokunulmaz |
| Kullanıcı/personel şubesi | `UsersViewModel`, `PersonnelViewModel` | ❌ |
| Makine/araç ataması | `MachineGate`, `VehiclesViewModel` | ❌ |
| Rapor filtresi | `ReportsViewModel` | ❌ (STK-06) |
| Talep sevk şubesi | `RequestOperationsViewModel` | ❌ (`RequestOperationsService` zaten doğruluyor) |

## 2. 🔴 BULGULAR — Web'dekilerin AYNISI masaüstünde de var

STK-04'te Web'de bulunan üç hatanın **üçü de** masaüstünde ayrıca duruyor
(iki istemci aynı servisi çağırıyor ama **eksik parametreyi ayrı ayrı** gönderiyor):

| # | Bulgu | Yer | Etki |
|---|---|---|---|
| D-1 | Sayım `branchId` **göndermiyor** | `StockCountViewModel:195` | Fark **ATANMAMIŞ**'a yazılır; sayılan depo hiç düzelmez |
| D-2 | Sayımda sistem miktarı **firma geneli** | `StockCountViewModel:159` | Kullanıcı **yanlış farkı** görür |
| D-3 | Açılış stoğu **`branchId`siz** | `MaterialsViewModel:539` | Her açılış **ATANMAMIŞ**'a düşer |
| D-4 | Giriş/çıkış bakiye çipi **firma geneli** | `StockEntryViewModel:244` | "15 var" der, çıkış reddedilir (o depoda 10 var) |

## 3. ÇEVRİMDIŞI AKIŞ — dokunulmayacak

```
View (XAML) → ViewModel → DesktopServices.Stock (Infrastructure) → YEREL SQLite
                                                    ↓ (internet gelince)
                         BusinessSyncPushService → /api/sync/business-push → sunucu
```
- Lokasyon listesi **yerel** `BranchService.List(session)`'dan gelir → **internet gerekmez** ✅
- Stok yazma **yerel** transaction'dır; API çağrısı **yok** ✅
- `EnsureLocationOwned` **serviste** olduğu için çevrimdışı da çalışır (STK-03 kararı) ✅
- `stock_balances` masaüstü PULL'unda **HARİÇ** (`BusinessSyncPullService:42,54,86`) → türetilmiş veri;
  tek doğruluk kaynağı `stock_movements` ✅ **korunacak**
  *(§8: push paketinde taşınıyor ama OTORİTER değil — sunucu defterden yeniden hesaplıyor.)*

## 4. YAPILACAKLAR

1. `StockCountViewModel` — **sayılan depo** (login şubesi / "Tüm Şubeler"de seçim) · sistem miktarı
   `GetCountSheet`/`GetBalanceAt` ile o depodan · `Count(..., branchId)`.
2. `MaterialsViewModel` — açılış stoğu **deposu** (`RecordOpening(..., branchId)`) + kartta **kırılım**.
3. `StockEntryViewModel` — bakiye çipi **seçili deponun** stoğu.
4. `StockMovementsViewModel` + XAML — **lokasyon kolonu** (`Kaynak → Hedef`).
5. Testler (22 senaryo) + gerçek veri + migration provası.

## 5. RİSKLER
- **Yeni ekran/alan XAML'i**: mevcut bileşenler kullanılacak, yeni tasarım sistemi kurulmayacak.
- **"Tüm Şubeler" modu**: Web'de seçiciyle açıldı; masaüstünde **aynı kural** uygulanacak mı?
  → Bu fazda **BranchGuard davranışı KORUNUYOR** (masaüstü tek-şube kullanımı için tasarlanmış;
  değiştirmek ayrı bir UX kararı). Web/Desktop **iş kuralı** aynı: lokasyon her zaman belirli.

## 6. KAPSAM DIŞI
`BKM-04` (bakım deposu) · `STK-06` (rapor lokasyon boyutu) · `STK-08` (KARAR-8) ·
masaüstünde "Tüm Şubeler" ile stok işlemi (ayrı UX kararı).

---

## 7. UYGULANDI (2026-08-11)

| Dosya | Yapılan |
|---|---|
| `StockCountViewModel` | 🔴 **D-1** `Count(..., branchId: CountLocationId)` · 🔴 **D-2** sistem miktarı `GetBalanceAt` ile **sayılan depodan** · yeni `CountLocationId` / `CountLocationName` |
| `StockCountView.axaml` | **"Sayılan Depo"** alanı (salt-okunur) · "Sistem Stoğu (bu depo)" etiketi |
| `MaterialsViewModel` | 🔴 **D-3** açılış stoğu `branchId: OperatingBranchId` · yeni `LocationBalances` (kart açılınca **tek sorgu**) |
| `MaterialsView.axaml` | Kartta **DEPO KIRILIMI** bölümü (Atanmamış en sonda) |
| `StockEntryViewModel` | 🔴 **D-4** bakiye çipi **oturumun deposunun** stoğu (`"Depo A stoğu: 10"`) |
| `StockMovementsView.axaml` | **DEPO / ŞANTİYE** kolonu (`Kaynak → Hedef`) |
| `StockService` (ortak) | `LocationFlowText` — Web ve masaüstü **aynı metni** gösterir |

### Değiştirilmeyenler (bilinçli)
`DailyActivityViewModel` (login şubesi zaten açık) · `MaintenanceViewModel` (**BKM-04**) ·
`DashboardViewModel` / `ReportsViewModel` (ortak servis STK-02'de düzeldi) ·
`BusinessSyncPushService` / `PullService` / `BusinessSyncService` (**senkron kodu değiştirilmedi**) ·
masaüstünde "Tüm Şubeler" ile stok işlemi (`BranchGuard` korundu — ayrı UX kararı).

## 8. SENKRON ANALİZİ (talimat madde 20-26) — **kod değiştirilmedi**

| Soru | Gerçek durum (kodda doğrulandı) |
|---|---|
| `stock_movements` şeması değişti mi? | **Hayır** — lokasyon zaten `branch_id`/`branch_from_id` kolonlarında |
| `stock_balances` senkronda mı? | **Evet, tablo listesinde var** (`BusinessSyncService.Tables:45`) — **ama otoriter değil**: sunucu push sonrası defterden yeniden hesaplar, masaüstü pull'u bakiyeyi **hariç tutar** (`BusinessSyncPullService:42,54,86`) |
| Bileşik PK sorun çıkarır mı? | **Hayır** — `DbIntrospect.PrimaryKey` üç kolonu okur, `ON CONFLICT` hedefi otomatik üretilir |
| Lokasyon senkronda kaybolur mu? | **Hayır** — testle kilitlendi (senaryo 11/14) |
| Yeni senkron yükü eklendi mi? | **Hayır** — lokasyon hareketin İÇİNDE taşınıyor; ek istek yok |

⚠️ **Bulgu (değiştirilmedi, kayda geçti):** `stock_balances`'ın push paketinde taşınması **gereksiz yüktür**
(sunucu zaten yeniden hesaplıyor). Zararsızdır — test, kasten bozulmuş bir bakiyenin bile defter tarafından
düzeltildiğini kanıtlıyor. Kaldırmak bir **senkron mimarisi değişikliğidir** ve bu fazın kuralı gereği
yapılmadı → `SNK-11` olarak devredildi.

## 9. DOĞRULAMALAR

| Doğrulama | Sonuç |
|---|---|
| Çözüm derlemesi | **0 hata** |
| Tam test takımı | **1267 · 1234 geçti · 0 kaldı · 33 atlandı** (taban 1254; **13 yeni senaryo**) |
| Dolu SQLite **v63 → v64** (tekrar) | 3 bakiye → 5 lokasyon satırı · toplam **8,3** korundu · ondalıklar tam · lokasyonsuz negatif ATANMAMIŞ'ta |
| Migration **rollback** kapısı (tekrar) | Uyuşmayan bakiyede **durdu**, şema 63'te kaldı, hiçbir şey yazılmadı |
| Çevrimdışı → senkron | Lokasyon **korunuyor**; sunucu kırılımı masaüstüyle **birebir aynı** |
| Online→offline→online döngüsü | **Kopya hareket yok**; yerel ve sunucu hareket sayısı eşit |
| Şirket izolasyonu | Başka firmanın deposu **çevrimdışı da** reddediliyor (403 → `ForbiddenException`) |

## 10. YENİ DEVREDİLEN İŞLER
| Kod | İş | Neden ertelendi |
|---|---|---|
| `SNK-11` | `stock_balances`'ı push paketinden çıkar (gereksiz yük) | Senkron mimarisi değişikliği — bu fazın kuralı gereği dokunulmadı |
| `BKM-04` | Bakım tüketimine depo seçimi | İş kuralı henüz tasarlanmadı; uydurulmadı |
