# PROJECT STATE

**Son güncelleme:** 2026-06-26
**Aktif faz:** Faz 11 — Malzeme Talep, Onay ve PDF
**Durum:** Tamamlandı

## Tamamlanan (Faz 11)
- **Migration010**: `material_requests` (belge no TLP-YYYY-NNNN, durum), `material_request_items`, `request_status_history`.
- **Durum makinesi** (`RequestStatusMachine`): draft→pending→approved/rejected/cancelled; geçersiz/çift onay engelli; terminaller. Web parite (`requestStatus`).
- **RequestService**: oluştur (belge no tenant/yıl benzersiz), submit/approve/reject/cancel (yetki: approve butonu + requests edit; tenant fail-closed), durum geçmişi + audit.
- **Onay STOK DEĞİŞTİRMEZ**: approve sonrası bakiye aynı; stok yalnız **`CreateIssueFromRequest`** (açık, kontrollü `StockService.IssueOut`) ile düşer. Onaysız talepten çıkış reddedilir.
- **PDF**: QuestPDF (Community) `RequestPdfService` — Türkçe karakterlerle (Şçğüöı) geçerli `%PDF` üretir; 3 imza alanı.
- **Web parite**: `lib/requests/status.ts`; Drizzle 3 yeni tablo + `drizzle/0007_requests.sql`.
- **Doğrulama**: 132/132 .NET test (16 yeni) + 40 web node:test; build/lint/typecheck yeşil.

## Tamamlanan (Faz 10)
- **Migration009**: `fuel_depot_entries`, `fuel_distributions` (fiyat snapshot), `daily_activities` (hareket/transfer/bakım).
- **FuelService**: depo girişi; dağıtım atomik (IMMEDIATE) — depo bakiye kontrolü + **fiyat snapshot** + araç sayacı ileri + meter log + audit; operation_id idempotent. Depo bakiyesi = tüm girişler − tüm dağıtımlar; güncel fiyat = son giriş.
- **Günlük Faaliyet bakım = TEK kayıt**: `DailyActivityService.SaveMaintenanceActivity` ORTAK `MaintenanceService.Save`'i çağırır (tek bakım + tek stok düşümü); daily_activities yalnız REFERANS (stock_processed=1, burada stok düşmez). Aynı veri iki ekranda.
- **Hareket/transfer**: transfer aracı otomatik pasife alır (ileri-yön); hareket durumu değiştirmez. Tümü idempotent.
- **Fiyat snapshot geçmişte değişmez** (yeni depo fiyatı eski dağıtımı etkilemez) — testle kanıt.
- **Web parite**: `lib/fuel/fuel.ts` (bakiye/fiyat/maliyet/L-100km); Drizzle 3 yeni tablo + `drizzle/0006_fuel_daily.sql`.
- **Doğrulama**: 116/116 .NET test (9 yeni) + 37 web node:test; build/lint/typecheck yeşil.

## Tamamlanan (Faz 09)
- **Migration008**: `maintenance_definitions` (ana/alt + periyot km/saat/gün) + araç kapsamı, `vehicle_maintenances`, `maintenance_materials`, `vehicle_inspections` (muayene/sigorta/kasko/kalibrasyon).
- **MaintenanceService.Save (atomik)**: bakım kaydı + malzeme **tek stok düşümü** (negatif guard, fiyat snapshot) + sayaç ileri + sonraki hedef + audit; operation_id **idempotent**.
- **İptal**: malzeme stoğu **ters hareketle** geri alınır + kayıt is_cancelled (silinmez), idempotent; uyarı en-son non-cancelled kayda göre yeniden hesaplanır.
- **Uyarı eşikleri** (`AlertRules`): <%85 Normal, %85–95 Approaching, %95–100 Critical, ≥%100 Overdue. km/saat/gün ilerleme. **Yeni bakım → en-son kayıt değişir → uyarı otomatik temizlenir.**
- **InspectionService**: tarih bazlı belgeler + yaklaşan(≤30g)/geçmiş uyarı.
- **Web parite**: `lib/maintenance/alerts.ts`; Drizzle 5 yeni tablo + `drizzle/0005_maintenance.sql`.
- **Doğrulama**: 107/107 .NET test (16 yeni) + 33 web node:test; build/lint/typecheck yeşil.

