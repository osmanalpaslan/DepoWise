# PROJECT STATE

**Son güncelleme:** 2026-06-26
**Aktif faz:** Faz 03 — Kimlik Doğrulama, Tenant ve Yetki Sistemi
**Durum:** Tamamlandı

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
- Faz 04: ortak UI, menü ve Tanımlar/Alan Ayarları altyapısı.
- Web oturum kalıcılığı: imzalı cookie + DB session lookup `getServerSession` içinde Faz 05'e bırakıldı (şimdilik fail-closed null) — R8.
- Yerel PostgreSQL geliştirme örneği kurulumu (R4/R7).

## Sıradaki tek iş
- **Faz 04 — Ortak UI, Menü ve Tanımlar/Alan Ayarları** (`prompts/04_...md`). Kullanıcı komutu olmadan başlatma.

## Güvenli komutlar
- `dotnet build DepoWise.sln`
- `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj`
- `dotnet run --project src/DepoWise.Desktop` (veya `dotnet <DLL>` — EXE/BAT yok)
- Web: `cd apps/web && npm run dev | npm run build | npm run typecheck`

## Bilinen engeller
- Bkz. `KNOWN_ISSUES.md`.
