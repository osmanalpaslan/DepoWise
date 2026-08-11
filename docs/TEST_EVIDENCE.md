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

## 2026-06-27 - Faz 09 / Bakım + uyarı testleri (.NET)
- **Komut:** `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj -c Debug`
- **Exit code:** 0
- **Sonuç:** Eşik %85/95/100 (Theory); bakım malzeme tek düşüm + fiyat snapshot; idempotency çift düşmez; yetersiz stok rollback (kayıt da oluşmaz); iptal stoğu geri alır (idempotent); sayaç ileri; uyarı kritik→yeni bakım Normal; gecikti ≥%100; deny-by-default; muayene/sigorta tarih uyarısı (yaklaşan/geçmiş).
- **Geçen/Kalan:** 107 geçti / 0 kaldı (16 yeni).
- **Kanıt/log yolu:** `tests/DepoWise.Tests/MaintenanceTests.cs`.
- **COMODO host ve DB yolu:** Test geçici DB (`%TEMP%\depowise_mnt_*.db`), `dotnet` host; EXE/BAT yok.

## 2026-06-27 - Faz 09 / Web bakım paritesi + migration + build
- **Komut:** `npm test`; `npx drizzle-kit generate --name maintenance`; `npx tsc --noEmit`; `npx next lint`; `npx next build`
- **Exit code:** 0 (tümü)
- **Sonuç:** 33 node:test (eşikler, interval 0, sonraki hedef, tüketilen/yeni-bakım-temizler); Drizzle `0005_maintenance.sql` (5 yeni tablo); typecheck/lint temiz; build başarılı.
- **Geçen/Kalan:** 33 geçti / 0 kaldı.
- **Kanıt/log yolu:** `apps/web/tests/maintenance.test.ts`, `apps/web/drizzle/0005_maintenance.sql`.
- **COMODO host ve DB yolu:** Uygulanamaz (web/node). Canlı PG'ye migration UYGULANMADI (R4).

## 2026-06-27 - Faz 10 / Yakıt + günlük faaliyet testleri (.NET)
- **Komut:** `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj -c Debug`
- **Exit code:** 0
- **Sonuç:** Yakıt depo+dağıtım bakiye/sayaç tutarlı; depo yetersiz engeli; fiyat snapshot geçmişte değişmez; dağıtım idempotent; günlük faaliyet bakım TEK kayıt + tek stok düşümü + referans; bakım idempotent; transfer aracı pasife alır; hareket durumu değiştirmez; deny-by-default.
- **Geçen/Kalan:** 116 geçti / 0 kaldı (9 yeni).
- **Kanıt/log yolu:** `tests/DepoWise.Tests/FuelDailyActivityTests.cs`.
- **COMODO host ve DB yolu:** Test geçici DB (`%TEMP%\depowise_fda_*.db`), `dotnet` host; EXE/BAT yok.

## 2026-06-27 - Faz 10 / Web yakıt paritesi + migration + build
- **Komut:** `npm test`; `npx drizzle-kit generate --name fuel_daily`; `npx tsc --noEmit`; `npx next lint`; `npx next build`
- **Exit code:** 0 (tümü)
- **Sonuç:** 37 node:test (depo bakiye, güncel fiyat, maliyet snapshot, L/100km güvenli); Drizzle `0006_fuel_daily.sql` (3 yeni tablo); typecheck/lint temiz; build başarılı.
- **Geçen/Kalan:** 37 geçti / 0 kaldı.
- **Kanıt/log yolu:** `apps/web/tests/fuel.test.ts`, `apps/web/drizzle/0006_fuel_daily.sql`.
- **COMODO host ve DB yolu:** Uygulanamaz (web/node). Canlı PG'ye migration UYGULANMADI (R4).

## 2026-06-27 - Faz 11 / Talep, onay, PDF testleri (.NET)
- **Komut:** `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj -c Debug`
- **Exit code:** 0
- **Sonuç:** Durum geçişleri (Theory: çift onay/erken onay engelli); belge no tenant/yıl benzersiz artar; **onay stoğu değiştirmez**; çift onay engeli; yetkisiz onay reddi; onaylı talepten kontrollü çıkış stok düşer; onaysızdan çıkış reddi; durum geçmişi; tenant izolasyonu; deny-by-default; **PDF Türkçe karakterlerle %PDF üretir**.
- **Geçen/Kalan:** 132 geçti / 0 kaldı (16 yeni).
- **Kanıt/log yolu:** `tests/DepoWise.Tests/RequestTests.cs`.
- **COMODO host ve DB yolu:** Test geçici DB (`%TEMP%\depowise_req_*.db`), `dotnet` host; EXE/BAT yok.

## 2026-06-27 - Faz 11 / Web talep paritesi + migration + build
- **Komut:** `npm test`; `npx drizzle-kit generate --name requests`; `npx tsc --noEmit`; `npx next lint`; `npx next build`
- **Exit code:** 0 (tümü)
- **Sonuç:** 40 node:test (durum geçişleri, terminaller, belge no); Drizzle `0007_requests.sql` (3 yeni tablo); typecheck/lint temiz; build başarılı.
- **Geçen/Kalan:** 40 geçti / 0 kaldı.
- **Kanıt/log yolu:** `apps/web/tests/requests.test.ts`, `apps/web/drizzle/0007_requests.sql`.
- **COMODO host ve DB yolu:** Uygulanamaz (web/node). Canlı PG'ye migration UYGULANMADI (R4).

## 2026-06-27 - Faz 12 / Dashboard, rapor, excel, import testleri (.NET)
- **Komut:** `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj -c Debug`
- **Exit code:** 0
- **Sonuç:** Rapor filtre tıklanmadan çalışmaz; tenant sızıntısı yok; firma filtresi yalnız süper admin + normal admin başka firma reddi; deny-by-default; Excel geçerli xlsx (PK); dashboard tenant KPI + düşük stok uyarısı; import örnek başlık; dry-run satır bazlı hata + DB yazmaz; commit geçerli satırları uygular + hata raporlar; import kod benzersizliği (iş kuralı atlamaz).
- **Geçen/Kalan:** 142 geçti / 0 kaldı (10 yeni).
- **Kanıt/log yolu:** `tests/DepoWise.Tests/ReportingTests.cs`.
- **COMODO host ve DB yolu:** Test geçici DB (`%TEMP%\depowise_rep_*.db`), `dotnet` host; EXE/BAT yok.

## 2026-06-27 - Faz 12 / Web rapor+import paritesi + build
- **Komut:** `npm test`; `npx tsc --noEmit`; `npx next lint`; `npx next build`
- **Exit code:** 0 (tümü)
- **Sonuç:** 45 node:test (rapor kapısı, firma filtresi, import örnek başlık/satır doğrulama/dry-run); typecheck/lint temiz; build başarılı.
- **Geçen/Kalan:** 45 geçti / 0 kaldı.
- **Kanıt/log yolu:** `apps/web/tests/reporting.test.ts`.
- **COMODO host ve DB yolu:** Uygulanamaz (web/node).

## 2026-06-27 - Faz 13 / Dosya, çöp kutusu, yedek testleri (.NET)
- **Komut:** `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj -c Debug`
- **Exit code:** 0
- **Sonuç:** Geçerli jpeg kaydı (base64 yok, içerik diskte); sahte dosya magic-byte reddi; MIME-içerik uyuşmazlığı reddi; 7MB üstü reddi; deny-by-default; güvenli ad path traversal temizliği; çöp kutusu liste/restore + reauth zorunlu + yetkisiz reddi; yedek al + integrity_check=ok; geri yükle veri korunur (admin+reauth) + yetki/reauth zorunlu.
- **Geçen/Kalan:** 154 geçti / 0 kaldı (12 yeni).
- **Kanıt/log yolu:** `tests/DepoWise.Tests/FileTrashBackupTests.cs`.
- **COMODO host ve DB yolu:** Test geçici DB (`%TEMP%\depowise_file_*.db`), `dotnet` host; EXE/BAT yok. Yedek integrity_check + geri yükle gerçek dosyayla doğrulandı.

## 2026-06-27 - Faz 13 / Web dosya doğrulama paritesi + build
- **Komut:** `npm test`; `npx tsc --noEmit`; `npx next lint`; `npx next build`
- **Exit code:** 0 (tümü)
- **Sonuç:** 51 node:test (magic-byte tespiti, sahte/MIME uyuşmazlığı/büyük dosya reddi, güvenli ad); typecheck/lint temiz; build başarılı.
- **Geçen/Kalan:** 51 geçti / 0 kaldı.
- **Kanıt/log yolu:** `apps/web/tests/files.test.ts`.
- **COMODO host ve DB yolu:** Uygulanamaz (web/node).

## 2026-06-27 - Faz 14 / Offline senkronizasyon testleri (.NET)
- **Komut:** `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj -c Debug`
- **Exit code:** 0
- **Sonuç:** Enrollment anahtarı tek-kullanımlık + 10 dk süre + yanlış anahtar reddi; onaysız/revoked cihaz push/pull 403; aynı operation_id ikinci kez already_applied (çift yazmaz); kritik işlem doğrulayıcı yoksa/red conflict kuyruğu; düşük-riskli version uyuşmazlığı conflict; pull cursor ilerler + bozuk sayfa rollback (ilerlemez); offline kalıcılık (yeniden açılış); outbox yerel-write atomik (rollback bırakmaz).
- **Geçen/Kalan:** 166 geçti / 0 kaldı (12 yeni).
- **Kanıt/log yolu:** `tests/DepoWise.Tests/SyncTests.cs`.
- **COMODO host ve DB yolu:** Test geçici DB (`%TEMP%\depowise_sync_*.db`), `dotnet` host; EXE/BAT yok.

