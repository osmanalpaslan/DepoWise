# ARA İŞ — RAPOR GELİŞTİRMELERİ · 01 UYGULAMA (2026-08-29, ADR-181) — ✅ KOD+TEST TAMAM · ⛔ YAYIN ONAYI BEKLİYOR

> Kararlar: **PK-R1=A · PK-R2=A · PK-R3=A · PK-R4=B** (kullanıcı, 2026-08-29).
> Analiz: [RAPOR_ARA_IS_00_ANALIZ.md](RAPOR_ARA_IS_00_ANALIZ.md). Production'a HİÇBİR aşamada bağlanılmadı.

## 1. Ön koşul — FIN-B1/Migration082 ayrıştırması (ADR-180, commit `ae08d0e`)
PK-R4=B gereği FIN-B1 çifti (Migration082 + 8 servis firma-süzgeci + yeni-sözleşme testleri FIN1–FIN7 +
`PostgresMigration082Tests`) master'dan BİREBİR geri çekildi; eski sessiz-atlama sözleşmesi FIN5 ile
yeniden kilitli; FIN8/FIN9/FIN10 (STK-B2·SNK-05) ve YET-01 KORUNDU. **Migration katalog azamisi yeniden
81 = canlı şema → deploy'da migration çalıştırıcısı NO-OP.** Tasarım `35d7bce`'de, AYRI ONAY BEKLİYOR;
FIN-B1 tamamlanmış SAYILMAZ.

## 2. Özellik 1 — "Araç Raporu — Günlük" (`vehicle-daily`, PK-R1=A)
- Katalog satırı + `ReportService.VehicleDailyReport` + dispatch satırı — **mevcut `vehicle` raporuna
  TEK SATIR dokunulmadı** (SQL/kolon/toplam aynen; tutarlılık testle kilitli).
- Gün anahtarı `tarih_ms / 86400000` tam sayı bölmesi → SQLite=PostgreSQL birebir; RPR-06 UTC gün
  sınırı (00:00:00.000–23:59:59.999, iki uç dahil) aynen.
- Sabit 5 sorgu (araçlar + 3 gün-gruplu toplam + gün-içi-son-sayaç ham fişleri) + bellekte birleştirme —
  **gün başına sorgu YOK, N+1 YOK**. Boş günler 0 ("-") satırıyla; satır=gün×araç; maxRows üretimde korur;
  TOPLAM satırı tüm dönemden. 16 kolon (+Tarih, +Gün İçi Son Sayaç); oranlar günün değerlerinden.
- İki platform otomatik: aynı katalog + aynı `Run` (masaüstü çevrimdışı yerel SQLite'ta da çalışır);
  Excel mevcut mekanizmadan (aynı TableModel). Yeni ekran/menü/DTO alanı YOK. **MIGRATION YOK.**

## 3. Özellik 2 — Rapor türü KATEGORİ yetkileri (PK-R2=A · PK-R3=A)
- `AppModules.All`'a 8 anahtar (`reports`'un hemen altında): report_vehicle · report_stock · report_fuel ·
  report_maintenance · report_requests · report_management · report_material · report_accounting.
- Eşleme TEK merkez: `ReportCatalog.CategoryModule(Category)` — üç kapı aynı eşlemeyi kullanır:
  (1) API katalog süzmesi (`/api/reports/catalog`), (2) masaüstü katalog süzmesi (`ReportsViewModel`),
  (3) **ortak servis kapısı `ReportService.Run`** (masaüstü+API+export hepsi buradan) → tür adı
  değiştirerek atlatma İMKÂNSIZ. `reports` ÜST KAPI olarak KALDI; tenant/BranchAccess/manager/
  RequiredModule/DataModule/export butonları AYNEN (hiçbiri gevşetilmedi). **MIGRATION YOK** (katalog
  kod-tabanlı; ceiling/şablon/rol-blok/firma paketi otomatik besleniyor).
- PK-R3=A: yeni anahtarlar herkese KAPALI başlar (deny-by-default); admin/firma admini bypass ile görür
  (canlı geçişin dayanağı — testle kilitli). Yayın sonrası kategoriler Yetkiler ekranından elle atanır.
- **Bilinçli sözleşme güncellemesi (kullanıcı onayı):** RPR15d "yalnız reports yeter" →
  "reports + kategori yeter" olarak yeniden kilitlendi (gevşetme değil, yeni kuralın kanıtı).

