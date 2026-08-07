# Gelen Görev — 2026-08-07 — Rapor altyapısının standartlaştırılması (ortak mimari)

> Yalnız ORTAK MİMARİ; rapor içerikleri/hesaplamaları bu fazda DEĞİŞMEZ. Raporlar sonra tek tek (önce Araç Raporu).

## Kullanıcı revizeleri (mimariye alındı)
1. Varsayılan tarih = **Bu Ay** (30 gün değil). 2. Şube yetkisi **genel** (`btn-branch-select`, Dashboard/Analiz'de de
kullanılacak). 3. **PDF/Yazdır yok** (yalnız Excel). 4. **Sayfalama yok** (bu faz). 5. **Otomatik sorgu yok**
(yalnız Sorgula). 6. Kolon tercihleri: ileride ListPrefs ile; altyapı uygun olsun. 7. **Maks kayıt koruması**
(Ayarlar'dan değişebilir). 8. Performans öncelik. 9. Her birim sonunda rapor + commit.

## Fazlar
- **Birim 1 — Backend temel** ✅ (+kategori/Description revizesi)
- **Birim 2 — Web ekran** ✅ katalog-sürümlü, dinamik filtre, yetkiyle şube seçici, Stok Sayım paritesi, yükleniyor
- **Birim 3 — Masaüstü ekran** ✅ katalog ComboBox, dinamik tarih, yetkili şube (checkbox çoklu), Bu Ay varsayılanı, ortak Run
- **Birim 4 — Ortak tablo bileşeni** ✅ genel amaçlı (rapora özel değil); kişisel tercih (sıra/genişlik/gizli
  aktif, pinned/sort infra); kolon-altı filtre + sıralama + genişlik + gizleme; yalnız Raporlar'a uygulandı

---

## BİRİM 1 — Backend temel (2026-08-07, Opus 4.8) ✅

**Yapılanlar:**
- **Ortak metadata:** `ReportCatalog` + `ReportDescriptor` (Key/Ad/Açıklama/Grup/Filtre-bayrakları/RequiresDate/
  ExportButton) — 12 rapor tek kaynakta. `CurrentMonthRange()` (Bu Ay varsayılanı). Web/masaüstü/API buradan sürecek.
- **Maks kayıt:** `ReportLimits` (varsayılan 50.000; `reports.max_rows` Ayar anahtarı; `Resolve(settingsGet)`).
- **Genel şube yetkisi:** `SpecialButtons.BranchSelect` = `btn-branch-select` → **Yetki Ağacına OTOMATİK**
  (SpecialButtons.All). Deny-by-default; admin/süper admin bypass. Migration GEREKMEZ.
- **`ReportScope`** (Infrastructure/Reporting): şube-yetki + SQL. **NON-BREAKING:** boş seçim → mevcut oturum-şube
  davranışı; yalnız yetkili + AÇIK seçim honor edilir → **ölü şube filtresi bug'ı güvenli düzeltilir**. Yetkisiz
  kullanıcı gönderse yok sayılır (fail-closed).
- **`ReportService.Run(s, key, req, maxRows)`** — TEK giriş noktası: katalog dispatch + tarih varsayılanı
  (RequiresDate → Bu Ay, sunucu zorlar) + maks-kayıt kesme. Masaüstü + API aynı yürütmeyi kullanacak.
  5 şube-kapsamlı rapor (`BranchScope.Sql/BindBranch`) → `ReportScope`'a geçirildi (hesaplama değişmedi).
- **API:** `/api/reports/catalog` (yeni) + `BuildReport` artık `Run`'ı çağırıyor (max Ayarlar'dan) + `IsManagerReport`
  katalogdan + `/api/reports/company-filter` artık `showBranchSelect` de dönüyor.

**Değişen/eklenen dosyalar:** `ReportCatalog.cs`(+), `ReportLimits.cs`(+), `AppModules.cs`(SpecialButtons),
`ReportScope.cs`(+), `ReportService.cs`, `Program.cs`(API), `ReportArchitectureTests.cs`(+).

**Testler:** 8 yeni test (katalog bütünlüğü, şube-yetki 4 senaryo [fail-closed/honor/boş=oturum/buton-ile], maks-kayıt
kesme, tarih-varsayılanı, bilinmeyen-anahtar). Tümü + tam paket **616/0** (608→616), regresyon yok.

**Performans:** Ortak Run tek dispatch (ek sorgu yok); tarih varsayılanı sunucu-taraflı zorlanır (filtresiz büyük
tarama engellenir); maks-kayıt kesme bellek koruması. Hesaplama sorguları değişmedi (N+1'ler rapor-bazlı redesign'da).

