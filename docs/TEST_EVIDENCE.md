# TEST EVIDENCE

Her kayıt aşağıdaki şablonla eklenir.

## 2026-06-26 - Faz 00 / Araç ve repo doğrulama
- **Komut:** `dotnet --version`, `node --version`
- **Exit code:** 0 / 0
- **Sonuç:** .NET SDK 8.0.422, Node v24.16.0 erişilebilir.
- **Geçen/Kalan:** Uygulanamaz (kaynak kod yok → build/unit test çalıştırılamadı).
- **Kanıt/log yolu:** Bu yanıt; repo envanteri PROJECT_STATE.md.
- **COMODO host ve DB yolu:** Uygulanamaz (henüz çalıştırılabilir uygulama yok). Hook + UseAppHost=false statik olarak doğrulandı.

## 2026-06-26 - Faz 01 / Çözüm iskeleti doğrulama
- **Komut:** `dotnet build DepoWise.sln -c Debug`
- **Exit code:** 0
- **Sonuç:** 5 proje derlendi (Domain/Application/Infrastructure/Desktop/Tests), 0 warning 0 error.
- **Geçen/Kalan:** —
- **Kanıt/log yolu:** Bu oturum çıktısı.

## 2026-06-26 - Faz 01 / .NET smoke testleri
- **Komut:** `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj -c Debug`
- **Exit code:** 0
- **Sonuç:** Connection WAL+FK, DatabaseHealth write/read, AppPaths mutlak yol, PageRequest/PagedResult, UnixTime, ApiError correlation.
- **Geçen/Kalan:** 7 geçti / 0 kaldı.
- **Kanıt/log yolu:** Bu oturum çıktısı.
- **COMODO host ve DB yolu:** Test geçici DB (`%TEMP%\depowise_test_*.db`); masaüstü runtime DB `%LOCALAPPDATA%\DepoWise\Data\Development\depowise.db` (startup.log). Build/test `dotnet` host ile, EXE/BAT çalıştırılmadı.

## 2026-06-26 - Faz 01 / Web typecheck + lint + build
- **Komut:** `npx tsc --noEmit`; `npx next lint`; `npx next build` (apps/web)
- **Exit code:** 0 / 0 / 0
- **Sonuç:** TS strict typecheck temiz; ESLint uyarısız; Next build başarılı (`/`, `/api/v1/health` route'ları üretildi). `next` 15.1.6 → 15.5.19 (CVE) sonrası build tekrar yeşil.
- **Geçen/Kalan:** —
- **Kanıt/log yolu:** Bu oturum çıktısı.
- **COMODO host ve DB yolu:** Uygulanamaz (web/node).

## 2026-06-26 - Faz 02 / Veritabanı temeli testleri
- **Komut:** `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj -c Debug`
- **Exit code:** 0
- **Sonuç:** Migration sıfır-DB + idempotent; tenant izolasyonu; TenantGuard fail-closed; soft-delete (fiziksel silinmez + başka firma silemez); audit create/delete; keyset sayfalama (tüm kayıt, tekrar yok, 3 sayfa); Unix ms zaman damgaları.
- **Geçen/Kalan:** 15 geçti / 0 kaldı (8 yeni temel + 7 iskelet).
- **Kanıt/log yolu:** Bu oturum çıktısı; `tests/DepoWise.Tests/DatabaseFoundationTests.cs`.
- **COMODO host ve DB yolu:** Test geçici DB (`%TEMP%\depowise_fnd_*.db`), `dotnet` host; EXE/BAT yok.

## 2026-06-26 - Faz 02 / Web şema + migration üretimi
- **Komut:** `npx tsc --noEmit`; `npx drizzle-kit generate --name core_schema`; `npx next lint`; `npx next build`
- **Exit code:** 0 / 0 / 0 / 0
- **Sonuç:** TS strict temiz; Drizzle 12 tablo → `apps/web/drizzle/0000_core_schema.sql` (offline, DB gerektirmez); lint temiz; build başarılı.
- **Geçen/Kalan:** —
- **Kanıt/log yolu:** `apps/web/drizzle/0000_core_schema.sql`.
- **COMODO host ve DB yolu:** Uygulanamaz (web/node). Canlı PG'ye migration UYGULANMADI (R4).

## 2026-06-26 - Faz 03 / Kimlik doğrulama + yetki testleri (.NET)
- **Komut:** `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj -c Debug`
- **Exit code:** 0
- **Sonuç:** Parola hash/verify + farklı salt; login başarı/hata; 5-hata kilidi + süre dolunca + başarı sıfırlama; deny-by-default; dashboard herkese açık; yalnız-view menü görünür/yazma reddi; admin tam yetki; payload farklı firma reddi; süper admin çapraz firma; admin-olmayan admin rolü atayamaz; firma admini foreign company reddi + kendi firmasında oluşturma; süper admin süper admin oluşturur.
- **Geçen/Kalan:** 28 geçti / 0 kaldı (12 yeni auth/yetki).
- **Kanıt/log yolu:** `tests/DepoWise.Tests/AuthPermissionTests.cs`.
- **COMODO host ve DB yolu:** Test geçici DB (`%TEMP%\depowise_auth_*.db`), `dotnet` host; EXE/BAT yok.

## 2026-06-26 - Faz 03 / Web güvenlik paritesi + build
- **Komut:** `node --test tests/`; `npx tsc --noEmit`; `npx next lint`; `npx next build` (apps/web)
- **Exit code:** 0 / 0 / 0 / 0
- **Sonuç:** 5 node:test geçti (parola .NET ile aynı biçim, deny-by-default, yalnız-view, admin bypass, payload firma reddi); typecheck/lint temiz; build başarılı (`/api/v1/me` korumalı uç 401 üretir).
- **Geçen/Kalan:** 5 geçti / 0 kaldı.
- **Kanıt/log yolu:** `apps/web/tests/security.test.ts`.
- **COMODO host ve DB yolu:** Uygulanamaz (web/node).

---

### Şablon
## YYYY-MM-DD HH:mm - Faz / Amaç
- **Komut:**
- **Exit code:**
- **Sonuç:**
- **Geçen/Kalan:**
- **Kanıt/log yolu:**
- **COMODO host ve DB yolu:** Uygulanamaz / değer
