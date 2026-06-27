# REQUIREMENTS TRACEABILITY

| ID | Gereksinim | Faz | Durum | Kod/Test Kanıtı |
|---|---|---:|---|---|
| REQ-MOD-01 | Ana Ekran ve Uyarı Merkezi | 12 | Bekliyor |  |
| REQ-MOD-02 | Firma, Şube ve Şantiye | 05 | Bekliyor |  |
| REQ-MOD-03 | Kullanıcı, Rol ve Yetki | 03 | Bekliyor |  |
| REQ-MOD-04 | Tanımlar ve Alan Ayarları | 04 | Bekliyor |  |
| REQ-MOD-05 | Malzemeler | 06 | Bekliyor |  |
| REQ-MOD-06 | Stok İşlemleri | 07 | Bekliyor |  |
| REQ-MOD-07 | Araçlar ve Araç Şablonları | 08 | Bekliyor |  |
| REQ-MOD-08 | Bakım Takibi | 09 | Bekliyor |  |
| REQ-MOD-09 | Muayene, Sigorta ve Kalibrasyon | 09 | Bekliyor |  |
| REQ-MOD-10 | Yakıt Sarfiyatı | 10 | Bekliyor |  |
| REQ-MOD-11 | Günlük Faaliyet | 10 | Bekliyor |  |
| REQ-MOD-12 | Malzeme Talep ve Onay | 11 | Bekliyor |  |
| REQ-MOD-13 | Personel | 05 | Bekliyor |  |
| REQ-MOD-14 | Raporlar | 12 | Bekliyor |  |
| REQ-MOD-15 | Import/Export | 12 | Bekliyor |  |
| REQ-MOD-16 | Dosya ve Fotoğraf | 13 | Bekliyor |  |
| REQ-MOD-17 | Sistem Logu, Audit ve Çöp Kutusu | 13 | Bekliyor |  |
| REQ-MOD-18 | Yedekleme | 13 | Bekliyor |  |
| REQ-MOD-19 | Setup ve Güncelleme | 15 | Bekliyor |  |
| REQ-MOD-20 | Offline Senkronizasyon | 14 | Bekliyor |  |

**Faz 00 (2026-06-26):** REQ-MOD-01..20 → faz eşlemesi V6 analiz §12 ile doğrulandı; eksik/çelişkili gereksinim bulunmadı. Tüm satırlar "Bekliyor" (kod henüz yok). Kanıt sütunları ilgili faz tamamlandıkça doldurulacak.

**Faz 16 (2026-06-27):** Güvenlik sertleştirme (çapraz-kesen; REQ-MOD-03/20 + analiz §9/§11 kabul kriterleri):
- `src/DepoWise.Application/Security/{LogRedactor,RateLimiter}.cs`, `Infrastructure/Sync/EnrollmentService.cs` (RotateDeviceToken); web `apps/web/src/lib/security/{headers,ratelimit,csrf,redact}.ts`, `next.config.mjs`, `docs/SECURITY.md`.
- Testler: `tests/DepoWise.Tests/SecurityHardeningTests.cs`, `apps/web/tests/security-hardening.test.ts`.

**Faz 15 (2026-06-27):** REQ-MOD-19 (Setup ve Güncelleme) çekirdeği tamamlandı (transport/UI R21):
- `src/DepoWise.Infrastructure/Update/{ReleaseService,UpdateService}.cs`, `Application/Update/UpdateModels.cs`, Migration012; web `apps/web/src/lib/update/update.ts`, Drizzle `0008_app_releases.sql`.
- COMODO: `comodo_guard.ps1` hook + `DesktopBootstrap`/`AppPaths` + Directory.Build.props (UseAppHost=false). Testler: `tests/DepoWise.Tests/UpdateComodoTests.cs`, `apps/web/tests/update.test.ts`.

**Faz 14 (2026-06-27):** REQ-MOD-20 (Offline Senkronizasyon) çekirdeği tamamlandı (transport/UI R19, apply R20):
- `src/DepoWise.Infrastructure/Sync/{EnrollmentService,OutboxWriter,SyncServer,SyncCrypto}.cs`, `Application/Sync/SyncModels.cs`, Migration011; web `apps/web/src/lib/sync/sync.ts`.
- Testler: `tests/DepoWise.Tests/SyncTests.cs`, `apps/web/tests/sync.test.ts`.

