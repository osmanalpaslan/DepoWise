# PROJECT STATE

**Son güncelleme:** 2026-06-26
**Aktif faz:** Faz 01 — Çözüm İskeleti ve Ortak Sözleşmeler
**Durum:** Tamamlandı

## Tamamlanan (Faz 01)
- **.NET çözümü** kuruldu: `DepoWise.sln` + `src/DepoWise.{Domain,Application,Infrastructure,Desktop}` + `tests/DepoWise.Tests`. Hepsi **net8.0** (Avalonia template'in net10.0 hedefi net8.0'a çekildi).
- **Ortak sözleşmeler** (Application/Common): `ApiError` + `ErrorCodes`, keyset `PageRequest`/`PagedResult`, `IClock`/`SystemClock` + `UnixTime`, `Correlation`, `HealthResult`/`IDatabaseHealth`.
- **Yerel DB temeli** (Infrastructure/Database): `AppPaths` (mutlak `%LOCALAPPDATA%\DepoWise\Data\<env>`), `SqliteConnectionFactory` (Cache=Private, WAL, foreign_keys=ON, busy_timeout=5000), `DatabaseHealth` (write/read).
- **Masaüstü açılış health**: `DesktopBootstrap` startup'ta health çalıştırıp `%LOCALAPPDATA%\DepoWise\Logs\startup.log`'a yazar; MainWindow özeti gösterir. App base tipi `Avalonia.Application` (namespace çakışması çözüldü).
- **Web iskeleti** (`apps/web`): Next.js 15 (TS strict + noUncheckedIndexedAccess), Drizzle/postgres, fail-closed `config.ts`, `/api/v1/health` (correlation_id + 200/503), `docs/openapi.yaml` (ApiError/PagedResult/Health şemaları). Web sözleşmeleri .NET ile fonksiyonel eşit.
- **Doğrulama**: .NET build + 7 test geçti; web typecheck/lint/build geçti. `next` güvenlik açığı (CVE-2025-66478) için 15.1.6 → 15.5.19 yamalı sürüme yükseltildi.

## Açık işler
- Faz 02: PostgreSQL + SQLite migration/audit temeli, gerçek tenant tabloları.
- Yerel PostgreSQL geliştirme örneği kurulumu (R4).

## Sıradaki tek iş
- **Faz 02 — Veritabanı Temeli, Audit ve Ortak Veri Kuralları** (`prompts/02_...md`). Kullanıcı komutu olmadan başlatma.

## Güvenli komutlar
- `dotnet build DepoWise.sln`
- `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj`
- `dotnet run --project src/DepoWise.Desktop` (veya `dotnet <DLL>` — EXE/BAT yok)
- Web: `cd apps/web && npm run dev | npm run build | npm run typecheck`

## Bilinen engeller
- Bkz. `KNOWN_ISSUES.md`.
