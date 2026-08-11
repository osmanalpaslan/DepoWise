# DepoWise Operasyon Runbook

## Üretim ortam değişkenleri
> Düzeltildi 2026-08-11 (FAZ H · H-4): burada `DATABASE_URL`, `SESSION_SECRET` ve `APP_BASE_URL`
> üretim değişkeni olarak listeleniyordu. Bu üçü **terk edilmiş Next.js uygulamasından** kalmadır ve
> .NET kodunda, `appsettings*.json` içinde ya da `fly*.toml` dosyalarında **hiç geçmez** (grep ile
> doğrulandı) — arıza anında yanlış yönlendiriyorlardı.

- **Tek kaynak: [`DEPLOYMENT.md`](DEPLOYMENT.md).** Üretimde kullanılan ortam değişkenlerinin güncel
  listesi, hangisinin zorunlu/opsiyonel ve secret olduğu, tanımlı değilse ne olduğu orada tutulur.
  Aynı bilgi burada **tekrarlanmaz** (iki liste ayrışır ve biri yanlış kalır).
- **Deploy öncesi/sonrası kontrol listesi:** `DEPLOYMENT.md` §8 kullanılır.
- 🔒 **Secret değerleri hiçbir dokümana yazılmaz** — ne buraya, ne `DEPLOYMENT.md`'ye. Dokümanlarda
  yalnız değişken **adı** ve değerin nereden sağlanacağı bulunur; gerçek değerler yalnız Fly secret
  deposundadır.
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
- Sağlık: API `GET /health` → `200 {"status":"ok","time":...}` (`DepoWise.Api/Program.cs:138`);
  masaüstü açılış `startup.log` (host/DB yolu/WAL/write-read).
  > Düzeltildi 2026-08-11 (FAZ H · H-5): burada `GET /api/v1/health` yazıyordu. Böyle bir uç **yoktur**;
  > bu projede API uçları `/api/...` altındadır ve **`/api/v1` sürüm öneki kullanılmaz** (CLAUDE.md §4).
  > Sağlık ucu sürüm öneksizdir: **`/health`**.
- Loglar redaction'lı (secret/PII yok). Hata oranı, login kilit sayısı, sync conflict kuyruğu izlenir.
- Yedek: günlük dosya mevcut mu + integrity_check.

## Acil durum
- **DB bozulması:** uygulamayı kapat → en son sağlam yedeği integrity_check ile doğrula → restore (admin + reauth) → yeniden aç.
- **Sızdırılmış cihaz token'ı:** `RevokeDevice` (push/pull 403) veya `RotateDeviceToken`.
- **Sızdırılmış sır:** JWT imza anahtarı (`DEPOWISE_JWT_KEY`) rotasyonu → eski anahtarla imzalanmış tüm
  token'lar doğrulamayı geçemez, **tüm oturumlar düşer** (`JwtTokens.ValidationParameters`,
  `ValidateIssuerSigningKey = true`). Ardından ilgili sağlayıcı anahtarını çevir. Değişken adları:
  `DEPLOYMENT.md`. Bkz. `SECURITY.md`.
  <!-- H-4 (2026-08-11): burada `SESSION_SECRET` yazıyordu — terk edilmiş Next.js uygulamasına ait,
       .NET kodunda hiç geçmeyen bir değişken. Gerçek karşılığı DEPOWISE_JWT_KEY'dir. -->
- **Başarısız güncelleme:** updater otomatik eski sürüme döner; tekrar denemeden önce checksum/log incele.

## COMODO (geliştirme makinesi)
- Proje EXE/BAT çalıştırılmaz; yalnız `dotnet build` / `dotnet run --project` / `dotnet <dll>`.
- `comodo_guard.ps1` hook .bat + imzasız `DepoWise*.exe`'yi engeller. Debug `UseAppHost=false`.
- Gerçek DB mutlak `%LOCALAPPDATA%\DepoWise\Data\<env>\depowise.db`; kapat-aç sonrası veri aynı DB'de (testle kanıt).
