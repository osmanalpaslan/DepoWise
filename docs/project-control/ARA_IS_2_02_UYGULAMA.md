# ARA İŞ 2 — PAKET-1 UYGULAMA KAYDI (2026-08-29, ADR-182) — 🔨 SÜRÜYOR

> Plan: [ARA_IS_2_01_PLAN.md](ARA_IS_2_01_PLAN.md) · Analiz: [ARA_IS_2_00_ANALIZ.md](ARA_IS_2_00_ANALIZ.md)
> Kararlar: PK-F1=A·F2·F3·F4=A·F5=A · T1=A·T2·T3=A·T4=A · V1=A · G1=A·G2=A · D1=A.
> **Production'a HİÇBİR aşamada bağlanılmadı · MIGRATION YOK (katalog azamisi 81 = canlı şema).**

| Adım | Konu | Durum |
|---|---|---|
| S1 | Yakıt: tarih yazım hatası + rapor kapsam sözleşmesi | ✅ **TAMAM** |
| S2 | "Yakıtı Veren" son seçim | ⏳ sırada |
| S3 | Yakıt-Günlük + Stok Hareketleri-Günlük | ⏳ |
| S4 | Günlük Faaliyet — Detay | ⏳ |
| S5 | Fotoğraf sunucu-otoriteli + silme kapısı | ⏳ |
| S6 | Kapanış doğrulaması + yayın öncesi rapor | ⏳ |

---

## S1 — YAKIT (✅ tamamlandı)

### S1a · Masaüstü tarih yazım hatası (PK-T2)
**Kök neden:** `FuelViewModel` seçilen günü HAM `DateTimeOffset.ToUnixTimeMilliseconds()` ile
gönderiyordu. Avalonia DatePicker günü YEREL ofsetle verir (TR +03:00) → kullanıcının seçtiği
**2 Ağustos, veritabanına 1 Ağustos 21:00 UTC** yazılıyordu; fiş tarih-filtreli tüm raporlarda
(fuel · vehicle · vehicle-daily · fuel-depot) **bir gün erken** görünüyordu. Web (`Fuel.razor`) bu
hatayı taşımıyordu (doğrulandı — web'e DOKUNULMADI).

**Düzeltme:** yeni `FuelViewModel.IsGunuMs(DateTimeOffset?)` — seçilen günün **UTC 00:00**'ı.
Kural, rapor tarih sınırı (`ReportDateRange`) ve web (`FieldChecks.ToUnixMs`) ile birebir aynıdır;
masaüstünün Duyuru/Zimmet/Takvim/Evrak/Proje/Satın Alma/İş Emri ekranlarının zaten kullandığı desen.
İki çağrı noktası: dağıtım `DistributionDate`, depo girişi `EntryDate`.
**PK-T3=A:** mevcut canlı kayıtlara DOKUNULMADI (eski fişler bir gün erken görünmeye devam eder —
bilinçli kabul; istenirse ileride ayrı onaylı düzeltme işi).

### S1b · Rapor kapsam sözleşmesi (PK-T1=A)
`FuelConsumption` içindeki türetilmiş yakıt tablosuna **INNER JOIN** (eskiden LEFT) → **yalnız seçilen
aralıkta yakıt fişi olan araçlar** listelenir. InfoNote kullanıcıya yeni davranışı ve "tüm filoyu görmek
için Araç Raporu / Araç Raporu — Günlük" yönlendirmesini söyler. **Yalnız bu rapor değişti.**
Sözleşme değişikliği testte açıkça belgelendi: eski kilit `YakitAlmayanArac_TamFilo_...` →
yeni kilit `YakitAlmayanArac_ARTIK_Listelenmez` (gevşetme DEĞİL — yeni kuralın kanıtı).

### S1d · Diğer masaüstü tarih alanları — SALT TARAMA (PK-T4=A, düzeltme YAPILMADI)
Aynı hata sınıfı (yerel ofsetli `DateTimeOffset` → ham ms) **10 ekranda / 17 yazım noktasında** daha var:

| Şiddet | Ekran (dosya:satır) | Alan |
|---|---|---|
| 🔴 **Her seferinde 1 gün kayar** (`new DateTimeOffset(DateTime.Today)`) | StockEntryViewModel:422,457,470 · StockCountViewModel:235 · StockDistributeViewModel:171 | stok belge tarihi (`docDate`) |
| 🟠 **Kullanıcı gün seçince kayar** (picker yerel gece yarısı verir) | MaintenanceViewModel:625 · InspectionViewModel:144,147 · InvoicesViewModel:344,345 · PartiesViewModel:356,358 · PaymentsViewModel:305 · FinanceViewModel:328,383 · DailyActivityViewModel:530,560,583 · RequestsViewModel:352 | bakım/muayene/fatura/cari/ödeme/finans/faaliyet/talep iş günü |

**DOĞRU desen zaten kullanan ekranlar** (referans): Duyurular · Zimmet · Takvim · Evrak · Proje ·
Satın Alma · İş Emri · Maliyet Merkezi (+ okuma filtreleri: Sistem Logu · Stok Değişiklik · Stok Hareketleri).
➡️ **Karar kullanıcıya bırakıldı** (kapsam dışı; bu pakette düzeltilmedi). Düzeltilirse davranış
değişikliği olacağından ayrı iş + ayrı test turu gerektirir.

### Testler (S1)
- **YENİ** `YakitTarihGunTests` **11 test**: UTC gün başı kuralı 4 saat diliminde (rapor filtresiyle
  parite) · eski ham dönüşümün bir gün erkene düştüğünün belgesi · **kaynak-düzeyi kilit** (FuelViewModel
  ham dönüşüme geri dönemez) · uçtan uca yazım→rapor (2 Ağustos'ta VAR, 1 Ağustos'ta YOK) ·
  **1 Ağustos ≠ 2 Ağustos araç listesi** (kullanıcının senaryosu) · gün sınırı iki uç dahil
  (00:00:00.000 + 23:59:59.999) · aralık dışı fişi olan araç listelenmez · hiç fişi olmayan listelenmez ·
  **REGRESYON: `vehicle` tam filo KORUNDU · `vehicle-daily` tam filo × tüm günler KORUNDU**.
- **GÜNCELLENEN** `FuelConsumptionTests`: satır sayısı 4→3 + tam-filo kilidi yeni sözleşmeye çevrildi.
- Koşu: hedefli aile **84/85** (1 atlanan = PG sınıfı) → geniş regresyon (Report/Rapor/Excel/Vehicle/
  Fuel/Yakit/BranchIsolation) **679 geçti / 0 başarısız / 2 atlanan**.

### Değişen dosyalar (S1)
`src/DepoWise.Desktop/ViewModels/FuelViewModel.cs` · `src/DepoWise.Infrastructure/Reporting/ReportService.cs` ·
`src/DepoWise.Application/Reports/ReportCatalog.cs` · `tests/DepoWise.Tests/FuelConsumptionTests.cs` ·
**yeni** `tests/DepoWise.Tests/YakitTarihGunTests.cs`. **Web'e dokunulmadı** (hata web'de yoktu).