**Faz 13 (2026-06-27):** REQ-MOD-16 (Dosya/Fotoğraf) + REQ-MOD-17 (Audit/Çöp Kutusu) + REQ-MOD-18 (Yedekleme) tamamlandı (servis + iş kuralı; UI bağlama R10):
- `src/DepoWise.Infrastructure/Files/{FileService,LocalFileStorageProvider,TrashService,BackupService}.cs`, `Application/Files/{FileValidation,IFileStorageProvider}.cs`; web `apps/web/src/lib/files/validation.ts`.
- Audit: tüm mutasyonlar `AuditWriter` (before/after/actor/correlation/time). Testler: `tests/DepoWise.Tests/FileTrashBackupTests.cs`, `apps/web/tests/files.test.ts`.

**Faz 12 (2026-06-27):** REQ-MOD-01 (Ana Ekran/Uyarı) + REQ-MOD-14 (Raporlar) + REQ-MOD-15 (Import/Export) tamamlandı (servis + iş kuralı; UI bağlama R10):
- `src/DepoWise.Infrastructure/Reporting/{DashboardService,ReportService,ExcelExportService,MaterialImportService}.cs`, `Application/Reports/{ReportModels,ImportModels}.cs`; web `apps/web/src/lib/reports/{gate,import}.ts`.
- Testler: `tests/DepoWise.Tests/ReportingTests.cs`, `apps/web/tests/reporting.test.ts`.

**Faz 11 (2026-06-27):** REQ-MOD-12 (Malzeme Talep ve Onay) tamamlandı (servis + iş kuralı + PDF; UI bağlama R10):
- `src/DepoWise.Infrastructure/Requests/{RequestService,RequestPdfService}.cs`, `Application/Requests/{RequestStatus,RequestPdfModel}.cs`, Migration010; web `apps/web/src/lib/requests/status.ts`, Drizzle `0007_requests.sql`.
- Testler: `tests/DepoWise.Tests/RequestTests.cs`, `apps/web/tests/requests.test.ts`.

**Faz 10 (2026-06-27):** REQ-MOD-10 (Yakıt Sarfiyatı) + REQ-MOD-11 (Günlük Faaliyet) tamamlandı (servis + iş kuralı; UI bağlama R10):
- `src/DepoWise.Infrastructure/Operations/{FuelService,DailyActivityService}.cs`, Migration009; web `apps/web/src/lib/fuel/fuel.ts`, Drizzle `0006_fuel_daily.sql`.
- Testler: `tests/DepoWise.Tests/FuelDailyActivityTests.cs`, `apps/web/tests/fuel.test.ts`.

**Faz 09 (2026-06-27):** REQ-MOD-08 (Bakım Takibi) + REQ-MOD-09 (Muayene/Sigorta/Kalibrasyon) tamamlandı (servis + iş kuralı; UI bağlama R10):
- `src/DepoWise.Infrastructure/Maintenance/{MaintenanceDefinitionService,MaintenanceService,InspectionService}.cs`, `Application/Maintenance/AlertRules.cs`, Migration008; web `apps/web/src/lib/maintenance/alerts.ts`, Drizzle `0005_maintenance.sql`.
- Testler: `tests/DepoWise.Tests/MaintenanceTests.cs`, `apps/web/tests/maintenance.test.ts`.

**Faz 08 (2026-06-27):** REQ-MOD-07 (Araçlar ve Araç Şablonları) tamamlandı (servis + iş kuralı; UI bağlama R10):
- `src/DepoWise.Infrastructure/Vehicles/{VehicleService,VehicleTemplateService}.cs`, `Application/Common/Meter.cs`, Migration007; web `apps/web/src/lib/vehicles/meter.ts`, Drizzle `0004_vehicles.sql`.
- Sayaç geriye gitmeme + log: vehicle_meter_logs. Testler: `tests/DepoWise.Tests/VehicleTests.cs`, `apps/web/tests/vehicles.test.ts`.

**Faz 07 (2026-06-27):** REQ-MOD-06 (Stok İşlemleri) tamamlandı (servis + iş kuralı; UI bağlama R10):
- `src/DepoWise.Infrastructure/Materials/StockService.cs`, Migration006 (stock_documents, movement kolonları, stock_count_lines); web `apps/web/src/lib/stock/ledger.ts`, Drizzle `0003_stock_documents.sql`.
- Testler: `tests/DepoWise.Tests/StockOperationTests.cs` (negatif/concurrency/idempotency/ters kayıt), `apps/web/tests/stock.test.ts`.