## Tamamlanan (Faz 08)
- **Migration007**: araç lookups (tip/kategori/model), `vehicle_templates` + `vehicle_template_materials`, `vehicles` (iç kod/plaka/sayaç/birim/durum/şasi-motor), `vehicle_meter_logs`.
- **VehicleTemplateService**: şablon CRUD + uyumlu malzeme (tam değiştir) + **otomatik iç kod** (önek+en büyük no+1, genişlik korunur).
- **VehicleService.Create**: iç kod benzersiz; **şablondan doldurma (kullanıcı değeri öncelikli)** + şablon malzemelerini araca `material_compatible_vehicles`'a kopyalama (aynı transaction).
- **Sayaç güvenliği**: `MeterRule` — `SetMeter` geriye gidişi **MeterBackwardException** ile reddeder; `AdvanceMeter` ileri-only (geçmiş düşük okuma no-op, engellemez). **Tüm değişimler vehicle_meter_logs'a yazılır.**
- **Uyumlu malzeme detayı**: `MaterialsForVehicle` araç için malzemeleri güncel stoğuyla döndürür (çift tık detayı).
- **Web parite**: `lib/vehicles/meter.ts` (meter + applyTemplate); Drizzle 8 yeni tablo + `drizzle/0004_vehicles.sql`.
- **Doğrulama**: 91/91 .NET test (10 yeni) + 29 web node:test; build/lint/typecheck yeşil.

## Tamamlanan (Faz 07)
- **Migration006**: `stock_documents` (in/out/transfer/count, doc_no, belge alanları), `stock_movements`'a document/branch_from/is_reversed/reverses kolonları, `stock_count_lines`.
- **StockService**: giriş/çıkış/transfer/sayım; belge no otomatik (GIR/CIK/TRF/SAY-YYYY-NNNN); hareket ana kaynak, bakiye yalnız hareketle değişir.
- **Negatif stok engeli + concurrency**: IMMEDIATE transaction (`deferred:false`) ile eş zamanlı çıkış serialize; düşüşte negatif → `NegativeStockException` + rollback.
- **Idempotency**: operation_id aynıysa ikinci hareket üretilmez (mevcut belge döner). Transfer kaynak çıkış + hedef giriş tek grup/atomik.
- **Sayım**: gerekçeli fark hareketi (system snapshot + counted + diff + reason). **İptal = ters hareket** (orijinal silinmez, is_reversed=1, belge cancelled, idempotent).
- **Web parite**: `lib/stock/ledger.ts`; Drizzle `stock_documents`/`stock_count_lines` + movement kolonları + `drizzle/0003_stock_documents.sql`.
- **Doğrulama**: 81/81 .NET test (12 yeni, eş zamanlı çıkış dahil) + 26 web node:test; build/lint/typecheck yeşil.

## Tamamlanan (Faz 06)
- **Migration005**: tanımlar (material_categories+alt kategori, brands brand_type, units, suppliers), `materials` (kod benzersiz, para TEXT+currency), `material_equivalents`, `material_compatible_vehicles`, **stok defteri** (stock_movements + stock_balances), `fx_rates`.
- **LookupService**: kategori/marka/birim/tedarikçi CRUD (tenant + "definitions" yetki + audit).
- **MaterialService**: kod benzersiz (tenant), muadil **çift yönlü + döngü güvenli BFS**, uyumlu araç çoklu seçim, araç→malzeme stok gösterimi, keyset liste+arama. Para `Money` (decimal + TRY/USD/EUR).
- **OpeningStockService**: açılış stoğu **kart alanı değil 'opening' hareketi**; hareket+bakiye aynı transaction, operation_id idempotent, audit. Bakiye yalnız ledger üzerinden.
- **Web parite**: `lib/materials/{equivalents,money}.ts`; Drizzle 11 yeni tablo + `drizzle/0002_materials_ledger.sql`.
- **Doğrulama**: 69/69 .NET test (12 yeni) + 20 web node:test; build/lint/typecheck yeşil.

