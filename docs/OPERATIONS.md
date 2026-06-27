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
- **Masaüstü (SQLite):** `MigrationRunner` açılışta bekleyen migration'ları sıralı + tek transaction uygular (idempotent). Geri alma: yedekten geri yükleme (BackupService).
- **Web (PostgreSQL):** `apps/web/drizzle/*.sql` sürümlü; `drizzle-kit migrate` ile uygula. Rollback: hedef sürüm yedeğinden restore + bir önceki migration setine dönüş (forward-fix tercih edilir).
- Migration öncesi **tam DB yedeği** zorunlu.

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