## 2026-06-27 - Faz 14 / Web sync paritesi + build
- **Komut:** `npm test`; `npx tsc --noEmit`; `npx next lint`; `npx next build`
- **Exit code:** 0 (tümü)
- **Sonuç:** 57 node:test (kritik tespit, retry/already_applied, kritik doğrulama red/kabul, version conflict, pull rollback); typecheck/lint temiz; build başarılı.
- **Geçen/Kalan:** 57 geçti / 0 kaldı.
- **Kanıt/log yolu:** `apps/web/tests/sync.test.ts`.
- **COMODO host ve DB yolu:** Uygulanamaz (web/node).

## 2026-06-27 - Faz 15 / Setup, güncelleme, COMODO testleri (.NET)
- **Komut:** `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj -c Debug`
- **Exit code:** 0
- **Sonuç:** SemVer karşılaştırma/geçersiz red; release yayın yalnız süper admin + latest en yüksek SemVer + geçersiz checksum red; updater check (güncelleme/min-supported/imzasız uyarı); **bozuk paket kurulmaz** (sürüm değişmez); başarılı kurulum 0-100 + sürüm güncellenir; **kurulum hatası → eski sürüme rollback**; COMODO gerçek DB mutlak yol + kapat-aç veri kalıcılığı + health WAL/write-read ok.
- **Geçen/Kalan:** 179 geçti / 0 kaldı (13 yeni).
- **Kanıt/log yolu:** `tests/DepoWise.Tests/UpdateComodoTests.cs`.
- **COMODO host ve DB yolu:** Test `dotnet` host; gerçek DB `%LOCALAPPDATA%\DepoWise\Data\Development\depowise.db` (mutlak); kapat-aç sonrası veri aynı DB'de doğrulandı; EXE/BAT çalıştırılmadı.

## 2026-06-27 - Faz 15 / Web güncelleme paritesi + migration + build
- **Komut:** `npm test`; `npx drizzle-kit generate --name app_releases`; `npx tsc --noEmit`; `npx next lint`; `npx next build`
- **Exit code:** 0 (tümü)
- **Sonuç:** 61 node:test (SemVer, güncelleme kontrolü+min-supported+signed uyarı, checksum doğrulama); Drizzle `0008_app_releases.sql`; typecheck/lint temiz; build başarılı.
- **Geçen/Kalan:** 61 geçti / 0 kaldı.
- **Kanıt/log yolu:** `apps/web/tests/update.test.ts`, `apps/web/drizzle/0008_app_releases.sql`.
- **COMODO host ve DB yolu:** Uygulanamaz (web/node).

## 2026-06-27 - Faz 16 / Güvenlik sertleştirme testleri (.NET)
- **Komut:** `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj -c Debug`
- **Exit code:** 0
- **Sonuç:** Log redaction secret/JSON/connstr maskeler + IsSensitiveKey; rate limit login 5/5dk + pencere sonrası açılır + anahtar-bazlı izole; cihaz token rotasyonu eski token geçersiz; revoke cascade push/pull 403; audit correlation_id taşınır.
- **Geçen/Kalan:** 186 geçti / 0 kaldı (7 yeni).
- **Kanıt/log yolu:** `tests/DepoWise.Tests/SecurityHardeningTests.cs`.
- **COMODO host ve DB yolu:** Test geçici DB (`%TEMP%\depowise_sec_*.db`), `dotnet` host; EXE/BAT yok.

## 2026-06-27 - Faz 16 / Web güvenlik paritesi + audit + secret tarama
- **Komut:** `npm test`; `npx tsc --noEmit`; `npx next lint`; `npx next build`; `npm audit`; `git grep` secret tarama
- **Exit code:** 0 / 0 / 0 / 0 / (audit advisory) / 0
- **Sonuç:** 66 node:test (güvenlik başlıkları CSP/nosniff/frame/HSTS-prod, rate limit, CSRF double-submit, redaction); typecheck/lint temiz; build başarılı. `npm audit`: 9 advisory dev/build araçlarında (R23). **Repo secret taraması temiz; .env izlenmiyor.**
- **Geçen/Kalan:** 66 geçti / 0 kaldı.
- **Kanıt/log yolu:** `apps/web/tests/security-hardening.test.ts`, `docs/SECURITY.md`.
- **COMODO host ve DB yolu:** Uygulanamaz (web/node).

## 2026-06-27 - Faz 17 / Uçtan uca + temiz koşu + release candidate
- **Komut / Exit:**
  - `dotnet build DepoWise.sln -c Debug` → 0 (0 hata)
  - `dotnet test tests/DepoWise.Tests` → 0 → **187/187 geçti** (uçtan uca `EndToEndTests` dahil)
  - `dotnet publish src/DepoWise.Desktop -c Release` → 0
  - `npx tsc --noEmit` → 0; `npx next lint` → temiz; `npx next build` → başarılı
  - `node --test` (web) → **66/66 geçti**
  - `git grep` secret tarama → temiz; `.env` izlenmiyor
  - `npm audit` → 9 advisory (dev/build araçları, runtime yok — R23)
- **Sonuç:** Uçtan uca akış (malzeme/stok→araç/bakım/uyarı→talep→sync→yedek/restore) + tenant izolasyonu tek senaryoda geçti. Tüm kabul testi alanları kanıtlı (bkz. RELEASE_CANDIDATE.md).
- **Release candidate:** 1.0.0-rc; DLL SHA-256 `2627A0F1...A448`, ZIP SHA-256 `69A7E9CF...D062` (54 dosya, ~246 MB).
- **Kanıt/log yolu:** `tests/DepoWise.Tests/EndToEndTests.cs`, `docs/RELEASE_CANDIDATE.md`, `docs/USER_GUIDE.md`, `docs/OPERATIONS.md`.
- **COMODO host ve DB yolu:** Tüm .NET koşusu `dotnet` host; gerçek DB mutlak `%LOCALAPPDATA%\DepoWise\Data\...`; kapat-aç kalıcılık `Comodo_KapatAc_VeriAyniDBdeKalir` + `EndToEndTests` ile doğrulandı; EXE/BAT çalıştırılmadı (publish ≠ çalıştırma).

### ÇALIŞTIRILAMAYAN / AÇIK (dürüst kayıt)
- Avalonia/React **UI ekranları** otomatik test edilmedi (ekranlar bağlı değil — R10); yalnız servis/iş mantığı + ortak UI mantık testleri var.
- **Canlı PostgreSQL** üzerinde Drizzle migration çalıştırılmadı (R4/R7); yalnız offline SQL üretildi.
- Web **login/oturum** uçtan uca akışı bağlı değil (R8/R9).

---

### Şablon
## YYYY-MM-DD HH:mm - Faz / Amaç
- **Komut:**
- **Exit code:**
- **Sonuç:**
- **Geçen/Kalan:**
- **Kanıt/log yolu:**
- **COMODO host ve DB yolu:** Uygulanamaz / değer

## 05.07.2026 — Güvenlik sertleştirme sonrası doğrulama
- Ortam: Linux sandbox, .NET SDK 8.0.128, NuGet offline fallback (host cache).
- `dotnet build DepoWise.Api` (Release): BAŞARILI, 0 hata.
- `dotnet build DepoWise.Web` (Release): BAŞARILI, 0 hata.
- `dotnet test` filtre SecurityHardening+AuthPermission: 30/30 GEÇTİ.
- `dotnet test` filtre SyncTests+BusinessSync+StockOperation: 30/30 GEÇTİ.
- `dotnet test` filtre EndToEnd+DatabaseFoundation+CompanyGrant: 10/10 GEÇTİ.
- Canlı web (depowise-web.fly.dev): tanım ekle/sil CRUD çalıştı; F5 bug'ı, kullanıcı formu autofill ve server-status yetki hatası canlıda tespit edildi (düzeltme kodda, deploy bekliyor).

## 05.07.2026 — business-push yetki+doğrulama
- `dotnet build Api` + `Tests` (Release): 0 hata.
- BusinessSync testleri: 9/9 GEÇTİ (3 yeni: Apply_YetkisizModul_TablosuUygulanmaz, Apply_Admin_TumTablolariYazabilir, Apply_NegatifStokBakiyesi_Reddedilir).
- Sync+AuthPermission+StockOperation birleşik: 56/56 GEÇTİ.

## 05.07.2026 — JWT refresh + updater rollback
- Api + Web + Desktop + Tests (Release): 0 hata derlendi.
- Yeni: JwtTokenTests 4/4 GEÇTİ. (test projesine DepoWise.Api referansı eklendi.)
- TAM test suit: 238/238 GEÇTİ (regresyon yok).
- Updater gerçek PowerShell yolu: Windows'ta manuel/entegrasyon testi gerekli (Linux sandbox'ta çalıştırılamaz).

## 2026-07-12 — ADR-064…074 (süper admin kilidi, senkron otoritesi, offline kuyruk, logolar)

**Komut:** `dotnet build DepoWise.sln -c Debug` · `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj -c Debug`
**Sonuç:** Build **0 hata** · Test **267/267 yeşil** (oturum başında 251 idi → **+16 yeni test**)

**Eklenen kritik testler (hepsi kullanıcının bildirdiği gerçek hataları çiviliyor):**

