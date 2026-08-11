# DepoWise Operasyon Runbook

## Üretim env kontrol listesi (web)
- [ ] `DATABASE_URL` ayarlı (yönetilen PostgreSQL).
- [ ] `SESSION_SECRET` ≥ 16 karakter, secret store'dan.
- [ ] `DEPOWISE_ENVIRONMENT=Production` (HSTS + fail-closed config aktif olur).
- [ ] `APP_BASE_URL` https.
- [ ] Sırlar repoda değil; `.env` git'te yok (`git ls-files` ile doğrula).
- [ ] Güvenlik başlıkları yanıtta görünür (CSP, HSTS, X-Frame DENY, nosniff).
- [ ] Rate limit aktif (login/sync/admin).

## Migration / rollback
> Düzeltildi 2026-08-11 (FAZ H · H-3): bu bölüm PostgreSQL migration yolu olarak `apps/web/drizzle` +
> `drizzle-kit migrate` gösteriyordu. O yol **terk edilmiş Next.js uygulamasına** aittir (2026-06-27'den
> beri donmuş, ADR-057) ve **üretimde kullanılmaz**. Gerçek akış aşağıdadır (koddan doğrulandı).

- **Tek migration mekanizması — her iki lehçe için aynı:** `MigrationRunner` + `MigrationCatalog`
  (`src/DepoWise.Infrastructure/Database/Migrations/`). Sunucu (PostgreSQL) ve masaüstü (SQLite)
  **aynı sürümlü kataloğu** yürütür; lehçe farkları `SqlDialect` / `DbIntrospect` / `DialectPurge`
  içinde toplanmıştır.
- **Sunucu (PostgreSQL):** migration **API açılışında OTOMATİK** çalışır —
  `ServerServices` yapıcısı `new MigrationRunner(Factory).Run()` çağırır (`ServerServices.cs:106`).
  Ayrı bir migration komutu/adımı **yoktur**: **deploy = migration**.
- **Masaüstü (SQLite):** aynı runner uygulama açılışında çalışır (`DesktopBootstrap.Run`).
- **Runner davranışı** (`MigrationRunner.cs`): yalnız **uygulanmamış** sürümleri **artan sırada**,
  her birini **tek transaction** içinde uygular; başarılı olanı `schema_migrations` tablosuna yazar.
  Bir migration hata verirse **yalnız o migration geri alınır** ve uygulama açılmaz. Tekrar çalıştırma
  zararsızdır (**idempotent**).
- **Rollback:** otomatik geri alma **yoktur** ve `schema_migrations`'tan satır silmek **yapılmaz**.
  Tercih **ileri düzeltme** (forward-fix). Gerekiyorsa: yedekten **YENİ** bir veritabanına dönülür ve
  bağlantı oraya yönlendirilir — bkz. `POSTGRES_BACKUP_RESTORE.md` §4.4.
- **Migration öncesi tam DB yedeği ZORUNLU:** PostgreSQL için `pg_dump -Fc`
  (`POSTGRES_BACKUP_RESTORE.md`); masaüstü SQLite için `BackupService` (`VACUUM INTO`).
  ⚠️ `BackupService` **yalnız SQLite** içindir, sunucu PostgreSQL'ini yedeklemez.

## İzleme (monitoring)
- Sağlık: web `GET /api/v1/health` (config/200-503); masaüstü açılış `startup.log` (host/DB yolu/WAL/write-read).
- Loglar redaction'lı (secret/PII yok). Hata oranı, login kilit sayısı, sync conflict kuyruğu izlenir.
- Yedek: günlük dosya mevcut mu + integrity_check.

## Acil durum
- **DB bozulması:** uygulamayı kapat → en son sağlam yedeği integrity_check ile doğrula → restore (admin + reauth) → yeniden aç.
- **Sızdırılmış cihaz token'ı:** `RevokeDevice` (push/pull 403) veya `RotateDeviceToken`.
- **Sızdırılmış sır:** `SESSION_SECRET` rotasyonu (tüm oturumlar düşer); ilgili sağlayıcı anahtarını çevir. Bkz. `SECURITY.md`.
- **Başarısız güncelleme:** updater otomatik eski sürüme döner; tekrar denemeden önce checksum/log incele.

## COMODO (geliştirme makinesi)
- Proje EXE/BAT çalıştırılmaz; yalnız `dotnet build` / `dotnet run --project` / `dotnet <dll>`.
- `comodo_guard.ps1` hook .bat + imzasız `DepoWise*.exe`'yi engeller. Debug `UseAppHost=false`.
- Gerçek DB mutlak `%LOCALAPPDATA%\DepoWise\Data\<env>\depowise.db`; kapat-aç sonrası veri aynı DB'de (testle kanıt).
