# PROJECT STATE

**Son güncelleme:** 2026-06-26
**Aktif faz:** Faz 17 — Uçtan Uca Doğrulama, Dokümantasyon ve Yayın Adayı
**Durum:** Tamamlandı — **Backend/iş mantığı YAYIN ADAYI (1.0.0-rc)**; genel kullanıcı yayını için UI/entegrasyon engelleri açık (R10, R8/R9, R4/R7).

## Tamamlanan (Faz 17)
- **Uçtan uca entegrasyon testi**: temiz DB'de çapraz-modül tam akış (malzeme/stok → araç/bakım/uyarı → talep onay-stok-değişmez → kontrollü çıkış → sync idempotent → yedek/geri yükleme) + tenant izolasyonu (`EndToEndTests`).
- **Temiz koşu**: .NET çözüm build (0 hata) + **187/187 test**; Release publish (exit 0); web typecheck/lint/build + **66/66 test**; repo secret tarama temiz; npm audit raporlandı.
- **Release candidate**: 1.0.0-rc publish + SHA-256 checksum (`docs/RELEASE_CANDIDATE.md`).
- **Dokümantasyon**: `USER_GUIDE.md` (kurulum/enrollment/günlük kullanım/yedek/güncelleme), `OPERATIONS.md` (prod checklist/migration-rollback/monitoring/acil durum), `SECURITY.md`.
- **İzlenebilirlik**: REQ-MOD-01..20 her biri kod/test/kanıt yoluyla kapatıldı veya **açık risk** olarak işaretlendi (test edilmeyen UI tamamlandı sayılmadı).
- **Doğrulama**: tüm build/test exit 0; kanıtlar `TEST_EVIDENCE.md`'de tekrar üretilebilir.

## Tamamlanan (Faz 16)
- **Web güvenlik başlıkları**: CSP, X-Content-Type-Options=nosniff, X-Frame-Options=DENY + frame-ancestors 'none', Referrer-Policy, Permissions-Policy; HSTS yalnız Production (`next.config.mjs` + `lib/security/headers.ts`).
- **Rate limit** (`RateLimiter` / `ratelimit.ts`): login 5/5dk, sync push 60/dk, admin 30/dk, anahtar-bazlı izole, fail-closed (masaüstü login ayrıca 5-hata/5dk kilit).
- **CSRF** (`csrf.ts`): double-submit token, sabit-zaman doğrulama, fail-closed.
- **Log redaction / PII'siz** (`LogRedactor` / `redact.ts`): password/token/secret/authorization/connection-string/session/Bearer maskelenir.
- **Cihaz token rotasyonu** (`RotateDeviceToken`): eski token anında geçersiz; revoke cascade push/pull 403.
- **Secret yönetimi + runbook** (`docs/SECURITY.md`): sırlar koda yazılmaz (.env gitignore, fail-closed config), rotasyon prosedürü; repo secret taraması temiz; dependency audit raporlandı (R23).
- **Doğrulama**: 186/186 .NET test (7 yeni) + 66 web node:test; build/lint/typecheck yeşil.

## Tamamlanan (Faz 15)
- **SemVer + UpdatePackage** (ortak): X.Y.Z parse/karşılaştırma; geçersiz sürüm reddi.
- **Migration012 + ReleaseService**: `app_releases` (version benzersiz, checksum, min_supported, signed); yayın **yalnız Süper Admin**; checksum (64 hex) doğrulama; `Latest()` en yüksek SemVer.
- **UpdateService** (masaüstü updater): `Check` (güncelleme var mı + min-supported altı + **imzasız→şeffaf uyarı**); `VerifyChecksum` ile **bozuk paket kurulmaz** (değişiklik yok); `ApplyUpdate` 0-100 yüzde + hata logu; **başarısız kurulumda eski sürüme rollback**.
- **COMODO kanıtı**: gerçek DB mutlak LocalAppData yolu; kapat-aç sonrası veri **aynı DB'de kalır** (havuz boşaltma + yeniden açılış); health WAL + write/read ok. Hook (`comodo_guard.ps1`) .bat/imzasız exe engeli + Debug UseAppHost=false korunuyor.
- **Web parite**: `lib/update/update.ts`; Drizzle `app_releases` + `drizzle/0008_app_releases.sql`.
- **Doğrulama**: 179/179 .NET test (13 yeni) + 61 web node:test; build/lint/typecheck yeşil.

