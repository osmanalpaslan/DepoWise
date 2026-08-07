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
- Birim 4 — Ortak sonuç tablosu bileşeni (kolon-altı filtre/sıralama/genişlik) + kolon tercihleri (ListPrefs) — SIRADA

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
