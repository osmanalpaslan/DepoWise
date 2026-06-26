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

## 2026-06-27 - Faz 04 / Ortak UI + tema testleri (.NET)
- **Komut:** `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj -c Debug`
- **Exit code:** 0
- **Sonuç:** Menü deny-by-default/admin; tarih gerçek takvim (29/02 artık yıl, 31/02, 13. ay, maske); numerik negatif/sınır; çoklu seçim arama-korur/tümünü-seç-yalnız-filtre/Türkçe duyarsız; alan "+" buton yetki; tema varsayılan+firma override+audit.
- **Geçen/Kalan:** 48 geçti / 0 kaldı (12 yeni UI).
- **Kanıt/log yolu:** `tests/DepoWise.Tests/UiCommonTests.cs`.
- **COMODO host ve DB yolu:** Test geçici DB (`%TEMP%\depowise_ui_*.db`), `dotnet` host; masaüstü build başarılı, EXE/BAT çalıştırılmadı.

## 2026-06-27 - Faz 04 / Web ortak UI paritesi + build
- **Komut:** `npm test` (node --test); `npx tsc --noEmit`; `npx next lint`; `npx next build` (apps/web)
- **Exit code:** 0 / 0 / 0 / 0
- **Sonuç:** 12 node:test geçti (menü, tarih, numerik, çoklu seçim, alan "+", tema CSS değişkenleri); typecheck/lint temiz; build başarılı.
- **Geçen/Kalan:** 12 geçti / 0 kaldı.
- **Kanıt/log yolu:** `apps/web/tests/ui.test.ts`, `apps/web/tests/security.test.ts`.
- **COMODO host ve DB yolu:** Uygulanamaz (web/node).

## 2026-06-27 - Faz 05 / Firma, şube, personel testleri (.NET)
- **Komut:** `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj -c Debug`
- **Exit code:** 0
- **Sonuç:** Firma normal admin başka firmayı göremez; firma oluşturma yalnız süper admin; başka firma erişimi reddi; şube kapsamlı kullanıcı kapsam dışına taşamaz; personel CRUD tenant izolasyonu; soft delete/restore; kapsam dışı şube reddi; liste kapsam dışı personeli göstermez; personel deny-by-default.
- **Geçen/Kalan:** 57 geçti / 0 kaldı (9 yeni org/personel).
- **Kanıt/log yolu:** `tests/DepoWise.Tests/OrgPersonnelTests.cs`.
- **COMODO host ve DB yolu:** Test geçici DB (`%TEMP%\depowise_org_*.db`), `dotnet` host; EXE/BAT yok.

## 2026-06-27 - Faz 05 / Web org paritesi + migration + build
- **Komut:** `npm test`; `npx drizzle-kit generate --name personnel_scopes`; `npx tsc --noEmit`; `npx next lint`; `npx next build`
- **Exit code:** 0 (tümü)
- **Sonuç:** 16 node:test (4 yeni org: firma yönetimi süper admin, görünür firmalar, kapsam çözümü, kapsam dışı şube reddi); Drizzle `0001_personnel_scopes.sql` üretildi; typecheck/lint temiz; build başarılı.
- **Geçen/Kalan:** 16 geçti / 0 kaldı.
- **Kanıt/log yolu:** `apps/web/tests/org.test.ts`, `apps/web/drizzle/0001_personnel_scopes.sql`.
- **COMODO host ve DB yolu:** Uygulanamaz (web/node). Canlı PG'ye migration UYGULANMADI (R4).

## 2026-06-27 - Faz 06 / Malzeme + tanımlar + açılış stoğu testleri (.NET)
- **Komut:** `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj -c Debug`
- **Exit code:** 0
- **Sonuç:** Kod benzersiz (tenant) + farklı firmada serbest; geçersiz currency reddi + USD saklama; muadil çift yönlü/kendine red/döngü güvenli/başka firma red; uyumlu araç detayı malzeme stoğu gösterir; açılış stoğu hareket defterinde + bakiye günceller; idempotent (op_id tekrarı çift yazmaz); açılış deny-by-default; tanımlar tenant izole.
- **Geçen/Kalan:** 69 geçti / 0 kaldı (12 yeni).
- **Kanıt/log yolu:** `tests/DepoWise.Tests/MaterialTests.cs`.
- **COMODO host ve DB yolu:** Test geçici DB (`%TEMP%\depowise_mat_*.db`), `dotnet` host; EXE/BAT yok.

