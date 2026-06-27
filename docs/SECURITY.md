# DepoWise Güvenlik Runbook

## Sırlar (secrets)
- Sırlar **koda yazılmaz**; yalnız environment / secret store (`.env`, repoya girmez — `.gitignore`).
- Başlangıçta eksik/zayıf sır **fail-closed** (`apps/web/src/lib/config.ts`; Production'da DATABASE_URL/SESSION_SECRET zorunlu).
- Loglarda ham secret/PII yok: `LogRedactor` (.NET) / `redact.ts` (web) password/token/secret/authorization/connection-string/session/Bearer maskeler.

## Rotasyon
- **SESSION_SECRET:** env'de değiştir → tüm oturumlar geçersiz olur; kullanıcılar yeniden login.
- **Cihaz token'ı:** `EnrollmentService.RotateDeviceToken` → eski token anında geçersiz (hash değişir). Sızıntı şüphesinde `RevokeDevice` (push/pull 403).
- **Enrollment anahtarı:** tek-kullanımlık + 10 dk; sızsa bile fiziksel master onayı gerekir.
- **DB / yönetici kodu:** ayrı kanaldan değiştirilir; değişim audit'lenir.

## HTTP güvenliği (web)
- Başlıklar: CSP, X-Content-Type-Options=nosniff, X-Frame-Options=DENY, frame-ancestors 'none', Referrer-Policy, Permissions-Policy; HSTS yalnız Production (`next.config.mjs` + `headers.ts`).
- CSRF: double-submit token (`csrf.ts`), sabit-zaman doğrulama, fail-closed.
- Rate limit: login 5/5dk, sync push 60/dk, admin 30/dk (`ratelimit.ts` / `RateLimiter`). Masaüstü login ayrıca 5-hata/5dk kilit (`AuthService`).
- Body limiti + parametreli SQL (Dapper/Drizzle) + dosya magic-byte/MIME/boyut (`FileValidation`).

## Bağımlılık / tedarik zinciri
- Lock dosyaları commit'li (`package-lock.json`, NuGet). `npm audit` / `dotnet list package --vulnerable` periyodik.
- Bilinen kritik açık çözümü: `next` CVE-2025-66478 → 15.5.x yamalı (ADR-011).

## Yayın öncesi maliyetli kalemler (temel güvenlikten AYRI)
| Kalem | Durum | Öncelik |
|---|---|---|
| Code-signing (imzalı dağıtım) | Bekliyor (R22) | Yayın öncesi |
| Bağımsız pentest | Bekliyor | Yayın öncesi |
| Gelişmiş MFA (TOTP/WebAuthn) | Bekliyor | Yayın sonrası |

Bunlar kodun temel güvenliğini erteleme gerekçesi değildir (analiz §2/§9).

## Audit
- Tüm kritik mutasyonlar `AuditWriter` ile (actor, tenant, before/after, correlation_id, zaman). Ham token/parola/PII audit'e yazılmaz.