## Tamamlanan (Faz 05)
- **Migration004**: `personnel` (ad/unvan/telefon/şube/aktiflik + standart kolonlar) + `user_scopes` (kullanıcı şube kapsamı).
- **CompanyService**: firma oluşturma/listeleme YALNIZ Süper Admin; normal admin başka firmayı göremez/erişemez (fail-closed, `EnsureAccess`).
- **ScopeResolver**: kullanıcı şube kapsamı — açık scope öncelikli, admin → tüm firma şubeleri, admin-olmayan kapsamsız → boş; `EnsureBranchAllowed` fail-closed.
- **BranchService**: tenant + permission + kapsam; `ListInScope` kapsam dışına taşmaz; soft delete/restore; `AssignScope`.
- **PersonnelService**: CRUD + tenant + "personnel" permission + şube kapsamı; keyset liste kapsam filtreli; soft delete/restore; tüm mutasyonlar audit.
- **Web parite**: `lib/org/scope.ts` (aynı karar mantığı); Drizzle `personnel`/`user_scopes` + `drizzle/0001_personnel_scopes.sql`.
- **Doğrulama**: 57/57 .NET test (9 yeni org/personel) + 16 web node:test; build/lint/typecheck yeşil.

## Tamamlanan (Faz 04)
- **Ortak UI mantığı (web=masaüstü)**: `MenuBuilder` (yetkiye göre menü), `DateInput` (GG/AA/YYYY gerçek takvim), `NumericInput` (negatif/sınır), `MultiSelectState` (arama seçimi korur, "tümünü seç" yalnız filtre, Türkçe duyarsız), `FieldDefinition`/`FieldVisibility` (lookup/çoklu seçim/foto/"+" buton, deny-by-default).
- **Merkezi tema/branding**: `ThemeTokens`/`BrandingSettings` + `SettingKeys`; renk/marka ekranlara SABİT yazılmaz. Migration003 `app_settings` (global/firma override) + audit'li `SettingsService`.
- **Masaüstü shell**: `ShellViewModel` + yenilenen `MainWindow` (sol menü `MenuBuilder`'dan, başlık branding'den, yüklenme göstergesi, min boyut/responsive); `ThemeApplier` token'ları `Application.Resources`'a yazar (`Brand.*` DynamicResource).
- **Web**: `lib/ui/{menu,validation,multiselect,fields,modules}.ts`, `lib/theme/tokens.ts`, `globals.css` CSS değişkenleri, `layout.tsx` kök tema enjeksiyonu.
- **Doğrulama**: 48/48 .NET test (12 yeni UI) + 12 web node:test; web typecheck/lint/build + .NET build yeşil.

## Tamamlanan (Faz 03)
- **Parola hash**: PBKDF2-HMAC-SHA256 (`pbkdf2$sha256$iter$salt$hash`) — .NET `PasswordHasher` ve web `password.ts` AYNI biçim (parite testle doğrulandı).
- **Login + brute-force kilidi**: `AuthService` — 5 ardışık hatada 5 dk kilit, başarı sıfırlar; `login_attempts` + `sessions` tabloları (Migration002).
- **Rol/yetki modeli**: 6 sistem rolü seed; `AppModules` katalog + `PermissionAction` + `PermissionSet`; `AccessControl` deny-by-default (menü/buton/alan), admin bypass; `SessionContext` company_id'yi yalnız oturumdan alır.
- **Tenant + yetki yükseltme**: `TenantAccessGuard` (payload farklı firma → 403; süper admin çapraz firma), `RoleAssignmentGuard` (admin/süper-admin rolü atama koruması), `UserService.CreateUser`/`EnsureInitialAdmin`.
- **Web eşleniği**: `lib/security/{password,permissions,tenant,session}.ts`; korumalı `/api/v1/me` (oturum yoksa 401, deny-by-default).
- **Doğrulama**: 28/28 .NET test (12 yeni auth/yetki) + 5 web node:test; web typecheck/lint/build yeşil.