**Commit:** `Birim 1 (rapor-mimarisi): ortak katalog + genel sube yetkisi + Run dispatch/tarih-varsayilani/maks-kayit`

---

## BİRİM 4 — Ortak tablo bileşeni (2026-08-07, Opus 4.8) ✅

> Kullanıcı 8 mimari kural verdi (genel amaçlı; kullanıcı-bazlı tercih; performans=tek yükleme; web+masaüstü
> aynı davranış; kolon-altı filtre; geleceğe hazır satır işlemleri; yalnız Raporlar'a uygula; test+commit+rapor).

**Yapılanlar:**
- **Tercih altyapısı (geleceğe hazır):** `Migration058` → `pinned_json` + `sort_json` (SQLite+PG, idempotent,
  dialect-safe). `UserListPreferenceService`: `GetPinned/SavePinned`, `GetSort/SaveSort`, ve **`GetAll` (TEK
  sorguda kolon+sayfa+genişlik+pinned+sort** — performans kuralı: ekran açılışında bir kez okunur). API:
  `/api/me/list-prefs/{listKey}` GET artık hepsini döndürür + yeni `.../sort` POST. Web ApiClient:
  `GetListPrefsFullAsync` + `SaveSortAsync`. **Aktif:** sıra/genişlik/gizli. **Altyapıda hazır (UI kapalı):**
  pinned, varsayılan sıralama.
- **Ortak çekirdek (test edilebilir):** `DepoWise.Application/Ui/GridDataView.cs` — istemci-tarafı filtre
  (Excel-benzeri: metin=içerir; sayısal=tam / `> < >= <=` / `5-10` aralık) + sıralama. Saf/deterministik.
- **Web bileşeni:** `DwDataGrid.razor` — genel amaçlı (`GridKey`+`Columns`+`Rows`), mevcut `dw-grid` tasarımı
  (kolon-altı filtre satırı, ⠿ sürükle-taşı, başlık-tık sırala, CSS genişlik + "Genişlikleri kaydet", kolon
  seçici). Filtre/sıralama tarayıcıda. → `Reports.razor` tabloyu bununla değiştirdi (GridKey=`reports:{key}`).
- **Masaüstü bileşeni:** `GridController` (beyin: kolon/satır VM'leri, filtre/sıralama `GridDataView`'e delege,
  tercih kancaları) + `DataGridView.axaml` kontrolü (dinamik kolonlar; header-tık sırala; Thumb sürükle-genişlik;
  "Kolonlar" menüsü=görünürlük+sıra; kolon-altı filtre). Komutlar KOLON üzerinde → popup/item-template binding
  güvenli. → `ReportsView` eski tabloyu `DataGridView`+`Grid` ile değiştirdi.
- **Kapsam:** yalnız Raporlar ekranı geçirildi; Malzeme/Araç/Günlük vb. **dokunulmadı** (kural 7). Bileşen
  ileride satır işlemleri (sağ tık/toplu seçim/renk) için esnek (RowVm/CellVm ayrı) — şimdi eklenmedi (kural 6).

**Değişen/eklenen dosyalar:** `Migration058...cs`(+), `MigrationCatalog.cs`, `UserListPreferenceService.cs`,
`Program.cs`(API GET+sort POST+DTO), `ApiClient.cs`(web), `GridDataView.cs`(+), `DwDataGrid.razor`(+),
`Reports.razor`(web), `GridController.cs`(+), `DataGridView.axaml`(+)/`.cs`(+), `ReportsView.axaml`, `ReportsViewModel.cs`,
`UserListPreferenceTests.cs`(+5), `GridDataViewTests.cs`(+12).

**Testler:** +17 (5 tercih: pinned/sort/GetAll round-trip+kişisel izolasyon; 12 grid davranış: metin/sayısal
filtre, karşılaştırma/aralık, boş-hücre eleme, çoklu-filtre VE, sıralama artan/azalan, Match theory). Tam
paket **633/0** (616→633, 11 PG atlandı), regresyon yok.

**Performans:** Tercih ekran açılışında **tek sorgu** (`GetAll`), değişince yalnız ilgili alan yazılır.
Filtre/sıralama **istemcide** (sunucuya tekrar sorgu YOK) → rapor gibi hazır sonuçta ideal, VPS'e ek yük yok.

**Görsel doğrulama:** Web build + davranış testleri yeşil; masaüstü Avalonia bu ortamda önizlenemez →
binding/davranış incelendi, görsel doğrulama **1.0.112'de kullanıcıyla** yapılacak (kullanıcı notu).

**Commit:** `Birim 4 (rapor-mimarisi): genel amacli ortak tablo bileseni + kullanici kolon tercihleri (web+masaustu)`
