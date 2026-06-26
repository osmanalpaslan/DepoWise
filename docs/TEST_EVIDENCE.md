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

---

### Şablon
## YYYY-MM-DD HH:mm - Faz / Amaç
- **Komut:**
- **Exit code:**
- **Sonuç:**
- **Geçen/Kalan:**
- **Kanıt/log yolu:**
- **COMODO host ve DB yolu:** Uygulanamaz / değer