## Tamamlanan (Faz 02)
- **SQLite migration altyapısı**: `IMigration` + `MigrationRunner` (schema_migrations, sıfır/mevcut DB güvenli, idempotent, her migration tek transaction) + `MigrationCatalog`.
- **Migration001 çekirdek şema**: companies, branches (şube/şantiye hiyerarşi), roles, users, user_roles, user_permissions, audit_logs, file_records, sync_devices/outbox/inbox. Standart kolonlar: id, company_id, created_at/updated_at (Unix ms), version, is_deleted.
- **Ortak veri kuralları**: `TenantContext`/`TenantGuard` (fail-closed company_id), `TenantSql` (tenant+soft-delete+keyset predikatları), `Cursor` (opak keyset), `AuditWriter` (aynı transaction'da audit). Referans `BranchRepository` deseni.
- **Web/PostgreSQL eşleniği**: Drizzle `schema.ts` 12 tabloyu aynaladı; `drizzle/0000_core_schema.sql` migration offline üretildi.
- **Doğrulama**: 15/15 .NET test (8 yeni temel testi: migration zero+idempotent, tenant izolasyonu, fail-closed, soft-delete, audit, keyset, Unix ms). Web typecheck/lint/build yeşil.

## Tamamlanan (Faz 01)
- **.NET çözümü** kuruldu: `DepoWise.sln` + `src/DepoWise.{Domain,Application,Infrastructure,Desktop}` + `tests/DepoWise.Tests`. Hepsi **net8.0** (Avalonia template'in net10.0 hedefi net8.0'a çekildi).
- **Ortak sözleşmeler** (Application/Common): `ApiError` + `ErrorCodes`, keyset `PageRequest`/`PagedResult`, `IClock`/`SystemClock` + `UnixTime`, `Correlation`, `HealthResult`/`IDatabaseHealth`.
- **Yerel DB temeli** (Infrastructure/Database): `AppPaths` (mutlak `%LOCALAPPDATA%\DepoWise\Data\<env>`), `SqliteConnectionFactory` (Cache=Private, WAL, foreign_keys=ON, busy_timeout=5000), `DatabaseHealth` (write/read).
- **Masaüstü açılış health**: `DesktopBootstrap` startup'ta health çalıştırıp `%LOCALAPPDATA%\DepoWise\Logs\startup.log`'a yazar; MainWindow özeti gösterir. App base tipi `Avalonia.Application` (namespace çakışması çözüldü).
- **Web iskeleti** (`apps/web`): Next.js 15 (TS strict + noUncheckedIndexedAccess), Drizzle/postgres, fail-closed `config.ts`, `/api/v1/health` (correlation_id + 200/503), `docs/openapi.yaml` (ApiError/PagedResult/Health şemaları). Web sözleşmeleri .NET ile fonksiyonel eşit.
- **Doğrulama**: .NET build + 7 test geçti; web typecheck/lint/build geçti. `next` güvenlik açığı (CVE-2025-66478) için 15.1.6 → 15.5.19 yamalı sürüme yükseltildi.

## Açık işler
- Faz 12: Ana Ekran, Uyarılar, Raporlar ve Import/Export.
- Web PDF render hattı (model hazır; .NET QuestPDF üretiyor, web tarafı render TBD — R16).
- Şube bazlı stok (R13); vehicle FK yumuşak (R11); alert GROUP BY (R14); UI (R10); login (R8/R9); PostgreSQL (R4/R7).

## Sıradaki tek iş
- **Faz 12 — Ana Ekran, Uyarılar, Raporlar ve Import/Export** (`prompts/12_...md`). Kullanıcı komutu olmadan başlatma.

## Güvenli komutlar
- `dotnet build DepoWise.sln`
- `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj`
- `dotnet run --project src/DepoWise.Desktop` (veya `dotnet <DLL>` — EXE/BAT yok)
- Web: `cd apps/web && npm run dev | npm run build | npm run typecheck`

## Bilinen engeller
- Bkz. `KNOWN_ISSUES.md`.