| Test | Neyi garanti ediyor |
|---|---|
| `OrgPersonnelTests.Firma_Silme_SuperAdmini_PasifeAlmaz` | Süper admin kendi firmasını silse bile **pasife alınmaz** ve **tekrar giriş yapabilir** (ADR-064) |
| `OrgPersonnelTests.SuperAdmin_CalistigiFirmayiSilince_Oturum_Dusmez_401_Vermez` | İçinde çalıştığı firmayı silince oturum **düşmez**, home firmaya düşer; firma listesi yüklenir (ADR-068) |
| `AuthPermissionTests.SuperAdmin_OlmayanFirmada_Oturum_Acamaz` *(mevcut, korundu)* | **Hiç var olmayan** firma id'sinde **fail-closed** (sahte token koruması bozulmadı) |
| `OrgPersonnelTests.Sube_Silinince_HicbirListede_Gorunmez` | Silinen şube ne listede ne şube-kapsam çözümleyicisinde çıkar (ADR-066) |
| `BusinessSyncTests.Webte_Silinen_Kayit_Yerelde_De_Silinir_SUNUCU_OTORITER` | Web'de silinen kayıt, makinede **daha yeni düzenleme olsa bile** yerelde silinir (ADR-069) |
| `BusinessSyncTests.Sunucuda_Silinen_Kayit_Cihaz_Pushuyla_Diriltilemez` | Cihaz push'u sunucudaki silmeyi **diriltemez** (ADR-069) |
| `BusinessSyncTests.GeriCekmede_SilinmemisKayitta_LWW_Korunur` | Karşı-kontrol: **silme dışında** LWW davranışı bozulmadı |
| `OrgPersonnelTests.Firma_Kuyruk_TekrarGonderiminde_HataVermez_IDEMPOTENT` | Offline kuyruk yeniden denerse **hata yok, mükerrer kayıt yok**; olmayan firmada yine hata (ADR-072) |
| `OrgPersonnelTests.SahaPersoneli_Kutucugu_Kaydedilir_VeOkunur` | "Saha personeli" kutucuğu kalıcı (ADR-067) |
| `OrgPersonnelTests.Unvan_Tanimi_Eklenir_Listelenir_MukerrerOlmaz` | Unvan sabit tanımı; **Türkçe duyarlı** mükerrer kontrolü ("Şoför" == "şoför") |
| `OrgPersonnelTests.Unvan_Tanimlari_FirmayaIzole` | Unvan tanımlarında **tenant izolasyonu** |
| `OrgPersonnelTests.Kullanici_PersoneleBaglanir_ListedeGorunur` | Kullanıcı↔personel bağı + **bir personele tek hesap** |
| `ServerPresenceTests` (4 test) | Kota ONLINE **kullanıcı bazında tekil**: aynı kişi web+masaüstü = **1**; farklı kullanıcılar ayrı; 5 dk penceresi; aynı kişi iki firmada bile tek |

**Canlı doğrulamalar:**
- API `depowise-erp.fly.dev/health` → **200** · Web `depowise-web.fly.dev` → **200**
- Yeni uç `/api/personnel-titles` → **401** (var, auth istiyor — 404 değil)
- Süper admin **canlı girişi doğrulandı** (paket yayın scripti login oldu: `Giris OK (superAdmin)`)
- Masaüstü **1.0.46** yayınlandı; sunucuda "en güncel sürüm" doğrulandı
- Paket saklama politikası doğrulandı: `/data/releases` altında **tam 3 paket** kaldı; disk %100 → **%36**
- `.exe` simgesi gömülü doğrulandı (7 boyutlu `.ico`); web `logo.png`/`favicon.png`/`favicon.ico` → 200

**Bilinen flake:** Tam suit ilk koşuda `OrgPersonnelTests` bir kez SQLite "disk/prepare" hatası verdi (paralel
dosya kilidi). İzole koşuda ve sonraki tam koşularda geçti — mantık hatası değil, ortam kaynaklı.