## 4. Test sonuçları (hepsi izole — temp SQLite + yerel test PG 127.0.0.1:5544)
- Yeni: `VehicleDailyReportTests` **16/16** (tek/çok gün · uç günler dahil · boş gün 0 · oranlar günlükten ·
  **günlük≡dönem birebir tutarlılık (TOPLAM satırları dahil)** · araç filtresi · saat-bazlı · tenant ·
  BranchAccess · soft-delete · yetkisiz/kategori-siz/yetkili · sıralama · katalog tanımı) ·
  `ReportTypePermissionTests` **17/17** (eşleme bütünlüğü · çift kapı matrisi · çapraz sızma yok ·
  üst kapı korunur · admin bypass · platform parite kaynak kilidi) · `ApiReportScopeTests` R30/R31
  (HTTP: endpoint+export kategori kapısı · katalog süzmesi) · `PostgresVehicleDailyReportTests` (PG
  gün bölmesi/sınır/boş gün/tutarlılık/çift kapı).
- Sözleşme-geçiş güncellemeleri (kapı gevşetilmedi; kategori atandı): IslemTarihi · AccountingReport ×2 ·
  RaporKapaliModulBypass (RPR15d yeni ad) · ApiReportScope kurulumları · BranchIsolationMatrix ·
  ScreenTreeParity A10 menusuz (+8) · ReportArchitecture sayaç 21→22.
- **İzole PostgreSQL süiti: 46/46** (çift kilit env aynen; −Migration082 testi, +günlük parite testi).
- **TAM SÜİT: 2.931 → 2.893 geçti / 0 başarısız / 38 bilinçli-atlanan** (PG sınıfları — ayrı koşuda kapsandı).
- Release build: API + Web + Masaüstü **3 × 0 hata**.

## 5. Değişen dosyalar
Kod (6): AppModules.cs · ReportCatalog.cs · ReportService.cs · Program.cs (katalog süzmesi) ·
ReportsViewModel.cs (katalog süzmesi) — UI dosyalarına dokunulmadı (katalogdan beslenirler).
Revert (ADR-180): 8 servis + MigrationCatalog + Migration082 silindi + FinalStabilizasyonTests + PG082
testi silindi. Test (yeni 3 + güncellenen 10). Belgeler: bu dosya + 00-ANALIZ + KNOWN_ISSUES +
DECISIONS (ADR-180/181) + CURRENT_PHASE + MASTER_ROADMAP + FINAL_* düzeltmeleri.

## 6. Yayın planı (onay sonrası — "YAYINLA" denince)
1) Canlı salt-okunur sağlık ön-kontrolü (health/sürümler; **SELECT'siz** uçlar) → 2) deploy: API
(fly.toml) + Web (fly.web.toml) + masaüstü sürüm paketi → 3) migration çalışmadığının kanıtı: canlı
şema **81 KALIR** (schema_migrations max) → 4) yayın sonrası salt-okunur kontroller: health · rapor
kataloğu · günlük+toplam araç raporu · rapor yetkileri (kategori-siz kullanıcı boş liste/403) · tenant ·
BranchAccess · export · mevcut raporlar → 5) **KULLANICI İŞİ:** kategorileri Yetkiler ekranından
rollere/kullanıcılara atamak (admin olmayan rapor kullanıcıları atanana dek listeyi boş görür —
PK-R3=A kabul edilmiş davranış) → 6) masaüstü istemciler ≤60 sn'de güncelleme uyarısı alır.
Geri dönüş: önceki API/Web imajına dönüş yeterli (migration yok → şema geri alma GEREKMEZ).

## 7. Ana plana dönüş (yayın sonrası işaretlenecek)
"RAPOR ARA İŞİ — GÜNLÜK ARAÇ RAPORU + RAPOR TÜRÜ YETKİLERİ — YAYINLANDI" → SONRAKİ ANA İŞ:
**AŞAMA 3 — FINAL KARAR PAKETİ** (1. FIN-B1/Migration082 **ayrı onay** · 2. YET-01 ✅ · 3. ARC-01a ✅ ·
4. STK-B2 ✅ · 5. RPR-02 ✅ · 6. SNK-05a ✅ · 7. MAK-01/b ✅ — FIN-B1 dışındakiler ADR-179'da kapandı ve
korunuyor). SNK-13 / M import / K / L / O / Excel Merkezi davranışları DEĞİŞMEDİ.