## 2026-06-27 - Faz 06 / Web malzeme paritesi + migration + build
- **Komut:** `npm test`; `npx drizzle-kit generate --name materials_ledger`; `npx tsc --noEmit`; `npx next lint`; `npx next build`
- **Exit code:** 0 (tümü)
- **Sonuç:** 20 node:test (muadil çift yönlü/döngü güvenli/kendine red; para birimi TRY/USD/EUR); Drizzle `0002_materials_ledger.sql` (11 yeni tablo); typecheck/lint temiz; build başarılı.
- **Geçen/Kalan:** 20 geçti / 0 kaldı.
- **Kanıt/log yolu:** `apps/web/tests/materials.test.ts`, `apps/web/drizzle/0002_materials_ledger.sql`.
- **COMODO host ve DB yolu:** Uygulanamaz (web/node). Canlı PG'ye migration UYGULANMADI (R4).

## 2026-06-27 - Faz 07 / Stok işlemleri testleri (.NET)
- **Komut:** `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj -c Debug`
- **Exit code:** 0
- **Sonuç:** Giriş/çıkış bakiye; negatif stok engeli + rollback; idempotency (op_id çift yazmaz); transfer atomik 2 hareket + yetersiz stok red; sayım gerekçeli fark + gerekçe zorunlu; iptal ters hareket (orijinal silinmez, belge cancelled) + idempotent; **eş zamanlı iki çıkış negatif oluşturamaz** (Parallel.For, 1 ok/1 fail); deny-by-default.
- **Geçen/Kalan:** 81 geçti / 0 kaldı (12 yeni).
- **Kanıt/log yolu:** `tests/DepoWise.Tests/StockOperationTests.cs`.
- **COMODO host ve DB yolu:** Test geçici DB (`%TEMP%\depowise_stk_*.db`), `dotnet` host; EXE/BAT yok.

## 2026-06-27 - Faz 07 / Web stok defteri paritesi + migration + build
- **Komut:** `npm test`; `npx drizzle-kit generate --name stock_documents`; `npx tsc --noEmit`; `npx next lint`; `npx next build`
- **Exit code:** 0 (tümü)
- **Sonuç:** 26 node:test (giriş/çıkış, negatif guard, idempotent, transfer net-zero, iptal ters hareket, applyDelta); Drizzle `0003_stock_documents.sql`; typecheck/lint temiz; build başarılı.
- **Geçen/Kalan:** 26 geçti / 0 kaldı.
- **Kanıt/log yolu:** `apps/web/tests/stock.test.ts`, `apps/web/drizzle/0003_stock_documents.sql`.
- **COMODO host ve DB yolu:** Uygulanamaz (web/node). Canlı PG'ye migration UYGULANMADI (R4).

## 2026-06-27 - Faz 08 / Araç + sayaç testleri (.NET)
- **Komut:** `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj -c Debug`
- **Exit code:** 0
- **Sonuç:** Sayaç doğrudan geriye reddi; ileri güncelle+log; AdvanceMeter küçük no-op (geçmiş engellemez); tüm değişimler loglanır; iç kod benzersiz + otomatik üretim; şablon yeni aracı doldurur + malzemeleri kopyalar; kullanıcı değeri öncelikli; deny-by-default; tenant izolasyonu.
- **Geçen/Kalan:** 91 geçti / 0 kaldı (10 yeni).
- **Kanıt/log yolu:** `tests/DepoWise.Tests/VehicleTests.cs`.
- **COMODO host ve DB yolu:** Test geçici DB (`%TEMP%\depowise_veh_*.db`), `dotnet` host; EXE/BAT yok.

## 2026-06-27 - Faz 08 / Web araç paritesi + migration + build
- **Komut:** `npm test`; `npx drizzle-kit generate --name vehicles`; `npx tsc --noEmit`; `npx next lint`; `npx next build`
- **Exit code:** 0 (tümü)
- **Sonuç:** 29 node:test (sayaç geriye red, ileri/no-op, şablon doldurma+kullanıcı önceliği); Drizzle `0004_vehicles.sql` (8 yeni tablo); typecheck/lint temiz; build başarılı.
- **Geçen/Kalan:** 29 geçti / 0 kaldı.
- **Kanıt/log yolu:** `apps/web/tests/vehicles.test.ts`, `apps/web/drizzle/0004_vehicles.sql`.
- **COMODO host ve DB yolu:** Uygulanamaz (web/node). Canlı PG'ye migration UYGULANMADI (R4).

---

### Şablon
## YYYY-MM-DD HH:mm - Faz / Amaç
- **Komut:**
- **Exit code:**
- **Sonuç:**
- **Geçen/Kalan:**
- **Kanıt/log yolu:**
- **COMODO host ve DB yolu:** Uygulanamaz / değer