## Tamamlanan (Faz 14)
- **Migration011**: `enrollment_keys` (tek-kullanımlık/10 dk), `server_changes` (pull seq cursor feed), `sync_conflicts`; `sync_devices`'a token_hash/revoked_at/last_seen_at.
- **OutboxWriter**: yerel write + outbox AYNI transaction (operation_id + payload_hash + base_version); rollback hiçbirini bırakmaz; offline kalıcılık (yeniden açılış).
- **EnrollmentService**: tek-kullanımlık 10 dk enrollment anahtarı + cihaz enroll (pending) + master onay (token üretir, hash saklanır) + revoke.
- **SyncServer.Push**: cihaz doğrulama (pending/revoked → 403), operation_id **idempotency** (already_applied), **kritik işlemlerde LWW yok** (sunucu doğrulaması zorunlu → rejected/conflict + sync_conflicts), düşük-riskli base_version uyuşmazlığı → conflict.
- **SyncServer.Pull**: seq cursor; **bozuk sayfada rollback, cursor ilerlemez**; revoked cihaz 403.
- **Web parite**: `lib/sync/sync.ts` (classifyPush/pullPage).
- **Doğrulama**: 166/166 .NET test (12 yeni) + 57 web node:test; build/lint/typecheck yeşil.

## Tamamlanan (Faz 13)
- **FileValidation** (ortak): boyut ≤7MB + izinli MIME + **magic-byte** (sahte içerik reddi, MIME-içerik uyuşmazlığı reddi) + güvenli dosya adı (path traversal temizliği).
- **Storage provider** (`IFileStorageProvider` + `LocalFileStorageProvider`): swappable; kök içine sınırlı (traversal koruması); storage_key relatif yol.
- **FileService**: doğrula + sakla + `file_records` metadata (provider/key/mime/size/sha256) — operasyonel tabloya **base64 YAZMAZ**; tenant + entity-modül permission; audit.
- **TrashService**: master-data soft-delete liste/restore; **özel buton + yeniden doğrulama (reauth)** zorunlu; tenant fail-closed; audit. Operasyonel kayıtlar çöp kutusunda değil (iptal/ters kayıt).
- **BackupService**: `VACUUM INTO` tutarlı yedek + 30 gün retention + **integrity_check (=ok)** + gerçek geri yükleme (admin + reauth, havuz boşaltma ile kilit yok).
- **Web parite**: `lib/files/validation.ts`.
- **Doğrulama**: 154/154 .NET test (12 yeni) + 51 web node:test; build/lint/typecheck yeşil.

## Tamamlanan (Faz 12)
- **DashboardService**: tenant KPI (araç/malzeme/personel/düşük stok/bekleyen talep) + **birleşik uyarılar** (bakım + muayene/sigorta + düşük stok), permission filtreli, NavigateKey ile köprü.
- **ReportService**: salt-okuma raporlar (stok durumu, yakıt tüketim) tenant + permission fail-closed; `ReportGate` ile **Sorgula/Filtrele tıklanmadan çalışmaz**; firma filtresi yalnız Süper Admin, diğerleri kendi firmasına kilitli.
- **ExcelExportService**: ClosedXML ile `TableModel` → geçerli `.xlsx` (PK/ZIP), sayısal hücreler sayı.
- **MaterialImportService**: örnek başlık + ön kontrol + **satır bazlı hata** + **dry-run (yazmadan)** + commit (iş kuralı atlamaz, MaterialService.Create); satır bazlı try/catch politikası.
- **Web parite**: `lib/reports/{gate,import}.ts`.
- **Doğrulama**: 142/142 .NET test (10 yeni) + 45 web node:test; build/lint/typecheck yeşil.

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

## Yayın engelleri (genel kullanıcı yayını öncesi kapanmalı)
- **R10:** Operasyonel modüllerin UI ekranları (liste/form/import-export) bağlanmadı — servis+iş kuralı+test tam.
- **R8/R9:** Web oturum kalıcılığı + masaüstü/web login akışı bağlanmalı.
- **R4/R7:** Yerel/üretim PostgreSQL canlı migration uygulanmadı (SQLite tarafı tam; PG migration SQL üretildi).
- **R22:** Code-signing (imzasız sürümde şeffaf uyarı var).

## Açık işler (yayın-engeli değil)
- Updater transport/UI (R21); sync transport/UI (R19); push apply (R20); foto opt (R18); import modül kapsamı (R17); web PDF render (R16); şube-bazlı stok (R13); vehicle FK (R11); alert GROUP BY sağlamlaştırma (R14); npm dev-araç audit (R23).

## Sıradaki tek iş
- **Tüm 17 faz tamamlandı.** Genel yayın için sıradaki iş: yayın engellerini (R10 UI, R8/R9 login, R4/R7 PostgreSQL) kapatmak. Kullanıcı komutu olmadan başlatma.

## Güvenli komutlar
- `dotnet build DepoWise.sln`
- `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj`
- `dotnet run --project src/DepoWise.Desktop` (veya `dotnet <DLL>` — EXE/BAT yok)
- Web: `cd apps/web && npm run dev | npm run build | npm run typecheck`

## Bilinen engeller
- Bkz. `KNOWN_ISSUES.md`.