## 2026-08-11 - FAZ C / STK-01 + STK-02 — Depo bazlı stok bakiyesi
- **Komut:** `dotnet build DepoWise.sln` · `dotnet test tests/DepoWise.Tests`
- **Exit code:** 0 / 0
- **Sonuç:** Build **0 hata**. Test **1223 toplam · 1190 geçti · 0 kaldı · 33 atlandı**
  (taban 1206'ydı; `StockLocationTests` ile **17 yeni senaryo**).
- **Ek prova 1 — izole PostgreSQL migration provası** (üretim yedeğinin yerel kopyası, canlıya bağlanılmadı):
  şema 62 → 64 · 667 hareket (666'sı lokasyonsuz) · bakiye 664 → **665** satır ·
  **uyuşmayan malzeme 0** · toplam **8952,3 → 8952,3** (korundu) · ATANMAMIŞ 8953,3 ·
  negatif 66 → 67 (defterin söylediği +1) · süre **173 ms**. Prova sonrası kopya veritabanı silindi.
- **Ek prova 2 — dolu SQLite v63 → v64 yükseltmesi:** 3 bakiye satırı → 5 lokasyon satırı ·
  toplam **8,3** korundu · ondalıklar tam (0.1 / 0.2 ayrı depolarda) · lokasyonsuz negatif ATANMAMIŞ'ta.
- **Ek prova 3 — doğrulama kapısı:** defterle uyuşmayan bakiye bırakıldı → migration **durdu**,
  şema **63'te kaldı**, bakiye **değişmedi** (transaction geri alındı).
- **Ek prova 4 — dönüştürülen sorgular PostgreSQL'de:** malzeme listesi **2459 satır = malzeme sayısı**
  (satır çoğaltma yok) · detay = liste = servis = lokasyon kırılımı toplamı (tutarlı) ·
  Stok Durumu 2459 · Şablon Dışı 2459 · dashboard 2459 malzeme / 2136 düşük stok.
- **Rapor:** `docs/tests/Stok_Lokasyon_Test_Report.md` · **Karar:** ADR-102

## 2026-08-11 - FAZ C / STK-03 — API lokasyon boyutu
- **Komut:** `dotnet build DepoWise.sln` · `dotnet test tests/DepoWise.Tests`
- **Exit code:** 0 / 0
- **Sonuç:** Build **0 hata**. Test **1240 toplam · 1207 geçti · 0 kaldı · 33 atlandı**
  (STK-02 tabanı 1223'tü; **17 yeni senaryo** — 15'i GERÇEK HTTP hattında, 2'si çevrimdışı yolda).
- **Yeni test dosyası:** `tests/DepoWise.Tests/ApiStockLocationTests.cs` (15/15) ·
  `StockLocationTests` 17 → 19 (masaüstü çevrimdışı + sync sonrası uyum senaryoları).
- **Bulgu (düzeltildi):** stok yazma yolları lokasyonun firmaya ait olduğunu doğrulamıyordu →
  `EnsureLocationOwned` (StockService `RunDocumentOnce` + OpeningStockService). Yabancı/bilinmeyen
  lokasyon artık **403** ve hiçbir kayıt oluşmuyor (senaryo 8/9/10/18).
- **Regresyon:** `StockOperationTests` uydurma şube kimliği ("b1"/"b2") kullanıyordu → GERÇEK şube
  oluşturacak şekilde güncellendi. **Üretim kuralı gevşetilmedi.**
- **Sync:** kod **değiştirilmedi**; çevrimdışı→snapshot→sunucu yeniden hesaplama senaryosu (19) ile
  lokasyon kırılımının iki tarafta aynı çıktığı kanıtlandı.
- **Rapor:** `docs/tests/Stok_Lokasyon_Test_Report.md` (EK bölümü) ·
  **Sözleşme:** `docs/project-control/STK_03_API_LOKASYON_PLANI.md`

## 2026-08-11 - FAZ C / STK-04 — Web lokasyon desteği
- **Komut:** `dotnet build DepoWise.sln` · `dotnet test tests/DepoWise.Tests`
- **Exit code:** 0 / 0
- **Sonuç:** Build **0 hata**. Test **1254 toplam · 1221 geçti · 0 kaldı · 33 atlandı**
  (STK-03 tabanı 1240'tı; **14 yeni senaryo** — `WebStockLocationContractTests`).
- **Düzeltilen 3 hata (Web):**
  1. Sayım POST'u `branchId` **hiç göndermiyordu** → fark ATANMAMIŞ'a yazılıyor, sayılan depo düzelmiyordu.
  2. Sayım ekranı "sistem stoğu" olarak **firma geneli toplamı** gösteriyordu → kullanıcı yanlış fark görürdü.
  3. Açılış stoğu **deposuz** gönderiliyordu → her açılış ATANMAMIŞ'a düşüyordu (canlıdaki 663 kaydın sebebi).
- **Gerçek veri kontrolü** (üretim yedeğinin izole kopyası, migration sonrası):
  DEPOWISE firması Tüm Şubeler **8951,3** · ATANMAMIŞ **8951,3** (663 satır) · gerçek depo **0** ·
  üç firma toplamı **8953,3**. Değer **değiştirilmedi**; 8953,3'ün üç firmanın toplamı olduğu netleşti.
  Bakiye 664 → 665, uyuşmayan 0, toplam korundu. Kopya veritabanı silindi, sunucu durduruldu.
- **Yeni uç:** `GET /api/stock/count-sheet` · `POST /api/materials` → `openingLocationId` (opsiyonel).
- **Plan/kayıt:** `docs/project-control/STK_04_WEB_LOKASYON_PLANI.md`

## 2026-08-11 - FAZ C / STK-05 — Masaüstü + çevrimdışı lokasyon
- **Komut:** `dotnet build DepoWise.sln` · `dotnet test tests/DepoWise.Tests`
- **Exit code:** 0 / 0
- **Sonuç:** Build **0 hata**. Test **1267 toplam · 1234 geçti · 0 kaldı · 33 atlandı**
  (STK-04 tabanı 1254'tü; **13 yeni senaryo** — `DesktopOfflineLocationTests`, HTTP kullanmaz).
- **Düzeltilen 4 hata (masaüstü):** sayım `branchId` göndermiyordu · sayımda sistem miktarı firma
  genelindendi · açılış stoğu deposuzdu · giriş/çıkış bakiye çipi firma geneliydi.
- **Çevrimdışı → senkron:** yerel hareketler sunucuya taşındığında lokasyon **korunuyor**; sunucunun
  defterden kurduğu kırılım masaüstüyle birebir aynı. Online→offline→online→offline→online döngüsünde
  **kopya hareket yok** (yerel ve sunucu hareket sayısı eşit).
- **Senkron sözleşmesi:** `stock_balances` push paketinde taşınıyor ama **otoriter değil** — kasten
  bozulmuş bakiye (999) senkron sonrası defterin değerine (10) düzeldi. Sync kodu **değiştirilmedi**.
- **Şirket izolasyonu:** başka firmanın deposu **çevrimdışı yolda da** reddediliyor (3 yazma yolunda).
- **Migration (tekrar doğrulama):** dolu SQLite v63→v64 → 3 bakiye satırı 5 lokasyon satırına, toplam
  **8,3** korundu, ondalıklar tam · doğrulama kapısı uyuşmayan bakiyede **durdu**, şema 63'te kaldı.
- **Plan/kayıt:** `docs/project-control/STK_05_DESKTOP_OFFLINE_PLANI.md`

## 2026-08-11 - FAZ C / STK-06 — Rapor lokasyon boyutu
- **Komut:** `dotnet build DepoWise.sln` · `dotnet test tests/DepoWise.Tests`
- **Exit code:** 0 / 0
- **Sonuç:** Build **0 hata**. Test **1281 toplam · 1248 geçti · 0 kaldı · 33 atlandı**
  (STK-05 tabanı 1267'ydi; **14 yeni senaryo** — `StockReportLocationTests`).
- **Stok Durumu:** filtre boşken eski sorgu **birebir** (regresyon yok); depo seçilince kırılım +
  "Depo / Şantiye" kolonu + **C# decimal** toplam satırı. Lokasyon toplamı = firma toplamı (test 3).
- **Stok Sayım:** **"Sayılan Depo" kolonu** eklendi; "Sistem" sütunu firma toplamı değil, sayılan deponun
  miktarı. Farklı depolarda yapılan sayımlar birbirine karışmıyor (test 11).
- **İzole PostgreSQL (üretim kopyası):** firma geneli 2459 satır / **8951,30** · ATANMAMIŞ 663 satır /
  **8951,3** · sayım raporu yeni kolonlarla çalışıyor. Kopya veritabanı silindi, sunucu durduruldu.
- **Ölçüm notu:** iki yol arasında **2×10⁻¹⁷** fark var — sebebi üretim verisindeki eski float artıkları
  (`0.31999999999999995` gibi). STK-06'nın getirdiği bir şey değil → `STK-11` olarak kaydedildi.
- **Çevrimdışı ↔ sunucu paritesi:** senkron sonrası sunucu raporu masaüstü raporuyla birebir aynı (test 14).
- **Plan/kayıt:** `docs/project-control/STK_06_UYGULAMA_PLANI.md`

## 2026-08-11 - FAZ C / STK-07 — Senkron sertifikasyonu
- **Komut:** `dotnet build DepoWise.sln` · `dotnet test tests/DepoWise.Tests`
- **Exit code:** 0 / 0
- **Başlangıç:** 1281 · 1248 geçti · 0 kaldı · 33 atlandı
- **Bitiş:** **1292 toplam · 1259 geçti · 0 kaldı · 33 atlandı** (**11 yeni senaryo**)
- **Yeni dosya:** `tests/DepoWise.Tests/SyncStockLocationCertificationTests.cs` — GERÇEK HTTP senkron
  uçları (`business-push` / `business-pull?since=` / `business-version`) + ayrı yerel SQLite (masaüstü).
- **Kanıtlananlar:** çevrimdışı giriş/çıkış/transfer/sayım senkronda lokasyonunu koruyor · transferin
  **iki bacağı** da taşınıyor (`branch_id`/`branch_from_id` birebir) · aynı paket 3 kez gönderildi,
  kopya hareket ve bakiye değişimi **yok** · offline→online döngüsünde yerel ve sunucu hareket sayısı
  **eşit** · yakınsama **hareket kimlikleri dahil** · şirket izolasyonu çevrimdışı da geçerli ·
  bakiye tablosunda **hayalet lokasyon satırı yok**.
- **Bakiyenin otoritesi DEFTER:** yerel bakiye kasten 999 yapıldı → senkron sonrası **10** (defter kazandı).
- **Delta pull:** güncel sürümden sonrası **boş paket**; eski kayıt tekrar inmiyor; sürüm ilerliyor.
- **Senkron kodu DEĞİŞTİRİLMEDİ.** Offline mimariye dokunulmadı.
- **Yeni bulgu `SNK-12`:** `branches` iş-senkronunda yok (web-otoriteli) → web'de açılan yeni depo
  masaüstüne org senkronu inmeden kullanılamıyor. Hata değil, görünürlük işi.
- **Kayıt:** `docs/project-control/STK_07_SENKRON_SERTIFIKASYONU.md`

## 2026-08-11 - SNK-12 — Masaüstünde depo listesi tazeleme
- **Komut:** `dotnet build DepoWise.sln` · `dotnet test tests/DepoWise.Tests`
- **Exit code:** 0 / 0
- **Başlangıç:** 1292 · 1259 geçti · 0 kaldı · 33 atlandı
- **Bitiş:** **1300 toplam · 1267 geçti · 0 kaldı · 33 atlandı** (**8 yeni senaryo** — `BranchMirrorTests`)
- **Kök neden:** `BranchMirror` yalnız girişte çağrılıyordu; oturum açıkken web'de açılan depo masaüstüne
  inmiyor, o depoya stok işlemi yapılamıyordu (`EnsureLocationOwned` reddeder).
- **Çözüm:** mevcut aynalama normal senkron turunda da çağrılıyor, **2 dakikalık kısıtlama** ile.
  Yeni protokol/tablo/uç **yok**; `stock_movements` senkronuna **dokunulmadı**.
- **Kanıtlananlar:** yeni depo aynalanmadan kullanılamıyor → aynalandıktan sonra **çevrimdışı** giriş/
  transfer/sayım çalışıyor · tekrarlanan aynalama **kopya üretmiyor** · isim/kod güncellemesi yansıyor ·
  sunucuda olmayan depo **pasife alınıyor, fiziksel silinmiyor** (geçmiş stok korunuyor), yeniden açılınca
  aktifleşiyor · **firma izolasyonu**: A'nın aynalaması B'nin depolarına dokunmuyor · sahiplik kontrolü
  bypass edilmiyor · çevrimdışıyken yerel liste korunuyor.

## 2026-08-11 - FAZ C / STK-08 — Atanmamış stok toplu dağıtımı
- **Komut:** `dotnet build DepoWise.sln` · `dotnet test tests/DepoWise.Tests`
- **Exit code:** 0 / 0
- **Başlangıç:** 1300 · 1267 geçti · 0 kaldı · 33 atlandı
- **Bitiş:** **1317 toplam · 1284 geçti · 0 kaldı · 33 atlandı** (**17 yeni senaryo** — `StockDistributeTests`)
- **Kanıtlananlar:** kaynak DAİMA ATANMAMIŞ (şubeye bağlı kullanıcıda sessizce kendi şubesine çevrilmiyor —
  eski hatanın nöbetçisi) · ATANMAMIŞ/yabancı/bilinmeyen/pasif depo hedef olamaz · sıfır/negatif/aşım
  reddediliyor · aynı malzeme iki satırdaysa TOPLAM kontrol ediliyor · kısmi ve tam dağıtım · farklı
  hedeflere bölme · çoklu malzeme tek belgede · **bir satır yetersizse tamamı rollback** · ondalık korunuyor ·
  **firma toplamı değişmiyor** · gerçek transfer hareketi (iki bacak) · audit izi · yetkisiz reddediliyor ·
  çevrimdışı dağıtım senkronda korunuyor ve **kopya üretmiyor** · liste kalan miktarı doğru gösteriyor.
- **Gerçek veri (izole üretim kopyası):** 663 atanmamış malzeme / 8951,3 birim → kısmi dağıtım ✅ ·
  aşım denemesi **reddedildi** ✅ · **rollback** doğrulandı (hedef bakiyesi değişmedi) ✅ · tam dağıtım ✅ ·
  **firma toplamı 9 → 9,0 KORUNDU** ✅. Prova için kopyaya geçici depo eklendi (canlıya DEĞİL), kopya silindi.
- **Bulgular:** B-1 transferler geri alınmaz (dağıtım da öyle; düzeltme = yeni transfer) ·
  B-2 DEPOWISE firmasında hiç depo yok (kullanıcı önce depo oluşturmalı).
- **Kayıt:** `docs/project-control/STK_08_UYGULAMA_PLANI.md`

## 2026-08-11 - SNK-11 — Türetilmiş bakiye senkron paketinden çıkarıldı
- **Komut:** `dotnet build DepoWise.sln` · `dotnet test tests/DepoWise.Tests`
- **Exit code:** 0 / 0
- **Başlangıç:** 1317 · 1284 geçti · 0 kaldı · 33 atlandı
- **Bitiş:** **1325 toplam · 1292 geçti · 0 kaldı · 33 atlandı** (**+7 yeni**; 3 mevcut test gerekçeli
  olarak yeniden yazıldı — bkz. `SNK_11_BAKIYE_SENKRON_YUKU.md` §4, gevşetme değil sözleşme değişikliği).
- **Değişiklik:** `BusinessSyncService.Tables`'tan `stock_balances` çıkarıldı + yetki eşlemesi kaldırıldı.
  Tablo KALDIRILMADI; yerel SQLite ve sunucu sorguları aynen duruyor.
- **İzole PostgreSQL (üretim kopyası):** paket **1807,1 KB** · 663 hareket taşınıyor ·
  **663 bakiye satırı taşınmıyor** · taşınmayan veri **~86 KB/tur**.
- **Kanıtlananlar:** kasten bozuk bakiye (999) sunucuya bulaşmıyor · yalnız bakiye değişirse paket
  taşımıyor ama **yerel okuma çalışıyor** · çevrimdışı giriş/çıkış/ters kayıt/transfer/sayım/STK-08
  dağıtımı/kırılım görüntüleme **çalışmaya devam ediyor** · offline→online döngüsünde kopya yok.
- **Kayıt:** `docs/project-control/SNK_11_BAKIYE_SENKRON_YUKU.md`

## 2026-08-11 - RPR-01 — Web ↔ masaüstü rapor filtre paritesi (koruma testi)
- **Komut:** `dotnet build DepoWise.sln` · `dotnet test tests/DepoWise.Tests`
- **Exit code:** 0 / 0
- **Başlangıç:** 1325 · 1292 geçti · 0 kaldı · 33 atlandı
- **Bitiş:** **1343 toplam · 1310 geçti · 0 kaldı · 33 atlandı** (**+18 yeni senaryo** —
  `ReportFilterParityTests` + `ReportFilterBehaviourParityTests`). Mevcut testlerin hiçbiri
  değiştirilmedi, silinmedi, gevşetilmedi.
- **Üretim davranışı DEĞİŞMEDİ.** Tek üretim dokunuşu: `Reports.razor` içinde yanlış bir yorum satırı
  düzeltildi (davranış açıklaması gerçeğe uydu). Yeni servis/uç/migration/senkron değişikliği YOK.
- **Envanter:** 12 rapor · 10 filtre bayrağı · bir filtre 6 dosyada bağlanıyor
  (`ReportCatalog` · `ReportModels` · `Api/Program.cs` · `Web/Reports.razor` ·
  `Desktop/ReportsViewModel.cs` · `Desktop/ReportsView.axaml`). Mevcut 10 bayrağın **tamamı**
  iki platformda tam bağlı çıktı → **gerçek parite eksiği bulunmadı**.
- **Kanıtlananlar:** her bayrağın 4 katmanda bağlı olduğu · kataloğa eklenip parite tablosuna
  girmeyen bayrağın yakalandığı · arayüzde katalogsuz "başıboş" bayrak olmadığı · sorgu ve export
  uçlarının **aynı** `ReportRequest`'i kurduğu · "📦 Atanmamış" seçeneğinin İKİ arayüzde de sunulduğu ·
  tarih varsayılanının (Bu Ay) iki arayüzde de uygulandığı · talep durumlarının tek kaynaktan geldiği.
- **Negatif ispat (üretim koduna sahte hata bırakmadan, kopya metinle):** 5 simüle hatanın **5'i**
  yakalandı — Web bloğu eksik · export gövdesi eksik · masaüstü XAML bloğu eksik ·
  `[NotifyPropertyChangedFor]` eksik · API katalog alanı eksik.
- **🔴 Negatif ispatın bulduğu gerçek zayıflık:** ilk yazdığım Web kontrolü `_sel?.UsesLocation == true`
  arıyordu; bu metin istek gövdelerinde de geçtiği için **ekran bloğu silinse bile test geçiyordu**.
  Token `@if (_sel?.UsesLocation ==` olarak sıkılaştırıldı.
- **Çevrimdışı:** masaüstü rapor filtreleri yerel SQLite üzerinde, **HTTP kullanılmadan** koşturuldu
  (`ApiTestHost` yok). Lokasyon listesi yerel `BranchService`'ten. API bağımlılığı eklenmedi.
- **STK-06 semantiği korundu:** Tüm Şubeler (20) ≠ tek depo (10) ≠ Atanmamış (6); kırılım toplamı =
  firma toplamı; Stok Sayım "Sayılan Depo" kolonu + filtre çalışıyor.
- **Yapılmayanlar (dürüst kayıt):** görsel browser/XAML render kontrolü **yapılmadı** (doğrulama
  kod/kaynak düzeyinde) · export çıktısı XLSX olarak açılıp karşılaştırılmadı (parite kaynak eşitliği
  + aynı gövde → aynı tablo yoluyla kanıtlandı) · PostgreSQL provası gerekmedi (SQL/lehçeye
  dokunulmadı; 33 atlanan PG testi tabanla aynı).
- **Kayıt:** `docs/project-control/RPR_01_FILTRE_PARITESI.md`

## 2026-08-11 - BKM-04 / KARAR-9 — Bakım malzemesinin çıktığı depo
- **Komut:** `dotnet build DepoWise.sln` · `dotnet test tests/DepoWise.Tests`
- **Exit code:** 0 / 0
- **Başlangıç:** 1343 · 1310 geçti · 0 kaldı · 33 atlandı
- **Bitiş:** **1387 toplam · 1353 geçti · 0 kaldı · 34 atlandı** (**+44 yeni senaryo**:
  `MaintenanceStockLocationTests` 27 · `ApiMaintenanceLocationTests` 9 · `MaintenanceLocationUiParityTests` 7 ·
  `PostgresMaintenanceLocationTests` 1). Mevcut testlerin hiçbiri değiştirilmedi/silinmedi/gevşetilmedi.
- **Regresyon kapısı:** mevcut `MaintenanceTests` + `MaintenanceTeamStockTests` + `ActivityMaintenanceEditTests` +
  `DailyActivity*` grubu (**115 test**) kod değişikliğinden SONRA ayrıca koşuldu → **115/115 geçti**.
- **Değişiklik:** `MaintenanceService` lokasyonu artık ÇAĞIRANDAN alıyor (eskiden sabit `Unassigned`);
  defter (`stock_movements.branch_id`) ve bakiye (`stock_balances.location_id`) AYNI depoyu kullanıyor.
- **İptal simetrisi:** `LoadMaintenanceMaterials` kaldırıldı, yerine `LoadUsageMovements` geldi →
  ters kayıt ORİJİNAL hareketin `branch_id`'sine yazılıyor, iptal anındaki oturumdan hesaplanmıyor.
  Ekip-stoğu satırları hiç hareket üretmediği için doğal olarak dışarıda kalıyor (yapısal).
  Ters kayda `reverses_movement_id` yazılıyor (geri izlenebilirlik; yeni kolon DEĞİL).
- **Kanıtlananlar (çevrimdışı, HTTP yok):** seçilen depodan düşüyor (defter+bakiye) · farklı depo
  seçilince oturum şubesi EZMİYOR · aracın şubesi lokasyonu belirlemiyor · lokasyon yoksa ATANMAMIŞ ·
  firma toplamı değişmiyor, yalnız kırılım taşınıyor · yabancı/pasif/bilinmeyen depo reddediliyor ve
  ROLLBACK oluyor · depo yoksa bakım engellenmiyor · negatif stok kuralı değişmedi ve eksik ATANMAMIŞ'a
  KAYMIYOR · **iptal orijinal depoya dönüyor (oturum şubesi değişse bile)** · çift iptal ikinci kez geri
  eklemiyor · ekip stoğu hiç düşmüyor · karışık satırlarda yalnız işaretsiz olan düşüyor · aynı
  operationId çift hareket üretmiyor · Günlük Faaliyet bakım + 3 ilave işlem türü aynı lokasyonu
  kullanıyor · Stok Durumu raporu seçilen depoda gösteriyor · **bakım raporundaki `op_branch_id`
  (Şube) ile stok lokasyonu karışmıyor** · içe aktarım oturum şubesini taşıyor ·
  **çevrimdışı→senkron→sunucu lokasyonu koruyor, aynı paket tekrarında kopya yok**.
- **Web gerçek HTTP hattı (9):** gönderilen depo uygulanıyor · `branchId` göndermeyen ESKİ istemci
  ATANMAMIŞ davranışını koruyor (kırılmıyor) · yabancı depo **403** · bilinmeyen depo **403** ·
  Günlük Faaliyet bakım + ilave işlem uçları lokasyonu uyguluyor · Günlük Faaliyet'te yabancı depo 403 ·
  iptal HTTP hattında da orijinal depoya dönüyor · ekip stoğu HTTP'de de düşmüyor.
- **Arayüz paritesi (kaynak taraması, 7):** varsayılan = oturum şubesi (iki arayüz) ·
  **"Atanmamış" yeni yazma hedefi olarak SUNULMUYOR** (Web `WriteTargets`, masaüstü listesinde yok) ·
  Web `_mLocationId` gönderiyor, `Auth.BranchId` ile EZMİYOR · masaüstü `MntLocation?.Id` gönderiyor ·
  aynı etiket ("Malzemenin çekildiği depo") dört arayüzde de bağlı · depo yoksa dört arayüz de uyarıyor ·
  "Tüm Şubeler" kapısı korunuyor.
- **İzole PostgreSQL (boş yerel test DB, port 5433):** `PostgresMaintenanceLocationTests` koştu.
  Gerçek satırlar: `usage` → Depo B, `usage_reverse` → **AYNI** Depo B + `reverses_movement_id` dolu.
  Test veritabanı sonra **silindi**, PG sunucusu durduruldu. Canlıya HİÇ bağlanılmadı.
- **Migration:** GEREKMEDİ — `stock_movements.branch_id` ve `stock_balances.location_id` zaten vardı.
  Yeni tablo/kolon/indeks/senkron protokolü açılmadı; SNK-11 geri alınmadı.
- **Yan düzeltme:** eksik-stok uyarısı iki arayüzde de FİRMA GENELİNE bakıyordu → artık SEÇİLEN
  deponun stoğuna bakıyor (STK-04/05'te düzeltilen hata sınıfının bakımdaki ikizi).
- **Yapılmayan (dürüst kayıt):** **görsel tarayıcı render kontrolü YAPILMADI**. Yerel API + yerel Web
  ayağa kaldırıldı ama yerel sunucu veritabanında zaten kullanıcılar olduğu için tohum parolası
  üretilmedi ve giriş yapılamadı; veritabanını sıfırlamak kullanıcının yerel verisine dokunmak,
  canlıdan bakmak ise canlıya yazma riski olurdu. Ayrıntı: `BKM_04_LOKASYON_ANALIZI.md` §9.
- **Kayıt:** `docs/DECISIONS.md` → ADR-103 · `docs/project-control/BKM_04_LOKASYON_ANALIZI.md`

## 2026-08-11 - STK-B1 — Stok hareket türü kataloğu / gösterim paritesi (STK-10 adım 0)
- **Komut:** `dotnet build DepoWise.sln` · `dotnet test tests/DepoWise.Tests`
- **Exit code:** 0 / 0
- **Başlangıç:** 1387 · 1353 geçti · 0 kaldı · 34 atlandı
- **Bitiş:** **1411 toplam · 1377 geçti · 0 kaldı · 34 atlandı** (**+24 yeni senaryo** —
  `MovementTypeCatalogTests` 10 + `MovementTypeRealPathTests` 7 + 7 Theory örneği).
  Mevcut testlerin hiçbiri silinmedi/gevşetilmedi/atlanmadı.
- **Envanter (koddan doğrulandı):** üretimde **8** `movement_type` değeri var —
  `opening` (OpeningStockService) · `in`/`out`/`transfer`/`adjustment` (StockService.ApplyLine) ·
  `reverse` (StockService.ReverseDocument) · `usage`/`usage_reverse` (MaintenanceService).
  `count` bir **`doc_type`**'tır, movement_type DEĞİL — kataloğa alınmadı, web'deki ölü dal kaldırıldı.
- **Bulunan kusur (planda 2 harita yazıyordu, gerçekte ÜÇ):**
  `StockMovementRow.TypeText` (masaüstü) · `StockService.RecentForMaterial` (malzeme kartı, İKİ platform) ·
  `Web/StockMovements.razor`. `adjustment` → "Düzeltme" / "Sayım Düzeltme" / "Sayım Düzeltme";
  `reverse` → HAM / "İptal (ters)" / "İptal"; `usage` ve `usage_reverse` → **üçünde de HAM İngilizce**.
- **Nihai etiketler (Web = Masaüstü):** Açılış · Giriş · Çıkış · Transfer · **Sayım Düzeltme** ·
  **Bakım Tüketimi** · **Bakım Tüketimi İptali** · **İptal (Ters Kayıt)**.
  Terminoloji projeden alındı (`AuditLogService: "reverse" => "Ters Kayıt"`), uydurulmadı.
- **Paylaşım yöntemi:** Web, Application'a proje referansı VERMEZ. Projenin mevcut deseni kullanıldı
  (`ListColumns`, `RequestOperationStatus`): **tek dosya, iki projede derlenir**
  (`<Compile Include="..\DepoWise.Application\Ui\MovementTypeOptions.cs">`). Ayna dosya YOK.
- **Kanıtlananlar:** katalog 8/8 kapsıyor · her türün dolu, anahtardan farklı Türkçe etiketi var ·
  8 türün 8'i **gerçek servislerle üretildi** ve defterde tam 8 tür çıktı · hareket listesinde ve
  malzeme kartında **hiçbir satır** ham İngilizce göstermiyor · `adjustment` ve `reverse` iki yüzeyde
  AYNI · `usage` ≠ `usage_reverse` ≠ `reverse` ≠ `adjustment` · `count` katalogda yok ·
  bilinmeyen değer sessizce gizlenmiyor · üç yüzey de kendi switch'ini taşımıyor ·
  web'de ayrı kopya yok.
- **Gelecek koruması:** kaynak taraması üretimdeki hareket türü literallerini çıkarıp katalogla
  karşılaştırır → yeni bir tür eklenip kataloğa girmezse test KIRILIR ve değeri söyler. Ayrıca
  `stock_movements`'a yazan ifade sayısı (3) kilitlendi → dördüncü bir yazma yolu da testi kırar.
- **🔴 Yan bulgu — KENDİ testimi düzelttim:** `MaintenanceStockLocationTests`'teki 4 iptal testi ters
  kaydı **sıra indeksiyle** (`[1]`) seçiyordu. Test saati dondurulmuş olduğu için orijinal hareket ile
  ters kaydın `created_at`'i AYNI oluyor, `ORDER BY created_at, id` **rastgele GUID'e** düşüyordu →
  testler **flaky**'ydi (3 koşuda 1 kırılma; BKM-04'te şans eseri geçmişler). Tür üzerinden seçime
  çevrildi → **5 ardışık koşuda 27/27** kararlı. **Üretim etkilenmedi** (iptal her hareketi kendi
  deposuna geri yazar, sıradan bağımsızdır — ayrı bir test bunu zaten kanıtlıyor).
- **Şube kapsamı notu (STK-10'a devredildi):** `SearchMovements`, `BranchScope.Sql(s, "sm.branch_id")`
  uygular → Depo A oturumu transferin yalnız KAYNAK bacağını görür. Mevcut ve doğru davranış,
  değiştirilmedi; STK-10'un lokasyon filtresi tasarlanırken hesaba katılmalı. Testle belgelendi.
- **Dokunulmayanlar:** migration YOK · `stock_movements` şeması ve VERİSİ değişmedi · senkron protokolü
  değişmedi · hareket üretim iş mantığı değişmedi · STK-10'un rapor/filtre/export kısmı YAPILMADI.
- **Yapılmayan (dürüst kayıt):** **görsel tarayıcı/XAML render kontrolü YAPILMADI** — BKM-04'teki aynı
  engel (yerel API veritabanında hesap yok; `launch.json` env değişkeni desteklemiyor; canlıya bağlanmak
  ve parola girmek yasak; CLI test-kullanıcı mekanizması yok). Statik risk: masaüstünde tür kolonları
  `Auto` genişlikli ve `TextTrimming` yok → uzun etiket kırpılmaz ama kolonu genişletir.
- **Kayıt:** `docs/project-control/STK_10_HAREKET_RAPORU_PLANI.md` §12

## 2026-08-11 - STK-10a — Stok Hareketleri raporu (katalog + Date/Location + gerçek XLSX)
- **Komut:** `dotnet build DepoWise.sln` · `dotnet test tests/DepoWise.Tests`
- **Exit code:** 0 / 0
- **Başlangıç:** 1411 · 1377 geçti · 0 kaldı · 34 atlandı
- **Bitiş:** **1452 toplam · 1417 geçti · 0 kaldı · 35 atlandı** (**+41 yeni senaryo**:
  `StockMovementsReportTests` 29 · `ApiStockMovementsReportTests` 11 · `PostgresStockMovementsReportTests` 1).
  (Atlanan 34→35: yeni PG testi DEPOWISE_PG_URL yokken atlanır.)
- **Kapsam:** YALNIZ `Date` + `Location`. `Search`/`Material`/`MovementType` **eklenmedi** (STK-10b).
- **🔴 Web ve masaüstünde HİÇ KOD DEĞİŞMEDİ:** rapor katalog-güdümlü olduğu için iki platformun
  Raporlar ekranında kendiliğinden göründü; Date+Location filtreleri STK-06'dan 6 katmanda bağlıydı.
  **RPR-01 koruma testi hiç değiştirilmeden yeşil kaldı.**
- **Kanıtlananlar (çevrimdışı/SQLite, 29):** katalogda ve `Run` tanıyor · yalnız Date+Location açık ·
  Kaynak/Hedef ayrı kolonlar · `direction>0` → hedef, `direction<0` → kaynak · **transfer İKİ satır**
  (giriş bacağı `Depo A → Depo B`) · Atanmamış etiketi · tarih filtresi · Tüm Şubeler · Depo A → iki
  bacak · Depo B → giriş bacağı · ilgisiz depo → boş · Atanmamış filtresi · çoklu lokasyon birleşimi ·
  **BranchScope × Location** (Depo A oturumu + A → yalnız kapsam içi; **Depo A + Depo B → BOŞ**) ·
  kapsam filtresiz de uygulanıyor · 8 hareket türü doğru Türkçe (STK-B1 tek kaynağından) ·
  bakım tüketimi seçilen depoda · ters bakım kaydı orijinal depoda · **tavan SQL'de** + sıralama korunuyor ·
  sıralama (en yeni üstte) · boş sonuç · firma izolasyonu · **çevrimdışı rapor + export** · Web/masaüstü
  aynı katalogdan aynı sonuç.
- **Gerçek HTTP (11):** katalog ucu · rapor ucu (Kaynak/Hedef) · lokasyon filtresi · **6 kombinasyonda
  rapor ucu ↔ export ucu XLSX satır-satır** · export yetkisiz → **403** · kimliksiz istek reddediliyor.
- **🔴 GERÇEK XLSX (RPR-01'in açık boşluğu KAPANDI):** ClosedXML ile XLSX **açılıp okunuyor** ve
  **hücre hücre** rapor sonucuyla karşılaştırılıyor — 6 kombinasyon × 2 hat (servis + HTTP):
  filtresiz · Depo A · Depo B · Atanmamış · iki depo · dar tarih. Ayrıca: XLSX'te Kaynak/Hedef ve
  "Atanmamış" doğru · ekran ve export **aynı tavana** tabi · boş sonuçta da XLSX üretiliyor.
- **İzole PostgreSQL + SORGU PLANI:** rapor çalıştı (23 satır) · lokasyon filtresi doğru · LIMIT etkili.
  Gerçek plan: `Limit → Sort (created_at DESC, id DESC) → Index Scan using ix_stock_movements_material
  (Index Cond: material_id, created_at >= / <=) Filter: (company_id) AND (branch_id OR branch_from_id)`.
  ➡️ **filtre + sıralama + LIMIT SQL'de**; tarih filtresi MEVCUT indeksi kullanıyor →
  **YENİ İNDEKS EKLENMEDİ** (plan kuralı: yalnız ölçüm gerekçelendirirse). Test DB silindi, PG durduruldu.
- **Performans düzeltmesi (D-2 uygulandı):** `Dispatch`'e `maxRows` geçirildi; `stock-movements` sorgusu
  kendi `LIMIT`'ini uyguluyor. `Run`'ın bellekteki `Take` kesmesi ikinci emniyet ağı olarak duruyor.
  Diğer raporların davranışı DEĞİŞMEDİ.
- **⚠️ 2 mevcut test gerekçeli güncellendi (gevşetme DEĞİL):** katalog sayısı 12→13
  (`ReportArchitectureTests`) · lokasyonlu rapor listesi 2→3 (`StockReportLocationTests`). İkisi de
  TAM EŞLEŞME ile sınanmaya devam ediyor; ikinciye **yeni bir nöbetçi** eklendi (STK-10b'nin 1024
  bayrağı hiçbir raporda açık olmamalı). Ayrıntı: plan §16.1.
- **Dokunulmayanlar:** migration YOK · şema YOK · senkron protokolü YOK · mevcut Stok Hareketleri
  ekranlarının davranışı DEĞİŞMEDİ (STK-10b'de bağlanacak) · STK-11 (float artığı) çözülmedi.
- **Yapılmayan (dürüst kayıt):** **görsel tarayıcı/XAML render kontrolü YAPILMADI** — yerel API
  veritabanında hesap yok, `launch.json` env desteklemiyor, canlıya bağlanmak ve parola girmek yasak.
  XLSX'in İÇERİĞİ hücre hücre doğrulandı; doğrulanmayan yalnız görsel sunum.
- **Kayıt:** `docs/project-control/STK_10_HAREKET_RAPORU_PLANI.md` §16

## 2026-08-11 - STK-10b-1 — Stok Hareketleri raporu: Hareket Türü filtresi
- **Komut:** `dotnet build DepoWise.sln` · `dotnet test tests/DepoWise.Tests`
- **Exit code:** 0 / 0
- **Başlangıç:** 1452 · 1417 geçti · 0 kaldı · 35 atlandı
- **Bitiş:** **1480 toplam · 1445 geçti · 0 kaldı · 35 atlandı** (**+28 yeni senaryo**:
  `StockMovementsTypeFilterTests` 23 · `ApiStockMovementsReportTests` +5 (3'ü Theory)).
- **Kapsam:** YALNIZ `MovementType`. `Search` (10b-2), `Material` (10b-3), ekran bağlantıları +
  B-1 (10b-4) bu artımda YOK.
- **6/6 KABLOLAMA:** katalog/descriptor (`ReportFilters.MovementType = 1024` + `UsesMovementType`) ·
  istek modeli (`ReportRequest.MovementTypes`, SONA) · API sorgu · API export · Web (`@if` bloğu +
  `CatItem` + `Bool` + iki gövde + etiket) · Masaüstü (`ShowMovementType` + Notify + koleksiyon +
  loader + `BuildTable` + XAML). **+ RPR-01 `Map`'e satır** → koruma testi bayrağı kendi denetliyor.
- **RPR-01: 14/14 YEŞİL** — gevşetilmedi, istisna eklenmedi, tarama kuralı değiştirilmedi.
- **🔴 Uygulama sırasında yakalanan KENDİ HATAM:** `MovementTypes` önce `LocationIds`'ten ÖNCE
  eklenmişti. Bu kayıt API uçlarında POZİSYONEL kuruluyor → `LocationIds` argümanı sessizce
  `MovementTypes`'a kayardı ve lokasyon filtresi çalışmayı bırakırdı. Derlemeden önce görüldü, alan
  SONA taşındı, yanına kalıcı uyarı yorumu eklendi.
- **Tek kaynak (STK-B1) korundu:** seçenekler yalnız `MovementTypeOptions.All`'dan. Web bu dosyayı
  zaten derliyor (paylaşılan dosya) → **`/api/reports/scope`'a yeni alan EKLENMEDİ**, ikinci
  harita/kopya oluşmadı. Kaynak taramalı testle kilitli.
- **Kanıtlananlar (çevrimdışı, 23):** 8 türün 8'i tek tek filtrelenebiliyor (Theory) · seçilmeyen tür
  gelmiyor · çoklu seçim birleşim · **bilinmeyen anahtar fail-closed** (veri sızmıyor) · filtre
  KANONİK anahtarla çalışıyor (etiket gönderilirse eşleşmiyor) · boş liste = filtre yok · etiketler
  katalogdan · Web/masaüstü aynı seçenek kaynağı (kaynak taraması) · **tür+lokasyon** · **tür+tarih** ·
  **tür+BranchScope yetki aşmıyor** · tür+lokasyon+BranchScope üçlüsü · **tavan filtrelenmiş küme
  üzerine iniyor** (SQL'de) · export'a uygulanıyor · çevrimdışı rapor+export.
- **Gerçek HTTP (+5):** katalog ucu `usesMovementType` yayınlıyor (ve diğer raporlarda kapalı) ·
  filtre HTTP'de uygulanıyor · fail-closed · **3 türde export XLSX ekranla hücre hücre aynı**.
- **Gerçek XLSX:** 6 kombinasyon (servis: filtresiz · tek tür · çoklu tür · tür+lokasyon · tür+tarih ·
  boş sonuç) + 3 kombinasyon (HTTP). Filtresiz XLSX > filtreli XLSX satır sayısı (filtre gerçekten
  export'a iniyor). **"MovementType + Search" kombinasyonu ÜRETİLMEDİ** — Search 10b-2 kapsamında.
- **İzole PostgreSQL:** tür filtresi doğru (20 `in` / 2 `transfer` bacağı) · fail-closed ·
  tür+lokasyon · **sorgu planı**: `Filter: (company_id) AND (movement_type='in') AND ((branch_id=…)
  OR (branch_from_id=…))` + `Limit`/`Sort` → filtre SQL'e indi. **Yeni indeks EKLENMEDİ.**
  Test DB (`stk10b1_test`) silindi, PG durduruldu.
  ⚠️ Not: ilk denemede DB adı `stk10b1` idi → `PostgresTestGuard` "adında 'test' geçmiyor" diyerek
  yıkıcı testi ENGELLEDİ. Koruma doğru çalıştı; ad düzeltilerek koşuldu.
- **⚠️ 2 mevcut test güncellendi (gevşetme DEĞİL):** ikisi de STK-10a'da eklediğim kapsam
  nöbetçileriydi ve yeni filtreyi doğru yakaladılar. `Rapor_Katalogda_…` filtre kümesi tam eşitlikle
  sınanmaya devam ediyor; `Lokasyon_Filtresi_…` yerine "tür filtresi YALNIZ stock-movements'ta açık"
  tam-eşleşmesi kondu. **İkisine de Search (2048) / Material (4096) hâlâ kapalı nöbetçisi eklendi.**
- **Dokunulmayanlar:** migration YOK · şema YOK · senkron YOK · `MovementTypeOptions` 8 tür aynen ·
  BKM-04 lokasyon semantiği aynen · STK-10a SQL LIMIT düzeltmesi aynen · hareket ekranları DEĞİŞMEDİ.
- **Yapılmayan (dürüst kayıt):** **görsel render kontrolü YAPILMADI** — yerel API veritabanında hesap
  yok, `launch.json` env desteklemiyor, canlıya bağlanmak/parola girmek yasak.
- **Kayıt:** `docs/project-control/STK_10_HAREKET_RAPORU_PLANI.md` §19

## 2026-08-11 - STK-10b-2 — Stok Hareketleri raporu: Serbest metin arama
- **Komut:** `dotnet build DepoWise.sln` · `dotnet test tests/DepoWise.Tests`
- **Exit code:** 0 / 0
- **Başlangıç:** 1480 · 1445 geçti · 0 kaldı · 35 atlandı
- **Bitiş:** **1521 toplam · 1486 geçti · 0 kaldı · 35 atlandı** (**+41 yeni senaryo**:
  `StockMovementsSearchFilterTests` 36 · `ApiStockMovementsReportTests` +5).
- **Kapsam:** YALNIZ `Search`. `Material` (10b-3), ekran bağlantıları + B-1 (10b-4) bu artımda YOK.
- **6/6 KABLOLAMA:** katalog (`ReportFilters.Search = 2048` + `UsesSearch`) · istek modeli
  (`ReportRequest.SearchText`, **skaler `string?`**, SONA) · API sorgu · API export · Web (`@if` +
  `MudTextField` + `CatItem` + `Bool` + iki gövde) · Masaüstü (`ShowSearch` + Notify + `SearchText` +
  `BuildTable` + XAML). **+ RPR-01 `Map` satırı** (`RequestProps = ["SearchText"]`) → **14/14 yeşil**.
- **Etiket parçası bilinçli uzun** (`"Ara (kod, malzeme, not, belge)"`): kısa "Ara" yazılsaydı RPR-01'in
  etiket kontrolü mevcut "Araç"/"Araç ara" metinlerine takılıp blok silinse bile geçerdi.
- **🔴 BULGU — belge notu aramada YOK (mevcut davranış, DEĞİŞTİRİLMEDİ):** `ApplyLine` hareket satırının
  `note`'unu NULL yazıyor; kullanıcının belge notu `stock_documents.note`'a gidiyor, arama ise
  `sm.note`'a bakıyor. "Not" araması bugün yalnız TERS KAYIT GEREKÇESİ ve BAKIM TÜKETİMİ kayıtlarını
  buluyor. Semantik birebir taşındı; mevcut davranış testle kilitlendi
  (`Belge_Notu_Aramada_YOK_Mevcut_Davranis` — rapor ve mevcut ekran AYNI sonucu veriyor → kayma yok).
  ⛔ Arama `d.note`'u da kapsasın mı? → davranış değişikliği, ayrı iş **STK-B2**, kullanıcı kararı.
- **Kanıtlananlar (çevrimdışı, 36):** kod · malzeme adı · **hareket notu** · fatura no · belge no ile
  arama · beş alan aynı OR grubunda · eşleşme yok → boş · null/boş/boşluk/tab → filtre yok ·
  Trim uygulanıyor · kısmi eşleşme · **büyük-küçük harf davranışı mevcut ekranla AYNI** (5 varyant) ·
  **rapor ve mevcut ekran aynı kümeyi döndürüyor** (5 varyant) · Search+Date · Search+Location ·
  Search+MovementType · üçlü kombinasyon · **BranchScope aşılmıyor** · yetkisiz depo → boş ·
  **firma izolasyonu** · **tavan filtrelenmiş küme üzerine iniyor** · export'a uygulanıyor ·
  çevrimdışı rapor+export · **önceki filtreler bozulmadı** (regresyon nöbetçisi).
- **Gerçek HTTP (+5):** katalog ucu `usesSearch` yayınlıyor (diğer raporlarda kapalı) · arama
  SUNUCUDA uygulanıyor · eşleşmeyen → boş · yalnız boşluk → filtre yok · **3 aramada export XLSX
  ekranla hücre hücre aynı**.
- **Gerçek XLSX:** 7 kombinasyon (servis: filtresiz · kod · hareket notu · fatura no · arama+lokasyon ·
  arama+tür · boş sonuç) + 3 (HTTP). Filtresiz XLSX > aramalı XLSX satır sayısı.
- **İzole PostgreSQL:** arama doğru · boşluk = filtre yok · arama+tür+lokasyon üçlüsü ·
  **sorgu planı**: `Filter: (m.code ~~ '%PG%') OR (m.name ~~ …) OR (sm.note ~~ …) OR (d.invoice_no ~~ …)
  OR (d.doc_no ~~ …)` + `Limit`/`Sort` → arama SQL'e indi. **YENİ İNDEKS EKLENMEDİ** — `LIKE '%…%'`
  baştan joker içerdiği için B-tree kullanılamaz (trigram gerekirdi; hacim gerektirmiyor).
  Test DB (`stk10b2_test`) silindi, PG durduruldu.
- **⚠️ 2 mevcut test güncellendi (gevşetme DEĞİL):** STK-10a/10b-1'de eklediğim kapsam nöbetçileri
  `Search`'ü de doğru yakaladı. Filtre kümesi tam eşitlikle sınanmaya devam ediyor; "arama filtresi
  YALNIZ stock-movements'ta açık" tam-eşleşmesi eklendi; **Material (4096) hâlâ kapalı** nöbetçisi kaldı.
- **Testler yazılırken 7 senaryo kırıldı** → nedeni koddan doğrulandı (belge notu bulgusu, yukarıda);
  üretim değiştirilmedi, **testler gerçeğe uyduruldu**.
- **Dokunulmayanlar:** migration YOK · şema YOK · senkron YOK · Date/Location/MovementType davranışı
  aynen · STK-10a SQL LIMIT düzeltmesi aynen · hareket ekranları DEĞİŞMEDİ.
- **Yapılmayan:** **görsel render kontrolü YAPILMADI** — aynı engel.
- **Kayıt:** `docs/project-control/STK_10_HAREKET_RAPORU_PLANI.md` §21

## 2026-08-12 - STK-10b-3 — Stok Hareketleri raporu: Malzeme filtresi + autocomplete
- **Komut:** `dotnet build DepoWise.sln` · `dotnet test tests/DepoWise.Tests`
- **Exit code:** 0 / 0
- **Başlangıç:** 1521 · 1486 geçti · 0 kaldı · 35 atlandı
- **Bitiş:** **1553 toplam · 1518 geçti · 0 kaldı · 35 atlandı** (**+32 yeni senaryo**:
  `StockMovementsMaterialFilterTests` 25 · `ApiStockMovementsReportTests` +7).
- **Kapsam:** YALNIZ `Material`. Ekran bağlantıları + B-1 (10b-4) ve **STK-B2** bu artımda YOK —
  `Search` semantiğine (kod · ad · `sm.note` · fatura no · belge no) **dokunulmadı**.
- **6/6 KABLOLAMA:** katalog (`ReportFilters.Material = 4096` + `UsesMaterial`) · istek modeli
  (`ReportRequest.MaterialIds`, **liste**, kaydın **SON** alanı) · API sorgu · API export ·
  Web (`@if` + `MudAutocomplete` + `CatItem` + `Bool(e,"usesMaterial")` + iki gövde) ·
  Masaüstü (`ShowMaterial` + Notify + `MaterialSearch`/`MaterialResults`/`PickedMaterial` +
  `BuildTable` + XAML). **+ RPR-01 `Map` satırı** (`RequestProps = ["MaterialIds"]`) → **14/14 yeşil**.
- **⚡ 2461 malzeme İNDİRİLMİYOR:** iki platform da MEVCUT arama desenini kullanıyor
  (Web `/api/materials?search=` + autocomplete · masaüstü yerel `Materials.List(term)`, ilk 30).
  Yeni uç açılmadı; `/api/reports/scope` **büyümedi** — kaynak taramasıyla kilitlendi
  (`Rapor_Kapsamina_Malzeme_Listesi_Eklenmedi`).
- **🔴 Pozisyonel argüman kayması:** kodlamadan ÖNCE tüm `new ReportRequest(` çağrıları tarandı
  (pozisyonel olan yalnız 2 API ucu). Alan SONA eklendi ve kural artık `MaterialIds_Kaydin_SON_Alani`
  testiyle kalıcı korunuyor (son 4 alanın sırası sabit).
- **Kanıtlananlar (çevrimdışı, 25):** doğru hareketler · yanlış kimlik → boş (fail-closed) ·
  boş/boşluk elemanlar atılıyor → filtresiz davranış · çoklu kimlik · **başka firmanın malzemesi
  erişilemez (AYNI kod+ad ile)** · **BranchScope aşılmıyor** · kapsam dışı deponun malzemesi görünmüyor ·
  Material+Date · +Location · +MovementType · +Search · üçlü ve **beşli** kombinasyon ·
  sonuçsuz filtre temiz boş · **tavan filtrelenmiş küme üzerine iniyor** · export'a uygulanıyor ·
  çevrimdışı arama+rapor+export · UI kaynak taraması (scope büyümedi, iki platform aynı kimlik alanı,
  masaüstünde `HttpClient` yok) · **önceki filtreler bozulmadı** · **Search semantiği değişmedi**.
- **Gerçek HTTP (+7):** katalog ucu `usesMaterial` yayınlıyor (diğer raporlarda kapalı) · filtre
  sunucuda · fail-closed · malzeme+lokasyon/tür/arama kombinasyonları · **3 senaryoda export XLSX
  ekranla hücre hücre aynı** · açık şube seçimi malzemeyle genişlemiyor.
- **Gerçek XLSX:** **10 kombinasyon** (servis: filtresiz · yalnız Material · +Date · +Location ·
  +MovementType · +Search · +Location+Tür · +Search+Tür · +Date+Location · sonuçsuz) + 3 (HTTP).
  Satır sayısı karşılaştırması TEK BAŞINA yeterli sayılmadı: filtreli XLSX'in her satırı beklenen
  malzemeye ait ve filtresiz XLSX'te birebir bulunuyor.
- **İzole PostgreSQL:** malzeme filtresi doğru · fail-closed · 4'lü kombinasyon · LIMIT filtre
  SONRASINDA · **sorgu planı**: `Index Scan Backward using ix_stock_movements_material` +
  `Index Cond: (material_id = …)` → filtre SQL'e indi ve **zaten var olan indeksi** kullanıyor.
  **YENİ İNDEKS EKLENMEDİ.** Test DB (`stk10b3_test`) silindi, PG durduruldu.
- **⚠️ 2 mevcut test güncellendi (gevşetme DEĞİL):** aynı kapsam nöbetçileri `Material`'ı doğru
  yakaladı. Filtre kümesi **tam eşitlikle** sınanmaya devam ediyor; "malzeme filtresi YALNIZ
  stock-movements'ta açık" tam-eşleşmesi eklendi; nöbetçi **bir sonraki bite (8192) kaydırıldı**.
- **🔴 BULGU (kapsam dışı → yeni iş `RPR-02`):** HTTP hattında oturumun `OperatingBranchId`'si HİÇ
  kurulmuyor (JWT yalnız kullanıcı+firma; `AuthService.CreateSessionForUser` şube atamıyor; tek
  istisna içe-aktarma ucu). Web'de rapor şube daralması yalnız açık `branchIds` ile oluyor.
  Tüm raporları etkileyen MEVCUT mimari; masaüstü (çevrimdışı) etkilenmiyor. **Düzeltilmedi.**
- **Dokunulmayanlar:** migration YOK · şema YOK · senkron YOK · Date/Location/MovementType/Search
  davranışı aynen · STK-10a SQL LIMIT düzeltmesi aynen · hareket ekranları DEĞİŞMEDİ ·
  production'a bağlanılmadı · canlı veriye yazılmadı · Migration064 çalıştırılmadı · master'a merge yok.
- **Yapılmayan:** **görsel render kontrolü YAPILMADI** — Raporlar ekranı giriş formunun arkasında;
  parolayı bir alana yazmam (güvenlik kuralı) ve canlıya bağlanmam (talimat §13).
- **Kayıt:** `docs/project-control/STK_10_HAREKET_RAPORU_PLANI.md` §23