**Faz 06 (2026-06-27):** REQ-MOD-05 (Malzemeler) + REQ-MOD-04 (Tanımlar) + REQ-MOD-06 ön koşulu (stok defteri):
- `src/DepoWise.Infrastructure/Materials/{LookupService,MaterialService,OpeningStockService}.cs`, `Application/Common/Money.cs`, Migration005; web `apps/web/src/lib/materials/*`, Drizzle `0002_materials_ledger.sql`.
- Açılış stoğu ledger (REQ-MOD-06 temeli): stock_movements/stock_balances.
- Testler: `tests/DepoWise.Tests/MaterialTests.cs`, `apps/web/tests/materials.test.ts`.

**Faz 05 (2026-06-27):** REQ-MOD-02 (Firma/Şube/Şantiye) + REQ-MOD-13 (Personel) iş kuralı çekirdeği (UI bağlama R10):
- `src/DepoWise.Infrastructure/Org/{CompanyService,BranchService,PersonnelService,ScopeResolver}.cs`, Migration004 (personnel + user_scopes); web `apps/web/src/lib/org/scope.ts`, Drizzle `personnel`/`user_scopes` + `drizzle/0001_personnel_scopes.sql`.
- Testler: `tests/DepoWise.Tests/OrgPersonnelTests.cs`, `apps/web/tests/org.test.ts`.

**Faz 04 (2026-06-27):** REQ-MOD-04 (Tanımlar/Alan Ayarları) çekirdeği + tüm modüllerin ortak UI altyapısı:
- Ortak UI: `src/DepoWise.Application/Ui/*` (Menu, Validation, MultiSelectState, FieldDefinition); web `apps/web/src/lib/ui/*`.
- Tema/branding: `src/DepoWise.Application/Theming/Branding.cs`, `Infrastructure/Settings/SettingsService.cs`, Migration003; web `apps/web/src/lib/theme/tokens.ts` + `globals.css`. Masaüstü shell: `Desktop/ViewModels/ShellViewModel.cs`, `Theming/ThemeApplier.cs`, `Views/MainWindow.axaml`.
- Testler: `tests/DepoWise.Tests/UiCommonTests.cs`, `apps/web/tests/ui.test.ts`.

**Faz 03 (2026-06-26):** REQ-MOD-03 (Kullanıcı/Rol/Yetki) çekirdeği — UI bağlama Faz 04/05:
- Auth + kilit + parola: `src/DepoWise.Infrastructure/Security/{PasswordHasher,AuthService,UserService}.cs`, Migration002 (login_attempts/sessions + rol seed); web `apps/web/src/lib/security/*`.
- Deny-by-default + tenant + yetki yükseltme: `src/DepoWise.Application/Security/{AppModules,Permissions,SessionContext,AccessControl,RoleAssignmentGuard}.cs`.
- Testler: `tests/DepoWise.Tests/AuthPermissionTests.cs`, `apps/web/tests/security.test.ts`, `apps/web/src/app/api/v1/me/route.ts`.

**Faz 02 (2026-06-26):** Veri temeli — tüm operasyonel modüllerin (REQ-MOD-02/03/04/16/17/18/20) ön koşulu kuruldu:
- Çekirdek şema + migration: `src/DepoWise.Infrastructure/Database/Migrations/*` (companies, branches, users, roles, permissions, audit_logs, file_records, sync_*); PG: `apps/web/src/db/schema.ts` + `apps/web/drizzle/0000_core_schema.sql`.
- Tenant/soft-delete/keyset/audit kuralları: `src/DepoWise.Application/Common/{Tenant,Cursor,Audit}.cs`, `Infrastructure/Database/{TenantSql,AuditWriter,BranchRepository}.cs`; test `tests/DepoWise.Tests/DatabaseFoundationTests.cs`.
- REQ-MOD-17 (audit/çöp kutusu ön koşulu): audit_logs + soft-delete altyapısı hazır (UI Faz 13).

**Faz 01 (2026-06-26):** Modül gereksinimi tamamlanmadı; iskelet + ortak sözleşmeler kuruldu. Tüm modülleri besleyen ortak altyapı kanıtları:
- Hata modeli / pagination / zaman / correlation: `src/DepoWise.Application/Common/*`, `apps/web/src/lib/contracts.ts`, `apps/web/docs/openapi.yaml`; test `tests/DepoWise.Tests/SkeletonSmokeTests.cs`.
- Yerel DB temeli (REQ-MOD-18/20 ön koşulu): `src/DepoWise.Infrastructure/Database/*`.
- Fail-closed config (REQ-MOD-03 ön koşulu): `apps/web/src/lib/config.ts`, `/api/v1/health`.

Her faz sonunda ilgili satırlar güncellenir.
